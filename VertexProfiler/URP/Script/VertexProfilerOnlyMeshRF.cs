using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProfilingScope = UnityEngine.Rendering.ProfilingScope;

namespace VertexProfilerTool
{
    /// <summary>
    /// OnlyMesh 对于替换渲染的实现方案，可以用多材质的方式选择性渲染来实现
    /// </summary>
    public class VertexProfilerOnlyMeshRF : ScriptableRendererFeature
    {
        VertexProfilerModeOnlyMeshRenderPass m_ScriptablePass;
        VertexProfilerOnlyMeshLogRenderPass m_LogPass;
        VertexProfilerPostEffectRenderPass m_PostEffectPass;
        
        public override void Create()
        {
            m_ScriptablePass = new VertexProfilerModeOnlyMeshRenderPass();
            m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            
            m_LogPass = new VertexProfilerOnlyMeshLogRenderPass();
            m_LogPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            
            m_PostEffectPass = new VertexProfilerPostEffectRenderPass();
            m_PostEffectPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            
            m_ScriptablePass.Setup();
            renderer.EnqueuePass(m_ScriptablePass);
            m_LogPass.Setup();
            renderer.EnqueuePass(m_LogPass);
            renderer.EnqueuePass(m_PostEffectPass);
        }
        
        private void OnDisable()
        {
            m_ScriptablePass?.OnDisable();
            m_LogPass?.OnDisable();
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_ScriptablePass?.OnDisable();
                m_LogPass?.OnDisable();
            }
        }
    }
    [System.Serializable]
    public class VertexProfilerModeOnlyMeshRenderPass : VertexProfilerModeBaseRenderPass
    {
        private Material MeshPixelCalMat;
        private VertexProfilerJobs.J_Culling Job_Culling;

        private RTHandle m_RendererIdAndVertexCountRT;
        private RTHandle m_RendererIdAndVertexDepthCountRT;
        // 原生渲染所需属性
        List<ShaderTagId> URPMeshPixelCalShaderTagId;
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_renderStateBlock;
        ProfilingSampler m_ProfilingSampler;
        
        public VertexProfilerModeOnlyMeshRenderPass() : base()
        {
            EDisplayType = DisplayType.OnlyMesh;

            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, -1);
            m_renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_ProfilingSampler = new ProfilingSampler("PixelCalShader");
        }

        public void Setup()
        {
            // 不能在构造函数初始化的部分在这创建
            URPMeshPixelCalShaderTagId = new List<ShaderTagId>() {new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly")};
            if (vp != null)
            {
                vp.ProfilerMode = this;
                EProfilerType = vp.EProfilerType;
                CheckColorRangeData(true);
                MeshPixelCalMat = vp.MeshPixelCalMat;
            };
        }

        public override void OnDisable()
        {
            base.OnDisable();
            ReleaseRTHandle(ref m_RendererIdAndVertexCountRT);
            ReleaseRTHandle(ref m_RendererIdAndVertexDepthCountRT);
            if (vp != null && vp.ProfilerMode == this)
            {
                vp.ProfilerMode = null;
            }

            ReleaseAllComputeBuffer();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            EDisplayType = DisplayType.OnlyMesh;
        }

        public override bool CheckProfilerEnabled()
        {
            return vp != null 
                   && vp.EnableProfiler
                   && MeshPixelCalMat != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
            // 简单模式默认使用硬编码的阈值，不做处理
            if (EProfilerType == ProfilerType.Simple) return;
            
            VertexProfilerUtil.OnlyMeshDensitySetting = new List<int>(VertexProfilerUtil.DefaultOnlyMeshDensitySetting);
            DensityList.Clear();
            NeedSyncColorRangeSetting = true;
            CheckColorRangeData();
        }
        public override void CheckColorRangeData(bool forceReload = false)
        {
            if (DensityList.Count <= 0 || forceReload) 
            {
                DensityList.Clear();
                if (EProfilerType == ProfilerType.Simple)
                {
                    foreach (int v in VertexProfilerUtil.SimpleModeOnlyMeshDensitySetting)
                    {
                        DensityList.Add(v);
                    }
                }
                else if (EProfilerType == ProfilerType.Detail)
                {
                    foreach (int v in VertexProfilerUtil.OnlyMeshDensitySetting)
                    {
                        DensityList.Add(v);
                    }
                }
                NeedSyncColorRangeSetting = true;
            }
            // 检查是否要同步设置
            if (NeedSyncColorRangeSetting)
            {
                m_ColorRangeSettings = new ColorRangeSetting[DensityList.Count];
                for (int i = 0; i < DensityList.Count; i++)
                {
                    float threshold = DensityList[i] * 0.0001f;
                    Color color = VertexProfilerUtil.GetProfilerColor(i, EProfilerType);
                    ColorRangeSetting setting = new ColorRangeSetting();
                    setting.threshold = threshold;
                    setting.color = color;
                    m_ColorRangeSettings[i] = setting;
                }
                NeedSyncColorRangeSetting = false;
            }
        }
        public override void InitRenderers()
        {
            // 初始化
            if (vp.NeedRecollectRenderers)
            {
                // 收集场景内的显示中的渲染器，并收集这些渲染器的包围盒数据
                rendererComponentDatas = RendererCuller.GetAllRenderers();
                
                m_RendererNum = 0;
                m_RendererBoundsData.Clear();
                Mesh mesh;
                for (int i = 0; i < rendererComponentDatas.Count; i++)
                {
                    RendererComponentData data = rendererComponentDatas[i];
                    Renderer renderer = data.renderer;
                
                    if(data.renderer == null)
                        continue;
                    mesh = data.m;
                    mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                    mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                    if(mesh == null) 
                        continue;
                
                    Bounds bound = renderer.bounds;
                    RendererBoundsData boundsData = new RendererBoundsData()
                    {
                        center = bound.center,
                        extends = bound.extents
                    };
                
                    m_RendererBoundsData.Add(boundsData);

                    // SetPropertyBlock不指定index的话，多材质renderer会无法生效
                    var smats = renderer.sharedMaterials;
                    for (int k = 0; k < smats.Length; k++)
                    {
                        MaterialPropertyBlock block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, k);
                        block.SetTexture(VertexProfilerUtil._MainTex, smats[k].mainTexture);
                        block.SetInt(VertexProfilerUtil._RendererId, i);
                        block.SetInt(VertexProfilerUtil._VertexCount, mesh.vertexCount);
                        renderer.SetPropertyBlock(block, k);
                    }
                
                    m_RendererNum++;
                }
            }
        }

        public override void SetupConstantBufferData(CommandBuffer cmd, ref ScriptableRenderContext context)
        {
            base.SetupConstantBufferData(cmd, ref context);

            // 外部处理
            vp.TileNumX = Mathf.CeilToInt((float)vp.MainCamera.pixelWidth / (float)vp.TileWidth);
            vp.TileNumY = Mathf.CeilToInt((float)vp.MainCamera.pixelHeight / (float)vp.TileHeight);
            
            // 在这里使用JobSystem调度视锥剔除计算
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(vp.MainCamera);
            NativeArray<RendererBoundsData> m_RendererBoundsNA = VertexProfilerUtil.ConvertToNativeArray(m_RendererBoundsData, Allocator.TempJob);
            NativeArray<uint> VisibleFlagNA = new NativeArray<uint>(m_RendererNum, Allocator.TempJob);
            NativeArray<Plane> frustumPlanesNA = VertexProfilerUtil.ConvertToNativeArray(frustumPlanes, Allocator.TempJob);
            Job_Culling = new VertexProfilerJobs.J_Culling()
            {
                RendererBoundsData = m_RendererBoundsNA,
                CameraFrustumPlanes = frustumPlanesNA,
                _VisibleFlagList = VisibleFlagNA
            };
            Job_Culling.Run();
            VisibleFlag = VisibleFlagNA.ToList();
            frustumPlanesNA.Dispose();
            m_RendererBoundsNA.Dispose();
            VisibleFlagNA.Dispose();
            
            ReAllocTileProfilerRT(GraphicsFormat.R32G32_SFloat, 
                GraphicsFormat.None, FilterMode.Point, ref m_RendererIdAndVertexCountRT, "_RendererIdAndVertexCountRT");
            ReAllocTileProfilerRT(GraphicsFormat.None, GraphicsFormat.D24_UNorm, 
                FilterMode.Point, ref m_RendererIdAndVertexDepthCountRT, "_RendererIdAndVertexDepthCountRT", false);
            
            m_PixelCounterBuffer = new ComputeBuffer(m_RendererNum, Marshal.SizeOf(typeof(uint)));
            m_PixelCounterBuffer.SetData(new uint[m_RendererNum]);
            cmd.SetRandomWriteTarget(4, m_PixelCounterBuffer);
            cmd.SetGlobalBuffer(VertexProfilerUtil._PixelCounterBuffer, m_PixelCounterBuffer);
            cmd.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetGlobalTexture(VertexProfilerUtil._RendererIdAndVertexCountRT, m_RendererIdAndVertexCountRT);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public override void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 先做一次前向渲染收集rendererId和顶点数信息，以及单独的深度信息,最终结果复制到支持随机读写的纹理中
            CoreUtils.SetRenderTarget(cmd, m_RendererIdAndVertexCountRT, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                m_RendererIdAndVertexDepthCountRT,
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            DrawObjects(cmd, ref URPMeshPixelCalShaderTagId, ref context, ref renderingData);
            
            // 还原原始的颜色缓冲
            CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                renderingData.cameraData.renderer.cameraDepthTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            
            cmd.ClearRandomWriteTargets();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        private void DrawObjects(CommandBuffer cmd, ref List<ShaderTagId> shaderTagIds, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags; 
                
                DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortFlags);
                drawSettings.overrideMaterial = MeshPixelCalMat;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings, ref m_renderStateBlock);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
    
    [System.Serializable]
    public class VertexProfilerOnlyMeshLogRenderPass : VertexProfilerLogBaseRenderPass
    {
        // log 
        private bool pixelCountDataReady = false;
        uint[] pixelCountData = null;
        private RTHandle m_ScreenshotRT;

        public void Setup()
        {
            if (vp == null) return;

            vp.LogMode = this;
        }
        
        public override void OnDisable()
        {
            if (m_ScreenshotRT != null)
            {
                m_ScreenshotRT.Release();
                m_ScreenshotRT = null;
            }
            if (vp != null && vp.LogMode == this)
            {
                vp.LogMode = null;
            }
        }
        public override void DispatchScreenShotAndReadBack(CommandBuffer cmd, ref ScriptableRenderContext context)
        {
            if (vp == null 
                || vp.LogMode != this 
                || VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer == null 
                || !VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer.IsValid()) 
                return;
            
            pixelCountDataReady = false;
            pixelCountData = null;
            
            // 截图
            RenderTextureDescriptor desc = new RenderTextureDescriptor(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, 0);
            desc.enableRandomWrite = true;
            VertexProfilerUtil.ReAllocRTIfNeeded(ref m_ScreenshotRT, desc, FilterMode.Point, TextureWrapMode.Clamp, false, name: "ScreenShot");
            cmd.Blit(colorAttachment, m_ScreenshotRT, vp.GammaCorrectionEffectMat);
            
            // 拉取数据，异步回读
            cmd.RequestAsyncReadback(VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer,
                sizeof(uint) * VertexProfilerModeBaseRenderPass.rendererComponentDatas.Count, 0, 
                (data) =>
            {
                pixelCountDataReady = true;
                pixelCountData = data.GetData<uint>().ToArray();
                LogoutProfilerData();
            });
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        private void LogoutProfilerData()
        {
            // 数据准备完成后再开始
            if (!pixelCountDataReady) return;
            vp.StartCoroutineForProfiler(PullOnlyMeshProfilerData());
        }
        private IEnumerator PullOnlyMeshProfilerData()
        {
            logoutDataList.Clear();
            Mesh mesh;
            for (int i = 0; i < pixelCountData.Length; i++)
            {
                uint flag = VertexProfilerModeBaseRenderPass.VisibleFlag[i];
                if (flag == 0) continue;
                
                RendererComponentData data = VertexProfilerModeBaseRenderPass.rendererComponentDatas[i];
                var renderer = data.renderer;
                if(renderer == null)
                    continue;
                mesh = data.m;
                mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                if(mesh == null) 
                    continue;

                int vertexCount = mesh.vertexCount;
                int pixelCount = (int)pixelCountData[i];
                if(vertexCount == 0) continue;
                float density = pixelCount > 0 ? (float)vertexCount / (float)pixelCount : float.MaxValue;
                string rendererHierarchyPath = VertexProfilerUtil.GetGameObjectNameFromRoots(renderer.transform);
                Color profilerColor = VertexProfilerModeBaseRenderPass.GetProfilerContentColor(density, out int thresholdLevel);
                ProfilerDataContents content = new ProfilerDataContents(
                    mesh.name,
                    vertexCount, 
                    pixelCount,
                    density,
                    rendererHierarchyPath,
                    thresholdLevel,
                    profilerColor);
                logoutDataList.Add(content);
            }
            // log一次就置false
            vp.NeedLogDataToProfilerWindow = false;
            vp.LastLogFrameCount = Time.frameCount;
            
            //截屏需要等待渲染线程结束
            yield return new WaitForEndOfFrame();
            
            // 如果需要输出到Excel才执行
            if (vp.NeedLogOutProfilerData)
            {
                vp.NeedLogOutProfilerData = false;
                //初始化Texture2D, 大小可以根据需求更改
                Texture2D screenShotWithPostEffect = new Texture2D(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, TextureFormat.RGB24, false);
                screenShotWithPostEffect.name = "screenShotWithPostEffect";
                //读取屏幕像素信息并存储为纹理数据
                screenShotWithPostEffect.ReadPixels(new Rect(0, 0, vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight), 0, 0);
                //应用
                screenShotWithPostEffect.Apply();
                // 写入Excel
                VertexProfilerEvent.CallLogoutToExcel(vp.EDisplayType, logoutDataList, m_ScreenshotRT.rt, screenShotWithPostEffect);
                // 释放资源
                Object.DestroyImmediate(screenShotWithPostEffect);
            }
        }
    }
}

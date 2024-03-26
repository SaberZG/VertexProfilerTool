using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VertexProfilerTool
{
    /// <summary>
    /// OnlyTile
    /// </summary>
    public class VertexProfilerTileBasedMeshRF : ScriptableRendererFeature
    {
        VertexProfilerModeTileBasedMeshRenderPass m_ScriptablePass;
        VertexProfilerPostEffectRenderPass m_PostEffectPass;
        VertexProfilerTileBasedMeshLogRenderPass m_LogPass;
        
        public override void Create()
        {
            m_ScriptablePass = new VertexProfilerModeTileBasedMeshRenderPass();
            m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            
            m_LogPass = new VertexProfilerTileBasedMeshLogRenderPass();
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
    public class VertexProfilerModeTileBasedMeshRenderPass : VertexProfilerModeBaseRenderPass
    {
        private Material MeshPixelCalMat;
        private ComputeShader CalculateVertexByTilesCS;

        private VertexProfilerJobs.J_Culling Job_Culling;
        private List<uint> VisibleFlag;

        private int CalculateVertexKernel = 1;
        private int CalculateVertexByTilesCSGroupX = 256;
        
        private RTHandle m_TileProfilerRT;
        private RTHandle m_RendererIdAndVertexCountRT;
        private RTHandle m_RendererIdAndVertexDepthCountRT;
        // 原生渲染所需属性
        List<ShaderTagId> URPMeshPixelCalShaderTagId;
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_renderStateBlock;
        ProfilingSampler m_ProfilingSampler;
        
        public VertexProfilerModeTileBasedMeshRenderPass() : base()
        {
            EDisplayType = DisplayType.TileBasedMesh;
            
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
                CalculateVertexByTilesCS = vp.CalculateVertexByTilesCS;
                MeshPixelCalMat = vp.MeshPixelCalMat;
            };
        }
        
        public override void OnDisable()
        {
            base.OnDisable();
            ReleaseRTHandle(ref m_TileProfilerRT);
            
            if (vp != null && vp.ProfilerMode == this)
            {
                vp.ProfilerMode = null;
            }

            ReleaseAllComputeBuffer();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            EDisplayType = DisplayType.TileBasedMesh;
        }

        public override bool CheckProfilerEnabled()
        {
            return vp != null 
                   && vp.EnableProfiler
                   && MeshPixelCalMat != null 
                   && CalculateVertexByTilesCS != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
            // 简单模式默认使用硬编码的阈值，不做处理
            if (EProfilerType == ProfilerType.Simple) return;
            
            VertexProfilerUtil.TileBasedMeshDensitySetting = new List<int>(VertexProfilerUtil.DefaultTileBasedMeshDensitySetting);
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
                    foreach (int v in VertexProfilerUtil.SimpleModeTileBasedMeshDensitySetting)
                    {
                        DensityList.Add(v);
                    }
                }
                else if (EProfilerType == ProfilerType.Detail)
                {
                    foreach (int v in VertexProfilerUtil.TileBasedMeshDensitySetting)
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
                m_RendererLocalToWorldMatrix.Clear();
            
                Mesh mesh;
                for (int i = 0; i < rendererComponentDatas.Count; i++)
                {
                    RendererComponentData data = rendererComponentDatas[i];
                    Renderer renderer = data.renderer;
                
                    if(renderer == null)
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
                    m_RendererLocalToWorldMatrix.Add(renderer.localToWorldMatrix);
                    
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

            // 相机矩阵
            Matrix4x4 m_v = vp.MainCamera.worldToCameraMatrix;
            Matrix4x4 m_p = GL.GetGPUProjectionMatrix(vp.MainCamera.projectionMatrix, SystemInfo.graphicsUVStartsAtTop);
            Matrix4x4 m_vp = m_p * m_v;
            
            vp.TileNumX = Mathf.CeilToInt((float)vp.MainCamera.pixelWidth / (float)vp.TileWidth);
            vp.TileNumY = Mathf.CeilToInt((float)vp.MainCamera.pixelHeight / (float)vp.TileHeight);
            int tileCount = vp.TileNumX * vp.TileNumY;

            m_VertexCounterBuffer = new ComputeBuffer(m_RendererNum * tileCount, Marshal.SizeOf(typeof(uint)));
            m_VertexCounterBuffer.SetData(new uint[m_RendererNum * tileCount]);
        
            m_PixelCounterBuffer = new ComputeBuffer(m_RendererNum * tileCount, Marshal.SizeOf(typeof(uint)));
            m_PixelCounterBuffer.SetData(new uint[m_RendererNum * tileCount]);
            
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
            
            ReAllocTileProfilerRT(GraphicsFormat.R32_UInt, 
                GraphicsFormat.None, FilterMode.Point, ref m_TileProfilerRT, "TileProfiler");
            ReAllocTileProfilerRT(GraphicsFormat.R32G32_SFloat, 
                GraphicsFormat.None, FilterMode.Point, ref m_RendererIdAndVertexCountRT, "_RendererIdAndVertexCountRT");
            ReAllocTileProfilerRT(GraphicsFormat.None, GraphicsFormat.D24_UNorm, 
                FilterMode.Point, ref m_RendererIdAndVertexDepthCountRT, "_RendererIdAndVertexDepthCountRT", false);
            
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileWidth, vp.TileWidth);
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileCount, tileCount);
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileNumX, vp.TileNumX);
            cmd.SetComputeVectorParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));
            cmd.SetComputeMatrixParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_MATRIX_VP, m_vp);
            cmd.SetComputeVectorParam(CalculateVertexByTilesCS, VertexProfilerUtil._ScreenParams, new Vector4(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 1.0f / vp.MainCamera.pixelWidth, 1.0f / vp.MainCamera.pixelHeight));
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_UV_STARTS_AT_TOP, SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            cmd.SetComputeBufferParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._VertexCounterBuffer, m_VertexCounterBuffer);
            
            cmd.SetGlobalInt(VertexProfilerUtil._TileWidth, vp.TileWidth);
            cmd.SetGlobalInt(VertexProfilerUtil._TileNumX, vp.TileNumX);
            cmd.SetGlobalInt(VertexProfilerUtil._TileCount, tileCount);
            cmd.SetGlobalVector(VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));

            cmd.SetRandomWriteTarget(4, m_PixelCounterBuffer);
            cmd.SetGlobalBuffer(VertexProfilerUtil._VertexCounterBuffer, m_VertexCounterBuffer);
            cmd.SetGlobalBuffer(VertexProfilerUtil._PixelCounterBuffer, m_PixelCounterBuffer);
            cmd.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            cmd.SetGlobalTexture(VertexProfilerUtil._RendererIdAndVertexCountRT, m_RendererIdAndVertexCountRT);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public override void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            using (new ProfilingScope(cmd, new ProfilingSampler("Calculate Vertex By Tiles")))
            {
                Mesh mesh;
                for (int k = 0; k < m_RendererNum; k++)
                {
                    uint flag = VisibleFlag[k];
                    if (flag <= 0u) continue; // 不在摄像机视锥范围内
                    
                    Matrix4x4 localToWorld = m_RendererLocalToWorldMatrix[k];
                    RendererComponentData data = rendererComponentDatas[k];
                    if(!data.renderer.enabled) continue; // 渲染器没有启用

                    mesh = data.m;
                    mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                    mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                    if(mesh == null) continue; // 没找到mesh对象
#if UNITY_2020_1_OR_NEWER
                    var meshBuffer = RendererCuller.GetGraphicBufferByMesh(mesh);
#else
                    var meshBuffer = RendererCuller.GetComputeBufferByMesh(mesh);
#endif
                    if(meshBuffer == null) continue; // 获取不到meshBuffer
                
                    int count = mesh.vertexCount;
                    cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._VertexNum, count);
                    cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._RendererId, k);
                    cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._VertexDataSize, meshBuffer.stride / sizeof(float));
                    cmd.SetComputeMatrixParam(CalculateVertexByTilesCS, VertexProfilerUtil._LocalToWorld, localToWorld);
                    cmd.SetComputeBufferParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._VertexData, meshBuffer);
                    cmd.DispatchCompute(CalculateVertexByTilesCS, CalculateVertexKernel, Mathf.CeilToInt((float)count / CalculateVertexByTilesCSGroupX), 1, 1);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    // meshBuffer?.Dispose();
                }
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
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
            cmd.SetRandomWriteTarget(3, m_TileProfilerRT);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        private void DrawObjects(CommandBuffer cmd, ref List<ShaderTagId> shaderTagIds, 
            ref ScriptableRenderContext context, ref RenderingData renderingData)
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
    public class VertexProfilerTileBasedMeshLogRenderPass : VertexProfilerLogBaseRenderPass
    {
        // log 
        private bool vertexCountDataReady = false;
        uint[] vertexCountData = null;
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
                || VertexProfilerModeBaseRenderPass.m_VertexCounterBuffer == null 
                || !VertexProfilerModeBaseRenderPass.m_VertexCounterBuffer.IsValid() 
                || VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer == null 
                || !VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer.IsValid())
                return;
            
            vertexCountDataReady = false;
            vertexCountData = null;
            pixelCountDataReady = false;
            pixelCountData = null;
            
            // 截图
            RenderTextureDescriptor desc = new RenderTextureDescriptor(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, 0);
            desc.enableRandomWrite = true;
            VertexProfilerUtil.ReAllocRTIfNeeded(ref m_ScreenshotRT, desc, FilterMode.Point, TextureWrapMode.Clamp, false, name: "ScreenShot");
            cmd.Blit(colorAttachment, m_ScreenshotRT, vp.GammaCorrectionEffectMat);
            
            // 拉取数据，异步回读
            int tileCount = vp.TileNumX * vp.TileNumY;
            int dataCount = VertexProfilerModeBaseRenderPass.rendererComponentDatas.Count * tileCount;
            cmd.RequestAsyncReadback(VertexProfilerModeBaseRenderPass.m_VertexCounterBuffer,sizeof(uint) * dataCount, 0, (data) =>
            {
                vertexCountDataReady = true;
                vertexCountData = data.GetData<uint>().ToArray();
                LogoutProfilerData();
            });
            cmd.RequestAsyncReadback(VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer,sizeof(uint) * dataCount, 0, (data) =>
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
            if (!vertexCountDataReady || !pixelCountDataReady) return;
            vp.StartCoroutineForProfiler(PullTileBasedMeshProfilerData());
        }
        private IEnumerator PullTileBasedMeshProfilerData()
        {
            if (vertexCountDataReady || pixelCountDataReady)
            {
                int tileCount = vp.TileNumX * vp.TileNumY;
                List<BatchProfilerDataContents> batchDataList = new List<BatchProfilerDataContents>();
                
                Mesh mesh;
                for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
                {
                    List<ProfilerDataContents> profilerDataContentsList = new List<ProfilerDataContents>();
                    int maxThresholdLevel = 0;
                    float maxDensity = 0f;
                    for (int rendererId = 0; rendererId < VertexProfilerModeBaseRenderPass.rendererComponentDatas.Count; rendererId++)
                    {
                        int bufferIndex = rendererId * tileCount + tileIndex;
                        int vertexCount = (int)vertexCountData[bufferIndex];
                        int pixelCount = (int)pixelCountData[bufferIndex];
                        if(vertexCount == 0 || pixelCount == 0) continue;
                        
                        RendererComponentData data = VertexProfilerModeBaseRenderPass.rendererComponentDatas[rendererId];
                        var renderer = data.renderer;
                        if(renderer == null)
                            continue;
                        mesh = data.m;
                        mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                        mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                        if(mesh == null) 
                            continue;

                        int meshVertexCount = mesh.vertexCount;
                        float density = pixelCount > 0 ? (float)vertexCount / (float)pixelCount : float.MaxValue;
                        maxDensity = pixelCount > 0 ? Mathf.Max(maxDensity, density) : maxDensity;
                        string rendererHierarchyPath = VertexProfilerUtil.GetGameObjectNameFromRoots(renderer.transform);
                        Color profilerColor = VertexProfilerModeBaseRenderPass.GetProfilerContentColor(density, out int thresholdLevel);
                        maxThresholdLevel = Mathf.Max(maxThresholdLevel, thresholdLevel);
                        ProfilerDataContents meshContent = new ProfilerDataContents(
                            mesh.name,
                            vertexCount, 
                            meshVertexCount,
                            pixelCount,
                            density,
                            rendererHierarchyPath,
                            thresholdLevel,
                            profilerColor);
                        profilerDataContentsList.Add(meshContent);
                    }
                    // 此棋盘格有数据就加入到最终输出的集合包中
                    if (profilerDataContentsList.Count > 0)
                    {
                        profilerDataContentsList.Sort();
                        // 仅用于分类tile作缩进并记录tileIndex，无实际作用
                        ProfilerDataContents tileContent = new ProfilerDataContents(
                            tileIndex, 
                            0, 
                            maxDensity, 
                            maxThresholdLevel,
                            Color.white);
                        BatchProfilerDataContents batchProfilerDataContents = new BatchProfilerDataContents(
                            tileContent, 
                            profilerDataContentsList,
                            maxDensity);
                        batchDataList.Add(batchProfilerDataContents);
                    }
                }
                batchDataList.Sort();
                // 遍历排序后的数据，再拆成ProfilerDataContents写入最终列表
                logoutDataList.Clear();
                for (int i = 0; i < batchDataList.Count; i++)
                {
                    BatchProfilerDataContents batchProfilerDataContents = batchDataList[i];
                    logoutDataList.Add(batchProfilerDataContents.RootProfilerDataContents);
                    logoutDataList.AddRange(batchProfilerDataContents.ProfilerDataContentsList);
                }
                // log一次就置false
                vp.NeedLogDataToProfilerWindow = false;
                vp.LastLogFrameCount = Time.frameCount;
                
                //因为需要带UI的截屏，因此需要等待渲染线程结束
                yield return new WaitForEndOfFrame();
                
                // 如果需要输出到Excel才执行
                if (vp.NeedLogOutProfilerData)
                {
                    vp.NeedLogOutProfilerData = false;
                    //初始化Texture2D, 大小可以根据需求更改
                    Texture2D screenShotWithGrids = new Texture2D(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, TextureFormat.RGB24, false);
                    screenShotWithGrids.name = "screenShotWithGrids";
                    //读取屏幕像素信息并存储为纹理数据
                    screenShotWithGrids.ReadPixels(new Rect(0, 0, vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight), 0, 0);
                    //应用
                    screenShotWithGrids.Apply();
                    // 写入Excel
                    VertexProfilerEvent.CallLogoutToExcel(vp.EDisplayType, logoutDataList, m_ScreenshotRT.rt, screenShotWithGrids);
                    // 释放资源
                    Object.DestroyImmediate(screenShotWithGrids);
                }
            }
        }
    }
}

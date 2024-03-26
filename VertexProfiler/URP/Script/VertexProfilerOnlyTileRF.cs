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
    public class VertexProfilerOnlyTileRF : ScriptableRendererFeature
    {
        VertexProfilerModeOnlyTileRenderPass m_ScriptablePass; 
        VertexProfilerOnlyTileLogRenderPass m_LogPass;
        VertexProfilerPostEffectRenderPass m_PostEffectPass;
        public override void Create()
        {
            m_ScriptablePass = new VertexProfilerModeOnlyTileRenderPass();
            m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            
            m_LogPass = new VertexProfilerOnlyTileLogRenderPass();
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
    
    public class VertexProfilerModeOnlyTileRenderPass : VertexProfilerModeBaseRenderPass
    {
        private ComputeShader CalculateVertexByTilesCS;
        private ComputeShader GenerateProfilerRTCS;
        private Shader VertexProfilerReplaceShader;
        
        private VertexProfilerJobs.J_Culling Job_Culling;

        private int CalculateVertexKernel = 0;
        private int GenerateProfilerKernel = 0;
        private int RendererCullingCSGroupX = 64;
        private int CalculateVertexByTilesCSGroupX = 256;
        
        private RTHandle m_TileProfilerRT;

        public VertexProfilerModeOnlyTileRenderPass() : base()
        {
            EDisplayType = DisplayType.OnlyTile;
        }

        public void Setup()
        {
            if (vp != null)
            {
                vp.ProfilerMode = this;
                EProfilerType = vp.EProfilerType;
                CheckColorRangeData(true);
                CalculateVertexByTilesCS = vp.CalculateVertexByTilesCS;
                GenerateProfilerRTCS = vp.GenerateProfilerRTCS;
                VertexProfilerReplaceShader = vp.VertexProfilerReplaceShader;
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
            EDisplayType = DisplayType.OnlyTile;
        }

        public override bool CheckProfilerEnabled()
        {
            return vp != null 
                   && vp.EnableProfiler
                   && CalculateVertexByTilesCS != null 
                   && GenerateProfilerRTCS != null 
                   && VertexProfilerReplaceShader != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
            // 简单模式默认使用硬编码的阈值，不做处理
            if (EProfilerType == ProfilerType.Simple) return;
            
            VertexProfilerUtil.OnlyTileDensitySetting = new List<int>(VertexProfilerUtil.DefaultOnlyTileDensitySetting);
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
                    foreach (int v in VertexProfilerUtil.SimpleModeOnlyTileDensitySetting)
                    {
                        DensityList.Add(v);
                    }
                }
                else if (EProfilerType == ProfilerType.Detail)
                {
                    foreach (int v in VertexProfilerUtil.OnlyTileDensitySetting)
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
                    float threshold = DensityList[i] * vp.TileHeight * vp.TileWidth * 0.0001f;
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
                    m_RendererNum++;
                    m_RendererBoundsData.Add(boundsData);
                    m_RendererLocalToWorldMatrix.Add(renderer.localToWorldMatrix);
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
            
            // 外部处理
            vp.TileNumX = Mathf.CeilToInt((float)vp.MainCamera.pixelWidth / (float)vp.TileWidth);
            vp.TileNumY = Mathf.CeilToInt((float)vp.MainCamera.pixelHeight / (float)vp.TileHeight);

            m_TileVerticesCountBuffer = new ComputeBuffer(vp.TileNumX * vp.TileNumY, Marshal.SizeOf(typeof(uint)));
            m_TileVerticesCountBuffer.SetData(new uint[vp.TileNumX * vp.TileNumY]);
            
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

            ReAllocTileProfilerRT(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, FilterMode.Point, ref m_TileProfilerRT, "TileProfiler");
            
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileWidth, vp.TileWidth);
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileNumX, vp.TileNumX);
            cmd.SetComputeVectorParam(CalculateVertexByTilesCS, VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));
            cmd.SetComputeMatrixParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_MATRIX_VP, m_vp);
            cmd.SetComputeVectorParam(CalculateVertexByTilesCS, VertexProfilerUtil._ScreenParams, new Vector4(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 1.0f / vp.MainCamera.pixelWidth, 1.0f / vp.MainCamera.pixelHeight));
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_UV_STARTS_AT_TOP, SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            cmd.SetComputeBufferParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._TileVerticesCount, m_TileVerticesCountBuffer);

            cmd.SetComputeVectorParam(GenerateProfilerRTCS, VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._TileNumX, vp.TileNumX);
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetComputeBufferParam(GenerateProfilerRTCS, 0, VertexProfilerUtil._TileVerticesCount, m_TileVerticesCountBuffer);
            cmd.SetComputeBufferParam(GenerateProfilerRTCS, 0, VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetComputeTextureParam(GenerateProfilerRTCS, 0, VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);

            cmd.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT.nameID);
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
                    cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._VertexDataSize, meshBuffer.stride / sizeof(float));
                    cmd.SetComputeMatrixParam(CalculateVertexByTilesCS, VertexProfilerUtil._LocalToWorld, localToWorld);
                    cmd.SetComputeBufferParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._VertexData, meshBuffer);
                    cmd.DispatchCompute(CalculateVertexByTilesCS, CalculateVertexKernel, Mathf.CeilToInt((float)count / CalculateVertexByTilesCSGroupX), 1, 1);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    // meshBuffer?.Dispose();
                }
            }

            using (new ProfilingScope(cmd, new ProfilingSampler("Generate Profiler RT")))
            {
                cmd.DispatchCompute(GenerateProfilerRTCS, GenerateProfilerKernel, Mathf.CeilToInt((float)m_TileProfilerRT.rt.width / 16), Mathf.CeilToInt((float)m_TileProfilerRT.rt.height / 16), 1);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
    
    [System.Serializable]
    public class VertexProfilerOnlyTileLogRenderPass : VertexProfilerLogBaseRenderPass
    {
        // log 
        private bool tileVerticesCountDataReady = false;
        uint[] tileVerticesCountData = null;
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
            if (vp == null || vp.LogMode != this 
                           || VertexProfilerModeBaseRenderPass.m_TileVerticesCountBuffer == null 
                           || !VertexProfilerModeBaseRenderPass.m_TileVerticesCountBuffer.IsValid()) 
                return;

            tileVerticesCountDataReady = false;
            tileVerticesCountData = null;

            // 截图
            RenderTextureDescriptor desc = new RenderTextureDescriptor(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, 0);
            desc.enableRandomWrite = true;
            VertexProfilerUtil.ReAllocRTIfNeeded(ref m_ScreenshotRT, desc, FilterMode.Point, TextureWrapMode.Clamp, false, name: "ScreenShot");
            cmd.Blit(colorAttachment, m_ScreenshotRT, vp.GammaCorrectionEffectMat);
            
            // 拉取数据，异步回读
            int tileNum = vp.TileNumX * vp.TileNumY;
            cmd.RequestAsyncReadback(VertexProfilerModeBaseRenderPass.m_TileVerticesCountBuffer,sizeof(uint) * tileNum, 0, (data) =>
            {
                tileVerticesCountDataReady = true;
                tileVerticesCountData = data.GetData<uint>().ToArray();
                LogoutProfilerData();
            });

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        private void LogoutProfilerData()
        {
            // 数据准备完成后再开始
            if (!tileVerticesCountDataReady) return;
            vp.StartCoroutineForProfiler(PullOnlyTileProfilerData());
        }
        private IEnumerator PullOnlyTileProfilerData()
        {
            if (tileVerticesCountDataReady)
            {
                logoutDataList.Clear();
                for (int i = 0; i < tileVerticesCountData.Length; i++)
                {
                    uint vertexCount = tileVerticesCountData[i];
                    if(vertexCount == 0) continue;
                    float density = (float)vertexCount / (float)(vp.TileHeight * vp.TileWidth) * 10000f;
                    Color profilerColor = VertexProfilerModeBaseRenderPass.GetProfilerContentColor(density, out int thresholdLevel);
                    ProfilerDataContents content = new ProfilerDataContents(
                        i, 
                        vertexCount, 
                        density,
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

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;

namespace VertexProfilerTool
{
    [System.Serializable][ExecuteInEditMode]
    public class ProfilerOnlyTileMode : ProfilerModeBase
    {
        public ComputeShader CalculateVertexByTilesCS;
        public ComputeShader GenerateProfilerRTCS;
        
        private int CalculateVertexKernel = 0;
        private int GenerateProfilerKernel = 0;
        private int CalculateVertexByTilesCSGroupX = 256;
        
        private Material GammaCorrectionEffectMat;

        private VertexProfilerJobs.J_Culling Job_Culling;
        private List<uint> VisibleFlag;
        private ComputeBuffer m_TileVerticesCountBuffer;
        
        private List<RendererComponentData> rendererComponentDatas;
        private RenderTexture m_TileProfilerRT;
        private RenderTexture screenShot;
        
        public ProfilerOnlyTileMode(VertexProfiler vp) : base(vp)
        {
            EdDisplayType = DisplayType.OnlyTile;

            CalculateVertexByTilesCS = vp.CalculateVertexByTilesCS;
            GenerateProfilerRTCS = vp.GenerateProfilerRTCS;
            
            GammaCorrectionEffectMat = vp.GammaCorrectionEffectMat;
        }
        
        public override void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (vp.NeedLogOutProfilerData) // 输出到Excel需要截屏，这个是将颜色缓冲单独渲染到一份RT上，需要做一次Gamma矫正
            {
                screenShot = RenderTexture.GetTemporary(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 0, GraphicsFormat.B8G8R8A8_UNorm);
                screenShot.name = "screenShot";
                Graphics.Blit(src, screenShot, GammaCorrectionEffectMat);
            }
            if (ApplyProfilerDataByPostEffectMat != null)
            {
                // 进入后处理阶段，使用m_TileProfilerRT之前需要执行一次释放
                Graphics.ClearRandomWriteTargets();
                Graphics.Blit(src, dest, ApplyProfilerDataByPostEffectMat);
            }
            else
            {
                Graphics.Blit(src, dest);
            }
        }
        
        public override bool CheckProfilerEnabled()
        {
            return vp != null 
                   && vp.EnableProfiler 
                   && CalculateVertexByTilesCS != null 
                   && GenerateProfilerRTCS != null 
                   && ApplyProfilerDataByPostEffectMat != null;
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
                    float threshold = DensityList[i] * 0.0001f * vp.TileHeight * vp.TileWidth; 
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
                    mesh = data.mf != null ? data.mf.sharedMesh : null;
                    mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                    if(mesh == null) 
                        continue;
                    
                    data.m = mesh;
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
        
        public override void SetupConstantBufferData()
        {
            base.SetupConstantBufferData();

            Vector3 cameraWorldPosition = vp.MainCamera.transform.position;
            // 相机矩阵
            Matrix4x4 m_v = vp.MainCamera.worldToCameraMatrix;
            Matrix4x4 m_p = GL.GetGPUProjectionMatrix(vp.MainCamera.projectionMatrix, SystemInfo.graphicsUVStartsAtTop);
            Matrix4x4 m_vp = m_p * m_v;
            
            vp.TileNumX = Mathf.CeilToInt((float)vp.MainCamera.pixelWidth / (float)vp.TileWidth);
            vp.TileNumY = Mathf.CeilToInt((float)vp.MainCamera.pixelHeight / (float)vp.TileHeight);

            m_TileVerticesCountBuffer = new ComputeBuffer(vp.TileNumX * vp.TileNumY, Marshal.SizeOf(typeof(uint)));
            m_TileVerticesCountBuffer.SetData(new uint[vp.TileNumX * vp.TileNumY]);
            
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, 0,
                "m_TileProfilerRT", ref m_TileProfilerRT);
            m_TileProfilerRT.filterMode = FilterMode.Point;
            
            // 在这里使用JobSystem调度视锥剔除计算
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(vp.MainCamera);
            NativeArray<Plane> frustumPlanesNA = VertexProfilerUtil.ConvertToNativeArray(frustumPlanes, Allocator.TempJob);
            NativeArray<RendererBoundsData> m_RendererBoundsNA = VertexProfilerUtil.ConvertToNativeArray(m_RendererBoundsData, Allocator.TempJob);
            NativeArray<uint> VisibleFlagNA = new NativeArray<uint>(m_RendererNum, Allocator.TempJob);
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
            
            CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._TileWidth, vp.TileWidth);
            CalculateVertexByTilesCS.SetVector(VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));
            CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._TileNumX, vp.TileNumX);
            CalculateVertexByTilesCS.SetMatrix(VertexProfilerUtil._UNITY_MATRIX_VP, m_vp);
            CalculateVertexByTilesCS.SetVector(VertexProfilerUtil._ScreenParams, new Vector4(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 1.0f / vp.MainCamera.pixelWidth, 1.0f / vp.MainCamera.pixelHeight));
            CalculateVertexByTilesCS.SetBool(VertexProfilerUtil._UNITY_UV_STARTS_AT_TOP, SystemInfo.graphicsUVStartsAtTop);
            CalculateVertexByTilesCS.SetBuffer(CalculateVertexKernel, VertexProfilerUtil._TileVerticesCount, m_TileVerticesCountBuffer);
            
            GenerateProfilerRTCS.SetVector(VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._TileNumX, vp.TileNumX);
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            GenerateProfilerRTCS.SetBuffer(0, VertexProfilerUtil._TileVerticesCount, m_TileVerticesCountBuffer);
            GenerateProfilerRTCS.SetBuffer(0, VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            GenerateProfilerRTCS.SetTexture(0, VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            
            Shader.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
        }

        public override void Dispatch()
        {
            Profiler.BeginSample("Calculate Vertex By Tiles");
            {
                Mesh mesh;
                for (int k = 0; k < m_RendererNum; k++)
                {
                    uint flag = VisibleFlag[k];
                    if (flag > 0)
                    {
                        Matrix4x4 localToWorld = m_RendererLocalToWorldMatrix[k];
                        RendererComponentData data = rendererComponentDatas[k];
                        if(!data.renderer.enabled)
                            continue;

                        mesh = data.m;
                        // mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                        // mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                        if(mesh == null)
                            continue;
                        
#if UNITY_2020_1_OR_NEWER
                        var meshBuffer = RendererCuller.GetGraphicBufferByMesh(mesh);
#else
                        var meshBuffer = RendererCuller.GetComputeBufferByMesh(mesh);
#endif
                        if(meshBuffer == null) continue;
                        
                        int count = mesh.vertexCount;
                        CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._VertexNum, count);
                        CalculateVertexByTilesCS.SetMatrix(VertexProfilerUtil._LocalToWorld, localToWorld);
                        CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._VertexDataSize, meshBuffer.stride / sizeof(float));
                        CalculateVertexByTilesCS.SetBuffer(CalculateVertexKernel, VertexProfilerUtil._VertexData, meshBuffer);
                        CalculateVertexByTilesCS.Dispatch(CalculateVertexKernel, Mathf.CeilToInt((float)count / CalculateVertexByTilesCSGroupX), 1, 1);
                        // meshBuffer?.Dispose();
                    }
                }
            }
            Profiler.EndSample();
            // todo GPU Instancing渲染也需要收集，后面加多一个处理 
            
            Profiler.BeginSample("Generate Profiler RT");
            {
                GenerateProfilerRTCS.Dispatch(GenerateProfilerKernel, Mathf.CeilToInt((float)m_TileProfilerRT.width / 16), Mathf.CeilToInt((float)m_TileProfilerRT.height / 16), 1);
            }
            Profiler.EndSample();
        }

        public override void Release()
        {
            ReleaseAllComputeBuffer();
            ReleaseRenderTexture(ref screenShot);
        }

        public override void ReleaseAllComputeBuffer()
        {
            base.ReleaseAllComputeBuffer();
            ReleaseComputeBuffer(ref m_TileVerticesCountBuffer);
            
            ReleaseRenderTexture(ref m_TileProfilerRT);
        }
        
        public override void LogoutProfilerData()
        {
            vp.StartCoroutineForProfiler(PullOnlyTileProfilerData());
        }
        
        private IEnumerator PullOnlyTileProfilerData()
        {
            if (m_TileVerticesCountBuffer == null)
            {
                vp.NeedLogDataToProfilerWindow = false;
                vp.NeedLogOutProfilerData = false;
                ReleaseRenderTexture(ref screenShot);
                yield break;
            }
            // 拉取数据，当前帧进行
            uint[] tileVerticesCountData = new uint[vp.TileNumX * vp.TileNumY];
            m_TileVerticesCountBuffer.GetData(tileVerticesCountData);
            logoutDataList.Clear();
            for (int i = 0; i < tileVerticesCountData.Length; i++)
            {
                uint vertexCount = tileVerticesCountData[i];
                if(vertexCount == 0) continue;
                float density = (float)vertexCount / (float)(vp.TileHeight * vp.TileWidth) * 10000f;
                Color profilerColor = GetProfilerContentColor(density, out int thresholdLevel);
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
                //初始化Texture2D, 大小可以根据需求更改
                Texture2D screenShotWithGrids = new Texture2D(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, TextureFormat.RGB24, false);
                screenShotWithGrids.name = "screenShotWithGrids";
                //读取屏幕像素信息并存储为纹理数据
                screenShotWithGrids.ReadPixels(new Rect(0, 0, vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight), 0, 0);
                //应用
                screenShotWithGrids.Apply();
                // 写入Excel
                VertexProfilerEvent.CallLogoutToExcel(vp.EDisplayType, logoutDataList, screenShot, screenShotWithGrids);
                // 释放资源
                ReleaseRenderTexture(ref screenShot);
                Object.DestroyImmediate(screenShotWithGrids);
                vp.NeedLogOutProfilerData = false;
            }
        }
    }
}


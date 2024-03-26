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
    public class ProfilerMeshHeatMapMode : ProfilerModeBase
    {
        public ComputeShader CalculateVertexByTilesCS;
        public ComputeShader GenerateProfilerRTCS;
        private int CalculateVertexKernel = 2;
        private int CalculateVertexKernel2 = 3;
        private int GenerateProfilerKernel = 1;
        private int CalculateVertexByTilesCSGroupX = 256;
        
        private Shader OutputRendererIdShader;
        private Material GammaCorrectionEffectMat;
        
        public bool initedRRCamera = false;
        private Camera rrCamera; // rr => Replace Rendering,用于Mesh类型的替换渲染

        private VertexProfilerJobs.J_Culling Job_Culling;
        private List<uint> VisibleFlag;
        
        private List<RendererComponentData> rendererComponentDatas;
        private RenderTexture m_RenderIdAndDepthRT; // 记录RendererId和深度
        private RenderTexture m_TileProfilerUIntRT;
        private RenderTexture m_TileProfilerUInt2RT;
        private RenderTexture m_TileProfilerRT;
        private RenderTexture screenShot;
        
        public ProfilerMeshHeatMapMode(VertexProfiler vp) : base(vp)
        {
            EdDisplayType = DisplayType.MeshHeatMap;
            
            CalculateVertexByTilesCS = vp.CalculateVertexByTilesCS;
            GenerateProfilerRTCS = vp.GenerateProfilerRTCS;
            OutputRendererIdShader = Shader.Find("VertexProfiler/OutputRendererIdShader");
            GammaCorrectionEffectMat = vp.GammaCorrectionEffectMat;
            
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R32G32_SFloat,  24, "_RenderIdAndDepthRT", ref m_RenderIdAndDepthRT);
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
            
            VertexProfilerUtil.OnlyMeshDensitySetting = new List<int>(VertexProfilerUtil.DefaultMeshHeatMapSetting);
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
                    foreach (int v in VertexProfilerUtil.SimpleModeMeshHeatMapSetting)
                    {
                        DensityList.Add(v);
                    }
                }
                else if (EProfilerType == ProfilerType.Detail)
                {
                    foreach (int v in VertexProfilerUtil.MeshHeatMapSetting)
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
                    float threshold = DensityList[i] * 0.001f;
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
            // 检查替换渲染相机的初始化和同步
            if (!initedRRCamera)
            {
                GameObject goCam;
                goCam = GameObject.Find("rrCamera");
                if (goCam == null)
                {
                    goCam = new GameObject("rrCamera");
                }
                goCam.transform.SetParent(vp.MainCamera.transform);
                goCam.transform.localPosition = Vector3.zero;
                goCam.transform.localRotation = Quaternion.identity;
                goCam.transform.localScale = Vector3.one;

                rrCamera = goCam.GetComponent<Camera>();
                if(rrCamera == null)
                    rrCamera = goCam.AddComponent<Camera>();
                
                rrCamera.cameraType = vp.MainCamera.cameraType;
                rrCamera.backgroundColor = Color.black;
                rrCamera.fieldOfView = vp.MainCamera.fieldOfView;
                rrCamera.aspect = vp.MainCamera.aspect;
                rrCamera.nearClipPlane = vp.MainCamera.nearClipPlane;
                rrCamera.farClipPlane = vp.MainCamera.farClipPlane;
                rrCamera.cullingMask = vp.MainCamera.cullingMask;
                rrCamera.targetTexture = null;
                // rrCamera.targetTexture = m_OutputRenderIdRT;
                rrCamera.clearFlags = CameraClearFlags.SolidColor;
                rrCamera.enabled = false; // rr相机不需要启动，通过调度替换渲染来执行就行
                rrCamera.SetTargetBuffers(m_RenderIdAndDepthRT.colorBuffer, m_RenderIdAndDepthRT.depthBuffer);
                
                initedRRCamera = true;
            }
            
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
                    
                    m_RendererBoundsData.Add(boundsData);
                    m_RendererLocalToWorldMatrix.Add(renderer.localToWorldMatrix);
                    RendererCuller.TryInitMaterialPropertyBlock(renderer, i, mesh.vertexCount);
                    m_RendererNum++;
                }
            }
        }
        
        public override void SetupConstantBufferData()
        {
            base.SetupConstantBufferData();

            // 相机矩阵
            Matrix4x4 m_v = vp.MainCamera.worldToCameraMatrix;
            Matrix4x4 m_p = GL.GetGPUProjectionMatrix(vp.MainCamera.projectionMatrix, SystemInfo.graphicsUVStartsAtTop);
            Matrix4x4 m_vp = m_p * m_v;

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

            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R32_UInt,  0, "m_TileProfilerUIntRT", ref m_TileProfilerUIntRT);
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R32G32_UInt,  0, "m_TileProfilerUInt2RT", ref m_TileProfilerUInt2RT);
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm,  0, "m_TileProfilerRT", ref m_TileProfilerRT);

            CalculateVertexByTilesCS.SetMatrix(VertexProfilerUtil._UNITY_MATRIX_VP, m_vp);
            CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._UNITY_REVERSED_Z, SystemInfo.usesReversedZBuffer ? 1 : 0);
            CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._CullMode, (int)ECullMode);
            CalculateVertexByTilesCS.SetVector(VertexProfilerUtil._ScreenParams, new Vector4(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 1.0f / vp.MainCamera.pixelWidth, 1.0f / vp.MainCamera.pixelHeight));
            CalculateVertexByTilesCS.SetBool(VertexProfilerUtil._UNITY_UV_STARTS_AT_TOP, SystemInfo.graphicsUVStartsAtTop);
            CalculateVertexByTilesCS.SetTexture(CalculateVertexKernel, VertexProfilerUtil._RenderIdAndDepthRT, m_RenderIdAndDepthRT);
            CalculateVertexByTilesCS.SetTexture(CalculateVertexKernel, VertexProfilerUtil._TileProfilerRTUint, m_TileProfilerUIntRT);
            
            CalculateVertexByTilesCS.SetTexture(CalculateVertexKernel2, VertexProfilerUtil._RenderIdAndDepthRT, m_RenderIdAndDepthRT);
            CalculateVertexByTilesCS.SetTexture(CalculateVertexKernel2, VertexProfilerUtil._TileProfilerRTUint, m_TileProfilerUIntRT);
            CalculateVertexByTilesCS.SetTexture(CalculateVertexKernel2, VertexProfilerUtil._TileProfilerRTUint2, m_TileProfilerUInt2RT);
            
            GenerateProfilerRTCS.SetVector(VertexProfilerUtil._ScreenParams, new Vector4(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 1.0f / vp.MainCamera.pixelWidth, 1.0f / vp.MainCamera.pixelHeight));
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._HeatMapRange, vp.HeatMapRange);
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._HeatMapStep, vp.HeatMapStep);
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._HeatMapOffsetCount, vp.HeatMapOffsetCount);
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            GenerateProfilerRTCS.SetBuffer(GenerateProfilerKernel, VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            GenerateProfilerRTCS.SetVector(VertexProfilerUtil._HeatMapRampRange, new Vector2(vp.HeatMapRampMin, vp.HeatMapRampMax));
            GenerateProfilerRTCS.SetTexture(GenerateProfilerKernel, VertexProfilerUtil._TileProfilerRTUint2, m_TileProfilerUInt2RT);
            GenerateProfilerRTCS.SetTexture(GenerateProfilerKernel, VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);

            Shader.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            Shader.SetGlobalTexture(VertexProfilerUtil._HeatMapTex, vp.HeatMapTex);
            
            Shader.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            Shader.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
        }

        public override void Dispatch()
        {
            Profiler.BeginSample("Calculate Meshes Cover Area");
            {
                // 调度渲染：计算出当前画面中mesh的Id分布情况
                rrCamera.RenderWithShader(OutputRendererIdShader, "");
            }
            Profiler.EndSample();
            
            // 根据分布情况，筛选出需要统计的顶点
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
                        mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                        mesh = data.smr != null ? data.smr.sharedMesh : mesh;
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
                        CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._RendererId, k);
                        CalculateVertexByTilesCS.SetMatrix(VertexProfilerUtil._LocalToWorld, localToWorld);
                        CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._VertexDataSize, meshBuffer.stride / sizeof(float));
                        CalculateVertexByTilesCS.SetBuffer(CalculateVertexKernel, VertexProfilerUtil._VertexData, meshBuffer);
                        CalculateVertexByTilesCS.Dispatch(CalculateVertexKernel, Mathf.CeilToInt((float)count / CalculateVertexByTilesCSGroupX), 1, 1);
                    }
                }
            }
            Profiler.EndSample();
             
            Profiler.BeginSample("Merge Data RT");
            {
                CalculateVertexByTilesCS.Dispatch(CalculateVertexKernel2, Mathf.CeilToInt((float)m_TileProfilerUInt2RT.width / 16), Mathf.CeilToInt((float)m_TileProfilerUInt2RT.height / 16), 1);
            }
            Profiler.EndSample();

            Profiler.BeginSample("Generate Profiler RT");
            {
                GenerateProfilerRTCS.Dispatch(GenerateProfilerKernel, Mathf.CeilToInt((float)m_TileProfilerRT.width / 16), Mathf.CeilToInt((float)m_TileProfilerRT.height / 16), 1);
            }
            Profiler.EndSample();
        }
        
        public override void Release()
        {
            // 还原颜色buffer再销毁
            if (rrCamera)
            {
                rrCamera.SetTargetBuffers(Graphics.activeColorBuffer, Graphics.activeDepthBuffer);
            }
            ReleaseAllComputeBuffer();
            DestroyRRCamera();
            ReleaseRenderTexture(ref screenShot);
            ReleaseRenderTexture(ref m_RenderIdAndDepthRT);
        }

        public override void ReleaseAllComputeBuffer()
        {
            base.ReleaseAllComputeBuffer();
            
            ReleaseRenderTexture(ref m_TileProfilerUIntRT);
            ReleaseRenderTexture(ref m_TileProfilerUInt2RT);
            ReleaseRenderTexture(ref m_TileProfilerRT);
        }
        
        void DestroyRRCamera()
        {
            if (initedRRCamera)
            {
                Object.DestroyImmediate(rrCamera.gameObject);
                rrCamera = null;
                initedRRCamera = false;
            }
        }
        
        public override void LogoutProfilerData()
        {
            // if (m_PixelCounterBuffer == null)
            // {
            //     vp.NeedLogDataToProfilerWindow = false;
            //     vp.NeedLogOutProfilerData = false;
            //     ReleaseRenderTexture(ref screenShot);
            //     return;
            // }
            uint[] pixelCounterData = new uint[m_RendererNum];
            // m_PixelCounterBuffer.GetData(pixelCounterData);
            logoutDataList.Clear();
            Mesh mesh;
            for (int i = 0; i < pixelCounterData.Length; i++)
            {
                uint flag = VisibleFlag[i];
                if (flag == 0) continue;
                
                RendererComponentData data = rendererComponentDatas[i];
                var renderer = data.renderer;
                if(renderer == null)
                    continue;
                mesh = data.m;
                // mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                // mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                if(mesh == null) 
                    continue;

                int vertexCount = mesh.vertexCount;
                int pixelCount = (int)pixelCounterData[i];
                if(vertexCount == 0) continue;
                float density = pixelCount > 0 ? (float)vertexCount / (float)pixelCount : float.MaxValue;
                string rendererHierarchyPath = VertexProfilerUtil.GetGameObjectNameFromRoots(renderer.transform);
                Color profilerColor = GetProfilerContentColor(density, out int thresholdLevel);
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
            // 如果需要输出到Excel才执行
            if (vp.NeedLogOutProfilerData)
            {
                // 写入Excel
                VertexProfilerEvent.CallLogoutToExcel(vp.EDisplayType, logoutDataList, screenShot);
                // 释放资源
                ReleaseRenderTexture(ref screenShot);
                vp.NeedLogOutProfilerData = false;
            }
        }
    }
}
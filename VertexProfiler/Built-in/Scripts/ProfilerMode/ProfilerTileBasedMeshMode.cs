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
    public class ProfilerTileBasedMeshMode : ProfilerModeBase
    {
        public ComputeShader CalculateVertexByTilesCS;
        
        private int CalculateVertexKernel = 1;
        private int CalculateVertexByTilesCSGroupX = 256;
        
        private Shader MeshPixelCalShader;
        private Material GammaCorrectionEffectMat;
        
        private bool initedRRCamera = false;
        private Camera rrCamera; // rr => Replace Rendering,用于Mesh类型的替换渲染
        
        private VertexProfilerJobs.J_Culling Job_Culling;
        private List<uint> VisibleFlag;
        private ComputeBuffer m_VertexCounterBuffer;
        private ComputeBuffer m_PixelCounterBuffer;
        
        private List<RendererComponentData> rendererComponentDatas;
        private RenderTexture m_TileProfilerRT;
        private RenderTexture m_RendererIdAndVertexCountRT;
        private RenderTexture screenShot;
        
        public ProfilerTileBasedMeshMode(VertexProfiler vp) : base(vp)
        {
            EdDisplayType = DisplayType.TileBasedMesh;
            
            MeshPixelCalShader = Shader.Find("VertexProfiler/MeshPixelCalShader");
            CalculateVertexByTilesCS = vp.CalculateVertexByTilesCS;
            // 特殊材质
            GammaCorrectionEffectMat = vp.GammaCorrectionEffectMat;
            
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 
                GraphicsFormat.R32G32_SFloat,  24, "_RendererIdAndVertexCountRT", ref m_RendererIdAndVertexCountRT);
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
                   && ApplyProfilerDataByPostEffectMat != null;
        }
        
        // 如果是使用tileBasedMesh统计，需要在正式渲染前释放m_PixelCounterBuffer，在设置渲染对象时所需要的m_TileProfilerRT为RandomWriteTarget
        public override void OnPreRender()
        {
            Graphics.ClearRandomWriteTargets();
            Graphics.SetRandomWriteTarget(3, m_TileProfilerRT);
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
                rrCamera.clearFlags = CameraClearFlags.SolidColor;
                rrCamera.SetTargetBuffers(m_RendererIdAndVertexCountRT.colorBuffer, m_RendererIdAndVertexCountRT.depthBuffer);
                rrCamera.enabled = false; // rr相机不需要启动，通过调度替换渲染来执行就行
                
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
            // todo GPU Instancing渲染也需要收集，后面加多一个处理 
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
            int tileCount = vp.TileNumX * vp.TileNumY;

            m_VertexCounterBuffer = new ComputeBuffer(m_RendererNum * tileCount, Marshal.SizeOf(typeof(uint)));
            m_VertexCounterBuffer.SetData(new uint[m_RendererNum * tileCount]);
            
            m_PixelCounterBuffer = new ComputeBuffer(m_RendererNum * tileCount, Marshal.SizeOf(typeof(uint)));
            m_PixelCounterBuffer.SetData(new uint[m_RendererNum * tileCount]);
            
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R32_UInt, 0, "m_TileProfilerRT", ref m_TileProfilerRT);

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
            CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._TileCount, tileCount);
            CalculateVertexByTilesCS.SetVector(VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));
            CalculateVertexByTilesCS.SetInt(VertexProfilerUtil._TileNumX, vp.TileNumX);
            CalculateVertexByTilesCS.SetMatrix(VertexProfilerUtil._UNITY_MATRIX_VP, m_vp);
            CalculateVertexByTilesCS.SetVector(VertexProfilerUtil._ScreenParams, new Vector4(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, 1.0f / vp.MainCamera.pixelWidth, 1.0f / vp.MainCamera.pixelHeight));
            CalculateVertexByTilesCS.SetBool(VertexProfilerUtil._UNITY_UV_STARTS_AT_TOP, SystemInfo.graphicsUVStartsAtTop);
            CalculateVertexByTilesCS.SetBuffer(CalculateVertexKernel, VertexProfilerUtil._VertexCounterBuffer, m_VertexCounterBuffer);
            
            Shader.SetGlobalInt(VertexProfilerUtil._TileWidth, vp.TileWidth);
            Shader.SetGlobalInt(VertexProfilerUtil._TileNumX, vp.TileNumX);
            Shader.SetGlobalInt(VertexProfilerUtil._TileCount, tileCount);
            Shader.SetGlobalVector(VertexProfilerUtil._TileParams2, new Vector4(1.0f / vp.TileWidth, 1.0f / vp.TileHeight, 1.0f / vp.TileNumX, 1.0f / vp.TileNumY));

            Graphics.SetRandomWriteTarget(4, m_PixelCounterBuffer);
            Shader.SetGlobalBuffer(VertexProfilerUtil._VertexCounterBuffer, m_VertexCounterBuffer);
            Shader.SetGlobalBuffer(VertexProfilerUtil._PixelCounterBuffer, m_PixelCounterBuffer);
            Shader.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            Shader.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            
            Shader.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            Shader.SetGlobalTexture(VertexProfilerUtil._RendererIdAndVertexCountRT, m_RendererIdAndVertexCountRT);
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
                        // meshBuffer?.Dispose();
                    }
                }
            }
            Profiler.EndSample();
            // todo GPU Instancing渲染也需要收集，后面加多一个处理 
            
            Profiler.BeginSample("Calculate Meshes Cover Pixels");
            {
                // 调度渲染：计算出每个参与渲染的Mesh所占用的像素信息
                rrCamera.RenderWithShader(MeshPixelCalShader, "");
            }
            Profiler.EndSample();
        }
        
        public override void Release()
        {
            ReleaseAllComputeBuffer();
            DestroyRRCamera();
            ReleaseRenderTexture(ref screenShot);
            ReleaseRenderTexture(ref m_RendererIdAndVertexCountRT);
        }

        public override void ReleaseAllComputeBuffer()
        {
            base.ReleaseAllComputeBuffer();
            // 确保解除buffer持有再释放buffer
            Graphics.ClearRandomWriteTargets();
            ReleaseComputeBuffer(ref m_VertexCounterBuffer);
            ReleaseComputeBuffer(ref m_PixelCounterBuffer);
            
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
            vp.StartCoroutineForProfiler(PullTileBasedMeshProfilerData());
        }
        private IEnumerator PullTileBasedMeshProfilerData()
        {
            if (m_VertexCounterBuffer == null || m_PixelCounterBuffer == null)
            {
                vp.NeedLogDataToProfilerWindow = false;
                vp.NeedLogOutProfilerData = false;
                ReleaseRenderTexture(ref screenShot);
                yield break;
            }
            // 拉取数据，当前帧进行
            int tileCount = vp.TileNumX * vp.TileNumY;
            int dataCount = m_RendererNum * tileCount;
            uint[] vertexCountData = new uint[dataCount];
            m_VertexCounterBuffer.GetData(vertexCountData);
            uint[] pixelCountData = new uint[dataCount];
            m_PixelCounterBuffer.GetData(pixelCountData);
            List<BatchProfilerDataContents> batchDataList = new List<BatchProfilerDataContents>();
            
            Mesh mesh;
            for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
            {
                List<ProfilerDataContents> profilerDataContentsList = new List<ProfilerDataContents>();
                int maxThresholdLevel = 0;
                float maxDensity = 0f;
                for (int rendererId = 0; rendererId < m_RendererNum; rendererId++)
                {
                    int bufferIndex = rendererId * tileCount + tileIndex;
                    int vertexCount = (int)vertexCountData[bufferIndex];
                    int pixelCount = (int)pixelCountData[bufferIndex];
                    if(vertexCount == 0 || pixelCount == 0) continue;
                    
                    RendererComponentData data = rendererComponentDatas[rendererId];
                    var renderer = data.renderer;
                    if(renderer == null)
                        continue;
                    mesh = data.m;
                    // mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                    // mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                    if(mesh == null) 
                        continue;

                    int meshVertexCount = mesh.vertexCount;
                    float density = pixelCount > 0 ? (float)vertexCount / (float)pixelCount : float.MaxValue;
                    maxDensity = pixelCount > 0 ? Mathf.Max(maxDensity, density) : maxDensity;
                    string rendererHierarchyPath = VertexProfilerUtil.GetGameObjectNameFromRoots(renderer.transform);
                    Color profilerColor = GetProfilerContentColor(density, out int thresholdLevel);
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
                Object.DestroyImmediate(screenShotWithGrids);
                ReleaseRenderTexture(ref screenShot);
                vp.NeedLogOutProfilerData = false;
            }
        }
    }
}


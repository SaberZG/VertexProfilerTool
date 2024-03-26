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
    public class ProfilerOnlyMeshMode : ProfilerModeBase
    {
        private Shader MeshPixelCalShader;
        private Material GammaCorrectionEffectMat;
        
        private bool initedRRCamera = false;
        private Camera rrCamera; // rr => Replace Rendering,用于Mesh类型的替换渲染

        private VertexProfilerJobs.J_Culling Job_Culling;
        private List<uint> VisibleFlag;
        private ComputeBuffer m_TileVerticesCountBuffer;
        
        private ComputeBuffer m_PixelCounterBuffer;
        
        private List<RendererComponentData> rendererComponentDatas;
        private RenderTexture m_RendererIdAndVertexCountRT;
        private RenderTexture screenShot;
        
        public ProfilerOnlyMeshMode(VertexProfiler vp) : base(vp)
        {
            EdDisplayType = DisplayType.OnlyMesh;
            
            MeshPixelCalShader = Shader.Find("VertexProfiler/MeshPixelCalShader");
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
                   && ApplyProfilerDataByPostEffectMat != null;
        }

        // 如果是使用mesh统计的方式，在开始渲染对象前需要释放GPU对Buffer的锁定持有，不然Shader内会拿不到Buffer数据
        public override void OnPreRender()
        {
            Graphics.ClearRandomWriteTargets();
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
                Mesh mesh;
                for (int i = 0; i < rendererComponentDatas.Count; i++)
                {
                    RendererComponentData data = rendererComponentDatas[i];
                    Renderer renderer = data.renderer;
                    
                    if(data.renderer == null)
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
                    RendererCuller.TryInitMaterialPropertyBlock(renderer, i, mesh.vertexCount);
                    m_RendererNum++;
                }
            }
        }
        
        public override void SetupConstantBufferData()
        {
            base.SetupConstantBufferData();

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
            
            m_PixelCounterBuffer = new ComputeBuffer(m_RendererNum, Marshal.SizeOf(typeof(uint)));
            m_PixelCounterBuffer.SetData(new uint[m_RendererNum]);
            Graphics.SetRandomWriteTarget(4, m_PixelCounterBuffer);
            Shader.SetGlobalBuffer(VertexProfilerUtil._PixelCounterBuffer, m_PixelCounterBuffer);
            Shader.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            Shader.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            Shader.SetGlobalTexture(VertexProfilerUtil._RendererIdAndVertexCountRT, m_RendererIdAndVertexCountRT);
        }

        public override void Dispatch()
        {
            Profiler.BeginSample("Calculate Meshes Cover Pixels");
            {
                // 调度渲染：计算出每个参与渲染的Mesh所占用的像素信息
                rrCamera.RenderWithShader(MeshPixelCalShader, "");
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
            ReleaseRenderTexture(ref m_RendererIdAndVertexCountRT);
        }

        public override void ReleaseAllComputeBuffer()
        {
            base.ReleaseAllComputeBuffer();
            // 确保解除buffer持有再释放buffer
            Graphics.ClearRandomWriteTargets();
            ReleaseComputeBuffer(ref m_TileVerticesCountBuffer);
            ReleaseComputeBuffer(ref m_PixelCounterBuffer);
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
            vp.StartCoroutineForProfiler(PullOnlyMeshProfilerData());
        }
        
        public IEnumerator PullOnlyMeshProfilerData()
        {
            if (m_PixelCounterBuffer == null)
            {
                vp.NeedLogDataToProfilerWindow = false;
                vp.NeedLogOutProfilerData = false;
                ReleaseRenderTexture(ref screenShot);
                yield break;
            }
            uint[] pixelCounterData = new uint[m_RendererNum];
            m_PixelCounterBuffer.GetData(pixelCounterData);
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
            //截屏需要等待渲染线程结束
            yield return new WaitForEndOfFrame();
            // 如果需要输出到Excel才执行
            if (vp.NeedLogOutProfilerData)
            {
                //初始化Texture2D, 大小可以根据需求更改
                Texture2D screenShotWithPostEffect = new Texture2D(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, TextureFormat.RGB24, false);
                screenShotWithPostEffect.name = "screenShotWithPostEffect";
                //读取屏幕像素信息并存储为纹理数据
                screenShotWithPostEffect.ReadPixels(new Rect(0, 0, vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight), 0, 0);
                //应用
                screenShotWithPostEffect.Apply();
                // 写入Excel
                VertexProfilerEvent.CallLogoutToExcel(vp.EDisplayType, logoutDataList, screenShot, screenShotWithPostEffect);
                // 释放资源
                ReleaseRenderTexture(ref screenShot);
                Object.DestroyImmediate(screenShotWithPostEffect);
                vp.NeedLogOutProfilerData = false;
            }
        }
    }
}

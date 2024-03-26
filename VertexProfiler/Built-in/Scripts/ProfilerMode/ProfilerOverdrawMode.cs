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
    public class ProfilerOverdrawMode : ProfilerModeBase
    {
        public ComputeShader GenerateProfilerRTCS;
        
        private Shader OverdrawCalculateShader;
        private Material GammaCorrectionEffectMat;

        private int GenerateProfilerKernel = 2;
        
        private bool initedRRCamera = false;
        private Camera rrCamera; // rr => Replace Rendering,用于Mesh类型的替换渲染

        private List<RendererComponentData> rendererComponentDatas;
        private RenderTexture m_OutputOverdrawRT; // 记录Overdraw情况的RT
        private RenderTexture m_TileProfilerRT;
        private RenderTexture screenShot;
        
        public ProfilerOverdrawMode(VertexProfiler vp) : base(vp)
        {
            EdDisplayType = DisplayType.Overdraw;

            GenerateProfilerRTCS = vp.GenerateProfilerRTCS;
            OverdrawCalculateShader = Shader.Find("VertexProfiler/OverdrawCalculateShader");
            GammaCorrectionEffectMat = vp.GammaCorrectionEffectMat;
            
            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R32_SFloat,  24, "m_OutputOverdrawRT", ref m_OutputOverdrawRT);
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
                   && OverdrawCalculateShader != null
                   && vp.ApplyProfilerDataByPostEffectMat != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
            // 简单模式默认使用硬编码的阈值，不做处理
            if (EProfilerType == ProfilerType.Simple) return;
            
            VertexProfilerUtil.OverdrawDensitySetting = new List<int>(VertexProfilerUtil.DefaultOverdrawDensitySetting);
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
                    foreach (int v in VertexProfilerUtil.SimpleModeOverdrawDensitySetting)
                    {
                        DensityList.Add(v);
                    }
                }
                else if (EProfilerType == ProfilerType.Detail)
                {
                    foreach (int v in VertexProfilerUtil.OverdrawDensitySetting)
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
                    float threshold = DensityList[i];
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
                rrCamera.SetTargetBuffers(m_OutputOverdrawRT.colorBuffer, m_OutputOverdrawRT.depthBuffer);
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

            GetTemporaryRT(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm,  0, "m_TileProfilerRT", ref m_TileProfilerRT);
            
            GenerateProfilerRTCS.SetInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            GenerateProfilerRTCS.SetBuffer(GenerateProfilerKernel, VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            GenerateProfilerRTCS.SetTexture(GenerateProfilerKernel, VertexProfilerUtil._OutputOverdrawRT, m_OutputOverdrawRT);
            GenerateProfilerRTCS.SetTexture(GenerateProfilerKernel, VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            
            Shader.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            Shader.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            Shader.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
        }

        public override void Dispatch()
        {
            Profiler.BeginSample("Calculate Meshes Cover Pixels");
            {
                // 调度渲染：计算出每个参与渲染的Mesh所占用的像素信息
                rrCamera.RenderWithShader(OverdrawCalculateShader, "");
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
            ReleaseRenderTexture(ref m_OutputOverdrawRT);
        }

        public override void ReleaseAllComputeBuffer()
        {
            base.ReleaseAllComputeBuffer();

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
            
        }
    }
}
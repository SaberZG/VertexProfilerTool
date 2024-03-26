using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProfilingScope = UnityEngine.Rendering.ProfilingScope;

namespace VertexProfilerTool
{
    public class VertexProfilerOverdrawRF : ScriptableRendererFeature
    {
        VertexProfilerModeOverdrawRenderPass m_ScriptablePass;
        VertexProfilerPostEffectRenderPass m_PostEffectPass;
        
        public override void Create()
        {
            m_ScriptablePass = new VertexProfilerModeOverdrawRenderPass();
            m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            
            m_PostEffectPass = new VertexProfilerPostEffectRenderPass();
            m_PostEffectPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            
            m_ScriptablePass.Setup();
            renderer.EnqueuePass(m_ScriptablePass);
            renderer.EnqueuePass(m_PostEffectPass);
        }

        private void OnDisable()
        {
            m_ScriptablePass?.OnDisable();
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_ScriptablePass?.OnDisable();
            }
        }
    }
    [System.Serializable]
    public class VertexProfilerModeOverdrawRenderPass : VertexProfilerModeBaseRenderPass
    {
        private Material OverdrawCalculateMat;
        public ComputeShader GenerateProfilerRTCS;
        
        private int GenerateProfilerKernel = 2;

        private RTHandle m_TileProfilerRT;
        private RTHandle m_OutputOverdrawDepthRT;
        private RTHandle m_OutputOverdrawRT;
        
        // 原生渲染所需属性
        List<ShaderTagId> URPOverdrawCalculateTagId;
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_renderStateBlock;
        ProfilingSampler m_ProfilingSampler;
        
        public VertexProfilerModeOverdrawRenderPass() : base()
        {
            EDisplayType = DisplayType.Overdraw;

            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, -1);
            m_renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_ProfilingSampler = new ProfilingSampler("PixelCalShader");
        }

        public void Setup()
        {
            // 不能在构造函数初始化的部分在这创建
            URPOverdrawCalculateTagId = new List<ShaderTagId>() {new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly")};
            if (vp != null)
            {
                vp.ProfilerMode = this;
                GenerateProfilerRTCS = vp.GenerateProfilerRTCS;
                OverdrawCalculateMat = new Material(Shader.Find("VertexProfiler/URPOverdrawCalculateShader"));
            };
        }

        public override void OnDisable()
        {
            base.OnDisable();
            
            ReleaseRTHandle(ref m_TileProfilerRT);
            ReleaseRTHandle(ref m_OutputOverdrawDepthRT);
            ReleaseRTHandle(ref m_OutputOverdrawRT);
            
            if (vp != null && vp.ProfilerMode == this)
            {
                vp.ProfilerMode = null;
            }
            
            ReleaseAllComputeBuffer();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            EDisplayType = DisplayType.Overdraw;
        }

        public override bool CheckProfilerEnabled()
        {
            return vp != null 
                   && vp.EnableProfiler
                   && GenerateProfilerRTCS != null
                   && OverdrawCalculateMat != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
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

            ReAllocTileProfilerRT(GraphicsFormat.None, GraphicsFormat.D24_UNorm, FilterMode.Point, ref m_OutputOverdrawDepthRT, "m_OutputOverdrawDepthRT", false);
            ReAllocTileProfilerRT(GraphicsFormat.R32G32_SFloat, GraphicsFormat.None, FilterMode.Point, ref m_OutputOverdrawRT, "m_OutputOverdrawRT");
            ReAllocTileProfilerRT(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, FilterMode.Point, ref m_TileProfilerRT, "m_TileProfilerRT");
            
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetComputeBufferParam(GenerateProfilerRTCS, GenerateProfilerKernel, VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetComputeTextureParam(GenerateProfilerRTCS, GenerateProfilerKernel, VertexProfilerUtil._OutputOverdrawRT, m_OutputOverdrawRT);
            cmd.SetComputeTextureParam(GenerateProfilerRTCS, GenerateProfilerKernel, VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            
            cmd.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public override void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 切换颜色缓冲，预渲染一次当前overdraw的情况
            CoreUtils.SetRenderTarget(cmd, m_OutputOverdrawRT, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                m_OutputOverdrawDepthRT,
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            DrawObjects(cmd, ref URPOverdrawCalculateTagId, ref context, ref renderingData);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            // 还原原始的颜色缓冲
            CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                renderingData.cameraData.renderer.cameraDepthTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            using (new ProfilingScope(cmd, new ProfilingSampler("Generate Profiler RT")))
            {
                cmd.DispatchCompute(GenerateProfilerRTCS, GenerateProfilerKernel, Mathf.CeilToInt((float)m_TileProfilerRT.rt.width / 16), Mathf.CeilToInt((float)m_TileProfilerRT.rt.height / 16), 1);
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        private void DrawObjects(CommandBuffer cmd, ref List<ShaderTagId> shaderTagIds, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags; 
                
                DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortFlags);
                drawSettings.overrideMaterial = OverdrawCalculateMat;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings, ref m_renderStateBlock);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
}

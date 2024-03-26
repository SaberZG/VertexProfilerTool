using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VertexProfilerTool
{
    [System.Serializable]
    public class VertexProfilerModeBaseRenderPass : ScriptableRenderPass
    {
        public static VertexProfilerURP vp;
        public ProfilerType EProfilerType = ProfilerType.Detail;
        public DisplayType EDisplayType = DisplayType.TileBasedMesh;
        public CullMode ECullMode = CullMode.Back;
        public List<int> DensityList = new List<int>();
        public bool NeedSyncColorRangeSetting = true;
        
        public static ComputeBuffer m_VertexCounterBuffer;
        public static ComputeBuffer m_PixelCounterBuffer;
        public static ComputeBuffer m_TileVerticesCountBuffer;
        public static List<RendererComponentData> rendererComponentDatas;
        
        public static List<uint> VisibleFlag;
        public static ColorRangeSetting[] m_ColorRangeSettings;
        // Tile Type Buffers
        internal ComputeBuffer m_ColorRangeSettingBuffer;
        internal int m_RendererNum;
        internal List<RendererBoundsData> m_RendererBoundsData = new List<RendererBoundsData>();
        internal List<Matrix4x4> m_RendererLocalToWorldMatrix = new List<Matrix4x4>();
    
        public VertexProfilerModeBaseRenderPass()
        {
            
        }

        public virtual void OnDisable()
        {
            ReleaseAllComputeBuffer();
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            
            ReleaseAllComputeBuffer();
            if (!CheckProfilerEnabled())
            {
                Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 0);
                RendererCuller.RevertAllReplaceShader(rendererComponentDatas);
                return;
            }

            InitRenderers();
            if (m_RendererNum <= 0)
            {
                Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 0);
                RendererCuller.RevertAllReplaceShader(rendererComponentDatas);
                return;
            }
        
            // 更新颜色阈值到GPU
            CheckColorRangeData();
            CommandBuffer cmd = CommandBufferPool.Get();
            // 设置静态Buffer到GPU
            SetupConstantBufferData(cmd, ref context);
            // 调度预渲染统计信息
            Dispatch(cmd, ref context, ref renderingData);
            CommandBufferPool.Release(cmd);
        }


        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
        
        public virtual bool CheckProfilerEnabled()
        {
            return false;
        }
        public virtual void UseDefaultColorRangeSetting()
        {
            
        }

        public virtual void CheckColorRangeData(bool forceReload = false)
        {
            
        }

        public virtual void InitRenderers()
        {
            
        }

        public virtual void SetupConstantBufferData(CommandBuffer cmd, ref ScriptableRenderContext context)
        {
            cmd.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 1);
            cmd.SetGlobalInt(VertexProfilerUtil._DisplayType, (int)vp.EDisplayType);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            if (m_ColorRangeSettings != null && m_ColorRangeSettings.Length > 0)
            {
                m_ColorRangeSettingBuffer = new ComputeBuffer(m_ColorRangeSettings.Length, Marshal.SizeOf(typeof(ColorRangeSetting)));
                m_ColorRangeSettingBuffer.SetData(m_ColorRangeSettings);
            }
        }

        public virtual void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            
        }
        
        public void ChangeProfilerType(ProfilerType profilerType)
        {
            if (EProfilerType != profilerType)
            {
                EProfilerType = profilerType;
                CheckColorRangeData(true);
            }
        }
        
        public virtual void ReleaseAllComputeBuffer()
        {
            ReleaseComputeBuffer(ref m_ColorRangeSettingBuffer);
            
            ReleaseComputeBuffer(ref m_VertexCounterBuffer);
            ReleaseComputeBuffer(ref m_PixelCounterBuffer);
            ReleaseComputeBuffer(ref m_TileVerticesCountBuffer);
        }

        internal static void ReleaseComputeBuffer(ref ComputeBuffer _buffer)
        {
            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }
        }

        internal static void ReleaseRTHandle(ref RTHandle handle)
        {
            if (handle != null)
            {
                handle.Release();
                handle = null;
            }
        }
        
        internal void ReAllocTileProfilerRT(GraphicsFormat colorFormat, GraphicsFormat depthFormat, FilterMode filterMode, ref RTHandle handle, string handleName = "", bool randomWrite = true)
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, colorFormat, depthFormat, 0);
            desc.enableRandomWrite = randomWrite;
            VertexProfilerUtil.ReAllocRTIfNeeded(ref handle, desc, filterMode, TextureWrapMode.Clamp, false, name: handleName);
        }

        public virtual void LogoutProfilerData()
        {
            
        }
        
        public static Color GetProfilerContentColor(float content, out int level)
        {
            Color color = Color.white;
            level = 0;
            if (m_ColorRangeSettings != null && m_ColorRangeSettings.Length > 0)
            {
                for (int i = 0; i < m_ColorRangeSettings.Length; i++)
                {
                    var setting = m_ColorRangeSettings[i];
                    if (setting.threshold < content)
                    {
                        color = setting.color;
                        level = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return color;
        }
    }
    
    // 用于处理后处理
    [System.Serializable]
    public class VertexProfilerPostEffectRenderPass : ScriptableRenderPass
    {
        private VertexProfilerURP vp;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            // 进入后处理阶段，使用m_TileProfilerRT之前需要执行一次释放
            CommandBuffer cmd = CommandBufferPool.Get();
            cmd.ClearRandomWriteTargets();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            vp = VertexProfilerModeBaseRenderPass.vp;
            if (vp == null) return;
            
            Blit(cmd, ref renderingData, vp.ApplyProfilerDataByPostEffectMat);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }
    }

    [System.Serializable]
    public class VertexProfilerLogBaseRenderPass : ScriptableRenderPass
    {
        public static VertexProfilerURP vp;
        public List<ProfilerDataContents> logoutDataList = new List<ProfilerDataContents>();
        
        public virtual void OnDisable()
        {
            
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (vp == null || !vp.EnableProfiler) return;
            
            CommandBuffer cmd = CommandBufferPool.Get();
            // 处理log操作
            if (vp.NeedLogOutProfilerData || vp.NeedLogDataToProfilerWindow)
            {
                DispatchScreenShotAndReadBack(cmd, ref context);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public virtual void DispatchScreenShotAndReadBack(CommandBuffer cmd, ref ScriptableRenderContext context)
        {
            
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
    }
}



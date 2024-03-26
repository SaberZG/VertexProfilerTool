using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    [System.Serializable][ExecuteInEditMode]
    public class ProfilerModeBase
    {
        public ProfilerType EProfilerType;
        public DisplayType EdDisplayType;
        public CullMode ECullMode = CullMode.Back;
        [NonReorderable]
        public List<int> DensityList = new List<int>();
        public bool NeedSyncColorRangeSetting = true;
        public List<ProfilerDataContents> logoutDataList = new List<ProfilerDataContents>();
        
        internal ColorRangeSetting[] m_ColorRangeSettings;
        internal Material ApplyProfilerDataByPostEffectMat;
        // Tile Type Buffers
        internal ComputeBuffer m_ColorRangeSettingBuffer;
        internal int m_RendererNum;
        internal List<RendererBoundsData> m_RendererBoundsData = new List<RendererBoundsData>();
        internal List<Matrix4x4> m_RendererLocalToWorldMatrix = new List<Matrix4x4>();
        internal VertexProfiler vp;
        public ProfilerModeBase(VertexProfiler vp)
        {
            this.vp = vp;
            EProfilerType = vp.EProfilerType;
            ApplyProfilerDataByPostEffectMat = vp.ApplyProfilerDataByPostEffectMat;
        }
        
        /// <summary>
        /// 在预剔除事件函数前调度，统计占用信息
        /// </summary>
        public void OnPreCull()
        {
            ReleaseAllComputeBuffer();
            if (!CheckProfilerEnabled())
            {
                Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 0);
                return;
            }

            InitRenderers();
            if (m_RendererNum <= 0)
            {
                Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 0);
                return;
            }
            // 检查是否需要更新shader
            // VertexProfilerEvent.CallTriggerRegenerateReplaceShader();
            // 更新颜色阈值到GPU
            CheckColorRangeData();
            // 设置静态Buffer到GPU
            SetupConstantBufferData();
            // 调度预渲染统计信息
            Dispatch();
        }

        public virtual bool CheckProfilerEnabled()
        {
            return true;
        }

        /// <summary>
        /// 根据不同统计类型的需要，在正式替换渲染前进行某些buffer等的操作
        /// </summary>
        public virtual void OnPreRender()
        {
            
        }

        public virtual void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            Graphics.Blit(src, dest);
        }

        public void ChangeProfilerType(ProfilerType profilerType)
        {
            if (EProfilerType != profilerType)
            {
                EProfilerType = profilerType;
                CheckColorRangeData(true);
            }
        }
        /// <summary>
        /// 将颜色阈值设置重新改回默认设置
        /// </summary>
        public virtual void UseDefaultColorRangeSetting()
        {
            
        }
        
        public virtual void CheckColorRangeData(bool forceReload = false)
        {
            
        }

        public virtual void SetupConstantBufferData()
        {
            Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 1);
            Shader.SetGlobalInt(VertexProfilerUtil._DisplayType, (int)vp.EDisplayType);

            if (m_ColorRangeSettings != null && m_ColorRangeSettings.Length > 0)
            {
                m_ColorRangeSettingBuffer = new ComputeBuffer(m_ColorRangeSettings.Length, Marshal.SizeOf(typeof(ColorRangeSetting)));
                m_ColorRangeSettingBuffer.SetData(m_ColorRangeSettings);
            }
        }

        public virtual void InitRenderers()
        {
            
        }
        
        public virtual void Dispatch()
        {
            
        }
        
        // 输出Excel内容部分整理
        public virtual void LogoutProfilerData()
        {
            
        }
        
        // 销毁释放
        public virtual void Release()
        {

        }

        public virtual void ReleaseAllComputeBuffer()
        {
            ReleaseComputeBuffer(ref m_ColorRangeSettingBuffer);
        }

        internal static void ReleaseComputeBuffer(ref ComputeBuffer _buffer)
        {
            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }
        }

        internal static void GetTemporaryRT(int width, int height, GraphicsFormat colorFormat, int depthBits, string name, ref RenderTexture rt)
        {
            ReleaseRenderTexture(ref rt);
            rt = RenderTexture.GetTemporary(width, height, depthBits, colorFormat);
            rt.name = name;
            rt.enableRandomWrite = true;
            rt.Create();
        }
        internal static void ReleaseRenderTexture(ref RenderTexture rt)
        {
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
                rt = null;
            }
        }
        
        public Color GetProfilerContentColor(float content, out int level)
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
}


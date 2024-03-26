using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;


namespace VertexProfilerTool
{
    [ExecuteInEditMode]
    public class VertexProfiler : VertexProfilerBase
    {
        public VertexProfiler()
        {
            isURP = false;
        }
        
        public ProfilerModeBase ProfilerMode = null;

        private void Awake()
        {
            VertexProfilerReplaceShader = Shader.Find("VertexProfiler/VertexProfilerReplaceShader");
            ApplyProfilerDataByPostEffectShader = Shader.Find("VertexProfiler/ApplyProfilerDataByPostEffect");
            GammaCorrectionShader = Shader.Find("VertexProfiler/GammaCorrection");
            ApplyProfilerDataByPostEffectMat = new Material(ApplyProfilerDataByPostEffectShader);
            GammaCorrectionEffectMat = new Material(GammaCorrectionShader);
            
            InitKeyword();
            InitUITile();
        }

        void Start()
        {
            NeedUpdateUITileGrid = true;
            InitCamera();
            CheckProfilerMode(true);

            // if (MainCamera != null)
            // {
            //     if(EnableProfiler)
            //         MainCamera.SetReplacementShader(VertexProfilerReplaceShader, "VertexProfilerTag");
            //     else
            //         MainCamera.ResetReplacementShader();
            // }
        }
        
#if UNITY_EDITOR
        private new void Update()
        {
            base.Update();
            
            if (VertexProfilerUtil.ForceReloadProfilerModeAfterScriptCompile)
            {
                CheckProfilerMode(true);
                VertexProfilerUtil.ForceReloadProfilerModeAfterScriptCompile = false;
            }
        }
#endif

        #region public function

        public void StartProfiler()
        {
            EnableProfiler = true;
            CheckProfilerMode();
            // if (MainCamera != null)
            // {
            //     MainCamera.SetReplacementShader(VertexProfilerReplaceShader, "VertexProfilerTag");
            // }  
            CheckShowUIGrid();
        }

        public void StopProfiler()
        {
            EnableProfiler = false;
            CheckProfilerMode();
            // if (MainCamera != null)
            // {
            //     MainCamera.ResetReplacementShader();
            // }

            if (tileCanvas != null)
            {
                tileCanvas.gameObject.SetActive(false);
            }
        }
        
        /// <summary>
        /// 检查当前的调试类型是否发生变化
        /// </summary>
        public void CheckProfilerMode(bool forceInit = false)
        {
            if (ProfilerMode == null || ProfilerMode.EdDisplayType != EDisplayType || forceInit)
            {
                RendererCuller.ClearCacheMaterialPropertyBlock();
                ProfilerMode?.Release();
                // 根据查看类型不同切换状态
                switch (EDisplayType)
                {
                    case DisplayType.OnlyTile:
                        ProfilerMode = new ProfilerOnlyTileMode(this);
                        break;
                    case DisplayType.OnlyMesh:
                        ProfilerMode = new ProfilerOnlyMeshMode(this);
                        break;
                    case DisplayType.TileBasedMesh:
                        ProfilerMode = new ProfilerTileBasedMeshMode(this);
                        break;
                    case DisplayType.MeshHeatMap:
                        ProfilerMode = new ProfilerMeshHeatMapMode(this);
                        break;
                    case DisplayType.Overdraw:
                        ProfilerMode = new ProfilerOverdrawMode(this);
                        break;
                }
            }

            CheckShowUIGrid();
        }

        public void ChangeProfilerType(int index)
        {
            EProfilerType = (ProfilerType)index;
            ProfilerMode?.ChangeProfilerType(EProfilerType);
        }
        #endregion

        #region EventFunction
        // 调度入口，通过Unity的事件函数来驱动，保证正式渲染前就有数据了
        private void OnPreCull()
        {
            if (!EnableProfiler) return;
            
            ProfilerMode?.OnPreCull();

            if (EUpdateType == UpdateType.Once)
                NeedRecollectRenderers = false;
            else
                NeedRecollectRenderers = true;
        }
        
        private void OnPreRender()
        {
            if (!EnableProfiler) return;
            
            ProfilerMode?.OnPreRender();
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (!EnableProfiler)
            {
                Graphics.Blit(src, dest);
                return;
            }
            
            ProfilerMode?.OnRenderImage(src, dest);
            // 如果需要输出Excel或ProfilerWindow需要持续获取当前的数据则执行数据抽取
            if (NeedLogOutProfilerData || NeedLogDataToProfilerWindow) 
            {
                // 根据不同的数据输出不同的性能报告
                ProfilerMode?.LogoutProfilerData();
            }
        }
        #endregion
        
        
        #region Log out
        public void StartCoroutineForProfiler(IEnumerator routine)
        {
            StartCoroutine(routine);
        }
        #endregion

        #region Recycle

        void OnDisable()
        {
            ProfilerMode?.Release();
            ProfilerMode = null;
        }
        private new void OnDestroy()
        {
            ProfilerMode?.Release();
            ProfilerMode = null;
            base.OnDestroy();
        }

        #endregion
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VertexProfilerTool
{
    [ExecuteInEditMode]
    public class VertexProfilerURP : VertexProfilerBase
    {
        /// <summary>
        /// 当前项目使用的管线资产对象
        /// </summary>
        public UniversalRenderPipelineAsset defaultPipelineAsset;
        /// <summary>
        /// VertexProfiler的渲染管线资产对象
        /// </summary>
        public UniversalRenderPipelineAsset vpPipelineAsset;

        public VertexProfilerURP()
        {
            isURP = true;
            VertexProfilerModeBaseRenderPass.vp = this;
            VertexProfilerLogBaseRenderPass.vp = this;
        }
        
        public VertexProfilerModeBaseRenderPass ProfilerMode;
        public VertexProfilerLogBaseRenderPass LogMode;
        public Shader MeshPixelCalShader;
        private void Awake()
        {
            VertexProfilerReplaceShader = Shader.Find("VertexProfiler/URPVertexProfilerReplaceShader");
            MeshPixelCalShader = Shader.Find("VertexProfiler/URPMeshPixelCalShader");
            MeshPixelCalMat = new Material(MeshPixelCalShader);
            ApplyProfilerDataByPostEffectShader = Shader.Find("VertexProfiler/URPApplyProfilerDataByPostEffect");
            ApplyProfilerDataByPostEffectMat = new Material(ApplyProfilerDataByPostEffectShader);
            GammaCorrectionShader = Shader.Find("VertexProfiler/URPGammaCorrection");
            GammaCorrectionEffectMat = new Material(GammaCorrectionShader);
            
            InitKeyword();
            InitUITile();
        }

        void Start()
        {
            NeedUpdateUITileGrid = true;
            InitCamera();
            CheckShowUIGrid();
        }
        private new void Update()
        {
            base.Update();
            if (VertexProfilerUtil.ForceReloadProfilerModeAfterScriptCompile)
            {
                CheckProfilerMode(true);
                VertexProfilerUtil.ForceReloadProfilerModeAfterScriptCompile = false;
            }
        }

        #region public function

        public void StartProfiler()
        {
            EnableProfiler = true;
            defaultPipelineAsset = defaultPipelineAsset == null
                ? (UniversalRenderPipelineAsset)GraphicsSettings.renderPipelineAsset
                : defaultPipelineAsset;
            if (vpPipelineAsset != null)
            {
                GraphicsSettings.renderPipelineAsset = vpPipelineAsset;
            }
            CheckShowUIGrid();
        }

        public void StopProfiler()
        {
            EnableProfiler = false;
            CheckShowUIGrid();
            if (defaultPipelineAsset != null)
            {
                GraphicsSettings.renderPipelineAsset = defaultPipelineAsset;
            }
        }
        
        public void ChangeProfilerType(int index)
        {
            EProfilerType = (ProfilerType)index;
            ProfilerMode?.ChangeProfilerType(EProfilerType);
        }

        public void CheckProfilerMode(bool forceInit = false)
        {
            if (MainCamera == null) return;
            
            if (ProfilerMode == null || ProfilerMode.EDisplayType != EDisplayType || forceInit)
            {
                UniversalAdditionalCameraData cameraData = MainCamera.GetUniversalAdditionalCameraData();
                cameraData.SetRenderer((int)EDisplayType);
            }
            CheckShowUIGrid();
        }
        
        #endregion
        
        #region Log out
        public void StartCoroutineForProfiler(IEnumerator routine)
        {
            StartCoroutine(routine);
        }
        #endregion

        #region Recycle

        private new void OnDestroy()
        {
            base.OnDestroy();
            VertexProfilerModeBaseRenderPass.vp = null;
            VertexProfilerLogBaseRenderPass.vp = null;
        }

        #endregion
    }
}

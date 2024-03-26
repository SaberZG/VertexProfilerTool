using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace VertexProfilerTool
{
    public class VertexProfilerBase : MonoBehaviour
    {
        public bool isURP = false;
        public Camera MainCamera;
        public bool SyncSceneCameraToMainCamera = false;
        [Range(32, 128)]
        public int TileWidth = 100;
        [Range(32, 128)]
        public int TileHeight = 100;

        // 热力图参数
        [Range(1, 8)]
        public int HeatMapRange = 2;
        [Range(1, 5)]
        public int HeatMapStep = 1;
        public int HeatMapOffsetCount
        {
            get
            {
                return HeatMapRange * HeatMapRange + (HeatMapRange + 1) * (HeatMapRange + 1);
            }
        }

        public Gradient HeatMapGradient;
        [Range(0.0f, 0.99f)]
        public float HeatMapRampMin = 0.0f;
        [Range(0.01f, 1.0f)]
        public float HeatMapRampMax = 1.0f;
        
        public ComputeShader CalculateVertexByTilesCS;
        public ComputeShader GenerateProfilerRTCS;
        public Texture2D HeatMapTex;
        public bool EnableProfiler = true;
        public ProfilerType EProfilerType = ProfilerType.Detail;
        public UpdateType EUpdateType = UpdateType.EveryFrame;
        public DisplayType EDisplayType = DisplayType.OnlyTile;
        public bool NeedRecollectRenderers = true;

        internal Shader VertexProfilerReplaceShader;
        internal Shader ApplyProfilerDataByPostEffectShader;
        internal Shader GammaCorrectionShader;
        public Material ApplyProfilerDataByPostEffectMat;
        public Material MeshPixelCalMat;
        public Material GammaCorrectionEffectMat;
        
        public int TileNumX = 1;
        public int TileNumY = 1;
        
        public GlobalKeyword _USE_ONLYTILE_PROFILER;
        public GlobalKeyword _USE_ONLYMESH_PROFILER;
        public GlobalKeyword _USE_TILEBASEDMESH_PROFILER; 
        
        // log 
        public GameObject GoUITile;
        public bool NeedLogOutProfilerData = false;
        public bool NeedUpdateUITileGrid = true;
        [HideInInspector]public bool NeedLogDataToProfilerWindow = false;
        [HideInInspector]public int LastLogFrameCount = 0;

        public bool HideGoTUITile = false;
        public bool HideTileNum = false;
        internal List<UITile> GoUITileList = new List<UITile>();
        internal Canvas tileCanvas;
        
        #if UNITY_EDITOR
        internal void OnEnable()
        {
            EditorApplication.update += EditorSyncCamera;
        }

        internal void OnDisable()
        {
            EditorApplication.update -= EditorSyncCamera;
        }

        internal void InitKeyword()
        {
            _USE_ONLYTILE_PROFILER = GlobalKeyword.Create("_USE_ONLYTILE_PROFILER");
            _USE_ONLYMESH_PROFILER = GlobalKeyword.Create("_USE_ONLYMESH_PROFILER");
            _USE_TILEBASEDMESH_PROFILER = GlobalKeyword.Create("_USE_TILEBASEDMESH_PROFILER");
        }
        /// <summary>
        /// 创建Canvas和EventSystem
        /// </summary>
        internal void InitUITile()
        {
            #if UNITY_EDITOR
            // 尝试查找场景tile grid画布对象
            GameObject go = GameObject.Find("TileGridCanvas");
            if (go == null)
            {
                var AllCanvas = FindObjectsOfType<Canvas>(true);
                foreach (var canvas in AllCanvas)
                {
                    if (canvas.name.Equals("TileGridCanvas"))
                    {
                        go = canvas.gameObject;
                        break;
                    }
                }
            }
            if (go == null)
            {
                go = new GameObject("TileGridCanvas");
                go.hideFlags = HideFlags.None;
                go.layer = LayerMask.NameToLayer("UI");
            }

            tileCanvas = go.GetComponent<Canvas>();
            if (tileCanvas == null)
            {
                tileCanvas = go.AddComponent<Canvas>();
                tileCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            if (go.GetComponent<CanvasScaler>() == null)
            {
                go.AddComponent<CanvasScaler>();
            }
            if (go.GetComponent<GraphicRaycaster>() == null)
            {
                go.AddComponent<GraphicRaycaster>();
            }
            var esys = FindObjectOfType<EventSystem>();
            if (esys == null)
            {
                var eventSystem = new GameObject("EventSystem");
                GameObjectUtility.SetParentAndAlign(eventSystem, null);
                esys = eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }
            #endif
        }
        /// <summary>
        /// 将Scene视图相机的位置和旋转同步到主摄像机(编辑器模式)
        /// </summary>
        internal void EditorSyncCamera()
        {
            // 将Scene视图相机的位置和旋转同步到主摄像机
            if (SyncSceneCameraToMainCamera && !Application.isPlaying)
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null && MainCamera != null)
                {
                    MainCamera.transform.rotation = sceneView.rotation;
                    MainCamera.transform.position = sceneView.pivot - MainCamera.transform.forward * sceneView.cameraDistance;
                }
            }
        }
        #endif
        
        /// <summary>
        /// 将Scene视图相机的位置和旋转同步到主摄像机
        /// </summary>
        internal void SyncCamera()
        {
            // 将Scene视图相机的位置和旋转同步到主摄像机
            if (SyncSceneCameraToMainCamera && Application.isPlaying)
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null && MainCamera != null)
                {
                    MainCamera.transform.rotation = sceneView.rotation;
                    MainCamera.transform.position = sceneView.pivot - MainCamera.transform.forward * sceneView.cameraDistance;
                }
            }
        }
        internal void InitCamera()
        {
            if (MainCamera == null)
            {
                MainCamera = Camera.main;
            }
        }
        internal void Update()
        {
            // 将Scene视图相机的位置和旋转同步到主摄像机
            SyncCamera();
            // 同步UI网格
            if (NeedUpdateUITileGrid)
            {
                UpdateGoTileGrid();
            }
        }
        
        #region UI
        public void CheckShowUIGrid()
        {
            // 检查是否需要显示uiTile
            bool showCanvas = EDisplayType != DisplayType.OnlyMesh
                              && EDisplayType != DisplayType.MeshHeatMap 
                              && EDisplayType != DisplayType.Overdraw 
                              && !HideGoTUITile;
            if (tileCanvas != null && tileCanvas.gameObject.activeSelf != showCanvas)
            {
                tileCanvas.gameObject.SetActive(showCanvas);
            }
        }

        public void SetTileNumShow()
        {
            for (int i = 0; i < GoUITileList.Count; i++)
            {
                var uiTile = GoUITileList[i];
                if (uiTile)
                {
                    uiTile.SetTileNumActive(!HideTileNum);
                }
            }
        }
        internal void UpdateGoTileGrid()
        {
            if (EDisplayType == DisplayType.OnlyMesh) return;
            
            TileNumX = Mathf.CeilToInt((float)MainCamera.pixelWidth / (float)TileWidth);
            TileNumY = Mathf.CeilToInt((float)MainCamera.pixelHeight / (float)TileHeight);
            
            int needNum = TileNumX * TileNumY;
            if (GoUITileList.Count < needNum)
            {
                for (int i = GoUITileList.Count; i < needNum; i++)
                {
                    // 先尝试获取，再实例化
                    var trans = tileCanvas.transform.Find("UITile" + i);
                    GameObject go = trans != null ? trans.gameObject : Instantiate(GoUITile, Vector3.zero, Quaternion.identity, tileCanvas.transform);
                    UITile uitile = go.GetComponent<UITile>();
                    GoUITileList.Add(uitile);
                }
            }

            for (int i = 0; i < GoUITileList.Count; i++)
            {
                var uitile = GoUITileList[i];
                if (i < needNum)
                {
                    uitile.SetData(TileWidth, TileHeight, TileNumX, i);
                }
                uitile.SetActive(i < needNum);
                uitile.SetTileNumActive(!HideTileNum);
            }

            NeedUpdateUITileGrid = false;
        }
        #endregion
        #region Recycle
        internal void OnDestroy()
        {
            if (ApplyProfilerDataByPostEffectMat != null)
            {
                DestroyImmediate(ApplyProfilerDataByPostEffectMat);
                ApplyProfilerDataByPostEffectMat = null;
            }
            if (GammaCorrectionEffectMat != null)
            {
                DestroyImmediate(GammaCorrectionEffectMat);
                GammaCorrectionEffectMat = null;
            }

            for (int i = GoUITileList.Count - 1; i >= 0; i--)
            {
                DestroyImmediate(GoUITileList[i]);
            }
            GoUITileList.Clear();
        }
        #endregion
    }
}
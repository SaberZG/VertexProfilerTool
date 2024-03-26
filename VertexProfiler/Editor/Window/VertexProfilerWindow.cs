using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace VertexProfilerTool
{
    public class VertexProfilerWindow : EditorWindow
    {
        private string[] TextureExtensions = new string[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };
        // 所需的ComputeShader路径
        private string ComputeShaderResPath = "Assets/VertexProfiler/ComputeShader/";
        private string UITIleGoResPath = "Assets/VertexProfiler/TestMeshes/";
        private string HeatMapResPath = "Assets/VertexProfiler/Textures/";
        private string TempHeatMapName = "TempHeatMapTex";

        /// <summary>
        /// 基类
        /// </summary>
        private VertexProfilerBase vertexProfilerBase;
        /// <summary>
        /// 内置管线版本
        /// </summary>
        private VertexProfiler vertexProfiler;
        /// <summary>
        /// URP版本
        /// </summary>
        private VertexProfilerURP vertexProfilerURP;

        private bool urpNameSpaceExist;
        private bool urpAssetExist;
        
        
        /// <summary>
        /// 当成功获取到vertexProfiler后设置为true
        /// </summary>
        private bool fullyInited;
        
        private ComputeShader CalculateVertexByTilesCS;
        private ComputeShader GenerateProfilerRTCS;
        private GameObject GoUITile;
        private Texture2D HeatmapDefaultTexture;
        private List<string> ResErrorLogContent = new List<string>();

        private SerializedObject serializedObject;
        
        private SerializedProperty SyncSceneCameraToMainCamera;
        private SerializedProperty TileWidth;
        private SerializedProperty TileHeight;
        private SerializedProperty EnableProfiler;
        private SerializedProperty EProfilerType;
        private SerializedProperty EUpdateType;
        private SerializedProperty EDisplayType;
        private SerializedProperty ProfilerModeDensityList;
        private SerializedProperty HeatMapTex;
        private SerializedProperty HeatMapRange;
        private SerializedProperty HeatMapStep;
        private SerializedProperty HeatMapGradient;
        private SerializedProperty ECullMode;
        private SerializedProperty HeatMapRampMin;
        private SerializedProperty HeatMapRampMax;

        private SerializedProperty NeedSyncColorRangeSetting;
        private SerializedProperty NeedUpdateUITileGrid;
        private SerializedProperty NeedRecollectRenderers;
        private SerializedProperty NeedLogOutProfilerData;
        private SerializedProperty NeedLogDataToProfilerWindow;
        private SerializedProperty LastLogFrameCount;
        
        private SerializedProperty HideGoTUITile;
        private SerializedProperty HideTileNum;

        private GUIContent c_EnableProfiler = new GUIContent("启用");
        private GUIContent c_SyncSceneCameraToMainCamera = new GUIContent("Scene视图同步到Game视图");
        private GUIContent c_HideGoUITile = new GUIContent("隐藏棋盘格划分UI");
        private GUIContent c_HideTileNum = new GUIContent("隐藏棋盘格Id");
        private GUIContent c_TileWidth = new GUIContent("棋盘格宽度");
        private GUIContent c_TileHeight = new GUIContent("棋盘格高度");
        private GUIContent c_EDisplayType = new GUIContent("展示类型", "分为基于棋盘格和基于Mesh，用于检查OverDraw和分析Mesh渲染占用的像素密度");
        private GUIContent c_EUpdateType = new GUIContent("Renderer更新频率", "如果不是实时更新，则需要通过下方的按钮来重新收集场景的Renderer对象");

        private Dictionary<int, GUIContent> profilerSettingUnitDict = new Dictionary<int, GUIContent>()
        {
            [(int)DisplayType.OnlyMesh] = new GUIContent("顶点/10000像素"),
            [(int)DisplayType.OnlyTile] = new GUIContent("顶点/10000像素"),
            [(int)DisplayType.TileBasedMesh] = new GUIContent("顶点/10000像素"),
            [(int)DisplayType.Overdraw] = new GUIContent("次"),
        };

        private string c_OnlyTileContent1 = "Only Tile 逐棋盘格的统计阈值设置";
        private string c_OnlyTileContent2 = "单位为【屏幕顶点数/1万屏幕像素】，设置的值为该范围的下限区间";
        private string c_OnlyMeshContent1 = "Only Mesh 逐Mesh的统计阈值设置";
        private string c_OnlyMeshContent2 = "单位为【网格顶点数/1万屏幕像素】，设置的值为该范围的下限区间";
        private string c_TileBasedMeshContent1 = "Tile Based Mesh 逐棋盘格的网格的顶点统计阈值设置";
        private string c_TileBasedMeshContent2 = "单位为【棋盘格内网格顶点数/1万屏幕像素】，设置的值为该范围的下限区间，并根据棋盘格切分屏幕画面，逐像素显示出平均占用最高的网格";
        private string c_OverdrawContent1 = "Overdraw 重绘像素点显示";
        private string c_OverdrawContent2 = "单位为【重绘次数】，设置的值为该范围的下限区间，并逐像素显示出像素的重绘次数";
        private string c_ThresholdContent1 = "若阈值第1档 N > 0 ，则在0~N的范围内的阈值不会被染色";

        private const int MaxDataCount = 8;
        private bool needUpdateColumnData = true;
        private float logTickTimer = 0;
        private int LastPullLogFrameCount = 0;
        
        // 原则上，项目是需要启动Static Batching的，但是由于这个Static Batching会导致运行时合并网格导致获取顶点数的方法出现误差
        // 因此在界面内提供关闭Static Batching以及相关的tips内容
        // 获取Static Batching需要通过反射，加一个变量用来调度更新
        private static bool needUpdateStaticBatchingTips = true;
        private static bool enableStaticBatching = true;
        private static bool enableDynamicBatching = true;
        
        // 根据不同的阈值进行数据的分割，阈值顺序与设置的相同
        private List<List<VertexProfilerTreeElement>> classifyProfilerList = new List<List<VertexProfilerTreeElement>>();
        private List<VertexProfilerTreeElement> treeElementsData = new List<VertexProfilerTreeElement>();
        
        // GUILayout 
        private GUIStyle headTipStyle;
        private GUIStyle boxStyle;
        private GUIStyle titleStyle;
        private GUIStyle contentStyle;
        private TreeModel<VertexProfilerTreeElement> m_TreeModel;
        private MultiColumnHeaderState.Column[] columns;
        private SearchField m_SearchField;
        /*[SerializeField]*/ TreeViewState m_TreeViewState; // Serialized in the window layout file so it survives assembly reloading
        /*[SerializeField]*/ MultiColumnHeaderState m_MultiColumnHeaderState;
        private MyMultiColumnHeader m_multiColumnHeader;
        private VertexProfilerMultiColumnTreeView m_TreeView;

        [MenuItem("Window/VertexProfilerWindow")]
        public static void ShowWindow()
        {
            var window = GetWindow<VertexProfilerWindow>();
            window.titleContent = new GUIContent("顶点/像素占比检测工具界面");
            window.minSize = new Vector2(800, 600);
            window.maxSize = new Vector2(1400, 1200);
            window.InitGUIStyle();
            window.CheckURPResource();
            window.InitRes();
            window.CheckShowAddVertexProfilerBtn(true);
            window.UpdateResToVertexProfiler();
            window.GetStaticBatchingStatus();
            window.Show();
        }
        
        private void OnEnable()
        {
            if (VertexProfilerEvent.LogoutToExcelEvent == null)
            {
                VertexProfilerEvent.LogoutToExcelEvent += ProfilerWriter.LogoutToExcel;
            }
        }

        private void OnDisable()
        {
            if (VertexProfilerEvent.LogoutToExcelEvent != null)
            {
                VertexProfilerEvent.LogoutToExcelEvent -= ProfilerWriter.LogoutToExcel;
            }
        }

        private void InitGUIStyle()
        {
            headTipStyle = new GUIStyle(EditorStyles.largeLabel);
            headTipStyle.fontStyle = FontStyle.Bold;
            headTipStyle.fontSize = 16;
            headTipStyle.wordWrap = true;

            titleStyle = new GUIStyle(EditorStyles.largeLabel);
            titleStyle.fontSize += 2;
            titleStyle.fontStyle = FontStyle.Bold;
            
            contentStyle = new GUIStyle(EditorStyles.largeLabel);
        }
        
        /// <summary>
        /// 检查当前的项目工程是否有URP环境,暂定仅打开Window时检查
        /// </summary>
        private void CheckURPResource()
        {
            urpNameSpaceExist = VertexProfilerEditorUtil.NamespaceExists("UnityEngine.Rendering.Universal");
            if (urpNameSpaceExist)
            {
                urpAssetExist = UniversalRenderPipeline.asset != null;
            }
            else
            {
                urpAssetExist = false;
            }
            // Debug.LogErrorFormat("urpNameSpaceExist = {0}", urpNameSpaceExist);
        }

        /// <summary>
        /// 是否需要用URP的版本来展示界面
        /// </summary>
        /// <returns></returns>
        private bool NeedUseURPVersion(bool onlyCheck = true)
        {
            if (onlyCheck)
            {
                return urpNameSpaceExist && urpAssetExist;
            }
            return urpNameSpaceExist && urpAssetExist && (vertexProfilerBase == null || !vertexProfilerBase.isURP); 
        }
        
        private void InitRes()
        {
            ResErrorLogContent.Clear();
            LoadComputeShader("CalculateVertexByTiles", ref CalculateVertexByTilesCS);
            LoadComputeShader("GenerateProfileRT", ref GenerateProfilerRTCS);
            LoadPrefab(UITIleGoResPath, "UITile", ref GoUITile);
            
            LoadTexture(HeatMapResPath, TempHeatMapName, ref HeatmapDefaultTexture);
            if (HeatmapDefaultTexture == null)
            {
                LoadTexture(HeatMapResPath, "Heatmap", ref HeatmapDefaultTexture);
            }
        }
        Rect searchFieldViewRect
        {
            get { return  GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(20)); }
        }
        Rect multiColumnTreeViewRect
        {
            get { return  GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)); }
        }

        private void LoadComputeShader(string name, ref ComputeShader cs)
        {
            string assetPath = string.Format("{0}{1}.compute", ComputeShaderResPath, name);
            cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
            if (cs == null)
            {
                string errorContent = string.Format("Compute Shader {0}.compute 加载失败，错误路径{1}", name, assetPath);
                ResErrorLogContent.Add(errorContent);
                Debug.LogError(errorContent);
            }
        }
        
        private void LoadPrefab(string folderPath, string name, ref GameObject go)
        {
            string assetPath = string.Format("{0}{1}.prefab", folderPath, name);
            go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null)
            {
                string errorContent = string.Format("Prefab {0}.prefab 加载失败，错误路径{1}", name, assetPath);
                ResErrorLogContent.Add(errorContent);
                Debug.LogError(errorContent);
            }
        }

        private void LoadTexture(string folderPath, string name, ref Texture2D tex)
        {
            foreach (var extension in TextureExtensions)
            {
                string assetPath = string.Format("{0}{1}{2}", folderPath, name, extension);
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex != null) break;
            }
            if (tex == null && !name.Equals(TempHeatMapName))
            {
                string errorContent = string.Format("Texture {0} 加载失败", name);
                ResErrorLogContent.Add(errorContent);
                Debug.LogError(errorContent);
            }
        }

        private void DrawGradientGUI()
        {
            EditorGUILayout.PropertyField(HeatMapGradient, new GUIContent("定制热力图渐变"));
            if (GUILayout.Button("应用颜色渐变到热力图Ramp"))
            {
                // 生成纹理，替换热力图ramp
                var texture = VertexProfilerEditorUtil.ConvertGradientToTexture(vertexProfilerBase.HeatMapGradient);
                texture.name = TempHeatMapName;
                serializedObject.ApplyModifiedProperties();
                vertexProfilerBase.HeatMapTex = texture;
                serializedObject.Update();
                
                // 存储这份纹理到本地
                string storePath = string.Format("{0}{1}{2}.png", Application.dataPath
                    , HeatMapResPath.Replace("Assets", ""), TempHeatMapName);
                System.IO.File.WriteAllBytes(storePath, texture.EncodeToPNG());
                AssetDatabase.Refresh();
            }
        }

        private bool CheckResourceReady()
        {
            if (ResErrorLogContent.Count > 0)
            {
                string content = "";
                for (int i = 0; i < ResErrorLogContent.Count; i++)
                {
                    if (i > 0) content += "\n";
                    content += ResErrorLogContent[i];
                }
                EditorGUILayout.HelpBox(content, MessageType.Info);
                return false;
            }
            
            return true;
        }
        // 判断是否需要显示添加脚本按钮
        private bool CheckShowAddVertexProfilerBtn(bool onlyCheck = false)
        {
            vertexProfilerBase = vertexProfilerBase != null ? vertexProfilerBase : FindObjectOfType<VertexProfilerBase>();
            Camera mainCamera = Camera.main;
            if (vertexProfilerBase != null && mainCamera != null)
            {
                if (vertexProfilerBase.isURP)
                {
                    vertexProfilerURP = FindObjectOfType<VertexProfilerURP>();
                }
                else
                {
                    vertexProfiler = FindObjectOfType<VertexProfiler>();
                }
                if (!fullyInited) // 每次都初始化太耗了,加上一个if判断优化
                {
                    UpdateResToVertexProfiler();
                }
                return true;
            }

            fullyInited = false;
            if (onlyCheck)
            {
                return false;
            }
            
            if (mainCamera == null)
            {
                if (GUILayout.Button("添加主摄像机（用于挂载VertexProfiler）"))
                {
                    GameObject gameObject = ObjectFactory.CreateGameObject(L10n.Tr("Main Camera"), typeof (Camera), typeof (AudioListener));
                    gameObject.hideFlags = HideFlags.None;
                    Camera component = gameObject.GetComponent<Camera>();
                    component.depth = -1f;
                    gameObject.tag = "MainCamera";
                    // component.transform.position = new Vector3(0.0f, 0.0f, -10f);
                    component.orthographic = false;
                    component.clearFlags = CameraClearFlags.Skybox;
                }
            }
            else
            {
                if (GUILayout.Button("给主摄像机挂载VertexProfiler"))
                {
                    var cameraTran = mainCamera.transform;
                    if (NeedUseURPVersion())
                    {
                        vertexProfilerURP = cameraTran.AddComponent<VertexProfilerURP>();
                        vertexProfilerBase = vertexProfilerURP;
                    }
                    else
                    {
                        vertexProfiler = cameraTran.AddComponent<VertexProfiler>();
                        vertexProfilerBase = vertexProfiler;
                    }
                    UpdateResToVertexProfiler();
                }
            }

            return false;
        }

        private void UpdateResToVertexProfiler()
        {
            if (vertexProfilerBase != null)
            {
                fullyInited = true;
                
                vertexProfilerBase.MainCamera = Camera.main;
                vertexProfilerBase.CalculateVertexByTilesCS = CalculateVertexByTilesCS;
                vertexProfilerBase.GenerateProfilerRTCS = GenerateProfilerRTCS;
                vertexProfilerBase.GoUITile = GoUITile;
                vertexProfilerBase.HeatMapTex = HeatmapDefaultTexture;
                logTickTimer = 0.99f; // 保证第一次刷新不会隔太久

                InitSerializedObject();
            }
        }

        private void InitSerializedObject()
        {
            if (vertexProfilerBase.isURP && vertexProfilerURP != null)
            {
                serializedObject = new SerializedObject(vertexProfilerURP);
            }
            else if(!vertexProfilerBase.isURP && vertexProfiler != null)
            {
                serializedObject = new SerializedObject(vertexProfiler);
            }
            // serializedObject = new SerializedObject(vertexProfilerBase);
            if (serializedObject != null)
            {
                SyncSceneCameraToMainCamera = serializedObject.FindProperty("SyncSceneCameraToMainCamera");
                TileWidth = serializedObject.FindProperty("TileWidth");
                TileHeight = serializedObject.FindProperty("TileHeight");
                EnableProfiler = serializedObject.FindProperty("EnableProfiler");
                EProfilerType = serializedObject.FindProperty("EProfilerType");
                EUpdateType = serializedObject.FindProperty("EUpdateType");
                EDisplayType = serializedObject.FindProperty("EDisplayType");
                HeatMapTex = serializedObject.FindProperty("HeatMapTex");
                HeatMapRange = serializedObject.FindProperty("HeatMapRange");
                HeatMapStep = serializedObject.FindProperty("HeatMapStep");
                HeatMapGradient = serializedObject.FindProperty("HeatMapGradient");
                ECullMode = serializedObject.FindProperty("ProfilerMode.ECullMode");
                HeatMapRampMin = serializedObject.FindProperty("HeatMapRampMin");
                HeatMapRampMax = serializedObject.FindProperty("HeatMapRampMax");
                
                ProfilerModeDensityList = serializedObject.FindProperty("ProfilerMode.DensityList");
                NeedSyncColorRangeSetting = serializedObject.FindProperty("ProfilerMode.NeedSyncColorRangeSetting");

                NeedUpdateUITileGrid = serializedObject.FindProperty("NeedUpdateUITileGrid");
                NeedRecollectRenderers = serializedObject.FindProperty("NeedRecollectRenderers");
                NeedLogOutProfilerData = serializedObject.FindProperty("NeedLogOutProfilerData");
                NeedLogDataToProfilerWindow = serializedObject.FindProperty("NeedLogDataToProfilerWindow");
                LastLogFrameCount = serializedObject.FindProperty("LastLogFrameCount");
                
                HideGoTUITile = serializedObject.FindProperty("HideGoTUITile");
                HideTileNum = serializedObject.FindProperty("HideTileNum");
                
                UpdateColumnComponent();
            }
        }

        public void GetStaticBatchingStatus()
        {
            if (needUpdateStaticBatchingTips)
            {
                VertexProfilerEditorUtil.GetBatchingForPlatform(out enableStaticBatching, out enableDynamicBatching);
                needUpdateStaticBatchingTips = false;
            }
        }

        private void OnGUI()
        {
            // 各种检查
            bool resReady = CheckResourceReady();
            if (!resReady)
            {
                if (GUILayout.Button("刷新工具所需资源"))
                {
                    InitRes();
                    UpdateResToVertexProfiler();
                }
            }
            bool showPanel = CheckShowAddVertexProfilerBtn();
            if (!showPanel || serializedObject == null) return;
            
            // 主要GUI
            serializedObject.Update();

            GUI.backgroundColor = Color.red;
            EditorGUI.BeginChangeCheck();
            int newProfileType = GUILayout.Toolbar(EProfilerType.enumValueIndex, new string[] { "简易模式", "详细模式" },
                GUILayout.Height(25));
            if(EditorGUI.EndChangeCheck())
            {
                // 如果枚举的值已经被修改，更新SerializedProperty的值
                EProfilerType.enumValueIndex = newProfileType;
                serializedObject.ApplyModifiedProperties();
                ChangeProfilerType(newProfileType);
                
                serializedObject.Update();
                needUpdateColumnData = true;
                logTickTimer = Mathf.Max(0.99f, logTickTimer); // 保证下一次刷新不会隔太久
            }
            GUI.backgroundColor = Color.white;
            
            
            EditorGUILayout.LabelField("由于Static Batching会导致静态对象在运行时的网格被合并，导致统计顶点时出现错误，因此建议在使用该工具时，关闭Static Batching\n如果是运行时修改则需要重新运行一次游戏", 
                headTipStyle);
            EditorGUI.BeginChangeCheck();
            enableStaticBatching = EditorGUILayout.Toggle("启用Static Batching", enableStaticBatching);
            EditorGUILayout.Space();
            if (EditorGUI.EndChangeCheck())
            {
                VertexProfilerEditorUtil.SetBatchingForPlatform(enableStaticBatching, enableDynamicBatching);
            }
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(EnableProfiler, c_EnableProfiler);
            if (!vertexProfilerBase.isURP)
            {
                if (GUILayout.Button("如果出现效果不对，点击重新生成新的调试Shader"))
                {
                    ReplaceShaderGenerator.GenerateNewReplaceShader();
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                if (EnableProfiler.boolValue)
                {
                    StartProfiler();
                }
                else
                {
                    StopProfiler();
                }
            }
            EditorGUILayout.Space();
            
            EditorGUILayout.PropertyField(SyncSceneCameraToMainCamera, c_SyncSceneCameraToMainCamera);
            EditorGUILayout.Space();
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(EDisplayType, c_EDisplayType);
            if (EditorGUI.EndChangeCheck())
            {
                NeedSyncColorRangeSetting.boolValue = true;
                serializedObject.ApplyModifiedProperties();
                CheckProfilerMode();
                serializedObject.Update();
                needUpdateColumnData = true;
            }

            if (EDisplayType.enumValueIndex == (int)DisplayType.OnlyTile 
                || EDisplayType.enumValueIndex == (int)DisplayType.TileBasedMesh)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(HideGoTUITile, c_HideGoUITile);
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    vertexProfilerBase.CheckShowUIGrid();
                }
                if (!HideGoTUITile.boolValue)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(HideTileNum, c_HideTileNum);
                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        vertexProfilerBase.SetTileNumShow();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (EProfilerType.enumValueIndex == (int)ProfilerType.Detail)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(TileWidth, c_TileWidth);
                    EditorGUILayout.PropertyField(TileHeight, c_TileHeight);
                    if (EditorGUI.EndChangeCheck())
                    {
                        NeedUpdateUITileGrid.boolValue = true;
                        needUpdateColumnData = true;
                    }
                }
            }
            EditorGUILayout.Space();
            
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = EditorGUIUtility.whiteTexture;
            bool showDensityContent = EDisplayType.enumValueIndex == (int)DisplayType.OnlyTile
                                      || EDisplayType.enumValueIndex == (int)DisplayType.OnlyMesh
                                      || EDisplayType.enumValueIndex == (int)DisplayType.TileBasedMesh
                                      || EDisplayType.enumValueIndex == (int)DisplayType.Overdraw;
            if (showDensityContent)
            {
                EditorGUILayout.BeginVertical(boxStyle);
                if (EDisplayType.enumValueIndex == (int)DisplayType.OnlyTile) // 使用棋盘格密度设置
                {
                    EditorGUILayout.LabelField(c_OnlyTileContent1, titleStyle);
                    EditorGUILayout.LabelField(c_OnlyTileContent2, contentStyle);
                }
                else if (EDisplayType.enumValueIndex == (int)DisplayType.OnlyMesh) // 使用顶点像素密度设置
                {
                    EditorGUILayout.LabelField(c_OnlyMeshContent1, titleStyle);
                    EditorGUILayout.LabelField(c_OnlyMeshContent2, contentStyle);
                }
                else if (EDisplayType.enumValueIndex == (int)DisplayType.TileBasedMesh) // 使用逐棋盘格的Mesh密度设置
                {
                    EditorGUILayout.LabelField(c_TileBasedMeshContent1, titleStyle);
                    EditorGUILayout.LabelField(c_TileBasedMeshContent2, contentStyle);
                }
                else if (EDisplayType.enumValueIndex == (int)DisplayType.Overdraw) // 使用逐棋盘格的Mesh密度设置
                {
                    EditorGUILayout.LabelField(c_OverdrawContent1, titleStyle);
                    EditorGUILayout.LabelField(c_OverdrawContent2, contentStyle);
                }
                EditorGUILayout.LabelField(c_ThresholdContent1, contentStyle);
                EditorGUILayout.EndVertical();
            }
            
            // 绘制阈值控件
            if (EDisplayType.enumValueIndex == (int)DisplayType.OnlyTile 
                || EDisplayType.enumValueIndex == (int)DisplayType.OnlyMesh
                || EDisplayType.enumValueIndex == (int)DisplayType.TileBasedMesh
                || EDisplayType.enumValueIndex == (int)DisplayType.Overdraw)
            {
                DrawDensitySetting(ProfilerModeDensityList);
            }
            if (EDisplayType.enumValueIndex == (int)DisplayType.MeshHeatMap)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(HeatMapTex, typeof(Texture2D), new GUIContent("热力图Ramp纹理"));
                EditorGUILayout.PropertyField(ECullMode, new GUIContent("热力图顶点剔除"), GUILayout.Width(300));
                EditorGUILayout.EndHorizontal();
                DrawGradientGUI();
                float minValue = HeatMapRampMin.floatValue;
                float maxValue = HeatMapRampMax.floatValue;
                EditorGUILayout.MinMaxSlider(new GUIContent("查看的热力图阈值范围"), ref minValue, ref maxValue, 0.0f, 1.0f);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.IntSlider(HeatMapRange, 1, 8, new GUIContent("热力图采样范围（过大会影响性能）"));
                EditorGUILayout.IntSlider(HeatMapStep, 1, 5, new GUIContent("热力图采样半径"));
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    HeatMapRampMin.floatValue = minValue;
                    HeatMapRampMax.floatValue = maxValue;
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }
            }
            EditorGUILayout.Space();
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(EUpdateType, c_EUpdateType);
            if (EditorGUI.EndChangeCheck())
            {
                NeedRecollectRenderers.boolValue = true;
            }
            if (EUpdateType.enumValueIndex == (int)UpdateType.Once)
            {
                if (GUILayout.Button("重新收集场景对象"))
                {
                    NeedRecollectRenderers.boolValue = true;
                }
            }

            bool canLog = EDisplayType.enumValueIndex == (int)DisplayType.OnlyTile
                          || EDisplayType.enumValueIndex == (int)DisplayType.OnlyMesh
                          || EDisplayType.enumValueIndex == (int)DisplayType.TileBasedMesh;
            if (canLog)
            {
                if (GUILayout.Button("输出当前画面ProfilerLog"))
                {
                    NeedLogOutProfilerData.boolValue = true;
                }
                serializedObject.ApplyModifiedProperties();
                
                // 轮询访问log数据，定时更新log列表
                logTickTimer += Time.deltaTime;
                if (logTickTimer > 1.0f)
                {
                    NeedLogDataToProfilerWindow.boolValue = true;
                    logTickTimer = 0;
                }
                
                if (needUpdateColumnData)
                {
                    UpdateColumnComponent();
                }
                UpdateProfilerLogData();
                if (m_TreeView != null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("搜索Mesh/Hierarchy名称:", GUILayout.Width(150),GUILayout.Height(14));
                    m_TreeView.searchString = m_SearchField.OnGUI(searchFieldViewRect, m_TreeView.searchString);
                    EditorGUILayout.EndHorizontal();
                    m_TreeView.OnGUI(multiColumnTreeViewRect);
                }
            }

            // 这个一定要放最后
            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawDensitySetting(SerializedProperty targetList)
        {
            EditorGUI.BeginChangeCheck();
            // EditorGUILayout.PropertyField(targetList, content, true);
            ProfilerType profilerType = (ProfilerType)EProfilerType.enumValueIndex;
            int listSize = targetList.arraySize;
            if (listSize > 0)
            {
                SerializedProperty lastProperty  = targetList.GetArrayElementAtIndex(listSize - 1);
                int sliderMax = lastProperty.intValue;
                
                for (int i = 0; i < targetList.arraySize; i++)
                {
                    SerializedProperty floatProperty = targetList.GetArrayElementAtIndex(i);

                    Color color = VertexProfilerUtil.GetProfilerColor(i, profilerType);
                    bool showDensityColor = color.a > 0f;
                    // 用作展示的颜色不考虑透明度（有可能没必要有这一段，先加上）
                    Color colorGamma = color.gamma;
                    colorGamma.a = 1.0f;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(string.Format("阈值{0}", i+1), GUILayout.Width(50));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ColorField(new GUIContent(), colorGamma, false, false, false, GUILayout.Width(60));
                    bool isLastElement = i == targetList.arraySize - 1;
                    var settingUnit = profilerSettingUnitDict.ContainsKey(EDisplayType.enumValueIndex)
                        ? profilerSettingUnitDict[EDisplayType.enumValueIndex]
                        : new GUIContent("这里单位漏了"); 
                    if (profilerType == ProfilerType.Simple)
                    {
                        if (isLastElement)
                        {
                            EditorGUILayout.PropertyField(floatProperty, new GUIContent(""));
                            EditorGUILayout.LabelField(settingUnit,GUILayout.MaxWidth(100));
                        }
                        else
                        {
                            EditorGUILayout.IntSlider(floatProperty, 0, sliderMax, new GUIContent(""));
                            EditorGUILayout.LabelField(settingUnit,GUILayout.MaxWidth(100));
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    else if (profilerType == ProfilerType.Detail)
                    {
                        EditorGUI.EndDisabledGroup();
                        if (isLastElement)
                        {
                            EditorGUILayout.PropertyField(floatProperty, new GUIContent(""));
                            EditorGUILayout.LabelField(settingUnit,GUILayout.MaxWidth(100));
                        }
                        else
                        {
                            EditorGUILayout.IntSlider(floatProperty, 0, sliderMax, new GUIContent(""));
                            EditorGUILayout.LabelField(settingUnit,GUILayout.MaxWidth(100));
                        }
                    }
                    // 是否显示该颜色区间
                    bool curShowDensityColor = EditorGUILayout.ToggleLeft(new GUIContent("显示区间"), showDensityColor, GUILayout.Width(80));
                    if (curShowDensityColor != showDensityColor)
                    {
                        VertexProfilerUtil.ActivateProfilerColor(i, profilerType, curShowDensityColor);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (targetList.arraySize < MaxDataCount)
            {
                if (GUILayout.Button("新增阈值"))
                {
                    AddData(targetList);
                }
            }
            if (GUILayout.Button("移除阈值") && targetList.arraySize > 0)
            {
                RemoveLastData(targetList);
            }
            if (EditorGUI.EndChangeCheck())
            {
                CheckAndUpdateDataRange(targetList);
                NeedSyncColorRangeSetting.boolValue = true;
                needUpdateColumnData = true;
            }
            
            if (GUILayout.Button("重置默认阈值"))
            {
                UseDefaultColorRangeSetting();
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }
        }
        private void AddData(SerializedProperty targetList)
        {
            // 插入新值
            targetList.InsertArrayElementAtIndex(targetList.arraySize);
            serializedObject.ApplyModifiedProperties();

            // 设置默认值
            targetList.GetArrayElementAtIndex(targetList.arraySize - 1).intValue = 0;

            // 如果存在上一条数据，则新插入的这条数据的值不可以小于上一条数据
            if (targetList.arraySize > 1)
            {
                int previousValue = targetList.GetArrayElementAtIndex(targetList.arraySize - 2).intValue;
                targetList.GetArrayElementAtIndex(targetList.arraySize - 1).intValue = Mathf.Max(0, previousValue);
            }
        }

        private void RemoveLastData(SerializedProperty targetList)
        {
            targetList.DeleteArrayElementAtIndex(targetList.arraySize - 1);
            serializedObject.ApplyModifiedProperties();
        }
        
        // 数据实时范围检查，不可以低于上一条的设置也不可以超过下一条的设置（如果存在的话）
        private void CheckAndUpdateDataRange(SerializedProperty targetList)
        {
            for (int i = 0; i < targetList.arraySize; i++)
            {
                SerializedProperty element = targetList.GetArrayElementAtIndex(i);
                // 顶点数一定要大于0
                element.intValue = Mathf.Max(element.intValue, 0);
                // 与上一条比较
                if (i > 0)
                {
                    int previousValue = targetList.GetArrayElementAtIndex(i - 1).intValue;
                    element.intValue = Mathf.Max(element.intValue, previousValue);
                }

                // 与下一条比较
                if (i < targetList.arraySize - 1)
                {
                    int nextValue = targetList.GetArrayElementAtIndex(i + 1).intValue;
                    element.intValue = Mathf.Min(element.intValue, nextValue);
                }
            }
        }

        /// <summary>
        /// 更新竖列的显示内容，与输出Excel的内容一一对应
        /// </summary>
        private void UpdateColumnComponent()
        {
            if (vertexProfilerBase == null || serializedObject == null || EDisplayType == null) return;
            // Check if it already exists (deserialized from window layout file or scriptable object)
            if (m_TreeViewState == null)
                m_TreeViewState = new TreeViewState();

            List<MultiColumnHeaderState.Column> columnsList = new List<MultiColumnHeaderState.Column>();
            columnsList.Add(new MultiColumnHeaderState.Column()
            {
                headerContent = new GUIContent("阈值"),
                width = 90,
                minWidth = 70,
                maxWidth = 120,
                autoResize = true,
                headerTextAlignment = TextAlignment.Center,
                canSort = false
            });
            columnsList.Add(new MultiColumnHeaderState.Column()
            {
                headerContent = new GUIContent("阈值设置值"),
                width = 90,
                minWidth = 70,
                maxWidth = 120,
                autoResize = true,
                headerTextAlignment = TextAlignment.Center,
                canSort = false
            });
            switch (EDisplayType.enumValueIndex)
            {
                case (int)DisplayType.OnlyTile:
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("Tile Offset"),
                        width = 90,
                        minWidth = 70,
                        maxWidth = 120,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("顶点数"),
                        width = 120,
                        minWidth = 120,
                        maxWidth = 140,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("密度（顶点数/1万像素）"),
                        width = 180,
                        minWidth = 140,
                        maxWidth = 220,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    break;
                
                case (int)DisplayType.OnlyMesh:
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("资源名称"),
                        width = 250,
                        minWidth = 150,
                        maxWidth = 400,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center,
                        canSort = false
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("顶点数"),
                        width = 120,
                        minWidth = 120,
                        maxWidth = 140,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("占用像素数"),
                        width = 120,
                        minWidth = 120,
                        maxWidth = 140,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("平均像素密度（总顶点数/总占用像素数）"),
                        width = 180,
                        minWidth = 140,
                        maxWidth = 220,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("资源场景路径"),
                        width = 700,
                        minWidth = 300,
                        maxWidth = 1000,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center,
                        canSort = false
                    });
                    break;
                
                case (int)DisplayType.TileBasedMesh:
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("Tile Offset"),
                        width = 90,
                        minWidth = 70,
                        maxWidth = 120,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("资源名称"),
                        width = 250,
                        minWidth = 150,
                        maxWidth = 400,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center,
                        canSort = false
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("棋盘格顶点数"),
                        width = 120,
                        minWidth = 120,
                        maxWidth = 140,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("网格顶点数（使用率）"),
                        width = 120,
                        minWidth = 120,
                        maxWidth = 140,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center,
                        canSort = false
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("占用棋盘格像素数"),
                        width = 120,
                        minWidth = 120,
                        maxWidth = 140,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("平均像素密度（总顶点数/棋盘格占用像素数）"),
                        width = 180,
                        minWidth = 140,
                        maxWidth = 220,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center
                    });
                    columnsList.Add(new MultiColumnHeaderState.Column()
                    {
                        headerContent = new GUIContent("资源场景路径"),
                        width = 700,
                        minWidth = 300,
                        maxWidth = 1000,
                        autoResize = true,
                        headerTextAlignment = TextAlignment.Center,
                        canSort = false
                    });
                    break;
            }

            var headerState = new MultiColumnHeaderState(columnsList.ToArray());
            if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_MultiColumnHeaderState, headerState))
                MultiColumnHeaderState.OverwriteSerializedFields(m_MultiColumnHeaderState, headerState);
            m_MultiColumnHeaderState = headerState;

            if (m_multiColumnHeader == null)
            {
                m_multiColumnHeader = new MyMultiColumnHeader(headerState);
            }
            else
            {
                m_multiColumnHeader.state = headerState;
            }

            m_multiColumnHeader.ResizeToFit();
            UpdateColorRangeDepthElements();
        }
        // 根据当前设置的颜色阈值构建二级树枝
        private void UpdateColorRangeDepthElements()
        {
            classifyProfilerList.Clear();
            int targetDensityListCount = 0;
            List<int> DensityList = GetDensityList();
            if(DensityList == null) return;
            targetDensityListCount = DensityList.Count;
            if(targetDensityListCount == 0) return;

            if (targetDensityListCount > 0)
            {
                for (int i = 0; i < targetDensityListCount; i++)
                {
                    classifyProfilerList.Add(new List<VertexProfilerTreeElement>());
                }
            }
            needUpdateColumnData = false;
        }

        /// <summary>
        /// 拉取数据
        /// </summary>
        private void UpdateProfilerLogData()
        {
            if (vertexProfilerBase == null || serializedObject == null || LastLogFrameCount == null || EDisplayType == null || m_TreeViewState == null) return;
            // 当window的framecount不等于log输出时的framecount时，更新window的log数据
            if (LastLogFrameCount.intValue != LastPullLogFrameCount)
            {
                int Id = 0;
                
                treeElementsData.Clear();
                // 必须插入根节点
                var root = new VertexProfilerTreeElement("Root", -1, Id);
                treeElementsData.Add(root);
                // AddColorRangeDepthElements();
                // 清空阈值分类节点的缓存
                for (int i = 0; i < classifyProfilerList.Count; i++)
                {
                    classifyProfilerList[i].Clear();
                }
                // 往阈值分类节点中插入数据
                switch (EDisplayType.enumValueIndex)
                {
                    case (int)DisplayType.OnlyTile:
                        AddOnlyTileProfilerElements(ref Id);
                        break;
                    case (int)DisplayType.OnlyMesh:
                        AddOnlyMeshProfilerElements(ref Id);
                        break;
                    case (int)DisplayType.TileBasedMesh:
                        AddTileBasedMeshProfilerElements(ref Id);
                        break;
                }

                if (m_TreeModel == null)
                {
                    m_TreeModel = new TreeModel<VertexProfilerTreeElement>(treeElementsData);
                }
                else
                {
                    m_TreeModel.SetData(treeElementsData);
                }

                if (m_TreeView == null)
                {
                    m_TreeView = new VertexProfilerMultiColumnTreeView(EDisplayType.enumValueIndex, m_TreeViewState, m_multiColumnHeader, m_TreeModel);
                }
                else
                {
                    m_TreeView.DisplayTypeIndex = EDisplayType.enumValueIndex;
                    m_TreeView.Reload();
                }
                
                if (m_SearchField == null)
                {
                    m_SearchField = new SearchField();
                    m_SearchField.downOrUpArrowKeyPressed += m_TreeView.SetFocusAndEnsureSelectedItem;
                }
                
                LastPullLogFrameCount = LastLogFrameCount.intValue;
            }
        }
        // 将分散到各个阈值的数据统一回总列表
        private void MergeThresholdListToTreeList(ref int Id)
        {
            List<int> targetDensityList = GetDensityList();
            // 临时处理：ID的作用是统计当前展开的层级结构，需要把每个阈值的id提到最前，保证刷新的时候不会出现层级展开的跳变
            int classifyCount = classifyProfilerList.Count;
            int depth0Id = 0;
            if (targetDensityList != null && targetDensityList.Count >= classifyCount)
            {
                for (int i = 0; i < classifyCount; i++)
                {
                    var targetProfilerList = classifyProfilerList[i];
                    int threshold = targetDensityList[i];
                    Color color = VertexProfilerUtil.DefaultProfilerColor[i];
                    // 插入一条阈值的depth数据
                    treeElementsData.Add(new VertexProfilerTreeElement(
                        "阈值" + (i + 1).ToString(), 0, ++depth0Id, threshold, color));
                    treeElementsData.AddRange(targetProfilerList);
                }
            }

            int depth1Id = classifyCount;
            for (int k = 0; k < treeElementsData.Count; k++)
            {
                if (treeElementsData[k].depth > 0)
                {
                    treeElementsData[k].id = ++depth1Id;
                }
            }
        }
        // 分类填入profile数据，注意depth要从1开始
        private void AddOnlyTileProfilerElements(ref int Id)
        {
            List<ProfilerDataContents> logData = GetLogoutDataList();
            if (logData == null) return;
            
            int classifyProfilerListCount = classifyProfilerList.Count;
            for (int i = 0; i < logData.Count; i++)
            {
                var data = logData[i];
                // treeElementsData.Add(new VectorProfilerTreeElement(
                //     "TitleIndex"+data.TileIndex, 0, ++Id, 
                //     data.TileIndex_Int, data.VertexCount_Int, data.Density2_float, data.ProfilerColor));
                var targetProfilerList = classifyProfilerListCount > data.ThresholdLevel ? classifyProfilerList[data.ThresholdLevel] : null;
                if (targetProfilerList != null)
                {
                    targetProfilerList.Add(new VertexProfilerTreeElement(
                    "TitleIndex"+data.TileIndex, 1, ++Id, 
                    data.TileIndex_Int, data.VertexCount_Int, data.Density2_float, data.ProfilerColor));
                }
            }
            MergeThresholdListToTreeList(ref Id);
        }
        // 分类填入profile数据，注意depth要从1开始
        private void AddOnlyMeshProfilerElements(ref int Id)
        {
            List<ProfilerDataContents> logData = GetLogoutDataList();
            if (logData == null) return;
            
            int classifyProfilerListCount = classifyProfilerList.Count;
            for (int i = 0; i < logData.Count; i++)
            {
                var data = logData[i];
                var targetProfilerList = classifyProfilerListCount >  data.ThresholdLevel ? classifyProfilerList[data.ThresholdLevel] : null;
                if (targetProfilerList != null)
                {
                    targetProfilerList.Add(new VertexProfilerTreeElement(
                    "TitleIndex"+data.TileIndex, 1, ++Id,
                    data.ResourceName, data.VertexCount_Int, data.PixelCount_Int, data.Density_float, data.RendererHierarchyPath, data.ProfilerColor));
                }
            }
            MergeThresholdListToTreeList(ref Id);
        }
        // 分类填入profile数据，注意depth要从1开始
        private void AddTileBasedMeshProfilerElements(ref int Id)
        {
            List<ProfilerDataContents> logData = GetLogoutDataList();
            if (logData == null) return;
            int classifyProfilerListCount = classifyProfilerList.Count;
            // NOTE : 一个tile中存在多个density（ThresholdLevel），但是在这里就处理分类会导致数据插入到分类列表中的顺序出错
            // 因此临时处理，不再根据Tile做树状结构，而是将TileIndex作为数据之一列在每一条Profiler记录内，与Excel的导出结果区别开

            int tileIndex = 0;
            for (int i = 0; i < logData.Count; i++)
            {
                var data = logData[i];
                var targetProfilerList = classifyProfilerListCount > data.ThresholdLevel ? classifyProfilerList[data.ThresholdLevel] : null;
                if (data.HasData(data.TileIndex) && targetProfilerList != null)
                {
                    tileIndex = data.TileIndex_Int;
                }
                else if(targetProfilerList != null)
                {
                    targetProfilerList.Add(new VertexProfilerTreeElement(
                        "TitleIndex"+data.TileIndex, 1, ++Id,
                        tileIndex, data.VertexInfo, data.ResourceName, data.VertexCount_Int, data.PixelCount_Int, data.Density_float, 
                        data.RendererHierarchyPath, data.ProfilerColor));
                }
            }
            MergeThresholdListToTreeList(ref Id);
        }

        private void ChangeProfilerType(int newProfileType)
        {
            if (vertexProfilerBase.isURP)
            {
                vertexProfilerURP.ChangeProfilerType(newProfileType);
                return;
            }
            vertexProfiler.ChangeProfilerType(newProfileType);
        }

        private void StartProfiler()
        {
            if (vertexProfilerBase.isURP)
            {
                vertexProfilerURP.StartProfiler();
                return;
            }
            vertexProfiler.StartProfiler();
        }
        private void StopProfiler()
        {
            if (vertexProfilerBase.isURP)
            {
                vertexProfilerURP.StopProfiler();
                return;
            }
            vertexProfiler.StopProfiler();
        }

        private void CheckProfilerMode()
        {
            if (vertexProfilerBase.isURP)
            {
                vertexProfilerURP.CheckProfilerMode();
                return;
            }
            vertexProfiler.CheckProfilerMode();
        }

        private void UseDefaultColorRangeSetting()
        {
            if (vertexProfilerBase.isURP)
            {
                vertexProfilerURP.ProfilerMode.UseDefaultColorRangeSetting();
                return;
            }
            vertexProfiler.ProfilerMode.UseDefaultColorRangeSetting();
        }

        private List<int> GetDensityList()
        {
            if (vertexProfilerBase.isURP)
            {
                return vertexProfilerURP.ProfilerMode?.DensityList;
            }
            return vertexProfiler.ProfilerMode?.DensityList;
        }

        private List<ProfilerDataContents> GetLogoutDataList()
        {
            if (vertexProfilerBase.isURP)
            {
                return vertexProfilerURP.LogMode?.logoutDataList;
            }
            return vertexProfiler.ProfilerMode?.logoutDataList;
        }
        private void OnDestroy()
        {
            if (vertexProfiler != null)
            {
                logTickTimer = 0;
            }
            if (vertexProfilerURP != null)
            {
                logTickTimer = 0;
            }
            if (serializedObject != null)
            {
                if (NeedLogDataToProfilerWindow != null)
                {
                    NeedLogDataToProfilerWindow.boolValue = false;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
    }
}


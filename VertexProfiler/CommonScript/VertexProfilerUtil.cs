using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    public enum UpdateType
    {
        Once,
        EveryFrame
    }

    // 展示统计类型
    public enum DisplayType
    {
        /// <summary>
        /// 仅Tile棋盘格的统计，ColorRangeSettings中的设置会同步计算成Tile面积内的总顶点数阈值，主要用于统计最终的顶点密度问题
        /// </summary>
        OnlyTile = 0,
        /// <summary>
        /// 仅逐Mesh的统计，ColorRangeSettings中的设置会同步计算成Mesh的顶点数与占用像素数的比值，主要用于筛选Mesh LOD不合理问题
        /// </summary>
        OnlyMesh = 1,
        /// <summary>
        /// 基于逐棋盘格的Mesh的顶点统计，需要结合Tile和Mesh的算法，通过Tile筛选出棋盘格中顶点数过多的区块，然后再筛选出这些区块中密度过高的区域进行区分显示
        /// </summary>
        TileBasedMesh = 2,
        /// <summary>
        /// 逐网格的热力图模式，以网格为单位计算方式为计算当前屏幕中每个像素的顶点数密度，根据密度形成热力图
        /// </summary>
        MeshHeatMap = 3,
        /// <summary>
        /// 输出Mesh重绘
        /// </summary>
        Overdraw = 4,
    }
    
    // 调试类型
    public enum ProfilerType
    {
        /// <summary>
        /// 简单模式。适合给美术童鞋，简化调节阈值数量为3个且不再暴露调节GUI，阈值颜色固定为 绿 黄 红 三色
        /// </summary>
        Simple,
        /// <summary>
        /// 详细模式。适合项目运行时查看，细化调节阈值8个档位
        /// </summary>
        Detail
    }
    public struct RendererBoundsData
    {
        public Vector3 center; // 包围盒世界空间位置，用于剔除
        public Vector3 extends; // 包围盒尺寸，用于剔除
    }

    // 调度出来的渲染对象结构体
    // 由于每个渲染对象的对应挂载的mesh组件的不同，这里做一个统一采集
    public class RendererComponentData
    {
        public Renderer renderer;
        public Mesh m;
        public MeshFilter mf;
        public SkinnedMeshRenderer smr;
    }

    /// <summary>
    /// 传递到compute shader的颜色划分配置列表内容
    /// 至少需要2条数据
    /// </summary>
    [System.Serializable]
    public struct ColorRangeSetting
    {
        /// <summary>
        /// 阈值下限(含)
        /// 当使用OnlyTile或TileBasedMesh时单位为【屏幕顶点数/1万屏幕像素】
        /// 当使用OnlyMesh时单位为【Mesh顶点数/Mesh占用像素】
        /// </summary>
        public float threshold;
        public Color color;

        public override string ToString()
        {
            return string.Format("threshold = {0}, color = {1}", threshold, color);
        }
    }

    public struct ProfilerDataContents : IComparable
    {
        /// <summary>
        /// 是否初始化了
        /// </summary>
        public bool Inited;
        
        /// <summary>
        /// 如果是棋盘格切分，则会有TileIndex
        /// </summary>
        public string TileIndex;
        /// <summary>
        /// 如果是基于Mesh的统计，则会拥有资源名称
        /// </summary>
        public string ResourceName;
        /// <summary>
        /// 渲染器在Hierarchy的完整路径，OnlyTile模式拿不到
        /// </summary>
        public string RendererHierarchyPath;
        /// <summary>
        /// 顶点占用数，如果是OnlyTile则代表tile内的顶点数，OnlyMesh类型则是Mesh的顶点数，
        /// </summary>
        public string VertexCount;
        /// <summary>
        /// 原生网格顶点数（tile内顶点使用率），仅TileBasedMesh有用，用来标记当前网格本来的顶点数以及在目标tile内使用了多少比例的顶点
        /// </summary>
        public string VertexInfo;
        /// <summary>
        /// 占用像素。如果是带Tile的类型则是在某个tile中占用的像素数，OnlyMesh类型则是Mesh总共占用的像素数
        /// </summary>
        public string PixelCount;
        /// <summary>
        /// 总顶点数/总像素占用后得到的平均密度
        /// </summary>
        public string Density;
        /// <summary>
        /// 带Tile类型使用，为在某个tile中的像素密度，将换算成10000像素的平均占用
        /// </summary>
        public string Density2;

        public int ThresholdLevel;
        public Color ProfilerColor;
        
        // 额外数据
        public int TileIndex_Int;
        public int VertexCount_Int;
        public int PixelCount_Int;
        public float Density_float;
        public float Density2_float;
        
        /// <summary>
        /// OnlyTile初始化
        /// </summary>
        /// <param name="index"></param>
        /// <param name="vertexCount"></param>
        /// <param name="density2Float"></param>
        /// <param name="color"></param>
        public ProfilerDataContents(int index, uint vertexCount, float density2Float, int thresholdLevel, Color color)
        {
            Inited = true;
            TileIndex = index.ToString();
            VertexCount = vertexCount.ToString();
            Density2 = density2Float.ToString("F");

            TileIndex_Int = index;
            VertexCount_Int = (int)vertexCount;
            Density2_float = density2Float;
            ThresholdLevel = thresholdLevel;
            ProfilerColor = color;

            ResourceName = string.Empty;
            RendererHierarchyPath = string.Empty;
            VertexInfo = string.Empty;
            PixelCount = string.Empty;
            Density = string.Empty;
            RendererHierarchyPath = string.Empty;

            PixelCount_Int = 0;
            Density_float = 0f;
        }

        public ProfilerDataContents(string resourceName, int vertexCount, int pixelCount, 
            float densityFloat, string rendererHierarchyPath, int thresholdLevel, Color color)
        {
            Inited = true;
            ResourceName = resourceName;
            VertexCount = vertexCount.ToString();
            PixelCount = pixelCount.ToString();
            Density = densityFloat.ToString("F");
            RendererHierarchyPath = rendererHierarchyPath;
            
            VertexCount_Int = vertexCount;
            PixelCount_Int = pixelCount;
            Density_float = densityFloat;
            ThresholdLevel = thresholdLevel;
            ProfilerColor = color;

            TileIndex = string.Empty;
            TileIndex_Int = 0;
            VertexInfo = string.Empty;
            Density2 = string.Empty;
            Density2_float = 0;
        }
        
        public ProfilerDataContents(string resourceName, int vertexCount, int meshVertexCount, int pixelCount, 
            float densityFloat, string rendererHierarchyPath, int thresholdLevel, Color color)
        {
            Inited = true;
            ResourceName = resourceName;
            VertexCount = vertexCount.ToString();
            VertexInfo = string.Format("{0}({1:F}%)", meshVertexCount, ((float)vertexCount / (float)meshVertexCount * 100f));
            PixelCount = pixelCount.ToString();
            Density = densityFloat.ToString("F");
            RendererHierarchyPath = rendererHierarchyPath;
            
            VertexCount_Int = vertexCount;
            PixelCount_Int = pixelCount;
            Density_float = densityFloat;
            ThresholdLevel = thresholdLevel;
            ProfilerColor = color;

            TileIndex = string.Empty;
            TileIndex_Int = 0;
            Density2 = string.Empty;
            Density2_float = 0;
        }
        
        public bool HasData(string s)
        {
            return !String.IsNullOrEmpty(s);
        }

        public string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (HasData(TileIndex)) sb.AppendFormat("TileIndex = {0}\n", TileIndex);
            if (HasData(RendererHierarchyPath)) sb.AppendFormat("RendererHierarchyPath = {0}\n", RendererHierarchyPath);
            if (HasData(VertexCount)) sb.AppendFormat("VertexCount = {0}\n", VertexCount);
            if (HasData(PixelCount)) sb.AppendFormat("PixelCount = {0}\n", PixelCount);
            if (HasData(Density)) sb.AppendFormat("Density = {0}\n", Density);
            if (HasData(Density2)) sb.AppendFormat("Density2 = {0}\n", Density2);

            return sb.ToString();
        }

        public int CompareTo(object obj)
        {
            if (obj is ProfilerDataContents)
            {
                ProfilerDataContents other = (ProfilerDataContents)obj;
                if (Density_float > 0f && other.Density_float > 0f)
                {
                    return Density_float > other.Density_float ? -1 : 1;
                }
                if (Density2_float > 0f && other.Density2_float > 0f)
                {
                    return Density2_float > other.Density2_float ? -1 : 1;
                }
                return TileIndex_Int < other.TileIndex_Int ? -1 : 1;
            }

            return -1;
        }
    }

    public class BatchProfilerDataContents : IComparable
    {
        public ProfilerDataContents RootProfilerDataContents;
        public List<ProfilerDataContents> ProfilerDataContentsList;
        public float MaxDensity;
        public BatchProfilerDataContents(
            ProfilerDataContents rootProfilerDataContents, 
            List<ProfilerDataContents> profilerDataContentsList,
            float maxDensity)
        {
            RootProfilerDataContents = rootProfilerDataContents;
            ProfilerDataContentsList = profilerDataContentsList;
            MaxDensity = maxDensity;
        }
        
        public int CompareTo(object obj)
        {
            if (obj is BatchProfilerDataContents)
            {
                BatchProfilerDataContents other = (BatchProfilerDataContents)obj;
                return MaxDensity > other.MaxDensity ? -1 : 1;
            }

            return -1;
        }
    }

    public static class VertexProfilerUtil
    {
        public static bool ForceReloadProfilerModeAfterScriptCompile = false;
        
        public static readonly int _EnableVertexProfiler = Shader.PropertyToID("_EnableVertexProfiler");
        public static readonly int _DisplayType = Shader.PropertyToID("_DisplayType");
        public static readonly int _RendererTotalNum = Shader.PropertyToID("_RendererTotalNum");
        public static readonly int _CameraWorldPosition = Shader.PropertyToID("_CameraWorldPosition");
        public static readonly int _UNITY_MATRIX_VP = Shader.PropertyToID("_UNITY_MATRIX_VP");
        public static readonly int _RendererBoundsDataBuffer = Shader.PropertyToID("_RendererBoundsDataBuffer");
        public static readonly int _VisibleFlagBuffer = Shader.PropertyToID("_VisibleFlagBuffer");
        public static readonly int _TileParams1 = Shader.PropertyToID("_TileParams1");
        public static readonly int _TileParams2 = Shader.PropertyToID("_TileParams2");
        public static readonly int _VertexData = Shader.PropertyToID("_VertexData");
        public static readonly int _VertexDataSize = Shader.PropertyToID("_VertexDataSize");
        public static readonly int _TileVerticesCount = Shader.PropertyToID("_TileVerticesCount");
        public static readonly int _LocalToWorld = Shader.PropertyToID("_LocalToWorld");
        public static readonly int _VertexNum = Shader.PropertyToID("_VertexNum");
        public static readonly int _ScreenParams = Shader.PropertyToID("_ScreenParams");
        public static readonly int _UNITY_UV_STARTS_AT_TOP = Shader.PropertyToID("_UNITY_UV_STARTS_AT_TOP");
        public static readonly int _UNITY_REVERSED_Z = Shader.PropertyToID("_UNITY_REVERSED_Z");
        public static readonly int _CullMode = Shader.PropertyToID("_CullMode");
        public static readonly int _ColorRangeSetting = Shader.PropertyToID("_ColorRangeSetting");
        public static readonly int _ColorRangeSettingCount = Shader.PropertyToID("_ColorRangeSettingCount");
        public static readonly int _TileProfilerRT = Shader.PropertyToID("_TileProfilerRT");
        public static readonly int _RendererIdAndVertexCountRT = Shader.PropertyToID("_RendererIdAndVertexCountRT");
        public static readonly int _TileProfilerRTUint = Shader.PropertyToID("_TileProfilerRTUint");
        public static readonly int _TileProfilerRTUint2 = Shader.PropertyToID("_TileProfilerRTUint2");
        public static readonly int _RenderIdAndDepthRT = Shader.PropertyToID("_RenderIdAndDepthRT");
        public static readonly int _OutputOverdrawRT = Shader.PropertyToID("_OutputOverdrawRT");
        public static readonly int _HeatMapTex = Shader.PropertyToID("_HeatMapTex");
        public static readonly int _HeatMapRange = Shader.PropertyToID("_HeatMapRange");
        public static readonly int _HeatMapStep = Shader.PropertyToID("_HeatMapStep");
        public static readonly int _HeatMapOffsetCount = Shader.PropertyToID("_HeatMapOffsetCount");
        public static readonly int _HeatMapRampRange = Shader.PropertyToID("_HeatMapRampRange");
        
        public static readonly int _RendererId = Shader.PropertyToID("_RendererId");
        public static readonly int _VertexCount = Shader.PropertyToID("_VertexCount");
        public static readonly int _VertexCounterBuffer = Shader.PropertyToID("_VertexCounterBuffer");
        public static readonly int _PixelCounterBuffer = Shader.PropertyToID("_PixelCounterBuffer");
        
        public static readonly int _TileWidth = Shader.PropertyToID("_TileWidth");
        public static readonly int _TileHeight = Shader.PropertyToID("_TileHeight");
        public static readonly int _TileNumX = Shader.PropertyToID("_TileNumX");
        public static readonly int _TileNumY = Shader.PropertyToID("_TileNumY");
        public static readonly int _TileCount = Shader.PropertyToID("_TileCount");
        
        public static readonly int _MainTex = Shader.PropertyToID("_MainTex");


        // 默认的顶点/棋盘格密度阈值设置，单位 顶点数/1万像素 (简单模式)
        public static readonly int[] SimpleModeOnlyTileDensitySetting = new int[3]
        {
            0,
            5000,
            10000
        };
        // 默认的顶点/棋盘格密度阈值设置，单位 顶点数/1万像素 (详细模式)
        public static readonly int[] DefaultOnlyTileDensitySetting = new int[8]
        {
            1000,
            2000,
            3000,
            4000,
            5000,
            8000,
            10000,
            12000
        };
        public static List<int> OnlyTileDensitySetting = new List<int>(DefaultOnlyTileDensitySetting);
        
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 mesh顶点数/1万像素 (简单模式)
        public static readonly int[] SimpleModeOnlyMeshDensitySetting = new int[3]
        {
            0,
            5000,
            10000
        };
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 mesh顶点数/1万像素 (详细模式)
        public static readonly int[] DefaultOnlyMeshDensitySetting = new int[8]
        {
            1000,
            2000,
            3000,
            4000,
            5000,
            8000,
            10000,
            12000
        };
        public static List<int> OnlyMeshDensitySetting = new List<int>(DefaultOnlyMeshDensitySetting);
        
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 mesh顶点数/1万像素 (简单模式)
        public static readonly int[] SimpleModeTileBasedMeshDensitySetting = new int[3]
        {
            1,
            5000,
            10000
        };
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 mesh顶点数/1万像素 (详细模式)
        public static readonly int[] DefaultTileBasedMeshDensitySetting = new int[8]
        {
            1000,
            2000,
            3000,
            4000,
            5000,
            8000,
            10000,
            12000
        };
        public static List<int> TileBasedMeshDensitySetting = new List<int>(DefaultTileBasedMeshDensitySetting);

        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 mesh顶点数/1万像素 (简单模式)
        public static readonly int[] SimpleModeMeshHeatMapSetting = new int[3]
        {
            0,
            2000,
            4000
        };
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 mesh顶点数/1万像素 (详细模式)
        public static readonly int[] DefaultMeshHeatMapSetting = new int[8]
        {
            500,
            1000,
            1500,
            2000,
            2500,
            3000,
            3500,
            4000
        };
        public static List<int> MeshHeatMapSetting = new List<int>(DefaultMeshHeatMapSetting);
        
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 重绘次数 (简单模式)
        public static readonly int[] SimpleModeOverdrawDensitySetting = new int[3]
        {
            0,
            2,
            5
        };
        // 默认的 mesh顶点/mesh占用像素密度阈值设置，单位 重绘次数 (详细模式)
        public static readonly int[] DefaultOverdrawDensitySetting = new int[8]
        {
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            8
        };
        public static List<int> OverdrawDensitySetting = new List<int>(DefaultOverdrawDensitySetting);
        
        // 阈值颜色设置 (简单模式)
        public static Color[] SimpleModeProfilerColor = new Color[3]
        {
            new Color(0, 1, 0, 1),
            new Color(1, 1, 0, 1),
            new Color(1, 0, 0, 1),
        };
        // 阈值颜色设置 (详细模式)
        public static Color[] DefaultProfilerColor = new Color[8]
        {
            new Color(0.5f, 1, 0.5f, 1),
            new Color(0, 1, 0, 1),
            new Color(0, 1, 1, 1),
            new Color(1, 1, 0, 1),
            new Color(0, 0.5f, 1f, 1),
            new Color(0, 0, 1, 1),
            new Color(1, 0, 1, 1),
            new Color(1, 0, 0, 1),
        };

        public static Color GetProfilerColor(int index, ProfilerType profilerType)
        {
            if (profilerType == ProfilerType.Simple && index < SimpleModeProfilerColor.Length)
            {
                return SimpleModeProfilerColor[index];
            }
            if (profilerType == ProfilerType.Detail && index < DefaultProfilerColor.Length)
            {
                return DefaultProfilerColor[index];
            }
            return Color.black;
        }

        public static void ActivateProfilerColor(int index, ProfilerType profilerType, bool activate)
        {
            if (profilerType == ProfilerType.Simple && index < SimpleModeProfilerColor.Length)
            {
                SimpleModeProfilerColor[index].a = activate ? 1.0f : 0.0f;
            }
            if (profilerType == ProfilerType.Detail && index < DefaultProfilerColor.Length)
            {
                DefaultProfilerColor[index].a = activate ? 1.0f : 0.0f;
            }
        }

        public static Dictionary<Color, Texture2D> pureColorTextureDict = new Dictionary<Color, Texture2D>();

        public static string GetGameObjectNameFromRoots(Transform trans)
        {
            if (trans == null)
            {
                return string.Empty;
            }

            // 递归获取所有父节点的名称
            string fullName = trans.name;
            Transform parent = trans.parent;

            while (parent != null)
            {
                fullName = parent.name + "/" + fullName;
                parent = parent.parent;
            }

            return fullName;
        }
        
        public static IOrderedEnumerable<T> Order<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector, bool ascending)
        {
            if (ascending)
            {
                return source.OrderBy(selector);
            }
            else
            {
                return source.OrderByDescending(selector);
            }
        }

        public static IOrderedEnumerable<T> ThenBy<T, TKey>(this IOrderedEnumerable<T> source, Func<T, TKey> selector, bool ascending)
        {
            if (ascending)
            {
                return source.ThenBy(selector);
            }
            else
            {
                return source.ThenByDescending(selector);
            }
        }

        public static NativeArray<T> ConvertToNativeArray<T>(List<T> list, Allocator allocator) where T : struct
        {
            NativeArray<T> nativeArray = new NativeArray<T>(list.Count, allocator);
            for (int i = 0; i < list.Count; i++)
            {
                nativeArray[i] = list[i];
            }
            return nativeArray;
        }
        
        public static NativeArray<T> ConvertToNativeArray<T>(T[] array, Allocator allocator) where T : struct
        {
            NativeArray<T> nativeArray = new NativeArray<T>(array.Length, allocator);
            for (int i = 0; i < array.Length; i++)
            {
                nativeArray[i] = array[i];
            }
            return nativeArray;
        }

        public static string GetOverrideTagName(string renderTypeTag, string blendSrcTag, string blendDstTag,
            int zwrite, CullMode cullMode)
        {
            string zwriteStr = "";
            if (renderTypeTag.Equals("Transparent")) // 透明队列默认不开深度写入
            {
                zwriteStr = (zwrite <= 0) ? "" : "WithZWriteOn";
            }
            else
            {
                zwriteStr = (zwrite == 0) ? "WithZWriteOff" : "";
            }

            string cullModeStr = "";
            if (cullMode != CullMode.Back)
            {
                cullModeStr = string.Format("WithCull{0}", cullMode.ToString());
            }

            return string.Concat(renderTypeTag, blendSrcTag, blendDstTag, zwriteStr, cullModeStr);
        }
        
        public static bool IsRTNeedReAlloc(RTHandle handle, RenderTextureDescriptor descriptor, FilterMode filterMode, TextureWrapMode wrapMode, bool isShadowMap, int anisoLevel, float mipMapBias, string name)
        {
            if (handle == null || handle.rt == null)
                return true;
            if (!handle.useScaling)
                return true;
            if ((handle.rt.width != descriptor.width || handle.rt.height != descriptor.height))
                return true;
            return
                handle.rt.descriptor.depthBufferBits != descriptor.depthBufferBits ||
                (handle.rt.descriptor.depthBufferBits == (int)DepthBits.None && !isShadowMap && handle.rt.descriptor.graphicsFormat != descriptor.graphicsFormat) ||
                handle.rt.descriptor.dimension != descriptor.dimension ||
                handle.rt.descriptor.enableRandomWrite != descriptor.enableRandomWrite ||
                handle.rt.descriptor.useMipMap != descriptor.useMipMap ||
                handle.rt.descriptor.autoGenerateMips != descriptor.autoGenerateMips ||
                handle.rt.descriptor.msaaSamples != descriptor.msaaSamples ||
                handle.rt.descriptor.bindMS != descriptor.bindMS ||
                handle.rt.descriptor.useDynamicScale != descriptor.useDynamicScale ||
                handle.rt.descriptor.memoryless != descriptor.memoryless ||
                handle.rt.filterMode != filterMode ||
                handle.rt.wrapMode != wrapMode ||
                handle.rt.anisoLevel != anisoLevel ||
                handle.rt.mipMapBias != mipMapBias ||
                handle.rt.enableRandomWrite != descriptor.enableRandomWrite ||
                handle.name != name;
        }

        public static bool ReAllocRTIfNeeded(ref RTHandle handle, RenderTextureDescriptor desc, FilterMode filterMode, TextureWrapMode wrapMode, bool isShadowMap, int anisoLevel = 1, float mipMapBias = 0, string name = "")
        {
            if (IsRTNeedReAlloc(handle, desc, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name))
            {
                handle?.Release();
                handle = RTHandles.Alloc(
                    desc.width,
                    desc.height,
                    1,
                    (DepthBits)desc.depthBufferBits,
                    desc.graphicsFormat,
                    filterMode, 
                    wrapMode, 
                    desc.dimension,
                    desc.enableRandomWrite,
                    desc.useMipMap,
                    desc.autoGenerateMips,
                    isShadowMap, 
                    anisoLevel, 
                    mipMapBias, 
                    (MSAASamples)desc.msaaSamples,
                    desc.bindMS,
                    desc.useDynamicScale,
                    desc.memoryless,
                    name
                    );
                return true;
            }

            return false;
        }
    }
}

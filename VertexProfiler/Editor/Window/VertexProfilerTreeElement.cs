using UnityEngine;

namespace VertexProfilerTool
{
    public class VertexProfilerTreeElement: TreeElement
    {
        // 阈值节点数据
        public int Threshold;
        // 页节点数据相关
        public int TileIndex, VertexCount, PixelCount;
        public float Density;
        public string VertexInfo, ResourceName, RendererHierarchyPath;
        public Color ProfilerColor;
        
        // 用于根节点
        public VertexProfilerTreeElement(string name, int depth, int id) : base (name, depth, id)
        {
            Threshold = -1;
            TileIndex = -1;
            VertexCount = 0;
            PixelCount = 0;
            Density = 0;
            ResourceName = "";
            RendererHierarchyPath = "";
            ProfilerColor = Color.white;
        }
        // 用于阈值节点
        public VertexProfilerTreeElement(string name, int depth, int id, int threshold, Color color) : base (name, depth, id)
        {
            Threshold = threshold;
            TileIndex = -1;
            VertexCount = 0;
            PixelCount = 0;
            Density = 0;
            ResourceName = "";
            RendererHierarchyPath = "";
            ProfilerColor = color;
        }
        
        public VertexProfilerTreeElement(
            string name, int depth, int id, // TreeElement
            int index, int vertexCount, float density2Float, Color color // tile类型数据
            ) : base (name, depth, id)
        {
            Threshold = -1;
            TileIndex = index;
            VertexCount = vertexCount;
            PixelCount = 0;
            Density = density2Float;
            ResourceName = "";
            RendererHierarchyPath = "";
            ProfilerColor = color;
        }
        
        public VertexProfilerTreeElement(
            string name, int depth, int id, // TreeElement
            string resourceName, int vertexCount, int pixelCount, float densityFloat, string rendererHierarchyPath, Color color // mesh类型数据
            ) : base (name, depth, id)
        {
            Threshold = -1;
            TileIndex = -1;
            VertexCount = vertexCount;
            PixelCount = pixelCount;
            Density = densityFloat;
            ResourceName = resourceName;
            RendererHierarchyPath = rendererHierarchyPath;
            ProfilerColor = color;
        }
        
        public VertexProfilerTreeElement(
            string name, int depth, int id, // TreeElement
            int index, string vertexInfo, string resourceName, int vertexCount, int pixelCount, 
            float densityFloat, string rendererHierarchyPath, Color color
        ) : base (name, depth, id)
        {
            Threshold = -1;
            TileIndex = index;
            VertexCount = vertexCount;
            PixelCount = pixelCount;
            Density = densityFloat;
            VertexInfo = vertexInfo;
            ResourceName = resourceName;
            RendererHierarchyPath = rendererHierarchyPath;
            ProfilerColor = color;
        }
    }
}


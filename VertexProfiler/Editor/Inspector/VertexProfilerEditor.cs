using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEditor;

namespace VertexProfilerTool
{
    [CustomEditor(typeof(VertexProfiler))]
    public class VertexProfilerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Note：现在不希望在Inspector面板调整参数了，统一到这边打开一个新的window
            if (GUILayout.Button("打开操作面板"))
            {
                VertexProfilerWindow.ShowWindow();
            }
        }
    }
}


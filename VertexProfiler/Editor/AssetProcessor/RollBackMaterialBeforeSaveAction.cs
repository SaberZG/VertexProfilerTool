using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VertexProfilerTool;

namespace VertexProfilerTool
{
    public class RollBackMaterialBeforeSaveAction : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            RendererCuller.RevertAllReplaceShader(RendererCuller.GetAllRenderers(true));
            return paths;
        }
    }
}

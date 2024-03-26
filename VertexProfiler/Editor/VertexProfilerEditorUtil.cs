using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VertexProfilerTool
{
    public class VertexProfilerEditorUtil
    {
        public static bool NamespaceExists(string desiredNamespace)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Any(t => t.Namespace == desiredNamespace);
        }
    
        
        public static bool GetBatchingForPlatform(out bool staticBatching, out bool dynamicBatching)
        {
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            return GetBatchingForPlatform(activeBuildTarget, out staticBatching, out dynamicBatching);
        }
        public static bool GetBatchingForPlatform(BuildTarget platform, out bool staticBatching, out bool dynamicBatching)
        {
            var playerSettingsType = typeof(PlayerSettings);
            var getBatchingForPlatformMethod = playerSettingsType.GetMethod("GetBatchingForPlatform", 
                BindingFlags.NonPublic | BindingFlags.Static);
            staticBatching = false;
            dynamicBatching = false;
            if (getBatchingForPlatformMethod == null) return false;

            object[] parameters = new object[3] { platform, 0, 0 };
            
            getBatchingForPlatformMethod.Invoke(null, parameters);
            staticBatching = (int)parameters[1] > 0;
            dynamicBatching = (int)parameters[2] > 0;
            return true;
        }

        public static void SetBatchingForPlatform(bool staticBatching, bool dynamicBatching)
        {
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            SetBatchingForPlatform(activeBuildTarget, staticBatching, dynamicBatching);
        }
        public static void SetBatchingForPlatform(BuildTarget platform, bool staticBatching, bool dynamicBatching)
        {
            var method = typeof(PlayerSettings).GetMethod("SetBatchingForPlatform", BindingFlags.Static | BindingFlags.Default | BindingFlags.NonPublic);
            if (method == null) return;
            
            object[] args = new object[3] { platform, staticBatching ? 1 : 0, dynamicBatching ? 1 : 0};
            method.Invoke(null, args);
        }

        /// <summary>
        /// 将渐变组件的颜色输出到texture中
        /// </summary>
        /// <param name="grad"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static Texture2D ConvertGradientToTexture(Gradient grad, int width = 256, int height = 8) {
            var gradTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            gradTex.filterMode = FilterMode.Bilinear;
            gradTex.wrapMode = TextureWrapMode.Clamp;
            float inv = 1f / width;
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var t = x * inv;
                    Color col = grad.Evaluate(t);
                    gradTex.SetPixel(x, y, col);
                }
            }
            gradTex.Apply();
            return gradTex;
        }
        
    }
}
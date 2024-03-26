using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    public static class ReplaceShaderGenerator
    {
        private static bool needRegenerateReplaceShader = false;
        private static readonly string ReplaceShaderPath = "Assets/VertexProfiler/Built-in/Shader/VertexProfilerReplaceShader.shader";
        private static string shaderText = "";
        private static Regex subShaderRegex = new Regex(@"SubShader\s*\{(?:[^{}]+|(?<open>{)|(?<-open>}))*\}(?(open)(?!))", RegexOptions.Multiline);
        private static Regex overrideTagRegex = new Regex(@"""VertexProfilerTag""\s*=\s*""([^""]*)""", RegexOptions.Multiline);
        private static Dictionary<string, string> subShaderCodeDict = new Dictionary<string, string>();

        private static readonly string shaderBaseCode = @"Shader ""VertexProfiler/VertexProfilerReplaceShader""
{{
    Properties
    {{
        _MainTex (""Texture"", 2D) = ""white"" {{}}

        // 被替换的shader需要根据项目的实际需求，添加剔除相关等参数
        // 0:Zero 1:One 2:DstColor 3:SrcColor 4:OneMinusDstColor 5: SrcAlpha 6:OneMinusSrcColor 7:DstAlpha 8:OneMinusDstAlpha 9:SrcAlphaSaturate 10:OneMinusSrcAlpha
        // BlendMode:https://docs.unity3d.com/ScriptReference/Rendering.BlendMode.html
        // _SrcBlend(""SrcBlend"", float) = 1.0
        // _DstBlend(""DstBlend"", float) = 0.0
        // _ZWrite(""ZWrite"", float) = 1.0
    }}
    // 需要根据项目的实际需求，新增多个RenderType类型的SubShader来完善对场景shader的替换
    // 内置的RenderType可以参考:https://docs.unity3d.com/Manual/SL-SubShaderTags.html
    
    // 以下是自动生成的代码框架
    {0}
}}";

        private static readonly string subShaderTemplate = @"
    SubShader
    {{
        Tags {{ ""RenderType""=""{0}"" ""VertexProfilerTag"" = ""{1}"" ""RenderedByReplacementCamera"" = ""True""}}

        Pass
        {{
            {2}
            ZWrite {3}
            Cull {4}
            ZTest LEqual
                
            CGPROGRAM
                #pragma target 4.5
                            
                #pragma vertex VPVert
                #pragma fragment VPFragment
                // make fog work
                #pragma multi_compile_fog
                #pragma enable_d3d11_debug_symbols

                #define VP_TRANSPARENT {5}
                #define VP_NEED_CLIP {6}
                #include ""./VertexProfilerCore.cginc""
            ENDCG
        }}
    }}";

        [InitializeOnLoadMethod]
        public static void LoadCurrentShader()
        {
            subShaderCodeDict.Clear();
            // 将Shader解析出来，拿到SubShader的部分
            shaderText = File.ReadAllText(ReplaceShaderPath);

            MatchCollection subShaderMatches = subShaderRegex.Matches(shaderText);
            foreach (Match subShaderMatch in subShaderMatches)
            {
                string subShaderCode = subShaderMatch.Value;
                if (subShaderCode.Trim().StartsWith("SubShader"))
                {
                    int vpTagIndex = subShaderCode.IndexOf("VertexProfilerTag");
                    if (vpTagIndex != -1)
                    {
                        // Debug.LogFormat("This sub shader has a VP tag.\n{0}", subShaderCode);
                        MatchCollection overrideTagMatches = overrideTagRegex.Matches(subShaderCode);
                        foreach (Match overrideTagMatch in overrideTagMatches)
                        {
                            string overrideTag = overrideTagMatch.Groups[1].Value;
                            subShaderCodeDict.Add(overrideTag, subShaderCode);
                            break;
                        }
                    }
                }
            }
        }

        [InitializeOnLoadMethod][DidReloadScripts]
        private static void OnScriptLoaded()
        {
            if (VertexProfilerEvent.RecordReplaceSubShaderEvent == null)
            {
                VertexProfilerEvent.RecordReplaceSubShaderEvent += ReplaceShaderGenerator.TryAddOverrideTRagSubShader;
            }
            if (VertexProfilerEvent.TriggerRegenerateReplaceShaderEvent == null)
            {
                VertexProfilerEvent.TriggerRegenerateReplaceShaderEvent += ReplaceShaderGenerator.GenerateNewReplaceShader;
            }
        }
        
        public static void TryAddOverrideTRagSubShader(string renderTypeTag, string blendSrcTag, string blendDstTag, int zwrite, CullMode cullMode)
        {
            string overrideTag = VertexProfilerUtil.GetOverrideTagName(renderTypeTag, blendSrcTag, blendDstTag, zwrite, cullMode);
            if (subShaderCodeDict.ContainsKey(overrideTag)) return;
            // 目前已有的三种渲染通道 Opaque Cutout Transparent
            // 需要根据不同的BlendMode,深度写入和剔除设置生成SubShader
            string subShaderCode = "";
            if (renderTypeTag.Equals("Opaque"))
            {
                subShaderCode = string.Format(subShaderTemplate,
                    renderTypeTag,
                    overrideTag,
                    "",
                    zwrite == 0 ? "Off" : "On",
                    cullMode.ToString(),
                    0,
                    0);
            }
            else if (renderTypeTag.Equals("Cutout"))
            {
                subShaderCode = string.Format(subShaderTemplate,
                    renderTypeTag,
                    overrideTag,
                    "",
                    zwrite == 0 ? "Off" : "On",
                    cullMode.ToString(),
                    0,
                    1);
            }
            else if (renderTypeTag.Equals("Opaque"))
            {
                subShaderCode = string.Format(subShaderTemplate,
                    renderTypeTag,
                    overrideTag,
                    string.Format("Blend {0} {1}", blendSrcTag, blendDstTag),
                    zwrite == 1 ? "On" : "Off",
                    cullMode.ToString(),
                    1,
                    0);
            }

            subShaderCodeDict.Add(overrideTag, subShaderCode);
            // 标记为需要重新创建shader
            needRegenerateReplaceShader = true;
        }

        public static void GenerateNewReplaceShader()
        {
            if (!needRegenerateReplaceShader) return;

            string subShaderCode = "";
            foreach (var kv in subShaderCodeDict)
            {
                subShaderCode += kv.Value + "\n\t";
            }
            string finalShaderCode = string.Format(shaderBaseCode, subShaderCode);

            StreamWriter writer = new StreamWriter(ReplaceShaderPath, false);
            writer.WriteLine(finalShaderCode);
            writer.Close();
            needRegenerateReplaceShader = false;
            
            UnityEditor.AssetDatabase.Refresh();
        }
    }
}
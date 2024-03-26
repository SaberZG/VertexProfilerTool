Shader "VertexProfiler/VertexProfilerReplaceShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 被替换的shader需要根据项目的实际需求，添加剔除相关等参数
        // 0:Zero 1:One 2:DstColor 3:SrcColor 4:OneMinusDstColor 5: SrcAlpha 6:OneMinusSrcColor 7:DstAlpha 8:OneMinusDstAlpha 9:SrcAlphaSaturate 10:OneMinusSrcAlpha
        // BlendMode:https://docs.unity3d.com/ScriptReference/Rendering.BlendMode.html
        // _SrcBlend("SrcBlend", float) = 1.0
        // _DstBlend("DstBlend", float) = 0.0
        // _ZWrite("ZWrite", float) = 1.0
    }
    // 需要根据项目的实际需求，新增多个RenderType类型的SubShader来完善对场景shader的替换
    // 内置的RenderType可以参考:https://docs.unity3d.com/Manual/SL-SubShaderTags.html
    
    // 以下是自动生成的代码框架
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "VertexProfilerTag" = "Opaque" "RenderedByReplacementCamera" = "True"}

        Pass
        {
            
            ZWrite On
            Cull Back
            ZTest LEqual
                
            CGPROGRAM
                #pragma target 4.5
                            
                #pragma vertex VPVert
                #pragma fragment VPFragment
                // make fog work
                #pragma multi_compile_fog
                #pragma enable_d3d11_debug_symbols

                #define VP_TRANSPARENT 0
                #define VP_NEED_CLIP 0
                #include "./VertexProfilerCore.cginc"
            ENDCG
        }
    }
	
}

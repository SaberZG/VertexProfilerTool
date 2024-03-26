Shader "VertexProfiler/URPVertexProfilerReplaceShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque"}

        Pass
        {
            Tags{"LightMode" = "UniversalForward"}
            Blend One Zero
            ZWrite true
            Cull Off
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma target 4.5
            
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VertexProfilerModeInclude.hlsl"
            #include "VertexProfilerURPCore.hlsl"
            
            ENDHLSL
        }
    }
}

Shader "VertexProfiler/URPOverdrawCalculateShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode"="UniversalForward"}

        Pass
        {
            Cull Back
            Blend One One
            HLSLPROGRAM
            
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                return o;
            }

            float frag (v2f i) : SV_Target
            {
                return 1;
            }
            ENDHLSL
        }
    }
}

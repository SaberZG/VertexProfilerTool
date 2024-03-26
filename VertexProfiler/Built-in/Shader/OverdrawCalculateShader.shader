Shader "VertexProfiler/OverdrawCalculateShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderedByReplacementCamera"="True"}

        Pass
        {
            Cull Back
            Blend One One
            CGPROGRAM
            
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            
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
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float frag (v2f i) : SV_Target
            {
                return 1;
            }
            ENDCG
        }
    }
}

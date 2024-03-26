/*
* @aAuthor: SaberGodLY
* @Description: 截屏（无UI），此shader用于做输出前的Gamma映射
*/
Shader "VertexProfiler/URPGammaCorrection"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			HLSLPROGRAM
			#pragma vertex FullscreenVert
			#pragma fragment frag
			
			#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Fullscreen.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D_X(_MainTex);
            SAMPLER(sampler_MainTex);

			half4 frag (Varyings i) : SV_Target
			{
				half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, i.uv);
				col.rgb = pow(col.rgb, 0.454545); // 1.0 / 2.2
				return col;
			}
			ENDHLSL
		}
	}
}
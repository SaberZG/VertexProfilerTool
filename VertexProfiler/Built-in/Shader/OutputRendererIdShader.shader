Shader "VertexProfiler/OutputRendererIdShader"
{
	Properties
	{
		// 需要根据项目的实际需求，添加剔除相关的参数控制像素计算时的片元计算shader
        // 如：_CullMode("CullMode", float) = 2.0 // 0:Off 1:Front 2:Back
        // 由MaterialPropertyBlock传入的在C#阶段就统计下来的RendererId
        [HideInInspector]_RendererId("RendererId", int) = 0
        [HideInInspector]_VertexCount("VertexCount", int) = 0
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"
			#include "VertexProfilerModeInclude.cginc"
			
			// MaterialPropertyBlock新增
            int _RendererId;
            int _VertexCount;
			
			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
			};
			
			v2f vert (appdata v)
			{
				v2f o;
				float4 posCS = UnityObjectToClipPos(v.vertex); 
				o.vertex = posCS;
				return o;
			}
			
			// half4 frag (v2f i) : SV_Target
			float2 frag (v2f i) : SV_Target
			{
				return float2(_RendererId, i.vertex.z);
			}
			ENDCG
		}
	}
}
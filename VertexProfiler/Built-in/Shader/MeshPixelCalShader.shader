Shader "VertexProfiler/MeshPixelCalShader"
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
        Tags { "RenderType"="Opaque" "RenderedByReplacementCamera"="True"}

        Pass
        {
            CGPROGRAM
            
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "VertexProfilerModeInclude.cginc"
            
            // MaterialPropertyBlock新增
            int _RendererId;
            int _VertexCount;

            // 外部传入
            // 调试类型
            uniform int _DisplayType;
            uniform int _TileCount;
            uniform int _TileNumX;
            uniform float4 _TileParams2; // 分块数据（1.0 / width, 1.0 / height, 1.0 / tileNumX, 1.0 / tileNumY）
            // 格式[RendererId] = uint，用于统计该Renderer在渲染时使用了多少个像素
            RWStructuredBuffer<uint> PixelCounterBuffer : register(u4);
            
            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 posWS : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.posWS = mul(UNITY_MATRIX_M, v.vertex);
                return o;
            }

            float2 frag (v2f i) : SV_Target
            {
                // 逐Mesh像素占用模式，则统计该对象的占用像素数
                if (_DisplayType == ONLY_MESH_MODE)
                {
                    InterlockedAdd(PixelCounterBuffer[_RendererId], 1);
                }
                // 棋盘格网格模式，统计该网格在对应棋盘格的占用像素数
                if (_DisplayType == TILE_BASED_MESH_MODE)
                {
                    float4 posCS = mul(UNITY_MATRIX_VP, float4(i.posWS, 1.0));
                    float4 screenPos = ComputeScreenPos(posCS);
                    float3 posHCS = screenPos.xyz / screenPos.w;
                    float2 texelPos = posHCS.xy * _ScreenParams.xy;
                    int2 tilePos = texelPos.xy * _TileParams2.xy;
                    int bufferIndex = _RendererId * _TileCount + tilePos.y * _TileNumX + tilePos.x;
                    InterlockedAdd(PixelCounterBuffer[bufferIndex], 1);
                }
                // +1是为了避免不参与的像素计算错误，也可以省掉一次对RT的初始化操作
                return float2(_RendererId + 1, _VertexCount);
            }
            ENDCG
        }
    }
}

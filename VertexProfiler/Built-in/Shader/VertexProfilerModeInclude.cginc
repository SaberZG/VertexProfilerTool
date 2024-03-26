#ifndef VERTEX_PROFILER_MODE_INCLUDE
#define VERTEX_PROFILER_MODE_INCLUDE

#define ONLY_TILE_MODE 0
#define ONLY_MESH_MODE 1
#define TILE_BASED_MESH_MODE 2
#define MESH_HEAT_MAP_MODE 3
#define OVERDRAW_MODE 4


// 将int按位分段存储到half4中
half4 EncodeIntToHalf4(int value)
{
    // 将int按位拆分
    half r = (value & 255) / 255.0;
    half g = ((value >> 8) & 255) / 255.0;
    half b = ((value >> 16) & 255) / 255.0;
    half a = ((value >> 24) & 255) / 255.0;

    // 将分段的值存储到half4中
    return half4(r, g, b, a);
}

// 将half4变回int
int DecodeHalf4ToInt(half4 encodedValue)
{
    // 将half4中的值重新组合成int
    int r = round(encodedValue.r * 255.0);
    int g = round(encodedValue.g * 255.0);
    int b = round(encodedValue.b * 255.0);
    int a = round(encodedValue.a * 255.0);

    // 组合成最终的int值
    return r | (g << 8) | (b << 16) | (a << 24);
}

#endif
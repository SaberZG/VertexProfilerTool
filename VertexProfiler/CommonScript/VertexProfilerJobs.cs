using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VertexProfilerTool
{
    /// <summary>
    /// 考虑到必须在CPU->GPU阶段尽可能减少数据传递，因此把遮挡剔除的部分移到CPU端来处理，使用JobSystem快速计算，还可以省掉一次回读操作
    /// </summary>
    public class VertexProfilerJobs
    {
        [BurstCompile]
        public struct J_Culling : IJob
        {
            [NoAlias][NativeDisableParallelForRestriction][ReadOnly]public NativeArray<RendererBoundsData> RendererBoundsData;
            [NoAlias][NativeDisableParallelForRestriction][ReadOnly]public NativeArray<Plane> CameraFrustumPlanes;
            [NativeDisableParallelForRestriction][WriteOnly]public NativeArray<uint> _VisibleFlagList;

            public void Execute()
            {
                for (int i = 0; i < RendererBoundsData.Length; i++)
                {
                    // 记录可以进行渲染的rendererId，其中groupIndex就是rendererId
                    int rendererId = i;
                    RendererBoundsData data = RendererBoundsData[rendererId];
                    uint isVisible = TestPlanesAABB(CameraFrustumPlanes, data.center, data.extends) ? 1u : 0u;
                    _VisibleFlagList[rendererId] = isVisible;
                }
            }
            
            bool TestPlanesAABB(NativeArray<Plane> planes, Vector3 center, Vector3 extents) // bounds.center bounds.extents
            {
                for (int i = 0; i < planes.Length; i++)
                {
                    Plane plane = planes[i];
                    float3 normal_sign = math.sign(plane.normal);
                    float3 test_point = (float3)(center) + (extents * normal_sign);
 
                    float dot = math.dot(test_point, plane.normal);
                    if (dot + plane.distance < 0)
                        return false;
                }
 
                return true;
            }
        }
    }
}
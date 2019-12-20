using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public struct TransformOffset : IComponentData
{
    public float3 Offset;
}


[ExecuteAlways]
[WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
public class PostLoadTransformOffsetSystem : JobComponentSystem
{
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (!HasSingleton<TransformOffset>())
            return inputDeps;

        var offset = GetSingleton<TransformOffset>().Offset;

        return Entities.ForEach((ref Translation translation) => { translation.Value += offset; }).Schedule(inputDeps);
    }
}

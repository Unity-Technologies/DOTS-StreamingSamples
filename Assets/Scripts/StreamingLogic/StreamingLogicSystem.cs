using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[AlwaysSynchronizeSystem]
public class StreamingLogicSystem : JobComponentSystem
{
    private EntityQuery _TileQuery;
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        float3 position = EntityManager.GetComponentData<LocalToWorld>(GetSingletonEntity<StreamingLogicConfig>()).Position;
        var config = GetSingleton<StreamingLogicConfig>();

        var cmd = new EntityCommandBuffer(Allocator.TempJob);
        
        var request = GetComponentDataFromEntity<RequestLoaded>();
        Entities.WithStoreEntityQueryInField(ref _TileQuery).ForEach((Entity entity, in ProceduralTileBoundingVolume bounds) =>
        {
            float sqDistance = bounds.Value.DistanceSq(position);
            if (sqDistance < config.DistanceForStreamingIn * config.DistanceForStreamingIn)
            {
                if (!request.HasComponent(entity))
                    cmd.AddComponent<RequestLoaded>(entity);
            }
            else if (sqDistance > config.DistanceForStreamingOut * config.DistanceForStreamingOut)
            {
                if (request.HasComponent(entity))
                    cmd.RemoveComponent<RequestLoaded>(entity);
            }
        }).Run();

        cmd.Playback(EntityManager);
        cmd.Dispose();

        return default;
    }

    protected override void OnCreate()
    {
        RequireSingletonForUpdate<StreamingLogicConfig>();
        RequireForUpdate(_TileQuery);
    }
}
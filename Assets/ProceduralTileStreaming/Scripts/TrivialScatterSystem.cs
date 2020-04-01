using Unity.Burst;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct TrivialScatterPrefabSettings : IBufferElementData
{
    public float ScatterRadius;
    public float3 PlacementOffset;
}

[AlwaysSynchronizeSystem]
[ExecuteAlways]
class TrivialScatterSystem : JobComponentSystem
{
    ScatterStreamingSystem m_ScatterSystem;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<ProceduralScatterPrefab>();
        m_ScatterSystem = World.GetExistingSystem<ScatterStreamingSystem>();
    }

    [BurstCompile]
    struct GeneratePointCloudJob : IJob
    {
        public NativeList<ProceduralInstanceData> Instances;
        [DeallocateOnJobCompletion]
        public NativeArray<TrivialScatterPrefabSettings> ScatterSettings;
        public float TileSize;
        public float3 TileOffset;

        int CountInstances(TrivialScatterPrefabSettings settings)
        {
            var count = (int)(TileSize / settings.ScatterRadius * 2) + 1;
            return count * count;
        }

        public void Execute()
        {
            var totalCount = 0;
            for (var s = 0; s != ScatterSettings.Length; s++)
                totalCount += CountInstances(ScatterSettings[s]);

            Instances.ResizeUninitialized(totalCount);

            var random = new Random(math.hash(TileOffset));

            var instancesArray = Instances.AsArray();
            int outputIndex = 0;
            for (var s = 0; s != ScatterSettings.Length; s++)
            {
                var count = CountInstances(ScatterSettings[s]);

                for (int i = 0; i != count; i++)
                {
                    var instance = new ProceduralInstanceData();
                    instance.Position = new float3(random.NextFloat(0, TileSize), 0, random.NextFloat(0, TileSize)) + TileOffset + ScatterSettings[s].PlacementOffset;
                    instance.Rotation = quaternion.AxisAngle(new float3(0, 1, 0), random.NextFloat(0, (float)math.PI));
                    instance.PrefabIndex = s;

                    instancesArray[outputIndex++] = instance;
                }

            }
            Assert.AreEqual(instancesArray.Length, outputIndex);
        }
    }

    struct LoadedState : ISystemStateComponentData
    {
        
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (m_ScatterSystem.ShouldGenerateTile)
        {
            Entities.WithStructuralChanges().WithAll<RequestLoaded>().WithNone<LoadedState>().ForEach((Entity tileEntity, in ProceduralTileMeta tile) =>
            {
                if (!m_ScatterSystem.ShouldGenerateTile)
                    return;
                
                var scatterSettings = EntityManager.GetBuffer<TrivialScatterPrefabSettings>(tile.PrefabSet);
                
                var job = new GeneratePointCloudJob();
                job.TileOffset = (float3)tile.Location * tile.TileSize;
                job.TileSize = tile.TileSize;
                job.Instances = new NativeList<ProceduralInstanceData>(0, Allocator.TempJob);
                job.ScatterSettings = new NativeArray<TrivialScatterPrefabSettings>(scatterSettings.AsNativeArray(), Allocator.TempJob);

                var genJob = job.Schedule();
                m_ScatterSystem.GenerateTileAsync(tile.Location, tileEntity, tile.PrefabSet, job.Instances, genJob);

                EntityManager.AddComponentData(tileEntity, new LoadedState());
            }).Run();
        }

        Entities.WithStructuralChanges().WithAll<LoadedState>().WithNone<RequestLoaded>().ForEach((Entity tileEntity) =>
        {
            if (m_ScatterSystem.UnloadTile(tileEntity))
                EntityManager.RemoveComponent<LoadedState>(tileEntity);
        }).Run();

        return default;
    }
}
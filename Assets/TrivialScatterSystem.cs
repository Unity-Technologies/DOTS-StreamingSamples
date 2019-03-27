using Unity.Burst;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public struct TrivialScatterPrefabSettings : IBufferElementData
{
    public float ScatterRadius;
}

/*
public struct TrivialScatterSettings : IBufferElementData
{
    public float TileSize;
}
*/


class TrivialScatterSystem : JobComponentSystem
{
    ScatterStreamingSystem m_ScatterSystem;
    protected override void OnCreateManager()
    {
        RequireSingletonForUpdate<ProceduralScatterPrefab>();
        m_ScatterSystem = World.GetExistingManager<ScatterStreamingSystem>();
    }

    int counter = 0;
    int maxDim = 10;
    float tileSize = 20;

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
                    instance.Position = new float3(random.NextFloat(0, TileSize), 0, random.NextFloat(0, TileSize)) + TileOffset;
                    instance.Rotation = quaternion.AxisAngle(new float3(0, 1, 0), random.NextFloat(0, (float)math.PI));
                    instance.Scale = 1F;
                    instance.PrefabIndex = s;

                    instancesArray[outputIndex++] = instance;
                }

            }
            Assert.AreEqual(instancesArray.Length, outputIndex);
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (!m_ScatterSystem.ShouldGenerateTile)
            return inputDeps;

        var scatterSingletonQuery = GetComponentGroup(typeof(ProceduralScatterPrefab));
        var scatterSingleton = scatterSingletonQuery.GetSingletonEntity();        
        
        var tile = new int3((counter % maxDim), 0, (counter / maxDim));

        var job = new GeneratePointCloudJob();
        job.TileOffset = (float3)tile * tileSize;
        job.TileSize = tileSize;
        //@TODO: Background temp job
        job.Instances = new NativeList<ProceduralInstanceData>(0, Allocator.Persistent);
        job.ScatterSettings = new NativeArray<TrivialScatterPrefabSettings>(EntityManager.GetBuffer<TrivialScatterPrefabSettings>(scatterSingleton).AsNativeArray(), Allocator.TempJob);

        var genJob = job.Schedule();
        
        m_ScatterSystem.GenerateTileAsync(tile, scatterSingleton, job.Instances, genJob);

        // Unload
        var unloadCounter = (counter + (maxDim * maxDim / 2) + 7) % ( maxDim * maxDim);
        var unloadTile = new int3((unloadCounter % maxDim), 0, (unloadCounter / maxDim));

        m_ScatterSystem.UnloadTile(unloadTile);
        
        counter++;
        if (counter >= maxDim * maxDim)
            counter = 0;

        return inputDeps;
    }
}
using Unity.Burst;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public struct TrivialScatterPrefabSettings : IBufferElementData
{
    public float ScatterRadius;
    public float3 PlacementOffset;
}

class TrivialScatterSystem : JobComponentSystem
{
    ScatterStreamingSystem m_ScatterSystem;
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<ProceduralScatterPrefab>();
        m_ScatterSystem = World.GetExistingSystem<ScatterStreamingSystem>();
    }

    int _Counter = 0;
    int _MaxDim = 10;
    float _TileSize = 20;

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

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (!m_ScatterSystem.ShouldGenerateTile)
            return inputDeps;

        var scatterSingletonQuery = GetEntityQuery(typeof(ProceduralScatterPrefab));
        var scatterSingleton = scatterSingletonQuery.GetSingletonEntity();        

        // Unload
        {
            var unloadCounter = (_Counter + (_MaxDim * _MaxDim / 2) + 7) % ( _MaxDim * _MaxDim);
            var unloadTile = new int3((unloadCounter % _MaxDim), 0, (unloadCounter / _MaxDim));

            m_ScatterSystem.UnloadTile(unloadTile, scatterSingleton);
        }

        // Generate
        {
            var tile = new int3((_Counter % _MaxDim), 0, (_Counter / _MaxDim));

            var job = new GeneratePointCloudJob();
            job.TileOffset = (float3)tile * _TileSize;
            job.TileSize = _TileSize;
            job.Instances = new NativeList<ProceduralInstanceData>(0, Allocator.TempJob);
            job.ScatterSettings = new NativeArray<TrivialScatterPrefabSettings>(EntityManager.GetBuffer<TrivialScatterPrefabSettings>(scatterSingleton).AsNativeArray(), Allocator.TempJob);

            var genJob = job.Schedule();
            
            m_ScatterSystem.GenerateTileAsync(tile, scatterSingleton, job.Instances, genJob);
        }
        
        
        _Counter++;
        if (_Counter >= _MaxDim * _MaxDim)
            _Counter = 0;

        return inputDeps;
    }
}
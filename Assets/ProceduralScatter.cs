using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Assertions;
using Random = Unity.Mathematics.Random;

public struct ProceduralScatterPrefab : IBufferElementData
{
    public Entity Prefab;
}

public struct ProceduralScatterTile : ISharedComponentData
{
    public int3 Position;
}

public struct ProceduralInstanceData
{
    public quaternion     Rotation;
    public float3         Position;
    public float          Scale;
    public int            PrefabIndex;
}

/*
public struct ProceduralScatterTileRequest : IComponentData
{
    public int3 Position;
}

public struct ProceduralScatterTileState : ISharedComponentData
{
    public int3 Position;
}
*/

struct TempProceduralScatterPrefabTag : IComponentData
{
    
}

class ScatterStreamingSystem : JobComponentSystem
{
    World m_StreamingWorld;

    struct GenerateTileJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public Entity                     ScatterPrefab;
        
        [DeallocateOnJobCompletion]
        public NativeArray<ProceduralInstanceData> InstanceData;

        public void Execute()
        {
            //@TODO: Copy reinterpreted data out is a bit awkward... 
            var tempPrefabs = Transaction.GetBuffer<ProceduralScatterPrefab>(ScatterPrefab).Reinterpret<Entity>().AsNativeArray();
            var prefabs = new NativeArray<Entity>(tempPrefabs, Allocator.Temp);

            //@TODO: Sort by prefab index and instantiate in batch

            for (int i = 0; i != InstanceData.Length; i++)
            {
                var instanceData = InstanceData[i];
                var instance = Transaction.Instantiate(prefabs[instanceData.PrefabIndex]);
                Transaction.SetComponentData(instance, new Translation {Value = instanceData.Position});

                if (!Transaction.HasComponent(instance, ComponentType.ReadWrite<Scale>()))
                    Transaction.AddComponent(instance, ComponentType.ReadWrite<Scale>());
                Transaction.SetComponentData(instance, new Scale {Value = instanceData.Scale});
            }
        }
    }

    Entity m_SrcScatterPrefab;
    Entity m_StreamingWorldScatterPrefab;
    ComponentGroup m_ScatterPrefabQuery;
    ComponentGroup m_InstancesQuery;

    void PrepareScatterPrefab(Entity scatterPrefab)
    {
        if (scatterPrefab == m_SrcScatterPrefab)
            return;

        m_SrcScatterPrefab = scatterPrefab;

        // Deep copy all of referenced prefabs
        var srcPrefabsTemp = EntityManager.GetBuffer<ProceduralScatterPrefab>(scatterPrefab).Reinterpret<Entity>().AsNativeArray();
        var srcPrefabs = new NativeArray<Entity>(srcPrefabsTemp, Allocator.Temp);

        var dstPrefabs = new NativeArray<Entity>(srcPrefabs.Length, Allocator.Temp);
        for (int i = 0; i != srcPrefabs.Length; i++)
            dstPrefabs[i] = EntityManager.Instantiate(srcPrefabs[i]);

        var scatterPrefabClone = EntityManager.CreateEntity(typeof(ProceduralScatterPrefab), typeof(TempProceduralScatterPrefabTag));
        EntityManager.GetBuffer<ProceduralScatterPrefab>(scatterPrefabClone).Reinterpret<Entity>().AddRange(dstPrefabs);
        
        // Add Prefab & ProceduralScatterPrefabTag to all prefab
        var commands = new EntityCommandBuffer(Allocator.Temp);

        for (int i = 0; i != dstPrefabs.Length; i++)
        {
            var linkedEntities = EntityManager.GetBuffer<LinkedEntityGroup>(dstPrefabs[i]);
            for (int j = 0; j != linkedEntities.Length; j++)
            {
                commands.AddComponent(linkedEntities[j].Value, default(TempProceduralScatterPrefabTag));
                commands.AddComponent(linkedEntities[j].Value, default(Prefab));
            }
        }
        commands.Playback(EntityManager);
        commands.Dispose();

        var entityRemapping = EntityManager.CreateEntityRemapArray(Allocator.TempJob);
        
        m_StreamingWorld.EntityManager.MoveEntitiesFrom(EntityManager, m_ScatterPrefabQuery, entityRemapping);

        m_StreamingWorldScatterPrefab = entityRemapping[scatterPrefabClone.Index].Target;
        Assert.AreEqual(m_StreamingWorld.EntityManager.GetBuffer<ProceduralScatterPrefab>(m_StreamingWorldScatterPrefab).Length, EntityManager.GetBuffer<ProceduralScatterPrefab>(scatterPrefab).Length);
            
        entityRemapping.Dispose();
        
        m_StreamingWorld.EntityManager.RemoveComponent(m_StreamingWorld.EntityManager.UniversalGroup, typeof(TempProceduralScatterPrefabTag));

    }

    public void GenerateTileAsync(int3 tile, Entity scatterPrefab, NativeArray<ProceduralInstanceData> instance, JobHandle dependency)
    {
        PrepareScatterPrefab(scatterPrefab);

        var job = new GenerateTileJob();
        job.Transaction = m_StreamingWorld.EntityManager.BeginExclusiveEntityTransaction();
        job.InstanceData = instance;
        job.ScatterPrefab = m_StreamingWorldScatterPrefab;
        
        job.Schedule(dependency).Complete();
        
        m_StreamingWorld.EntityManager.EndExclusiveEntityTransaction();
        
        var entityRemapping = m_StreamingWorld.EntityManager.CreateEntityRemapArray(Allocator.TempJob);
        EntityManager.MoveEntitiesFrom(m_StreamingWorld.EntityManager, m_InstancesQuery, entityRemapping);
        entityRemapping.Dispose();

    }
    
    public void UnloadTile(int3 tile)
    {
        
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        return inputDeps;
    }

    protected override void OnCreateManager()
    {       
        m_StreamingWorld = new World("StreamingWorld");

        m_ScatterPrefabQuery = GetComponentGroup(new EntityArchetypeQuery 
        { 
            All = new[] { ComponentType.ReadOnly<TempProceduralScatterPrefabTag>() }, 
            Options = EntityArchetypeQueryOptions.IncludePrefab | EntityArchetypeQueryOptions.IncludeDisabled
        });
        m_InstancesQuery = m_StreamingWorld.EntityManager.CreateComponentGroup(new EntityArchetypeQuery 
        { 
            None = new[] { ComponentType.ReadOnly<ProceduralScatterPrefab>() },
            Options = EntityArchetypeQueryOptions.IncludeDisabled
        });

    }

    protected override void OnDestroyManager()
    {
        m_InstancesQuery.Dispose();
        m_StreamingWorld.Dispose();
    }
}

class TrivialScatterSystem : JobComponentSystem
{
    protected override void OnCreateManager()
    {
        RequireSingletonForUpdate<ProceduralScatterPrefab>();
    }

    int counter = 0;
    

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        counter++;
        if (counter >= 10)
        {
            return inputDeps;
        }
        
        var scatterSingletonQuery = GetComponentGroup(typeof(ProceduralScatterPrefab));
        var scatterSingleton = scatterSingletonQuery.GetSingletonEntity();

        var prefabs = EntityManager.GetBuffer<ProceduralScatterPrefab>(scatterSingleton);
        
        var instanceArray = new NativeArray<ProceduralInstanceData>(512, Allocator.TempJob);
        var random = new Random(0x6E624EB7u);
        for (int i = 0; i != instanceArray.Length; i++)
        {
            var instance = new ProceduralInstanceData();
            instance.Position = new float3(counter * 10 + random.NextFloat(0, 10), 0, random.NextFloat(0, 20));
            instance.Rotation = random.NextQuaternionRotation();
            instance.Scale = 0.2F;
            instance.PrefabIndex = random.NextInt(0, prefabs.Length);

            instanceArray[i] = instance;
        }
        
        World.GetExistingManager<ScatterStreamingSystem>().GenerateTileAsync(new int3(counter, 0, 0), scatterSingleton, instanceArray, default(JobHandle));

        var blah = scatterSingletonQuery.GetSingletonEntity();
        
        return inputDeps;
    }
}
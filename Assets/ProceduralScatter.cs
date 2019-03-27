using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Assertions;

public struct ProceduralScatterPrefab : IBufferElementData
{
    public Entity Prefab;
}


public struct ProceduralScatterTile : ISharedComponentData
{
    public int3      Position;
    public Entity    ScatterPrefab;
}

public struct ProceduralInstanceData
{
    public quaternion     Rotation;
    public float3         Position;
    public float          Scale;
    public int            PrefabIndex;
}

struct TempProceduralScatterPrefabTag : IComponentData
{
    
}

class ScatterStreamingSystem : JobComponentSystem
{
    struct Stream
    {
        public World StreamingWorld;
        public bool  IsGeneratingTile;
        public int3  Tile;
        
        public NativeList<ProceduralInstanceData> ToBeDestroyedInstances;
//        public NativeHashMap<Entity, Entity> SrcToStreamingWorldScatterPrefab;
        
        public Entity SrcScatterPrefab;
        public Entity StreamingWorldScatterPrefab;
    }

    Stream m_Stream;
    ComponentGroup m_ScatterPrefabQuery;
    ComponentGroup m_StreamingInstancesQuery;
    ComponentGroup m_UnloadInstancesQuery;

    struct GenerateTileJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public Entity                     ScatterPrefab;
        
        public NativeArray<ProceduralInstanceData> InstanceData;

        public void Execute()
        {
            var prefabs = new NativeArray<Entity>(Transaction.GetBuffer<ProceduralScatterPrefab>(ScatterPrefab).Reinterpret<Entity>().AsNativeArray(), Allocator.Temp);

            //@TODO: Sort by prefab index and instantiate in batch

            for (int i = 0; i != InstanceData.Length; i++)
            {
                var instanceData = InstanceData[i];
                var instance = Transaction.Instantiate(prefabs[instanceData.PrefabIndex]);
                Transaction.SetComponentData(instance, new Translation {Value = instanceData.Position});
                Transaction.SetComponentData(instance, new Rotation {Value = instanceData.Rotation});
            }
        }
    }

    void PrepareScatterPrefab(ref Stream stream, Entity scatterPrefab)
    {
        if (scatterPrefab == stream.SrcScatterPrefab)
            return;

        stream.SrcScatterPrefab = scatterPrefab;

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
        
        stream.StreamingWorld.EntityManager.MoveEntitiesFrom(EntityManager, m_ScatterPrefabQuery, entityRemapping);

        stream.StreamingWorldScatterPrefab = entityRemapping[scatterPrefabClone.Index].Target;
        Assert.AreEqual(stream.StreamingWorld.EntityManager.GetBuffer<ProceduralScatterPrefab>(stream.StreamingWorldScatterPrefab).Length, EntityManager.GetBuffer<ProceduralScatterPrefab>(scatterPrefab).Length);
            
        entityRemapping.Dispose();
        
        stream.StreamingWorld.EntityManager.RemoveComponent(stream.StreamingWorld.EntityManager.UniversalGroup, typeof(TempProceduralScatterPrefabTag));
    }

    public bool ShouldGenerateTile
    {
        get { return !m_Stream.IsGeneratingTile; }
    }

    //@TODO: Warn about duplicate tiles
    public void GenerateTileAsync(int3 tile, Entity scatterPrefab, NativeList<ProceduralInstanceData> instance, JobHandle dependency)
    {
        if (!ShouldGenerateTile)
            throw new System.ArgumentException("GenerateTileAsync can only be called when ShouldGenerateTile returns true");
        
        PrepareScatterPrefab(ref m_Stream, scatterPrefab);

        var job = new GenerateTileJob();
        job.Transaction = m_Stream.StreamingWorld.EntityManager.BeginExclusiveEntityTransaction();
        job.InstanceData = instance.AsDeferredJobArray();
        job.ScatterPrefab = m_Stream.StreamingWorldScatterPrefab;

        m_Stream.ToBeDestroyedInstances = instance;
        
        m_Stream.StreamingWorld.EntityManager.ExclusiveEntityTransactionDependency = job.Schedule(dependency);
        m_Stream.Tile = tile;
        m_Stream.IsGeneratingTile = true;
    }
    
    public void UnloadTile(int3 tile)
    {
        m_UnloadInstancesQuery.SetFilter(new ProceduralScatterTile { Position = tile });
        EntityManager.DestroyEntity(m_UnloadInstancesQuery);
        m_UnloadInstancesQuery.ResetFilter();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (m_Stream.IsGeneratingTile && m_Stream.StreamingWorld.EntityManager.ExclusiveEntityTransactionDependency.IsCompleted)
        {
            m_Stream.StreamingWorld.EntityManager.EndExclusiveEntityTransaction();

            //@TODO: Move this to tile generation job
            m_Stream.StreamingWorld.EntityManager.AddSharedComponentData(m_StreamingInstancesQuery, new ProceduralScatterTile { Position = m_Stream.Tile});
            
            var entityRemapping = m_Stream.StreamingWorld.EntityManager.CreateEntityRemapArray(Allocator.TempJob);
            EntityManager.MoveEntitiesFrom(m_Stream.StreamingWorld.EntityManager, m_StreamingInstancesQuery, entityRemapping);
            entityRemapping.Dispose();
            
            //@TODO: Annoying that this can't be deallocated on the job
            m_Stream.ToBeDestroyedInstances.Dispose();
            
            m_Stream.IsGeneratingTile = false;
        }
            
        return inputDeps;
    }

    protected override void OnCreateManager()
    {       
        m_Stream.StreamingWorld = new World("StreamingWorld");

        m_ScatterPrefabQuery = GetComponentGroup(new EntityArchetypeQuery 
        { 
            All = new[] { ComponentType.ReadOnly<TempProceduralScatterPrefabTag>() }, 
            Options = EntityArchetypeQueryOptions.IncludePrefab | EntityArchetypeQueryOptions.IncludeDisabled
        });
        m_StreamingInstancesQuery = m_Stream.StreamingWorld.EntityManager.CreateComponentGroup(new EntityArchetypeQuery 
        { 
            None = new[] { ComponentType.ReadOnly<ProceduralScatterPrefab>() },
            Options = EntityArchetypeQueryOptions.IncludeDisabled
        });
        
        m_UnloadInstancesQuery = GetComponentGroup(new EntityArchetypeQuery 
        { 
            All = new[] { ComponentType.ReadOnly<ProceduralScatterTile>() }, 
            Options = EntityArchetypeQueryOptions.IncludePrefab | EntityArchetypeQueryOptions.IncludeDisabled
        });
        
        RequireForUpdate(GetComponentGroup(ComponentType.ReadOnly<ProceduralScatterPrefab>()));
    }

    protected override void OnDestroyManager()
    {
        m_Stream.StreamingWorld.EntityManager.EndExclusiveEntityTransaction();
        m_Stream.ToBeDestroyedInstances.Dispose();
        m_StreamingInstancesQuery.Dispose();
        m_Stream.StreamingWorld.Dispose();
    }
}
using System;
using System.Collections.Generic;
using Unity.Burst;
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
        
        public Entity SrcScatterPrefab;
        public Entity StreamingWorldScatterPrefab;
    }

    Stream m_Stream;
    EntityQuery m_ScatterPrefabQuery;
    EntityQuery m_StreamingInstancesQuery;
    EntityQuery m_UnloadInstancesQuery;

    [BurstCompile]
    struct SortBatchesJob : IJob
    {
        public NativeArray<ProceduralInstanceData> InstanceData;
        struct SortPrefabIndex : IComparer<ProceduralInstanceData>
        {
            public int Compare(ProceduralInstanceData x, ProceduralInstanceData y)
            {
                return x.PrefabIndex.CompareTo(y.PrefabIndex);
            }
        }
        public void Execute()
        {
            InstanceData.Sort(new SortPrefabIndex());
        }
    }
    
    struct GenerateTileJob : IJob
    {
        public ExclusiveEntityTransaction Transaction;
        public Entity                     ScatterPrefab;
        public NativeArray<ProceduralInstanceData> InstanceData;

        public void Execute()
        {
            var prefabs = new NativeArray<Entity>(Transaction.GetBuffer<ProceduralScatterPrefab>(ScatterPrefab).Reinterpret<Entity>().AsNativeArray(), Allocator.Temp);
            var instancesMem = new NativeArray<Entity>(InstanceData.Length, Allocator.Temp);

            for (int beginBatch = 0; beginBatch < InstanceData.Length;)
            {
                // Find the number of instances we are creating from the same prefab index
                // (InstanceData is sorted by previous job)
                int prefabIndex = InstanceData[beginBatch].PrefabIndex;
                int endBatch = beginBatch + 1;
                for (; endBatch < InstanceData.Length && InstanceData[endBatch - 1].PrefabIndex == prefabIndex; endBatch++) { }
                var batchCount = endBatch - beginBatch;

                var instances = instancesMem.GetSubArray(0, batchCount);
                
                // Instantiate in batch (it is significantly faster than instantiating one entity at a time)
                Transaction.Instantiate(prefabs[prefabIndex], instances);

                // Place objects
                for (int j = 0; j != batchCount; j++)
                {
                    Transaction.SetComponentData(instances[j], new Translation {Value = InstanceData[j + beginBatch].Position});
                    Transaction.SetComponentData(instances[j], new Rotation {Value = InstanceData[j + beginBatch].Rotation});
                }
                beginBatch = endBatch;
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
        
        stream.StreamingWorld.EntityManager.RemoveComponent(stream.StreamingWorld.EntityManager.UniversalQuery, typeof(TempProceduralScatterPrefabTag));
    }

    public bool ShouldGenerateTile
    {
        get { return !m_Stream.IsGeneratingTile; }
    }
    
    //@TODO: Warn about duplicate tiles
    public JobHandle GenerateTileAsync(int3 tile, Entity scatterPrefab, NativeList<ProceduralInstanceData> instance, JobHandle dependency)
    {
        if (!ShouldGenerateTile)
            throw new System.ArgumentException("GenerateTileAsync can only be called when ShouldGenerateTile returns true");
        
        PrepareScatterPrefab(ref m_Stream, scatterPrefab);

        var sortJob = new SortBatchesJob {InstanceData = instance.AsDeferredJobArray()};
        
        var job = new GenerateTileJob();
        job.Transaction = m_Stream.StreamingWorld.EntityManager.BeginExclusiveEntityTransaction();
        job.InstanceData = instance.AsDeferredJobArray();
        job.ScatterPrefab = m_Stream.StreamingWorldScatterPrefab;
        
        dependency = sortJob.Schedule(dependency);
        dependency = job.Schedule(dependency);
        dependency = instance.Dispose(dependency);
        
        m_Stream.StreamingWorld.EntityManager.ExclusiveEntityTransactionDependency = dependency;
        m_Stream.Tile = tile;
        m_Stream.IsGeneratingTile = true;

        return dependency;
    }
    
    public void UnloadTile(int3 tile, Entity scatterPrefab)
    {
        m_UnloadInstancesQuery.SetSharedComponentFilter(new ProceduralScatterTile { Position = tile, ScatterPrefab = scatterPrefab});
        EntityManager.DestroyEntity(m_UnloadInstancesQuery);
        m_UnloadInstancesQuery.ResetFilter();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (m_Stream.IsGeneratingTile && m_Stream.StreamingWorld.EntityManager.ExclusiveEntityTransactionDependency.IsCompleted)
        {
            m_Stream.StreamingWorld.EntityManager.EndExclusiveEntityTransaction();

            //@TODO: Move this to tile generation job
            m_Stream.StreamingWorld.EntityManager.AddSharedComponentData(m_StreamingInstancesQuery, new ProceduralScatterTile { Position = m_Stream.Tile, ScatterPrefab = m_Stream.SrcScatterPrefab });
            
            var entityRemapping = m_Stream.StreamingWorld.EntityManager.CreateEntityRemapArray(Allocator.TempJob);
            EntityManager.MoveEntitiesFrom(m_Stream.StreamingWorld.EntityManager, m_StreamingInstancesQuery, entityRemapping);
            entityRemapping.Dispose();
            
            m_Stream.IsGeneratingTile = false;
        }
            
        return inputDeps;
    }

    protected override void OnCreate()
    {       
        m_Stream.StreamingWorld = new World("StreamingWorld");

        m_ScatterPrefabQuery = GetEntityQuery(new EntityQueryDesc 
        { 
            All = new[] { ComponentType.ReadOnly<TempProceduralScatterPrefabTag>() }, 
            Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled
        });
        m_StreamingInstancesQuery = m_Stream.StreamingWorld.EntityManager.CreateEntityQuery(new EntityQueryDesc 
        { 
            None = new[] { ComponentType.ReadOnly<ProceduralScatterPrefab>() },
            Options = EntityQueryOptions.IncludeDisabled
        });
        
        m_UnloadInstancesQuery = GetEntityQuery(new EntityQueryDesc 
        { 
            All = new[] { ComponentType.ReadOnly<ProceduralScatterTile>() }, 
            Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled
        });
        
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<ProceduralScatterPrefab>()));
    }

    protected override void OnDestroy()
    {
        m_Stream.StreamingWorld.EntityManager.EndExclusiveEntityTransaction();
        m_StreamingInstancesQuery.Dispose();
        m_Stream.StreamingWorld.Dispose();
    }
}
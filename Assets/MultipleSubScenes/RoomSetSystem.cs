using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

struct RoomSetSystemMetaTag : IComponentData
{
}

struct RoomSetTileTag : IComponentData
{
}

struct RoomSetRequestGeneration : IComponentData
{
    public float TileSize;
}

struct RoomSetGeneratedTag : ISystemStateComponentData
{
}

struct RoomSetGenerationGroup : ISharedComponentData
{
    public Entity Group;
}

[ExecuteAlways]
class RoomSetSystem : SystemBase
{
    EntityQuery m_LoadPending;
    EntityQuery m_DungeonTiles;
    SceneSystem m_SceneSystem;
    SceneSystem.LoadParameters m_LoadParameters;

    EntityQuery m_RequestedGenerationQuery;
    EntityQuery m_CleanupGenerationQuery;
    EntityQuery m_CleanupTilesQuery;

    protected override void OnCreate()
    {
        m_RequestedGenerationQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadOnly<RoomSetRequestGeneration>(), },
            None = new[] {ComponentType.ReadOnly<RoomSetGeneratedTag>(), },
        });

        m_CleanupGenerationQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadOnly<RoomSetGeneratedTag>(), },
            None = new[] {ComponentType.ReadOnly<RoomSetRequestGeneration>(), },
        });

        m_CleanupTilesQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadOnly<RoomSetTileTag>(), ComponentType.ReadOnly<RoomSetGenerationGroup>(), },
        });

        m_LoadPending = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadOnly<RoomSetSystemMetaTag>(), },
            None = new[] {ComponentType.ReadOnly<ResolvedSectionEntity>(), }
        });

        m_DungeonTiles = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] {ComponentType.ReadOnly<RoomSetTileTag>(), }
        });

        m_SceneSystem = World.GetExistingSystem<SceneSystem>();

        m_LoadParameters = new SceneSystem.LoadParameters
        {
            Flags = SceneLoadFlags.NewInstance
        };

#if UNITY_EDITOR
        m_LoadParameters.Flags |= EditorApplication.isPlaying ? SceneLoadFlags.BlockOnImport : 0;
#else
        m_LoadParameters.Flags |= SceneLoadFlags.BlockOnImport;
#endif
    }

    protected override void OnUpdate()
    {
        // Is there a previous generated maze that should be unloaded?
        using (var cleanupEntities = m_CleanupGenerationQuery.ToEntityArray(Allocator.TempJob))
        {
            foreach (var entity in cleanupEntities)
            {
                m_CleanupTilesQuery.SetSharedComponentFilter(new RoomSetGenerationGroup {Group = entity});
                //Can not use query to destroy since the scene entities have linked entity groups
                using (var sceneEntitiesToDestroy = m_CleanupTilesQuery.ToEntityArray(Allocator.TempJob))
                {
                    for (int i = 0; i < sceneEntitiesToDestroy.Length; ++i)
                        EntityManager.DestroyEntity(sceneEntitiesToDestroy[i]);
                }

                m_CleanupTilesQuery.ResetFilter();
            }
        }

        EntityManager.RemoveComponent<RoomSetGeneratedTag>(m_CleanupGenerationQuery);

        // Are we still waiting on some meta data to load?
        bool allMetadataLoaded = true;
        Entities.WithoutBurst().WithAll<RoomSetSystemMetaTag>().ForEach(
            (Entity sceneEntity, in SceneReference sceneReference, in DynamicBuffer<ResolvedSectionEntity> sectionEntities) =>
            {
                if (sectionEntities.Length == 0)
                    allMetadataLoaded = false;
            }).Run();

        if (!allMetadataLoaded)
            return;

        var generationCount = m_RequestedGenerationQuery.CalculateEntityCount();
        if (generationCount == 0)
            return;

        var connectivityToSceneEntity = new NativeArray<ValueTuple<Entity, int>>(16, Allocator.Temp);

        var roomComponentAccessor = GetComponentDataFromEntity<RoomComponent>(true);
        Entities.WithoutBurst().WithAll<RoomSetSystemMetaTag>().ForEach(
            (Entity sceneEntity, in SceneReference sceneReference,
                in DynamicBuffer<ResolvedSectionEntity> sectionEntities) =>
            {
                for (int i = 0; i < sectionEntities.Length; ++i)
                {
                    var sectionEntity = sectionEntities[i].SectionEntity;
                    if (!roomComponentAccessor.HasComponent(sectionEntity))
                        continue;

                    var connectivity = roomComponentAccessor[sectionEntity].Connectivity;

                    for (int rot = 0; rot < 4; ++rot)
                    {
                        if (connectivityToSceneEntity[connectivity].Item1 == Entity.Null)
                        {
                            connectivityToSceneEntity[connectivity] = (sceneEntity, rot);
                            connectivity = ((connectivity << 1) | (connectivity >> 3)) & 0xf;
                        }
                    }
                }
            }).Run();

        var generationEntities = m_RequestedGenerationQuery.ToEntityArray(Allocator.TempJob);
        var generationParams = m_RequestedGenerationQuery.ToComponentDataArray<RoomSetRequestGeneration>(Allocator.TempJob);
        EntityManager.AddComponent<RoomSetGeneratedTag>(m_RequestedGenerationQuery);

        for (int genIndex = 0; genIndex < generationCount; ++genIndex)
        {
            var generationEntity = generationEntities[genIndex];
            var generationParam = generationParams[genIndex];

            var group = new RoomSetGenerationGroup {Group = generationEntity};

            const int RING = 2;
            const int SIZE = RING * 2 + 1;

            var maze = new NativeArray<int>(SIZE * SIZE, Allocator.Temp);
            maze[SIZE * RING + RING] = 0xf;
            var links = new NativeArray<int>(SIZE * SIZE, Allocator.Temp);

            var options = new NativeList<ValueTuple<int, int>>(Allocator.Temp);

            // NESW
            for (int ddd = 0; ddd < 50; ++ddd)
            {
                for (int i = 0; i < SIZE - 1; ++i)
                {
                    for (int j = 0; j < SIZE; ++j)
                    {
                        links[i + j * SIZE] |= maze[(i + 1) + j * SIZE] & (1 << 3);
                        links[(i + 1) + j * SIZE] |= maze[i + j * SIZE] & (1 << 1);
                        links[j + i * SIZE] |= maze[j + (i + 1) * SIZE] & (1 << 0);
                        links[j + (i + 1) * SIZE] |= maze[j + i * SIZE] & (1 << 2);
                    }
                }

                for (int i = 0; i < maze.Length; ++i)
                {
                    if (maze[i] == 0 && links[i] != 0)
                    {
                        maze[i] = Random.Range(0, 16) | ((links[i] << 2) | (links[i] >> 2)) & 0xf;
                    }
                }
            }

            for (int i = 0; i < SIZE - 1; ++i)
            {
                for (int j = 0; j < SIZE; ++j)
                {
                    var a = maze[(i + 1) + j * SIZE] & (1 << 3);
                    var b = maze[i + j * SIZE] & (1 << 1);
                    var c = maze[j + (i + 1) * SIZE] & (1 << 0);
                    var d = maze[j + i * SIZE] & (1 << 2);

                    a = ((a << 2) | (a >> 2)) & 0xf;
                    b = ((b << 2) | (b >> 2)) & 0xf;
                    c = ((c << 2) | (c >> 2)) & 0xf;
                    d = ((d << 2) | (d >> 2)) & 0xf;

                    maze[i + j * SIZE] &= (13 | a);
                    maze[(i + 1) + j * SIZE] &= (7 | b);
                    maze[j + i * SIZE] &= (11 | c);
                    maze[j + (i + 1) * SIZE] &= (14 | d);
                }
            }

            for (int x = -RING; x <= RING; ++x)
            {
                for (int z = -RING; z <= RING; ++z)
                {
                    if (x == 0 && z == 0)
                        continue;

                    var index = (x + RING) * SIZE + (z + RING);
                    var sceneEntity = connectivityToSceneEntity[maze[index]].Item1;
                    if (sceneEntity == Entity.Null)
                        continue;

                    sceneEntity = EntityManager.Instantiate(sceneEntity);
                    m_SceneSystem.LoadSceneAsync(sceneEntity, m_LoadParameters);
                    EntityManager.AddComponent<RoomSetTileTag>(sceneEntity);
                    EntityManager.AddComponent<DisableLiveLink>(sceneEntity);
                    EntityManager.AddSharedComponentData(sceneEntity, group);

                    var sceneOffset = new SubSceneOffset
                    {
                        Translation = new float3(x, 0, z) * generationParam.TileSize,
                        RotationInQuadrants = connectivityToSceneEntity[maze[index]].Item2,
                    };

                    var postLoadCommandBuffer = new PostLoadCommandBuffer();
                    postLoadCommandBuffer.CommandBuffer =
                        new EntityCommandBuffer(Allocator.Persistent, PlaybackPolicy.MultiPlayback);
                    var postLoadSingleton = postLoadCommandBuffer.CommandBuffer.CreateEntity();
                    postLoadCommandBuffer.CommandBuffer.AddComponent(postLoadSingleton, sceneOffset);
                    EntityManager.AddComponentData(sceneEntity, postLoadCommandBuffer);
                }
            }

            generationParams.Dispose();
            generationEntities.Dispose();
        }
    }
}

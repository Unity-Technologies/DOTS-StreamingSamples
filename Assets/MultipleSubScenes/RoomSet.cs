using System.Collections.Generic;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
public class RoomSet : MonoBehaviour
{
    private Dictionary<World, Dictionary<SceneAsset, Entity>> SceneEntities =
        new Dictionary<World, Dictionary<SceneAsset, Entity>>();

    private Dictionary<World, Entity> GenerationEntities = new Dictionary<World, Entity>();

    public void LoadTileSet(List<SceneAsset> sceneAssets)
    {
        DefaultWorldInitialization.DefaultLazyEditModeInitialize();

        foreach (var world in World.All)
        {
            var sceneSystem = world.GetExistingSystem<SceneSystem>();
            if (sceneSystem == null)
                continue;

            if (world.GetExistingSystem<RoomSetSystem>() == null)
                continue;

            if (!SceneEntities.TryGetValue(world, out var sceneEntities))
            {
                sceneEntities = new Dictionary<SceneAsset, Entity>();
                SceneEntities.Add(world, sceneEntities);
            }

            foreach (var sceneAsset in sceneAssets)
            {
                if (sceneEntities.ContainsKey(sceneAsset))
                    continue;

                var sceneGuid = new GUID(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sceneAsset)));
                var sceneEntity = sceneSystem.LoadSceneAsync(sceneGuid, new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.DisableAutoLoad
                });
                //TODO: this should not be needed, livelink should ignore scenes that are not loaded
                world.EntityManager.AddComponent<DisableLiveLink>(sceneEntity);
                world.EntityManager.AddComponent<RoomSetSystemMetaTag>(sceneEntity);

                sceneEntities.Add(sceneAsset, sceneEntity);
            }
        }
    }

    public void RequestGeneration(GUID guid, DungeonTiles dungeonTiles)
    {
        CleanupGeneration();

        foreach (var world in World.All)
        {
            if (world.GetExistingSystem<RoomSetSystem>() == null)
                continue;

            var generationEntity = world.EntityManager.CreateEntity(ComponentType.ReadWrite<RoomSetRequestGeneration>());

            var requestGeneration = new RoomSetRequestGeneration {TileSize = dungeonTiles.TileSize};
            world.EntityManager.SetComponentData(generationEntity, requestGeneration);

            GenerationEntities[world] = generationEntity;
            var sceneSystem = world.GetExistingSystem<SceneSystem>();
            if (sceneSystem == null)
                continue;

            var sceneEntity = sceneSystem.LoadSceneAsync(guid, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.NewInstance
            });
            world.EntityManager.AddComponent<RoomSetTileTag>(sceneEntity);
            world.EntityManager.AddSharedComponentData(sceneEntity, new RoomSetGenerationGroup {Group = generationEntity});
        }
    }

    public void CleanupGeneration()
    {
        foreach (var generationEntity in GenerationEntities)
        {
            var entityManager = generationEntity.Key.EntityManager;
            entityManager.DestroyEntity(generationEntity.Value);
        }
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        if(EditorApplication.isPlaying)
#endif
            CleanupGeneration();

        foreach (var sceneEntities in SceneEntities)
        {
            var entityManager = sceneEntities.Key.EntityManager;

            foreach (var entity in sceneEntities.Value)
            {
                entityManager.DestroyEntity(entity.Value);
            }
        }
    }
}

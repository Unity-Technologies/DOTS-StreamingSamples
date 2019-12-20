using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Scenes;
using Hash128 = Unity.Entities.Hash128;

[ExecuteAlways]
public class RoomLoader : MonoBehaviour
{
    [SerializeField] SubScene[] _Subscenes;
   
    
    void AddSceneEntities(Hash128 guid, float x, float y, float z)
    {
        foreach (var world in World.AllWorlds)
        {
            var sceneSystem = world.GetExistingSystem<SceneSystem>();
            if (sceneSystem != null)
            {
                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.DisableAutoLoad
                };
                var sceneEntity = sceneSystem.CreateSceneEntity(guid, loadParams);

                var a = world.EntityManager.CreateArchetype(typeof(TransformOffset));
                var commandBuffer = new EntityCommandBuffer(Allocator.Persistent);
                var entity = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(entity, new TransformOffset{ Offset = new float3(x,y,z)});
                
                var copy = world.EntityManager.Instantiate(sceneEntity);
                world.EntityManager.SetComponentData(copy, new RequestSceneLoaded { LoadFlags = 0 });
                world.EntityManager.AddComponentData(copy, new PostLoadCommandBuffer { CommandBuffer = commandBuffer});
            }
        }
    }

    
    private void OnEnable()
    {
        AddSceneEntities(_Subscenes[0].SceneGUID, 0, 0, 0);
        AddSceneEntities(_Subscenes[0].SceneGUID, 5, 0, 0);
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

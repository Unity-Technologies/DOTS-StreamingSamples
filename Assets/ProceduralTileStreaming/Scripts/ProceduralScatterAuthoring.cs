using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.WSA;

struct ProceduralTileMeta : IComponentData
{
    public int3    Location;
    public float   TileSize;
    public Entity  PrefabSet;
}
struct ProceduralTileBoundingVolume : IComponentData
{
    public AABB Value;
}

struct RequestLoaded : IComponentData
{
    
}

[RequiresEntityConversion]
public class ProceduralScatterAuthoring : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
{
    [Serializable]
    public struct Data
    {
        public GameObject Prefab;
        public float      ScatterRadius;
        public float3     PlacementOffset;
    }

    public float WorldSize = 1000;
    public int   TileSize = 50;
    public int   MaxTilesTotal = 10000;

    public Data[] Prefabs;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponents(entity, new ComponentTypes(typeof(ProceduralScatterPrefab), typeof(TrivialScatterPrefabSettings)));
        
        var scatterBuffer = dstManager.GetBuffer<ProceduralScatterPrefab>(entity);
        var scatterSettings = dstManager.GetBuffer<TrivialScatterPrefabSettings>(entity);

        for (int i = 0; i != Prefabs.Length; i++)
        {
            var prefabEntity = conversionSystem.GetPrimaryEntity(Prefabs[i].Prefab);
            scatterBuffer.Add(new ProceduralScatterPrefab { Prefab = prefabEntity});
            
            scatterSettings.Add(new TrivialScatterPrefabSettings { ScatterRadius = Prefabs[i].ScatterRadius, PlacementOffset = Prefabs[i].PlacementOffset});
        }

        int tileCount = (int)math.ceil(WorldSize / TileSize);
        int maxTileCountSqrt = (int)math.round(math.sqrt((float) MaxTilesTotal));
        tileCount = math.min(maxTileCountSqrt, tileCount);
        for (int x = 0; x != tileCount; x++)
        {
            for (int y = 0; y != tileCount; y++)
            {
                var tileEntity = conversionSystem.CreateAdditionalEntity(this);
                var location = new int3(x, 0, y);
                dstManager.AddComponentData(tileEntity, new ProceduralTileMeta
                {
                    Location = new int3(x, 0, y),
                    TileSize = TileSize,
                    PrefabSet = entity
                });
                dstManager.AddComponentData(tileEntity, new ProceduralTileBoundingVolume
                {
                    Value = new MinMaxAABB { Min = location * TileSize, Max = (location + 1) * TileSize }
                });
            }
        }
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        foreach(var prefab in Prefabs)
            referencedPrefabs.Add(prefab.Prefab);
    }
}
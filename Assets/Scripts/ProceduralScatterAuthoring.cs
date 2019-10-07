using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        foreach(var prefab in Prefabs)
            referencedPrefabs.Add(prefab.Prefab);
    }
}

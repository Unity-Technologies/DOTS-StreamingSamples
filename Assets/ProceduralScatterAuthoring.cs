using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[RequiresEntityConversion]
public class ProceduralScatterAuthoring : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
{
    public GameObject[] Prefabs;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var scatterBuffer = dstManager.AddBuffer<ProceduralScatterPrefab>(entity);
        for (int i = 0; i != Prefabs.Length; i++)
        {
            var prefabEntity = conversionSystem.GetPrimaryEntity(Prefabs[i]);
            scatterBuffer.Add(new ProceduralScatterPrefab { Prefab = prefabEntity});
        }
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.AddRange(Prefabs);
    }
}

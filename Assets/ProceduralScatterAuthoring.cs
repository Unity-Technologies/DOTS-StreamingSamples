using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[RequiresEntityConversion]
public class ProceduralScatterAuthoring : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
{
    [Serializable]
    public struct Data
    {
        public GameObject Prefab;
        public float      ScatterRadius;
    }

    public Data[] Prefabs;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponents(entity, new ComponentTypes(typeof(ProceduralScatterPrefab), typeof(TrivialScatterSettings)));
        
        var scatterBuffer = dstManager.GetBuffer<ProceduralScatterPrefab>(entity);
        var scatterSettings = dstManager.GetBuffer<TrivialScatterSettings>(entity);

        for (int i = 0; i != Prefabs.Length; i++)
        {
            var prefabEntity = conversionSystem.GetPrimaryEntity(Prefabs[i].Prefab);
            scatterBuffer.Add(new ProceduralScatterPrefab { Prefab = prefabEntity});
            
            scatterSettings.Add(new TrivialScatterSettings { ScatterRadius = Prefabs[i].ScatterRadius});
        }
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        foreach(var prefab in Prefabs)
            referencedPrefabs.Add(prefab.Prefab);
    }
}

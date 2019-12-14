using System;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct StreamingLogicConfig : IComponentData
{
    public float DistanceForStreamingIn;
    public float DistanceForStreamingOut;
}

public class StreamingLogicConfigComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public StreamingLogicConfig Config = new StreamingLogicConfig
    {
        DistanceForStreamingIn = 600,
        DistanceForStreamingOut = 800
    };

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, Config.DistanceForStreamingIn);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, Config.DistanceForStreamingOut);
    }

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, Config);
    }
}

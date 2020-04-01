using Unity.Entities;
using UnityEngine;

struct RoomComponent : IComponentData
{
    public int Connectivity;
}

public class RoomAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    public bool ConnectNorth;
    public bool ConnectSouth;
    public bool ConnectEast;
    public bool ConnectWest;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var sectionEntity = conversionSystem.GetSceneSectionEntity(entity);
        dstManager.AddComponentData(sectionEntity, new RoomComponent
        {
            Connectivity = 0
                | (ConnectNorth ? 1 : 0)
                | (ConnectEast ? 2 : 0)
                | (ConnectSouth ? 4 : 0)
                | (ConnectWest ? 8 : 0)
        });
    }
}

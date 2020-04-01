using Unity.Entities;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;

public struct SubSceneOffset : IComponentData
{
    public float3 Translation;
    public int RotationInQuadrants;
}

[WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
[UpdateInGroup(typeof(ProcessAfterLoadGroup))]
public class SubSceneOffsetSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<SubSceneOffset>();
    }

    protected override void OnUpdate()
    {
        var offset = GetSingleton<SubSceneOffset>();
        Entities.WithNone<Parent>().ForEach((ref Translation translation) =>
        {
            translation.Value += offset.Translation;
        }).Schedule();
        var rotationOffset = quaternion.RotateY(math.PI * offset.RotationInQuadrants / 2f);
        Entities.WithNone<Parent>().ForEach((ref Rotation rotation) =>
        {
            rotation.Value = math.normalize(math.mul(rotation.Value, rotationOffset));
        }).Schedule();
    }
}

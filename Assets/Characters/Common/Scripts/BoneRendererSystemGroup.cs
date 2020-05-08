using Unity.Entities;
using Unity.Animation;

[UpdateAfter(typeof(PostAnimationSystemGroup))]
public class BoneRendererSystemGroup : ComponentSystemGroup
{
}

[UpdateInGroup(typeof(BoneRendererSystemGroup))]
public class BoneRendererMatrixSystem : BoneRendererMatrixSystemBase
{
}

[UpdateInGroup(typeof(BoneRendererSystemGroup))]
[UpdateAfter(typeof(BoneRendererMatrixSystem))]
public class BoneRendererRenderingSystem : BoneRendererRenderingSystemBase
{
}
using Unity.Animation;
using Unity.Animation.Hybrid;
using Unity.DataFlowGraph;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

[RequiresEntityConversion]
[ConverterVersion("ClipPlayer", 1)]
public class ClipPlayer : MonoBehaviour, IConvertGameObjectToEntity
{
    public AnimationClip Clip;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        if (Clip == null)
            return;

        conversionSystem.DeclareAssetDependency(gameObject, Clip);

        dstManager.AddComponentData(entity, new PlayClipComponent
        {
            Clip = conversionSystem.BlobAssetStore.GetClip(Clip)
        });
    }
}

public struct PlayClipComponent : IComponentData
{
    public BlobAssetReference<Clip> Clip;
}

public struct PlayClipStateComponent : ISystemStateComponentData
{
    public GraphHandle Graph;
    public NodeHandle<ClipPlayerNode> ClipPlayerNode;
}

[UpdateBefore(typeof(PreAnimationSystemGroup))]
public class PlayClipSystem : SystemBase
{
    PreAnimationGraphSystem m_GraphSystem;
    EndSimulationEntityCommandBufferSystem m_ECBSystem;
    EntityQuery m_AnimationDataQuery;

    protected override void OnCreate()
    {
        base.OnCreate();
        m_GraphSystem = World.GetOrCreateSystem<PreAnimationGraphSystem>();
        // Increase the reference count on the graph system so it knows
        // that we want to use it.
        m_GraphSystem.AddRef();
        m_ECBSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();

        m_AnimationDataQuery = GetEntityQuery(new EntityQueryDesc()
        {
            None = new ComponentType[] { typeof(PlayClipComponent) },
            All = new ComponentType[] { typeof(PlayClipStateComponent) }
        });

        m_GraphSystem.Set.RendererModel = NodeSet.RenderExecutionModel.Islands;
    }

    protected override void OnDestroy()
    {
        if (m_GraphSystem == null)
            return;

        m_GraphSystem.RemoveRef();
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        Dependency.Complete();

        var ecb = m_ECBSystem.CreateCommandBuffer();

        // Create graph for entities that have a PlayClipComponent but no graph (PlayClipStateComponent)
        Entities
            .WithName("CreateGraph")
            .WithNone<PlayClipStateComponent>()
            .WithoutBurst()
            .WithStructuralChanges()
            .ForEach((Entity e, ref Rig rig, ref PlayClipComponent animation) =>
            {
                var state = CreateGraph(e, m_GraphSystem, ref rig, ref animation);
                ecb.AddComponent(e, state);
                ecb.AddComponent(e, m_GraphSystem.TagComponent);
            }).Run();

        // Update graph if the animation component changed
        Entities
            .WithName("UpdateGraph")
            .WithChangeFilter<PlayClipComponent>()
            .WithoutBurst()
            .ForEach((Entity e, ref PlayClipComponent animation, ref PlayClipStateComponent state) =>
            {
                m_GraphSystem.Set.SendMessage(state.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Clip, animation.Clip);
            }).Run();

        // Destroy graph for which the entity is missing the PlayClipComponent
        Entities
            .WithName("DestroyGraph")
            .WithNone<PlayClipComponent>()
            .WithoutBurst()
            .WithStructuralChanges()
            .ForEach((Entity e, ref PlayClipStateComponent state) =>
            {
                m_GraphSystem.Dispose(state.Graph);
            }).Run();


        if (m_AnimationDataQuery.CalculateEntityCount() > 0)
            ecb.RemoveComponent(m_AnimationDataQuery, typeof(PlayClipStateComponent));
    }

    /// <summary>
    /// The graph executes in the PreAnimationSystem, but because we connect an EntityNode to the output of the ClipPlayerNode,
    /// the AnimatedData buffer gets updated on the entity and can be used in other systems, such as the PostAnimationSystem.
    /// </summary>
    /// <param name="entity">An entity that has a PlayClipComponent and a Rig.</param>
    /// <param name="graphSystem">The PreAnimationGraphSystem.</param>
    /// <param name="rig">The rig that will get animated.</param>
    /// <param name="playClip">The clip to play.</param>
    /// <returns>Returns a StateComponent containing the NodeHandles of the graph.</returns>
    static PlayClipStateComponent CreateGraph(Entity entity, PreAnimationGraphSystem graphSystem, ref Rig rig, ref PlayClipComponent playClip)
    {
        GraphHandle graph = graphSystem.CreateGraph();
        var data = new PlayClipStateComponent
        {
            Graph = graph,
            ClipPlayerNode = graphSystem.CreateNode<ClipPlayerNode>(graph)
        };

        var deltaTimeNode = graphSystem.CreateNode<DeltaTimeNode>(graph);
        var entityNode = graphSystem.CreateNode(graph, entity);

        var set = graphSystem.Set;

        // Connect kernel ports
        set.Connect(deltaTimeNode, DeltaTimeNode.KernelPorts.DeltaTime, data.ClipPlayerNode, ClipPlayerNode.KernelPorts.DeltaTime);
        set.Connect(data.ClipPlayerNode, ClipPlayerNode.KernelPorts.Output, entityNode);

        // Send messages to set parameters on the ClipPlayerNode
        set.SetData(data.ClipPlayerNode, ClipPlayerNode.KernelPorts.Speed, 1.0f);
        set.SendMessage(data.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Configuration, new ClipConfiguration { Mask = ClipConfigurationMask.LoopTime });
        set.SendMessage(data.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Rig, rig);
        set.SendMessage(data.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Clip, playClip.Clip);

        return data;
    }
}

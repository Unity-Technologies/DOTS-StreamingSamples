using Unity.Animation;
using Unity.DataFlowGraph;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace AnimPreview
{
    public class AnimationClipPreviewer : MonoBehaviour, IConvertGameObjectToEntity
    {
        public AnimationClip Clip;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new PlayClipComponent
            {
                Clip = ClipBuilder.AnimationClipToDenseClip(Clip)
            });
        }
    }

    public struct PlayClipComponent : IComponentData
    {
        public BlobAssetReference<Clip> Clip;
    }

    public struct PlayClipStateComponent : ISystemStateComponentData
    {
        public NodeHandle<DeltaTimeNode>  DeltaTimeNode;
        public NodeHandle<ClipPlayerNode> ClipPlayerNode;
        public NodeHandle<ComponentNode>  EntityNode;
    }

    // Can't inherit SystemBase yet, since DFG NodeSet assumes JobComponentSystem
    [UpdateBefore(typeof(PreAnimationSystemGroup))]
    public class PlayClipSystem : JobComponentSystem
    {
        EndSimulationEntityCommandBufferSystem m_ECBSystem;
        EntityQuery m_AnimationDataQuery;

        // Unity.Animation.GraphSystemBase is set up to have a NodeSet shared across multiple
        // systems that want to use it, but this can cause leaks or double-deletes if systems are
        // destroyed in the wrong order.
        private NodeSet Set;

        static readonly ProfilerMarker k_ProfilerMarker = new ProfilerMarker("PlayClipSystem");

        protected override void OnCreate()
        {
            Set = new NodeSet(this);
            Set.RendererModel = NodeSet.RenderExecutionModel.Islands;

            m_ECBSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();

            m_AnimationDataQuery = GetEntityQuery(new EntityQueryDesc()
            {
                None = new ComponentType[] { typeof(PlayClipComponent) },
                All = new ComponentType[] { typeof(PlayClipStateComponent) }
            });
        }

        protected override void OnDestroy()
        {
            // Clean up all our nodes in the graph
            var nodes = Set;
            Entities
                .WithoutBurst()
                .WithStructuralChanges()
                .ForEach((Entity e, ref PlayClipStateComponent data) =>
                {
                    DestroyGraph(nodes, ref data);
                }).Run();

            Set.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            k_ProfilerMarker.Begin();

            inputDeps.Complete();

            var set = Set;
            var ecb = m_ECBSystem.CreateCommandBuffer();

            // Create graph for entities that have a PlayClipComponent but no graph
            Entities
                .WithName("CreateGraph")
                .WithNone<PlayClipStateComponent>()
                .WithoutBurst()
                .WithStructuralChanges()
                .ForEach((Entity e, ref Rig rig, ref PlayClipComponent animation) =>
                {
                    var state = CreateGraph(e, set, ref rig, ref animation);
                    ecb.AddComponent(e, state);
                    //ecb.AddComponent(e, TagComponent);
                }).Run();

            // Update graph if the animation component changed
            Entities
                .WithName("UpdateGraph")
                .WithChangeFilter<PlayClipComponent>()
                .WithoutBurst()
                .ForEach((Entity e, ref PlayClipComponent animation, ref PlayClipStateComponent state) =>
                {
                    set.SendMessage(state.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Clip, animation.Clip);
                }).Run();

            // Destroy graph for which the entity is missing the AnimationComponent
            Entities
                .WithName("DestroyGraph")
                .WithNone<PlayClipComponent>()
                .WithoutBurst()
                .WithStructuralChanges()
                .ForEach((Entity e, ref PlayClipStateComponent state) =>
                {
                    DestroyGraph(set, ref state);
                }).Run();

            ecb.RemoveComponent(m_AnimationDataQuery, typeof(PlayClipStateComponent));

            var nodeUpdate = Set.Update(inputDeps);

            k_ProfilerMarker.End();

            return nodeUpdate;
        }

        static PlayClipStateComponent CreateGraph(Entity entity, NodeSet set, ref Rig rig, ref PlayClipComponent playClip)
        {
            var data = new PlayClipStateComponent
            {
                DeltaTimeNode = set.Create<DeltaTimeNode>(),
                ClipPlayerNode = set.Create<ClipPlayerNode>(),
                EntityNode = set.CreateComponentNode(entity)
            };

            // Connect kernel ports
            set.Connect(data.DeltaTimeNode, DeltaTimeNode.KernelPorts.DeltaTime, data.ClipPlayerNode, ClipPlayerNode.KernelPorts.DeltaTime);
            set.Connect(data.ClipPlayerNode, ClipPlayerNode.KernelPorts.Output, data.EntityNode);

            // Send messages to set parameters on the ClipPlayerNode
            set.SetData(data.ClipPlayerNode, ClipPlayerNode.KernelPorts.Speed, 1.0f);
            set.SendMessage(data.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Configuration, new ClipConfiguration { Mask = ClipConfigurationMask.LoopTime });
            set.SendMessage(data.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Rig, rig);
            set.SendMessage(data.ClipPlayerNode, ClipPlayerNode.SimulationPorts.Clip, playClip.Clip);

            return data;
        }

        static void DestroyGraph(NodeSet nodes, ref PlayClipStateComponent stateComponent)
        {
            nodes.Destroy(stateComponent.DeltaTimeNode);
            nodes.Destroy(stateComponent.ClipPlayerNode);
            nodes.Destroy(stateComponent.EntityNode);
        }
    }
}

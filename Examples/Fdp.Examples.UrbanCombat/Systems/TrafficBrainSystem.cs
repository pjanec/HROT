using Fdp.Kernel;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Examples.UrbanCombat.Systems
{
    /// <summary>
    /// Tier-1 hardcoded brain for civilian entities (pedestrians, cars).
    /// <para>
    /// Each frame, writes a locomotion intent into <see cref="LocomotionChannel.ActiveAction"/>:
    /// <list type="bullet">
    ///   <item>If the entity perceives at least one threat
    ///         (<see cref="TargetMemory.Count"/> &gt; 0) → <see cref="NavigationConstants.ActionIdFlee"/>.</item>
    ///   <item>Otherwise → <see cref="NavigationConstants.ActionIdMoveTo"/> (wander / road-graph follow).</item>
    /// </list>
    /// </para>
    /// <para>
    /// Only processes entities with <see cref="SimTier.Value"/> == 1 so it never interferes
    /// with Tier-2 tactical actors driven by BTree or HSM brains.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChannelArbitrationSystem))]
    public class TrafficBrainSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var q = World.Query()
                .With<SimTier>()
                .With<LocomotionChannel>()
                .With<ActorCapabilityState>()
                .Build();

            foreach (var entity in q)
            {
                var tier = World.GetComponent<SimTier>(entity);

                // Only drive Tier-1 (civilian) entities.
                if (tier.Value != 1)
                    continue;

                var caps = World.GetComponent<ActorCapabilityState>(entity);
                if (!caps.Capabilities.HasFlag(ActorCapabilities.CanMove))
                    continue;

                ref var channel = ref World.GetComponentRW<LocomotionChannel>(entity);

                // Check threat awareness if the entity has a TargetMemory component.
                bool hasThreat = false;
                if (World.HasComponent<TargetMemory>(entity))
                {
                    var tm = World.GetComponent<TargetMemory>(entity);
                    hasThreat = tm.Count > 0;
                }

                channel.ActiveAction = hasThreat
                    ? NavigationConstants.ActionIdFlee    // 2 — flee from nearest threat
                    : NavigationConstants.ActionIdMoveTo; // 1 — wander / follow road graph

                // Stamp DoctrineInstanceId so ChannelArbitrationSystem does not clear
                // this frame's intent (ChannelArb guards on DoctrineInstanceId mismatch).
                // Only applied when the entity has a DoctrineState component.
                if (World.HasComponent<DoctrineState>(entity))
                {
                    var doctrine = World.GetComponent<DoctrineState>(entity);
                    channel.DoctrineInstanceId = doctrine.InstanceId;
                }
            }
        }
    }
}

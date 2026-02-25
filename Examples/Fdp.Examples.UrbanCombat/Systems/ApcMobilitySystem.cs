using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Systems;

namespace Fdp.Examples.UrbanCombat.Systems
{
    /// <summary>
    /// Demo-application system that strips <see cref="ActorCapabilities.CanMove"/> from any
    /// entity whose <see cref="Health.Current"/> has dropped below its maximum value.
    ///
    /// <para>
    /// <b>Purpose:</b> Bridges the gap between <c>DamageSystem</c> (which reduces
    /// <c>Health.Current</c> but only strips capabilities on lethal hits) and
    /// <c>HsmDamageBridgeSystem</c> (which watches for <c>CanMove</c> → cleared transitions
    /// to inject the HSM <c>MobilityLost</c> event). This system fires when ANY damage is
    /// received, reflecting the real-world behaviour of an APC being mission-killed by a
    /// hit — mobility is lost even before the vehicle is destroyed.
    /// </para>
    ///
    /// <para>
    /// <b>Execution order:</b> Runs in <see cref="SimulationSystemGroup"/> after
    /// <see cref="FDP.Toolkit.Combat.Systems.DamageSystem"/> (so health is already reduced)
    /// and before <see cref="HsmDamageBridgeSystem"/> (so the capability change is visible
    /// in the same frame).
    /// </para>
    ///
    /// <para>
    /// This system is intentionally scoped to the demo application — it is not a toolkit
    /// component. Real projects should model mobility loss through doctrine-specific
    /// HSM action delegates once DEBT-007 context threading is resolved.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageSystem))]
    [UpdateBefore(typeof(HsmDamageBridgeSystem))]
    public class ApcMobilitySystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var q = World.Query()
                .With<Health>()
                .With<ActorCapabilityState>()
                .With<BrainHsm128>()          // Only process HSM entities (i.e. the APC)
                .Build();

            foreach (var entity in q)
            {
                var health = World.GetComponent<Health>(entity);

                // Skip entities that are still at full health.
                if (health.Current >= health.Max)
                    continue;

                ref var caps = ref World.GetComponentRW<ActorCapabilityState>(entity);

                // Only strip if CanMove is currently set (avoid redundant writes).
                if ((caps.Capabilities & ActorCapabilities.CanMove) == 0)
                    continue;

                caps.Capabilities &= ~ActorCapabilities.CanMove;
            }
        }
    }
}

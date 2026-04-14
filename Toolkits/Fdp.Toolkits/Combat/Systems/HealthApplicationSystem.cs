using System;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="DamageAssessedEvent"/> events, checks authority, and applies
    /// the computed HP loss directly to the entity's <see cref="Health"/> component.
    ///
    /// <para>
    /// This system is the distributed counterpart of the local damage path in
    /// <see cref="DamageSystem"/>.  In a split topology, <c>EntityHitDamageIngressTranslator</c>
    /// delivers the DDS <c>EntityHitDamage</c> message as a <see cref="DamageAssessedEvent"/>
    /// on the Brain/Authority node's event bus; this system applies the damage there.
    /// </para>
    ///
    /// <para>
    /// <b>On reaching 0 HP:</b> <see cref="ActorCapabilities.CanMove"/> and
    /// <see cref="ActorCapabilities.CanShoot"/> are cleared so that downstream systems
    /// (e.g. <c>HsmDamageBridgeSystem</c>) can detect the mobility kill.  Entity destruction
    /// is deferred to a separate workstream task.
    /// </para>
    ///
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/>.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class HealthApplicationSystem : ComponentSystem
    {
        public HealthApplicationSystem()
        {
        }

        protected override void OnUpdate()
        {
            var events = World.Bus.Consume<DamageAssessedEvent>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var targetEntity = evt.HitEntity;

                // Skip if entity is no longer alive.
                if (!World.IsAlive(targetEntity))
                    continue;

                // Authority gate: only the owning node applies health changes.
                if (World.HasComponent<NetworkAuthority>(targetEntity))
                {
                    ref readonly var auth = ref World.GetComponentRO<NetworkAuthority>(targetEntity);
                    if (!auth.HasAuthority) continue;
                }

                // Require a Health component to be present.
                if (!World.HasComponent<Health>(targetEntity))
                    continue;

                // Apply damage with a floor of 0.
                ref var health = ref World.GetComponentRW<Health>(targetEntity);
                health.Current = MathF.Max(0f, health.Current - evt.TotalDamage);

                // At zero HP: strip mobility and shoot capabilities.
                if (health.Current <= 0f && World.HasComponent<ActorCapabilityState>(targetEntity))
                {
                    ref var caps = ref World.GetComponentRW<ActorCapabilityState>(targetEntity);
                    caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                }
                // Non-lethal hit (HP below max but above 0): strip only CanMove (PACK-M002).
                // This replaces the cross-domain ApcMobilityTriggerSystem so Brain-tier
                // HsmDamageBridgeSystem can detect the capability change and inject MobilityLost.
                else if (health.Current < health.Max && World.HasComponent<ActorCapabilityState>(targetEntity))
                {
                    ref var caps = ref World.GetComponentRW<ActorCapabilityState>(targetEntity);
                    caps.Capabilities &= ~ActorCapabilities.CanMove;
                }
            }
        }
    }
}

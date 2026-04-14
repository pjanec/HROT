using System;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="DetonationNotification"/> events, computes a flat HP loss
    /// value and publishes a <see cref="DamageAssessedEvent"/> for the authoritative
    /// node to apply to the entity's <c>Health</c> component.
    ///
    /// <para>
    /// <b>Authority gate:</b> Only publishes <see cref="DamageAssessedEvent"/> when the
    /// local node has authority over the target entity.  In the POC, the damage value is
    /// always <see cref="CombatConstants.DefaultBulletDamage"/>; armor penetration curves
    /// are deferred.
    /// </para>
    ///
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/> — runs after ingress
    /// translators so that <see cref="DetonationNotification"/> events published by
    /// <c>MunitionDetonationIngressTranslator</c> in the same tick are available.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class DamageCalculationSystem : ComponentSystem
    {
        public DamageCalculationSystem()
        {
        }

        protected override void OnUpdate()
        {
            var events = World.Bus.Consume<DetonationNotification>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                // PACK-P003: evt.Target is already a local ECS Entity handle.
                // Skip if the entity is not alive on this node.
                var targetEntity = evt.Target;
                if (!World.IsAlive(targetEntity)) continue;

                // Authority gate: only the owning node computes and publishes damage.
                // Fall through (authoritative) when no NetworkAuthority component is present
                // (single-node / AllInOne / unit-test scenario).
                if (World.HasComponent<NetworkAuthority>(targetEntity))
                {
                    ref readonly var auth = ref World.GetComponentRO<NetworkAuthority>(targetEntity);
                    if (!auth.HasAuthority) continue;
                }

                // POC: flat damage value; armor/penetration curves are deferred.
                World.Bus.Publish(new DamageAssessedEvent
                {
                    HitEntity   = targetEntity,
                    TotalDamage = CombatConstants.DefaultBulletDamage,
                });
            }
        }
    }
}

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
    /// <b>No entity-ownership gate:</b> This system runs exclusively on the Muscle node.
    /// Because the Muscle is the designated damage-calculation authority for all detonations
    /// it observes (via <c>HitResolutionSystem</c> or <c>MunitionDetonationIngressTranslator</c>),
    /// the existence of a live target entity is sufficient to publish the verdict.
    /// Entity CQRS ownership (Brain vs. Muscle) is not checked here.
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
            var events = World.Bus.Read<DetonationNotification>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.IsRemote) continue;

                // PACK-P003: evt.Target is already a local ECS Entity handle.
                // Skip if the entity is not alive on this node.
                var targetEntity = evt.Target;
                if (!World.IsAlive(targetEntity)) continue;

                // No entity-ownership gate here: DamageCalculationSystem runs exclusively
                // on the Muscle node. The fact that a DetonationNotification was emitted
                // (by HitResolutionSystem or MunitionDetonationIngressTranslator) is
                // sufficient — the Muscle is the designated damage-calculation authority for
                // all detonations it observes, regardless of which node owns the entity in
                // the CQRS ownership map.

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

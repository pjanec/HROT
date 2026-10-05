using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="DetonationNotification"/> events, computes the HP loss through <see cref="ArmorModel"/> (⭐ <c>CE-3071</c>; was flat)
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
    [UpdateInPhase(SystemPhase.Simulation)]
    public class DamageCalculationSystem : IEcsModuleSystem
    {
        public DamageCalculationSystem()
        {
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(DamageCalculationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.Read<DetonationNotification>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (evt.IsRemote) continue;

                // PACK-P003: evt.Target is already a local ECS Entity handle.
                // Skip if the entity is not alive on this node.
                var targetEntity = evt.Target;
                if (!repo.IsAlive(targetEntity)) continue;

                // No entity-ownership gate here: DamageCalculationSystem runs exclusively
                // on the Muscle node. The fact that a DetonationNotification was emitted
                // (by HitResolutionSystem or MunitionDetonationIngressTranslator) is
                // sufficient -- the Muscle is the designated damage-calculation authority for
                // all detonations it observes, regardless of which node owns the entity in
                // the CQRS ownership map.

                // ⭐⭐ CE-3071 (R-212) — ammunition vs armour, through the ONE model the AI also uses (ArmorModel): the
                //   face the shot came from (the shooter, or the hit point when the shooter is gone), that face's armour
                //   from the target's TKB type, and the round's penetration and damage the bullet carried. An unknown
                //   munition (an external detonation: penetration 0) keeps the flat default. Was: a flat 25 for every hit.
                float armour = 0f;
                if (evt.Penetration > 0f && repo.HasComponent<SimTransform>(targetEntity))
                {
                    var from = repo.IsAlive(evt.Shooter) && repo.HasComponent<SimTransform>(evt.Shooter)
                        ? repo.GetComponent<SimTransform>(evt.Shooter).Position
                        : new Vector3(evt.HitX, evt.HitY, evt.HitZ);
                    var facing = ArmorModel.FacingOf(repo.GetComponent<SimTransform>(targetEntity), from);
                    armour = ArmorModel.ArmourFor(CombatTkb.PlatformOf(repo, targetEntity), facing);
                }
                float damage = ArmorModel.HitDamage(evt.Penetration, evt.Damage, armour);

                repo.Bus.Publish(new DamageAssessedEvent
                {
                    HitEntity   = targetEntity,
                    TotalDamage = damage,
                });
            }
        }
    }
}

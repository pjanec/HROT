using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
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
    /// <see cref="ActorCapabilities.CanShoot"/> are cleared so downstream systems
    /// (e.g. <c>HsmDamageBridgeSystem</c>) detect the kill. ⛔ <b>The entity is NOT destroyed — a dead
    /// body stays in the world.</b>
    /// </para>
    ///
    /// <para>⚠⚠ <b><c>CE-267</c> WAS REVERTED HERE (<c>2026-09-13</c>).</b> 🔒 User ruling: <i>"dead entity
    /// should not vanish, it should stay dead in the world, every entity (no magic dead body vanishing)."</i>
    /// An earlier version published a <c>DestroyEntityCommand</c> at 0 HP so that downstream
    /// <c>!world.IsAlive(target)</c> checks (<c>AimAndFireExecutor</c>, <c>Action_AimAndFireSpecific</c>)
    /// would come true. ⛔ That conflated two different concepts: <c>IsAlive</c> is an ECS-EXISTENCE
    /// predicate (does the entity handle exist?), NOT a combat-death one. Deleting the body to satisfy an
    /// existence check was the wrong fix. <b>Combat-death is the STATE <c>Health.Current &lt;= 0</c></b>
    /// (+ capabilities stripped), which the target now carries while REMAINING in the world — the same
    /// convention the target-finding EQS already uses (<c>AreaQuerySolverSystem</c>, which skips
    /// <c>Health.Current &lt;= 0</c>). ⚠ The distributed-death defect CE-267 was meant to fix is real and
    /// is being chased on its own terms, not by removing the corpse.</para>
    ///
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/>.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class HealthApplicationSystem : IEcsModuleSystem
    {
        public HealthApplicationSystem()
        {
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(HealthApplicationSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.Read<DamageAssessedEvent>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var targetEntity = evt.HitEntity;

                // Skip if entity is no longer alive.
                if (!repo.IsAlive(targetEntity))
                    continue;

                // Authority gate: only the owning node applies health changes.
                if (repo.HasComponent<NetworkAuthority>(targetEntity))
                {
                    ref readonly var auth = ref repo.GetComponentRO<NetworkAuthority>(targetEntity);
                    if (!auth.HasAuthority) continue;
                }

                // Require a Health component to be present.
                if (!repo.HasComponent<Health>(targetEntity))
                    continue;

                // Apply damage with a floor of 0.
                ref var health = ref repo.GetComponentRW<Health>(targetEntity);
                health.Current = MathF.Max(0f, health.Current - evt.TotalDamage);

                // At zero HP: strip mobility and shoot capabilities. ⛔ The entity is NOT destroyed —
                //   a dead body stays in the world. 🔒 User ruling `2026-09-13`: *"dead entity should not
                //   vanish, it should stay dead in the world, every entity (no magic dead body
                //   vanishing)."*
                //
                // ⚠⚠ CE-267 REVERTED HERE `2026-09-13`. It published a DestroyEntityCommand at 0 HP to
                //   make downstream `!IsAlive(target)` checks come true — but `IsAlive` is an ECS-EXISTENCE
                //   predicate, not a combat-death one, and removing the body to satisfy it was the wrong
                //   fix: it conflates "the entity exists" with "the unit is alive." Combat-death is the
                //   STATE `Health.Current <= 0` (+ capabilities stripped), which the target now carries
                //   while remaining in the world. The EQS already reads death as `Health.Current <= 0`
                //   (AreaQuerySolverSystem), so the scenario terminates on health, not on removal.
                //   📄 DESIGN_Node_Roles_And_Policies.md — and the real distributed-death defect CE-267
                //   masked is being chased separately, not by deleting the corpse.
                if (health.Current <= 0f)
                {
                    if (repo.HasComponent<ActorCapabilityState>(targetEntity))
                    {
                        ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(targetEntity);
                        caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                    }
                }
                // Non-lethal hit (HP below max but above 0): strip only CanMove (PACK-M002).
                // This replaces the cross-domain ApcMobilityTriggerSystem so Brain-tier
                // HsmDamageBridgeSystem can detect the capability change and inject MobilityLost.
                else if (health.Current < health.Max && repo.HasComponent<ActorCapabilityState>(targetEntity))
                {
                    ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(targetEntity);
                    caps.Capabilities &= ~ActorCapabilities.CanMove;
                }
            }
        }
    }
}

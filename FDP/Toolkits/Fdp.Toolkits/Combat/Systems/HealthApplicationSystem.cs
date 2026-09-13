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
    /// <see cref="ActorCapabilities.CanShoot"/> are cleared so that downstream systems
    /// (e.g. <c>HsmDamageBridgeSystem</c>) can detect the mobility kill, and the entity is then
    /// <b>DESTROYED</b> through the replicating lifecycle path.
    /// </para>
    ///
    /// <para>✅✅✅ <b><c>CE-267</c> — the destruction is no longer deferred</b> <i>(<c>2026-09-13</c>; an
    /// earlier version of this comment said "Entity destruction is deferred to a separate workstream
    /// task", and that unfinished task was a live defect for every split Brain/Muscle deployment)</i>.
    ///
    /// <para>🔴 <b>What the gap cost.</b> <see cref="DamageSystem"/> — the LOCAL path — already destroyed
    /// at 0 HP. This one did not, so in a split topology a killed entity stayed ALIVE with
    /// <c>Health.Current == 0</c> forever. ⛔ <c>AimAndFireExecutor</c>'s ONLY success condition is
    /// <c>!world.IsAlive(target)</c>, so the fire action never completed, the behaviour tree could never
    /// leave its engage node, and the attacking platoon cycled advance→fire→withdraw <b>indefinitely,
    /// draining ammo into a corpse</b>. 📐 Measured on <c>hill-attack-close</c>: both hostiles at 0 HP by
    /// <c>t=161</c>, still cycling at <c>t=747</c>.</para>
    ///
    /// <para>⛔⛔ <b>WHY <c>DestroyEntityCommand</c> AND NOT <c>repo.DestroyEntity()</c> — this is the whole
    /// subtlety.</b> <see cref="DamageSystem"/> calls <c>repo.DestroyEntity()</c> directly, which is
    /// <b>LOCAL ONLY</b>: doing that here would remove the entity on the authority node and leave orphan
    /// ghosts on every peer. ⭐ The replicating path is this command → <c>NetworkSpawningSystem</c>'s
    /// <c>ProcessDestroy</c> — <b>exactly ONE consumer on every host</b> — which sets
    /// <c>EntityLifecycle.TearDown</c> and runs <c>ELM.BeginDestruction</c>, i.e. the two-ack teardown and
    /// the <c>EntityMaster</c> DISPOSE that purges peers. 📌 That loop was verified across three processes
    /// as <c>CE-144</c>.</para>
    ///
    /// <para>⭐ <b>Published on the 0-HP TRANSITION only</b> — <c>wasAlive</c> below. ⛔ A test on
    /// <c>Current &lt;= 0</c> alone would re-publish for every further <c>DamageAssessedEvent</c> against a
    /// corpse, and those keep arriving: the shooters do not stop until the entity is gone.</para>
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
                bool wasAlive = health.Current > 0f;          // CE-267 — the transition, not the state
                health.Current = MathF.Max(0f, health.Current - evt.TotalDamage);

                // At zero HP: strip mobility and shoot capabilities.
                if (health.Current <= 0f)
                {
                    if (repo.HasComponent<ActorCapabilityState>(targetEntity))
                    {
                        ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(targetEntity);
                        caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                    }

                    // ⭐⭐⭐ CE-267 — and now DESTROY it, through the REPLICATING path. See the class
                    //   summary for why this is a command and not repo.DestroyEntity().
                    // ⚠ Only on the transition: further DamageAssessedEvents against a corpse keep
                    //   arriving until the teardown completes, and each would re-publish.
                    if (wasAlive && repo.HasComponent<NetworkIdentity>(targetEntity))
                    {
                        repo.Bus.PublishManaged(new DestroyEntityCommand
                        {
                            NetworkId = repo.GetComponentRO<NetworkIdentity>(targetEntity).Value,
                            Reason    = "killed",
                            IsRemote  = false,
                        });
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

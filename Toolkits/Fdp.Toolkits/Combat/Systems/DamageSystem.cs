using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core to Combat.Contracts
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="HitEvent"/>s and applies damage to the struck entity's
    /// <see cref="Health"/> component.
    /// <para>
    /// <b>Execution phase:</b> <see cref="SimulationSystemGroup"/>.
    /// <see cref="HitEvent"/>s published during <c>Input</c> by <c>HitResolutionSystem</c>
    /// are available to <c>Simulation</c> systems via the bus swap.
    /// </para>
    /// <para>
    /// <b>Per event:</b>
    /// <list type="number">
    ///   <item>Verify the hit entity is still alive; skip if not.</item>
    ///   <item>Verify the hit entity has a <see cref="Health"/> component; skip if not.</item>
    ///   <item>Verify the bullet entity from <see cref="HitEvent.BulletEntity"/> is still alive; skip if not.</item>
    ///   <item>Read <see cref="BallisticProjectile.Damage"/> from the bullet entity.</item>
    ///   <item>Apply damage: <c>Health.Current -= Damage</c>, clamped to 0.</item>
    ///   <item>If <c>Health.Current == 0</c>: destroy the hit entity.</item>
    /// </list>
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class DamageSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;
            var events = view.ReadEvents<HitEvent>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                // Authority guard (BS1-T003): skip if a remote node owns this entity.
                // When no NetworkAuthority component is present, treat as authoritative
                // (single-node / AllInOne / unit-test scenario).
                if (view.HasComponent<NetworkAuthority>(evt.HitEntity))
                {
                    ref readonly var auth = ref view.GetComponentRO<NetworkAuthority>(evt.HitEntity);
                    if (!auth.HasAuthority) continue;
                }

                // 1. Skip if the target entity is already dead.
                if (!view.IsAlive(evt.HitEntity)) continue;

                // 2. Skip if the target has no health component (non-damageable entity).
                if (!view.HasComponent<Health>(evt.HitEntity)) continue;

                // 3. The bullet entity handle is carried directly in the event.
                //    Guard: bullet may have been consumed already (e.g. by HitResolutionSystem
                //    when DamageSystem is absent, or double-hit edge case).
                if (!view.IsAlive(evt.BulletEntity)) continue;

                // 4. Confirm the entity is actually a bullet (generation-safety check).
                if (!view.HasComponent<BallisticProjectile>(evt.BulletEntity)) continue;

                // 5. Read damage from the bullet.
                float damage = view.GetComponentRO<BallisticProjectile>(evt.BulletEntity).Damage;

                // 6. Apply damage to the hit entity's health.
                ref var health = ref repo.GetComponentRW<Health>(evt.HitEntity);
                health.Current -= damage;
                if (health.Current < 0f) health.Current = 0f;

                // 7. If lethal: strip capabilities first (HsmDamageBridgeSystem reads this in
                //    the same frame), then destroy the hit entity.
                if (health.Current <= 0f)
                {
                    // Strip CanMove + CanShoot so downstream systems (e.g. HsmDamageBridgeSystem)
                    // can detect mobility loss even though the entity is about to be removed.
                    if (view.HasComponent<ActorCapabilityState>(evt.HitEntity))
                    {
                        ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(evt.HitEntity);
                        caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                    }
                    repo.DestroyEntity(evt.HitEntity);
                }
                else if (view.HasComponent<ActorCapabilityState>(evt.HitEntity))
                {
                    // Non-lethal hit: strip CanMove so HsmDamageBridgeSystem can detect
                    // the mobility-kill transition (set->cleared) and inject MobilityLost.
                    ref var caps = ref repo.GetComponentRW<ActorCapabilityState>(evt.HitEntity);
                    caps.Capabilities &= ~ActorCapabilities.CanMove;
                }
            }
        }
    }
}

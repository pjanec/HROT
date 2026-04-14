using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel to Combat.Contracts
using Fdp.Kernel;
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
    ///   <item>Resolve the bullet entity via its raw index (<see cref="EntityRepository.GetEntityByIndex"/>).</item>
    ///   <item>Verify the bullet entity is alive; skip if not (DEBT-027 generational guard).</item>
    ///   <item>Read <see cref="BallisticProjectile.Damage"/> from the bullet entity.</item>
    ///   <item>Apply damage: <c>Health.Current -= Damage</c>, clamped to 0.</item>
    ///   <item>If <c>Health.Current == 0</c>: destroy the hit entity.</item>
    ///   <item>Destroy the bullet entity (single-hit semantics).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>DEBT-027 mitigation:</b> <c>HitEvent.BulletIndex</c> is a raw <c>int</c> index
    /// extracted from <c>PackBulletRayId</c>.  <see cref="EntityRepository.GetEntityByIndex"/>
    /// performs the generation lookup internally and returns <see cref="Entity.Null"/> when the
    /// slot is inactive.  The subsequent <see cref="EntityRepository.IsAlive"/> check then
    /// guards against the (rare) case where the slot has been recycled for a different entity.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class DamageSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var events = World.Bus.Consume<HitEvent>();
            if (events.Length == 0) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                // Authority guard (BS1-T003): skip if a remote node owns this entity.
                // When no NetworkAuthority component is present, treat as authoritative
                // (single-node / AllInOne / unit-test scenario).
                if (World.HasComponent<NetworkAuthority>(evt.HitEntity))
                {
                    ref readonly var auth = ref World.GetComponentRO<NetworkAuthority>(evt.HitEntity);
                    if (!auth.HasAuthority) continue;
                }

                // 1. Skip if the target entity is already dead.
                if (!World.IsAlive(evt.HitEntity)) continue;

                // 2. Skip if the target has no health component (non-damageable entity).
                if (!World.HasComponent<Health>(evt.HitEntity)) continue;

                // 3. Resolve the bullet entity from its raw index (DEBT-027 pattern).
                var bulletEntity = World.GetEntityByIndex(evt.BulletIndex);

                // 4. Guard: bullet may have been destroyed before DamageSystem ran.
                if (!World.IsAlive(bulletEntity)) continue;

                // Additional guard: confirm the entity at this slot is actually a bullet.
                // Protects against index recycling (DEBT-027).
                if (!World.HasComponent<BallisticProjectile>(bulletEntity)) continue;

                // 5. Read damage from the bullet.
                float damage = World.GetComponent<BallisticProjectile>(bulletEntity).Damage;

                // 6. Apply damage to the hit entity's health.
                ref var health = ref World.GetComponentRW<Health>(evt.HitEntity);
                health.Current -= damage;
                if (health.Current < 0f) health.Current = 0f;

                // 7. If lethal: strip capabilities first (HsmDamageBridgeSystem reads this in
                //    the same frame), then destroy the hit entity.
                if (health.Current <= 0f)
                {
                    // Strip CanMove + CanShoot so downstream systems (e.g. HsmDamageBridgeSystem)
                    // can detect mobility loss even though the entity is about to be removed.
                    if (World.HasComponent<ActorCapabilityState>(evt.HitEntity))
                    {
                        ref var caps = ref World.GetComponentRW<ActorCapabilityState>(evt.HitEntity);
                        caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);
                    }
                    World.DestroyEntity(evt.HitEntity);
                }
                else if (World.HasComponent<ActorCapabilityState>(evt.HitEntity))
                {
                    // Non-lethal hit: strip CanMove so HsmDamageBridgeSystem can detect
                    // the mobility-kill transition (set→cleared) and inject MobilityLost.
                    ref var caps = ref World.GetComponentRW<ActorCapabilityState>(evt.HitEntity);
                    caps.Capabilities &= ~ActorCapabilities.CanMove;
                }

                // 8. Destroy the bullet entity (single-hit — bullet is consumed on impact).
                World.DestroyEntity(bulletEntity);
            }
        }
    }
}

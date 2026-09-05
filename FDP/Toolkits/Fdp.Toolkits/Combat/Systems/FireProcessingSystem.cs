using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="WeaponFireIntent"/> events and spawns a bullet entity for each shot.
    ///
    /// <para>
    /// <b>BS1-T007:</b> Replaces the old <see cref="FireRequestEvent"/>-based firing loop.
    /// <see cref="WeaponFireIntent"/> now carries local ECS <see cref="Entity"/> handles
    /// directly (PACK-P003); this system uses them without any <c>NetworkEntityMap</c> lookup.
    /// </para>
    ///
    /// <para>
    /// <b>Execution phase:</b> <see cref="InputSystemGroup"/> — runs after the weapon dispatcher
    /// group so that fire-intent events are available in the current frame's consume window.
    /// </para>
    ///
    /// <para>
    /// <b>Per event:</b>
    /// <list type="number">
    ///   <item>Uses <see cref="WeaponFireIntent.Shooter"/> and <see cref="WeaponFireIntent.Target"/> directly.</item>
    ///   <item>Creates a new entity.</item>
    ///   <item>Adds <see cref="SimTransform"/> — position at the shot origin.</item>
    ///   <item>Adds <see cref="SimVelocity"/> — linear velocity = <c>direction × MuzzleVelocity</c>.</item>
    ///   <item>Adds <see cref="BallisticProjectile"/> — records shooter, damage, spawn tick.</item>
    ///   <item>Adds <see cref="PhysicsCollider"/> — small bounding circle for raycast broadphase.</item>
    ///   <item>Publishes <see cref="WeaponFireNotification"/> so the egress translator can
    ///         forward a muzzle-flash event to the IG.</item>
    /// </list>
    /// If the shooter entity is no longer alive the event is skipped.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class FireProcessingSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(FireProcessingSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.Read<WeaponFireIntent>();
            if (events.Length == 0) return;

            uint currentTick = repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var shooter = evt.Shooter;
                var target  = evt.Target;

                // Skip if either entity is no longer alive.
                if (!repo.IsAlive(shooter)) continue;
                if (!repo.IsAlive(target))  continue;

                // ⛔⛔ CE-198 — THERE IS DELIBERATELY NO NetworkAuthority GATE HERE.
                //
                // TD-6 added one ("only spawn if this node is authoritative over the shooter").
                // It could never pass on the only node that runs this system, and it silently
                // disabled the entire kill chain:
                //
                //   • NetworkAuthority.HasAuthority is PrimaryOwnerId == LocalNodeId.
                //   • The BRAIN is the primary owner — NetworkSpawningSystem stamps
                //     new NetworkAuthority(cmd.OwnerNodeId, _localNodeId) at creation, and
                //     HealthApplicationSystem relies on exactly that to apply damage.
                //   • On the MUSCLE these entities are ghosts, and EntityMasterIngressTranslator
                //     deliberately stamps the unknown-owner sentinel PrimaryOwnerId = -1.
                //   ⇒ measured live on hill-attack-close, every combatant on SimHost:
                //     { HasAuthority = false, PrimaryOwnerId = -1, LocalNodeId = 1 }
                //     ⇒ every WeaponFireIntent was skipped, WeaponFire/EntityHitDamage
                //       published 0 samples, and no entity could ever be killed.
                //
                // The design is explicit that the Muscle executes the shot the Brain ordered
                // (BS-1-DESIGN.md §2.1: "Brain ── WeaponFireIntent ─► WeaponFireRequest ─► Muscle
                // ── spawns bullet"), so the Muscle is CORRECTLY not the primary owner.
                //
                // TD-6's real concern was several nodes spawning duplicate bullets. That is a
                // COMPOSITION property, and it is now structurally enforced: only the node whose
                // role composes the combat capability schedules this system (measured live on
                // --mode all: exactly one of three subsystems carries FireProcessingSystem).
                // A runtime flag that is false by construction cannot express it.

                // Skip if the shooter does not yet have a WeaponState (e.g. incomplete spawn).
                if (!repo.HasComponent<WeaponState>(shooter)) continue;

                // Read muzzle velocity from the shooter's WeaponState.
                var weapon      = repo.GetComponent<WeaponState>(shooter);
                var shooterPos  = repo.GetComponent<SimTransform>(shooter).Position;
                var targetPos   = repo.GetComponent<SimTransform>(target).Position;

                // Compute normalised direction from shooter toward target.
                var delta     = targetPos - shooterPos;
                var direction = delta.LengthSquared() > 0f
                    ? Vector3.Normalize(delta)
                    : Vector3.UnitX;    // fallback: fire east if entities are co-located
                var velocity  = direction * weapon.MuzzleVelocity;

                // 1. Spawn the bullet entity.
                var bullet = repo.CreateEntity();

                // 2. Spatial transform — position at the shot origin.
                repo.AddComponent(bullet, new SimTransform
                {
                    Position = shooterPos,
                    Rotation = Quaternion.Identity,
                });

                // 3. Kinematics — velocity inherited from muzzle velocity; no angular spin.
                repo.AddComponent(bullet, new SimVelocity
                {
                    Linear  = velocity,
                    Angular = Vector3.Zero,
                });

                // 4. Ballistic tag — used by BallisticsSystem and DamageSystem.
                repo.AddComponent(bullet, new BallisticProjectile
                {
                    Shooter          = shooter,
                    PreviousPosition = shooterPos,
                    Damage           = CombatConstants.DefaultBulletDamage,
                    SpawnTick        = currentTick,
                });

                // 5. Physics collider — small sphere for broadphase candidate selection.
                repo.AddComponent(bullet, new PhysicsCollider
                {
                    Radius         = CombatConstants.BulletColliderRadius,
                    CollisionLayer = CombatConstants.BulletCollisionLayer,
                });

                // 6. Notify egress translator (muzzle flash for IG).
                //    Entity handles are passed; WeaponFireNotificationEgressTranslator resolves
                //    them to network IDs on the egress boundary.
                repo.Bus.Publish(new WeaponFireNotification
                {
                    Shooter     = shooter,
                    Target      = target,
                    WeaponIndex = evt.WeaponIndex,
                });
            }
        }
    }
}

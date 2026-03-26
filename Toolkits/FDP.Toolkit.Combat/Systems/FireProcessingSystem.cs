using System;
using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="WeaponFireIntent"/> events and spawns a bullet entity for each shot.
    ///
    /// <para>
    /// <b>BS1-T007:</b> Replaces the old <see cref="FireRequestEvent"/>-based firing loop.
    /// <see cref="WeaponFireIntent"/> carries stable network entity IDs; this system resolves
    /// them to local <see cref="Entity"/> handles via <see cref="NetworkEntityMap"/>, computes
    /// the muzzle direction from the entities' <see cref="SimTransform"/> positions, and
    /// publishes a <see cref="WeaponFireNotification"/> after the bullet entity is created.
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
    ///   <item>Resolve shooter and target entities via <see cref="NetworkEntityMap"/>; skip if either is missing.</item>
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
    [UpdateInGroup(typeof(InputSystemGroup))]
    public class FireProcessingSystem : ComponentSystem
    {
        private readonly NetworkEntityMap _entityMap;

        /// <param name="entityMap">
        /// Shared network entity map used to resolve <see cref="WeaponFireIntent"/> IDs to
        /// local <see cref="Entity"/> handles.  Required; must not be null.
        /// </param>
        public FireProcessingSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        protected override void OnUpdate()
        {
            var events = World.Bus.Consume<WeaponFireIntent>();
            if (events.Length == 0) return;

            uint currentTick = World.HasSingleton<GlobalTime>()
                ? (uint)World.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                // Resolve network IDs to local entity handles.
                if (!_entityMap.TryGetEntity(evt.ShooterEntityId, out var shooter)) continue;
                if (!_entityMap.TryGetEntity(evt.TargetEntityId,  out var target))  continue;

                // TD-6 authority gate: skip if a remote node owns the shooter.
                // When no NetworkAuthority component is present, treat as authoritative
                // (single-node / AllInOne / unit-test scenario).
                if (World.HasComponent<NetworkAuthority>(shooter))
                {
                    ref readonly var auth = ref World.GetComponentRO<NetworkAuthority>(shooter);
                    if (!auth.HasAuthority) continue;
                }

                // Skip if either entity is no longer alive.
                if (!World.IsAlive(shooter)) continue;
                if (!World.IsAlive(target))  continue;

                // Read muzzle velocity from the shooter's WeaponState.
                var weapon      = World.GetComponent<WeaponState>(shooter);
                var shooterPos  = World.GetComponent<SimTransform>(shooter).Position;
                var targetPos   = World.GetComponent<SimTransform>(target).Position;

                // Compute normalised direction from shooter toward target.
                var delta     = targetPos - shooterPos;
                var direction = delta.LengthSquared() > 0f
                    ? Vector3.Normalize(delta)
                    : Vector3.UnitX;    // fallback: fire east if entities are co-located
                var velocity  = direction * weapon.MuzzleVelocity;

                // 1. Spawn the bullet entity.
                var bullet = World.CreateEntity();

                // 2. Spatial transform — position at the shot origin.
                World.AddComponent(bullet, new SimTransform
                {
                    Position = shooterPos,
                    Rotation = Quaternion.Identity,
                });

                // 3. Kinematics — velocity inherited from muzzle velocity; no angular spin.
                World.AddComponent(bullet, new SimVelocity
                {
                    Linear  = velocity,
                    Angular = Vector3.Zero,
                });

                // 4. Ballistic tag — used by BallisticsSystem and DamageSystem.
                World.AddComponent(bullet, new BallisticProjectile
                {
                    Shooter          = shooter,
                    PreviousPosition = shooterPos,
                    Damage           = CombatConstants.DefaultBulletDamage,
                    SpawnTick        = currentTick,
                });

                // 5. Physics collider — small sphere for broadphase candidate selection.
                World.AddComponent(bullet, new PhysicsCollider
                {
                    Radius         = CombatConstants.BulletColliderRadius,
                    CollisionLayer = CombatConstants.BulletCollisionLayer,
                });

                // 6. Notify egress translator (muzzle flash for IG).
                World.Bus.Publish(new WeaponFireNotification
                {
                    ShooterEntityId = evt.ShooterEntityId,
                    TargetEntityId  = evt.TargetEntityId,
                    WeaponIndex     = evt.WeaponIndex,
                });
            }
        }
    }
}


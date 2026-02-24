using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Physics.Components;

namespace FDP.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="FireRequestEvent"/>s published by
    /// <see cref="Executors.AimAndFireExecutor"/> and spawns a bullet entity for each shot.
    /// <para>
    /// <b>Execution phase:</b> <see cref="InputSystemGroup"/> — runs after the weapon dispatcher
    /// group so that fire-request events are available in the current frame's consume window.
    /// </para>
    /// <para>
    /// <b>Per event:</b>
    /// <list type="number">
    ///   <item>Creates a new entity.</item>
    ///   <item>Adds <see cref="SimTransform"/> — position at the shot origin.</item>
    ///   <item>Adds <see cref="SimVelocity"/> — linear velocity = <c>direction × MuzzleVelocity</c>.</item>
    ///   <item>Adds <see cref="BallisticProjectile"/> — records shooter, damage, spawn tick.</item>
    ///   <item>Adds <see cref="PhysicsCollider"/> — small bounding circle for raycast broadphase.</item>
    /// </list>
    /// If the shooter entity is no longer alive the event is skipped.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(InputSystemGroup))]
    public class FireProcessingSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            var events = World.Bus.Consume<FireRequestEvent>();
            if (events.Length == 0) return;

            uint currentTick = World.HasSingleton<GlobalTime>()
                ? (uint)World.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                // Skip if the shooter is no longer alive (edge case: destroyed before this system runs).
                if (!World.IsAlive(evt.Shooter)) continue;

                // Read muzzle velocity from the shooter's WeaponState.
                var weapon    = World.GetComponent<WeaponState>(evt.Shooter);
                var direction = evt.Direction;                 // already normalised by AimAndFireExecutor
                var velocity  = direction * weapon.MuzzleVelocity;

                // 1. Spawn the bullet entity.
                var bullet = World.CreateEntity();

                // 2. Spatial transform — position at the shot origin; bullet always faces east (Identity).
                World.AddComponent(bullet, new SimTransform
                {
                    Position = evt.Origin,
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
                    Shooter          = evt.Shooter,
                    PreviousPosition = evt.Origin,   // initialised to origin; updated by BallisticsSystem
                    Damage           = CombatConstants.DefaultBulletDamage,
                    SpawnTick        = currentTick,
                });

                // 5. Physics collider — small sphere for broadphase candidate selection.
                World.AddComponent(bullet, new PhysicsCollider
                {
                    Radius         = CombatConstants.BulletColliderRadius,
                    CollisionLayer = CombatConstants.BulletCollisionLayer,
                });
            }
        }
    }
}

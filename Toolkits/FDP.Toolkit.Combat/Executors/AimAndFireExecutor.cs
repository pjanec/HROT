using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Kernel;
using Fbt;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Events;

namespace FDP.Toolkit.Combat.Executors
{
    /// <summary>
    /// Executor for the AimAndFire weapon action.
    /// Registered with <see cref="FDP.Toolkit.Behavior.Systems.WeaponDispatcherSystem"/> as the
    /// handler for the AimAndFire action ID.
    ///
    /// <b>Phase 0 Adaptation:</b> Uses <see cref="Fdp.Kernel.SimTransform"/> for position
    /// (not <c>VehicleState</c>), consistent with the Phase 0 refactor that replaced
    /// VehicleState-derived positions with SimTransform.
    /// </summary>
    public sealed class AimAndFireExecutor : IActionExecutor<WeaponChannel>
    {
        // ── OnEnter ──────────────────────────────────────────────────────────

        public unsafe void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // Read params from channel inline storage.
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            // Store target in channel State so Execute can read it each tick.
            fixed (byte* dst = channel.State)
                *(Entity*)dst = p.Target;

            channel.Status = NodeStatus.Running;
        }

        // ── Execute ───────────────────────────────────────────────────────────

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            // Read params.
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            // 1. Target-dead check — success: the objective is achieved.
            if (!world.IsAlive(p.Target))
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            // 2. Ammo check — failure: cannot fire without ammunition.
            ref var weapon = ref world.GetComponentRW<WeaponState>(entity);
            if (weapon.Ammo == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // 3. Cooldown check — wait until cooldown expires.
            if (weapon.CooldownTicksRemaining > 0)
            {
                weapon.CooldownTicksRemaining--;
                channel.Status = NodeStatus.Running;
                return;
            }

            // 4. Compute aim direction using SimTransform (Phase 0 Adaptation: NOT VehicleState).
            Vector3 origin    = world.GetComponent<SimTransform>(entity).Position;
            Vector3 targetPos = world.GetComponent<SimTransform>(p.Target).Position;
            Vector3 direction = Vector3.Normalize(targetPos - origin);

            // 5. Publish FireRequestEvent for FireProcessingSystem to consume.
            world.Bus.Publish(new FireRequestEvent
            {
                Shooter   = entity,
                Target    = p.Target,
                Origin    = origin,
                Direction = direction,
            });

            // 6. Consume one round and start cooldown.
            weapon.Ammo--;
            weapon.CooldownTicksRemaining = p.CooldownTicks;

            channel.Status = NodeStatus.Running;
        }

        // ── OnExit ───────────────────────────────────────────────────────────

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // No state to clean up.
        }
    }
}

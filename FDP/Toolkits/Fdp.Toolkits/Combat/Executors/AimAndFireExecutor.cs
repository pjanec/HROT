using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat.Executors
{
    /// <summary>
    /// Executor for the AimAndFire weapon action.
    /// Registered with <see cref="Fdp.Toolkit.Behavior.Systems.WeaponDispatcherSystem"/> as the
    /// handler for the AimAndFire action ID.
    ///
    /// PACK-P003: Publishes WeaponFireIntent with local ECS Entity handles (Shooter and Target)
    /// instead of long network IDs. NetworkEntityMap is no longer needed here; network-ID
    /// resolution is deferred to WeaponFireIntentEgressTranslator at the network boundary.
    ///
    /// Phase 0 Adaptation: Uses SimTransform for position, consistent with the Phase 0 refactor.
    /// </summary>
    public sealed class AimAndFireExecutor : IActionExecutor<WeaponChannel>
    {
        // OnEnter

        public unsafe void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            fixed (byte* dst = channel.State)
                *(Entity*)dst = p.Target;

            channel.Status = NodeStatus.Running;
        }

        // Execute

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            // ⭐ CE-466 rule (CombatLife): done when the target is gone OR knocked out (Health ≤ 0) — CE-267's revert keeps the
            //   body in the world, so existence alone never ends the action (CE-321: the squad emptied its rifles into a corpse).
            if (!CombatLife.IsAlive(world, p.Target))
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            ref var weapon = ref world.GetComponentRW<WeaponState>(entity);
            if (weapon.Ammo == 0)
            {
                channel.Status = NodeStatus.Failure;
                return;
            }

            // Drain cooldown continuously each frame; fire only after cooldown reaches zero.
            if (weapon.CooldownSecondsRemaining > 0f)
            {
                weapon.CooldownSecondsRemaining -= dt;
                channel.Status = NodeStatus.Running;
                return;
            }

            // ⭐ CE-321 — hold fire while a friendly is on the line (LineOfFire): the action keeps running and no round is spent,
            //   so a unit that moves clear (or a friendly that moves away) fires again.
            if (LineOfFire.BlockedByFriendly(world, entity, p.Target))
            {
                channel.Status = NodeStatus.Running;
                return;
            }

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = entity,
                Target      = p.Target,
                WeaponIndex = 0,
            });

            weapon.Ammo--;
            weapon.CooldownSecondsRemaining = p.CooldownSeconds;

            channel.Status = NodeStatus.Running;
        }

        // OnExit

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            // No state to clean up.
        }
    }
}

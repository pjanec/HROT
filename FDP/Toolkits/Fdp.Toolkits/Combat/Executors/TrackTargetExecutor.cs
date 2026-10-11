using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat.Executors
{
    /// <summary>⭐ WL-3 — the params of <see cref="TrackTargetExecutor"/> (<see cref="WeaponChannel.Params"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TrackTargetParams
    {
        /// <summary>The entity to keep the gun on.</summary>
        public Entity Target;
        /// <summary>The mount whose turret follows it (its index in the type's weapon suite); 0 = the primary.</summary>
        public byte Mount;
    }

    /// <summary>
    /// ⭐ WL-3 (docs/DESIGN_Weapon_Layers.md W-1, §5e) — <b>keep the gun on a target without firing</b>: the mount's turret gets a
    /// <see cref="WeaponTaskMode.Track"/> order and the weapon muscle follows the target. Running while it does; Success when the
    /// target is down; Failure when the mount has no turret or the target leaves the turret's arc. Releasing the channel sets the
    /// task to Idle — the turret holds where it points.
    /// </summary>
    public sealed class TrackTargetExecutor : IActionExecutor<WeaponChannel>
    {
        public void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world) => channel.Status = NodeStatus.Running;

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            TrackTargetParams p;
            fixed (byte* src = channel.Params) p = *(TrackTargetParams*)src;
            if (!CombatLife.IsAlive(world, p.Target))
            {
                AimAndFireExecutor.ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            var mount = CombatTkb.MountOf(world, entity, p.Mount);
            var part = mount != null && mount.TurretIndex >= 0 && world.IsComponentTypeRegistered<WeaponTask>()
                ? TurretParts.Find(world, entity, mount.TurretIndex) : Entity.Null;
            if (part.IsNull)
            {
                channel.Status = NodeStatus.Failure;
                Say(world, entity, ActionReason.Failed, p.Target);
                return;
            }

            var current = world.HasComponent<WeaponTask>(part) ? world.GetComponent<WeaponTask>(part) : default;
            bool same = current.Mode == WeaponTaskMode.Track && current.Target == p.Target && current.MountIndex == p.Mount;
            if (!same)
                world.SetComponent(part, new WeaponTask { Mode = WeaponTaskMode.Track, Target = p.Target, MountIndex = p.Mount, Epoch = current.Epoch + 1 });

            var status = world.IsComponentTypeRegistered<WeaponTaskStatus>() && world.HasComponent<WeaponTaskStatus>(part)
                ? world.GetComponent<WeaponTaskStatus>(part) : default;
            if (same && status.Epoch == current.Epoch && status.State == WeaponTaskState.Failed)
            {
                AimAndFireExecutor.ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Failure;
                Say(world, entity, status.Reason == WeaponTaskFailure.OutOfArc ? ActionReason.OutOfArc : ActionReason.Failed, p.Target);
                return;
            }
            channel.Status = NodeStatus.Running;
            Say(world, entity, same && status.Epoch == current.Epoch && status.State == WeaponTaskState.OnTarget
                ? ActionReason.Aiming : ActionReason.Slewing, p.Target);
        }

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world) => AimAndFireExecutor.ReleaseTurrets(world, entity);

        private static void Say(EntityRepository world, Entity unit, ActionReason reason, Entity target)
            => ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdTrackTarget, reason, target, 0f, 0f);
    }
}

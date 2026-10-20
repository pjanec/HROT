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
            if (!Injury.IsInTheFight(world, p.Target))
            {
                AimAndFireExecutor.ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            // ⭐⭐ Weapon systems WS-3 (W-D; DESIGN_Weapon_Systems §4a) — a weapon system this unit controls tracks on ITS OWN turret, as
            //   AimAndFire fires it: mount 0 names the primary (a bare host's station weapon — PrimaryMount), a controlled index the weapon
            //   itself (its mount 0). It slews only while it may fire for this unit (SystemHold — the gunner, the deploy state, the arc).
            Entity platform = entity;
            int platformMount = p.Mount;
            if (p.Mount == 0 && WeaponSystems.IsControlledIndex(WeaponSystems.PrimaryMount(world, entity, out var primary))) platform = primary;
            else if (WeaponSystems.IsControlledIndex(p.Mount)) platform = WeaponSystems.ControlledAt(world, entity, p.Mount);
            if (platform != entity) platformMount = 0;
            if (!platform.IsNull && platform != entity && AimAndFireExecutor.SystemHold(world, entity, platform, p.Target) is { } hold)
            {
                AimAndFireExecutor.ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Running;
                Say(world, entity, hold, p.Target);
                return;
            }

            var mount = platform.IsNull ? null : CombatTkb.MountOf(world, platform, platformMount);
            var part = mount != null && mount.TurretIndex >= 0 && world.IsComponentTypeRegistered<WeaponTask>()
                ? TurretParts.Find(world, platform, mount.TurretIndex) : Entity.Null;
            if (part.IsNull)
            {
                channel.Status = NodeStatus.Failure;
                Say(world, entity, ActionReason.Failed, p.Target);
                return;
            }

            var current = world.HasComponent<WeaponTask>(part) ? world.GetComponent<WeaponTask>(part) : default;
            bool same = current.Mode == WeaponTaskMode.Track && current.Target == p.Target && current.MountIndex == platformMount;
            if (!same)
                world.SetComponent(part, new WeaponTask { Mode = WeaponTaskMode.Track, Target = p.Target, MountIndex = (byte)platformMount, Epoch = current.Epoch + 1 });

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
        {
            ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdTrackTarget, reason, target, 0f, 0f);
            WeaponPostureOf.Report(world, unit, reason, target);   // ⭐ WL-6 (W-13b) — the weapon posture
        }
    }
}

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>WL-4</c> L-8 — the params of the shared <see cref="TrackTargetNodes.TrackTarget"/> node.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TrackTargetNodeParams
    {
        /// <summary>The entity to keep the gun on.</summary>
        public Entity Target;
        /// <summary>The mount whose turret follows it; 0 = the primary.</summary>
        public byte Mount;
        [System.Text.Json.Serialization.JsonIgnore] public uint Started;
    }

    /// <summary>
    /// ⭐⭐ <c>WL-4</c> L-8 (📄 docs/DESIGN_Weapon_Layers.md §5f; W-1's vocabulary) — <c>TrackTarget</c>, a SHARED action node: starts
    /// <see cref="Executors.TrackTargetExecutor"/> (weapon channel action 3) on the unit and reports its answer once — the shape of
    /// <see cref="FireAtPointNodes.FireAtPoint"/>. A new target restarts the action (the turret's task gets a new epoch).
    /// </summary>
    public static class TrackTargetNodes
    {
        [Fbt.Kernel.SharedAiAction]
        public static unsafe NodeStatus TrackTarget(ref TrackTargetNodeParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return NodeStatus.Failure;
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            if (p.Started != 0 && channel.ActionInstanceId == p.Started && channel.ActiveAction == CombatConstants.ActionIdTrackTarget)
            {
                var running = System.Runtime.CompilerServices.Unsafe.As<byte, Executors.TrackTargetParams>(ref channel.Params[0]);
                if (running.Target == p.Target && running.Mount == p.Mount)
                {
                    if (channel.Status == NodeStatus.Running) return NodeStatus.Running;
                    var done = channel.Status;   // the executor's answer — reported ONCE, then the node may run again
                    p.Started = 0;
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                    return done;
                }
            }
            if (p.Target.IsNull) return NodeStatus.Failure;
            if (world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(self))
                channel.BehaviorInstanceId = world.GetComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = CombatConstants.ActionIdTrackTarget;
            channel.Status = NodeStatus.Running;
            System.Runtime.CompilerServices.Unsafe.As<byte, Executors.TrackTargetParams>(ref channel.Params[0]) =
                new Executors.TrackTargetParams { Target = p.Target, Mount = p.Mount };
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2165</c> V4 — ends the tracking THIS node started (the channel still holds its instance), so a node that
        /// tracks while hidden can hand the gun to its fire step or leave. Anything else on the channel is left alone.</summary>
        public static void Stop(ref TrackTargetNodeParams p, Entity self, EntityRepository world)
        {
            if (p.Started != 0 && world.HasComponent<WeaponChannel>(self))
            {
                ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
                if (channel.ActiveAction == CombatConstants.ActionIdTrackTarget && channel.ActionInstanceId == p.Started)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                }
            }
            p.Started = 0;
        }
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat.Executors
{
    /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-8) — the params of <see cref="FireAtPointExecutor"/> (<see cref="WeaponChannel.Params"/>, 32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FireAtPointParams
    {
        /// <summary>The ground point to fire at (world metres).</summary>
        public Vector3 Point;
        /// <summary>Seconds between rounds.</summary>
        public float CooldownSeconds;
        /// <summary>Rounds to fire; 0 = until the ammunition runs out.</summary>
        public int Rounds;
        /// <summary>The mount that fires (its index in the type's weapon suite).</summary>
        public byte Mount;
    }

    /// <summary>
    /// ⭐⭐ Stage 6 (<c>CE-1032</c>, R-225 W-8) — fire at a ground POINT: a thrown grenade, a mortar. The sibling of
    /// <see cref="AimAndFireExecutor"/> on the same <see cref="WeaponChannel"/> (<see cref="CombatConstants.ActionIdFireAtPoint"/>),
    /// with the same per-shot rules — the mount's ammunition and cooldown, and the unit's ROE (<see cref="AimAndFireExecutor.RoePermitsFire"/>)
    /// — raising a <see cref="WeaponFireIntent"/> with <see cref="WeaponFireIntent.AtPoint"/>; <c>FireProcessingSystem</c> flies an
    /// indirect warhead on its arc. Success once <see cref="FireAtPointParams.Rounds"/> are fired; Failure when out of ammunition.
    /// <para>⚠ WHEN to throw or call fire is the behaviour's decision (the behaviors lane, §3k); this is only the mechanism.</para>
    /// </summary>
    public sealed class FireAtPointExecutor : IActionExecutor<WeaponChannel>
    {
        public unsafe void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            fixed (byte* dst = channel.State) *(int*)dst = 0;   // rounds fired
            channel.Status = NodeStatus.Running;
        }

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            FireAtPointParams p;
            fixed (byte* src = channel.Params) p = *(FireAtPointParams*)src;
            int fired;
            fixed (byte* st = channel.State) fired = *(int*)st;
            if (p.Rounds > 0 && fired >= p.Rounds) { channel.Status = NodeStatus.Success; Say(world, entity, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Done); return; }

            var mountEntity = MountEntity(world, entity, p.Mount);
            if (!world.HasComponent<WeaponState>(mountEntity)) { channel.Status = NodeStatus.Failure; return; }
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (weapon.Ammo == 0) { channel.Status = fired > 0 ? NodeStatus.Success : NodeStatus.Failure; Say(world, entity, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfAmmo); return; }
            if (weapon.CooldownSecondsRemaining > 0f)
            {
                weapon.CooldownSecondsRemaining -= dt;
                channel.Status = NodeStatus.Running;
                Say(world, entity, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Cooldown, weapon.CooldownSecondsRemaining);
                return;
            }
            if (!AimAndFireExecutor.RoePermitsFire(world, entity)) { channel.Status = NodeStatus.Running; Say(world, entity, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldRoe); return; }
            // ⭐ CE-3136 P-5 (D9) — an empty magazine reloads (held, Running) instead of ending the burst
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
                channel.Status = NodeStatus.Running;
                Say(world, entity, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Reloading, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
                return;
            }

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter = entity, Target = Entity.Null, WeaponIndex = p.Mount, AtPoint = true, TargetPoint = p.Point,
            });
            Magazine.Spend(ref weapon);   // ⭐ CE-3136 P-5
            weapon.CooldownSecondsRemaining = p.CooldownSeconds;
            fixed (byte* st = channel.State) *(int*)st = fired + 1;
            channel.Status = p.Rounds > 0 && fired + 1 >= p.Rounds ? NodeStatus.Success : NodeStatus.Running;
            Say(world, entity, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing, fired + 1, p.Rounds);   // ⭐ CE-3136 — blind fire: no aim, no sight gate
        }

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world) { }

        // ⭐ CE-3136 (T1) — the weapon row of the unit's ActionStatus (no target: a point).
        private static void Say(EntityRepository world, Entity unit, Fdp.Toolkit.Behavior.Diagnostics.ActionReason reason,
            float progress = 0f, float needed = 0f)
            => Fdp.Toolkit.Behavior.Diagnostics.ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdFireAtPoint, reason,
                   Entity.Null, progress, needed);

        /// <summary>The entity holding mount <paramref name="index"/>'s <see cref="WeaponState"/>: the owner for mount 0, the mount
        /// child otherwise (<see cref="WeaponMountQuery"/>, as <see cref="AimAndFireExecutor"/> finds it).</summary>
        private static Entity MountEntity(EntityRepository world, Entity owner, int index)
        {
            if (index == 0 || !world.IsComponentTypeRegistered<WeaponMountInfo>()) return owner;
            System.Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, owner, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == index)
                    return mounts[i];
            return owner;
        }
    }
}

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-8) — the params of the shared <see cref="FireAtPointNodes.FireAtPoint"/> node.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FireAtPointNodeParams
    {
        public Vector3 Point;
        public float CooldownSeconds;
        /// <summary>Rounds to fire; 0 = until the ammunition runs out.</summary>
        public int Rounds;
        public byte Mount;
        [System.Text.Json.Serialization.JsonIgnore] public uint Started;
    }

    /// <summary>
    /// ⭐⭐ Stage 6 (<c>CE-1032</c>, R-225 W-8) — <c>FireAtPoint</c>, a SHARED action node (<see cref="Fbt.Kernel.SharedAiActionAttribute"/>:
    /// BTree, HSM and Blueprint all reach it): starts <see cref="Executors.FireAtPointExecutor"/> on the unit's
    /// <see cref="WeaponChannel"/> and reports its answer once — the shape of the door nodes (<c>DoorNodes.OperateDoor</c>).
    /// </summary>
    public static class FireAtPointNodes
    {
        [Fbt.Kernel.SharedAiAction]
        public static unsafe NodeStatus FireAtPoint(ref FireAtPointNodeParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return NodeStatus.Failure;
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            if (p.Started != 0 && channel.ActionInstanceId == p.Started && channel.ActiveAction == CombatConstants.ActionIdFireAtPoint)
            {
                if (channel.Status == NodeStatus.Running) return NodeStatus.Running;
                var done = channel.Status;   // the executor's answer — reported ONCE, then the node may run again
                p.Started = 0;
                channel.ActiveAction = 0;
                unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                return done;
            }
            if (world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(self))
                channel.BehaviorInstanceId = world.GetComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = CombatConstants.ActionIdFireAtPoint;
            channel.Status = NodeStatus.Running;
            System.Runtime.CompilerServices.Unsafe.As<byte, Executors.FireAtPointParams>(ref channel.Params[0]) = new Executors.FireAtPointParams
            {
                Point = p.Point, CooldownSeconds = p.CooldownSeconds, Rounds = p.Rounds, Mount = p.Mount,
            };
            p.Started = channel.ActionInstanceId;
            return NodeStatus.Running;
        }
    }
}

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
            if (p.Rounds > 0 && fired >= p.Rounds) { channel.Status = NodeStatus.Success; Say(world, entity, p.Point, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Done); return; }

            var mountEntity = MountEntity(world, entity, p.Mount);
            if (!world.HasComponent<WeaponState>(mountEntity)) { channel.Status = NodeStatus.Failure; return; }
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (weapon.Ammo == 0) { channel.Status = fired > 0 ? NodeStatus.Success : NodeStatus.Failure; Say(world, entity, p.Point, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfAmmo); return; }
            // ⭐ CE-2173 slice 5 (Air Ops §8 A6, the backend's E4 report) — a BOMB on a point waits for the release cue, as the aimed path
            //   does (AimAndFireExecutor.HoldOf): where a bomb dropped now lands must have reached the point. The aircraft flies; the
            //   weapon releases. No body turn for a bomb (a person's arc rule).
            bool bomb = CombatTkb.MountOf(world, entity, p.Mount)?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb;
            if (bomb && !AimAndFireExecutor.ReleaseCue(world, entity, p.Point))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, p.Point, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.AwaitingRelease);
                return;
            }
            var reason = ShootAtPoint(world, entity, p.Mount, ref weapon, p.Point, p.CooldownSeconds, dt, obeyRoe: true, obeyArc: !bomb);
            if (reason != Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing)
            {
                channel.Status = NodeStatus.Running;
                SayHeld(world, entity, p.Point, reason, in weapon);
                return;
            }
            fixed (byte* st = channel.State) *(int*)st = fired + 1;
            channel.Status = p.Rounds > 0 && fired + 1 >= p.Rounds ? NodeStatus.Success : NodeStatus.Running;
            Say(world, entity, p.Point, Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing, fired + 1, p.Rounds);   // ⭐ CE-3136 — blind fire: no aim, no sight gate
        }

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world) { }

        /// <summary>
        /// ⭐ The ONE per-shot round at a point (shared with <see cref="AimAndFireExecutor"/>'s operator fire, <c>CE-3172</c> S4d):
        /// the mount's cooldown, the person's fire arc (when <paramref name="obeyArc"/> — the AI's, <c>CE-3195</c>), the unit's ROE (unless <paramref name="obeyRoe"/> is false — an operator overrides it, C-2), a
        /// DIRECT burst never through a friend (an indirect warhead arcs over — exempt), the magazine; then a
        /// <see cref="WeaponFireIntent"/> at the point and the round spent. Returns <c>Firing</c> when a round left, otherwise why
        /// not (the round is held, nothing spent). The caller has checked the ammunition.
        /// </summary>
        public static Fdp.Toolkit.Behavior.Diagnostics.ActionReason ShootAtPoint(EntityRepository world, Entity unit, int mountIndex,
            ref WeaponState weapon, Vector3 point, float cooldownSeconds, float dt, bool obeyRoe, bool obeyArc, long ammo = 0L)
        {
            // ⭐ CE-2166 — read, never drained here: WeaponDispatcherSystem drains every weapon and mount once a frame
            if (weapon.CooldownSecondsRemaining > 0f) return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Cooldown;
            // ⭐ CE-3195 (W-14a) — a person's round at a point waits for his body to face it (the AI's; an operator turns himself)
            if (obeyArc && BodyAim.HoldsFire(world, unit, point)) return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Turning;
            // ⭐ Air Ops slice 6a (AW-1, §7a) — the AI's fixed gun / rocket pod: only inside its nose cone (the operator points it himself)
            if (obeyArc && BodyAim.OutsideFixedCone(world, unit, CombatTkb.MountOf(world, unit, mountIndex), point))
                return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.OutOfArc;
            if (obeyRoe && !AimAndFireExecutor.RoePermitsFire(world, unit)) return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldRoe;
            // ⭐ CE-3158 G3 — a DIRECT burst never flies through a friend (CE-321's rule, to a point); held like the aimed guard
            //   (Running, no round spent). An INDIRECT warhead (a mortar, a thrown grenade) arcs over them — exempt.
            if (CombatTkb.WarheadOf(world, CombatTkb.MountOf(world, unit, mountIndex)) is not { Indirect: true }
                && LineOfFire.BlockedByFriendly(world, unit, point))
                return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.HoldFriendlyOnLine;
            // ⭐ CE-3136 P-5 (D9) — an empty magazine reloads (held, Running) instead of ending the burst
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
                return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Reloading;
            }
            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter = unit, Target = Entity.Null, WeaponIndex = mountIndex, AtPoint = true, TargetPoint = point,
                AmmoGuid = ammo,   // ⭐ CE-1079 — the loaded round (0 = the mount's own)
            });
            Magazine.Spend(ref weapon);   // ⭐ CE-3136 P-5
            weapon.CooldownSecondsRemaining = cooldownSeconds;
            return Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing;
        }

        private static void SayHeld(EntityRepository world, Entity unit, Vector3 point, Fdp.Toolkit.Behavior.Diagnostics.ActionReason reason,
            in WeaponState weapon)
        {
            switch (reason)
            {
                case Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Cooldown:
                    Say(world, unit, point, reason, weapon.CooldownSecondsRemaining); break;
                case Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Reloading:
                    Say(world, unit, point, reason, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds); break;
                default:
                    Say(world, unit, point, reason); break;
            }
        }

        // ⭐ CE-3136 (T1) — the weapon row of the unit's ActionStatus (no target: a point).
        //   ⭐ WL-6 (DESIGN_Weapon_Layers W-13b) — and the weapon posture, aimed at the point.
        private static void Say(EntityRepository world, Entity unit, Vector3 point, Fdp.Toolkit.Behavior.Diagnostics.ActionReason reason,
            float progress = 0f, float needed = 0f)
        {
            Fdp.Toolkit.Behavior.Diagnostics.ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdFireAtPoint, reason,
                Entity.Null, progress, needed);
            WeaponPostureOf.Report(world, unit, reason, Entity.Null, point);
        }

        /// <summary>The entity holding mount <paramref name="index"/>'s <see cref="WeaponState"/>: the owner for mount 0, the mount
        /// child otherwise (<see cref="WeaponMountQuery"/>, as <see cref="AimAndFireExecutor"/> finds it).</summary>
        private static Entity MountEntity(EntityRepository world, Entity owner, int index) => WeaponMountQuery.MountEntity(world, owner, index);
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

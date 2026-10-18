using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The params of <see cref="SuppressNodes.SuppressContact"/>: whom to suppress and where the squad last placed him.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SuppressContactParams
    {
        /// <summary>The contact (Null = only a point is known).</summary>
        public Entity Target;
        /// <summary>Where the squad last placed him (world m); used when this soldier has no memory of him.</summary>
        public Vector3 Point;
        /// <summary>1 when <see cref="Point"/> is set.</summary>
        public byte HasPoint;
        /// <summary>Seconds between aimed rounds (blind rounds go slower — the executor's cadence).</summary>
        public float CooldownSeconds;
    }

    /// <summary>What <see cref="SuppressNodes.SuppressContact"/> is doing.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SuppressContactState
    {
        /// <summary>The aimed / blind fire at the named contact (<c>PostureNodes.Fire</c>'s state).</summary>
        public EngageState Fire;
        /// <summary>The fire-at-point action this node started (0 = none).</summary>
        [JsonIgnore] public uint PointStarted;
        /// <summary>The point that action fires at.</summary>
        public Vector3 PointFiring;
        /// <summary>1 once the node stopped this soldier's move.</summary>
        public byte Held;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2152</c> C-c (R-2012 K5′, R-b; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.5) — <b>suppress a named contact
    /// whether or not this soldier can see him</b>. The order a squad commander gives its covering members: hold where you stand and put
    /// rounds on HIM — aimed when seen, blind at where your own memory places him when hidden (<c>PostureNodes.Fire</c> with
    /// blind fire, AQ89 A), and at the squad's reported point (<see cref="Fdp.Toolkit.Combat.FireAtPointNodes"/>'s action) when this
    /// soldier never saw him at all. ⛔ Not a posture decision: a plain <c>Hold</c> fires only when the soldier's own utility picks
    /// Suppress (§7.5.1). Running until the order is taken away; Failure without a weapon channel.
    /// </summary>
    public static class SuppressNodes
    {
        /// <summary>Blind rounds at a reported point go this many times slower than aimed ones (the executor's blind cadence).</summary>
        public const float PointCooldownFactor = 2.5f;

        [SharedAiAction]
        public static unsafe NodeStatus SuppressContact(ref SuppressContactParams p, ref SuppressContactState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return NodeStatus.Failure;
            if (ws.Held == 0) { LocomotionMoveTo.Stop(world, self); ws.Held = 1; }
            float cooldown = p.CooldownSeconds > 0f ? p.CooldownSeconds : 1f;

            var target = p.Target;
            // ⭐ CE-2282 DA-27 — his own sector: a target (or point) outside it is not fired at, he holds his position
            var aimAt = !target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target)
                ? world.GetComponentRO<SimTransform>(target).Position : p.Point;
            if ((p.HasPoint != 0 || !target.IsNull) && !Fdp.Toolkit.Behavior.Components.EngagementAreas.Allows(world, self, aimAt))
            {
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                StopPoint(world, self, ref ws);
                return NodeStatus.Running;
            }
            bool known = !target.IsNull && world.IsAlive(target) && Injury.IsInTheFight(world, target)
                      && (Fdp.Toolkit.Perception.SightNow.Sees(world, self, target) || HiddenTargetThrow.LastKnown(world, self, target, out _));
            if (known)
            {
                StopPoint(world, self, ref ws);
                PostureNodes.Fire(world, self, ref ws.Fire, cooldown, 0, target, blind: true);
                return NodeStatus.Running;
            }

            PostureNodes.StopFiring(world, self, ref ws.Fire);
            if (p.HasPoint == 0 || (!target.IsNull && (!world.IsAlive(target) || !Injury.IsInTheFight(world, target))))
            {
                StopPoint(world, self, ref ws);   // nothing to put rounds on (or he is down)
                return NodeStatus.Running;
            }
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            bool running = ws.PointStarted != 0 && channel.ActionInstanceId == ws.PointStarted
                        && channel.ActiveAction == CombatConstants.ActionIdFireAtPoint && channel.Status == NodeStatus.Running;
            if (running && Vector3.DistanceSquared(ws.PointFiring, p.Point) < 1f) return NodeStatus.Running;
            if (world.HasComponent<BehaviorState>(self)) channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = CombatConstants.ActionIdFireAtPoint;
            channel.Status = NodeStatus.Running;
            System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.FireAtPointParams>(ref channel.Params[0]) =
                new Fdp.Toolkit.Combat.Executors.FireAtPointParams { Point = p.Point, CooldownSeconds = cooldown * PointCooldownFactor, Rounds = 0, Mount = 0 };
            ws.PointStarted = channel.ActionInstanceId;
            ws.PointFiring = p.Point;
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.SuppressNodes.SuppressContact")]
        public static void Deactivate_SuppressContact(ref SuppressContactParams p, ref SuppressContactState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopPoint(world, self, ref ws);
            ws.Held = 0;
        }

        private static void StopPoint(EntityRepository world, Entity self, ref SuppressContactState ws)
        {
            if (ws.PointStarted == 0 || !world.HasComponent<WeaponChannel>(self)) { ws.PointStarted = 0; return; }
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            if (channel.ActionInstanceId == ws.PointStarted && channel.ActiveAction == CombatConstants.ActionIdFireAtPoint)
            {
                channel.ActiveAction = 0;
                unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            }
            ws.PointStarted = 0;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-b) — the library <c>Suppress</c>: ONE node, <see cref="SuppressNodes.SuppressContact"/>. What a squad
    /// commander orders its covering members (<c>MemberOrderKind.Suppress</c>); usable on its own as an order to any soldier.
    /// </summary>
    public static class SuppressBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SuppressBlackboard
        {
            public SuppressContactParams Order;
            public SuppressContactState State;
        }

        [BehaviorResolver(BehaviorNames.Suppress)]
        public static void Resolve(in SuppressParamsJsonDto authored, ref SuppressBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SuppressContactParams
            {
                Target          = authored.Target.Resolve(world),
                Point           = new Vector3(authored.X, authored.Y, authored.Z),
                HasPoint        = authored.X != 0f || authored.Y != 0f ? (byte)1 : (byte)0,
                CooldownSeconds = authored.CooldownSeconds > 0f ? authored.CooldownSeconds : 1f,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.Suppress, Params = typeof(SuppressParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.Suppress, Curated = true, Group = "Library/Infantry", ParamsType = typeof(SuppressBlackboard))]
        public static BTreeBuilder<SuppressBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SuppressBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, SuppressNodes.SuppressContact);
    }
}

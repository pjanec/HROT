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
        public SimDuration CooldownSeconds;
        /// <summary>⭐ <c>CE-8314</c> — the firing-position search round where he was ordered to hold (<c>PostureNodes.PlaceSuppressor</c>); a
        /// SearchRadius of 0 = he holds exactly where he stands (the stance rule still applies).</summary>
        public EqsTacticsParams From;
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
        /// <summary>⭐ <c>CE-8314</c> — where and how he suppresses from (the ONE placement rule, D-15).</summary>
        public SuppressPlacement Place;
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
                ? (Vector3)world.GetComponentRO<SimTransform>(target).Position : p.Point;
            if ((p.HasPoint != 0 || !target.IsNull) && !Fdp.Toolkit.Behavior.Components.EngagementAreas.Allows(world, self, aimAt))
            {
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                StopPoint(world, self, ref ws);
                return NodeStatus.Running;
            }
            bool known = !target.IsNull && world.IsAlive(target) && Injury.IsInTheFight(world, target)
                      && (Fdp.Toolkit.Perception.SightNow.Sees(world, self, target) || HiddenTargetThrow.LastKnown(world, self, target, out _));
            // ⭐⭐ CE-8317 — a named target his weapons cannot hurt (a tank, for a rifleman) is not suppressed: he holds (still placed, D-15)
            if (known && !Fdp.Toolkit.Perception.ThreatDanger.ICanHurt(world, PostureNodes.Tkb(world), self, target))
            {
                PostureNodes.StopFiring(world, self, ref ws.Fire);
                StopPoint(world, self, ref ws);
                PostureNodes.PlaceSuppressor(ref p.From, ref ws.Place, self, world, target, default);
                return NodeStatus.Running;
            }
            if (known)
            {
                StopPoint(world, self, ref ws);
                PostureNodes.Fire(world, self, ref ws.Fire, cooldown, 0, target, blind: true);
                PostureNodes.PlaceSuppressor(ref p.From, ref ws.Place, self, world, target, default);   // ⭐⭐ CE-8314 — D-15's placement
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
            if (running && Vector3.DistanceSquared(ws.PointFiring, p.Point) < 1f)
            {
                PostureNodes.PlaceSuppressor(ref p.From, ref ws.Place, self, world, Entity.Null, p.Point);   // ⭐ CE-8314
                return NodeStatus.Running;
            }
            if (world.HasComponent<BehaviorState>(self)) channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
            unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
            channel.ActiveAction = CombatConstants.ActionIdFireAtPoint;
            channel.Status = NodeStatus.Running;
            System.Runtime.CompilerServices.Unsafe.As<byte, Fdp.Toolkit.Combat.Executors.FireAtPointParams>(ref channel.Params[0]) =
                new Fdp.Toolkit.Combat.Executors.FireAtPointParams { Point = AimAtReported(world, self, p.Point), CooldownSeconds = cooldown * PointCooldownFactor, Rounds = 0, Mount = 0 };
            ws.PointStarted = channel.ActionInstanceId;
            ws.PointFiring = p.Point;
            PostureNodes.PlaceSuppressor(ref p.From, ref ws.Place, self, world, Entity.Null, p.Point);   // ⭐ CE-8314 — never saw him: the stance for the point
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-8319</c> — where a round at the squad's REPORTED point goes: the point ON THE GROUND (a reported post is 2-D — its Z is 0 — and
        /// 📐 live on village-street, ground ≈ 176 m, every round flew at a point 176 m under the gun and "expired": 220 of 262), then THE blind aim
        /// (<see cref="Fdp.Toolkit.Combat.AimPoint.BlindAt"/>, CE-8315 — the one rule: the point + the blind height, or through the nearest opening of
        /// the building it is in). No world ⇒ the point as given.
        /// </summary>
        internal static Vector3 AimAtReported(EntityRepository world, Entity self, Vector3 point)
        {
            var q = Fdp.Toolkit.World.WorldQuery.Of(world);
            if (q == null) return point;
            if (point.Z == 0f && q.TryStandAt(point.X, point.Y, world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position.Z : 0f, out float z))
                point.Z = z;
            var eye = ShotOrigin.Eye(world, self, out _);
            return Fdp.Toolkit.Combat.AimPoint.BlindAt(world, q, eye, point, Fdp.Toolkit.Combat.Executors.AimAndFireExecutor.BlindAimHeightMetres);
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.SuppressNodes.SuppressContact")]
        public static void Deactivate_SuppressContact(ref SuppressContactParams p, ref SuppressContactState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopPoint(world, self, ref ws);
            PostureNodes.ReleaseSuppressor(ref p.From, ref ws.Place, self, world);   // ⭐ CE-8314
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

        /// <summary>⭐ <c>CE-8314</c> — how far from where his commander put him an ordered suppressor looks for a firing position (m): a man's own
        /// adjustment into a fold or behind a wall, never a move off the base of fire.</summary>
        public const float OrderedFiringPositionMetres = 8f;

        [BehaviorResolver(BehaviorNames.Suppress)]
        public static void Resolve(in SuppressParamsJsonDto authored, ref SuppressBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SuppressContactParams
            {
                Target          = authored.Target.Ref.Resolve(world),
                Point           = new Vector3(authored.X, authored.Y, authored.Z),
                HasPoint        = authored.X != 0f || authored.Y != 0f ? (byte)1 : (byte)0,
                CooldownSeconds = authored.CooldownSeconds > 0f ? authored.CooldownSeconds : 1f,
                // ⭐⭐ CE-8314 — the D-15 placement round where his commander put him: a firing position within a few metres, crouched, its own fire off
                From            = new EqsTacticsParams { SearchRadius = OrderedFiringPositionMetres, MinRepositionMetres = 2f, Speed = 3f, ArrivalRadius = 1.5f, ScoreDeltaThreshold = 0.05f },
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.Suppress, Params = typeof(SuppressParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent, Description = "Keeps a target under fire to pin it down.")]
        [BTreeDefinition(BehaviorNames.Suppress, Curated = true, Group = "Library/Combat", ParamsType = typeof(SuppressBlackboard))]
        public static BTreeBuilder<SuppressBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SuppressBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, SuppressNodes.SuppressContact);
    }
}

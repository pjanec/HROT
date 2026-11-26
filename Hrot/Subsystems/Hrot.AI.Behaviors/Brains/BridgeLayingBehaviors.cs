using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Terrain;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4693</c> (bridge laying BL-S3 / BL-S4, ④ ⑤; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12) — the BRIDGE ENGINEER's plays: lay its span across
    /// the water between two banks, or take a laid one back. ⭐ One implementation (<see cref="Lay"/>, <see cref="Recover"/>) registered as the <c>&lt;Class&gt;&lt;Order&gt;</c>
    /// plays of each engineer class (the routing rule, Orders §1 O1–O2), the <see cref="UnitRelayBehaviors"/> shape.
    /// <para>The brain plans (<see cref="BridgeLaying.Plan"/> — the one place the deck pose and the refusal are computed, so the mover cannot disagree), drives its hull
    /// to the stand on the line (through an approach point behind it, so the hull ends aligned with the deck), then sets its OWN <see cref="BridgeCarried"/> — the order
    /// and the banks (R-4212: it writes nothing else). The work is the mover's (<c>BridgeWorkSystem</c>): it holds the seconds and creates / destroys the bridge
    /// obstacle. The brain SEES the result — the deck Open (Active, baked) at the plan's centre, or the bridge gone — and updates its count (BL-17).</para>
    /// <para>⛔ Refused (Failure at the first tick, the reason on <see cref="BridgeCarried.Refusal"/> and in the log): a gap wider than the span, a bank or a deck end in
    /// the water, no water between the banks, no span on board, a recover target that is not a laid bridge of the type it carries.</para>
    /// <para>⚠ An interrupted play leaves <see cref="BridgeCarried.Order"/> set (a BTree action has no abort hook); the mover works only while the hull is at its stand,
    /// and the next bridge order overwrites it.</para>
    /// </summary>
    public static class BridgeLayingBehaviors
    {
        public const string AssaultBridgeLayerLayBridge = "AssaultBridgeLayerLayBridge";
        public const string AssaultBridgeLayerRecoverBridge = "AssaultBridgeLayerRecoverBridge";
        public const string FloatingBridgeEngineerLayBridge = "FloatingBridgeEngineerLayBridge";
        public const string FloatingBridgeEngineerRecoverBridge = "FloatingBridgeEngineerRecoverBridge";

        public const float DefaultSpeed = 4f;
        /// <summary>A RecoverBridge order with no bridge named takes the nearest laid one of its type within this (m).</summary>
        public const float NearestBridgeMetres = 300f;
        public const float ArrivalRadius = 2f;

        public enum Step : byte { Plan = 0, Approach = 1, Stand = 2, Work = 3, Clear = 4 }

        [StructLayout(LayoutKind.Sequential)]
        public struct BridgeArgs
        {
            /// <summary>Lay: the two banks (local metres).</summary>
            public Vector3 Near;
            public Vector3 Far;
            /// <summary>Recover: the bridge.</summary>
            public Entity Bridge;
            public float Speed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BridgeWork
        {
            public Step Step;
            public byte Moving;
            /// <summary>The deck's centre (lay: planned; recover: the bridge's).</summary>
            public Vector2 Centre;
            public Vector2 Stand;
            public Vector2 Approach;
            /// <summary>Recover: 1 once the bridge was seen DESTROYED (its Health at 0 — a demolition, never a recovery): its span never comes back.</summary>
            public byte Destroyed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BridgeBlackboard
        {
            public BridgeArgs Order;
            public BridgeWork State;
        }

        // ── the resolvers: the neutral order DTO → this play's args ──
        [BehaviorResolver(AssaultBridgeLayerLayBridge)]
        public static void ResolveAssaultLay(in LayBridgeOrder o, ref BridgeBlackboard b, EntityRepository world, Entity self) => ResolveLay(in o, ref b);
        [BehaviorResolver(FloatingBridgeEngineerLayBridge)]
        public static void ResolveFloatingLay(in LayBridgeOrder o, ref BridgeBlackboard b, EntityRepository world, Entity self) => ResolveLay(in o, ref b);
        [BehaviorResolver(AssaultBridgeLayerRecoverBridge)]
        public static void ResolveAssaultRecover(in RecoverBridgeOrder o, ref BridgeBlackboard b, EntityRepository world, Entity self) => ResolveRecover(in o, ref b, world, self);
        [BehaviorResolver(FloatingBridgeEngineerRecoverBridge)]
        public static void ResolveFloatingRecover(in RecoverBridgeOrder o, ref BridgeBlackboard b, EntityRepository world, Entity self) => ResolveRecover(in o, ref b, world, self);

        private static void ResolveLay(in LayBridgeOrder o, ref BridgeBlackboard b)
            => b = new BridgeBlackboard { Order = new BridgeArgs { Near = o.NearBank.Local, Far = o.FarBank.Local, Speed = o.Speed > 0f ? o.Speed : DefaultSpeed } };

        private static void ResolveRecover(in RecoverBridgeOrder o, ref BridgeBlackboard b, EntityRepository world, Entity self)
        {
            var bridge = o.Bridge.Ref.Resolve(world);
            if (bridge.IsNull) bridge = NearestBridge(world, self);   // none named: the nearest laid bridge of the type it carries
            b = new BridgeBlackboard { Order = new BridgeArgs { Bridge = bridge, Speed = o.Speed > 0f ? o.Speed : DefaultSpeed } };
        }

        /// <summary>The nearest OPEN walkable deck of the type <paramref name="self"/> carries, within <see cref="NearestBridgeMetres"/>; else null. (The resolver: serial, once per order.)</summary>
        internal static Entity NearestBridge(EntityRepository world, Entity self)
        {
            if (!world.IsComponentTypeRegistered<StaticObstacle>() || !world.IsComponentTypeRegistered<BridgeCarried>() || !world.HasComponent<BridgeCarried>(self)
                || !world.HasComponent<SimTransform>(self)) return Entity.Null;
            long type = world.GetComponentRO<BridgeCarried>(self).BridgeType;
            var here = world.GetComponentRO<SimTransform>(self).Position;
            Entity best = Entity.Null; float bestD = NearestBridgeMetres;
            foreach (var e in BridgeLaying.OpenObstacles(world))
            {
                if (!IsItsBridge(world, e, type)) continue;
                float d = Vector3.Distance(world.GetComponentRO<SimTransform>(e).Position, here);
                if (d <= bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ── the plays ──
        [Behavior(AssaultBridgeLayerLayBridge, Params = typeof(LayBridgeOrder), Fulfils = nameof(OrderKind.LayBridge), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Lays the assault bridge across the water between two banks (refused over a gap wider than its span).")]
        [BTreeDefinition(AssaultBridgeLayerLayBridge, Curated = true, Group = "Library/Engineer", ParamsType = typeof(BridgeBlackboard))]
        public static BTreeBuilder<BridgeBlackboard, BTreeContext> BuildAssaultLay() => LayTree();

        [Behavior(FloatingBridgeEngineerLayBridge, Params = typeof(LayBridgeOrder), Fulfils = nameof(OrderKind.LayBridge), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Lays the floating bridge across the water between two banks (refused over a gap wider than its span).")]
        [BTreeDefinition(FloatingBridgeEngineerLayBridge, Curated = true, Group = "Library/Engineer", ParamsType = typeof(BridgeBlackboard))]
        public static BTreeBuilder<BridgeBlackboard, BTreeContext> BuildFloatingLay() => LayTree();

        [Behavior(AssaultBridgeLayerRecoverBridge, Params = typeof(RecoverBridgeOrder), Fulfils = nameof(OrderKind.RecoverBridge), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Drives to the nearer end of a laid assault bridge and takes it back on board.")]
        [BTreeDefinition(AssaultBridgeLayerRecoverBridge, Curated = true, Group = "Library/Engineer", ParamsType = typeof(BridgeBlackboard))]
        public static BTreeBuilder<BridgeBlackboard, BTreeContext> BuildAssaultRecover() => RecoverTree();

        [Behavior(FloatingBridgeEngineerRecoverBridge, Params = typeof(RecoverBridgeOrder), Fulfils = nameof(OrderKind.RecoverBridge), Categories = BehaviorCategory.MilitaryApc,
                  Description = "Drives to the nearer end of a laid floating bridge and takes it back on board.")]
        [BTreeDefinition(FloatingBridgeEngineerRecoverBridge, Curated = true, Group = "Library/Engineer", ParamsType = typeof(BridgeBlackboard))]
        public static BTreeBuilder<BridgeBlackboard, BTreeContext> BuildFloatingRecover() => RecoverTree();

        private static BTreeBuilder<BridgeBlackboard, BTreeContext> LayTree()
            => new BTreeBuilder<BridgeBlackboard, BTreeContext>().StatefulAction(bb => bb.Order, bb => bb.State, Lay);

        private static BTreeBuilder<BridgeBlackboard, BTreeContext> RecoverTree()
            => new BTreeBuilder<BridgeBlackboard, BTreeContext>().StatefulAction(bb => bb.Order, bb => bb.State, Recover);

        // ⭐ Parallel (R-4212): each tick writes only the engineer's OWN BridgeCarried and locomotion channel; it reads the bridge obstacles (StaticObstacle, SimTransform,
        //   ObstacleShape, TkbIdentity — nobody writes them in the brain phase) and the terrain. The open-bridge query is per thread (built once per world).
        [ThreadStatic] private static EntityQuery? t_obstacles;
        [ThreadStatic] private static EntityRepository? t_obstaclesFor;

        /// <summary>One tick of LayBridge.</summary>
        [SharedAiAction]
        public static NodeStatus Lay(ref BridgeArgs p, ref BridgeWork ws, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<BridgeCarried>() || !world.HasComponent<BridgeCarried>(self) || !world.HasComponent<SimTransform>(self))
                return NodeStatus.Failure;
            var carried = world.GetComponentRO<BridgeCarried>(self);
            if (ws.Step == Step.Plan)
            {
                if (!BridgeLaying.Figures(world, self, out var layer, out var span)) return Refuse(world, self, BridgeRefusal.NoSpanCarried, 0f, 0f, "LayBridge");
                if (carried.Count == 0) return Refuse(world, self, BridgeRefusal.NoSpanCarried, 0f, layer.SpanMaxM, "LayBridge");
                var plan = BridgeLaying.Plan(Fdp.Toolkit.Spatial.Eqs.EqsTerrainSight.World(world), new Vector2(p.Near.X, p.Near.Y), new Vector2(p.Far.X, p.Far.Y),
                                             layer.SpanMaxM, span.Length, span.Height, HullHalf(world, self));
                if (!plan.Ok) return Refuse(world, self, plan.Refusal, plan.Gap, layer.SpanMaxM, "LayBridge");
                ws.Centre = plan.Centre; ws.Stand = plan.Stand; ws.Approach = plan.Approach;
                ws.Step = Step.Approach;
                Set(world, self, carried with { Order = BridgeOrder.None, Refusal = BridgeRefusal.NotRefused, GapM = plan.Gap });
                return NodeStatus.Running;
            }
            if (ws.Step == Step.Clear) return ClearApproach(ref p, ref ws, self, world);
            if (ws.Step == Step.Work)
            {
                if (!OpenBridgeAt(world, carried.BridgeType, ws.Centre).IsNull)
                {
                    Set(world, self, carried with { Count = (byte)Math.Max(0, carried.Count - 1), Order = BridgeOrder.None });
                    // ⭐ the bridge is open: the layer CLEARS THE APPROACH — it stands on the deck's line before the near ramp, where the crossing comes
                    StopMove(ref ws, self, world);
                    ws.Step = Step.Clear;
                    return ClearApproach(ref p, ref ws, self, world);
                }
            }
            if (!Drive(ref p, ref ws, self, world)) return NodeStatus.Running;
            // at the stand: the hull's order is Lay between the order's banks (the mover re-plans from the same inputs ⇒ the same deck)
            var want = carried with { Order = BridgeOrder.Lay, NearBank = p.Near, FarBank = p.Far, Target = default, Refusal = BridgeRefusal.NotRefused };
            Set(world, self, want);
            return NodeStatus.Running;
        }

        /// <summary>One tick of RecoverBridge.</summary>
        [SharedAiAction]
        public static NodeStatus Recover(ref BridgeArgs p, ref BridgeWork ws, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<BridgeCarried>() || !world.HasComponent<BridgeCarried>(self) || !world.HasComponent<SimTransform>(self))
                return NodeStatus.Failure;
            var carried = world.GetComponentRO<BridgeCarried>(self);
            var b = p.Bridge;
            bool gone = b.IsNull || !world.IsAlive(b) || world.GetLifecycleState(b) == EntityLifecycle.TearDown;
            // ⭐ a demolished bridge (Health 0 — the charge, WaterFallSystem) is gone too, but its span is LOST: seen once, remembered
            if (!b.IsNull && world.IsAlive(b) && world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()
                && world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(b) && world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(b).Current <= 0f) ws.Destroyed = 1;
            if (ws.Step == Step.Plan)
            {
                if (gone || !IsItsBridge(world, b, carried.BridgeType) || !BridgeLaying.Figures(world, self, out _, out var span))
                    return Refuse(world, self, BridgeRefusal.NotItsBridge, 0f, 0f, "RecoverBridge");
                var tf = world.GetComponentRO<SimTransform>(b);
                var shape = world.GetComponentRO<ObstacleShape>(b);
                var here = world.GetComponentRO<SimTransform>(self).Position;
                var c = new Vector2(tf.Position.X, tf.Position.Y);
                var (stand, approach) = BridgeLaying.RecoverStand(c, TerrainObstacles.Yaw(tf.Rotation), shape.Length, shape.Height > 0f ? shape.Height : span.Height,
                                                                  HullHalf(world, self), new Vector2(here.X, here.Y));
                ws.Centre = c; ws.Stand = stand; ws.Approach = approach;
                ws.Step = Step.Approach;
                Set(world, self, carried with { Order = BridgeOrder.None, Refusal = BridgeRefusal.NotRefused });
                return NodeStatus.Running;
            }
            if (gone)
            {
                StopMove(ref ws, self, world);
                // ⭐ the span is back only when the bridge went while THIS engineer was taking it back at its stand and it was not destroyed —
                //   📌 bl-river-crossing: a recovery ordered as the sapper's charge dropped the span counted it back on board
                bool recovered = ws.Step == Step.Work && ws.Destroyed == 0 && carried.Order == BridgeOrder.Recover;
                Set(world, self, carried with { Count = recovered ? (byte)Math.Min(255, carried.Count + 1) : carried.Count, Order = BridgeOrder.None, Target = default });
                if (!recovered) Hrot.AI.Behaviors.Logging.BehaviorLog.Warn(self, world, "RecoverBridge: the bridge is gone, not recovered (destroyed, or taken by another)");
                return recovered ? NodeStatus.Success : NodeStatus.Failure;
            }
            if (!Drive(ref p, ref ws, self, world)) return NodeStatus.Running;
            Set(world, self, carried with { Order = BridgeOrder.Recover, Target = Fdp.Toolkit.Replication.EntityRef.Of(world, b), Refusal = BridgeRefusal.NotRefused });
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ <c>CE-4693</c> — the laid bridge is open: the engineer BACKS OFF THE APPROACH, out of the deck's line (it stood before the near ramp, where the crossing comes):
        /// reversing <see cref="ClearBackMetres"/> back and <see cref="ClearSideMetres"/> to the side — an arc any hull here can reverse (📐 the floating transporter,
        /// 6.6 m wheelbase, turns on ~15.6 m: facing the river 13.5 m ahead it could not turn away forwards — the no-wading rule held it at the water's edge). Success when
        /// it has backed off (or its move ended where it could).
        /// </summary>
        private static NodeStatus ClearApproach(ref BridgeArgs p, ref BridgeWork ws, Entity self, EntityRepository world)
        {
            if (ws.Moving == 1)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Moving = 0;
                return NodeStatus.Success;
            }
            var along = ws.Stand - ws.Approach;
            along = along.LengthSquared() > 1e-6f ? Vector2.Normalize(along) : Vector2.UnitY;
            var side = new Vector2(-along.Y, along.X);
            var to = ws.Stand - along * ClearBackMetres + side * ClearSideMetres;
            float z = world.GetComponentRO<SimTransform>(self).Position.Z;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, new Vector3(to, z), MathF.Min(p.Speed, 3f), ArrivalRadius, MoveGait.Tactical, reverseAllowed: true,
                                                      onSurface: true, source: LocomotionMoveTo.OrderSource(world, self)) ? 1 : 0);
            return ws.Moving == 1 ? NodeStatus.Running : NodeStatus.Success;
        }

        /// <summary>How far back along the deck's line and to its side (m) the engineer backs off once its bridge is open.</summary>
        public const float ClearBackMetres = 20f, ClearSideMetres = 10f;

        /// <summary>Drives approach → stand; true while the hull is at its stand (then <see cref="Step.Work"/>). Re-drives if it is pushed off it.</summary>
        private static bool Drive(ref BridgeArgs p, ref BridgeWork ws, Entity self, EntityRepository world)
        {
            var at3 = world.GetComponentRO<SimTransform>(self).Position;
            var here = new Vector2(at3.X, at3.Y);
            if (Vector2.Distance(here, ws.Stand) <= ArrivalRadius + 0.5f) { StopMove(ref ws, self, world); ws.Step = Step.Work; return true; }
            if (ws.Step == Step.Work)
            {
                if (Vector2.Distance(here, ws.Stand) <= BridgeLaying.StandReachMetres) return true;   // close enough for the mover (it checks the same reach)
                ws.Step = Step.Stand;
            }
            // already nearer the stand than the approach (and in front of it): skip the approach
            if (ws.Step == Step.Approach && Vector2.Distance(here, ws.Stand) < Vector2.Distance(ws.Approach, ws.Stand)
                && Vector2.Dot(here - ws.Approach, ws.Stand - ws.Approach) > 0f) { StopMove(ref ws, self, world); ws.Step = Step.Stand; }
            if (ws.Moving == 1)
            {
                var s = LocomotionMoveTo.Status(world, self);
                if (s == NodeStatus.Running) return false;
                ws.Moving = 0;
                if (ws.Step == Step.Approach) { ws.Step = Step.Stand; }
                else if (Vector2.Distance(here, ws.Stand) <= BridgeLaying.StandReachMetres) { ws.Step = Step.Work; return true; }
            }
            var to = ws.Step == Step.Approach ? ws.Approach : ws.Stand;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, new Vector3(to, at3.Z), p.Speed, ArrivalRadius, MoveGait.Tactical, onSurface: true,
                                                      source: LocomotionMoveTo.OrderSource(world, self)) ? 1 : 0);
            return false;
        }

        private static NodeStatus Refuse(EntityRepository world, Entity self, BridgeRefusal why, float gap, float spanMax, string play)
        {
            var carried = world.GetComponentRO<BridgeCarried>(self);
            Set(world, self, carried with { Order = BridgeOrder.None, Refusal = why, GapM = gap });
            Hrot.AI.Behaviors.Logging.BehaviorLog.Warn(self, world, play + " refused: " + BridgeLaying.Say(why, gap, spanMax));
            return NodeStatus.Failure;
        }

        private static bool IsItsBridge(EntityRepository world, Entity b, long bridgeType)
        {
            if (!world.IsComponentTypeRegistered<StaticObstacle>() || !world.HasComponent<StaticObstacle>(b) || !world.HasComponent<ObstacleShape>(b) || !world.HasComponent<SimTransform>(b))
                return false;
            if (world.GetComponentRO<StaticObstacle>(b).Kind != ObstacleKind.WalkableDeck) return false;
            return !world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>() || !world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(b)
                   || (long)world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(b).TkbType == bridgeType;
        }

        private static Entity OpenBridgeAt(EntityRepository world, long bridgeType, Vector2 centre)
        {
            if (!world.IsComponentTypeRegistered<StaticObstacle>()) return Entity.Null;
            if (!ReferenceEquals(t_obstaclesFor, world) || t_obstacles == null) { t_obstacles = BridgeLaying.OpenObstacles(world); t_obstaclesFor = world; }
            return BridgeLaying.OpenBridgeAt(world, t_obstacles, bridgeType, centre);
        }

        /// <summary>Hull centre to its front (m): half its <c>VehicleParams</c> length, else 3.</summary>
        internal static float HullHalf(EntityRepository world, Entity self) => BridgeLaying.HullHalf(world, self);   // ⭐ CE-6500 — the ONE copy

        /// <summary>Sets the engineer's OWN component only when it changes (no needless chunk-version bump, no needless sample).</summary>
        private static void Set(EntityRepository world, Entity self, in BridgeCarried want)
        {
            var have = world.GetComponentRO<BridgeCarried>(self);
            if (have.Count != want.Count || have.Order != want.Order || have.BridgeType != want.BridgeType || !have.NearBank.Equals(want.NearBank)
                || !have.FarBank.Equals(want.FarBank) || have.Target.NetworkId != want.Target.NetworkId || have.Refusal != want.Refusal || have.GapM != want.GapM)
                world.SetComponent(self, want);
        }

        private static void StopMove(ref BridgeWork ws, Entity self, EntityRepository world)
        {
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            ws.Moving = 0;
        }
    }
}

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Squad.Command;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2276</c> E-1 (DA-51, DA-52) — the platoon's defence plan: the order (position, engagement area, authored squads) and, once
    /// <see cref="Made"/>, each roster slot's role, point and slice of the area. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8d.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PlatoonDefencePlan
    {
        public const int Capacity = CommandPicture.MaxSubordinates;
        public const byte NoRole = 0, Line = 1, Reserve = 2;

        public Vector3 Position;
        public float SearchRadius;
        public float Speed;
        /// <summary>The engagement area: the order's arc, else (at plan time) the platoon's own sector.</summary>
        public EngagementArea Area;
        /// <summary>1 = the order named the squads (DA-51): a slot it did not name takes no part.</summary>
        public byte Authored;
        /// <summary>1 = the plan is made.</summary>
        public byte Made;
        /// <summary>DA-54 — 1 = the line holds its fire until the trigger (the platoon is Regular or better).</summary>
        public byte HoldsFire;
        public fixed byte Role[Capacity];
        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        public fixed float Radius[Capacity];
        /// <summary>A line squad's slice of the area (compass °), when the area is an arc.</summary>
        public fixed float Left[Capacity];
        public fixed float Right[Capacity];

        public Vector3 Point(int slot) => new(X[slot], Y[slot], Z[slot]);

        public void Set(int slot, byte role, Vector3 point, float radius)
        {
            Role[slot] = role;
            X[slot] = point.X; Y[slot] = point.Y; Z[slot] = point.Z;
            Radius[slot] = radius;
        }
    }

    /// <summary>⭐ <c>CE-2276</c> — what <see cref="PlatoonDefendBehavior.KeepDefence"/> keeps: how many orders it gave.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlatoonDefendState
    {
        public int Orders;
    }

    /// <summary>⭐ <c>CE-2276</c> E-1 (DA-52, DA-24's "each 10 % wider than its share") — pure: an arc split into equal shares, left to right,
    /// each widened by <c>overlap</c> of a share (half on each side) so neighbours support each other.</summary>
    public static class SectorSplit
    {
        public const float Overlap = 0.1f;

        public static void Split(float left, float right, Span<float> lefts, Span<float> rights, float overlap = Overlap)
        {
            int n = Math.Min(lefts.Length, rights.Length);
            if (n == 0) return;
            float span = Norm(right - left);
            if (span <= 0f) span = 360f;
            float share = span / n, pad = share * overlap * 0.5f;
            for (int i = 0; i < n; i++)
            {
                lefts[i] = Norm(left + i * share - pad);
                rights[i] = Norm(left + (i + 1) * share + pad);
            }
        }

        public static float Norm(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2276</c> E-1 (DA-E; Platoon §5, Defend §8d) — <c>PlatoonDefend</c>, the platoon holds a position over its squads: DPlan
    /// makes the plan (roles, points, each line squad's slice of the engagement area); Occupy orders every squad to <c>SquadDefend</c> its
    /// point and waits for each line squad's PROGRESS Set; Ready holds the line's fire; an enemy reported inside the area opens it (Engage),
    /// and the area quiet for <see cref="QuietSeconds"/> closes it again. The orders are kept by ONE activity in all three states, so a
    /// squad whose order was replaced is ordered again wherever the plan stands. Nothing here reads another brain — only the picture.
    /// </summary>
    public static class PlatoonDefendBehavior
    {
        /// <summary>DA-53 — an element inside the area observed this recently opens fire (the SPOT refresh is 15 s).</summary>
        public const float TriggerFreshSeconds = 30f;
        /// <summary>DA-53 — none inside observed this recently ⇒ the line holds its fire again.</summary>
        public const float QuietSeconds = 120f;
        /// <summary>DA-53 — with no area at all, an element this near the position counts as inside.</summary>
        public const float NoAreaMetres = 300f;
        /// <summary>DA-51 — the default reserve stands this far behind the position (opposite the area's middle).</summary>
        public const float ReserveBehindMetres = 40f;

        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonDefendBlackboard
        {
            public PlatoonDefencePlan Plan;
            public PlatoonDefendState Keep;
        }

        [BehaviorResolver(BehaviorNames.PlatoonDefend)]
        public static unsafe void Resolve(in PlatoonDefendParamsJsonDto authored, ref PlatoonDefendBlackboard block, EntityRepository world, Entity self)
        {
            block.Plan = default;
            block.Keep = default;
            ref var plan = ref block.Plan;
            plan.Position = authored.Position;
            plan.SearchRadius = authored.SearchRadius;
            plan.Speed = authored.Speed;
            if (SectorSplit.Norm(authored.Right - authored.Left) > 0f)
                plan.Area = new EngagementArea
                {
                    ArcSet = 1, ArcOrigin = authored.Position, LeftBearing = SectorSplit.Norm(authored.Left), RightBearing = SectorSplit.Norm(authored.Right),
                    MinRange = Math.Max(0f, authored.MinRange), MaxRange = Math.Max(0f, authored.MaxRange),
                };
            if (authored.Squads == null) return;
            foreach (var s in authored.Squads)
            {
                int slot = s.Squad - 1;
                if (slot < 0 || slot >= PlatoonDefencePlan.Capacity) continue;
                plan.Authored = 1;
                plan.Set(slot, s.Reserve ? PlatoonDefencePlan.Reserve : PlatoonDefencePlan.Line,
                         s.Point == Vector3.Zero ? authored.Position : s.Point, s.SearchRadius > 0f ? s.SearchRadius : authored.SearchRadius);
            }
        }

        [Behavior(BehaviorNames.PlatoonDefend, Params = typeof(PlatoonDefendParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.PlatoonDefend, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonDefendBlackboard))]
        public static StateMachineGraph BuildPlatoonDefend()
        {
            var builder = new HsmBuilder("PlatoonDefend_HSM");
            var plan = builder.State("DPlan").Initial()
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, PlanDefence);
            var occupy = builder.State("Occupy")
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, HoldTheLine)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence);
            var ready = builder.State("Ready")
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, HoldTheLine)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence);
            var engage = builder.State("Engage")
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, OpenFire)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence);
            plan.On(0).GoTo("Occupy").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Planned).Polled();
            occupy.On(0).GoTo("Ready").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, LineSet).Polled();
            ready.On(0).GoTo("Engage").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, EnemyInArea).Polled();
            engage.On(0).GoTo("Ready").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, AreaQuiet).Polled();
            return builder.Build();
        }

        // ── the plan ─────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>DPlan's activity (DA-51, DA-52, DA-54): once the platoon has squads, give each a role and a point (authored, else the
        /// default), take the platoon's own sector when the order has none, and split the area among the line squads by their bearing from
        /// the position. Running until made (the state's guard moves on).</summary>
        [SharedAiAction]
        public static unsafe NodeStatus PlanDefence(ref PlatoonDefencePlan p, ref PlatoonDefendState ws, Entity self, EntityRepository world)
        {
            if (p.Made != 0) return NodeStatus.Running;
            int n = RosterCount(world, self);
            if (n == 0) return NodeStatus.Running;
            p.HoldsFire = ProficiencyOf.Tier(world, self) >= ProficiencyTier.Regular ? (byte)1 : (byte)0;
            if (p.Area.ArcSet == 0 && p.Area.AllowArea == 0 && world.IsComponentTypeRegistered<EngagementArea>() && world.HasComponent<EngagementArea>(self))
            {
                var own = world.GetComponentRO<EngagementArea>(self);
                if (own.ArcSet != 0 || own.AllowArea != 0)
                    p.Area = new EngagementArea
                    {
                        ArcSet = own.ArcSet, ArcOrigin = own.ArcOrigin, LeftBearing = own.LeftBearing, RightBearing = own.RightBearing,
                        MinRange = own.MinRange, MaxRange = own.MaxRange, AllowArea = own.AllowArea,
                    };
            }
            if (p.Position == Vector3.Zero && p.Area.ArcSet != 0) p.Position = p.Area.ArcOrigin;
            if (p.Authored == 0)
                for (int slot = 0; slot < n; slot++)
                    p.Set(slot, n >= 3 && slot == n - 1 ? PlatoonDefencePlan.Reserve : PlatoonDefencePlan.Line,
                          n >= 3 && slot == n - 1 ? Behind(world, in p) : p.Position, p.SearchRadius);
            if (p.Area.ArcSet != 0) Slice(ref p, n);
            p.Made = 1;
            return NodeStatus.Running;
        }

        /// <summary>The default reserve's point: <see cref="ReserveBehindMetres"/> behind the position, away from the area's middle.</summary>
        private static Vector3 Behind(EntityRepository world, in PlatoonDefencePlan p)
        {
            var area = p.Area;
            if (!EngagementAreasMiddle(world, in area, out var middle)) return p.Position;
            var away = new Vector2(p.Position.X - middle.X, p.Position.Y - middle.Y);
            if (away.LengthSquared() < 1e-4f) return p.Position;
            away = Vector2.Normalize(away) * ReserveBehindMetres;
            return new Vector3(p.Position.X + away.X, p.Position.Y + away.Y, p.Position.Z);
        }

        private static bool EngagementAreasMiddle(EntityRepository world, in EngagementArea a, out Vector3 middle)
        {
            middle = default;
            if (a.ArcSet == 0) return false;
            float bearing = (a.LeftBearing + SectorSplit.Norm(a.RightBearing - a.LeftBearing) * 0.5f) * (MathF.PI / 180f);
            float range = a.MaxRange > 0f ? (a.MinRange + a.MaxRange) * 0.5f : 100f;
            middle = a.ArcOrigin + new Vector3(MathF.Sin(bearing) * range, MathF.Cos(bearing) * range, 0f);
            return true;
        }

        /// <summary>DA-52 — the line squads, ordered left to right by their bearing from the position (roster order when they all stand on
        /// it), each get one share of the arc.</summary>
        private static unsafe void Slice(ref PlatoonDefencePlan p, int n)
        {
            Span<int> lines = stackalloc int[PlatoonDefencePlan.Capacity];
            Span<float> key = stackalloc float[PlatoonDefencePlan.Capacity];
            int k = 0;
            bool spread = false;
            // the signed angle from the arc's MIDDLE (−180…180): a squad standing outside the arc still sorts on its own side
            // (measured from the left edge, a squad west of a north arc wrapped round to ~320° and sorted right of the rest)
            float middle = p.Area.LeftBearing + SectorSplit.Norm(p.Area.RightBearing - p.Area.LeftBearing) * 0.5f;
            for (int slot = 0; slot < n && slot < PlatoonDefencePlan.Capacity; slot++)
            {
                if (p.Role[slot] != PlatoonDefencePlan.Line) continue;
                var d = new Vector2(p.X[slot] - p.Area.ArcOrigin.X, p.Y[slot] - p.Area.ArcOrigin.Y);
                if (d.Length() > 1f) spread = true;
                float off = SectorSplit.Norm(MathF.Atan2(d.X, d.Y) * (180f / MathF.PI) - middle);
                key[k] = off > 180f ? off - 360f : off;
                lines[k++] = slot;
            }
            if (k == 0) return;
            if (spread)
                for (int i = 1; i < k; i++)
                    for (int j = i; j > 0 && key[j] < key[j - 1]; j--)
                    {
                        (key[j], key[j - 1]) = (key[j - 1], key[j]);
                        (lines[j], lines[j - 1]) = (lines[j - 1], lines[j]);
                    }
            Span<float> lefts = stackalloc float[k];
            Span<float> rights = stackalloc float[k];
            SectorSplit.Split(p.Area.LeftBearing, p.Area.RightBearing, lefts, rights);
            for (int i = 0; i < k; i++) { p.Left[lines[i]] = lefts[i]; p.Right[lines[i]] = rights[i]; }
        }

        // ── the orders ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The activity of Occupy, Ready and Engage: a squad of the plan whose order from THIS run does not stand is ordered to
        /// <c>SquadDefend</c> its point, and given its sector (a line squad its slice, the reserve none). Running while the platoon lives.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus KeepDefence(ref PlatoonDefencePlan p, ref PlatoonDefendState ws, Entity self, EntityRepository world)
        {
            if (p.Made == 0) return NodeStatus.Running;
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity);
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] == PlatoonDefencePlan.NoRole) continue;
                var squad = CommandPictureOps.Subordinate(world, self, slot);
                if (squad.IsNull || !world.IsAlive(squad) || CommandOrders.Stands(world, self, squad)) continue;
                var order = new SquadDefendParamsJsonDto { Position = p.Point(slot), SearchRadius = p.Radius[slot], Speed = p.Speed };
                CommandOrders.Assign(world, self, squad, BehaviorNames.SquadDefend,
                                     JsonSerializer.Serialize(order, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), newOrder: true);
                Sector(ref p, slot, world, squad);
                ws.Orders++;
            }
            return NodeStatus.Running;
        }

        private static unsafe void Sector(ref PlatoonDefencePlan p, int slot, EntityRepository world, Entity squad)
        {
            if (!world.Bus.IsRegistered<SetEngagementAreaEvent>()) return;
            if (p.Role[slot] != PlatoonDefencePlan.Line || (p.Area.ArcSet == 0 && p.Area.AllowArea == 0))
            {
                EngagementAreas.WithdrawSuperior(world, squad);
                return;
            }
            world.Bus.Publish(new SetEngagementAreaEvent
            {
                Entity = squad, Origin = BehaviorOrigin.Superior,
                ArcSet = p.Area.ArcSet, ArcOrigin = p.Area.ArcOrigin, LeftBearing = p.Left[slot], RightBearing = p.Right[slot],
                MinRange = p.Area.MinRange, MaxRange = p.Area.MaxRange, AllowArea = p.Area.AllowArea,
            });
        }

        // ── fire control (DA-54) ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Occupy's and Ready's entry: the line holds its fire (Regular+; below, it fires at will), the reserve returns fire.</summary>
        [SharedAiAction]
        public static NodeStatus HoldTheLine(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            var line = p.HoldsFire != 0 ? RoeFire.HoldFire : RoeFire.FireAtWill;
            var reserve = p.HoldsFire != 0 ? RoeFire.ReturnFire : RoeFire.FireAtWill;
            SetFire(ref p, self, world, line, reserve);
            return NodeStatus.Success;
        }

        /// <summary>Engage's entry: the line fires at will (it reaches every man, PC-6); the reserve keeps returning fire only.</summary>
        [SharedAiAction]
        public static NodeStatus OpenFire(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            SetFire(ref p, self, world, RoeFire.FireAtWill, p.HoldsFire != 0 ? RoeFire.ReturnFire : RoeFire.FireAtWill);
            return NodeStatus.Success;
        }

        private static unsafe void SetFire(ref PlatoonDefencePlan p, Entity self, EntityRepository world, RoeFire line, RoeFire reserve)
        {
            if (!world.Bus.IsRegistered<SetRoeEvent>()) return;
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity);
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] == PlatoonDefencePlan.NoRole) continue;
                var squad = CommandPictureOps.Subordinate(world, self, slot);
                if (squad.IsNull || !world.IsAlive(squad)) continue;
                world.Bus.Publish(new SetRoeEvent
                {
                    Entity = squad, Origin = BehaviorOrigin.Superior, Fire = p.Role[slot] == PlatoonDefencePlan.Line ? line : reserve,
                });
            }
        }

        // ── the guards ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>DPlan → Occupy.</summary>
        [SharedAiCondition]
        public static bool Planned(ref PlatoonDefencePlan p, Entity self, EntityRepository world) => p.Made != 0;

        /// <summary>Occupy → Ready: every live line squad reported PROGRESS Set for its current order (and there is one).</summary>
        [SharedAiCondition]
        public static unsafe bool LineSet(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity), lines = 0;
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] != PlatoonDefencePlan.Line) continue;
                var squad = CommandPictureOps.Subordinate(world, self, slot);
                if (squad.IsNull || !world.IsAlive(squad)) continue;
                if (CommandPictureOps.ProgressOf(world, self, slot) != ProgressKind.Set) return false;
                lines++;
            }
            return lines > 0;
        }

        /// <summary>Ready → Engage (DA-53): the picture holds an element inside the area, observed within <see cref="TriggerFreshSeconds"/>.</summary>
        [SharedAiCondition]
        public static bool EnemyInArea(ref PlatoonDefencePlan p, Entity self, EntityRepository world) => ElementInside(in p, self, world, TriggerFreshSeconds);

        /// <summary>Engage → Ready (DA-53): no element inside the area observed within <see cref="QuietSeconds"/>.</summary>
        [SharedAiCondition]
        public static bool AreaQuiet(ref PlatoonDefencePlan p, Entity self, EntityRepository world) => !ElementInside(in p, self, world, QuietSeconds);

        private static bool ElementInside(in PlatoonDefencePlan p, Entity self, EntityRepository world, float freshSeconds)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float now = CommandPictureOps.Now(world);
            bool hasArea = p.Area.ArcSet != 0 || p.Area.AllowArea != 0;
            var area = p.Area;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || now - e.ObservedAt > freshSeconds) continue;
                if (hasArea ? EngagementAreas.Contains(world, in area, e.At)
                            : Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(p.Position.X, p.Position.Y)) <= NoAreaMetres)
                    return true;
            }
            return false;
        }

        private static int RosterCount(EntityRepository world, Entity self)
            => world.HasComponent<UnitRoster>(self) ? world.GetComponentRO<UnitRoster>(self).Count : 0;
    }
}

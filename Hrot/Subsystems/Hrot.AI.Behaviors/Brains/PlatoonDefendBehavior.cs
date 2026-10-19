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
using Fdp.Toolkit.Perception.Survey;
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
        public const byte NoRole = 0, Line = 1, Reserve = 2, Op = 3;

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
        /// <summary>⭐ E-2 (DA-65) — 1 = the control region acts (Regular or better): OpBack, Supplementary, Reinforce.</summary>
        public byte Controls;
        /// <summary>⭐ E-2 (DA-61) — 1 = a reserve has been committed (once).</summary>
        public byte Committed;
        /// <summary>⭐ E-3a (DA-69, DA-71) — 1 = the platoon is falling back: <c>KeepDefence</c> gives the fall-back orders, the control region rests.</summary>
        public byte Falling;
        /// <summary>⭐ E-3a (DA-66) — the fall-back point (made at plan time when the order has none).</summary>
        public Vector3 FallBackTo;
        /// <summary>⭐ E-4 (DA-85, DA-90) — 1 once the area's survey was asked (battle positions) · when (sim s).</summary>
        public byte Asked;
        public double AskedAt;
        public fixed byte Role[Capacity];
        public fixed float X[Capacity];
        public fixed float Y[Capacity];
        public fixed float Z[Capacity];
        public fixed float Radius[Capacity];
        /// <summary>A line squad's slice of the area (compass °), when the area is an arc.</summary>
        public fixed float Left[Capacity];
        public fixed float Right[Capacity];
        /// <summary>⭐ E-2 (DA-62) — 1 = the control region changed this slot: <c>KeepDefence</c> orders it again, then clears it.</summary>
        public fixed byte Dirty[Capacity];
        /// <summary>⭐ E-2 (DA-60) — 1 = this line squad was turned to a flank (once).</summary>
        public fixed byte Flank[Capacity];
        /// <summary>⭐ E-2 (DA-59) — where the OP comes back to.</summary>
        public fixed float BackX[Capacity];
        public fixed float BackY[Capacity];
        public fixed float BackZ[Capacity];
        /// <summary>⭐ <c>CE-2305</c> RQ-3, RQ-4 — 1 = this reserve is out on a casevac (<c>SquadRecover</c>): <c>KeepDefence</c> leaves it
        /// alone until its PROGRESS Set or TaskFailed, then orders it back to its post.</summary>
        public fixed byte Casevac[Capacity];
        /// <summary>⭐ <c>CE-2305</c> — 1 = this reserve is spent: it blocks the stair (E-3b) and is no longer free for anything else.</summary>
        public fixed byte Spent[Capacity];

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
        /// <summary>⭐ E-2 (DA-63) — the SITREP period asked of every squad (s).</summary>
        public const float SitrepSeconds = 15f;
        /// <summary>⭐ E-2 (DA-59) — an enemy this near the OP brings it back (m).</summary>
        public const float OpThreatMetres = 300f;
        /// <summary>⭐ E-2 (DA-60) — an enemy this near the position and outside the area is a flank (m).</summary>
        public const float FlankMetres = 150f;
        /// <summary>⭐ E-2 (DA-61) — the committed reserve stands this far behind the squad it reinforces (m).</summary>
        public const float ReinforceBehindMetres = 15f;
        /// <summary>⭐ E-3a (DA-66) — with no authored fall-back point, the platoon falls back this far behind the position (m).</summary>
        public const float FallBackMetres = 100f;
        /// <summary>⭐ E-3b (DA-72) — the defended building: the survey asks for buildings meeting this circle round the position (m).</summary>
        public const float BuildingAskMetres = 5f;
        /// <summary>⭐ E-3b (DA-72) — the platoon's building-survey site.</summary>
        public const int BuildingSite = 0x22760001;
        /// <summary>⭐ E-3b (DA-74) — the reserve's posts round the stair (m).</summary>
        public const float StairRadius = 6f;
        private const byte StairPortal = 3;   // BuildingPortalKind.Stair
        /// <summary>⭐ E-4 (DA-85, DA-90) — the area's building survey (the battle positions) · how long the plan waits for it (s).</summary>
        public const int PositionsSite = 0x22760002;
        public const float PositionsTimeoutSeconds = 5f;

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
            plan.FallBackTo = authored.FallBackTo;
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
                plan.Set(slot, s.Op ? PlatoonDefencePlan.Op : s.Reserve ? PlatoonDefencePlan.Reserve : PlatoonDefencePlan.Line,
                         s.Point == Vector3.Zero ? authored.Position : s.Point, s.SearchRadius > 0f ? s.SearchRadius : authored.SearchRadius);
                var back = s.Back == Vector3.Zero ? authored.Position : s.Back;   // ⭐ E-2 (DA-59)
                plan.BackX[slot] = back.X; plan.BackY[slot] = back.Y; plan.BackZ[slot] = back.Z;
            }
        }

        [Behavior(BehaviorNames.PlatoonDefend, Params = typeof(PlatoonDefendParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.PlatoonDefend, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonDefendBlackboard))]
        public static StateMachineGraph BuildPlatoonDefend()
        {
            // ⭐ E-2 (Platoon §5) — two regions: 0 the phase (E-1), 1 the control. The control states only edit the plan (DA-62); region 0's
            //   KeepDefence gives every order.
            var builder = new HsmBuilder("PlatoonDefend_HSM");
            var root = builder.State("Defending").Initial().Parallel();
            StateBuilder plan = null!, occupy = null!, ready = null!, engage = null!, fallBack = null!, watch = null!, opBack = null!, supplement = null!, reinforce = null!;
            root.Child("DPlan", c => plan = c.InRegion(0).Initial()
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, PlanDefence));
            root.Child("Occupy", c => occupy = c.InRegion(0)
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, HoldTheLine)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence));
            root.Child("Ready", c => ready = c.InRegion(0)
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, HoldTheLine)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence));
            root.Child("Engage", c => engage = c.InRegion(0)
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, OpenFire)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence));
            root.Child("FallBack", c => fallBack = c.InRegion(0)   // ⭐ E-3a (DA-67…DA-70)
                .OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, FallBackOrders)
                .Activity<PlatoonDefendBlackboard, PlatoonDefencePlan, PlatoonDefendState>(bb => bb.Plan, bb => bb.Keep, KeepDefence));
            root.Child("FellBack", c => c.InRegion(0).Final());
            root.Child("DWatch", c => watch = c.InRegion(1).Initial());
            root.Child("OpBack", c => opBack = c.InRegion(1).OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, BringOpBack));
            root.Child("Supplementary", c => supplement = c.InRegion(1).OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Supplement));
            root.Child("Reinforce", c => reinforce = c.InRegion(1).OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Reinforce));
            StateBuilder block = null!;
            root.Child("BlockStair", c => block = c.InRegion(1).OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, BlockTheStair));
            StateBuilder answer = null!;   // ⭐ CE-2305 RQ-3 — a subordinate's Casevac answered with the free reserve
            root.Child("DAnswer", c => answer = c.InRegion(1).OnEntry<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, AnswerCasevac));
            plan.On(0).GoTo("Occupy").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Planned).Polled();
            occupy.On(0).GoTo("Ready").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, LineSet).Polled();
            ready.On(0).GoTo("Engage").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, EnemyInArea).Polled();
            engage.On(0).GoTo("FallBack").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, HalfTheLineFailing).Polled();
            engage.On(0).GoTo("Ready").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, AreaQuiet).Polled();
            fallBack.On(0).GoTo("FellBack").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, LineSet).Polled();
            watch.On(0).GoTo("OpBack").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, OpThreatened).Polled();
            watch.On(0).GoTo("Supplementary").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Flanked).Polled();
            watch.On(0).GoTo("Reinforce").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, LineSquadFailing).Polled();
            opBack.On(0).GoTo("DWatch").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Handled).Polled();
            supplement.On(0).GoTo("DWatch").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Handled).Polled();
            reinforce.On(0).GoTo("DWatch").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Handled).Polled();
            watch.On(0).GoTo("BlockStair").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, EnemyInside).Polled();
            block.On(0).GoTo("DWatch").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Handled).Polled();
            watch.On(0).GoTo("DAnswer").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, CasevacPending).Polled();
            answer.On(0).GoTo("DWatch").Guard<PlatoonDefendBlackboard, PlatoonDefencePlan>(bb => bb.Plan, Handled).Polled();
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
            p.Controls = p.HoldsFire;   // ⭐ E-2 (DA-65) — below Regular the control region does nothing
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
            {
                // ⭐ E-4 (DA-85…DA-90) — an AREA: one survey of its buildings, then a battle position per line squad
                BattlePositions.Position[]? positions = null;
                if (p.SearchRadius >= BattlePositions.AreaMetres)
                {
                    double now = CommandPictureOps.Now(world);
                    if (p.Asked == 0)
                    {
                        WorldSurveyChildSensor.Ensure(world, self, PositionsSite,
                            WorldSurveyRequest.OfArea(new Vector2(p.Position.X, p.Position.Y), p.SearchRadius));
                        p.Asked = 1;
                        p.AskedAt = now;
                        return NodeStatus.Running;
                    }
                    var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, PositionsSite);
                    var read = WorldSurveyChildSensor.Read(world, child, now);
                    bool ready = read.Ready && !read.Stale && read.Payload != null;
                    if (!ready && now - p.AskedAt < PositionsTimeoutSeconds) return NodeStatus.Running;
                    var area = p.Area;
                    Vector3? middle = EngagementAreasMiddle(world, in area, out var m) ? m : null;
                    positions = BattlePositions.Plan(ready ? read.Payload : null, p.Position, p.SearchRadius, middle, n >= 3 ? n - 1 : n);
                    WorldSurveyChildSensor.Release(world, child);
                }
                for (int slot = 0; slot < n; slot++)
                {
                    bool reserve = n >= 3 && slot == n - 1;
                    if (reserve) p.Set(slot, PlatoonDefencePlan.Reserve, Behind(world, in p, ReserveBehindMetres), positions != null ? BattlePositions.OpenRadius : p.SearchRadius);
                    else if (positions != null) p.Set(slot, PlatoonDefencePlan.Line, positions[slot].Point, positions[slot].Radius);
                    else p.Set(slot, PlatoonDefencePlan.Line, p.Position, p.SearchRadius);
                }
            }
            if (p.Area.ArcSet != 0) Slice(ref p, n);
            if (p.FallBackTo == Vector3.Zero) p.FallBackTo = Behind(world, in p, FallBackMetres);   // ⭐ E-3a (DA-66)
            if (p.Controls != 0)   // ⭐ E-3b (DA-72) — the defended building, surveyed once and kept (a standing sensor)
                WorldSurveyChildSensor.Ensure(world, self, BuildingSite,
                    WorldSurveyRequest.OfArea(new Vector2(p.Position.X, p.Position.Y), BuildingAskMetres));
            p.Made = 1;
            return NodeStatus.Running;
        }

        /// <summary>A point <paramref name="metres"/> behind the position, away from the area's middle (the default reserve's point, and
        /// the default fall-back point); no arc ⇒ the position.</summary>
        private static Vector3 Behind(EntityRepository world, in PlatoonDefencePlan p, float metres)
        {
            var area = p.Area;
            if (!EngagementAreasMiddle(world, in area, out var middle)) return p.Position;
            var away = new Vector2(p.Position.X - middle.X, p.Position.Y - middle.Y);
            if (away.LengthSquared() < 1e-4f) return p.Position;
            away = Vector2.Normalize(away) * metres;
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
                if (squad.IsNull || !world.IsAlive(squad)) continue;
                if (p.Casevac[slot] != 0)   // ⭐ CE-2305 RQ-4 — out on a casevac: back to its post once that task ended (Stands is only "recorded")
                {
                    var progress = CommandPictureOps.ProgressOf(world, self, slot);
                    if (progress != ProgressKind.Set && progress != ProgressKind.TaskFailed) continue;
                    p.Casevac[slot] = 0;
                    p.Dirty[slot] = 1;
                }
                if (p.Dirty[slot] == 0 && CommandOrders.Stands(world, self, squad)) continue;   // ⭐ E-2 (DA-62) — a dirty slot goes again
                p.Dirty[slot] = 0;
                var json = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
                if (p.Falling != 0 && p.Role[slot] == PlatoonDefencePlan.Line)   // ⭐ E-3a (DA-68) — the line bounds back
                    CommandOrders.Assign(world, self, squad, BehaviorNames.BreakContact,
                                         JsonSerializer.Serialize(new BreakContactParamsJsonDto { To = p.FallBackTo, Speed = p.Speed }, json), newOrder: true);
                else
                {
                    var order = p.Falling != 0   // ⭐ E-3a (DA-69) — the reserve (and an OP) covers at the fall-back point
                        ? new SquadDefendParamsJsonDto { Position = p.FallBackTo, SearchRadius = p.Radius[slot], Speed = p.Speed }
                        : new SquadDefendParamsJsonDto { Position = p.Point(slot), SearchRadius = p.Radius[slot], Speed = p.Speed };
                    CommandOrders.Assign(world, self, squad, BehaviorNames.SquadDefend, JsonSerializer.Serialize(order, json), newOrder: true);
                }
                Sector(ref p, slot, world, squad);
                AskSitrep(world, squad);
                ws.Orders++;
            }
            return NodeStatus.Running;
        }

        /// <summary>⭐ E-2 (DA-63) — the SITREP every few seconds the Reinforce guard reads (its strength colour), asked with each order.</summary>
        private static void AskSitrep(EntityRepository world, Entity squad)
        {
            if (!world.Bus.IsRegistered<SetReportingEvent>()) return;
            world.Bus.Publish(new SetReportingEvent { Entity = squad, Origin = BehaviorOrigin.Superior, SitrepEverySeconds = SitrepSeconds });
        }

        private static unsafe void Sector(ref PlatoonDefencePlan p, int slot, EntityRepository world, Entity squad)
        {
            if (!world.Bus.IsRegistered<SetEngagementAreaEvent>()) return;
            if (p.Role[slot] != PlatoonDefencePlan.Line || p.Falling != 0 || (p.Area.ArcSet == 0 && p.Area.AllowArea == 0))
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

        // ── the control region (E-2: DA-59…DA-62, Platoon §5) ────────────────────────────────────────────────────────────

        /// <summary>DWatch → OpBack (DA-59): an OP squad has a fresh element within <see cref="OpThreatMetres"/>.</summary>
        [SharedAiCondition]
        public static bool OpThreatened(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
            => p.Controls != 0 && p.Made != 0 && p.Falling == 0 && ThreatenedOp(ref p, self, world) >= 0;

        /// <summary>OpBack's entry (DA-59): the OP goes back to its <c>Back</c> point and becomes a reserve; <c>KeepDefence</c> orders it.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus BringOpBack(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int slot = ThreatenedOp(ref p, self, world);
            if (slot < 0) return NodeStatus.Success;
            p.Set(slot, PlatoonDefencePlan.Reserve, new Vector3(p.BackX[slot], p.BackY[slot], p.BackZ[slot]), p.Radius[slot]);
            p.Dirty[slot] = 1;
            return NodeStatus.Success;
        }

        /// <summary>DWatch → Supplementary (DA-60): a fresh element within <see cref="FlankMetres"/> of the position, outside the area's
        /// bearings, and a line squad not yet turned.</summary>
        [SharedAiCondition]
        public static bool Flanked(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
            => p.Controls != 0 && p.Made != 0 && p.Falling == 0 && FlankTarget(ref p, self, world, out _, out _);

        /// <summary>Supplementary's entry (DA-60): the line squad nearest the flank gets a slice centred on it (its own slice's width) and
        /// is ordered again — its <c>SquadDefend</c> re-plans its posts facing the flank.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus Supplement(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            if (!FlankTarget(ref p, self, world, out int slot, out var at)) return NodeStatus.Success;
            float width = SectorSplit.Norm(p.Right[slot] - p.Left[slot]);
            if (width <= 0f) width = 90f;
            float bearing = BearingOf(p.Area.ArcOrigin, at);
            p.Left[slot] = SectorSplit.Norm(bearing - width * 0.5f);
            p.Right[slot] = SectorSplit.Norm(bearing + width * 0.5f);
            p.Flank[slot] = 1;
            p.Dirty[slot] = 1;
            return NodeStatus.Success;
        }

        /// <summary>DWatch → Reinforce (DA-61): a line squad's SITREP Red or worse with a fresh element inside its slice, and a reserve not
        /// yet committed.</summary>
        [SharedAiCondition]
        public static bool LineSquadFailing(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
            => p.Controls != 0 && p.Made != 0 && p.Falling == 0 && p.Committed == 0 && FailingSquad(ref p, self, world, out _, out _);

        /// <summary>Reinforce's entry (DA-61): the reserve takes the failing squad's slice, <see cref="ReinforceBehindMetres"/> behind it,
        /// on the line, firing at will; once.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus Reinforce(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            if (p.Committed != 0 || !FailingSquad(ref p, self, world, out int squad, out int reserve)) return NodeStatus.Success;
            float middle = p.Left[squad] + SectorSplit.Norm(p.Right[squad] - p.Left[squad]) * 0.5f;
            float rad = middle * (MathF.PI / 180f);
            var behind = p.Point(squad) - new Vector3(MathF.Sin(rad), MathF.Cos(rad), 0f) * ReinforceBehindMetres;
            p.Set(reserve, PlatoonDefencePlan.Line, behind, p.Radius[reserve]);
            p.Left[reserve] = p.Left[squad];
            p.Right[reserve] = p.Right[squad];
            p.Dirty[reserve] = 1;
            p.Committed = 1;
            var unit = CommandPictureOps.Subordinate(world, self, reserve);
            if (!unit.IsNull && world.Bus.IsRegistered<SetRoeEvent>())
                world.Bus.Publish(new SetRoeEvent { Entity = unit, Origin = BehaviorOrigin.Superior, Fire = RoeFire.FireAtWill });
            return NodeStatus.Success;
        }

        // ── the fall-back (E-3a: DA-66…DA-70) ────────────────────────────────────────────────────────────────────────

        /// <summary>Engage → FallBack (DA-67): half or more of the live line squads report SITREP Red or worse.</summary>
        [SharedAiCondition]
        public static unsafe bool HalfTheLineFailing(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity), lines = 0, failing = 0;
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != PlatoonDefencePlan.Line || !Alive(world, self, s)) continue;
                lines++;
                var colour = new SubordinateColourParams { Slot = s, AtLeast = StatusColour.Red };
                if (PictureConditions.SubordinateColour(ref colour, self, world)) failing++;
            }
            return failing > 0 && failing * 2 >= lines;
        }

        /// <summary>FallBack's entry (DA-68, DA-69): the platoon falls back — every squad is ordered again (<c>KeepDefence</c> picks the
        /// fall-back order), and everyone fires at will. FallBack → FellBack is <see cref="LineSet"/>: every line squad Set for its
        /// BreakContact.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus FallBackOrders(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            p.Falling = 1;
            for (int s = 0; s < PlatoonDefencePlan.Capacity; s++) if (p.Role[s] != PlatoonDefencePlan.NoRole) p.Dirty[s] = 1;
            SetFire(ref p, self, world, RoeFire.FireAtWill, RoeFire.FireAtWill);
            return NodeStatus.Success;
        }

        // ── block the stair (E-3b: DA-72…DA-76) ──────────────────────────────────────────────────────────────────────

        /// <summary>DWatch → BlockStair (DA-73, DA-76): a fresh element inside an indoor room of the defended building, a free reserve not
        /// yet committed, a stair to block (Regular+; not while falling back). Once: blocking commits the reserve.</summary>
        [SharedAiCondition]
        public static bool EnemyInside(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
            => p.Controls != 0 && p.Made != 0 && p.Falling == 0 && p.Committed == 0
               && FreeReserve(ref p, self, world) >= 0 && Stair(ref p, self, world, out _, out var building) && AnyFresh(self, world, at => Inside(building!, at));

        /// <summary>BlockStair's entry (DA-74): the reserve defends the building's lowest stair, firing at will; it is spent.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus BlockTheStair(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int reserve = FreeReserve(ref p, self, world);
            if (reserve < 0 || !Stair(ref p, self, world, out var stair, out _)) return NodeStatus.Success;
            p.Set(reserve, PlatoonDefencePlan.Reserve, stair, StairRadius);
            p.Dirty[reserve] = 1;
            p.Spent[reserve] = 1;
            p.Committed = 1;
            var unit = CommandPictureOps.Subordinate(world, self, reserve);
            if (!unit.IsNull && world.Bus.IsRegistered<SetRoeEvent>())
                world.Bus.Publish(new SetRoeEvent { Entity = unit, Origin = BehaviorOrigin.Superior, Fire = RoeFire.FireAtWill });
            return NodeStatus.Success;
        }

        /// <summary>A live reserve not out on a casevac and not spent on the stair (CE-2305), or -1. ⭐ A reserve committed by
        /// <c>Reinforce</c> is on the line (its role is Line), so it is no reserve here.</summary>
        private static unsafe int FreeReserve(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity);
            for (int s = 0; s < n; s++)
                if (p.Role[s] == PlatoonDefencePlan.Reserve && p.Casevac[s] == 0 && p.Spent[s] == 0 && Alive(world, self, s)) return s;
            return -1;
        }

        // ── the request round trip (CE-2305: RQ-3, RQ-4) ─────────────────────────────────────────────────────────────

        /// <summary>DWatch → DAnswer (RQ-3): a subordinate's Casevac waits and a reserve is FREE (<see cref="FreeReserve"/>: not on the
        /// line, not at the stair, not on another casevac — PER RESERVE, so a second reserve still answers); not while falling back. No free
        /// reserve ⇒ the request waits in the picture.</summary>
        [SharedAiCondition]
        public static bool CasevacPending(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
            => p.Made != 0 && p.Falling == 0
               && CommandPictureOps.FirstRequest(world, self, RequestKind.Casevac) >= 0 && FreeReserve(ref p, self, world) >= 0;

        /// <summary>DAnswer's entry (RQ-3): the free reserve is ordered <c>SquadRecover</c> — fetch the request's casualty to the reserve's
        /// own point and put him down — and the request is answered (it leaves the picture). A request naming nobody is answered with no
        /// order: there is no one to fetch.</summary>
        [SharedAiAction]
        public static unsafe NodeStatus AnswerCasevac(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int index = CommandPictureOps.FirstRequest(world, self, RequestKind.Casevac);
            int reserve = FreeReserve(ref p, self, world);
            if (index < 0 || reserve < 0) return NodeStatus.Success;
            long casualty = world.GetComponentRO<CommandPicture>(self).Requests.Items[index].SubjectNetId;
            CommandPictureOps.Answered(world, self, index);
            if (casualty == 0) return NodeStatus.Success;
            var unit = CommandPictureOps.Subordinate(world, self, reserve);
            var order = new SquadRecoverParamsJsonDto { Casualty = new Fdp.Toolkit.Replication.EntityRef(casualty), To = p.Point(reserve), PutDown = true, Speed = p.Speed };
            CommandOrders.Assign(world, self, unit, BehaviorNames.SquadRecover,
                                 JsonSerializer.Serialize(order, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), newOrder: true);
            p.Casevac[reserve] = 1;
            return NodeStatus.Success;
        }

        /// <summary>DA-72, DA-74 — the defended building (the surveyed one nearest the position) and its lowest stair, from a ready and
        /// fresh answer; false when there is none.</summary>
        private static bool Stair(ref PlatoonDefencePlan p, Entity self, EntityRepository world, out Vector3 stair, out DefendedBuilding? building)
        {
            stair = default; building = null;
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, BuildingSite);
            var read = WorldSurveyChildSensor.Read(world, child, CommandPictureOps.Now(world));
            if (!read.Ready || read.Stale || read.Payload == null || read.Payload.Buildings.Count == 0) return false;
            var answer = read.Payload;
            SurveyBuilding? ours = null;
            float best = float.MaxValue;
            foreach (var b in answer.Buildings)
            {
                float d = Vector2.Distance(new Vector2(b.CentreX, b.CentreY), new Vector2(p.Position.X, p.Position.Y));
                if (d < best) { best = d; ours = b; }
            }
            bool found = false;
            foreach (var portal in answer.Portals)
                if (portal.Building == ours!.Building && portal.Kind == StairPortal && (!found || portal.Z < stair.Z))
                {
                    stair = new Vector3(portal.X, portal.Y, portal.Z);
                    found = true;
                }
            if (found) building = new DefendedBuilding(ours!.Building, answer);
            return found;
        }

        /// <summary>The defended building of one survey answer.</summary>
        private sealed record DefendedBuilding(int Building, WorldSurveyPayload Answer);

        /// <summary>DA-73 — inside an indoor room of the building, at that room's storey.</summary>
        private static bool Inside(DefendedBuilding building, Vector3 at)
        {
            foreach (var room in building.Answer.Rooms)
            {
                if (room.Building != building.Building || !room.Indoors || room.Corners.Length < 8) continue;
                if (at.Z < room.FloorZ - 1f || at.Z > room.FloorZ + 2.8f) continue;
                if (InQuad(room.Corners, at.X, at.Y)) return true;
            }
            return false;
        }

        private static bool InQuad(float[] c, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = 3; i < 4; j = i++)
            {
                float xi = c[i * 2], yi = c[i * 2 + 1], xj = c[j * 2], yj = c[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        /// <summary>The control states' way back to DWatch: their entry did the work.</summary>
        [SharedAiCondition]
        public static bool Handled(ref PlatoonDefencePlan p, Entity self, EntityRepository world) => true;

        private static unsafe int ThreatenedOp(ref PlatoonDefencePlan p, Entity self, EntityRepository world)
        {
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity);
            for (int slot = 0; slot < n; slot++)
            {
                if (p.Role[slot] != PlatoonDefencePlan.Op || !Alive(world, self, slot)) continue;
                var op = p.Point(slot);
                if (AnyFresh(self, world, at => Flat(at, op) <= OpThreatMetres)) return slot;
            }
            return -1;
        }

        private static unsafe bool FlankTarget(ref PlatoonDefencePlan p, Entity self, EntityRepository world, out int slot, out Vector3 at)
        {
            slot = -1; at = default;
            if (p.Area.ArcSet == 0 || !world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float now = CommandPictureOps.Now(world);
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity);
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count == 0 || now - e.ObservedAt > TriggerFreshSeconds || Flat(e.At, p.Position) > FlankMetres
                    || EngagementAreas.InArc(p.Area.ArcOrigin, p.Area.LeftBearing, p.Area.RightBearing, 0f, 0f, e.At)
                    || Faced(ref p, n, e.At)) continue;
                float best = float.MaxValue;
                for (int s = 0; s < n; s++)
                {
                    if (p.Role[s] != PlatoonDefencePlan.Line || p.Flank[s] != 0 || !Alive(world, self, s)) continue;
                    float d = Flat(p.Point(s), e.At);
                    if (d < best) { best = d; slot = s; }
                }
                if (slot >= 0) { at = e.At; return true; }
            }
            return false;
        }

        /// <summary>DA-60 — a flank element a turned squad's slice already holds is handled: it does not turn the next squad too.</summary>
        private static unsafe bool Faced(ref PlatoonDefencePlan p, int n, Vector3 at)
        {
            for (int s = 0; s < n; s++)
                if (p.Flank[s] != 0 && EngagementAreas.InArc(p.Area.ArcOrigin, p.Left[s], p.Right[s], 0f, 0f, at)) return true;
            return false;
        }

        private static unsafe bool FailingSquad(ref PlatoonDefencePlan p, Entity self, EntityRepository world, out int squad, out int reserve)
        {
            squad = reserve = -1;
            int n = Math.Min(RosterCount(world, self), PlatoonDefencePlan.Capacity);
            for (int s = 0; s < n && reserve < 0; s++)
                if (p.Role[s] == PlatoonDefencePlan.Reserve && Alive(world, self, s)) reserve = s;
            if (reserve < 0) return false;
            var area = p.Area;
            for (int s = 0; s < n; s++)
            {
                if (p.Role[s] != PlatoonDefencePlan.Line || !Alive(world, self, s)) continue;
                var colour = new SubordinateColourParams { Slot = s, AtLeast = StatusColour.Red };
                if (!PictureConditions.SubordinateColour(ref colour, self, world)) continue;
                float left = p.Left[s], right = p.Right[s];
                bool inSlice = AnyFresh(self, world, at => area.ArcSet != 0
                    ? EngagementAreas.InArc(area.ArcOrigin, left, right, area.MinRange, area.MaxRange, at)
                    : EngagementAreas.Contains(world, in area, at));
                if (inSlice) { squad = s; return true; }
            }
            return false;
        }

        private static bool AnyFresh(Entity self, EntityRepository world, Func<Vector3, bool> where)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            float now = CommandPictureOps.Now(world);
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Count != 0 && now - e.ObservedAt <= TriggerFreshSeconds && where(e.At)) return true;
            }
            return false;
        }

        private static bool Alive(EntityRepository world, Entity self, int slot)
        {
            var squad = CommandPictureOps.Subordinate(world, self, slot);
            return !squad.IsNull && world.IsAlive(squad);
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));

        private static float BearingOf(Vector3 from, Vector3 to) => SectorSplit.Norm(MathF.Atan2(to.X - from.X, to.Y - from.Y) * (180f / MathF.PI));

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

        private static readonly int DefendHash = BehaviorHash.FromName(BehaviorNames.PlatoonDefend);

        /// <summary>⭐ DA-55 — the platoon's task is NOT a defence. <c>BasicPlatoonSop</c>'s hold row asks it first: a defending platoon's
        /// squads already hold their posts, and pausing the task to order them <c>Hold</c> would pull them off the posts and stop the
        /// trigger.</summary>
        [SharedAiCondition]
        public static bool NotDefending(Entity self, EntityRepository world)
            => !world.HasComponent<BehaviorState>(self) || world.GetComponentRO<BehaviorState>(self).ActiveBehaviorHash != DefendHash;

        private static int RosterCount(EntityRepository world, Entity self)
            => world.HasComponent<UnitRoster>(self) ? world.GetComponentRO<UnitRoster>(self).Count : 0;
    }
}

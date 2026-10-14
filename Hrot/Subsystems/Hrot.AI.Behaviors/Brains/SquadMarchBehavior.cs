using System;
using System.Numerics;
using System.Runtime.InteropServices;
using CarKinem.Formation;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2123</c> S3 — a squad march (resolved from <see cref="SquadMarchParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadMarchParams
    {
        public Vector3 Objective;
        /// <summary><see cref="SquadMarchManeuver.ShapeFile"/> / <c>ShapeWedge</c> / <c>ShapeLine</c>.</summary>
        public byte Shape;
        public float Spacing;
        public float Speed;
        public float ArrivalRadius;
        public float MaxLagMetres;
        /// <summary>⭐ <c>CE-2159</c> X-a — 1 = the march owns the commander's danger-area sensor to <see cref="Objective"/>.</summary>
        public byte DangerAreas;
        /// <summary>⭐ <c>CE-2161</c> — 1 = the danger-area sensor also asks for OPEN GROUND (CE-3081 A5), so D3 can choose Bound.</summary>
        public byte OpenGround;
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> S3 — the squad MARCHES to a point on the commander: the lead walks there, the others keep their File / Wedge /
    /// Line slot on it, and a member whose slot does not work walks in file on the lead's own trail.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §6.1.
    /// </summary>
    public static class SquadMarchBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SquadMarchBlackboard
        {
            public SquadMarchParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadMarch)]
        public static void Resolve(in SquadMarchParamsJsonDto authored, ref SquadMarchBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadMarchParams
            {
                Objective     = authored.Objective,
                Shape         = authored.Shape is >= SquadMarchManeuver.ShapeFile and <= SquadMarchManeuver.ShapeLine ? (byte)authored.Shape : SquadMarchManeuver.ShapeFile,
                Spacing       = authored.Spacing > 0f ? authored.Spacing : 5f,
                Speed         = authored.Speed > 0f ? authored.Speed : 1.5f,
                ArrivalRadius = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : 5f,
                MaxLagMetres  = authored.MaxLagMetres > 0f ? authored.MaxLagMetres : 10f,
                DangerAreas   = authored.DangerAreas ? (byte)1 : (byte)0,
                OpenGround    = authored.OpenGround ? (byte)1 : (byte)0,
            };
            block.State = default;
        }
[Behavior(BehaviorNames.SquadMarch, Params = typeof(global::Hrot.Map.Definitions.Behavior.SquadMarchParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.SquadMarch, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadMarchBlackboard))]
        public static BTreeBuilder<SquadMarchBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadMarchBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadMarchParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadMarchPlay, SquadMarchParams>(ref p, ref ws, self, world);
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> S3 — the march as a play over <see cref="SquadMarchManeuver"/>. The runner sends every order at the start,
    /// after a pause and when the lead changes; between those, <see cref="Update"/> re-orders ONE member when its slot has moved
    /// <see cref="ReorderMetres"/> — so the path-request cost follows how far each slot moves, not squad size × ticks.
    /// </summary>
    public unsafe struct SquadMarchPlay : ISquadPlay<SquadMarchParams>
    {
        /// <summary>A follower is re-ordered when its slot is this far from its last order (m).</summary>
        public const float ReorderMetres = 3f;
        /// <summary>No 0.5 m of progress toward its point for this long ⇒ the member walks on file (s).</summary>
        public const float StuckSeconds = 3f;
        public const float ProgressMetres = 0.5f;
        /// <summary>How long a member stays on file before it tries its shape slot again (s).</summary>
        public const float FileSeconds = 10f;
        /// <summary>The heading is the lead's last this-many metres of travel.</summary>
        public const float HeadingLookback = 5f;
        /// <summary>The lead waits for stragglers at most this long, then this long without waiting (s).</summary>
        public const float HaltMaxSeconds = 15f;
        /// <summary>After the lead arrives the march ends when everyone is at its slot, or after this long (s).</summary>
        public const float SettleSeconds = 20f;
        /// <summary>A lead that gets no 0.5 m closer to the objective for this long hands the lead on (s) — 📐 live: the first lead
        /// picked stood on a spot no move works from (z 9 on <c>test-town</c>) and the whole squad waited behind it.</summary>
        public const float LeadStuckSeconds = 5f;
        /// <summary>⭐ <c>CE-2159</c> (§6.1b) — a follower more than <see cref="CatchUpLagMetres"/> behind its slot is sent this much faster
        /// than the march: a slot on the lead's trail moves at the march speed, so a man sent at that speed never closes the gap and
        /// the lead held for him again and again (📐 live: ~0.3 m/s from a diagonal start).</summary>
        public const float CatchUpFactor = 1.6f;
        public const float CatchUpLagMetres = 4f;

        private static readonly SquadHsmShell s_shell = new(SquadMarchManeuver.BuildTransitionTable(), SquadMarchManeuver.PhaseAborted);
        private static readonly FormationTemplateManager s_templates = new();

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadMarchManeuver.ManeuverKind;
        public ushort StartPhase => SquadMarchManeuver.PhaseMarching;
        public ushort AbortPhase => SquadMarchManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadMarchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            ws.March = default;
            ws.March.LeadArrivedAt = -1f;
            int lead = PickLead(in p, ref ws.March, in ctx);
            if (lead < 0) return PlayStart.Fail;
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            TakeLead(ref ws, in ctx, lead, 0f, in p);
            return PlayStart.Go;
        }

        public void Update(ref SquadMarchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.DangerAreas != 0) WatchRoute(in p, in ws, in ctx);
            ref var march = ref ws.March;
            float t = (float)(ctx.Now - ws.StartedAt);
            int lead = march.LeadPlusOne - 1;
            if (lead < 0 || !ctx.Live(lead, out _))
            {
                // the lead fell, or handed the lead on: the next one leads from where IT stands, and every order goes out again
                lead = PickLead(in p, ref march, in ctx);
                if (lead < 0) return;
                TakeLead(ref ws, in ctx, lead, t, in p);
                ws.Reissue = 1;
                return;
            }
            var leadEntity = ctx.Roster.SubordinateEntities[lead];
            var leadAt = ctx.At(leadEntity);
            if (march.Trail.Record(leadAt)) march.LeadWalked += SquadTrail.CrumbMetres;
            float toGo = Flat(p.Objective - leadAt);
            if (march.LeadArrivedAt < 0f && toGo <= p.ArrivalRadius) march.LeadArrivedAt = t;
            if (ws.IssuedPhase != ctx.Squad.PhaseId || ws.Reissue != 0) return;                       // the runner sends everything this tick

            // A lead that makes no progress (not waiting, not there) hands the lead on.
            if (march.LeadHalted != 0 || march.LeadArrivedAt >= 0f || toGo < march.LeadBest - ProgressMetres) { march.LeadBest = MathF.Min(march.LeadBest, toGo); march.LeadProgressAt = t; }
            else if (t - march.LeadProgressAt > LeadStuckSeconds)
            {
                march.FailedLeads |= (ushort)(1 << lead);
                march.LeadPlusOne = 0;
                return;
            }
            bool silent = SquadLeadership.Silent(ctx.World, ctx.Commander, ctx.Now);                    // ⭐ CE-2125 — no new orders

            var forward = Forward(in p, ref march, leadAt);
            float worstLag = 0f;
            bool allAtSlots = true;
            int rank = 0;
            for (int i = 0; i < ctx.Roster.Count; i++)
            {
                if (i == lead || !ctx.Live(i, out var m)) continue;
                var at = ctx.At(m);
                var ordered = new Vector3(ws.TargetX[i], ws.TargetY[i], ws.TargetZ[i]);

                float d = Flat(ordered - at);
                if (d < march.Best[i] - ProgressMetres) { march.Best[i] = d; march.ProgressAt[i] = t; }
                else if (d > SquadManeuverRunner.MoverArrivalMetres && t - march.ProgressAt[i] > StuckSeconds && march.FileUntil[i] <= t)
                    march.FileUntil[i] = t + FileSeconds;                                               // stuck ⇒ walk on the lead's trail

                bool has = Slot(in p, ref march, leadAt, at, forward, rank++, i, t, out var slot);
                if (!has) continue;                                                                     // file, trail not long enough yet: keep holding
                float lag = Flat(slot - at);
                if (lag > worstLag) worstLag = lag;
                if (lag > SquadManeuverRunner.MoverArrivalMetres) allAtSlots = false;
                if (!silent && Flat(slot - ordered) >= ReorderMetres)
                {
                    SquadManeuverRunner.Move(ctx.World, ctx.Commander, m, slot, FollowerSpeed(in p, lag), SquadManeuverRunner.MoverArrivalMetres);
                    Aim(ref ws, i, slot, at, t);
                    march.Reorders++;
                }
            }
            march.AllAtSlots = allAtSlots ? (byte)1 : (byte)0;
            if (silent || march.LeadArrivedAt >= 0f) return;

            // The lead waits for stragglers — at most HaltMaxSeconds, then as long again without waiting.
            if (march.LeadHalted == 0 && worstLag > p.MaxLagMetres && t >= march.HaltsAllowedAt && march.LeadWalked >= p.MaxLagMetres)
            {
                march.LeadHalted = 1; march.HaltSince = t;
                SquadManeuverRunner.Hold(ctx.World, ctx.Commander, leadEntity, leadAt, p.Speed);
                Aim(ref ws, lead, leadAt, leadAt, t);
                march.Reorders++;
            }
            else if (march.LeadHalted != 0 && (worstLag < p.MaxLagMetres * 0.5f || t - march.HaltSince > HaltMaxSeconds))
            {
                if (t - march.HaltSince > HaltMaxSeconds) march.HaltsAllowedAt = t + HaltMaxSeconds;
                march.LeadHalted = 0;
                march.LeadProgressAt = t;
                SquadManeuverRunner.Move(ctx.World, ctx.Commander, leadEntity, p.Objective, p.Speed, p.ArrivalRadius);
                Aim(ref ws, lead, p.Objective, leadAt, t);
                march.Reorders++;
            }
        }

        /// <summary>The commander's danger-area sensor's site (the march's run owns it).</summary>
        public const int DangerSensorSite = 0x21590001;

        /// <summary>
        /// ⭐ <c>CE-2159</c> X-a (Squad Tactics §7.7.6) — the march owns the commander's route sensor: the route from where the squad
        /// stands to the objective, answered every 2 s. A crossing reaction pauses the march and keeps it (the SOP row and the crossing
        /// read it); back from a pause the march RE-POINTS it, so the answer it reads next is from where the squad now stands — past the
        /// area it crossed.
        /// </summary>
        private static void WatchRoute(in SquadMarchParams p, in SquadPlayState ws, in SquadPlayContext ctx)
        {
            var settings = Fdp.Toolkit.Squad.DangerArea.DangerAreaSettings.ToPoint(p.Objective);
            // ⭐ CE-2161 — open ground too, when the order asks (the crossings alone otherwise: CE-3081 A5)
            if (p.OpenGround != 0)
                settings.Kinds = Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds.OpenGround
                               | Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds.StreetCrossing
                               | Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds.Intersection;
            var sensor = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(ctx.World, ctx.Commander, DangerSensorSite);
            if (sensor.IsNull) Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure(ctx.World, ctx.Commander, DangerSensorSite, in settings);
            else if (ws.Reissue != 0) Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Configure(ctx.World, sensor, in settings);
        }

        public NodeStatus Outcome(ref SquadMarchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            if (ws.March.LeadArrivedAt < 0f) return NodeStatus.Running;
            float t = (float)(ctx.Now - ws.StartedAt);
            return ws.March.AllAtSlots != 0 || t - ws.March.LeadArrivedAt >= SettleSeconds ? NodeStatus.Success : NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadMarchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref SquadMarchParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadMarchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            float t = (float)(ctx.Now - ws.StartedAt);
            ws.March.Best[member] = float.MaxValue;
            ws.March.ProgressAt[member] = t;
            int lead = ws.March.LeadPlusOne - 1;
            if (member == lead)
                return ws.March.LeadHalted != 0
                    ? new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadMarchManeuver.RoleLead }
                    : new MemberOrder { Kind = MemberOrderKind.AdvanceTo, Point = p.Objective, Speed = p.Speed, ArrivalRadius = p.ArrivalRadius,
                                        RoleId = SquadMarchManeuver.RoleLead };

            int rank = 0;
            for (int i = 0; i < member; i++) if (i != lead && ctx.Live(i, out _)) rank++;
            if (lead < 0) return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadMarchManeuver.RoleFollower };
            var leadAt = ctx.At(ctx.Roster.SubordinateEntities[lead]);
            if (!Slot(in p, ref ws.March, leadAt, at, Forward(in p, ref ws.March, leadAt), rank, member, t, out var slot))
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadMarchManeuver.RoleFollower };
            return new MemberOrder
            {
                Kind = MemberOrderKind.AdvanceTo, Point = slot, Speed = FollowerSpeed(in p, Flat(slot - at)),
                ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres, RoleId = SquadMarchManeuver.RoleFollower,
            };
        }

        /// <summary>⭐ <c>CE-2159</c> (§6.1b) — the follower's asked speed: the march's, or faster while he is behind his slot.</summary>
        public static float FollowerSpeed(in SquadMarchParams p, float lag) => lag > CatchUpLagMetres ? p.Speed * CatchUpFactor : p.Speed;

        /// <summary>⭐ CE-2141 — nothing of its own to drop; the members' orders are released when the commander's RUN ends
        /// (<c>SubordinateOrders</c>), not here: a play inside <c>ReactToContact</c> ends and the next starts in one tick.</summary>
        public void Release(ref SquadPlayState ws, EntityRepository world) { }

        /// <summary>
        /// The next lead: the live member NEAREST the objective (the one in front — a point man, not the leader), skipping those that
        /// already failed as lead; when every live member has failed, the slate is wiped and they get another turn. −1 = nobody alive.
        /// </summary>
        public static int PickLead(in SquadMarchParams p, ref MarchMemory march, in SquadPlayContext ctx)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < ctx.Roster.Count; i++)
                {
                    if ((march.FailedLeads & (1 << i)) != 0 || !ctx.Live(i, out var m)) continue;
                    float d = Flat(p.Objective - ctx.At(m));
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0) return best;
                march.FailedLeads = 0;
            }
            return -1;
        }

        /// <summary><paramref name="lead"/> leads from where it stands: a fresh trail (the old one may not join its position by a
        /// walkable line), no waiting yet.</summary>
        private static void TakeLead(ref SquadPlayState ws, in SquadPlayContext ctx, int lead, float t, in SquadMarchParams p)
        {
            ref var march = ref ws.March;
            var at = ctx.At(ctx.Roster.SubordinateEntities[lead]);
            march.LeadPlusOne = (byte)(lead + 1);
            march.LeadHalted = 0;
            march.LeadWalked = 0f;
            march.LeadBest = Flat(p.Objective - at);
            march.LeadProgressAt = t;
            march.Trail.Reset(at);
        }

        /// <summary>
        /// Follower <paramref name="rank"/>'s slot: on FILE (the shape, or a member falling back) the point <c>(rank+1) × Spacing</c>
        /// behind the lead on its trail — false while the trail is shorter; otherwise the Wedge / Line template offset turned to the
        /// lead's heading and scaled by <c>Spacing / 5</c> (the templates are built at 4–5 m).
        /// </summary>
        public static bool Slot(in SquadMarchParams p, ref MarchMemory march, Vector3 leadAt, Vector3 memberAt, Vector2 forward, int rank, int member, float t, out Vector3 slot)
        {
            if (p.Shape == SquadMarchManeuver.ShapeFile || march.FileUntil[member] > t)
                return march.Trail.PointBehind(leadAt, (rank + 1) * p.Spacing, out slot);
            var template = s_templates.GetTemplate(p.Shape == SquadMarchManeuver.ShapeWedge ? FormationType.Wedge : FormationType.Line);
            var offset = template.GetSlotPosition(rank, Vector2.Zero, forward) * (p.Spacing / 5f);
            // the height is the MEMBER's own (its ground), not the lead's: 📐 live, a lead standing at z 9 sent every slot 9 m above
            // the ground the others stood on — off the walkable area, so no move worked. A file slot keeps the trail's own height.
            slot = new Vector3(leadAt.X + offset.X, leadAt.Y + offset.Y, memberAt.Z);
            return true;
        }

        /// <summary>The lead's heading: its last <see cref="HeadingLookback"/> m of travel, or toward the objective before that.</summary>
        private static Vector2 Forward(in SquadMarchParams p, ref MarchMemory march, Vector3 leadAt)
        {
            if (march.Trail.Heading(leadAt, HeadingLookback, out var f)) return f;
            var d = new Vector2(p.Objective.X - leadAt.X, p.Objective.Y - leadAt.Y);
            return d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        private static void Aim(ref SquadPlayState ws, int member, Vector3 point, Vector3 at, float t)
        {
            ws.TargetX[member] = point.X; ws.TargetY[member] = point.Y; ws.TargetZ[member] = point.Z;
            ws.March.Best[member] = Flat(point - at);
            ws.March.ProgressAt[member] = t;
        }

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
    }
}

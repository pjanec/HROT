using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Utility;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2159</c> X-b — the squad danger crossing's params; the area and the pair fields are the play's own memory.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadDangerCrossingParams
    {
        /// <summary>How many lanes across the crossing line, and how far apart (m).</summary>
        public byte Lanes;
        public float LaneSpacing;
        /// <summary>The near spots stand this far short of the near handle, the far spots this far past the far handle (m).</summary>
        public float BackMetres;
        public float BeyondMetres;
        /// <summary>The walking speed to the spots, and the crossers' speed (m/s).</summary>
        public float Speed;
        public float CrossSpeed;
        /// <summary>A phase whose gate does not hold in this long moves on anyway (traced as Timeout) (s).</summary>
        public float PhaseTimeoutSeconds;

        // ── runtime ──
        /// <summary>The area, copied at Start: the buffer moves with the squad while it crosses.</summary>
        public Vector3 Near, Far;
        public uint Feature;
        /// <summary>The lanes used so far (reset when all are used, so consecutive pairs never share one).</summary>
        public SlotRotationState LaneRotation;
        /// <summary>Roster bits past the far handle.</summary>
        public ushort Across;
        /// <summary>Roster bits of the pair crossing now, and its lane.</summary>
        public ushort Pair;
        public byte PairLane;
        /// <summary>The phase the fields below were reset for, and when it began.</summary>
        public ushort SeenPhase;
        public double PhaseSince;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2159</c> X-b (R-2014; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.7.6) — the squad crosses a danger area
    /// over <see cref="DangerAreaCrossingManeuver"/>: the security half watches beside the near side while the crossing half waits short
    /// of it; once security is set, the crossers go ONE PAIR AT A TIME, each pair on the next lane; across, they watch from the far side;
    /// once they are set, security crosses the same way; then the squad reforms beyond. Covering members WATCH (D5) — nobody fires at
    /// what they do not see. The area is the commander's own danger-area sensor's next one (the march owns it — <see cref="SquadMarchPlay"/>).
    /// </summary>
    public struct DangerCrossingPlay : ISquadPlay<SquadDangerCrossingParams>
    {
        /// <summary>Spots beside a crossing point: the first at this side offset, each further pair this much wider (m).</summary>
        public const float SpotOffsetMetres = 5f;
        public const float SpotStepMetres = 3f;
        /// <summary>The reform point lies this far past the far spots (m), the men this far apart across it.</summary>
        public const float ReformMetres = 8f;
        public const float ReformSpacing = 2.5f;
        /// <summary>A watcher this close to his spot is set (m).</summary>
        public const float SetMetres = 2f;
        /// <summary>The reform ends when everyone is set, or after this long (s).</summary>
        public const float ReformSeconds = 15f;

        private static readonly SquadHsmShell s_shell = new(DangerAreaCrossingManeuver.BuildTransitionTable(), DangerAreaCrossingManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => DangerAreaCrossingManeuver.ManeuverKind;
        public ushort StartPhase => DangerAreaCrossingManeuver.PhaseSetSecurity;
        public ushort AbortPhase => DangerAreaCrossingManeuver.PhaseAborted;
        public byte ElementCount => 2;

        public PlayStart Start(ref SquadDangerCrossingParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            if (!NextArea(ctx.World, ctx.Commander, out var area)) return PlayStart.Fail;   // nothing to cross
            var d = area.FarSideHandle - area.NearSideHandle; d.Z = 0f;
            if (d.Length() < 1f) return PlayStart.Fail;                                      // no crossing line
            p.Near = area.NearSideHandle; p.Far = area.FarSideHandle; p.Feature = area.FeatureId;
            p.LaneRotation = default; p.Across = 0; p.Pair = 0; p.PairLane = 0;
            p.SeenPhase = ushort.MaxValue;
            DangerAreaCrossingManeuver.ComputePartitionInputs(ctx.Roster.Count, partition);   // front half crosses, back half secures
            return PlayStart.Go;
        }

        public void Update(ref SquadDangerCrossingParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            ushort phase = ctx.Squad.PhaseId;
            if (phase != p.SeenPhase) { p.SeenPhase = phase; p.PhaseSince = ctx.Now; p.Pair = 0; }
            byte crossing = phase switch
            {
                DangerAreaCrossingManeuver.PhaseCrossElement     => DangerAreaCrossingManeuver.ElementCrossing,
                DangerAreaCrossingManeuver.PhaseCollapseSecurity => DangerAreaCrossingManeuver.ElementSecurity,
                _ => (byte)255,
            };
            if (crossing == 255) return;

            // who is across now (the line, not a radius: a wide road is crossed straight)
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
                if (ctx.Live(i, out var m) && PastFar(in p, ctx.At(m))) p.Across |= (ushort)(1 << i);
            if (p.Pair != 0 && (p.Pair & ~p.Across & LiveMask(in ctx)) == 0) p.Pair = 0;   // the pair is over: the next one goes
            if (p.Pair != 0) return;

            ushort waiting = (ushort)(SquadGates.Element(in ctx, crossing) & ~p.Across & LiveMask(in ctx));
            if (waiting == 0) return;
            for (int i = 0, n = 0; i < 16 && n < 2; i++)
                if ((waiting & (1 << i)) != 0) { p.Pair |= (ushort)(1 << i); n++; }
            int lane = SlotRotation.AcquireSlot(ref p.LaneRotation, Math.Max(1, (int)p.Lanes));
            if (lane < 0) { p.LaneRotation = default; lane = SlotRotation.AcquireSlot(ref p.LaneRotation, Math.Max(1, (int)p.Lanes)); }
            p.PairLane = (byte)lane;
            ws.Reissue = 1;   // the pair goes
        }

        public NodeStatus Outcome(ref SquadDangerCrossingParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            if (ctx.Squad.PhaseId != DangerAreaCrossingManeuver.PhaseReform || ws.IssuedPhase != DangerAreaCrossingManeuver.PhaseReform)
                return NodeStatus.Running;
            bool set = SquadGates.Set(in ws, in ctx, LiveMask(in ctx), SetMetres);
            return set || ctx.Now - p.PhaseSince >= ReformSeconds ? NodeStatus.Success : NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadDangerCrossingParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            ushort phase = ctx.Squad.PhaseId;
            ushort crossers = SquadGates.Element(in ctx, DangerAreaCrossingManeuver.ElementCrossing);
            ushort security = SquadGates.Element(in ctx, DangerAreaCrossingManeuver.ElementSecurity);
            ushort live = LiveMask(in ctx);
            bool done;
            switch (phase)
            {
                case DangerAreaCrossingManeuver.PhaseSetSecurity:
                    evt = PhaseEventKind.DefiladeReached;
                    done = SquadGates.Set(in ws, in ctx, security, SetMetres);
                    break;
                case DangerAreaCrossingManeuver.PhaseCrossElement:
                    evt = PhaseEventKind.FarSideReached;
                    done = (crossers & live & ~p.Across) == 0;
                    break;
                case DangerAreaCrossingManeuver.PhaseFarSideCover:
                    evt = PhaseEventKind.DefiladeReached;   // ⭐ D2 — the far side is set (was ShotFired)
                    done = SquadGates.Set(in ws, in ctx, crossers, SetMetres);
                    break;
                case DangerAreaCrossingManeuver.PhaseCollapseSecurity:
                    evt = PhaseEventKind.FarSideReached;
                    done = (security & live & ~p.Across) == 0;
                    break;
                default:
                    evt = default;
                    return false;   // Reform ends the play (Outcome)
            }
            if (done) return true;
            if (ctx.Now - p.PhaseSince < p.PhaseTimeoutSeconds) return false;
            ws.CompletedByTimeout = 1;
            return true;
        }

        public void BeginIssue(ref SquadDangerCrossingParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadDangerCrossingParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ushort phase = ctx.Squad.PhaseId;
            ushort bit = (ushort)(1 << member);
            byte role = element == DangerAreaCrossingManeuver.ElementSecurity
                ? DangerAreaCrossingManeuver.RoleSecurity : DangerAreaCrossingManeuver.RoleCrossing;
            if (phase == DangerAreaCrossingManeuver.PhaseReform)
                return Watch(Reform(in p, in ctx, member), p.Speed, role);
            if ((p.Pair & bit) != 0)
                return new MemberOrder
                {
                    Kind = MemberOrderKind.AdvanceTo, Point = CrossTo(in p, p.PairLane), Speed = p.CrossSpeed,
                    ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres, RoleId = role,
                };
            int rank = Rank(in ctx, member, element);
            if ((p.Across & bit) != 0 || (element == DangerAreaCrossingManeuver.ElementCrossing && phase >= DangerAreaCrossingManeuver.PhaseFarSideCover))
                return Watch(FarSpot(in p, rank), p.Speed, role);
            return element == DangerAreaCrossingManeuver.ElementSecurity
                ? Watch(NearSpot(in p, rank), p.Speed, role)
                : Watch(WaitSpot(in p, rank), p.Speed, role);
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }

        // ── the area and its geometry ──

        /// <summary>The commander's danger-area sensor's next area (the one the route reaches first), whoever owns the sensor.</summary>
        public static bool NextArea(EntityRepository world, Entity commander, out DangerAreaDescriptor area)
        {
            area = default;
            if (!UnitSensors.TryGetResults<DangerAreaCognitiveBuffer>(world, commander, SensorModality.DangerArea, out var buffer)) return false;
            if (!buffer.IsReady || buffer.Count <= 0) return false;
            area = buffer.GetSpanRO()[0];
            return true;
        }

        private static Vector3 Dir(in SquadDangerCrossingParams p)
        {
            var d = p.Far - p.Near; d.Z = 0f;
            return d.LengthSquared() > 1e-4f ? Vector3.Normalize(d) : Vector3.UnitY;
        }

        private static Vector3 Side(in SquadDangerCrossingParams p) { var d = Dir(in p); return new Vector3(-d.Y, d.X, 0f); }

        /// <summary>Past the far handle along the crossing line.</summary>
        public static bool PastFar(in SquadDangerCrossingParams p, Vector3 at) => Vector3.Dot(at - p.Far, Dir(in p)) >= 0f;

        private static float Beside(int rank) => (rank % 2 == 0 ? 1f : -1f) * (SpotOffsetMetres + SpotStepMetres * (rank / 2));

        public static Vector3 NearSpot(in SquadDangerCrossingParams p, int rank) => p.Near - Dir(in p) * p.BackMetres + Side(in p) * Beside(rank);
        public static Vector3 FarSpot(in SquadDangerCrossingParams p, int rank) => p.Far + Dir(in p) * p.BeyondMetres + Side(in p) * Beside(rank);
        public static Vector3 WaitSpot(in SquadDangerCrossingParams p, int rank) => p.Near - Dir(in p) * (p.BackMetres + 2f + 2f * rank);

        public static Vector3 CrossTo(in SquadDangerCrossingParams p, int lane)
        {
            float offset = (lane - (Math.Max(1, (int)p.Lanes) - 1) * 0.5f) * p.LaneSpacing;
            return p.Far + Dir(in p) * p.BeyondMetres + Side(in p) * offset;
        }

        private static Vector3 Reform(in SquadDangerCrossingParams p, in SquadPlayContext ctx, int member)
            => p.Far + Dir(in p) * (p.BeyondMetres + ReformMetres) + Side(in p) * ((member - (ctx.Roster.Count - 1) * 0.5f) * ReformSpacing);

        private static MemberOrder Watch(Vector3 point, float speed, byte role)
            => new() { Kind = MemberOrderKind.Watch, Point = point, Speed = speed, RoleId = role };

        private static int Rank(in SquadPlayContext ctx, int member, byte element)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            int rank = 0;
            for (int i = 0; i < member; i++) if (squad.Elements.MemberElements[i] == element) rank++;
            return rank;
        }

        private static ushort LiveMask(in SquadPlayContext ctx)
        {
            ushort mask = 0;
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++) if (ctx.Live(i, out _)) mask |= (ushort)(1 << i);
            return mask;
        }
    }

    /// <summary>⭐ <c>CE-2159</c> D3 (§7.7.7) — the crossing order: the crossing drill's params, and the two other ways across.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadDangerCrossingOrder
    {
        public SquadDangerCrossingParams Cross;
        /// <summary>How to cross (<see cref="SquadCrossingDecision"/>).</summary>
        public ChooseOptionParams Choose;
        /// <summary>Open ground: bounding overwatch to beyond the far side (its objective is set when chosen).</summary>
        public BoundingOverwatchParams Bound;
        /// <summary>Too dangerous now: hold for a while, then decide again.</summary>
        public SquadHoldParams Hold;
    }

    /// <summary>⭐ <c>CE-2159</c> D3 — where the crossing is: the choice and the chosen play's state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadDangerCrossingState
    {
        public UtilityChoice Choice;
        public SquadPlayState Play;
    }

    /// <summary>
    /// ⭐ <c>CE-2159</c> X-b — the squad danger crossing on the commander (resolved from <see cref="SquadDangerCrossingParamsJsonDto"/>),
    /// started by the squad SOP's "danger area ahead" row as a reaction (<c>Alert</c>) — the march it pauses resumes when it ends.
    /// ⭐ D3 (§7.7.7): the leader first decides HOW (<see cref="SquadCrossingDecision"/>) — the crossing drill, bounding overwatch
    /// across open ground, or a hold and a fresh decision.
    /// </summary>
    public static class SquadDangerCrossingBehavior
    {
        /// <summary>⭐ D3 — a Hold lasts this long before the leader decides again (s).</summary>
        public const float HoldSeconds = 10f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SquadDangerCrossingBlackboard
        {
            public SquadDangerCrossingOrder Order;
            public SquadDangerCrossingState State;
        }

        [BehaviorResolver(BehaviorNames.SquadDangerCrossing)]
        public static void Resolve(in SquadDangerCrossingParamsJsonDto authored, ref SquadDangerCrossingBlackboard block, EntityRepository world, Entity self)
        {
            var cross = new SquadDangerCrossingParams
            {
                Lanes               = authored.Lanes is > 0 and <= 16 ? (byte)authored.Lanes : (byte)4,
                LaneSpacing         = authored.LaneSpacing > 0f ? authored.LaneSpacing : 4f,
                BackMetres          = authored.BackMetres > 0f ? authored.BackMetres : 3f,
                BeyondMetres        = authored.BeyondMetres > 0f ? authored.BeyondMetres : 4f,
                Speed               = authored.Speed > 0f ? authored.Speed : 2f,
                CrossSpeed          = authored.CrossSpeed > 0f ? authored.CrossSpeed : 4f,
                PhaseTimeoutSeconds = authored.PhaseTimeoutSeconds > 0f ? authored.PhaseTimeoutSeconds : 40f,
            };
            block.Order = new SquadDangerCrossingOrder
            {
                Cross  = cross,
                Choose = new ChooseOptionParams { Decision = UtilityDecisionRef.FromAssetId(SquadCrossingDecision.DecisionAssetId) },
                Bound  = new BoundingOverwatchParams
                {
                    BoundMetres = 20f, Speed = cross.CrossSpeed, ArrivalRadius = 6f, BoundTimeoutSeconds = cross.PhaseTimeoutSeconds,
                },
                Hold   = new SquadHoldParams { Speed = cross.Speed, DurationSeconds = HoldSeconds },
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SquadDangerCrossing, Params = typeof(SquadDangerCrossingParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SquadDangerCrossing, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadDangerCrossingBlackboard))]
        public static BTreeBuilder<SquadDangerCrossingBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadDangerCrossingBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadDangerCrossingOrder p, ref SquadDangerCrossingState ws, Entity self, EntityRepository world)
        {
            if (ws.Choice.Winner == 0)
            {
                if (!DangerCrossingPlay.NextArea(world, self, out var area)) return NodeStatus.Failure;   // nothing to cross
                // ⭐ D3 — the area the decision's inputs read (the existing primitive; nothing called it before)
                if (world.HasComponent<SquadCognitiveState>(self))
                    TacticalFeatureHandles.Acquire(ref world.GetComponentRW<SquadCognitiveState>(self), area.FeatureId);
                UtilityNodes.ChooseOptionOnce(ref p.Choose, ref ws.Choice, self, world);
                if (ws.Choice.Winner == 0) ws.Choice.Winner = (byte)SquadCrossingChoice.Cross;   // nothing scored: the careful drill
                if ((SquadCrossingChoice)ws.Choice.Winner == SquadCrossingChoice.Bound)
                {
                    var d = area.FarSideHandle - area.NearSideHandle; d.Z = 0f;
                    d = d.LengthSquared() > 1e-4f ? Vector3.Normalize(d) : Vector3.UnitY;
                    p.Bound.Objective = area.FarSideHandle + d * p.Cross.BeyondMetres;
                    p.Bound.Cover = default;
                }
                ws.Play = default;
            }
            var choice = (SquadCrossingChoice)ws.Choice.Winner;
            var status = choice switch
            {
                SquadCrossingChoice.Bound => SquadManeuverRunner.Tick<BoundingOverwatchPlay, BoundingOverwatchParams>(ref p.Bound, ref ws.Play, self, world),
                SquadCrossingChoice.Hold  => SquadManeuverRunner.Tick<SquadHoldPlay, SquadHoldParams>(ref p.Hold, ref ws.Play, self, world),
                _                         => SquadManeuverRunner.Tick<DangerCrossingPlay, SquadDangerCrossingParams>(ref p.Cross, ref ws.Play, self, world),
            };
            if (choice == SquadCrossingChoice.Hold && status == NodeStatus.Success)
            {
                SquadManeuverRunner.DropSensor(ref ws.Play, world);   // held long enough: decide again
                ws = default;
                return NodeStatus.Running;
            }
            return status;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.SquadDangerCrossingBehavior.Run")]
        public static void Deactivate_Run(ref SquadDangerCrossingOrder p, ref SquadDangerCrossingState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws.Play, world);
    }
}

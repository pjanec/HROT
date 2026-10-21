using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Perception.Survey;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2259</c> BC-3 (CB-57…CB-62) — the building drill's memory: the order, the survey's clock, the building, each room's state
    /// (by its building-local index), the room in hand and its drill. 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClearBuildingParams
    {
        public const int MaxRooms = 32;

        public Vector3 Point;
        public float Speed;
        /// <summary>1 once the survey was asked · when · 1 once the building is known · 1 once it failed · 1 once the building is clear.</summary>
        public byte Asked, Planned, Failed, Done;
        public double AskedAt;
        public int Building;
        /// <summary>Per room index: <see cref="RoomOrder.Unknown"/>, <c>Cleared</c> or <c>Blocked</c>.</summary>
        public fixed byte Rooms[MaxRooms];
        /// <summary>The room in hand (its index) · how many rooms have been finished (cleared or blocked — the teams' turn, CB-61) ·
        /// how many cleared.</summary>
        public int Current, Turn, ClearedCount;
        public RoomDrillState Drill;
    }

    /// <summary>
    /// ⭐ <c>CE-2259</c> BC-3 (CB-59) — pure: the next room to clear — among the building's indoor rooms not yet cleared or blocked, those
    /// reached by a door, gap or stair from a cleared room (or, before any room is cleared, a door or gap from outside); the lowest storey
    /// first, then the one whose way in is nearest the squad.
    /// </summary>
    public static class RoomOrder
    {
        public const byte Unknown = 0, Cleared = 1, Blocked = 2;

        /// <summary>The next room and the portal it is entered by; <c>false</c> when none is left to reach.</summary>
        public static bool Next(WorldSurveyPayload answer, int building, ReadOnlySpan<byte> states, Vector3 squad, out SurveyRoom room, out SurveyPortal way)
        {
            room = null!;
            way = null!;
            bool anyCleared = false;
            foreach (var b in states) if (b == Cleared) { anyCleared = true; break; }
            int bestLevel = int.MaxValue;
            float bestDistance = float.MaxValue;
            foreach (var p in answer.Portals)
            {
                if (p.Building != building) continue;
                if (p.Kind != (byte)BuildingPortalKind.Door && p.Kind != (byte)BuildingPortalKind.Gap && p.Kind != (byte)BuildingPortalKind.Stair) continue;
                for (int side = 0; side < 2; side++)
                {
                    int target = side == 0 ? p.RoomA : p.RoomB, from = side == 0 ? p.RoomB : p.RoomA;
                    if (target < 0 || target >= states.Length || states[target] != Unknown) continue;
                    bool reached = from < 0 ? !anyCleared && p.Kind != (byte)BuildingPortalKind.Stair : from < states.Length && states[from] == Cleared;
                    if (!reached) continue;
                    var r = RoomOf(answer, building, target);
                    if (r == null || !r.Indoors || r.Corners.Length < 8) continue;
                    float d = Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(squad.X, squad.Y));
                    if (r.Level < bestLevel || (r.Level == bestLevel && d < bestDistance))
                    {
                        bestLevel = r.Level; bestDistance = d; room = r; way = p;
                    }
                }
            }
            return room != null;
        }

        public static SurveyRoom? RoomOf(WorldSurveyPayload answer, int building, int index)
        {
            foreach (var r in answer.Rooms) if (r.Building == building && r.Index == index) return r;
            return null;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2259</c> BC-3 — <c>ClearBuilding</c>: a squad clears a building room by room (Battle Drill 6, CB-10): one survey of the
    /// building, then <see cref="RoomDrill"/> on each room <see cref="RoomOrder"/> picks, the fire teams taking turns; a door that will
    /// not open marks its room Blocked and the squad goes on; PROGRESS RoomClear / RoomBlocked per room, Foothold with the first,
    /// BuildingClear when no room is left. 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.2.
    /// </summary>
    public static class ClearBuildingBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ClearBuildingBlackboard
        {
            public ClearBuildingParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.ClearBuilding)]
        public static void Resolve(in ClearBuildingParamsJsonDto authored, ref ClearBuildingBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new ClearBuildingParams { Point = authored.Building, Speed = authored.Speed > 0f ? authored.Speed : 2f, Current = -1 };
            block.State = default;
        }

        [Behavior(BehaviorNames.ClearBuilding, Params = typeof(global::Hrot.Map.Definitions.Behavior.ClearBuildingParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.ClearBuilding, Curated = true, Group = "Library/Squad", ParamsType = typeof(ClearBuildingBlackboard))]
        public static BTreeBuilder<ClearBuildingBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ClearBuildingBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref ClearBuildingParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<ClearBuildingPlay, ClearBuildingParams>(ref p, ref ws, self, world);

        /// <summary>Leaving the node: the survey goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.ClearBuildingBehavior.Run")]
        public static void Deactivate_Run(ref ClearBuildingParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => ClearBuildingPlay.DropSurvey(world, self);
    }

    /// <summary>⭐ <c>CE-2259</c> BC-3 — the building drill as a play over <see cref="StackAndRoomEntryManeuver"/>: the room drill's phases,
    /// with Cleared (and a blocked Breaching) going back to Stacking for the next room.</summary>
    public struct ClearBuildingPlay : ISquadPlay<ClearBuildingParams>
    {
        /// <summary>The building survey's site · the survey's radius round the order's point (m) · given up after this long (s).</summary>
        public const int BuildingSite = 0x22590001;
        public const float BuildingAskMetres = 3f, AskTimeoutSeconds = 5f;

        private static readonly SquadHsmShell s_shell = new(StackAndRoomEntryManeuver.BuildTransitionTable(), StackAndRoomEntryManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => StackAndRoomEntryManeuver.ManeuverKind;
        public ushort StartPhase => StackAndRoomEntryManeuver.PhaseStacking;
        public ushort AbortPhase => StackAndRoomEntryManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Planned == 0 && p.Failed == 0) { Survey(ref p, ref ws, in ctx); return; }
            if (p.Planned == 0 || p.Done != 0) return;
            byte before = p.Drill.Reported;
            RoomDrill.Update(ref p.Drill, ref ws, in ctx);
            if (before == 0 && p.Drill.Reported != 0 && p.ClearedCount == 0)
                RoomDrill.Report(in ctx, p.Drill.RoomAt, ProgressKind.Foothold);   // CB-62 — the first room is the foothold
        }

        public NodeStatus Outcome(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => p.Failed != 0 || !SquadManeuverRunner.AnyLive(in ctx) ? NodeStatus.Failure
             : p.Done != 0 ? NodeStatus.Success
             : NodeStatus.Running;

        public unsafe bool PhaseComplete(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (p.Planned == 0 || p.Done != 0 || ws.Reissue != 0) return false;
            if (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseCleared)
            {
                if (p.Drill.Outcome != RoomDrill.Cleared) return false;   // the RoomClear goes first (Update)
                Finish(ref p, RoomOrder.Cleared);
                return NextRoom(ref p, ref ws, in ctx);                   // Cleared → Stacking on the next
            }
            if (!RoomDrill.PhaseComplete(ref p.Drill, ref ws, in ctx, out evt)) return false;
            if (p.Drill.Outcome != RoomDrill.Blocked) return true;
            Finish(ref p, RoomOrder.Blocked);                             // CB-58 — skipped, not the end
            evt = PhaseEventKind.FarSideReached;                          // Breaching → Stacking on the next
            if (NextRoom(ref p, ref ws, in ctx)) return true;
            if (p.ClearedCount == 0) { p.Failed = 1; return false; }      // nothing could be entered at all
            return false;
        }

        public void BeginIssue(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
            => p.Planned == 0 || p.Current < 0
                ? new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = StackAndRoomEntryManeuver.RoleUnassigned }
                : RoomDrill.OrderFor(ref p.Drill, in ctx, member, p.Speed);

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>The play's survey child goes (the node is left).</summary>
        public static void DropSurvey(EntityRepository world, Entity self)
        {
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, BuildingSite);
            if (!child.IsNull) WorldSurveyChildSensor.Release(world, child);
        }

        // ── the building ─────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CB-57 — one survey round the order's point; the building nearest it; the first room. The survey is KEPT: the rooms are
        /// read again for each next room.</summary>
        private static void Survey(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Asked == 0)
            {
                WorldSurveyChildSensor.Ensure(ctx.World, ctx.Commander, BuildingSite, WorldSurveyRequest.OfArea(new Vector2(p.Point.X, p.Point.Y), BuildingAskMetres));
                p.Asked = 1;
                p.AskedAt = ctx.Now;
                return;
            }
            var answer = Answer(in ctx);
            if (answer == null)
            {
                if (ctx.Now - p.AskedAt >= AskTimeoutSeconds) Fail(ref p, in ctx);
                return;
            }
            float best = float.MaxValue;
            p.Building = -1;
            foreach (var b in answer.Buildings)
            {
                if (b.Solid) continue;
                float d = Vector2.Distance(new Vector2(b.CentreX, b.CentreY), new Vector2(p.Point.X, p.Point.Y));
                if (d < best) { best = d; p.Building = b.Building; }
            }
            if (p.Building < 0) { Fail(ref p, in ctx); return; }
            p.Planned = 1;
            if (!Begin(ref p, in ctx, answer)) { p.Planned = 0; Fail(ref p, in ctx); return; }
            ws.Reissue = 1;
        }

        /// <summary>The room in hand is finished: its state.</summary>
        private static unsafe void Finish(ref ClearBuildingParams p, byte state)
        {
            if (p.Current >= 0 && p.Current < ClearBuildingParams.MaxRooms) p.Rooms[p.Current] = state;
            if (state == RoomOrder.Cleared) p.ClearedCount++;
            p.Turn++;
            p.Current = -1;
        }

        /// <summary>The next room's drill; none left ⇒ PROGRESS BuildingClear and the play is done (CB-62).</summary>
        private static bool NextRoom(ref ClearBuildingParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var answer = Answer(in ctx);
            if (answer != null && Begin(ref p, in ctx, answer)) return true;
            if (p.ClearedCount > 0)
            {
                RoomDrill.Report(in ctx, p.Point, ProgressKind.BuildingClear);
                p.Done = 1;
            }
            return false;
        }

        private static unsafe bool Begin(ref ClearBuildingParams p, in SquadPlayContext ctx, WorldSurveyPayload answer)
        {
            fixed (byte* rooms = p.Rooms)
            {
                if (!RoomOrder.Next(answer, p.Building, new ReadOnlySpan<byte>(rooms, ClearBuildingParams.MaxRooms), RoomDrill.SquadCentre(in ctx),
                                    out var room, out var way)) return false;
                p.Current = room.Index;
                RoomDrill.Begin(ref p.Drill, in ctx, room, way, p.Turn, RoomDrill.StackRoom(answer, room, way));
                return true;
            }
        }

        /// <summary>The kept survey's answer (it may have aged: the rooms do not move; the doors are read live).</summary>
        private static WorldSurveyPayload? Answer(in SquadPlayContext ctx)
        {
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(ctx.World, ctx.Commander, BuildingSite);
            var read = WorldSurveyChildSensor.Read(ctx.World, child, ctx.Now);
            return read.Ready ? read.Payload : null;
        }

        private static void Fail(ref ClearBuildingParams p, in SquadPlayContext ctx)
        {
            DropSurvey(ctx.World, ctx.Commander);
            p.Failed = 1;
            RoomDrill.Report(in ctx, p.Point, ProgressKind.RoomBlocked);
        }
    }
}

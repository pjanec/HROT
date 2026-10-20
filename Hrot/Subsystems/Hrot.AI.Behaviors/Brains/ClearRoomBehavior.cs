using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception;
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
    /// ⭐ <c>CE-2257</c> BC-1 (CB-48…CB-56) — the room order's memory: the order (a point in the room), the survey's clock, and the room's
    /// drill (<see cref="RoomDrillState"/>, ⭐ <c>CE-2259</c>: shared with the building play). 📄
    /// docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ClearRoomParams
    {
        public Vector3 Room;
        public float Speed;
        /// <summary>1 once the survey was asked · when · 1 once the plan is made · 1 once it failed (no room, no door, no answer).</summary>
        public byte Asked, Planned, Failed;
        public double AskedAt;
        public RoomDrillState Drill;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2257</c> BC-1 — <c>ClearRoom</c>: a squad clears ONE room (Battle Drill 6): it finds the room and its door from one building
    /// survey, stacks beside the door, #1 opens or breaches it, the entry men go to their points of domination one after another with
    /// their sectors, and once each is at his point with no hostile left in the room it reports PROGRESS RoomClear and holds. 📄
    /// docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1.
    /// </summary>
    public static class ClearRoomBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ClearRoomBlackboard
        {
            public ClearRoomParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.ClearRoom)]
        public static void Resolve(in ClearRoomParamsJsonDto authored, ref ClearRoomBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new ClearRoomParams { Room = authored.Room, Speed = authored.Speed > 0f ? authored.Speed : 2f };
            block.State = default;
        }

        [Behavior(BehaviorNames.ClearRoom, Params = typeof(global::Hrot.Map.Definitions.Behavior.ClearRoomParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.ClearRoom, Curated = true, Group = "Library/Squad", ParamsType = typeof(ClearRoomBlackboard))]
        public static BTreeBuilder<ClearRoomBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ClearRoomBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref ClearRoomParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<ClearRoomPlay, ClearRoomParams>(ref p, ref ws, self, world);

        /// <summary>Leaving the node: the survey goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.ClearRoomBehavior.Run")]
        public static void Deactivate_Run(ref ClearRoomParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => ClearRoomPlay.DropSurvey(world, self);
    }

    /// <summary>⭐ <c>CE-2257</c> BC-1 — the room drill as a play over <see cref="StackAndRoomEntryManeuver"/>: Stacking, Breaching, Entering,
    /// Cleared — the drill itself is <see cref="RoomDrill"/> (⭐ <c>CE-2259</c>); this play finds the room and fails on a door that will
    /// not open.</summary>
    public struct ClearRoomPlay : ISquadPlay<ClearRoomParams>
    {
        /// <summary>The room survey's site · the survey's radius round the room point (m).</summary>
        public const int RoomSite = 0x22570001;
        public const float RoomAskMetres = 3f;
        /// <summary>CB-49 — the survey given up after this long (s).</summary>
        public const float AskTimeoutSeconds = 5f;

        private static readonly SquadHsmShell s_shell = new(StackAndRoomEntryManeuver.BuildTransitionTable(), StackAndRoomEntryManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => StackAndRoomEntryManeuver.ManeuverKind;
        public ushort StartPhase => StackAndRoomEntryManeuver.PhaseStacking;
        public ushort AbortPhase => StackAndRoomEntryManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Planned == 0 && p.Failed == 0) { Survey(ref p, ref ws, in ctx); return; }
            if (p.Planned != 0) RoomDrill.Update(ref p.Drill, ref ws, in ctx);
        }

        public NodeStatus Outcome(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => p.Failed != 0 || !SquadManeuverRunner.AnyLive(in ctx) ? NodeStatus.Failure : NodeStatus.Running;   // cleared ⇒ they hold (CB-55)

        public bool PhaseComplete(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (p.Planned == 0 || ws.Reissue != 0) return false;
            bool done = RoomDrill.PhaseComplete(ref p.Drill, ref ws, in ctx, out evt);
            if (p.Drill.Outcome == RoomDrill.Blocked) p.Failed = 1;   // CB-53 — one room, and it cannot be entered
            return done;
        }

        public void BeginIssue(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
            => p.Planned == 0
                ? new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = StackAndRoomEntryManeuver.RoleUnassigned }
                : RoomDrill.OrderFor(ref p.Drill, in ctx, member, p.Speed);

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>The play's survey child goes (the node is left).</summary>
        public static void DropSurvey(EntityRepository world, Entity self)
        {
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, RoomSite);
            if (!child.IsNull) WorldSurveyChildSensor.Release(world, child);
        }

        /// <summary>CB-49 — the room holding the order's point, from one survey: its quad, its door (or gap) nearest the squad, and the
        /// drill. No answer in <see cref="AskTimeoutSeconds"/>, no room, no portal ⇒ PROGRESS RoomBlocked and the play fails.</summary>
        private static void Survey(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Asked == 0)
            {
                WorldSurveyChildSensor.Ensure(ctx.World, ctx.Commander, RoomSite, WorldSurveyRequest.OfArea(new Vector2(p.Room.X, p.Room.Y), RoomAskMetres));
                p.Asked = 1;
                p.AskedAt = ctx.Now;
                return;
            }
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(ctx.World, ctx.Commander, RoomSite);
            var read = WorldSurveyChildSensor.Read(ctx.World, child, ctx.Now);
            if (!read.Ready || read.Stale || read.Payload == null)
            {
                if (ctx.Now - p.AskedAt >= AskTimeoutSeconds) Fail(ref p, in ctx, child);
                return;
            }
            var answer = read.Payload;
            SurveyRoom? room = null;
            foreach (var r in answer.Rooms)
                if (r.Corners.Length >= 8 && p.Room.Z >= r.FloorZ - 1f && p.Room.Z <= r.FloorZ + 2.8f
                    && RoomEntryPlan.Inside(r.Corners, new Vector2(p.Room.X, p.Room.Y))) { room = r; break; }
            var centre = RoomDrill.SquadCentre(in ctx);
            SurveyPortal? door = null;
            float best = float.MaxValue;
            if (room != null)
                foreach (var portal in answer.Portals)
                {
                    if (portal.Building != room.Building || (portal.RoomA != room.Index && portal.RoomB != room.Index)) continue;
                    if (portal.Kind != 0 && portal.Kind != 2) continue;   // a door or a gap (BuildingPortalKind)
                    float d = Vector2.Distance(new Vector2(portal.X, portal.Y), new Vector2(centre.X, centre.Y));
                    if (d < best) { best = d; door = portal; }
                }
            if (room == null || door == null) { Fail(ref p, in ctx, child); return; }

            RoomDrill.Begin(ref p.Drill, in ctx, room, door, turn: 0, RoomDrill.StackRoom(answer, room, door));
            p.Drill.RoomAt = p.Room;   // its reports name the point it was ordered to
            p.Planned = 1;
            WorldSurveyChildSensor.Release(ctx.World, child);
            ws.Reissue = 1;
        }

        private static void Fail(ref ClearRoomParams p, in SquadPlayContext ctx, Entity child)
        {
            if (!child.IsNull) WorldSurveyChildSensor.Release(ctx.World, child);
            p.Failed = 1;
            RoomDrill.Report(in ctx, p.Room, ProgressKind.RoomBlocked);
        }
    }
}

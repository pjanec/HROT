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
    /// ⭐ <c>CE-2257</c> BC-1 (CB-48…CB-56) — the room drill's memory: the order (a point in the room), the room and its door as the survey
    /// gave them, the plan (<see cref="RoomEntryPlan"/>), each roster slot's place in the stack, and the drill's clocks.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClearRoomParams
    {
        public const int Capacity = 16;
        public const sbyte NotInIt = -1;

        public Vector3 Room;
        public float Speed;
        /// <summary>1 once the survey was asked · when · 1 once the plan is made · 1 once it failed (no room, no door, no answer).</summary>
        public byte Asked, Planned, Failed;
        public double AskedAt;
        /// <summary>The room's quad (x, y pairs) and floor.</summary>
        public fixed float Corners[8];
        public float FloorZ;
        /// <summary>The door it is entered by (none for a gap) and the plan.</summary>
        public TerrainObjectRef Door;
        public byte HasDoor;
        public RoomEntryPlan.Result Plan;
        /// <summary>Per roster slot: his place in the stack (#1 = 0; the first <c>Plan.Entry</c> go in), or <see cref="NotInIt"/>.</summary>
        public fixed sbyte OrderOf[Capacity];
        /// <summary>When the breach began · when the entry began · how many entry men have been sent in · since when the room looks clear.</summary>
        public double BreachSince, EnterSince, ClearSince;
        public byte Released, Reported;
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
    /// Cleared.</summary>
    public struct ClearRoomPlay : ISquadPlay<ClearRoomParams>
    {
        /// <summary>The room survey's site · the survey's radius round the room point (m).</summary>
        public const int RoomSite = 0x22570001;
        public const float RoomAskMetres = 3f;
        /// <summary>CB-49 — the survey given up after this long (s) · CB-53 the door given up after this long (s).</summary>
        public const float AskTimeoutSeconds = 5f, BreachTimeoutSeconds = 12f;
        /// <summary>Stacked: every man this close to his stack point (m); the stack given up waiting after this long (s).</summary>
        public const float StackedMetres = 1f, StackTimeoutSeconds = 60f;
        /// <summary>CB-54 — the next entry man goes this long after the one before (s).</summary>
        public const float FlowSeconds = 0.6f;
        /// <summary>CB-55 — at his point: this close (m); the room clear after this long with every man there and no hostile in it (s).</summary>
        public const float AtPointMetres = 1.2f, ClearSeconds = 3f;
        /// <summary>How far round his point a man fights from it (his post, m) · the stack's arc toward the door (half width, °).</summary>
        public const float PointRadius = 1.2f, StackArcHalfWidth = 60f;

        private static readonly SquadHsmShell s_shell = new(StackAndRoomEntryManeuver.BuildTransitionTable(), StackAndRoomEntryManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => StackAndRoomEntryManeuver.ManeuverKind;
        public ushort StartPhase => StackAndRoomEntryManeuver.PhaseStacking;
        public ushort AbortPhase => StackAndRoomEntryManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            unsafe { for (int i = 0; i < ClearRoomParams.Capacity; i++) p.OrderOf[i] = ClearRoomParams.NotInIt; }
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Planned == 0 && p.Failed == 0) { Survey(ref p, ref ws, in ctx); return; }
            if (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseEntering) Flow(ref p, ref ws, in ctx);
            if (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseCleared && p.Reported == 0)
            {
                p.Reported = 1;
                Report(in ctx, p.Room, ProgressKind.RoomClear);
            }
        }

        public NodeStatus Outcome(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => p.Failed != 0 || !SquadManeuverRunner.AnyLive(in ctx) ? NodeStatus.Failure : NodeStatus.Running;   // cleared ⇒ they hold (CB-55)

        public bool PhaseComplete(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (p.Planned == 0 || ws.Reissue != 0) return false;
            switch (ctx.Squad.PhaseId)
            {
                case StackAndRoomEntryManeuver.PhaseStacking:
                    if (ctx.Now - ws.IssuedAt >= StackTimeoutSeconds) { ws.CompletedByTimeout = 1; return true; }
                    if (!AllAt(ref p, in ctx, stack: true, StackedMetres)) return false;
                    p.BreachSince = ctx.Now;
                    return true;
                case StackAndRoomEntryManeuver.PhaseBreaching:
                    if (DoorOpen(ref p, ctx.World)) { p.EnterSince = ctx.Now; p.Released = 0; return true; }
                    if (ctx.Now - p.BreachSince < BreachTimeoutSeconds) return false;
                    Report(in ctx, p.Room, ProgressKind.RoomBlocked);   // CB-53 — the door would not open
                    p.Failed = 1;
                    evt = PhaseEventKind.Abort;
                    return true;
                case StackAndRoomEntryManeuver.PhaseEntering:
                    if (!AllAt(ref p, in ctx, stack: false, AtPointMetres) || HostileInRoom(ref p, in ctx)) { p.ClearSince = 0; return false; }
                    if (p.ClearSince == 0) { p.ClearSince = ctx.Now; return false; }
                    if (ctx.Now - p.ClearSince < ClearSeconds) return false;
                    evt = PhaseEventKind.FarSideReached;
                    return true;
            }
            return false;
        }

        public void BeginIssue(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public unsafe MemberOrder OrderFor(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            if (p.Planned == 0 || member >= ClearRoomParams.Capacity || p.OrderOf[member] == ClearRoomParams.NotInIt)
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = StackAndRoomEntryManeuver.RoleUnassigned };
            int k = p.OrderOf[member];
            byte role = k == 0 ? StackAndRoomEntryManeuver.RolePointMan : k < p.Plan.Entry ? StackAndRoomEntryManeuver.RoleSecondary : StackAndRoomEntryManeuver.RoleBreachCover;
            var stack = p.Plan.Stack(k);
            var atStack = new MemberOrder
            {
                Kind = MemberOrderKind.Watch, Point = stack, Speed = p.Speed, RoleId = role,
                Sector = MemberArc.Around(LookAt.BearingTo(stack, new Vector3(p.Plan.Door, p.FloorZ)), StackArcHalfWidth),
                Post = new MemberPost { Set = 1, Radius = PointRadius },
            };
            switch (ctx.Squad.PhaseId)
            {
                case StackAndRoomEntryManeuver.PhaseBreaching:
                    if (k == 0 && p.HasDoor != 0 && !DoorOpen(ref p, ctx.World))
                        return new MemberOrder { Kind = MemberOrderKind.OperateDoor, Door = p.Door, Speed = p.Speed, RoleId = role, Point = stack };
                    return atStack;
                case StackAndRoomEntryManeuver.PhaseEntering:
                case StackAndRoomEntryManeuver.PhaseCleared:
                    if (k >= p.Plan.Entry || (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseEntering && k >= p.Released)) return atStack;
                    var point = p.Plan.Point(k);
                    return new MemberOrder
                    {
                        Kind = MemberOrderKind.Watch, Point = point, Speed = p.Speed * 1.5f, RoleId = role,
                        Sector = MemberArc.Around(p.Plan.Facing(k), RoomEntryPlan.Result.HalfWidth(k)),
                        Post = new MemberPost { Set = 1, Radius = PointRadius },
                    };
                default:
                    return atStack;
            }
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>The play's survey child goes (the node is left).</summary>
        public static void DropSurvey(EntityRepository world, Entity self)
        {
            var child = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(world, self, RoomSite);
            if (!child.IsNull) WorldSurveyChildSensor.Release(world, child);
        }

        // ── the plan ─────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CB-49 — the room holding the order's point, from one survey: its quad, its door nearest the squad, and the plan.
        /// No answer in <see cref="AskTimeoutSeconds"/>, no room, no portal ⇒ PROGRESS RoomBlocked and the play fails.</summary>
        private static unsafe void Survey(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
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
            var centre = SquadCentre(in ctx);
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

            for (int i = 0; i < 8; i++) p.Corners[i] = room.Corners[i];
            p.FloorZ = room.FloorZ;
            p.HasDoor = door.IsDoor && door.DoorKey.Length > 0 ? (byte)1 : (byte)0;
            if (p.HasDoor != 0) p.Door = new TerrainObjectRef(door.DoorKey);
            int inFight = 0;
            for (int i = 0; i < ctx.Roster.Count; i++) if (ctx.Live(i, out _)) inFight++;
            p.Plan = RoomEntryPlan.Plan(room.Corners, room.FloorZ, new Vector2(door.X, door.Y), door.Width, inFight);
            Places(ref p, in ctx, inFight);
            p.Planned = 1;
            WorldSurveyChildSensor.Release(ctx.World, child);
            ws.Reissue = 1;
        }

        /// <summary>The stack order: the live men in roster order, the leader (slot 0) last when there are more men than go in — he stays
        /// outside and directs.</summary>
        private static unsafe void Places(ref ClearRoomParams p, in SquadPlayContext ctx, int inFight)
        {
            int k = 0;
            bool leaderOut = inFight > p.Plan.Entry;
            for (int i = 0; i < ctx.Roster.Count && i < ClearRoomParams.Capacity; i++)
            {
                if (leaderOut && i == 0) continue;
                if (ctx.Live(i, out _)) p.OrderOf[i] = (sbyte)k++;
            }
            if (leaderOut && ctx.Live(0, out _)) p.OrderOf[0] = (sbyte)k;
        }

        private static void Fail(ref ClearRoomParams p, in SquadPlayContext ctx, Entity child)
        {
            if (!child.IsNull) WorldSurveyChildSensor.Release(ctx.World, child);
            p.Failed = 1;
            Report(in ctx, p.Room, ProgressKind.RoomBlocked);
        }

        // ── the drill ────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>CB-54 — the next entry man goes <see cref="FlowSeconds"/> after the one before.</summary>
        private static void Flow(ref ClearRoomParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            int due = Math.Min(p.Plan.Entry, 1 + (int)((ctx.Now - p.EnterSince) / FlowSeconds));
            if (due <= p.Released) return;
            p.Released = (byte)due;
            ws.Reissue = 1;
        }

        /// <summary>Every live man of the stack (or of the entry) within <paramref name="metres"/> of his point.</summary>
        private static unsafe bool AllAt(ref ClearRoomParams p, in SquadPlayContext ctx, bool stack, float metres)
        {
            bool any = false;
            for (int i = 0; i < ctx.Roster.Count && i < ClearRoomParams.Capacity; i++)
            {
                int k = p.OrderOf[i];
                if (k == ClearRoomParams.NotInIt || !ctx.Live(i, out var m)) continue;
                if (!stack && k >= p.Plan.Entry) continue;
                any = true;
                var target = stack ? p.Plan.Stack(k) : p.Plan.Point(k);
                if (Vector3.Distance(ctx.At(m), target) > metres) return false;
            }
            return any;
        }

        /// <summary>CB-53 — the door stands open (or broken), read live from the door entity on this node; a gap is always open.</summary>
        private static bool DoorOpen(ref ClearRoomParams p, EntityRepository world)
        {
            if (p.HasDoor == 0) return true;
            var door = p.Door.Resolve(world);
            if (door.IsNull || !world.HasComponent<DoorState>(door)) return false;
            var s = world.GetComponentRO<DoorState>(door).State;
            return s == TerrainDoorState.Open || s == TerrainDoorState.Destroyed;
        }

        /// <summary>CB-55 — a pooled contact inside the room's quad at its storey, alive and still in the fight.</summary>
        private static unsafe bool HostileInRoom(ref ClearRoomParams p, in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            Span<float> corners = stackalloc float[8];
            for (int i = 0; i < 8; i++) corners[i] = p.Corners[i];
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0) continue;
                if (pool[c].PositionZ < p.FloorZ - 1f || pool[c].PositionZ > p.FloorZ + 2.8f) continue;
                if (!RoomEntryPlan.Inside(corners, new Vector2(pool[c].PositionX, pool[c].PositionY))) continue;
                var e = new Entity((ulong)pool[c].EntityId);
                if (ctx.World.IsAlive(e) && !ThreatDanger.IsOutOfTheFight(ctx.World, e)) return true;
            }
            return false;
        }

        private static Vector3 SquadCentre(in SquadPlayContext ctx)
        {
            var sum = Vector3.Zero;
            int n = 0;
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (ctx.Live(i, out var m)) { sum += ctx.At(m); n++; }
            return n == 0 ? Vector3.Zero : sum / n;
        }

        private static void Report(in SquadPlayContext ctx, Vector3 at, ProgressKind kind)
        {
            float now = (float)ctx.Now;
            ReportQueue.Enqueue(ctx.World, ctx.Commander, new CommandReport
            {
                Kind = ReportKind.Progress, Subtype = (byte)kind, At = at, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
        }
    }
}

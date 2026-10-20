using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
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

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2259</c> BC-3 (CB-58) — ONE room's drill as data: the room and its way in as the survey gave them, the plan
    /// (<see cref="RoomEntryPlan"/>), each roster slot's place in the stack, the clocks, and how it ended. Driven by
    /// <see cref="RoomDrill"/> for <see cref="ClearRoomPlay"/> and <see cref="ClearBuildingPlay"/> alike.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.1, §7.2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RoomDrillState
    {
        public const int Capacity = 16;
        public const sbyte NotInIt = -1;

        /// <summary>The room's quad (x, y pairs) and floor · the point its reports name.</summary>
        public fixed float Corners[8];
        public float FloorZ;
        public Vector3 RoomAt;
        /// <summary>The door it is entered by (none for a gap or a stair) and the plan.</summary>
        public TerrainObjectRef Door;
        public byte HasDoor;
        public RoomEntryPlan.Result Plan;
        /// <summary>Per roster slot: his place in the stack (#1 = 0; the first <c>Plan.Entry</c> go in), or <see cref="NotInIt"/>.</summary>
        public fixed sbyte OrderOf[Capacity];
        /// <summary>When the breach began · when the entry began · since when the room looks clear.</summary>
        public double BreachSince, EnterSince, ClearSince;
        /// <summary>How many entry men have been sent in · 1 once RoomClear went · <see cref="RoomDrill.Running"/>, <c>Cleared</c> or
        /// <c>Blocked</c>.</summary>
        public byte Released, Reported, Outcome;
        /// <summary>⭐ CE-2334 — 1 when the way in is crossed on foot first (a door or a gap; a stair has no threshold) · per roster slot, a
        /// bit once that entry man has crossed it.</summary>
        public byte Threshold;
        public ushort AtDoor, Crossed;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2259</c> BC-3 (CB-58) — the room drill (Battle Drill 6), extracted from BC-1's <see cref="ClearRoomPlay"/> so the building
    /// play drives the SAME drill room after room: stack beside the way in, #1 opens or breaches a door, the entry men flow to their points
    /// of domination 0.6 s apart with their sectors, and with each one there and no hostile in the room for 3 s the room is clear.
    /// A door that will not open ends the room as <see cref="Blocked"/>; what that means is the caller's.
    /// </summary>
    public static class RoomDrill
    {
        public const byte Running = 0, Cleared = 1, Blocked = 2;

        /// <summary>CB-53 — the door given up after this long (s).</summary>
        public const float BreachTimeoutSeconds = 12f;
        /// <summary>Stacked: every ENTRY man this close to his stack point (m) — the posture's own "he is there" (CE-2334); the stack
        /// given up waiting after this long (s).</summary>
        public const float StackedMetres = PostureNodes.OccupiedMetres, StackTimeoutSeconds = 30f;
        /// <summary>CB-54 — the next entry man goes this long after the one before (s).</summary>
        public const float FlowSeconds = 0.6f;
        /// <summary>CB-55 — at his point: this close (m); the room clear after this long with every man there and no hostile in it (s).</summary>
        public const float AtPointMetres = PostureNodes.OccupiedMetres, ClearSeconds = 3f;
        /// <summary>⭐ CE-2334 — the threshold is crossed this far past the way in (m) · reached within this (m) · the door front reached
        /// within this (m).</summary>
        public const float CrossedMetres = 0.5f, ThresholdArrival = 0.4f, DoorFrontMetres = 0.6f;
        /// <summary>How far round his point a man fights from it (his post, m) · the stack's arc toward the door (half width, °).</summary>
        public const float PointRadius = 1.2f, StackArcHalfWidth = 60f;

        /// <summary>
        /// The drill for <paramref name="room"/> entered by <paramref name="way"/> (a door, a gap, or — CB-60 — a stair), by the live men in
        /// roster order with the leader last when more are in the fight than go in; <paramref name="turn"/> turns the rest by the entry's
        /// size so the team that held the stack goes in next (CB-61).
        /// </summary>
        public static unsafe void Begin(ref RoomDrillState d, in SquadPlayContext ctx, SurveyRoom room, SurveyPortal way, int turn)
        {
            d = default;
            for (int i = 0; i < RoomDrillState.Capacity; i++) d.OrderOf[i] = RoomDrillState.NotInIt;
            for (int i = 0; i < 8; i++) d.Corners[i] = room.Corners[i];
            d.FloorZ = room.FloorZ;
            d.RoomAt = new Vector3(room.CentreX, room.CentreY, room.FloorZ);
            d.HasDoor = way.IsDoor && way.DoorKey.Length > 0 ? (byte)1 : (byte)0;
            d.Threshold = way.Kind == (byte)BuildingPortalKind.Stair ? (byte)0 : (byte)1;
            if (d.HasDoor != 0) d.Door = new TerrainObjectRef(way.DoorKey);
            int inFight = 0;
            for (int i = 0; i < ctx.Roster.Count; i++) if (ctx.Live(i, out _)) inFight++;
            d.Plan = way.Kind == (byte)BuildingPortalKind.Stair
                ? RoomEntryPlan.PlanFrom(room.Corners, room.FloorZ, new Vector2(way.X, way.Y), way.SillZ, inFight)
                : RoomEntryPlan.Plan(room.Corners, room.FloorZ, new Vector2(way.X, way.Y), way.Width, inFight);
            Places(ref d, in ctx, inFight, turn);
        }

        /// <summary>Each frame: the flow while entering; RoomClear once the room is cleared (the drill's <see cref="RoomDrillState.Outcome"/>
        /// then reads <see cref="Cleared"/>).</summary>
        public static void Update(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseEntering) { Flow(ref d, ref ws, in ctx); Cross(ref d, ref ws, in ctx); }
            if (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseCleared && d.Reported == 0)
            {
                d.Reported = 1;
                d.Outcome = Cleared;
                Report(in ctx, d.RoomAt, ProgressKind.RoomClear);
            }
        }

        /// <summary>The drill's own phase steps: stacked ⇒ breach; open ⇒ enter; every man at his point, nobody hostile, 3 s ⇒ clear. A door
        /// shut after <see cref="BreachTimeoutSeconds"/> ⇒ PROGRESS RoomBlocked, <see cref="Blocked"/>, and <see cref="PhaseEventKind.Abort"/>
        /// (a caller that goes on maps it to its own event).</summary>
        public static bool PhaseComplete(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            switch (ctx.Squad.PhaseId)
            {
                case StackAndRoomEntryManeuver.PhaseStacking:
                    if (ctx.Now - ws.IssuedAt >= StackTimeoutSeconds) { ws.CompletedByTimeout = 1; d.BreachSince = ctx.Now; return true; }
                    if (!AllAt(ref d, in ctx, stack: true, StackedMetres)) return false;
                    d.BreachSince = ctx.Now;
                    return true;
                case StackAndRoomEntryManeuver.PhaseBreaching:
                    if (DoorOpen(ref d, ctx.World)) { d.EnterSince = ctx.Now; d.Released = 0; return true; }
                    if (ctx.Now - d.BreachSince < BreachTimeoutSeconds) return false;
                    Report(in ctx, d.RoomAt, ProgressKind.RoomBlocked);   // CB-53 — the door would not open
                    d.Outcome = Blocked;
                    evt = PhaseEventKind.Abort;
                    return true;
                case StackAndRoomEntryManeuver.PhaseEntering:
                    if (!AllAt(ref d, in ctx, stack: false, AtPointMetres) || HostileInRoom(ref d, in ctx)) { d.ClearSince = 0; return false; }
                    if (d.ClearSince == 0) { d.ClearSince = ctx.Now; return false; }
                    if (ctx.Now - d.ClearSince < ClearSeconds) return false;
                    evt = PhaseEventKind.FarSideReached;
                    return true;
            }
            return false;
        }

        /// <summary>One man's order in the drill's current phase: the stack (watching the door), #1 to the door while breaching, an entry man
        /// once released to his point with his sector; a man not in it holds.</summary>
        public static unsafe MemberOrder OrderFor(ref RoomDrillState d, in SquadPlayContext ctx, int member, float speed)
        {
            if (member >= RoomDrillState.Capacity || d.OrderOf[member] == RoomDrillState.NotInIt)
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = speed, RoleId = StackAndRoomEntryManeuver.RoleUnassigned };
            int k = d.OrderOf[member];
            byte role = k == 0 ? StackAndRoomEntryManeuver.RolePointMan : k < d.Plan.Entry ? StackAndRoomEntryManeuver.RoleSecondary : StackAndRoomEntryManeuver.RoleBreachCover;
            var stack = d.Plan.Stack(k);
            var atStack = new MemberOrder
            {
                Kind = MemberOrderKind.Watch, Point = stack, Speed = speed, RoleId = role,
                Sector = MemberArc.Around(LookAt.BearingTo(stack, new Vector3(d.Plan.Door, d.Plan.StackZ)), StackArcHalfWidth),
                Post = new MemberPost { Set = 1, Radius = PointRadius },
            };
            switch (ctx.Squad.PhaseId)
            {
                case StackAndRoomEntryManeuver.PhaseBreaching:
                    if (k == 0 && d.HasDoor != 0 && !DoorOpen(ref d, ctx.World))
                        return new MemberOrder { Kind = MemberOrderKind.OperateDoor, Door = d.Door, Speed = speed, RoleId = role, Point = stack };
                    return atStack;
                case StackAndRoomEntryManeuver.PhaseEntering:
                case StackAndRoomEntryManeuver.PhaseCleared:
                    if (k >= d.Plan.Entry || (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseEntering && k >= d.Released)) return atStack;
                    // ⭐ CE-2334 — square to the way in, through it straight, firing on the way; then to his point
                    if (d.Threshold != 0 && (d.Crossed & (1 << member)) == 0)
                        return new MemberOrder
                        {
                            Kind = MemberOrderKind.AdvanceTo, Point = (d.AtDoor & (1 << member)) == 0 ? d.Plan.DoorFront : d.Plan.Threshold,
                            Speed = speed * 1.5f, RoleId = role, ArrivalRadius = ThresholdArrival,
                        };
                    return new MemberOrder
                    {
                        Kind = MemberOrderKind.Watch, Point = d.Plan.Point(k), Speed = speed * 1.5f, RoleId = role,
                        Sector = MemberArc.Around(d.Plan.Facing(k), RoomEntryPlan.Result.HalfWidth(k)),
                        Post = new MemberPost { Set = 1, Radius = PointRadius },
                    };
                default:
                    return atStack;
            }
        }

        /// <summary>A PROGRESS report from the squad commander, sent after the report delay.</summary>
        public static void Report(in SquadPlayContext ctx, Vector3 at, ProgressKind kind)
        {
            float now = (float)ctx.Now;
            ReportQueue.Enqueue(ctx.World, ctx.Commander, new CommandReport
            {
                Kind = ReportKind.Progress, Subtype = (byte)kind, At = at, ObservedAt = now,
            }, now + CommandNodes.ReportDelaySeconds);
        }

        /// <summary>The live members' centre.</summary>
        public static Vector3 SquadCentre(in SquadPlayContext ctx)
        {
            var sum = Vector3.Zero;
            int n = 0;
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (ctx.Live(i, out var m)) { sum += ctx.At(m); n++; }
            return n == 0 ? Vector3.Zero : sum / n;
        }

        /// <summary>The stack order: the live men in roster order, the leader (slot 0) last when there are more men than go in — he stays
        /// outside and directs; the others turned by <paramref name="turn"/> × the entry's size.</summary>
        private static unsafe void Places(ref RoomDrillState d, in SquadPlayContext ctx, int inFight, int turn)
        {
            bool leaderOut = inFight > d.Plan.Entry;
            Span<int> men = stackalloc int[RoomDrillState.Capacity];
            int n = 0;
            for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
            {
                if (leaderOut && i == 0) continue;
                if (ctx.Live(i, out _)) men[n++] = i;
            }
            int shift = n == 0 ? 0 : (turn * Math.Max(1, d.Plan.Entry)) % n;
            for (int k = 0; k < n; k++) d.OrderOf[men[(k + shift) % n]] = (sbyte)k;
            if (leaderOut && ctx.Live(0, out _)) d.OrderOf[0] = (sbyte)n;
        }

        /// <summary>CB-54 — the next entry man goes <see cref="FlowSeconds"/> after the one before, ⭐ CE-2334 and not before every man
        /// sent ahead of him is through the way in (or down): one man in the doorway at a time.</summary>
        private static unsafe void Flow(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            int due = Math.Min(d.Plan.Entry, 1 + (int)((ctx.Now - d.EnterSince) / FlowSeconds));
            if (d.Threshold != 0)
            {
                int through = 0;
                for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
                {
                    int k = d.OrderOf[i];
                    if (k == RoomDrillState.NotInIt || k >= d.Released) continue;
                    if ((d.Crossed & (1 << i)) != 0 || !ctx.Live(i, out _)) through++;
                }
                due = Math.Min(due, Math.Max(1, through + 1));
            }
            if (due <= d.Released) return;
            d.Released = (byte)due;
            ws.Reissue = 1;
        }

        /// <summary>⭐ CE-2334 — a released entry man within <see cref="DoorFrontMetres"/> of the door front is at the door: next, the
        /// threshold; in the room, <see cref="CrossedMetres"/> past the way in, he has crossed it: next, his point.</summary>
        private static unsafe void Cross(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (d.Threshold == 0) return;
            Span<float> corners = stackalloc float[8];
            for (int i = 0; i < 8; i++) corners[i] = d.Corners[i];
            for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
            {
                int k = d.OrderOf[i];
                if (k == RoomDrillState.NotInIt || k >= d.Plan.Entry || k >= d.Released || (d.Crossed & (1 << i)) != 0 || !ctx.Live(i, out var m)) continue;
                var at = ctx.At(m);
                var flat = new Vector2(at.X, at.Y);
                bool crossed = RoomEntryPlan.Inside(corners, flat) && Vector2.Dot(flat - d.Plan.Door, d.Plan.Into) >= CrossedMetres;
                bool atDoor = crossed || Vector2.Distance(flat, new Vector2(d.Plan.DoorFront.X, d.Plan.DoorFront.Y)) <= DoorFrontMetres;
                if (atDoor && (d.AtDoor & (1 << i)) == 0) { d.AtDoor |= (ushort)(1 << i); ws.Reissue = 1; }
                if (crossed) { d.Crossed |= (ushort)(1 << i); ws.Reissue = 1; }
            }
        }

        /// <summary>Every live ENTRY man within <paramref name="metres"/> of his stack point (or of his point) — the rest of the stack (the
        /// leader, the support) need not be in place for the entry team to go (CE-2334).</summary>
        private static unsafe bool AllAt(ref RoomDrillState d, in SquadPlayContext ctx, bool stack, float metres)
        {
            bool any = false;
            for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
            {
                int k = d.OrderOf[i];
                if (k == RoomDrillState.NotInIt || !ctx.Live(i, out var m)) continue;
                if (k >= d.Plan.Entry) continue;
                any = true;
                var target = stack ? d.Plan.Stack(k) : d.Plan.Point(k);
                if (Vector3.Distance(ctx.At(m), target) > metres) return false;
            }
            return any;
        }

        /// <summary>CB-53 — the door stands open (or broken), read live from the door entity on this node; a gap or a stair is always open.</summary>
        public static bool DoorOpen(ref RoomDrillState d, EntityRepository world)
        {
            if (d.HasDoor == 0) return true;
            var door = d.Door.Resolve(world);
            if (door.IsNull || !world.HasComponent<DoorState>(door)) return false;
            var s = world.GetComponentRO<DoorState>(door).State;
            return s == TerrainDoorState.Open || s == TerrainDoorState.Destroyed;
        }

        /// <summary>CB-55 — a pooled contact inside the room's quad at its storey, alive and still in the fight.</summary>
        private static unsafe bool HostileInRoom(ref RoomDrillState d, in SquadPlayContext ctx)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            Span<float> corners = stackalloc float[8];
            for (int i = 0; i < 8; i++) corners[i] = d.Corners[i];
            for (int c = 0; c < squad.Contacts.Count && c < 16; c++)
            {
                if ((pool[c].Flags & SquadContact.AnonymousFlag) != 0) continue;
                if (pool[c].PositionZ < d.FloorZ - 1f || pool[c].PositionZ > d.FloorZ + 2.8f) continue;
                if (!RoomEntryPlan.Inside(corners, new Vector2(pool[c].PositionX, pool[c].PositionY))) continue;
                var e = new Entity((ulong)pool[c].EntityId);
                if (ctx.World.IsAlive(e) && !ThreatDanger.IsOutOfTheFight(ctx.World, e)) return true;
            }
            return false;
        }
    }
}

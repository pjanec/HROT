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
using Fdp.Toolkit.Perception.Events;
using Hrot.Map.Definitions.Behavior;

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

        // ⭐ CE-2260 / CE-2261 (BC-4, BC-5 — §7.3) — the gate before the first man goes through the way in
        /// <summary>The order's <c>ClearIntensity</c> · 1 when the order says the room is held · 1 once a man of the stack was fired on at it.</summary>
        public byte Intensity, Ordered, FiredOn;
        /// <summary>The entry is free to flow (the grenade is done or none, no contact holds it).</summary>
        public byte GateOpen;
        /// <summary>The grenade step (<see cref="RoomDrill.GrenadeUndecided"/> … <see cref="RoomDrill.GrenadeDone"/>) · 1 once one went into this room ·
        /// the thrower's roster slot (−1 none) · his grenade mount · its rounds when the throw was asked.</summary>
        public byte GrenadeStep, Thrown;
        public sbyte Thrower;
        public byte GrenadeMount;
        public int RoundsBefore;
        public Vector3 Aim;
        public float Fuze;
        public double StepSince, BangAt;
        /// <summary>CB-72 — 1 while contact at the way in holds the entry · since when it has been quiet.</summary>
        public byte Contact;
        public double QuietSince, ContactSince;
        /// <summary>CB-72 — the pie slice last issued (0 = the first, widest off the doorway's line).</summary>
        public byte PieStepIssued;
        /// <summary>CB-73 — the turn the stack was placed with · the roster slots that were live when it was.</summary>
        public int Turn;
        public ushort Placed;
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
        /// <param name="stackRoom">⭐ CE-2334 CB-64 — the room on the stack's side of <paramref name="way"/> (null from outside): the stack
        /// stands inside it.</param>
        public static unsafe void Begin(ref RoomDrillState d, in SquadPlayContext ctx, SurveyRoom room, SurveyPortal way, int turn,
                                        SurveyRoom? stackRoom = null)
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
                : RoomEntryPlan.Plan(room.Corners, room.FloorZ, new Vector2(way.X, way.Y), way.Width, inFight,
                    stackRoom != null && stackRoom.Corners.Length >= 8 ? stackRoom.Corners : default);
            d.Thrower = -1;
            d.Turn = turn;
            Places(ref d, in ctx, inFight, turn);
            d.Placed = LiveMask(in ctx);
        }

        /// <summary>⭐ <c>CE-2260</c> (CB-65) — the order's intensity and whether it says the room is held; after <see cref="Begin"/>.</summary>
        public static void Arm(ref RoomDrillState d, ClearIntensity intensity, bool ordered)
        {
            d.Intensity = (byte)intensity;
            d.Ordered = ordered ? (byte)1 : (byte)0;
        }

        /// <summary>Each frame: the flow while entering; RoomClear once the room is cleared (the drill's <see cref="RoomDrillState.Outcome"/>
        /// then reads <see cref="Cleared"/>).</summary>
        public static void Update(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            ushort phase = ctx.Squad.PhaseId;
            if (phase is StackAndRoomEntryManeuver.PhaseStacking or StackAndRoomEntryManeuver.PhaseBreaching or StackAndRoomEntryManeuver.PhaseEntering)
                Restack(ref d, ref ws, in ctx);
            if (phase == StackAndRoomEntryManeuver.PhaseEntering)
            {
                if (Gate(ref d, ref ws, in ctx)) Flow(ref d, ref ws, in ctx);
                Cross(ref d, ref ws, in ctx);
            }
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
                    if (ctx.Squad.PhaseId == StackAndRoomEntryManeuver.PhaseEntering && d.GateOpen == 0)
                    {
                        // ⭐ CE-2261 (CB-72) — contact at the way in: the first two fight from pie points beside the doorway, leaning toward it
                        if (d.Contact != 0 && k < 2 && d.Threshold != 0) return PiePoint(ref d, k, speed, role);
                        // ⭐ CE-2260 (CB-68, as built) — the thrower steps to the JAMB on his side (the opening's edge, 0.4 m out), throws across
                        //   through the far half of the opening, and is back on his stack point for the bang. 📐 live, a throw from the door
                        //   front put him in the doorway the defenders were aimed at and he was shot there; from the stack point the line
                        //   through the opening runs along the wall and no aim in the room is reachable
                        if (member == d.Thrower && d.GrenadeStep == GrenadeToDoor)
                            return new MemberOrder { Kind = MemberOrderKind.AdvanceTo, Point = ThrowFrom(ref d), Speed = speed, RoleId = role, ArrivalRadius = JambArrival };
                        if (member == d.Thrower && d.GrenadeStep == GrenadeThrowing)
                            return new MemberOrder { Kind = MemberOrderKind.FireAtPoint, Point = d.Aim, Mount = d.GrenadeMount, Rounds = 1, RoleId = role, Speed = speed };
                        return atStack;
                    }
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

        // ── ⭐ CE-2260 / CE-2261 (BC-4, BC-5) — the gate before the entry: contact at the way in, the grenade, a man down ──────────

        /// <summary>The grenade steps (CB-68, CB-69).</summary>
        public const byte GrenadeUndecided = 0, GrenadeToDoor = 1, GrenadeThrowing = 2, GrenadeThrown = 3, GrenadeDone = 4;
        /// <summary>CB-69 — the thrower given up reaching his throwing point (s) · the throw given up (s) · the entry released this long after the bang (s) ·
        /// a hand grenade's speed for its flight time (m/s) · how far into the room it is thrown (m).</summary>
        /// <summary>CB-68 — the jamb point lies this far out from the wall (m) · reached within this (m).</summary>
        public const float JambBackMetres = 0.4f, JambArrival = 0.3f;
        /// <summary>CB-68 — the jamb point lies this far inside the opening's edge (a shoulder exposed, m) · a throw passes the opening this far
        /// clear of its edges (m).</summary>
        public const float JambInsetMetres = 0.15f, JambClearMetres = 0.1f;
        public const float ToDoorTimeoutSeconds = 8f, ThrowTimeoutSeconds = 6f, AfterBangSeconds = 0.3f, ThrowSpeed = 15f, ThrowInMetres = 3f;
        /// <summary>CB-72 — fired on within this long holds the entry (s) · quiet this long releases it (s) · the pie points' distance (m) and
        /// their angles off the doorway's line (°, #1 then #2).</summary>
        public const float FiredOnSeconds = 3f, QuietSeconds = 3f, PieMetres = 1.8f, PieAngle0 = 60f, PieAngle1 = 30f;
        /// <summary>CB-72 — a pie slice every this long (s), this much nearer the doorway's line each (°), never nearer than these (°, #1, #2).</summary>
        public const float PieStepSeconds = 2f, PieStepDegrees = 15f, PieMinAngle0 = 10f, PieMinAngle1 = 5f;

        private static int PieStep(ref RoomDrillState d, double now)
            => Math.Min(8, (int)((now - d.ContactSince) / PieStepSeconds));

        /// <summary>
        /// ⭐ The gate (CB-69, CB-72): true once the entry may flow. While no entry man has crossed, contact at the way in (a hostile SEEN in
        /// the room, or a man of the stack fired on) holds it — the released go back, the first two pie; 3 s quiet releases it. Then the
        /// grenade (once per room): chosen, carried, safe for every friend where he will stand at the bang ⇒ thrown from the door front,
        /// the stack waiting out of the doorway, the entry 0.3 s after the bang.
        /// </summary>
        public static bool Gate(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (d.Crossed != 0) { d.GateOpen = 1; return true; }   // somebody is through: the room is fought from inside (BC-1)
            var know = Knowledge(ref d, in ctx);
            bool firedOn = FiredOnAtTheWayIn(ref d, in ctx);
            if (firedOn) d.FiredOn = 1;
            // ⭐ CB-72 (as built) — once in contact, it lasts while a hostile the squad still holds in the room is alive: 📐 live
            //   (bc-house-defended), a defender down the hallway's axis was unseen from behind the wall, the 3 s "quiet" came, the next
            //   man went in and was shot
            if (know.HostileSeen || firedOn || (d.Contact != 0 && know.HostileKnown))
            {
                if (d.Contact == 0 || d.GateOpen != 0)
                {
                    d.ContactSince = ctx.Now;
                    d.PieStepIssued = 0;
                    d.Contact = 1;
                    d.GateOpen = 0;
                    d.Released = 0;
                    if (d.GrenadeStep is GrenadeToDoor or GrenadeThrowing) { d.GrenadeStep = GrenadeUndecided; d.Thrower = -1; }   // not into the fire
                    ws.Reissue = 1;
                }
                d.QuietSince = 0;
                int step = PieStep(ref d, ctx.Now);
                if (step != d.PieStepIssued) { d.PieStepIssued = (byte)step; ws.Reissue = 1; }   // the next slice
                return false;
            }
            if (d.Contact != 0)
            {
                if (d.QuietSince == 0) { d.QuietSince = ctx.Now; return false; }
                if (ctx.Now - d.QuietSince < QuietSeconds) return false;
                d.Contact = 0;
                if (d.Thrown == 0) d.GrenadeStep = GrenadeUndecided;   // the room is suspected now: decide again
                ws.Reissue = 1;
            }
            if (d.GateOpen != 0) return true;
            if (!Grenade(ref d, ref ws, in ctx, know)) return false;
            d.GateOpen = 1;
            d.EnterSince = ctx.Now;
            d.Released = 0;
            ws.Reissue = 1;
            return true;
        }

        /// <summary>CB-66 — what the squad knows of the room now.</summary>
        public static RoomKnowledge Knowledge(ref RoomDrillState d, in SquadPlayContext ctx)
        {
            Span<float> corners = stackalloc float[8];
            for (int i = 0; i < 8; i++) corners[i] = Corner(ref d, i);
            return RoomSuspicion.Of(ctx.World, ctx.Commander, corners, d.FloorZ, d.Ordered != 0, d.FiredOn != 0);
        }

        private static unsafe float Corner(ref RoomDrillState d, int i) => d.Corners[i];

        /// <summary>CB-72 — a man of the stack (in it, not through) hit or near-missed within <see cref="FiredOnSeconds"/>.</summary>
        private static unsafe bool FiredOnAtTheWayIn(ref RoomDrillState d, in SquadPlayContext ctx)
        {
            for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
            {
                if (d.OrderOf[i] == RoomDrillState.NotInIt || (d.Crossed & (1 << i)) != 0 || !ctx.Live(i, out var m)) continue;
                if (RecentSensesOf.Within(ctx.World, m, SensorChange.Hit, FiredOnSeconds)
                    || RecentSensesOf.Within(ctx.World, m, SensorChange.NearMiss, FiredOnSeconds)) return true;
            }
            return false;
        }

        /// <summary>CB-67…CB-70 — one step of the grenade; true once it is done (thrown and banged, or none to throw).</summary>
        private static unsafe bool Grenade(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx, in RoomKnowledge know)
        {
            switch (d.GrenadeStep)
            {
                case GrenadeUndecided:
                    if (d.Thrown != 0 || !Decide(ref d, in ctx, in know)) { d.GrenadeStep = GrenadeDone; return true; }
                    d.GrenadeStep = GrenadeToDoor;
                    d.StepSince = ctx.Now;
                    ws.Reissue = 1;
                    return false;
                case GrenadeToDoor:
                {
                    if (!ThrowerLive(ref d, in ctx, out var man) || ctx.Now - d.StepSince > ToDoorTimeoutSeconds) { d.GrenadeStep = GrenadeDone; return true; }
                    var at = ctx.At(man);
                    var from = ThrowFrom(ref d);
                    if (Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(from.X, from.Y)) > JambArrival + 0.2f) return false;
                    d.RoundsBefore = GrenadeCheck.Rounds(ctx.World, man, d.GrenadeMount);
                    d.GrenadeStep = GrenadeThrowing;
                    d.StepSince = ctx.Now;
                    ws.Reissue = 1;
                    return false;
                }
                case GrenadeThrowing:
                {
                    if (!ThrowerLive(ref d, in ctx, out var man) || ctx.Now - d.StepSince > ThrowTimeoutSeconds) { d.GrenadeStep = GrenadeDone; return true; }
                    if (GrenadeCheck.Rounds(ctx.World, man, d.GrenadeMount) >= d.RoundsBefore) return false;
                    float flight = Vector3.Distance(ThrowFrom(ref d), d.Aim) / ThrowSpeed;
                    d.BangAt = ctx.Now + d.Fuze + flight;
                    d.Thrown = 1;
                    d.GrenadeStep = GrenadeThrown;
                    ws.Reissue = 1;   // the thrower back to his stack point, out of the doorway
                    return false;
                }
                case GrenadeThrown:
                    if (ctx.Now < d.BangAt + AfterBangSeconds) return false;
                    d.GrenadeStep = GrenadeDone;
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>CB-68 — the jamb he throws from: just inside the opening's edge on his side (<see cref="JambInsetMetres"/>),
        /// <see cref="JambBackMetres"/> out from the wall.</summary>
        private static unsafe Vector3 ThrowFrom(ref RoomDrillState d)
            => d.Thrower >= 0 && d.OrderOf[d.Thrower] != RoomDrillState.NotInIt
                ? new Vector3(Jamb(ref d, JambSide(ref d, d.OrderOf[d.Thrower])), d.Plan.StackZ) : d.Plan.DoorFront;

        private static Vector2 Jamb(ref RoomDrillState d, float side)
            => d.Plan.Door + d.Plan.Along * side * MathF.Max(0f, d.Plan.Half - JambInsetMetres) - d.Plan.Into * JambBackMetres;

        /// <summary>CB-68 — does a throw from <paramref name="from"/> to <paramref name="to"/> pass the wall's plane INSIDE the opening (clear of its
        /// edges by <see cref="JambClearMetres"/>)?</summary>
        private static bool ThrowClears(ref RoomDrillState d, Vector2 from, Vector2 to)
        {
            float a = Vector2.Dot(d.Plan.Door - from, d.Plan.Into), b = Vector2.Dot(to - from, d.Plan.Into);
            if (b <= 1e-4f || a < 0f || a > b) return false;
            var cross = from + (to - from) * (a / b);
            return MathF.Abs(Vector2.Dot(cross - d.Plan.Door, d.Plan.Along)) <= d.Plan.Half - JambClearMetres;
        }

        /// <summary>+1 when the <paramref name="k"/>-th stack point is on the <c>Along</c> side of the way in, −1 on the far side (a split stack).</summary>
        private static float JambSide(ref RoomDrillState d, int k)
        {
            var at = d.Plan.Stack(k);
            return Vector2.Dot(new Vector2(at.X, at.Y) - d.Plan.Door, d.Plan.Along) >= 0f ? 1f : -1f;
        }

        private static unsafe bool ThrowerLive(ref RoomDrillState d, in SquadPlayContext ctx, out Entity man)
        {
            man = Entity.Null;
            return d.Thrower >= 0 && ctx.Live(d.Thrower, out man);
        }

        /// <summary>CB-67, CB-68, CB-70 — the grenade, the thrower and the aim point; false when none is to be thrown.</summary>
        private static unsafe bool Decide(ref RoomDrillState d, in SquadPlayContext ctx, in RoomKnowledge know)
        {
            var kind = GrenadeCheck.Choose((ClearIntensity)d.Intensity, in know);
            if (kind == RoomGrenadeKind.None) return false;
            if (!FindThrower(ref d, in ctx, kind, out int slot, out int mount, out var warhead)
                && !(kind == RoomGrenadeKind.Fragmentation && FindThrower(ref d, in ctx, RoomGrenadeKind.Stun, out slot, out mount, out warhead)))
                return false;
            // where every man will stand at the bang: his stack point (the thrower back at his), else where he is
            Span<Vector3> men = stackalloc Vector3[RoomDrillState.Capacity];
            int n = 0;
            for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
            {
                if (!ctx.Live(i, out var m)) continue;
                men[n++] = d.OrderOf[i] == RoomDrillState.NotInIt ? ctx.At(m) : d.Plan.Stack(d.OrderOf[i]);
            }
            var query = Fdp.Toolkit.World.WorldQuery.Of(ctx.World);
            var scratch = new System.Collections.Generic.List<Fdp.Toolkit.World.TraceCrossing>();
            float depth = Depth(ref d);
            // ⭐ CB-68 (as built) — thrown from the jamb on his side. The aims sweep from STRAIGHT IN (the point the wall shields the stack
            //   from) toward the far side; the first the throw clears through the opening AND that spares every friend is taken. 📐 live: a
            //   throw across the room from the jamb is seen back through the opening by the men stacked beside him — every one refused.
            float side = JambSide(ref d, d.OrderOf[slot]);
            var jamb = Jamb(ref d, side);
            Span<float> corners = stackalloc float[8];
            for (int c = 0; c < 8; c++) corners[c] = d.Corners[c];
            Span<float> reaches = stackalloc float[] { MathF.Min(ThrowInMetres, depth * 0.5f), depth * 2f / 3f, MathF.Min(2f, depth * 0.4f) };
            Span<float> drifts = stackalloc float[] { 0f, 0.5f, 1f, 1.5f };
            foreach (float reach0 in reaches)
                foreach (float drift in drifts)
                {
                    float reach = reach0;
                    var lateral = -d.Plan.Along * side * drift;
                    while (reach > 0.5f && !RoomEntryPlan.Inside(corners, d.Plan.Door + d.Plan.Into * reach + lateral)) reach -= 0.25f;   // inside the room
                    var flat = d.Plan.Door + d.Plan.Into * reach + lateral;
                    if (!ThrowClears(ref d, jamb, flat)) continue;
                    var aim = new Vector3(flat, d.FloorZ + 0.3f);
                    if (!GrenadeCheck.SafeFor(query, warhead, aim, men[..n], scratch)) continue;
                    d.Aim = aim;
                    d.Thrower = (sbyte)slot;
                    d.GrenadeMount = (byte)mount;
                    d.Fuze = warhead.FuzeDelayS > 0f ? warhead.FuzeDelayS : 1.5f;
                    return true;
                }
            return false;   // CB-70 — every aim point would hurt a friend
        }

        /// <summary>CB-68 — #2 when he carries it, else the first entry man who does, else any live man of the stack.</summary>
        private static unsafe bool FindThrower(ref RoomDrillState d, in SquadPlayContext ctx, RoomGrenadeKind kind, out int slot, out int mount,
                                               out Fdp.Toolkit.Tkb.Domain.WarheadDto warhead)
        {
            slot = -1; mount = -1; warhead = null!;
            Span<int> byOrder = stackalloc int[RoomDrillState.Capacity];
            int count = 0;
            for (int k = 0; k < RoomDrillState.Capacity; k++)
                for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++)
                    if (d.OrderOf[i] == k) { byOrder[count++] = i; break; }
            Span<int> pref = stackalloc int[count];
            int p = 0;
            if (count > 1) pref[p++] = byOrder[1];
            for (int k = 0; k < count; k++) if (k != 1) pref[p++] = byOrder[k];
            foreach (int i in pref[..p])
            {
                if (!ctx.Live(i, out var m)) continue;
                if (GrenadeCheck.Carried(ctx.World, m, kind, out mount, out _, out warhead)) { slot = i; return true; }
            }
            return false;
        }

        /// <summary>How deep the room runs from its way in along <c>Into</c> (m).</summary>
        private static unsafe float Depth(ref RoomDrillState d)
        {
            float deepest = 1f;
            for (int c = 0; c < 4; c++)
                deepest = MathF.Max(deepest, Vector2.Dot(new Vector2(d.Corners[c * 2], d.Corners[c * 2 + 1]) - d.Plan.Door, d.Plan.Into));
            return deepest;
        }

        /// <summary>CB-72 — the <paramref name="k"/>-th man's pie point: on an arc <see cref="PieMetres"/> out from the doorway, on his own side of
        /// it, a frame post leaning toward the opening (HoldWatch's lean, D-2l).</summary>
        private static MemberOrder PiePoint(ref RoomDrillState d, int k, float speed, byte role)
        {
            var stack = d.Plan.Stack(k);
            float s = Vector2.Dot(new Vector2(stack.X, stack.Y) - d.Plan.Door, d.Plan.Along) >= 0f ? 1f : -1f;
            // ⭐ CB-22 / CB-72 (as built) — SLICE THE PIE: each slice steps toward the doorway's line, so the leaning man comes to see down it
            float swing = d.PieStepIssued * PieStepDegrees;
            float a = MathF.Max(k == 0 ? PieMinAngle0 : PieMinAngle1, (k == 0 ? PieAngle0 : PieAngle1) - swing) * MathF.PI / 180f;
            Vector2 door = d.Plan.Door, into = d.Plan.Into, along = d.Plan.Along;
            Vector2 At(float side) => door + PieMetres * (MathF.Cos(a) * -into + MathF.Sin(a) * side * along);
            var point = new Vector3(At(s), d.Plan.StackZ);
            var opening = new Vector3(d.Plan.Door, d.Plan.StackZ);
            return new MemberOrder
            {
                Kind = MemberOrderKind.Watch, Point = point, Speed = speed, RoleId = role,
                Sector = MemberArc.Around(LookAt.BearingTo(point, opening), StackArcHalfWidth),
                Post = new MemberPost { Set = 1, Radius = 0.6f, Frame = 1, FrameA = point, FrameB = new Vector3(At(-s), d.Plan.StackZ), Opening = opening },
            };
        }

        /// <summary>⭐ CE-2261 (CB-73) — a man of the stack down before anybody went through: the stack is placed again over the live men (the next
        /// fills his place) and the entry starts again from the gate (a grenade already thrown is not thrown again).</summary>
        private static unsafe void Restack(ref RoomDrillState d, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (d.Crossed != 0) return;
            ushort live = LiveMask(in ctx);
            if ((d.Placed & ~live) == 0) { d.Placed &= live; return; }
            int inFight = 0;
            for (int i = 0; i < ctx.Roster.Count; i++) if (ctx.Live(i, out _)) inFight++;
            if (inFight == 0) return;
            for (int i = 0; i < RoomDrillState.Capacity; i++) d.OrderOf[i] = RoomDrillState.NotInIt;
            Places(ref d, in ctx, inFight, d.Turn);
            d.Placed = live;
            d.Released = 0;
            d.GateOpen = 0;
            d.AtDoor = 0;
            if (d.GrenadeStep is GrenadeToDoor or GrenadeThrowing) { d.GrenadeStep = GrenadeUndecided; d.Thrower = -1; }
            ws.Reissue = 1;
        }

        private static ushort LiveMask(in SquadPlayContext ctx)
        {
            ushort mask = 0;
            for (int i = 0; i < ctx.Roster.Count && i < RoomDrillState.Capacity; i++) if (ctx.Live(i, out _)) mask |= (ushort)(1 << i);
            return mask;
        }

        /// <summary>⭐ CE-2334 CB-64 — the room on the other side of <paramref name="way"/> from <paramref name="room"/> in
        /// <paramref name="answer"/>: where the stack stands; null when the way in leads outside.</summary>
        public static SurveyRoom? StackRoom(WorldSurveyPayload answer, SurveyRoom room, SurveyPortal way)
        {
            int other = way.RoomA == room.Index ? way.RoomB : way.RoomA;
            if (other < 0 || other == room.Index) return null;
            var r = RoomOrder.RoomOf(answer, room.Building, other);
            return r != null && r.Indoors && r.Corners.Length >= 8 ? r : null;
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

        /// <summary>CB-55 — a pooled HOSTILE inside the room's quad at its storey, alive and still in the fight (a neutral is not one).</summary>
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
                // ⭐ CB-55 says HOSTILE: a neutral (a civilian cowering in the corner — CE-2337's occupant stays) does not keep the room
                //   uncleared; BC-2 controls him. 📌 bc-house-clear: room C never cleared with its occupant in it.
                if (ctx.World.IsAlive(e) && !ThreatDanger.IsOutOfTheFight(ctx.World, e) && !ThreatDanger.IsNeutral(ctx.World, e)) return true;
            }
            return false;
        }
    }
}

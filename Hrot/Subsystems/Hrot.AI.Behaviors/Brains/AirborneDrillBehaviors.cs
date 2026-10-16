using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ CE-1118 H-1 (📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6) — what the airborne drills share: who is coming down, the
    /// carrier's ropes, the Drop leg, the stick's landing points and the ring a squad takes once down. Pure reads of this node's world.
    /// </summary>
    public static class AirborneDrill
    {
        /// <summary>The next man goes down a rope when the one below is this far under the door (m) — design §2.</summary>
        public const float RopeGapMetres = 3f;
        /// <summary>A man is never sent down a rope sooner than this after the last one was told (s): the exit and his
        /// <see cref="Descent"/> take a moment to show on this node.</summary>
        public const float MinRopeIntervalSeconds = 1f;
        /// <summary>A man told to leave who is still aboard after this long is told again (s).</summary>
        public const float ReorderSeconds = 4f;
        /// <summary>A ring spot's surface may differ from the centre's by this much and still be "the same roof" (m) — D-20.</summary>
        public const float SameSurfaceMetres = 0.5f;
        /// <summary>A member this close to his spot (flat, m) has taken it.</summary>
        public const float AtSpotMetres = 2f;

        /// <summary>True while <paramref name="man"/> is coming down a rope or under a canopy (his <see cref="Descent"/>, landed or not).</summary>
        public static bool Descending(EntityRepository world, Entity man)
            => world.IsComponentTypeRegistered<Descent>() && world.HasComponent<Descent>(man);

        /// <summary>True when <paramref name="man"/> rides <paramref name="carrier"/>.</summary>
        public static bool Aboard(EntityRepository world, Entity man, Entity carrier)
            => !carrier.IsNull && AirHolds.CarrierOf(world, man) == carrier;

        /// <summary>The carrier's ropes (its <c>RopeAnchors</c>; 2 — one per side door — when it states none).</summary>
        public static int RopeCount(EntityRepository world, Entity carrier)
            => Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(world, carrier, out var t)
               && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>() is { RopeAnchors.Count: > 0 } c
                ? Math.Min(c.RopeAnchors.Count, 4) : 2;

        /// <summary>The carrier's navigation phase (Idle when it reports none here).</summary>
        public static NavigationPhase PhaseOf(EntityRepository world, Entity carrier)
            => world.HasComponent<NavigationStatus>(carrier) ? world.GetComponentRO<NavigationStatus>(carrier).Phase : NavigationPhase.Idle;

        /// <summary>
        /// True when rope <paramref name="rope"/> is free for the next man: its last man (<paramref name="last"/>) is gone, or out of the
        /// carrier AND either landed / off the rope or <see cref="RopeGapMetres"/> under the door — and he was told at least
        /// <see cref="MinRopeIntervalSeconds"/> ago (<paramref name="sinceOrdered"/>).
        /// </summary>
        public static bool RopeClear(EntityRepository world, Entity carrier, int rope, Entity last, float sinceOrdered)
        {
            if (last.IsNull || !world.IsAlive(last)) return true;
            if (sinceOrdered < MinRopeIntervalSeconds || Aboard(world, last, carrier)) return false;
            if (!Descending(world, last)) return true;   // off the rope (or never on it): it is free
            ref readonly var d = ref world.GetComponentRO<Descent>(last);
            if (d.Phase is DescentPhase.Landed or DescentPhase.Falling) return true;
            if (!world.HasComponent<SimTransform>(last) || !DescentRules.DoorPoint(world, carrier, rope, out var door)) return true;
            return door.Z - world.GetComponentRO<SimTransform>(last).Position.Z >= RopeGapMetres;
        }

        /// <summary>The highest surface at (x, y) this node knows (the ground, else <paramref name="z"/>).</summary>
        public static float SurfaceAt(Fdp.Toolkit.World.IWorldQuery? terrain, float x, float y, float z)
            => terrain != null ? DescentRules.HighestSurface(terrain, x, y) : z;

        /// <summary>
        /// ⭐ D-20 — spot <paramref name="k"/> of <paramref name="n"/> in a ring of <paramref name="radius"/> round <paramref name="centre"/>,
        /// ON THE SAME SURFACE as the centre: the radius shrinks (×0.6, ×0.3) until the spot and the midpoint to it stand within
        /// <see cref="SameSurfaceMetres"/> of the centre's surface — on a roof the ring stays inside its outline (a man is never sent off the
        /// ledge, CE-3145). A spot that never fits is the centre. Z is the surface.
        /// </summary>
        public static Vector3 RingSpot(Fdp.Toolkit.World.IWorldQuery? terrain, Vector3 centre, int k, int n, float radius)
        {
            float level = SurfaceAt(terrain, centre.X, centre.Y, centre.Z);
            double angle = 2.0 * Math.PI * k / Math.Max(n, 1);
            var dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            foreach (float f in stackalloc float[] { 1f, 0.6f, 0.3f })
            {
                var p = new Vector2(centre.X, centre.Y) + dir * (radius * f);
                var mid = new Vector2(centre.X, centre.Y) + dir * (radius * f * 0.5f);
                float s = SurfaceAt(terrain, p.X, p.Y, level), sm = SurfaceAt(terrain, mid.X, mid.Y, level);
                if (MathF.Abs(s - level) <= SameSurfaceMetres && MathF.Abs(sm - level) <= SameSurfaceMetres) return new Vector3(p.X, p.Y, s);
            }
            return new Vector3(centre.X, centre.Y, level);
        }

        /// <summary>True when <paramref name="man"/> stands within <see cref="AtSpotMetres"/> (flat) of <paramref name="spot"/>.</summary>
        public static bool AtSpot(EntityRepository world, Entity man, Vector3 spot)
        {
            if (!world.HasComponent<SimTransform>(man)) return false;
            var at = world.GetComponentRO<SimTransform>(man).Position;
            return Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(spot.X, spot.Y)) <= AtSpotMetres;
        }

        /// <summary>
        /// ⭐ D-21 — the Drop LEG the carrier flies (B-3: it begins at a <see cref="RouteHold.Drop"/> waypoint and runs to the next one):
        /// its first point <paramref name="start"/> and its flat direction <paramref name="along"/>. The Drop waypoint nearest the carrier
        /// on its route; no route here ⇒ from the carrier along its velocity (false when it is not moving either).
        /// </summary>
        public static bool DropLeg(EntityRepository world, Entity carrier, out Vector3 start, out Vector2 along)
        {
            start = default; along = default;
            if (!world.HasComponent<SimTransform>(carrier)) return false;
            var at = world.GetComponentRO<SimTransform>(carrier).Position;
            long routeId = world.HasComponent<NavigationIntent>(carrier) ? world.GetComponentRO<NavigationIntent>(carrier).RouteEntityId : 0;
            var route = routeId > 0 ? Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, routeId) : Entity.Null;
            if (!route.IsNull && world.HasManagedComponent<RoutePlan>(route)
                && ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<RoutePlan>(route) is { } plan)
            {
                float best = float.MaxValue;
                for (int i = 0; i < plan.Waypoints.Count; i++)
                {
                    var w = plan.Waypoints[i];
                    if (w.Hold != RouteHold.Drop) continue;
                    float d = Vector2.DistanceSquared(new Vector2(w.Position.X, w.Position.Y), new Vector2(at.X, at.Y));
                    if (d >= best) continue;
                    var next = i + 1 < plan.Waypoints.Count ? plan.Waypoints[i + 1].Position : w.Position;
                    var dir = new Vector2(next.X - w.Position.X, next.Y - w.Position.Y);
                    if (dir.LengthSquared() < 1f) continue;
                    best = d; start = w.Position; along = Vector2.Normalize(dir);
                }
                if (best < float.MaxValue) return true;
            }
            var v = world.HasComponent<SimVelocity>(carrier) ? world.GetComponentRO<SimVelocity>(carrier).Linear : Vector3.Zero;
            var flat = new Vector2(v.X, v.Y);
            if (flat.LengthSquared() < 1f) return false;
            start = at; along = Vector2.Normalize(flat);
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════ SquadFastRope

    /// <summary>⭐ CE-1118 H-1 — <c>SquadFastRope</c>'s params and the play's per-member memory (D-19).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SquadFastRopeParams
    {
        public float TimeoutSeconds;
        public float RingRadius;
        /// <summary>The helicopter (found at the start: the one a member rides).</summary>
        public Entity Carrier;
        /// <summary>Where the ropes come down (under the carrier at the start; Z = the surface there).</summary>
        public Vector3 RopePoint;
        public byte Ropes;
        /// <summary>Per member: 0 waiting aboard, 1 told to go down, 2 down (or never aboard) — to his ring spot.</summary>
        public fixed byte State[UnitRoster.Capacity];
        public fixed byte Rope[UnitRoster.Capacity];
        /// <summary>Per member: seconds after the play went that he was told to go down.</summary>
        public fixed float OrderedAt[UnitRoster.Capacity];
        /// <summary>Per rope: the member index + 1 of the last man sent down it (0 = none).</summary>
        public fixed byte LastOnRope[4];
    }

    /// <summary>
    /// ⭐ CE-1118 H-1 (📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.3) — <c>SquadFastRope</c> on the commander: the squad's SOP
    /// gives it while its helicopter holds at a <c>Rope</c> stop (<see cref="SquadSopConditions.CarrierRoping"/>). One man per rope at a
    /// time, the next when the one below is <see cref="AirborneDrill.RopeGapMetres"/> down; each man down takes his spot in a 360° ring
    /// round the rope point. Success when nobody is aboard or on a rope and every man holds his spot — the squad's task resumes.
    /// </summary>
    public static class SquadFastRopeBehavior
    {
        public const float DefaultTimeoutSeconds = 60f;
        public const float DefaultRingRadius = 5f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SquadFastRopeBlackboard
        {
            public SquadFastRopeParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadFastRope)]
        public static void Resolve(in SquadFastRopeParamsJsonDto authored, ref SquadFastRopeBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadFastRopeParams
            {
                TimeoutSeconds = authored.TimeoutSeconds > 0f ? authored.TimeoutSeconds : DefaultTimeoutSeconds,
                RingRadius = authored.RingRadius > 0f ? authored.RingRadius : DefaultRingRadius,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SquadFastRope, Params = typeof(SquadFastRopeParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SquadFastRope, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadFastRopeBlackboard))]
        public static BTreeBuilder<SquadFastRopeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadFastRopeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadFastRopeParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadFastRopePlay, SquadFastRopeParams>(ref p, ref ws, self, world);
    }

    /// <summary>⭐ CE-1118 H-1 — the fast rope as a play (§6.3).</summary>
    public unsafe struct SquadFastRopePlay : ISquadPlay<SquadFastRopeParams>
    {
        private static readonly SquadHsmShell s_shell = new(SquadFastRopeManeuver.BuildTransitionTable(), SquadFastRopeManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadFastRopeManeuver.ManeuverKind;
        public ushort StartPhase => SquadFastRopeManeuver.PhaseRoping;
        public ushort AbortPhase => SquadFastRopeManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadFastRopeParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            p.Carrier = Entity.Null;
            for (int i = 0; i < ctx.Roster.Count && p.Carrier.IsNull; i++)
                if (ctx.Live(i, out var m)) p.Carrier = AirHolds.CarrierOf(ctx.World, m);
            if (p.Carrier.IsNull || !ctx.World.HasComponent<SimTransform>(p.Carrier)) return PlayStart.Fail;
            var at = ctx.World.GetComponentRO<SimTransform>(p.Carrier).Position;
            p.RopePoint = new Vector3(at.X, at.Y, AirborneDrill.SurfaceAt(Fdp.Toolkit.World.WorldQuery.Of(ctx.World), at.X, at.Y, 0f));
            p.Ropes = (byte)AirborneDrill.RopeCount(ctx.World, p.Carrier);
            int k = 0;
            for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity); i++)
            {
                bool aboard = ctx.Live(i, out var m) && AirborneDrill.Aboard(ctx.World, m, p.Carrier);
                p.State[i] = aboard ? (byte)0 : (byte)2;
                p.Rope[i] = aboard ? (byte)(k++ % p.Ropes) : (byte)0;
            }
            for (int r = 0; r < 4; r++) p.LastOnRope[r] = 0;
            return PlayStart.Go;
        }

        public void Update(ref SquadFastRopeParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var world = ctx.World;
            float t = (float)(ctx.Now - ws.StartedAt);
            int n = Math.Min(ctx.Roster.Count, UnitRoster.Capacity);
            // a man told to go down: out ⇒ on his rope (then to his spot once off it); still aboard too long ⇒ told again
            for (int i = 0; i < n; i++)
            {
                if (p.State[i] != 1 || !ctx.Live(i, out var m)) continue;
                if (AirborneDrill.Aboard(world, m, p.Carrier))
                {
                    if (t - p.OrderedAt[i] >= AirborneDrill.ReorderSeconds) { p.OrderedAt[i] = t; ws.Reissue = 1; }
                    continue;
                }
                if (!AirborneDrill.Descending(world, m) && t - p.OrderedAt[i] >= AirborneDrill.MinRopeIntervalSeconds) { p.State[i] = 2; ws.Reissue = 1; }
            }
            // each rope: the next man when the one below is far enough down
            for (int r = 0; r < p.Ropes; r++)
            {
                int lastIdx = p.LastOnRope[r] - 1;
                var last = lastIdx >= 0 ? ctx.Roster.SubordinateEntities[lastIdx] : Entity.Null;
                float since = lastIdx >= 0 ? t - p.OrderedAt[lastIdx] : float.MaxValue;
                if (!AirborneDrill.RopeClear(world, p.Carrier, r, last, since)) continue;
                for (int i = 0; i < n; i++)
                {
                    if (p.State[i] != 0 || p.Rope[i] != r || !ctx.Live(i, out _)) continue;
                    p.State[i] = 1; p.OrderedAt[i] = t; p.LastOnRope[r] = (byte)(i + 1);
                    ws.Reissue = 1;
                    break;
                }
            }
        }

        public NodeStatus Outcome(ref SquadFastRopeParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            bool done = true;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(ctx.World);
            for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity) && done; i++)
            {
                if (!ctx.Live(i, out var m)) continue;
                if (p.State[i] != 2 || AirborneDrill.Descending(ctx.World, m)
                    || !AirborneDrill.AtSpot(ctx.World, m, AirborneDrill.RingSpot(terrain, p.RopePoint, i, ctx.Roster.Count, p.RingRadius))) done = false;
            }
            if (done) return NodeStatus.Success;
            return p.TimeoutSeconds > 0f && ctx.Now - ws.StartedAt >= p.TimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadFastRopeParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref SquadFastRopeParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadFastRopeParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ctx.Live(member, out var m);
            byte state = member < UnitRoster.Capacity ? p.State[member] : (byte)2;
            if (state == 1 && AirborneDrill.Aboard(ctx.World, m, p.Carrier))
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Disembark, Exit = ExitKind.RopeDown, AnchorIndex = p.Rope[member], RoleId = SquadFastRopeManeuver.RoleRoping,
                };
            if (state != 2 || AirborneDrill.Descending(ctx.World, m))
                return new MemberOrder { Kind = MemberOrderKind.Stay, RoleId = state == 0 ? SquadFastRopeManeuver.RoleWaiting : SquadFastRopeManeuver.RoleRoping };
            var spot = AirborneDrill.RingSpot(Fdp.Toolkit.World.WorldQuery.Of(ctx.World), p.RopePoint, member, ctx.Roster.Count, p.RingRadius);
            return new MemberOrder { Kind = MemberOrderKind.Watch, Point = spot, Speed = 2f, ArrivalRadius = 1f, RoleId = SquadFastRopeManeuver.RoleSecuring };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }

    // ═══════════════════════════════════════════════════════════════ SquadAirborneExit

    /// <summary>⭐ CE-1118 H-1 — <c>SquadAirborneExit</c>'s params and the jumpmaster's per-member plan (D-19, D-21).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SquadAirborneExitParams
    {
        public float IntervalSeconds;
        public float RingRadius;
        public float TimeoutSeconds;
        public Entity Carrier;
        /// <summary>1 once the stick is planned (the carrier on its Drop leg).</summary>
        public byte Planned;
        public byte Doors;
        /// <summary>How many of the stick have been told to jump.</summary>
        public byte Jumped;
        public byte StickCount;
        /// <summary>Seconds after the play went that the last man was told to jump (−1 = none yet).</summary>
        public float LastJumpAt;
        /// <summary>The Drop leg: its first point and its flat direction (X, Y).</summary>
        public Vector3 LegStart;
        public Vector2 LegAlong;
        /// <summary>The assembly point (the stick's middle landing point).</summary>
        public Vector3 Rally;
        /// <summary>Per member: 0 waiting aboard, 1 told to jump, 2 coming down, 3 down — to his ring spot.</summary>
        public fixed byte State[UnitRoster.Capacity];
        /// <summary>Per member: his place in the stick.</summary>
        public fixed byte Slot[UnitRoster.Capacity];
        public fixed float LandX[UnitRoster.Capacity];
        public fixed float LandY[UnitRoster.Capacity];
        public fixed float LandZ[UnitRoster.Capacity];
        /// <summary>Per member: how far along the leg his release point lies (m).</summary>
        public fixed float ReleaseAlong[UnitRoster.Capacity];
        public fixed float OrderedAt[UnitRoster.Capacity];
    }

    /// <summary>
    /// ⭐ CE-1118 H-1 (📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.4) — <c>SquadAirborneExit</c>, the JUMPMASTER on the
    /// commander: the squad's SOP gives it while its aircraft flies a Drop leg (<see cref="SquadSopConditions.CarrierDropping"/>). Each
    /// man's landing point lies along the leg one second of flight from the next (a free-fall team: a 5 m ring); he jumps when the carrier
    /// passes his release point (<see cref="ReleasePoint.For"/>) and a second after the man before, alternating doors, with that point as
    /// his <c>Descent.Target</c> (D-18). Once down he takes his spot in the assembly ring round the stick's middle point.
    /// </summary>
    public static class SquadAirborneExitBehavior
    {
        public const float DefaultIntervalSeconds = 1f;
        public const float DefaultRingRadius = 8f;
        public const float DefaultTimeoutSeconds = 300f;
        /// <summary>A man told to jump counts as coming down after this long even before his Descent shows here (s).</summary>
        public const float ExitSeconds = 2f;
        /// <summary>The least gap between two jumpers, as a share of the interval — a floor; the release points set the pace.</summary>
        public const float MinGapShare = 0.8f;
        /// <summary>The free-fall team's landing ring (m).</summary>
        public const float TeamRingMetres = 5f;
        /// <summary>The first landing point lies this far INTO the leg beyond the point whose release is its start (m).</summary>
        public const float LeadInMetres = 5f;

        [StructLayout(LayoutKind.Sequential)]
        public struct SquadAirborneExitBlackboard
        {
            public SquadAirborneExitParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadAirborneExit)]
        public static void Resolve(in SquadAirborneExitParamsJsonDto authored, ref SquadAirborneExitBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadAirborneExitParams
            {
                IntervalSeconds = authored.IntervalSeconds > 0f ? authored.IntervalSeconds : DefaultIntervalSeconds,
                RingRadius = authored.RingRadius > 0f ? authored.RingRadius : DefaultRingRadius,
                TimeoutSeconds = authored.TimeoutSeconds > 0f ? authored.TimeoutSeconds : DefaultTimeoutSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.SquadAirborneExit, Params = typeof(SquadAirborneExitParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.SquadAirborneExit, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadAirborneExitBlackboard))]
        public static BTreeBuilder<SquadAirborneExitBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadAirborneExitBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadAirborneExitParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadAirborneExitPlay, SquadAirborneExitParams>(ref p, ref ws, self, world);

        /// <summary>
        /// ⭐ D-21 — plans the stick once the carrier flies its Drop leg: the men aboard in roster order; each one's landing point along the
        /// leg (or in the team ring) and how far along it his release point lies. False while there is no leg to plan on.
        /// </summary>
        public static unsafe bool Plan(ref SquadAirborneExitParams p, in SquadPlayContext ctx)
        {
            var world = ctx.World;
            if (!AirborneDrill.DropLeg(world, p.Carrier, out var w0, out var along)) return false;
            var carrierAt = world.GetComponentRO<SimTransform>(p.Carrier).Position;
            var v3 = world.HasComponent<SimVelocity>(p.Carrier) ? world.GetComponentRO<SimVelocity>(p.Carrier).Linear : Vector3.Zero;
            var v = new Vector2(v3.X, v3.Y);
            float speed = MathF.Max(v.Length(), 1f);
            if (v.LengthSquared() < 1f) v = along * speed;
            var wind = Wind.Of(world);
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(world);
            int n = Math.Min(ctx.Roster.Count, UnitRoster.Capacity);
            int count = 0;
            for (int i = 0; i < n; i++)
                if (p.State[i] == 0 && ctx.Live(i, out var m) && AirborneDrill.Aboard(world, m, p.Carrier)) p.Slot[i] = (byte)count++;
                else if (p.State[i] == 0) p.State[i] = 3;   // not in this aircraft: nothing to jump from
            p.StickCount = (byte)count;
            if (count == 0) { p.Planned = 1; return true; }
            float spacing = speed * p.IntervalSeconds;
            var start2 = new Vector2(w0.X, w0.Y);
            // the release point lies a fixed offset from its landing point (the same chute, height, wind and carrier speed for all), so the
            // first landing point goes as far into the leg as puts its release point on the leg's start
            float lead = 0f, need = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (p.State[i] != 0 || !ctx.Live(i, out var m)) continue;
                    var chute = ParachuteModel.Of(world, m, CanopyKind.NoCanopy);
                    bool team = chute.Canopy == CanopyKind.RamAir;
                    var land = start2 + along * (team ? lead : lead + p.Slot[i] * spacing);
                    if (team)
                    {
                        double a = 2.0 * Math.PI * p.Slot[i] / count;
                        land += new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * TeamRingMetres;
                    }
                    float ground = AirborneDrill.SurfaceAt(terrain, land.X, land.Y, 0f);
                    var release = ReleasePoint.For(land, MathF.Max(carrierAt.Z - ground, 0f), chute, wind, v);
                    float rAlong = Vector2.Dot(release - start2, along);
                    if (pass == 0) { need = MathF.Max(need, -rAlong); continue; }   // every release point on the leg (≥ 0 along it)
                    p.LandX[i] = land.X; p.LandY[i] = land.Y; p.LandZ[i] = ground;
                    p.ReleaseAlong[i] = rAlong;
                }
                lead = need + LeadInMetres;
            }
            // the rally: the middle of the stick's landing points
            var mid = Vector3.Zero; int k = 0;
            for (int i = 0; i < n; i++) if (p.State[i] == 0) { mid += new Vector3(p.LandX[i], p.LandY[i], p.LandZ[i]); k++; }
            p.Rally = k > 0 ? mid / k : carrierAt;
            p.LegStart = w0; p.LegAlong = along;
            p.Doors = (byte)AirborneDrill.RopeCount(world, p.Carrier);
            p.Planned = 1;
            return true;
        }
    }

    /// <summary>⭐ CE-1118 H-1 — the jumpmaster as a play (§6.4).</summary>
    public unsafe struct SquadAirborneExitPlay : ISquadPlay<SquadAirborneExitParams>
    {
        private static readonly SquadHsmShell s_shell = new(SquadAirborneExitManeuver.BuildTransitionTable(), SquadAirborneExitManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadAirborneExitManeuver.ManeuverKind;
        public ushort StartPhase => SquadAirborneExitManeuver.PhaseJumping;
        public ushort AbortPhase => SquadAirborneExitManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadAirborneExitParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            p.Carrier = Entity.Null;
            for (int i = 0; i < ctx.Roster.Count && p.Carrier.IsNull; i++)
                if (ctx.Live(i, out var m)) p.Carrier = AirHolds.CarrierOf(ctx.World, m);
            if (p.Carrier.IsNull || !ctx.World.HasComponent<SimTransform>(p.Carrier)) return PlayStart.Fail;
            for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity); i++) p.State[i] = 0;
            p.Planned = 0; p.Jumped = 0; p.LastJumpAt = -1f;
            return PlayStart.Go;
        }

        public void Update(ref SquadAirborneExitParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var world = ctx.World;
            if (p.Planned == 0 && !SquadAirborneExitBehavior.Plan(ref p, in ctx)) return;
            float t = (float)(ctx.Now - ws.StartedAt);
            int n = Math.Min(ctx.Roster.Count, UnitRoster.Capacity);
            for (int i = 0; i < n; i++)
            {
                if (!ctx.Live(i, out var m)) continue;
                if (p.State[i] == 1)
                {
                    if (AirborneDrill.Aboard(world, m, p.Carrier))
                    {
                        if (t - p.OrderedAt[i] >= AirborneDrill.ReorderSeconds) { p.OrderedAt[i] = t; ws.Reissue = 1; }
                    }
                    else if (AirborneDrill.Descending(world, m) || t - p.OrderedAt[i] >= SquadAirborneExitBehavior.ExitSeconds) p.State[i] = 2;
                }
                else if (p.State[i] == 2 && !AirborneDrill.Descending(world, m) && t - p.OrderedAt[i] >= SquadAirborneExitBehavior.ExitSeconds)
                {
                    p.State[i] = 3; ws.Reissue = 1;   // down and out of his harness: to his spot
                }
            }
            // the next man in the stick, at his release point — the release points are one interval of flight apart, so they pace the
            // stick; the gap after the man before is only a floor (a slowing aircraft must not bunch them at the door)
            if (p.Jumped >= p.StickCount || (p.LastJumpAt >= 0f && t - p.LastJumpAt < SquadAirborneExitBehavior.MinGapShare * p.IntervalSeconds)) return;
            if (!world.HasComponent<SimTransform>(p.Carrier)) return;
            var at = world.GetComponentRO<SimTransform>(p.Carrier).Position;
            float carrierAlong = Vector2.Dot(new Vector2(at.X - p.LegStart.X, at.Y - p.LegStart.Y), p.LegAlong);
            for (int i = 0; i < n; i++)
            {
                if (p.State[i] != 0 || p.Slot[i] != p.Jumped) continue;
                if (!ctx.Live(i, out var m) || !AirborneDrill.Aboard(world, m, p.Carrier)) { p.State[i] = 3; p.Jumped++; ws.Reissue = 1; return; }
                if (carrierAlong < p.ReleaseAlong[i]) return;
                p.State[i] = 1; p.OrderedAt[i] = t; p.LastJumpAt = t; p.Jumped++;
                ws.Reissue = 1;
                return;
            }
        }

        public NodeStatus Outcome(ref SquadAirborneExitParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            if (p.Planned != 0)
            {
                bool done = true;
                var terrain = Fdp.Toolkit.World.WorldQuery.Of(ctx.World);
                for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity) && done; i++)
                {
                    if (!ctx.Live(i, out var m)) continue;
                    if (p.State[i] != 3 || !AirborneDrill.AtSpot(ctx.World, m, AirborneDrill.RingSpot(terrain, p.Rally, i, ctx.Roster.Count, p.RingRadius)))
                        done = false;
                }
                if (done) return NodeStatus.Success;
            }
            return p.TimeoutSeconds > 0f && ctx.Now - ws.StartedAt >= p.TimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadAirborneExitParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref SquadAirborneExitParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadAirborneExitParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ctx.Live(member, out var m);
            byte state = member < UnitRoster.Capacity ? p.State[member] : (byte)3;
            if (state == 1 && AirborneDrill.Aboard(ctx.World, m, p.Carrier))
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Disembark, Exit = ExitKind.JumpOut, AnchorIndex = (byte)(p.Slot[member] % Math.Max((int)p.Doors, 1)),
                    Point = new Vector3(p.LandX[member], p.LandY[member], p.LandZ[member]), RoleId = SquadAirborneExitManeuver.RoleJumping,
                };
            if (state != 3)
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Stay,
                    RoleId = state == 0 ? SquadAirborneExitManeuver.RoleWaiting : SquadAirborneExitManeuver.RoleDescending,
                };
            var spot = AirborneDrill.RingSpot(Fdp.Toolkit.World.WorldQuery.Of(ctx.World), p.Rally, member, ctx.Roster.Count, p.RingRadius);
            return new MemberOrder { Kind = MemberOrderKind.Watch, Point = spot, Speed = 3f, ArrivalRadius = 1f, RoleId = SquadAirborneExitManeuver.RoleAssembling };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }

    // ═══════════════════════════════════════════════════════════════ AssembleAfterDrop

    /// <summary>⭐ CE-1118 H-1 (D-22) — <c>AssembleAfterDrop</c>'s params and which members are down.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct AssembleAfterDropParams
    {
        public Vector3 Point;
        public float RingRadius;
        public float TimeoutSeconds;
        /// <summary>Per member: 1 once he was told his spot (down, out of any carrier).</summary>
        public fixed byte Down[UnitRoster.Capacity];
    }

    /// <summary>
    /// ⭐ CE-1118 H-1 (D-22, 📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6) — <c>AssembleAfterDrop</c> on the commander: every man
    /// down (no <see cref="Descent"/>, out of any carrier) takes his spot in a ring round the rally point (the order's; none ⇒ the squad's
    /// mean at the start); a man still coming down keeps what he does. Success when every man holds his spot.
    /// </summary>
    public static class AssembleAfterDropBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AssembleAfterDropBlackboard
        {
            public AssembleAfterDropParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.AssembleAfterDrop)]
        public static void Resolve(in AssembleAfterDropParamsJsonDto authored, ref AssembleAfterDropBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new AssembleAfterDropParams
            {
                Point = authored.Point,
                RingRadius = authored.RingRadius > 0f ? authored.RingRadius : SquadAirborneExitBehavior.DefaultRingRadius,
                TimeoutSeconds = authored.TimeoutSeconds > 0f ? authored.TimeoutSeconds : SquadAirborneExitBehavior.DefaultTimeoutSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.AssembleAfterDrop, Params = typeof(AssembleAfterDropParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.AssembleAfterDrop, Curated = true, Group = "Library/Squad", ParamsType = typeof(AssembleAfterDropBlackboard))]
        public static BTreeBuilder<AssembleAfterDropBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AssembleAfterDropBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref AssembleAfterDropParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<AssembleAfterDropPlay, AssembleAfterDropParams>(ref p, ref ws, self, world);

        /// <summary>True when <paramref name="m"/> is down: alive, out of any carrier, no longer coming down.</summary>
        public static bool IsDown(EntityRepository world, Entity m)
            => AirHolds.CarrierOf(world, m).IsNull && !AirborneDrill.Descending(world, m);
    }

    /// <summary>⭐ CE-1118 H-1 (D-22) — the assembly as a play.</summary>
    public unsafe struct AssembleAfterDropPlay : ISquadPlay<AssembleAfterDropParams>
    {
        private static readonly SquadHsmShell s_shell = new(AssembleAfterDropManeuver.BuildTransitionTable(), AssembleAfterDropManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => AssembleAfterDropManeuver.ManeuverKind;
        public ushort StartPhase => AssembleAfterDropManeuver.PhaseAssembling;
        public ushort AbortPhase => AssembleAfterDropManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref AssembleAfterDropParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            if (!SquadManeuverRunner.AnyLive(in ctx)) return PlayStart.Fail;
            if (p.Point == Vector3.Zero)
            {
                var sum = Vector3.Zero; int k = 0;
                for (int i = 0; i < ctx.Roster.Count; i++) if (ctx.Live(i, out var m)) { sum += ctx.At(m); k++; }
                p.Point = sum / k;
            }
            for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity); i++) p.Down[i] = 0;
            return PlayStart.Go;
        }

        public void Update(ref AssembleAfterDropParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity); i++)
                if (p.Down[i] == 0 && ctx.Live(i, out var m) && AssembleAfterDropBehavior.IsDown(ctx.World, m)) { p.Down[i] = 1; ws.Reissue = 1; }
        }

        public NodeStatus Outcome(ref AssembleAfterDropParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(ctx.World);
            bool done = true;
            for (int i = 0; i < Math.Min(ctx.Roster.Count, UnitRoster.Capacity) && done; i++)
                if (ctx.Live(i, out var m)
                    && (p.Down[i] == 0 || !AirborneDrill.AtSpot(ctx.World, m, AirborneDrill.RingSpot(terrain, p.Point, i, ctx.Roster.Count, p.RingRadius))))
                    done = false;
            if (done) return NodeStatus.Success;
            return p.TimeoutSeconds > 0f && ctx.Now - ws.StartedAt >= p.TimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
        }

        public bool PhaseComplete(ref AssembleAfterDropParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref AssembleAfterDropParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref AssembleAfterDropParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            if (member >= UnitRoster.Capacity || p.Down[member] == 0)
                return new MemberOrder { Kind = MemberOrderKind.Stay, RoleId = AssembleAfterDropManeuver.RoleDescending };
            var spot = AirborneDrill.RingSpot(Fdp.Toolkit.World.WorldQuery.Of(ctx.World), p.Point, member, ctx.Roster.Count, p.RingRadius);
            return new MemberOrder { Kind = MemberOrderKind.Watch, Point = spot, Speed = 3f, ArrivalRadius = 1f, RoleId = AssembleAfterDropManeuver.RoleAssembling };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }
}

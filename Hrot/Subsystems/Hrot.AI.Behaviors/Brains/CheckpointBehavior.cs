using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-4622</c> K-4 — a checkpoint as the commander holds it (resolved from <see cref="CheckpointParamsJsonDto"/>; the site once the
    /// approach route is known).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CheckpointParams
    {
        public int ApproachNet;
        public Vector3 SearchBay;
        public float StandOff;
        public long ExpectedCharge;
        public float WarningSeconds;
        public float Speed;
        /// <summary>1 once <see cref="Site"/> is built from the route.</summary>
        public byte Ready;
        public ApproachSite Site;
        public Vector3 Bay;
        /// <summary>Each roster member's role (<see cref="CheckpointManeuver"/>'s role ids), by roster index.</summary>
        public fixed byte Roles[Fdp.Core.CommandHierarchy.UnitRoster.Capacity];
        /// <summary>Each member's place among the men of his role (0, 1, …).</summary>
        public fixed byte Slot[Fdp.Core.CommandHierarchy.UnitRoster.Capacity];
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-4 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-1, CP-1a, CP-2) — <c>Checkpoint</c> on a squad's commander: the
    /// squad mans a vehicle checkpoint on the approach route's end and keeps the <see cref="VehicleApproachWatch"/> on it (into its own
    /// <see cref="CheckpointWatch"/>). It holds; it never ends on its own. ⛔ It fires no shot and commits no act — the ladder (K-5) answers the watch.
    /// </summary>
    public static class CheckpointBehavior
    {
        public const float DefaultWarningSeconds = 15f;
        /// <summary>CP-1a — the stand-off is this many times the expected charge's lethal blast radius, within [<see cref="MinStandOffM"/>, <see cref="MaxStandOffM"/>].</summary>
        public const float StandOffPerLethalRadius = 2f;
        public const float MinStandOffM = 25f, MaxStandOffM = 150f, DefaultStandOffM = 50f;
        /// <summary>The posts beside the lane (m off its axis).</summary>
        public const float StopperOffsetM = 4f, BayOffsetM = 6f, BayBeyondM = 8f, RearOffsetM = 6f;

        [StructLayout(LayoutKind.Sequential)]
        public struct CheckpointBlackboard
        {
            public CheckpointParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.Checkpoint)]
        public static void Resolve(in CheckpointParamsJsonDto authored, ref CheckpointBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new CheckpointParams
            {
                ApproachNet = authored.Approach.Ref.NetworkId, SearchBay = authored.SearchBay, StandOff = authored.StandOff,
                ExpectedCharge = authored.ExpectedCharge, WarningSeconds = authored.WarningSeconds > 0f ? authored.WarningSeconds : DefaultWarningSeconds,
                Speed = authored.Speed,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.Checkpoint, Params = typeof(CheckpointParamsJsonDto), Categories = BehaviorCategory.Commander,
            Description = "A squad mans a vehicle checkpoint at the end of an approach route: a stopper, two searchers, the machine gun and the reaction, watching who comes in.")]
        [BTreeDefinition(BehaviorNames.Checkpoint, Curated = true, Group = "Library/Security", ParamsType = typeof(CheckpointBlackboard))]
        public static BTreeBuilder<CheckpointBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CheckpointBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref CheckpointParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<CheckpointPlay, CheckpointParams>(ref p, ref ws, self, world);

        [ThreadStatic] private static List<NavRouteWaypoint>? t_points;
        private static readonly Hrot.Map.Common.Components.HrotRouteSource s_routes = new();

        /// <summary>CP-1 — the site from the approach route: its last point the stop line, its last leg the inbound direction. False while the
        /// route is not known here.</summary>
        public static bool TrySite(EntityRepository world, int approachNet, out Vector3 stop, out Vector2 inbound)
        {
            stop = default; inbound = default;
            var points = t_points ??= new List<NavRouteWaypoint>(16);
            if (approachNet == 0 || !s_routes.TryGetRoute(world, approachNet, points, out _, out _) || points.Count < 2) return false;
            stop = points[^1].Position;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                var d = new Vector2(stop.X - points[i].Position.X, stop.Y - points[i].Position.Y);
                if (d.LengthSquared() > 1f) { inbound = Vector2.Normalize(d); return true; }
            }
            return false;
        }

        /// <summary>CP-1a — the stand-off: authored, else <see cref="StandOffPerLethalRadius"/> × the expected charge's lethal blast radius (the TKB
        /// warhead), within [25, 150] m; 50 m when the charge has no warhead here.</summary>
        public static float StandOffFor(EntityRepository world, float authored, long charge)
        {
            if (authored > 0f) return authored;
            var db = world.HasSingleton<Fdp.Interfaces.ITkbDatabase>() ? world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null;
            var w = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.WarheadOnly(db, charge != 0 ? charge : Hrot.Core.Tkb.MunitionTkbCatalog.TkbExplosiveCharge);
            if (w == null || w.BlastLethalRadiusM <= 0f) return DefaultStandOffM;
            return Math.Clamp(w.BlastLethalRadiusM * StandOffPerLethalRadius, MinStandOffM, MaxStandOffM);
        }

        /// <summary>A member carrying a machine gun (any of his first four mounts).</summary>
        public static bool CarriesMachineGun(EntityRepository world, Entity man)
        {
            for (int k = 0; k < 4; k++)
            {
                var m = CombatTkb.MountOf(world, man, k);
                if (m == null) break;
                if (m.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun) return true;
            }
            return false;
        }

        /// <summary>Compass bearing (0 = north, clockwise) of a flat direction.</summary>
        public static float Bearing(Vector2 d) { float b = MathF.Atan2(d.X, d.Y) * (180f / MathF.PI); return b < 0f ? b + 360f : b; }
    }

    /// <summary>⭐ <c>CE-4622</c> K-4 — the checkpoint as a play over <see cref="CheckpointManeuver"/>.</summary>
    public unsafe struct CheckpointPlay : ISquadPlay<CheckpointParams>
    {
        private static readonly SquadHsmShell s_shell = new(CheckpointManeuver.BuildTransitionTable(), CheckpointManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => CheckpointManeuver.ManeuverKind;
        public ushort StartPhase => CheckpointManeuver.PhaseManning;
        public ushort AbortPhase => CheckpointManeuver.PhaseAborted;
        public byte ElementCount => 1;

        /// <summary>The site (once the route is known here), the stand-off, the bay and each man's role: the machine gun's man is the overwatch (the
        /// first such, else the last live man), the first other live man the stopper, the next two the searchers, the rest the reaction.</summary>
        public PlayStart Start(ref CheckpointParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            if (!SquadManeuverRunner.AnyLive(in ctx)) return PlayStart.Fail;
            if (p.Ready == 0)
            {
                if (!CheckpointBehavior.TrySite(ctx.World, p.ApproachNet, out var stop, out var inbound)) return PlayStart.Wait;
                p.Site = new ApproachSite { Stop = stop, Inbound = inbound, StandOffM = CheckpointBehavior.StandOffFor(ctx.World, p.StandOff, p.ExpectedCharge),
                                            WarningSeconds = p.WarningSeconds };
                var right = new Vector3(inbound.Y, -inbound.X, 0f);
                var ahead = new Vector3(inbound.X, inbound.Y, 0f);
                p.Bay = p.SearchBay != Vector3.Zero ? p.SearchBay : stop + ahead * CheckpointBehavior.BayBeyondM + right * CheckpointBehavior.BayOffsetM;
                p.Ready = 1;
            }
            int overwatch = -1, last = -1;
            for (int i = 0; i < ctx.Roster.Count && i < Fdp.Core.CommandHierarchy.UnitRoster.Capacity; i++)
            {
                p.Roles[i] = 0; p.Slot[i] = 0;
                if (!ctx.Live(i, out var m)) continue;
                last = i;
                if (overwatch < 0 && CheckpointBehavior.CarriesMachineGun(ctx.World, m)) overwatch = i;
            }
            if (overwatch < 0) overwatch = last;
            byte searchers = 0, reaction = 0; bool stopper = false;
            for (int i = 0; i < ctx.Roster.Count && i < Fdp.Core.CommandHierarchy.UnitRoster.Capacity; i++)
            {
                if (!ctx.Live(i, out _)) continue;
                if (i == overwatch) { p.Roles[i] = CheckpointManeuver.RoleOverwatch; continue; }
                if (!stopper) { p.Roles[i] = CheckpointManeuver.RoleStopper; stopper = true; continue; }
                if (searchers < 2) { p.Roles[i] = CheckpointManeuver.RoleSearcher; p.Slot[i] = searchers++; continue; }
                p.Roles[i] = CheckpointManeuver.RoleReaction; p.Slot[i] = reaction++;
            }
            return PlayStart.Go;
        }

        /// <summary>CP-2 — the watch: every live man's eyes, the most urgent vehicle into the commander's own <see cref="CheckpointWatch"/>; a vehicle
        /// newly judged a threat (or newly stopped as routine) is counted once.</summary>
        public void Update(ref CheckpointParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (p.Ready == 0) return;
            var world = ctx.World;
            var watch = world.HasComponent<CheckpointWatch>(ctx.Commander) ? world.GetComponentRO<CheckpointWatch>(ctx.Commander) : default;
            var now = watch;
            now.Vehicle = 0; now.Verdict = 0; now.SecondsToStandOff = 9999f; now.Indicators = 0;
            for (int i = 0; i < ctx.Roster.Count; i++)
                if (ctx.Live(i, out var m)) VehicleApproachWatch.Scan(world, m, in p.Site, ref now);
            if (now.Verdict == (byte)ApproachVerdict.Threat && now.Vehicle != now.LastThreat) { now.Threats++; now.LastThreat = now.Vehicle; }
            if (now.Verdict == (byte)ApproachVerdict.Routine && now.Vehicle != now.LastRoutine) { now.Routines++; now.LastRoutine = now.Vehicle; }
            ws.ThreatId = now.Verdict == (byte)ApproachVerdict.Threat ? now.Vehicle : 0;
            if (world.HasComponent<CheckpointWatch>(ctx.Commander)) world.GetComponentRW<CheckpointWatch>(ctx.Commander) = now;
            else world.AddComponent(ctx.Commander, now);   // added WITH its final value (the parallel rule)
        }

        public NodeStatus Outcome(ref CheckpointParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
            => SquadManeuverRunner.AnyLive(in ctx) ? NodeStatus.Running : NodeStatus.Failure;

        public bool PhaseComplete(ref CheckpointParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref CheckpointParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        /// <summary>CP-1 — each man to his post, watching the approach (his arc toward where traffic comes from).</summary>
        public MemberOrder OrderFor(ref CheckpointParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            var inbound = p.Site.Inbound;
            var right = new Vector3(inbound.Y, -inbound.X, 0f);
            var ahead = new Vector3(inbound.X, inbound.Y, 0f);
            var stop = p.Site.Stop;
            byte role = member < Fdp.Core.CommandHierarchy.UnitRoster.Capacity ? p.Roles[member] : (byte)0;
            byte slot = member < Fdp.Core.CommandHierarchy.UnitRoster.Capacity ? p.Slot[member] : (byte)0;
            var rear = stop + ahead * p.Site.StandOffM;
            Vector3 post = role switch
            {
                CheckpointManeuver.RoleStopper => stop + right * CheckpointBehavior.StopperOffsetM,
                CheckpointManeuver.RoleSearcher => p.Bay + ahead * (slot == 0 ? -2f : 2f),
                CheckpointManeuver.RoleOverwatch => rear + right * CheckpointBehavior.RearOffsetM,
                _ => rear - right * (CheckpointBehavior.RearOffsetM + 3f * slot),
            };
            return new MemberOrder
            {
                Kind = MemberOrderKind.Watch, Point = post, Speed = p.Speed, ArrivalRadius = 1.5f, Post = new MemberPost { Set = 1, Radius = 2f },
                Sector = MemberArc.Around(CheckpointBehavior.Bearing(-inbound), role == CheckpointManeuver.RoleOverwatch ? 30f : 60f),
                RoleId = role == 0 ? CheckpointManeuver.RoleReaction : role,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }
}

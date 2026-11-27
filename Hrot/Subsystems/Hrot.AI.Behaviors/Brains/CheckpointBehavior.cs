using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
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

        private static readonly int s_hash = Fdp.Toolkit.Behavior.BehaviorHash.FromName(BehaviorNames.Checkpoint);

        /// <summary>
        /// ⭐ <c>CE-4622</c> K-7 — <paramref name="unit"/>'s current order (running or paused) is a <c>Checkpoint</c>. The squad SOP spares it as it spares a
        /// defence (<see cref="SquadDefendBehavior.NotDefending"/>): its watch and ladder ARE the answer to a vehicle coming in. 📐 Measured on
        /// <c>cp-routine-car</c>: the default <c>BasicSquadSop</c>'s contact row paused the checkpoint for a fire-and-movement drill 6.5 s in, the watch froze
        /// at "approaching", and nobody signalled or searched.
        /// </summary>
        public static bool Manning(EntityRepository world, Entity unit)
        {
            var o = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, unit);
            return o.Exists && o.BehaviorHash == s_hash;
        }

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

        /// <summary>⭐ K-5 — the ladder's fire: rounds per warning shot, per disabling burst, and how often the burst is re-aimed (s).</summary>
        public const int WarningRounds = 3, DisablingRounds = 8;
        /// <summary>⭐ K-5 (R-2132) — a signalled vehicle still at this fraction of its closing speed at the warning-shot time ignored the signal.</summary>
        public const float IgnoredFraction = 0.8f;
        public const float RefireSeconds = 1.5f;
        /// <summary>⭐ K-5 — a warning shot lands this far ahead of the vehicle (m), on the ground.</summary>
        public const float WarningAheadM = 15f;

        /// <summary>⭐ K-5 — the ground <see cref="WarningAheadM"/> ahead of a vehicle at <paramref name="car"/> on the approach.</summary>
        public static Vector3 GroundAhead(Vector3 car, Vector2 inbound) => new(car.X + inbound.X * WarningAheadM, car.Y + inbound.Y * WarningAheadM, car.Z);

        /// <summary>⭐ K-5 — the live man in the stopper's role, if any.</summary>
        public static unsafe Entity Stopper(ref CheckpointParams p, in SquadPlayContext ctx)
        {
            for (int i = 0; i < ctx.Roster.Count && i < Fdp.Core.CommandHierarchy.UnitRoster.Capacity; i++)
                if (p.Roles[i] == CheckpointManeuver.RoleStopper && ctx.Live(i, out var m)) return m;
            return Entity.Null;
        }

        /// <summary>⭐ K-5 (CP-3 SIGNAL) — the stopper shows the signal (<see cref="GestureKind.Warning"/>) or drops it — his component, so after the
        /// brains' phase when inside it.</summary>
        public static void Signal(EntityRepository world, Entity stopper, bool on)
        {
            if (ParallelReadCheck.Defers(world)) { ParallelReadCheck.CurrentBatch!.Later(SignalAfterPhase.Instance, stopper, on ? 1 : 0, 0L); return; }
            Gestures.Set(world, stopper, on ? GestureKind.Warning : GestureKind.None);
        }

        private sealed class SignalAfterPhase : Fdp.Core.Jobs.IPhaseFollowUp
        {
            public static readonly SignalAfterPhase Instance = new();
            public void Apply(EntityRepository repo, Entity e, int code, long arg) => Gestures.Set(repo, e, code != 0 ? GestureKind.Warning : GestureKind.None);
        }

        /// <summary>⭐ K-6 — the live man in role <paramref name="role"/> with slot <paramref name="slot"/>, if any.</summary>
        public static unsafe Entity InRole(ref CheckpointParams p, in SquadPlayContext ctx, byte role, byte slot)
        {
            for (int i = 0; i < ctx.Roster.Count && i < Fdp.Core.CommandHierarchy.UnitRoster.Capacity; i++)
                if (p.Roles[i] == role && p.Slot[i] == slot && ctx.Live(i, out var m)) return m;
            return Entity.Null;
        }

        /// <summary>⭐ K-6 (CP-4) — one tick of the search (stages in <see cref="CheckpointWatch.SearchStage"/>). Every ask is an event (custody, the
        /// reaction to each man) — nothing here writes another entity.</summary>
        public static unsafe void Search(ref CheckpointParams p, ref CheckpointWatch now, ref SquadPlayState ws, in SquadPlayContext ctx, Entity vehicle, VehicleRung rung)
        {
            var world = ctx.World;
            var searched = now.SearchVehicle != 0 ? new Entity((ulong)now.SearchVehicle) : Entity.Null;
            if (now.SearchStage == 0)
            {
                if (vehicle.IsNull || rung >= VehicleRung.WarningShot || now.Verdict != (byte)ApproachVerdict.Routine || now.Vehicle == now.LastSearched
                    || !Fdp.Toolkit.Civilians.CustodyRules.IsStoppedVehicle(world, vehicle)) return;
                now.SearchVehicle = now.Vehicle; now.SearchStage = 1; now.OccupantCount = 0; now.ConcealedAtDemand = 0;
                if (world.HasComponent<PassengerBuffer>(vehicle))
                {
                    var buffer = world.GetComponentRO<PassengerBuffer>(vehicle);
                    for (int i = 0; i < PassengerBuffer.Capacity; i++)
                    {
                        var thing = buffer.Passengers[i];
                        if (thing.IsNull) continue;
                        if (CarryStations.IsConcealed(world, thing)) { now.ConcealedAtDemand++; continue; }
                        if (Fdp.Toolkit.Combat.Items.IsMountable(world, thing) || now.OccupantCount >= 4) continue;
                        now.Occupants[now.OccupantCount++] = (long)thing.PackedValue;
                        world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent
                        {
                            Entity = thing, IntentId = BehaviorNames.ComplyAtCheckpoint, JsonParams = "{}", Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Reaction,
                        });
                    }
                }
                ws.Reissue = 1;
                return;
            }
            if (searched.IsNull || !world.IsAlive(searched)) { now.SearchStage = 0; now.SearchVehicle = 0; ws.Reissue = 1; return; }
            if (now.SearchStage >= 2)
            {
                if (!Fdp.Toolkit.Civilians.CustodyRules.IsStoppedVehicle(world, searched)) { now.SearchStage = 0; now.SearchVehicle = 0; ws.Reissue = 1; }   // it drove off
                return;
            }
            var men = InRole(ref p, in ctx, CheckpointManeuver.RoleSearcher, 0);
            var boot = InRole(ref p, in ctx, CheckpointManeuver.RoleSearcher, 1);
            if (boot.IsNull) boot = men;
            // the men's searcher goes to the first man OUT not yet searched — re-sent when that man changes (📐 cp-routine-car: sent once, while the
            //   driver was still aboard, he stood at the car's other side and the man's search aborted)
            long next = 0;
            for (int k = 0; k < now.OccupantCount && next == 0; k++)
            {
                var man = new Entity((ulong)now.Occupants[k]);
                if (world.IsAlive(man) && !IsAboard(world, man) && !Searched(world, man)) next = now.Occupants[k];
            }
            // ⭐ K-7 — and when that man has MOVED more than 1 m since (📐 cp-routine-car: counted out while still at the car's point, he stepped 5 m to
            //   the side and the searcher waited where he had been)
            var nextAt = next != 0 ? world.GetComponentRO<SimTransform>(new Entity((ulong)next)).Position : default;
            if (next != now.SearchMan || (next != 0 && Vector2.DistanceSquared(new Vector2(nextAt.X, nextAt.Y), new Vector2(now.SearchManX, now.SearchManY)) > 1f))
            {
                now.SearchMan = next; now.SearchManX = nextAt.X; now.SearchManY = nextAt.Y; ws.Reissue = 1;
            }
            bool allMen = true;
            for (int k = 0; k < now.OccupantCount; k++)
            {
                var man = new Entity((ulong)now.Occupants[k]);
                if (!world.IsAlive(man)) continue;
                if (Searched(world, man)) continue;
                allMen = false;
                if (!men.IsNull && !Searching(world, man) && Fdp.Toolkit.Civilians.CustodyRules.Complies(world, man)
                    && Fdp.Toolkit.Civilians.CustodyRules.InReach(world, man, men))
                    Fdp.Toolkit.Civilians.CustodyRules.Ask(world, man, men, Fdp.Toolkit.Civilians.CustodyVerb.Search);
            }
            bool car = Searched(world, searched);
            if (!car && !boot.IsNull && !Searching(world, searched) && Fdp.Toolkit.Civilians.CustodyRules.InReach(world, searched, boot))
                Fdp.Toolkit.Civilians.CustodyRules.Ask(world, searched, boot, Fdp.Toolkit.Civilians.CustodyVerb.Search);
            if (!allMen || !car) return;
            now.Found += now.ConcealedAtDemand;
            now.LastSearched = now.SearchVehicle;
            if (now.ConcealedAtDemand > 0)
            {
                now.SearchStage = 3; now.Held++;
                for (int k = 0; k < now.OccupantCount; k++)
                    if (!men.IsNull) Fdp.Toolkit.Civilians.CustodyRules.Ask(world, new Entity((ulong)now.Occupants[k]), men, Fdp.Toolkit.Civilians.CustodyVerb.Detain);
            }
            else
            {
                now.SearchStage = 2; now.Released++;
                int net = world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(searched)
                    ? world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(searched).Value : 0;
                for (int k = 0; k < now.OccupantCount; k++)
                    world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent
                    {
                        // ⭐ the release is an INSTRUCTION (Superior): his "out, hands up" is a running reaction held until the checkpoint ends it, and a
                        //   reaction yields only to a more urgent one — 📐 sent as a reaction (and keyed "Vehicle", Embark reads "vehicle") it was refused and
                        //   every released man stood beside his car for good (cp-routine-car's golden: released ~50 s, still out at 90 s)
                        Entity = new Entity((ulong)now.Occupants[k]), IntentId = BehaviorNames.Embark, JsonParams = "{\"vehicle\":" + net + "}",
                        Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior,
                    });
            }
            ws.Reissue = 1;
        }

        /// <summary>Is <paramref name="man"/> aboard a vehicle?</summary>
        public static bool IsAboard(EntityRepository world, Entity man)
            => world.IsComponentTypeRegistered<IsEmbarkedTag>() && world.HasComponent<IsEmbarkedTag>(man);

        private static bool Searched(EntityRepository world, Entity e)
            => world.HasComponent<Fdp.Toolkit.Civilians.Custody>(e) && world.GetComponentRO<Fdp.Toolkit.Civilians.Custody>(e).Searched != 0;

        private static bool Searching(EntityRepository world, Entity e)
            => world.HasComponent<Fdp.Toolkit.Civilians.Custody>(e) && world.GetComponentRO<Fdp.Toolkit.Civilians.Custody>(e).Searching != 0;

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
            // ⭐ K-5 (CP-2, R-2132) — IGNORED THE SIGNAL: a vehicle signalled at the warning line that has not slowed below IgnoredFraction of its
            //   speed then by the warning-shot time is a THREAT (the brake test alone names a fast car only ~2 s out — too late for any rung but lethal)
            if (now.Vehicle != 0 && now.Vehicle != now.SignalledVehicle && now.SecondsToStandOff <= p.Site.WarningSeconds
                && now.Verdict == (byte)ApproachVerdict.Approaching)
            {
                now.SignalledVehicle = now.Vehicle; now.ClosingAtSignal = now.ClosingMps;
                // ⭐ R-2133's ⏭ — the signal ASKS the vehicle to stop at the line: a REACTION (an event, never a write — the reaction rules decide: a
                //   vehicle whose ROE keeps it on its task never takes it; one under a RAM order ignores it); a civilian slows and stops (StopAtSignalBehavior)
                world.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent
                {
                    Entity = new Entity((ulong)now.Vehicle), IntentId = BehaviorNames.StopAtSignal, Origin = Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Reaction,
                    JsonParams = "{\"X\":" + p.Site.Stop.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                               + ",\"Y\":" + p.Site.Stop.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "}",
                });
            }
            if (now.Verdict == (byte)ApproachVerdict.Approaching && now.Vehicle == now.SignalledVehicle
                && now.SecondsToStandOff <= p.Site.WarningSeconds * VehicleLadder.WarningShotAt
                && now.ClosingMps >= now.ClosingAtSignal * CheckpointBehavior.IgnoredFraction && now.ClosingMps >= VehicleApproachWatch.RoutineMps)
            {
                now.Verdict = (byte)ApproachVerdict.Threat;
                if (now.Vehicle != now.LastThreat) { now.Threats++; now.LastThreat = now.Vehicle; }
            }
            // ⭐ K-5 (CP-3) — the ladder: ONE rung for the squad, by the vehicle's time to the stand-off
            var vehicle = now.Vehicle != 0 ? new Entity((ulong)now.Vehicle) : Entity.Null;
            var current = now.RungVehicle == now.Vehicle ? (VehicleRung)now.Rung : VehicleRung.Quiet;
            bool bike = !vehicle.IsNull && VehicleApproachWatch.IsTwoWheeler(world, vehicle);
            bool selfDefence = !vehicle.IsNull && Fdp.Toolkit.Civilians.HostileActs.Of(world, vehicle) == Fdp.Toolkit.Civilians.HostileActKind.Lethal;
            var due = VehicleLadder.Due((ApproachVerdict)now.Verdict, now.SecondsToStandOff, p.Site.WarningSeconds, bike,
                                        Fdp.Toolkit.Behavior.Components.RoeOf.ForceLevel(world, ctx.Commander), selfDefence, current);
            if (due != current || now.RungVehicle != now.Vehicle) ws.Reissue = 1;
            else if (due >= VehicleRung.Disabling && ctx.Now - ws.IssuedAt >= CheckpointBehavior.RefireSeconds) ws.Reissue = 1;   // the aim moves: fresh orders
            now.Rung = (byte)due; now.RungVehicle = now.Vehicle;
            if ((byte)due > now.MaxRung) now.MaxRung = (byte)due;
            byte signal = due >= VehicleRung.Signal ? (byte)1 : (byte)0;
            if (signal != now.Signalling)
            {
                now.Signalling = signal;
                var stopper = CheckpointBehavior.Stopper(ref p, in ctx);
                if (!stopper.IsNull) CheckpointBehavior.Signal(world, stopper, signal != 0);
            }
            if (!vehicle.IsNull && world.HasComponent<SimTransform>(vehicle)) ws.ThreatPoint = world.GetComponentRO<SimTransform>(vehicle).Position;
            // ⭐ K-6 (CP-4) — the SEARCH of a vehicle stopped at the line: "out, hands up" (a reaction to each man aboard), the men searched by one
            //   searcher, the vehicle (its concealed stations) by the other; nothing found ⇒ they board again and go; something found ⇒ held
            CheckpointBehavior.Search(ref p, ref now, ref ws, in ctx, vehicle, due);
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
            // ⭐ K-6 — during a search the searchers go to it: one to the next man out not yet searched, the other to the vehicle's side
            if (role == CheckpointManeuver.RoleSearcher && ctx.World.HasComponent<CheckpointWatch>(ctx.Commander))
            {
                var w = ctx.World.GetComponentRO<CheckpointWatch>(ctx.Commander);
                var sv = w.SearchVehicle != 0 ? new Entity((ulong)w.SearchVehicle) : Entity.Null;
                if (w.SearchStage == 1 && !sv.IsNull && ctx.World.IsAlive(sv) && ctx.World.HasComponent<SimTransform>(sv))
                {
                    Vector3 to = ctx.World.GetComponentRO<SimTransform>(sv).Position + right * 2.5f;
                    var next = w.SearchMan != 0 ? new Entity((ulong)w.SearchMan) : Entity.Null;
                    if (slot == 0 && !next.IsNull && ctx.World.IsAlive(next) && ctx.World.HasComponent<SimTransform>(next))
                    {
                        // ⭐ K-7 — to ARM'S LENGTH of the man (1.2 m on the searcher's own side, 0.4 m arrival — outside the crowd's push, inside custody's 2 m reach): 📐 cp-routine-car — sent onto him with a
                        //   watch post (1 m arrival) he stood ~1.9 m off, outside custody's 1.5 m reach, and the search aborted and restarted for good
                        var man = ctx.World.GetComponentRO<SimTransform>(next).Position;
                        var from = new Vector2(at.X - man.X, at.Y - man.Y);
                        var dir = from.LengthSquared() > 1e-4f ? Vector2.Normalize(from) : new Vector2(right.X, right.Y);
                        return new MemberOrder { Kind = MemberOrderKind.AdvanceTo, Point = new Vector3(man.X + dir.X * 1.2f, man.Y + dir.Y * 1.2f, man.Z),
                                                 Speed = p.Speed, ArrivalRadius = 0.4f, RoleId = role };
                    }
                    return new MemberOrder { Kind = MemberOrderKind.Watch, Point = to, Speed = p.Speed, ArrivalRadius = 1f, Post = new MemberPost { Set = 1, Radius = 1f },
                                             RoleId = role };
                }
            }
            Vector3 post = role switch
            {
                CheckpointManeuver.RoleStopper => stop + right * CheckpointBehavior.StopperOffsetM,
                CheckpointManeuver.RoleSearcher => p.Bay + ahead * (slot == 0 ? -2f : 2f),
                // the MG on the FAR side of the lane from the stopper and the searchers, so none of them stands on its line to the road (📐 cp-vbied:
                //   on the stopper's side the burst was held — a direct round never flies through a friend)
                CheckpointManeuver.RoleOverwatch => rear - right * CheckpointBehavior.RearOffsetM,
                _ => rear + right * (CheckpointBehavior.RearOffsetM + 3f * slot),
            };
            // ⭐ K-5 (CP-3) — the rung's fire: the warning shot into the ground ahead and disabling fire at the engine are the MACHINE GUN's;
            //   lethal fire at the vehicle is everyone's
            var watch = ctx.World.HasComponent<CheckpointWatch>(ctx.Commander) ? ctx.World.GetComponentRO<CheckpointWatch>(ctx.Commander) : default;
            var rung = (VehicleRung)watch.Rung;
            var target = watch.RungVehicle != 0 ? new Entity((ulong)watch.RungVehicle) : Entity.Null;
            if (!target.IsNull && ctx.World.IsAlive(target) && ctx.World.HasComponent<SimTransform>(target))
            {
                var car = ctx.World.GetComponentRO<SimTransform>(target).Position;
                byte r = role == 0 ? CheckpointManeuver.RoleReaction : role;
                if (rung == VehicleRung.Lethal)
                    return new MemberOrder { Kind = MemberOrderKind.Suppress, Target = target, Point = car, Speed = p.Speed, RoleId = r };
                if (role == CheckpointManeuver.RoleOverwatch && rung == VehicleRung.WarningShot)
                    return new MemberOrder { Kind = MemberOrderKind.FireAtPoint, Point = CheckpointBehavior.GroundAhead(car, inbound), Rounds = CheckpointBehavior.WarningRounds,
                                             Arc = 1, Speed = p.Speed, RoleId = r };
                if (role == CheckpointManeuver.RoleOverwatch && rung == VehicleRung.Disabling)
                    return new MemberOrder { Kind = MemberOrderKind.FireAtPoint, Point = AimPoint.Engine(ctx.World, target) ?? car, Rounds = CheckpointBehavior.DisablingRounds,
                                             Arc = 1, Speed = p.Speed, RoleId = r };
            }
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

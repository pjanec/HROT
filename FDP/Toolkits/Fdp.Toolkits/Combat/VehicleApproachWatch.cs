using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-4622</c> K-4 (CP-2) — what the checkpoint makes of one vehicle on its approach.</summary>
    public enum ApproachVerdict : byte
    {
        /// <summary>Nothing on the approach.</summary>
        None = 0,
        /// <summary>Coming in, still beyond the warning line (or able to stop at the stop line with an ordinary brake).</summary>
        Approaching = 1,
        /// <summary>Slow (under <see cref="VehicleApproachWatch.RoutineMps"/>) near the stop line, or stopped at it: a routine car.</summary>
        Routine = 2,
        /// <summary>Past the warning line and too fast to stop at the stop line with an ordinary brake — or past the stop line still moving fast.</summary>
        Threat = 3,
    }

    /// <summary>⭐ <c>CE-4622</c> K-4 (CP-2) — the indicators that raise the watch on a vehicle (a bit set).</summary>
    [Flags]
    public enum ApproachIndicator : byte
    {
        /// <summary>No indicator (⚠ not "None": the DDS code generator puts every enum of a file in one IDL scope).</summary>
        Plain = 0,
        /// <summary>IM-8 — two men on one bike (a two-wheeler whose second seat is taken).</summary>
        TwoUpBike = 1,
    }

    /// <summary>
    /// ⭐ <c>CE-4622</c> K-4 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-2, §4) — the checkpoint's site as the watch sees it: the STOP
    /// line's point, the INBOUND direction (the way a vehicle travels toward it), the STAND-OFF (the stop line lies this far in front of the protected men,
    /// so reaching the stop line is reaching the stand-off) and the WARNING time (the warning line is where a vehicle is this many seconds out).
    /// </summary>
    public struct ApproachSite
    {
        public Vector3 Stop;
        /// <summary>Unit, flat: the direction a vehicle on the approach travels.</summary>
        public Vector2 Inbound;
        public float StandOffM;
        public float WarningSeconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-4 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-2, §4; KV-2 lines in TIME, KV-4/KV-5 no act) — the checkpoint's
    /// own judgement of the vehicles on its approach. ⛔ It commits NO hostile act (a watch is suspicion, not evidence): it says which vehicle matters most
    /// and how many SECONDS it is from the stand-off. A vehicle's distance to the stop line along the approach over its closing speed is its time; the
    /// warning line is <see cref="ApproachSite.WarningSeconds"/> out. Past it, a vehicle that cannot stop at the stop line with an ordinary brake
    /// (<see cref="ThreatDecelMps2"/>) is a THREAT; slow near the line, ROUTINE. The vehicles are the ones the squad's men REMEMBER seeing (their
    /// <see cref="TargetMemory"/> — at night the watch is bounded by what they can see), their motion as this node knows it.
    /// </summary>
    public static unsafe class VehicleApproachWatch
    {
        /// <summary>CP-2 — under this (m/s) by the stop line is routine.</summary>
        public const float RoutineMps = 3f;
        /// <summary>The deceleration (m/s²) above which stopping at the stop line is no ordinary brake: harder than a driver brakes for a checkpoint (~0.4 g).</summary>
        public const float ThreatDecelMps2 = 4f;
        /// <summary>A vehicle within this of the stop line (m, either side) and slow is AT the line.</summary>
        public const float AtLineM = 12f;
        /// <summary>How far out (s) a vehicle is watched at all: beyond twice the warning time it is just traffic.</summary>
        public const float WatchFactor = 2f;
        /// <summary>Under this closing speed (m/s) a vehicle is not coming in.</summary>
        public const float ClosingMinMps = 0.5f;

        /// <summary>
        /// CP-2 — <paramref name="position"/> and <paramref name="velocity"/> judged against <paramref name="site"/>: the verdict and the seconds to the
        /// stand-off (∞ when it is not closing; 0 when it is there or past).
        /// </summary>
        public static ApproachVerdict Judge(in ApproachSite site, Vector3 position, Vector3 velocity, out float secondsToStandOff)
            => Judge(in site, position, velocity, out secondsToStandOff, out _);

        /// <inheritdoc cref="Judge(in ApproachSite, Vector3, Vector3, out float)"/>
        public static ApproachVerdict Judge(in ApproachSite site, Vector3 position, Vector3 velocity, out float secondsToStandOff, out float closingMps)
        {
            var rel = new Vector2(site.Stop.X - position.X, site.Stop.Y - position.Y);
            float along = Vector2.Dot(rel, site.Inbound);                       // metres still to go to the stop line (negative: past it)
            float lateral = MathF.Abs(rel.X * site.Inbound.Y - rel.Y * site.Inbound.X);
            float closing = Vector2.Dot(new Vector2(velocity.X, velocity.Y), site.Inbound);
            closingMps = closing;
            float speed = new Vector2(velocity.X, velocity.Y).Length();
            secondsToStandOff = along <= 0f ? 0f : closing > ClosingMinMps ? along / closing : float.PositiveInfinity;
            if (lateral > MathF.Max(AtLineM, along * 0.5f) + 20f) return ApproachVerdict.None;   // off to the side: not on this approach
            if (along < -site.StandOffM) return ApproachVerdict.None;                           // through and beyond the post
            if (along <= 0f) return speed >= RoutineMps ? ApproachVerdict.Threat : ApproachVerdict.Routine;
            if (closing <= ClosingMinMps)
                return along <= AtLineM ? ApproachVerdict.Routine : ApproachVerdict.None;   // standing (or leaving): at the line it is a stopped car
            if (secondsToStandOff > site.WarningSeconds * WatchFactor) return ApproachVerdict.None;
            if (closing < RoutineMps && along <= site.StandOffM) return ApproachVerdict.Routine;
            if (secondsToStandOff > site.WarningSeconds) return ApproachVerdict.Approaching;     // not yet at the warning line
            float needed = closing * closing / (2f * MathF.Max(along, 0.1f));               // the brake it needs to stop at the line
            return needed > ThreatDecelMps2 ? ApproachVerdict.Threat : ApproachVerdict.Approaching;
        }

        /// <summary>CP-2 indicators of <paramref name="vehicle"/> (IM-8: two men on one bike).</summary>
        public static ApproachIndicator Indicators(EntityRepository world, Entity vehicle)
        {
            var flags = ApproachIndicator.Plain;
            if (world.IsComponentTypeRegistered<PassengerBuffer>() && world.HasComponent<PassengerBuffer>(vehicle)
                && world.GetComponentRO<PassengerBuffer>(vehicle).Count >= 2 && IsTwoWheeler(world, vehicle))
                flags |= ApproachIndicator.TwoUpBike;
            return flags;
        }

        /// <summary>A two-wheeler: its first seat is ridden astride (<see cref="Fdp.Toolkit.Tkb.Domain.SeatPose.Ride"/>).</summary>
        public static bool IsTwoWheeler(EntityRepository world, Entity vehicle)
            => CombatTkb.TryGetTemplate(world, vehicle, out var t) && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>() is { Seats.Count: > 0 } c
               && c.Seats[0].Pose == Fdp.Toolkit.Tkb.Domain.SeatPose.Ride;

        /// <summary>
        /// CP-2 — the vehicle that matters most among those <paramref name="watcher"/> remembers seeing (identified, live, a vehicle): a THREAT before
        /// anything else, then the soonest at the stand-off; an indicator raises a vehicle one step (an approaching two-up bike is watched as a
        /// threat). False when nothing is on the approach. Allocation-free.
        /// </summary>
        public static bool Scan(EntityRepository world, Entity watcher, in ApproachSite site, ref CheckpointWatch best)
        {
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(watcher)) return false;
            ref readonly var memory = ref world.GetComponentRO<TargetMemory>(watcher);
            bool any = false;
            for (int i = 0; i < memory.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in memory, i)) continue;
                var e = new Entity((ulong)memory.EntityIds[i]);
                if (!world.IsAlive(e) || !Fdp.Toolkit.Motion.Locomotions.IsVehicle(world, e)) continue;
                if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, e, out var at, out var velocity)) continue;
                var verdict = Judge(in site, at, velocity, out float seconds, out float closing);
                if (verdict == ApproachVerdict.None) continue;
                var flags = Indicators(world, e);
                if (verdict == ApproachVerdict.Approaching && (flags & ApproachIndicator.TwoUpBike) != 0) verdict = ApproachVerdict.Threat;
                any = true;
                if (best.Vehicle != 0 && !Before(verdict, seconds, (ApproachVerdict)best.Verdict, best.SecondsToStandOff)) continue;   // keep the more urgent
                best.Vehicle = memory.EntityIds[i];
                best.Verdict = (byte)verdict;
                best.SecondsToStandOff = float.IsPositiveInfinity(seconds) ? 9999f : seconds;
                best.Indicators = (byte)flags;
                best.ClosingMps = closing;
            }
            return any;
        }

        /// <summary>Is (<paramref name="v"/>, <paramref name="s"/>) more urgent than (<paramref name="bv"/>, <paramref name="bs"/>)?</summary>
        private static bool Before(ApproachVerdict v, float s, ApproachVerdict bv, float bs)
        {
            int r = Rank(v), br = Rank(bv);
            return r != br ? r > br : s < bs;
        }

        private static int Rank(ApproachVerdict v) => v switch
        {
            ApproachVerdict.Threat => 3, ApproachVerdict.Approaching => 2, ApproachVerdict.Routine => 1, _ => 0,
        };
    }

    /// <summary>
    /// ⭐ <c>CE-4622</c> K-4 (CP-2; held id 127, §5a) — the checkpoint's watch on its commander: the vehicle that matters most now, the verdict and its seconds
    /// to the stand-off. Written only by the commander's own <c>Checkpoint</c> play (a self write); read by the vehicle ladder (K-5) and by the
    /// scenario checks. Brain-local, unmanaged, never saved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CheckpointWatch)]
    [DataPolicy(DataPolicy.NoScenario)]
    public unsafe struct CheckpointWatch
    {
        /// <summary>The vehicle (its entity id, as remembered); 0 = none.</summary>
        public long Vehicle;
        /// <summary>An <see cref="ApproachVerdict"/>.</summary>
        public byte Verdict;
        /// <summary>Seconds to the stand-off (9999 when it is not closing).</summary>
        public float SecondsToStandOff;
        /// <summary><see cref="ApproachIndicator"/> bits.</summary>
        public byte Indicators;
        /// <summary>How many vehicles this checkpoint has judged a THREAT (each counted once, when first so judged).</summary>
        public int Threats;
        /// <summary>How many it has seen stop at the line as routine (each counted once).</summary>
        public int Routines;
        /// <summary>The last vehicle counted (so one vehicle counts once per verdict).</summary>
        public long LastThreat, LastRoutine;
        /// <summary>⭐ K-5 — the squad's ONE ladder rung (a <see cref="VehicleRung"/>) and the vehicle it is on.</summary>
        public byte Rung;
        public long RungVehicle;
        /// <summary>⭐ K-5 — the highest rung reached so far (for the checks).</summary>
        public byte MaxRung;
        /// <summary>⭐ K-5 — the vehicle's closing speed now (m/s), and the one it had when it was first signalled (inside the warning line).</summary>
        public float ClosingMps, ClosingAtSignal;
        /// <summary>⭐ K-5 — the vehicle first signalled (<see cref="ClosingAtSignal"/> is its).</summary>
        public long SignalledVehicle;
        /// <summary>⭐ K-6 — the vehicle being searched, the search's stage (0 none, 1 demanded "out, hands up", 2 released, 3 held: something found).</summary>
        public long SearchVehicle;
        public byte SearchStage;
        /// <summary>⭐ K-6 — how many things the vehicle searches revealed; how many searched vehicles were let go, how many held.</summary>
        public int Found, Released, Held;
        /// <summary>⭐ K-6 — the people aboard when the demand went out (up to 4), how many, the concealed things aboard then, the last vehicle searched.</summary>
        public fixed long Occupants[4];
        public byte OccupantCount, ConcealedAtDemand;
        public long LastSearched;
        /// <summary>⭐ K-6 — the man out of the vehicle the men's searcher is sent to now (0 = none yet): a change re-issues his order.</summary>
        public long SearchMan;
        /// <summary>⭐ K-7 — where that man stood when the searcher was sent (a move of more than 1 m re-sends him).</summary>
        public float SearchManX, SearchManY;
        /// <summary>⭐ K-5 — 1 while the stopper shows the signal.</summary>
        public byte Signalling;
    }
}

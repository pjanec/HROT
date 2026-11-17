using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-4622</c> K-5 (CP-3) — the rungs of the checkpoint's VEHICLE ladder, in order.</summary>
    public enum VehicleRung : byte
    {
        /// <summary>Nothing to do: no vehicle inside the warning line.</summary>
        Quiet = 0,
        /// <summary>The stopper signals (the gesture; a light at night).</summary>
        Signal = 1,
        /// <summary>A warning shot into the ground ahead of the vehicle.</summary>
        WarningShot = 2,
        /// <summary>Disabling fire at the ENGINE (<see cref="AimPoint.Engine"/>), the machine gun.</summary>
        Disabling = 3,
        /// <summary>Lethal fire at the vehicle.</summary>
        Lethal = 4,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-5 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-3, §4; KV-2, KV-3, KV-7, KV-14) — the VEHICLE branch beside
    /// <see cref="EscalationLadder"/>: ONE rung for the whole squad, keyed on the vehicle's TIME to the stand-off. Every vehicle inside the warning line is
    /// signalled; a THREAT climbs by time — the warning shot at ⅔ of the warning time, disabling fire at the engine at 0.4, lethal fire at 0.2 — and a
    /// rung whose time has already passed is SKIPPED, never served late. A bike has no engine to disable: its table goes from the warning shot to lethal
    /// at the disabling time. The ROE caps it — the warning shot needs NonLethal, disabling and lethal fire Lethal — except in SELF-DEFENCE (the vehicle
    /// has committed a lethal hostile act: a ram). The rung never steps down while the same vehicle stays a threat.
    /// </summary>
    public static class VehicleLadder
    {
        /// <summary>The rungs' times, as fractions of the warning time (§4: 15 s → 10 s, 6 s, 3 s).</summary>
        public const float WarningShotAt = 2f / 3f, DisablingAt = 0.4f, LethalAt = 0.2f;

        /// <summary>
        /// The rung due now for a vehicle <paramref name="seconds"/> from the stand-off with <paramref name="verdict"/>, never below
        /// <paramref name="current"/> while it stays a threat; <paramref name="cap"/> is the unit's ROE force level (unset = Lethal).
        /// </summary>
        public static VehicleRung Due(ApproachVerdict verdict, float seconds, float warningSeconds, bool bike, RoeForceLevel cap, bool selfDefence,
                                      VehicleRung current)
        {
            if (verdict == ApproachVerdict.None) return VehicleRung.Quiet;
            if (verdict != ApproachVerdict.Threat)
                return seconds <= warningSeconds || verdict == ApproachVerdict.Routine ? VehicleRung.Signal : VehicleRung.Quiet;
            var due = VehicleRung.Signal;
            if (seconds <= warningSeconds * WarningShotAt) due = VehicleRung.WarningShot;
            if (seconds <= warningSeconds * DisablingAt) due = bike ? VehicleRung.Lethal : VehicleRung.Disabling;
            if (seconds <= warningSeconds * LethalAt) due = VehicleRung.Lethal;
            if (current > due) due = current;   // never down while the same vehicle is a threat
            return Cap(due, cap, selfDefence);
        }

        /// <summary>The ROE cap: the warning shot needs NonLethal or more, disabling and lethal fire Lethal; self-defence lifts it.</summary>
        public static VehicleRung Cap(VehicleRung rung, RoeForceLevel cap, bool selfDefence)
        {
            if (selfDefence || cap == RoeForceLevel.ForceUnset || cap == RoeForceLevel.Lethal) return rung;
            if (cap == RoeForceLevel.NonLethal) return rung > VehicleRung.WarningShot ? VehicleRung.WarningShot : rung;
            return rung > VehicleRung.Signal ? VehicleRung.Signal : rung;
        }
    }
}

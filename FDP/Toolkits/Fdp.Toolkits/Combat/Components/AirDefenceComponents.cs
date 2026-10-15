using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>⭐ <c>CE-2188</c> (DESIGN_Missiles_And_Ammo §3c) — which seeker a decoy fools.</summary>
    public enum DecoyKind : byte
    {
        /// <summary>A flare — fools an <c>InfraRed</c> seeker.</summary>
        Flare = 1,
        /// <summary>Chaff — fools a <c>Radar</c> seeker.</summary>
        Chaff = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> (DESIGN_Missiles_And_Ammo §3c, AD3) — a flare or a chaff cloud an aircraft dispensed: a point with a velocity
    /// (it leaves at the aircraft's and falls) that a homing round of the matching seeker may take as its target. Spawned and destroyed
    /// by <c>CountermeasureSystem</c> on the SimHost; never saved, never replicated (the round that chases it is SimHost-local too).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Decoy)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Decoy
    {
        public DecoyKind Kind;
        /// <summary>The aircraft that dispensed it.</summary>
        public Entity Owner;
        /// <summary>Sim time (s) it burns out.</summary>
        public double Until;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> (§3c, AD3) — an aircraft's dispensers: flares and chaff left, and the earliest sim time it may dispense again.
    /// Filled from its <c>Combat.Countermeasures</c> TKB entry the first time it dispenses; written by <c>CountermeasureSystem</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.CountermeasureState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct CountermeasureState
    {
        public int Flares;
        public int Chaff;
        public double NextAt;
        /// <summary>1 once the counts were taken from the TKB (0 = a fresh aircraft).</summary>
        public byte Filled;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2189</c> (§3c, AD4) — the last missile launched at this aircraft, as its BRAIN learned of it: where it came from, the
    /// bearing to it (degrees, 0 = north, clockwise), its seeker and the time to impact at launch. Written by
    /// <c>MissileWarningSensingSystem</c> beside <c>SensorChange.MissileWarning</c>; read by the behaviours (flares on the IR seeker's
    /// bearing, a break turn — <c>CE-2191</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MissileWarning)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct MissileWarning
    {
        /// <summary>Sim time (s) the warning arrived.</summary>
        public double At;
        public Vector3 From;
        public float BearingDeg;
        public Fdp.Toolkit.Tkb.Domain.GuidanceKind Seeker;
        public float TimeToImpactS;
    }
}

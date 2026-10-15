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
    /// ⭐⭐ <c>CE-2188</c> (BS-1-DESIGN §5.4c, DESIGN_Missiles_And_Ammo §3c) — a flare or a chaff cloud an aircraft released: a point that
    /// follows <c>DecoyFlight.At</c>(<see cref="Origin"/>, <see cref="Velocity"/>, <see cref="SinkMps"/>, now − <see cref="FiredAt"/>) — the
    /// SAME path every viewer draws from the fire notification — and that a homing round of the matching seeker may take as its target.
    /// Spawned and destroyed by <c>CountermeasureSystem</c> on the SimHost; never saved, never replicated (the round that chases it is
    /// SimHost-local too).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Decoy)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Decoy
    {
        public DecoyKind Kind;
        /// <summary>The aircraft that released it.</summary>
        public Entity Owner;
        /// <summary>Sim time (s) it burns out.</summary>
        public double Until;
        /// <summary>The aircraft's velocity at release. ⭐ No <c>SimVelocity</c>: <c>CountermeasureSystem</c> places it by
        /// <c>DecoyFlight.At</c>, so no host's integrator (narrowed to projectiles on Stride, CE-238) has to know about it.</summary>
        public Vector3 Velocity;
        /// <summary>Where it left the aircraft.</summary>
        public Vector3 Origin;
        /// <summary>Sim time (s) it left — the notification's <c>FiredAt</c>.</summary>
        public double FiredAt;
        /// <summary>How fast it sinks (its munition's <c>ExpendableDto.SinkMps</c>).</summary>
        public float SinkMps;
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

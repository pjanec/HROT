using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8, LS-14 / D-31 / D-74) — a device's LASER as its logic
    /// points it: written ONLY by whoever points (the device's own behaviour; later an AI designator's), read by <see cref="LaserSpotSystem"/>
    /// (the muscle). Unmanaged, recorded. ⭐ <see cref="Direction"/> is in the DEVICE's frame (zero = boresight, the device's forward +X): the
    /// muscle composes it with the device's world pose (<see cref="DevicePoses.TryWorld"/>) at trace time, so a device carried by a moving body
    /// never re-writes its pointing to keep it on — the component changes only when the device's logic changes it (sent on change).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LaserPointing)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct LaserPointing
    {
        /// <summary>1 while the laser emits.</summary>
        public byte On;
        /// <summary>The band — the key of the ONE visibility rule (<see cref="LaserVisibility"/>, design §3.8a D-159; names in <see cref="LaserBands"/>): 0 visible, 1 an eye-safe rangefinder (1 540 nm — nothing sees it), 2 a designator (1 064 nm — night vision, faint), 3 a near-IR pointer / illuminator (≈ 850 nm — night vision, bright).</summary>
        public byte Band;
        /// <summary>Relative output, 0..1 (a device's LOW / HIGH modes, D-74) — the spot's strength scales with it.</summary>
        public float Power;
        /// <summary>The beam's direction in the device's frame; zero = the device's forward (+X).</summary>
        public Vector3 Direction;
        /// <summary>The pointer's REQUEST number — bumped each time it starts a measurement; the spot echoes the one it answers
        /// (<see cref="LaserSpot.PointingSeq"/>), so a device never reads a spot left from an earlier emission (one hop late across nodes).</summary>
        public uint Seq;
    }

    /// <summary>One point the beam reaches (S1c: the first return only — the room for four is for partial obscurants, SD-S6).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LaserSpotPoint
    {
        /// <summary>World position of the return.</summary>
        public Vector3 Point;
        /// <summary>Metres from the device's world origin.</summary>
        public float Range;
        /// <summary>The return's strength — <see cref="LaserSpots.StrengthAt"/>.</summary>
        public float Strength;
        /// <summary>The body hit (this node's handle); <see cref="Entity.Null"/> on terrain. ⚠ local until SD-C translates it for the wire.</summary>
        public Entity Hit;
        /// <summary>The body part hit (<c>BodyPartHitResult.Part</c>), −1 when none (terrain, a body with no parts).</summary>
        public int Part;
    }

    /// <summary>Four <see cref="LaserSpotPoint"/>s.</summary>
    [InlineArray(Count)]
    public struct LaserSpotPoints
    {
        public const int Count = 4;
#pragma warning disable IDE0044 // backing field required by InlineArray
        private LaserSpotPoint _element;
#pragma warning restore IDE0044
    }

    /// <summary>
    /// ⭐⭐ S1c (§3.8 LS-14, R5-9 / R5-10) — where a device's laser lands, written ONLY by <see cref="LaserSpotSystem"/> (the muscle, one writer).
    /// Nearest first. <see cref="Preview"/> = written on a paused frame: the device's own HUD shows it, ⛔ no consumer (a seeker, perception)
    /// acts on it (R-4300 — no designation in pause). Unmanaged, recorded (replay shows the spot as it was).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LaserSpot)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct LaserSpot
    {
        public LaserSpotPoints Points;
        /// <summary>How many of <see cref="Points"/> are set; 0 with a matching <see cref="PointingSeq"/> = traced, no return within range.</summary>
        public byte Count;
        /// <summary>1 when written on a paused frame (dt = 0).</summary>
        public byte Preview;
        /// <summary>The <see cref="LaserPointing.Seq"/> this spot answers; 0 = no emission answered (the laser is off).</summary>
        public uint PointingSeq;
    }

    /// <summary>⭐ The laser's documented constants (S1c).</summary>
    public static class LaserSpots
    {
        /// <summary>The longest trace (m).</summary>
        public const float MaxRangeM = 10_000f;
        /// <summary>The range at which a full-power return has strength ½ (m).</summary>
        public const float ReferenceRangeM = 1_000f;

        /// <summary>
        /// The return's strength: <c>Power / (1 + (range / ReferenceRangeM)²)</c> — an inverse-square falloff that is finite at the device
        /// (1 × power at 0 m, ½ at 1 km, ~0.04 at 5 km). ⚠ A simple stand-in (no target reflectivity, no atmosphere) — the visibility rule
        /// (SD-S6) reads it, not a physics model.
        /// </summary>
        public static float StrengthAt(float power, float range)
        {
            float r = range / ReferenceRangeM;
            return power / (1f + r * r);
        }
    }
}

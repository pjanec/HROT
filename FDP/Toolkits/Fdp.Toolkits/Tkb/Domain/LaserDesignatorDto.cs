using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-113) — a LASER TARGET DESIGNATOR type's duty cycle and code: how long
    /// one lase may run before the laser must cool (the duty limit that makes "lase on" timing a skill), how long it cools, and the PRF code a fresh device holds.
    /// Read once by the designator's logic when its run starts (<c>DesignatorBehavior.Resolve</c>).
    /// </summary>
    [TkbDescriptor("Gen.LaserDesignator")]
    public record LaserDesignatorDto
    {
        /// <summary>The longest single lase (s of sim time); then the laser goes off and COOLS.</summary>
        public float LaseSeconds { get; init; } = 60f;

        /// <summary>How long it cools after a lase ran to its limit (s of sim time) — LASE is refused meanwhile.</summary>
        public float CoolSeconds { get; init; } = 30f;

        /// <summary>The 4-digit PRF code a fresh device holds (each digit in its proven range: 1 · 1–7 · 1–8 · 1–8).</summary>
        public ushort DefaultCode { get; init; } = 1688;
    }
}

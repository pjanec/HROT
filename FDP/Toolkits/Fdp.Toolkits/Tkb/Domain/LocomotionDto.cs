using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ CE-2168 (docs/designs/motion-models/DESIGN_Motion_Domains.md MD-A) — HOW a body moves. The class picks the motion DOMAIN (which
    /// module moves it: ground, air, naval) and, inside it, the motion MODEL. <c>VehicleClass</c> stays the ground preset key.
    /// </summary>
    public enum LocomotionClass : byte
    {
        /// <summary>Not stated (a type with no motion, or one built before CE-2168: read by its <c>VehicleParams</c>).</summary>
        Unknown = 0,
        Pedestrian = 1,
        Wheeled = 2,
        Tracked = 3,
        /// <summary>A helicopter, a quadcopter: hovers, climbs, yaws (MD-H's first air class).</summary>
        Rotorcraft = 4,
        /// <summary>Later — a table row that fails loudly until built (MD-H).</summary>
        FixedWing = 5,
        /// <summary>Later — a boat on the water (the naval domain).</summary>
        Surface = 6,
        /// <summary>Later — ONE ground model composing the wheeled and boat integrators by the surface below (MD-F).</summary>
        Amphibious = 7,
        /// <summary>⭐ Airborne insertion B-2 (D-2) — a man sliding down a fast rope (<c>RopeSlideModel</c>). Never a type's class: implied by
        /// his <c>Descent</c>.</summary>
        Rope = 8,
        /// <summary>⭐ Airborne insertion B-2 (D-2) — a man under (or falling before) a canopy (<c>ParachuteModel</c>). Never a type's class.</summary>
        Parachute = 9,
    }

    /// <summary>⭐ CE-2168 (MD-A) — the TKB descriptor <c>Gen.Locomotion</c>: the type's <see cref="LocomotionClass"/>.</summary>
    [TkbDescriptor("Gen.Locomotion")]
    public record LocomotionDto
    {
        public LocomotionClass Class { get; init; }
    }
}

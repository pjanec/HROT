using System.Collections.Generic;
using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ <c>CE-3210</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-7) — what damage to a zone DOES, beside the
    /// health it takes: nothing more, a slower man (a leg), a worse aim (an arm). S2 adds the vehicle consequences.</summary>
    public enum WoundEffect : byte
    {
        None = 0,
        /// <summary>A leg: the wounded speed factor, no running.</summary>
        Slow = 1,
        /// <summary>An arm: the aim's spread grows.</summary>
        Aim = 2,
        /// <summary>⭐ <c>CE-3211</c> (S2-8, R-263) — a vehicle's running gear or engine: the mobility kill (<c>CanMove</c> off).</summary>
        Mobility = 3,
        /// <summary>⭐ <c>CE-3211</c> (S2-8, R-263) — a vehicle's turret or gun: the firepower kill (<c>CanShoot</c> off).</summary>
        Firepower = 4,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (HL-4, HL-12) — one ZONE of a body: a set of part ids with its armour per face, a damage multiplier, and what a
    /// wound there does. In a type's <see cref="BodyZonesDto"/> every field is OPTIONAL — only what differs from the type's body profile
    /// (<c>bodies.json</c>, matched by DIS class) is written; <see cref="Parts"/> belongs to the profile (the plan fixes the parts).
    /// </summary>
    public record BodyZoneDto
    {
        [Description("Zone name: Head, Torso, Arm, Leg (a person).")]
        public string Zone { get; init; } = "";

        [Description("The part ids in this zone (profile only).")]
        public List<byte>? Parts { get; init; }

        [EditUnit("mm RHA")] public float? ArmourFront { get; init; }
        [EditUnit("mm RHA")] public float? ArmourSide { get; init; }
        [EditUnit("mm RHA")] public float? ArmourRear { get; init; }
        [EditUnit("mm RHA")] public float? ArmourTop { get; init; }

        [Description("Damage × this for a hit in the zone (head 4, torso 1, limbs below 1).")]
        public float? Multiplier { get; init; }

        [Description("What a wound in this zone does.")]
        public WoundEffect? Effect { get; init; }

        [Description("The part damage level (0–255) from which the effect applies.")]
        public int? WoundedAt { get; init; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3210</c> (HL-12; 🔒 user: "customizable per tkb type if really needed") — a TKB type's SPARSE override of its body
    /// profile: only the zones and fields that differ ("this militia soldier: no vest"). Merged field by field over the DIS-class profile.
    /// </summary>
    [TkbDescriptor("Combat.BodyZones")]
    public record BodyZonesDto
    {
        public List<BodyZoneDto> Zones { get; init; } = new();
    }
}

using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Combat capabilities of a weapon system mounted on a vehicle.
    /// </summary>
    [TkbDescriptor("Gen.WeaponCapabilities")]
    public record WeaponCapabilitiesDto
    {
        /// <summary>Effective engagement range.</summary>
        [EditUnit("m")]
        public float EffectiveRange { get; init; }

        /// <summary>Sustained rate of fire.</summary>
        [EditUnit("rpm")]
        public float RateOfFire { get; init; }

        /// <summary>Number of rounds in the magazine or ready-rack.</summary>
        public int MagazineCapacity { get; init; }
    }
}

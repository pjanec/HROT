using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-2186</c> (W2, R-2020; Hrot docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §2b) — <b>what one round of a munition
    /// costs</b>, relative to the others (a rifle round 1, a guided missile in the hundreds). On the MUNITION type. The weapon brain picks the
    /// CHEAPEST loaded round that is enough (<c>AmmoChoice</c>); a munition with none (0) keeps the old pick — by effect alone.
    /// </summary>
    [TkbDescriptor("Munition.Cost")]
    public record MunitionCostDto
    {
        /// <summary>The relative cost of one round; 0 = unknown.</summary>
        public float RelativeCost { get; init; }
    }
}

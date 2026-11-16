using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ <c>CE-4623</c> AB-9 (docs/designs/animals/DESIGN_Animals.md AR-16) — the animal area type's DEFAULTS (TKB 712): what a newly drawn area
    /// holds until its author sets otherwise (the area's own <c>AnimalArea</c> wins).</summary>
    [TkbDescriptor("Gen.AnimalArea")]
    public record AnimalAreaDto
    {
        /// <summary>The species (<c>Fdp.Toolkit.Animals.AnimalSpecies</c>).</summary>
        public byte Species { get; init; }
        public int Count { get; init; } = 10;
        public bool Enclosed { get; init; }
    }
}

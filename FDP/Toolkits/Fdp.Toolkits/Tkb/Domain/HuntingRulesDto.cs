using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4649</c> PART D (AN-21g; docs/designs/animals/DESIGN_Animals.md §4c) — a type's STANDING hunting rule: the animal species (<c>AnimalSpecies</c> values) its units may engage on their own initiative —
    /// "animals of the listed species, never people". Read by <c>Animals.MayEngage</c>, the one seam the weapon brain's target choice asks (R-3101's explicit-order path stays separate). A type with none engages no animal
    /// unless ordered. ⛔ A dog is never listed by the hunter; a person is never an animal, so no rule here can name one.
    /// </summary>
    [TkbDescriptor("Gen.HuntingRules")]
    public record HuntingRulesDto
    {
        /// <summary>The species (<c>Fdp.Toolkit.Animals.AnimalSpecies</c> values) the unit may engage.</summary>
        public List<byte> Species { get; init; } = new();
    }
}

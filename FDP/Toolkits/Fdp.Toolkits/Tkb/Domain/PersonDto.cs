using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ ui finding F-4 (docs/DESIGN_Civilians_And_Crowd_Control.md §7.6) — a person's age band.</summary>
    public enum AgeBand : byte
    {
        Adult = 0,
        Child = 1,
        Elderly = 2,
    }

    /// <summary>⭐ F-4 — a person's sex, where the type states one; Unspecified lets a reader choose (e.g. by seed).</summary>
    public enum PersonSex : byte
    {
        Unspecified = 0,
        Male = 1,
        Female = 2,
    }

    /// <summary>
    /// ⭐ F-4 — who a person type IS: its age band and sex. Read by the 3-D outfits (U1-b) so an elderly or female civilian looks it, instead
    /// of inferring from the type's name or height. ⭐ Absent ⇒ an adult of unspecified sex, so soldiers need none.
    /// </summary>
    [TkbDescriptor("Body.Person")]
    public record PersonDto
    {
        [Description("The person's age band.")]
        public AgeBand Age { get; init; }

        [Description("The person's sex; Unspecified lets the look be chosen per person.")]
        public PersonSex Sex { get; init; }
    }
}

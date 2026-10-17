using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-3234</c> (civilians B-12, AW-14; docs/DESIGN_Civilians_And_Crowd_Control.md §7.14 HI-3) — what a person type carries in his
    /// pockets: type data, never entities. He can hold one in his hand (<c>HeldItems.Set</c>). ⭐ Absent ⇒ none.
    /// </summary>
    [TkbDescriptor("Body.PocketItems")]
    public record PocketItemsDto
    {
        [Description("He carries a phone (a cue to an observer; a remote charge's trigger).")]
        public bool Phone { get; init; }

        [Description("He carries binoculars (held up, he sees further and narrower).")]
        public bool Binoculars { get; init; }
    }
}

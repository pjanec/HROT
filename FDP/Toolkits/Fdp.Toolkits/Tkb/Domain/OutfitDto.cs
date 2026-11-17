using System.Collections.Generic;
using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    // ⭐ CE-4648 (docs/designs/person-looks/DESIGN_Person_Outfits.md §3 OF-1) — what a person TYPE wears. The TKB says it; the presentation
    //   flattens it once per type and the seed only picks inside the type's own lists. An option's <see cref="OutfitOption.Kind"/> is the NAME of a
    //   garment kind (<c>Hrot.Core.Tkb.OutfitTop</c> / <c>OutfitLegs</c> / <c>OutfitOver</c> / <c>OutfitHead</c> / <c>OutfitHair</c> / <c>OutfitBeard</c>), so a
    //   TKB file reads "Robe", not 3. ⛔ Those enums are not here: this project's DDS code generator puts every enum of a file in ONE IDL scope,
    //   where the shared member name <c>None</c> collides.

    /// <summary>⭐ OF-11 — how a Uniform is camouflaged: not at all, or by the person's SIDE (R-1020: friend woodland, hostile desert).</summary>
    public enum OutfitCamo : byte { None, BySide }

    /// <summary>
    /// ⭐ OF-1 — one weighted option of a slot: a garment / hair / beard <see cref="Kind"/> (the member name of the slot's enum) in one of
    /// the <see cref="Colours"/> (<c>#RRGGBB</c>; the seed picks one, uniformly). The option itself is picked by <see cref="Weight"/>.
    /// </summary>
    public record OutfitOption
    {
        [Description("The kind, by name: a member of the slot's garment enum — Top: Shirt, TShirt, Suit, Robe, Abaya, Uniform, Jacket, Buckskin, Perahan, Chadari; Legs: Trousers, Shorts, Skirt, LongSkirt, Hidden, Breechcloth, Tunban; Over: None, ChestRig, BeadedYoke, HairPipe, YokeHairPipe; Head: None, Cap, PoliceCap, RiotHelmet, Helmet, Shemagh, Skullcap, Hijab, Niqab, ShemaghFaceWrap, Headband, Pakol, Turban; Hair: Short, Long, None, Braids; Beard: None, Moustache, Beard, Paint (face paint) (the Skin slot ignores it).")]
        public string Kind { get; init; } = "";

        [Description("The colours this option comes in, #RRGGBB; one is picked per person.")]
        public List<string> Colours { get; init; } = new();

        [Description("The option's share of its slot (relative weights; need not sum to 100).")]
        public float Weight { get; init; } = 1f;

        [Description("A Uniform's camouflage: None, or BySide (his side's cloth and pattern).")]
        public OutfitCamo Camo { get; init; }
    }

    /// <summary>⭐ OF-1 — the options of one slot. Empty ⇒ the slot is not authored (the everyday default).</summary>
    public record OutfitSlot
    {
        public List<OutfitOption> Options { get; init; } = new();
    }

    /// <summary>⭐ OF-2 — one sex's slots, for a type whose <c>PersonDto.Sex</c> is Unspecified. A slot left empty falls back to the DTO's own.</summary>
    public record OutfitSet
    {
        public OutfitSlot Top { get; init; } = new();
        public OutfitSlot Legs { get; init; } = new();
        public OutfitSlot Over { get; init; } = new();
        public OutfitSlot Head { get; init; } = new();
        public OutfitSlot Hair { get; init; } = new();
        public OutfitSlot Beard { get; init; } = new();
        public OutfitSlot Extra { get; init; } = new();   // ⭐ R-1064
    }

    /// <summary>
    /// ⭐ CE-4648 (OF-1) — what a person type wears: slots, each a list of weighted options, drawn per person from his seed in a fixed order
    /// (OF-6: skin, sex, top, legs, over, head, hair, beard). ⭐ OF-11: a type WITH an <c>OutfitDto</c> wears it whatever its class. Absent ⇒ the
    /// everyday wardrobes the presentation has always drawn (now data in <c>OutfitCatalog</c>, OF-3).
    /// </summary>
    [TkbDescriptor("Body.Outfit")]
    public record OutfitDto
    {
        public OutfitSlot Skin { get; init; } = new();
        public OutfitSlot Top { get; init; } = new();
        public OutfitSlot Legs { get; init; } = new();
        public OutfitSlot Over { get; init; } = new();
        public OutfitSlot Head { get; init; } = new();
        public OutfitSlot Hair { get; init; } = new();
        public OutfitSlot Beard { get; init; } = new();

        [Description("Extra garments and ornaments, drawn LAST (R-1064): Extra: None, Armbands, Necklace, ArmbandsNecklace, Waistcoat, Patu, WaistcoatPatu.")]
        public OutfitSlot Extra { get; init; } = new();

        [Description("Slots used for a man of an Unspecified-sex type (empty slots fall back to the ones above).")]
        public OutfitSet? Men { get; init; }

        [Description("Slots used for a woman of an Unspecified-sex type (empty slots fall back to the ones above).")]
        public OutfitSet? Women { get; init; }
    }
}

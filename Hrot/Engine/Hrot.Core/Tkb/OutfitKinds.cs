namespace Hrot.Core.Tkb
{
    // ⭐ CE-4648 (docs/designs/person-looks/DESIGN_Person_Outfits.md §3 OF-4) — the garment KINDS the block figure can draw. An
    //   Fdp.Toolkit.Tkb.Domain.OutfitOption names one by its member name (so a TKB file reads "Robe"); the presentation parses the names once per type.

    // ⭐ CE-4648 (R-1062, R-1063) — ui added the Native American kinds: Top Buckskin, Legs Breechcloth, Head Headband (with feathers), Hair Braids, Beard Paint (FACE PAINT lives in the beard slot: no new draw, so no existing person moves).

    /// <summary>⭐ OF-4 — the garment on the body.</summary>
    public enum OutfitTop : byte { Shirt, TShirt, Suit, Robe, Abaya, Uniform, Jacket, Buckskin }

    /// <summary>⭐ OF-4 — what is on the legs (the presentation's <c>Legwear</c> has the same member names). <c>Hidden</c> = under a robe / abaya.</summary>
    public enum OutfitLegs : byte { Trousers, Shorts, Skirt, LongSkirt, Hidden, Breechcloth }

    /// <summary>⭐ OF-4 — a garment worn OVER the top (an ammunition vest).</summary>
    public enum OutfitOver : byte { None, ChestRig }

    /// <summary>⭐ OF-4 — headwear. A scarf-type covering (Shemagh, Hijab, Niqab, ShemaghFaceWrap) hides the hair (OF-5).</summary>
    public enum OutfitHead : byte { None, Cap, PoliceCap, RiotHelmet, Helmet, Shemagh, Skullcap, Hijab, Niqab, ShemaghFaceWrap, Headband }

    /// <summary>⭐ OF-1 — the hair under the headwear (the presentation's <c>HairStyle</c> has the same member names).</summary>
    public enum OutfitHair : byte { Short, Long, None, Braids }

    /// <summary>⭐ OF-1 — a moustache or a beard, or none (the presentation's <c>BeardKind</c> has the same member names).</summary>
    public enum OutfitBeard : byte { None, Moustache, Beard, Paint }
}

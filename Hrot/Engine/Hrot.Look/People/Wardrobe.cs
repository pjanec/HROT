using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Color = Hrot.Look.Rgba;   // ⭐ CE-4671 LK-S2 — plain bytes, no renderer colour (the map converts when it draws)

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-4648</c> (docs/designs/person-looks/DESIGN_Person_Outfits.md §3 OF-6, OF-7) — a person type's <see cref="OutfitDto"/>, FLATTENED ONCE
/// (when the type's <see cref="EntityBodyLayer3D.TypeLook"/> is built): each slot is arrays — the option kinds, their colours, cumulative
/// weights — so <see cref="Outfits.For"/> only indexes them, per person, per frame, with no allocation and no LINQ (R-2084).
/// </summary>
public sealed class Wardrobe
{
    /// <summary>One slot: option <c>i</c> has kind <see cref="Kind"/>[i] (the slot enum's byte), colours <see cref="Colours"/>[i], and covers
    /// the share of [0,1) up to <see cref="Cumulative"/>[i].</summary>
    public sealed class Slot
    {
        public readonly byte[] Kind;
        public readonly OutfitCamo[] Camo;
        public readonly Color[][] Colours;
        public readonly float[] Cumulative;

        internal Slot(byte[] kind, OutfitCamo[] camo, Color[][] colours, float[] cumulative)
        {
            Kind = kind; Camo = camo; Colours = colours; Cumulative = cumulative;
        }

        /// <summary>The option a draw <paramref name="u"/> in [0,1) lands on (the last one for any rounding left over).</summary>
        public int PickOption(float u)
        {
            for (int i = 0; i < Cumulative.Length; i++)
                if (u < Cumulative[i]) return i;
            return Cumulative.Length - 1;
        }
    }

    /// <summary>The garment slots of one sex (or of the type, when it names no per-sex sets).</summary>
    public sealed class Set
    {
        public readonly Slot Top, Legs, Over, Head, Hair, Beard, Extra;
        internal Set(Slot top, Slot legs, Slot over, Slot head, Slot hair, Slot beard, Slot extra) { Top = top; Legs = legs; Over = over; Head = head; Hair = hair; Beard = beard; Extra = extra; }
    }

    public readonly Slot Skin;
    public readonly Set Neutral;
    public readonly Set? Men, Women;

    private Wardrobe(Slot skin, Set neutral, Set? men, Set? women) { Skin = skin; Neutral = neutral; Men = men; Women = women; }

    /// <summary>The slots a man / a woman of the type is dressed from: his sex's set, else the type's own (OF-2).</summary>
    public Set SetFor(bool woman) => woman ? Women ?? Neutral : Men ?? Neutral;

    /// <summary>Flattens <paramref name="dto"/> (null ⇒ null: the type keeps the everyday wardrobes). ⛔ Throws on an unknown kind or a bad colour.</summary>
    public static Wardrobe? Of(OutfitDto? dto)
    {
        if (dto == null) return null;
        var neutral = new Set(
            Top(dto.Top, null), Legs(dto.Legs, null), Over(dto.Over, null), Head(dto.Head, null), Hair(dto.Hair, null), Beard(dto.Beard, null), Extra(dto.Extra, null));
        return new Wardrobe(Flatten(dto.Skin, null, 0, (string[])OutfitCatalog.SkinTones, _ => 0), neutral, SetOf(dto.Men, neutral), SetOf(dto.Women, neutral));
    }

    private static Set? SetOf(OutfitSet? set, Set fallback)
        => set == null ? null
         : new Set(Top(set.Top, fallback.Top), Legs(set.Legs, fallback.Legs), Over(set.Over, fallback.Over), Head(set.Head, fallback.Head),
                   Hair(set.Hair, fallback.Hair), Beard(set.Beard, fallback.Beard), Extra(set.Extra, fallback.Extra));

    // an unauthored slot is the everyday default: shirts in everyday colours, trousers, nothing over or on the head, short hair, no beard
    private static Slot Top(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, OutfitCatalog.Tops, Kind<OutfitTop>);
    private static Slot Legs(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, OutfitCatalog.Bottoms, Kind<Legwear>);
    private static Slot Over(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, System.Array.Empty<string>(), Kind<OutfitOver>);
    private static Slot Head(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, System.Array.Empty<string>(), Kind<OutfitHead>);
    private static Slot Hair(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, OutfitCatalog.HairColours, Kind<HairStyle>);
    private static Slot Beard(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, System.Array.Empty<string>(), Kind<BeardKind>);

    private static Slot Extra(OutfitSlot s, Slot? fb) => Flatten(s, fb, 0, System.Array.Empty<string>(), Kind<OutfitExtra>);

    private static byte Kind<T>(string name) where T : struct, System.Enum
        => System.Enum.TryParse<T>(name, out var kind) && System.Enum.IsDefined(kind)
            ? System.Convert.ToByte(kind)
            : throw new System.FormatException($"'{name}' is not a {typeof(T).Name} (an outfit option's Kind is a member name)");

    private static Slot Flatten(OutfitSlot slot, Slot? fallback, byte defaultKind, string[] defaultColours, System.Func<string, byte> kindOf)
    {
        var options = slot.Options;
        if (options.Count == 0)
            return fallback ?? new Slot([defaultKind], [OutfitCamo.None], [OutfitPalettes.Of(defaultColours)], [1f]);
        var kinds = new byte[options.Count];
        var camo = new OutfitCamo[options.Count];
        var colours = new Color[options.Count][];
        var cumulative = new float[options.Count];
        float total = 0f;
        foreach (var o in options) total += o.Weight > 0f ? o.Weight : 0f;
        float running = 0f;
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            kinds[i] = kindOf(o.Kind);
            camo[i] = o.Camo;
            colours[i] = new Color[o.Colours.Count];
            for (int c = 0; c < o.Colours.Count; c++) colours[i][c] = OutfitPalettes.Parse(o.Colours[c]);
            running += total > 0f ? (o.Weight > 0f ? o.Weight : 0f) / total : 1f / options.Count;
            cumulative[i] = running;
        }
        cumulative[^1] = float.MaxValue;   // the last option takes any rounding left over
        return new Slot(kinds, camo, colours, cumulative);
    }
}

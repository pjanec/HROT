using System.Globalization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ CE-1033 S5c (docs/DESIGN_Map_3D_Mode.md §3.7, M15 — approved U22) — an entity's body COLOUR, built like affiliation
    /// (<see cref="EntityInfo.ForceId"/>): the TKB default (<c>VisualData.ColorHex</c>) is stamped only if absent, a per-spawn value
    /// wins, and it is saved in the scenario. 🔒 R-136: this — not an override written into the TKB-derived <c>VisualData</c> — is
    /// the legal home of an authored colour.
    /// <para>⚠ Its network descriptor arm and the runtime <c>"Color"</c> attribute are the backend lane's (§3.7); until they land
    /// the value is local to each node, so a replica shows the TKB colour.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.EntityAppearance)]
    // ⭐ Authored PER SPAWN (a scenario's own value must beat the template's), exactly as EntityInfo — so once its descriptor
    //   exists a ghost waits for the real value. ⚠ Without an egress declaring it the component is not ingressible, and the
    //   mandatory set (MandatoryComponentResolver) does not include it — no stall in the meantime.
    [PerInstanceValue]
    public struct EntityAppearance
    {
        /// <summary><c>0xRRGGBBAA</c>. Alpha 0 = no colour: draw the TKB's / the family's default.</summary>
        public uint ColorRgba;

        public bool HasColour => (ColorRgba & 0xFFu) != 0;

        public byte R => (byte)(ColorRgba >> 24);
        public byte G => (byte)(ColorRgba >> 16);
        public byte B => (byte)(ColorRgba >> 8);

        public static EntityAppearance Of(byte r, byte g, byte b) => new() { ColorRgba = ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | 0xFFu };

        /// <summary><c>#RRGGBB</c> or <c>#RRGGBBAA</c> (the TKB's notation); anything else ⇒ no colour.</summary>
        public static EntityAppearance FromHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return default;
            var s = hex.Trim().TrimStart('#');
            if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return default;
            return new EntityAppearance { ColorRgba = s.Length == 6 ? (v << 8) | 0xFFu : v };
        }

        /// <summary>The colour as <c>#RRGGBB</c> (or <c>#RRGGBBAA</c> when not opaque); empty when there is none.</summary>
        public string ToHex() => !HasColour ? string.Empty
            : (ColorRgba & 0xFFu) == 0xFFu ? $"#{ColorRgba >> 8:X6}" : $"#{ColorRgba:X8}";
    }
}

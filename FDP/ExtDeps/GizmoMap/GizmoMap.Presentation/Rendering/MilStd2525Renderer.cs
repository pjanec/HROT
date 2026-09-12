using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// <b>Stub</b> renderer for NATO MIL-STD-2525 symbology: a filled disc in the standard
    /// affiliation colour with a black outline, plus a short SIDC label.
    ///
    /// <para>⚠ It is a stub <i>by design</i> — the original spec asked for exactly this
    /// (<c>.dev/_DONE/gizmos-1/batches/BATCH-20-INSTRUCTIONS.md:126</c>), and it is retained as a
    /// selectable symbology path on that basis. It is <b>not</b> the composed multi-polyline STANAG
    /// frame renderer (frame + inner icon + modifiers); that remains unbuilt.</para>
    ///
    /// <para>The <b>affiliation</b> is character 2 of the SIDC (index 1), and the standard assigns a
    /// colour per affiliation — which is why this renderer derives the colour from the code and
    /// ignores <c>DebugPrimitive.Color</c>. The colour is a property of the symbology standard, not
    /// of the primitive; a caller selects it by choosing the affiliation character.</para>
    ///
    /// <para>⚠ Corrected <c>2026-08-30</c>. The previous table had <b>neutral and unknown swapped</b>
    /// (neutral→yellow, unknown→green) and put Joker in the friendly bucket, and it handled only
    /// 7 of the 15 affiliation characters — everything else fell through to the "unknown" arm and
    /// was coloured green.</para>
    /// </summary>
    public static class MilStd2525Renderer
    {
        private const float SymbolRadius = 20f;
        private const int   LabelFontSize = 10;

        // ── Standard affiliation fill colours (MIL-STD-2525 / APP-6) ─────────────────
        // Pale fills, drawn under a black outline, as the standard renders them.

        /// <summary>Friend / assumed friend — light blue.</summary>
        public static readonly Color FriendColor  = new Color((byte)128, (byte)224, (byte)255, (byte)255);

        /// <summary>Hostile / suspect / joker / faker — light red.</summary>
        public static readonly Color HostileColor = new Color((byte)255, (byte)128, (byte)128, (byte)255);

        /// <summary>Neutral — light green.</summary>
        public static readonly Color NeutralColor = new Color((byte)170, (byte)255, (byte)170, (byte)255);

        /// <summary>Unknown / pending / unspecified — light yellow.</summary>
        public static readonly Color UnknownColor = new Color((byte)255, (byte)255, (byte)128, (byte)255);

        public static void Draw(
            string sidcCode,
            float worldX,
            float worldY,
            Camera2D camera,
            float zoom)
        {
            var affiliationColor = GetAffiliationColor(sidcCode);
            var center = new Vector2(worldX, worldY);

            Raylib.DrawCircleV(center, SymbolRadius / zoom, affiliationColor);
            Raylib.DrawCircleLinesV(center, SymbolRadius / zoom, Color.Black);

            // Label: first 4 characters of SIDC.
            string label = sidcCode.Length >= 4 ? sidcCode[..4] : sidcCode;
            int tx = (int)(worldX - 10f);
            int ty = (int)(worldY + SymbolRadius / zoom + 2f);
            Raylib.DrawText(label, tx, ty, LabelFontSize, Color.Black);
        }

        /// <summary>
        /// Returns the standard affiliation colour for the given SIDC code, read from
        /// character 2 (index 1). All 15 standard affiliation characters are covered:
        ///
        /// <list type="bullet">
        ///   <item><b>Friend</b> (<see cref="FriendColor"/>) — <c>F</c> friend, <c>A</c> assumed
        ///         friend, <c>D</c> exercise friend, <c>M</c> exercise assumed friend.</item>
        ///   <item><b>Hostile</b> (<see cref="HostileColor"/>) — <c>H</c> hostile, <c>S</c> suspect,
        ///         <c>J</c> joker, <c>K</c> faker. ⚠ Joker and Faker are friendly tracks acting as
        ///         suspect/hostile for exercise purposes and the standard renders them in the
        ///         hostile colour, which is why <c>J</c> is here and not with the friends.</item>
        ///   <item><b>Neutral</b> (<see cref="NeutralColor"/>) — <c>N</c> neutral, <c>L</c> exercise
        ///         neutral.</item>
        ///   <item><b>Unknown</b> (<see cref="UnknownColor"/>) — <c>U</c> unknown, <c>P</c> pending,
        ///         <c>G</c> exercise pending, <c>W</c> exercise unknown, <c>O</c> none specified,
        ///         and any unrecognised or too-short code.</item>
        /// </list>
        /// </summary>
        public static Color GetAffiliationColor(string sidcCode)
        {
            if (sidcCode == null || sidcCode.Length < 2)
                return UnknownColor;

            char aff = char.ToUpperInvariant(sidcCode[1]);
            return aff switch
            {
                'F' or 'A' or 'D' or 'M'            => FriendColor,
                'H' or 'S' or 'J' or 'K'            => HostileColor,
                'N' or 'L'                          => NeutralColor,
                _                                   => UnknownColor,
            };
        }

        /// <summary>
        /// Returns the affiliation color as an <see cref="Rgba32"/> for headless tests.
        /// </summary>
        public static Rgba32 GetAffiliationColorRgba(string sidcCode)
        {
            var c = GetAffiliationColor(sidcCode);
            return new Rgba32(c.R, c.G, c.B, c.A);
        }
    }
}

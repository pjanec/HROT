using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Stub renderer for NATO MIL-STD-2525 symbology.
    /// Draws a filled circle in the symbol's standard affiliation color,
    /// plus a short SIDC label.
    ///
    /// Affiliation is read from the second character of the SIDC code:
    ///   'F'|'A'|'D'|'J' = friendly (blue)
    ///   'H'|'S'          = hostile (red)
    ///   'N'|'L'          = neutral (yellow)
    ///   else              = unknown (green)
    /// </summary>
    public static class MilStd2525Renderer
    {
        private const float SymbolRadius = 20f;
        private const int   LabelFontSize = 10;

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
        /// Returns the standard affiliation color for the given SIDC code.
        /// Uses the second character (index 1) to determine affiliation.
        /// </summary>
        public static Color GetAffiliationColor(string sidcCode)
        {
            if (sidcCode == null || sidcCode.Length < 2)
                return Color.Green; // unknown

            char aff = char.ToUpperInvariant(sidcCode[1]);
            return aff switch
            {
                'F' or 'A' or 'D' or 'J' => Color.Blue,
                'H' or 'S'               => Color.Red,
                'N' or 'L'               => Color.Yellow,
                _                        => Color.Green,
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

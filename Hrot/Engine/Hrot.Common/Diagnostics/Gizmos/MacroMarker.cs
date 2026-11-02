using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ SR-178 / SR-192 (docs/DESIGN_Scenario_Macros.md §14.2, §14.4) — the ONE colour of everything that says "this is a macro": the
    /// instance's own map marker and the lines from a selected instance to the entities it expanded. ⛔ Never a side colour — the marker
    /// is no unit, and a line in a side colour would read as a command line. Violet: no side, no affiliation colour and no damage colour is.
    /// </summary>
    public static class MacroMarker
    {
        /// <summary>The marker colour (opaque).</summary>
        public static readonly Rgba32 Colour = new(170, 90, 220, 255);

        /// <summary>The same colour for a thin link line (a little translucent, so overlapping lines stay readable).</summary>
        public static readonly Rgba32 LineColour = new(170, 90, 220, 220);
    }
}

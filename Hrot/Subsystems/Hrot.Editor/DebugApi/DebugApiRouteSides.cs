using System;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ N-8 (CE-4470; 📄 docs/DESIGN_Threaded_Work_Inventory.md §3) — which THREAD a debug-API route's work runs on in the threaded editor.
    /// <list type="bullet">
    /// <item>⭐ SIM side (the default): the work runs on the sim thread between two ticks — it sees all of S, consistent, and a write is applied
    /// there (the same code, retargeted from A to S). Reads are answered at a tick boundary.</item>
    /// <item>UI side: the route drives what only the UI thread owns — the panels' captured models, the film director (camera, stills, the held
    /// shot), the map layers, editor commands and documents (windows), perspectives, the view / follow camera, audio, annotations, the selection (A's own since R-4211).</item>
    /// </list>
    /// Single-threaded hosts have ONE queue and this changes nothing. ⭐ A rail (<c>DebugApiRouteSidesTests</c>) holds every route to a deliberate
    /// side: a new route under a UI prefix is UI-side by construction, everything else sim-side.
    /// </summary>
    public static class DebugApiRouteSides
    {
        /// <summary>The first path segments whose routes drive the UI thread.</summary>
        public static readonly string[] UiPrefixes =
        {
            "/panels", "/director", "/film", "/map", "/view", "/editor", "/documents", "/perspective", "/perspectives", "/audio", "/annotations",
            "/selection",   // ⭐ R-4211 — the selection is the UI's own (A), written by the interaction block
        };

        /// <summary>Is <paramref name="template"/> a UI-side route?</summary>
        public static bool IsUiSide(string template)
        {
            foreach (var p in UiPrefixes)
                if (template.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                    && (template.Length == p.Length || template[p.Length] == '/'))
                    return true;
            return false;
        }
    }
}

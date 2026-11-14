using Hrot.Common.Editing;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>⭐ B-10 K3 — the level one Esc press on the map goes to.</summary>
    public enum EscapeLevel
    {
        /// <summary>An armed modal tool: its own handling (cancel + cleanup) is the level. Nothing else reacts to this press.</summary>
        Tool,
        /// <summary>The camera scope: a follow camera hands the camera back.</summary>
        Camera,
        /// <summary>The Embodied / Direct hat steps out (the camera driver releases the unit / the director).</summary>
        Hat,
        /// <summary>The Command hat (RTS): the press drops a half-done gesture and STOPS — ⛔ Esc never leaves RTS (R-4163).</summary>
        RtsStops,
        /// <summary>Nothing to step out of.</summary>
        Nothing,
    }

    /// <summary>
    /// ⭐⭐ B-10 K3 (📄 docs/UX/DESIGN_Input_And_Commands.md §6c; Tool Model step 6) — the ONE answer to "who does this Esc go to": <b>one level out</b>, innermost first —
    /// <b>an armed tool → the camera scope (a follow) → the hat (Embodied / Direct step out) → RTS stops</b>. Every Esc reader on the map asks it (a camera driver
    /// yields to an armed tool), so a press never moves two levels at once. ⛔ Esc is a key the map's terminal forwards raw (§2.4); this is the BACKEND's
    /// resolver — the gizmo still does its own cleanup when it is the level (an awaiting pick completes its task in its own Esc handler).
    /// </summary>
    public static class EscapeChain
    {
        /// <summary>The level for one press. ⭐ Pure, allocation-free.</summary>
        public static EscapeLevel Resolve(bool toolArmed, bool cameraOwnsEscape, Hat hat)
            => toolArmed ? EscapeLevel.Tool
             : cameraOwnsEscape ? EscapeLevel.Camera
             : hat is Hat.Embodied or Hat.Direct ? EscapeLevel.Hat
             : hat == Hat.Command ? EscapeLevel.RtsStops
             : EscapeLevel.Nothing;
    }
}

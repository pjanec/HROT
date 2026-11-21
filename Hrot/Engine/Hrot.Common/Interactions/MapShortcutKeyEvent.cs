using Fdp.Core;

namespace Hrot.Common.Interactions
{
    /// <summary>
    /// ⭐ CE-1076 (docs/DESIGN_Entity_Control.md §13.6) — a key typed on the map (not in a panel, not in first person), handed by the
    /// canvas' key layer to the map's <c>MenuShortcuts</c>, which runs the menu item of the selected unit that shows this key.
    /// </summary>
    [DataPolicy(DataPolicy.NoReplay)]   // ⭐ the coordinator's replay-UI-state handoff ② (CE-4049) — a UI command: never recorded, never re-fired by a player
    [EventId(8064)]   // after the gizmo interaction events (8061-8063); free on every lane, checked 2026-10-14
    public struct MapShortcutKeyEvent
    {
        /// <summary>The key (Raylib / <c>MapKeyboardKey</c> code: a letter is its upper-case ASCII, Space 32).</summary>
        public int Key;

        /// <summary>Ctrl or Alt was held — then it is not a menu shortcut (Ctrl+C, Alt+… belong to the application).</summary>
        public bool Modified;
    }
}

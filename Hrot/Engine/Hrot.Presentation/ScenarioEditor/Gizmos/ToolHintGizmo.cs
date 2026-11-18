using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.ScenarioEditor.Map;
using Hrot.ScenarioEditor.Tools;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐ B-17 P1 (📄 docs/UX/DESIGN_Map_Interaction.md §2.4, §2.5 <i>"a short hint follows the cursor"</i>; mockup <c>map-interaction-visual.svg</c>) —
    /// the armed tool's hint (<see cref="ToolController.ActiveHint"/>) drawn just below the cursor as GIZMO PRIMITIVES, so a remote terminal draws it
    /// too (the map is a dumb terminal, R-4132). Not for the plain Select tool, and not when the tool's gizmo draws its own line there
    /// (<see cref="ToolController.CursorHintClaimed"/> — the placement ghost's). ⭐ No allocation: the hint is the descriptor's constant string.
    /// </summary>
    /// <remarks>⚠ The cursor is the LOCAL canvas's (<see cref="MapCursorReadout"/>); a remote viewer's cursor is not known here yet, so on a remote
    /// terminal the hint shows only through the status line.</remarks>
    [GizmoProjector]
    public sealed class ToolHintGizmo : IGlobalStatelessGizmo
    {
        /// <summary>Below the cursor's crosshair, in screen pixels.</summary>
        public const float BelowCursorPx = 22f;
        /// <summary>⭐ CE-1977 — the hint line under the tool's name.</summary>
        public const float LinePx = 17f;
        private static readonly Rgba32 Colour = new(232, 232, 238, 235);
        /// <summary>⭐ CE-1977 — the tool's name, in the status line's tool colour.</summary>
        public static readonly Rgba32 TitleColour = new(64, 196, 196, 255);

        // ⭐ B-17 P7 ⑥ — no hint says ✓ any more (Roboto has no ✓, so the map drew "tick"): the hint says "Done", as the on-map button does, and the map's text is
        //   drawn on a backdrop. The hint is the descriptor's constant string, so the steady frame still allocates nothing.

        public void Draw(ISimulationView view, IDebugDrawBuilder draw)
        {
            if (view is not EntityRepository world) return;
            if (MapUiStates.ToolsOf(world) is not { ActiveModal: { } tool } tools || tool.Id == ScenarioToolIds.Select) return;
            if (tools.CursorHintClaimed || tools.ActiveHint is not { Length: > 0 } hint) return;
            if (MapUiStates.CanvasOf(world)?.Get<MapCursorReadout>()?.Latest is not { } at) return;
            // ⭐ CE-1977 — TWO lines under the cursor: the armed tool's NAME (Ditch) and what the next click does — a ⭐ Cursor-style label, never hidden by the
            //   3-D ground. 🔒 User, 2026-11-18: "before I start clicking, I'd like to have an indicator for clicking tool especially". Two primitives, no string built.
            if (tools.ActiveTitle is { Length: > 0 } title)
            {
                draw.DrawTextLong(at.Local.X, at.Local.Y, title, TitleColour, lineOffsetPx: BelowCursorPx, style: GizmoTextStyle.Cursor);
                draw.DrawTextLong(at.Local.X, at.Local.Y, hint, Colour, lineOffsetPx: BelowCursorPx + LinePx, style: GizmoTextStyle.Cursor);
            }
            else draw.DrawTextLong(at.Local.X, at.Local.Y, hint, Colour, lineOffsetPx: BelowCursorPx, style: GizmoTextStyle.Cursor);
        }
    }
}

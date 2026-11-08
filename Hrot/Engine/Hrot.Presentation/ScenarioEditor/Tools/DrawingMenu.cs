using Fdp.Toolkit.Vis2D.Gizmos;
using Hrot.Common.Actions;
using Hrot.Common.Constants;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.2, §2.5 — D11) — the BINDING of the drawing menu: what each catalogue row
    /// (<see cref="DrawingActions"/>, the declaration and the words) runs on an armed <see cref="ShapeEditSession"/>, and where the session's
    /// "too few points" reason goes (the tool's hint line). ⭐ ONE call per drawing tool, at the site that builds the gizmo — the gizmos themselves
    /// (FDP) know neither the catalogue nor the tool controller.
    /// </summary>
    public static class DrawingMenu
    {
        /// <summary>The hint of a drawing tool, from the session's own words (the minimum and the keys) — a descriptor and the gizmo say the same line.</summary>
        public static string HintFor(int minPoints, bool closed = false)
            => new ShapeEditSession(minPoints, closed).KeysHint;

        /// <summary>The Draw Area / Draw Zone tools' hint (3 points).</summary>
        public static readonly string AreaHint = HintFor(3, closed: true);

        /// <summary>The Draw Route tool's hint (2 points).</summary>
        public static readonly string RouteHint = HintFor(2);

        /// <summary>What a catalogue row's id means to a session; null for an id that is not a drawing row.</summary>
        public static ShapeCommand? CommandOf(int actionId) => actionId switch
        {
            GlobalActionIds.FinishDrawing => ShapeCommand.Finish,
            GlobalActionIds.RemoveLastPoint => ShapeCommand.RemoveLast,
            GlobalActionIds.CancelDrawing => ShapeCommand.Cancel,
            _ => null,
        };

        /// <summary>
        /// Gives <paramref name="session"/> its menu (<paramref name="editing"/>: the editor of a drawn shape or route — Finish and Cancel only) and, when
        /// <paramref name="tools"/> is given, puts the session's too-few reason on the armed tool's hint line until the next click.
        /// </summary>
        public static void Attach(ShapeEditSession session, ToolController? tools = null, bool editing = false)
        {
            session.Menu = new ShapeMenu(DrawingActions.Json(editing), CommandOf);
            if (tools != null) session.ReasonChanged += reason => tools.SetHint(reason);
        }
    }
}

using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Constants;
using Hrot.Common.Editing;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.2, §2.5 — D11; 📄 DESIGN_Action_Catalogue.md §9) — the drawing menu, declared ONCE and WORDED once:
    /// <i>Finish · Remove last point · Cancel</i>, whose target is the ARMED DRAWING TOOL (<see cref="ActionTarget.Drawing"/>); the editor of a drawn shape
    /// or route (<see cref="ActionTarget.ShapeEdit"/>) offers Finish and Cancel only. Right-click on the map opens it — right-click no longer finishes or
    /// cancels anything. The BINDING (which command a row runs on the armed session) is <c>DrawingMenu</c> in Hrot.Presentation.
    /// </summary>
    public static class DrawingActions
    {
        // ── the words — ONE place ──────────────────────────────────────────────────────────────────────────────────────
        public const string FinishLabel = "Finish";
        public const string RemoveLastLabel = "Remove last point";
        public const string CancelLabel = "Cancel";

        /// <summary>The shortcuts the menu shows (the keys the gizmos honour — Enter, Backspace, Esc).</summary>
        public const string FinishKey = "Enter", RemoveLastKey = "Backspace", CancelKey = "Esc";

        public static void DeclareInto(ActionCatalogue catalogue)
        {
            const ActionTarget Both = ActionTarget.Drawing | ActionTarget.ShapeEdit;
            catalogue.Declare(new ActionDecl(GlobalActionIds.FinishDrawing, FinishLabel, ActionGroup.Tool, 10, Both) { Shortcut = FinishKey });
            catalogue.Declare(new ActionDecl(GlobalActionIds.RemoveLastPoint, RemoveLastLabel, ActionGroup.Tool, 20, ActionTarget.Drawing) { Shortcut = RemoveLastKey });
            catalogue.Declare(new ActionDecl(GlobalActionIds.CancelDrawing, CancelLabel, ActionGroup.Tool, 30, Both) { Shortcut = CancelKey });
        }

        /// <summary>The menu rows for an armed drawing tool (<paramref name="editing"/> false) or the editor of a drawn shape / route (true), as JSON.</summary>
        public static string Json(bool editing, ActionCatalogue? catalogue = null)
            => MenuComposer.Json(ActionContext.ForTool(editing ? ActionTarget.ShapeEdit : ActionTarget.Drawing, EditingMode.Edit), catalogue);

        /// <summary>
        /// The menu of an EDITOR (a drawn shape's vertices, a route's waypoints): its own rows first (Insert point after …), a separator, then the
        /// catalogue's Finish and Cancel. ⭐ ONE composition of the shared rows — the editor adds only what is its own.
        /// </summary>
        public static string JsonWith(params ContextMenuItemDto[] own)
        {
            var shared = MenuComposer.Compose(ActionContext.ForTool(ActionTarget.ShapeEdit, EditingMode.Edit));
            var rows = new System.Collections.Generic.List<ContextMenuItemDto>(own);
            if (own.Length > 0 && shared.Length > 0) rows.Add(new ContextMenuItemDto { IsSeparator = true });
            rows.AddRange(shared);
            return System.Text.Json.JsonSerializer.Serialize(rows, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        }
    }
}

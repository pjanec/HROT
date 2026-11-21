using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Constants;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐ A6 (W-3, 📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the run-mode moves on a MOMENT, declared ONCE and WORDED once: the timeline's
    /// <i>DAR / AAR / Go live from {t}</i>, <i>Take checkpoint here</i>, and the mode indicator's <i>DAR — the last N</i>, <i>DAR from the start</i>,
    /// <i>Run to …</i>. Every surface takes the words from here (<see cref="DarFrom"/> …); the BINDING (what runs, why greyed) is the surface's
    /// that holds the run choice (<c>RunModeBinding</c> in Hrot.Presentation).
    /// </summary>
    public static class RunModeActions
    {
        /// <summary>The DAR windows the mode indicator offers (seconds back from now).</summary>
        public static readonly double[] DarWindows = { 30, 120, 600 };

        // ── the words — ONE place ──────────────────────────────────────────────────────────────────────────
        public static string DarFrom(string? time) => $"DAR from {time}";
        public const string DarFromThisEvent = "DAR from this event";
        public static string AarFrom(string? time) => $"AAR from {time}";
        public static string GoLiveFrom(string? time) => $"Go live from {time}";
        public const string TakeCheckpointHereLabel = "Take checkpoint here";
        public static string DarLast(double seconds) => $"DAR — the last {(seconds < 60 ? $"{seconds:0} s" : $"{seconds / 60:0} min")}";
        public const string DarFromStartLabel = "DAR from the start";
        public const string RunToLabel = "Run to …";

        private const ActionTarget Clicked = ActionTarget.Moment | ActionTarget.Event;

        /// <summary>The DAR rows' glyph — FontAwesome 6 <c>ClockRotateLeft</c> (the mode indicator's DAR glyph; Hrot.Common has no font table).</summary>
        public const string DarGlyph = "\uf1da";

        public static void DeclareInto(ActionCatalogue catalogue)
        {
            // the timeline: a clicked moment / event
            catalogue.Declare(new ActionDecl(GlobalActionIds.DarFromHere, "DAR from {t}", ActionGroup.Run, 10, ActionTarget.Moment)
                { Modes = ActionModes.InLive, Offer = c => Row(GlobalActionIds.DarFromHere, DarFrom(c.Time), DarGlyph) });
            catalogue.Declare(new ActionDecl(GlobalActionIds.DarFromEvent, DarFromThisEvent, ActionGroup.Run, 10, ActionTarget.Event)
                { Modes = ActionModes.InLive, Offer = c => Row(GlobalActionIds.DarFromEvent, c.Lane is { Length: > 0 } l ? $"{DarFromThisEvent} ({l})" : DarFromThisEvent, DarGlyph) });
            catalogue.Declare(new ActionDecl(GlobalActionIds.AarFromHere, "AAR from {t}", ActionGroup.Run, 20, Clicked)
                { Modes = ActionModes.InLive, Offer = c => Row(GlobalActionIds.AarFromHere, AarFrom(c.Time)) });
            catalogue.Declare(new ActionDecl(GlobalActionIds.GoLiveFromHere, "Go live from {t}", ActionGroup.Run, 30, Clicked | ActionTarget.Playhead)
                { Modes = ActionModes.InReview, Offer = c => Row(GlobalActionIds.GoLiveFromHere, GoLiveFrom(c.Target == ActionTarget.Playhead ? "here" : c.Time)) });
            catalogue.Declare(new ActionDecl(GlobalActionIds.TakeCheckpointHere, TakeCheckpointHereLabel, ActionGroup.Run, 40, Clicked | ActionTarget.Playhead)
                { Modes = ActionModes.InLive | ActionModes.InReview });

            // the mode indicator: the playhead
            catalogue.Declare(new ActionDecl(GlobalActionIds.DarLastWindow, "DAR — the last N", ActionGroup.Run, 10, ActionTarget.Playhead)
                { Modes = ActionModes.InLive, Expand = _ => DarWindowRows() });
            catalogue.Declare(new ActionDecl(GlobalActionIds.DarFromStart, DarFromStartLabel, ActionGroup.Run, 11, ActionTarget.Playhead) { Modes = ActionModes.InLive });
            catalogue.Declare(new ActionDecl(GlobalActionIds.RunTo, RunToLabel, ActionGroup.Run, 50, ActionTarget.Playhead) { Modes = ActionModes.InEdit | ActionModes.InPreview });
        }

        private static ContextMenuItemDto Row(int id, string label, string? icon = null) => new() { Id = id, Label = label, Icon = icon };

        /// <summary>One row per DAR window; the window rides in <see cref="ContextMenuItemDto.Priority"/> (seconds) so the binding knows which.</summary>
        private static IEnumerable<ContextMenuItemDto> DarWindowRows()
        {
            foreach (var w in DarWindows) yield return new ContextMenuItemDto { Id = GlobalActionIds.DarLastWindow, Label = DarLast(w), Priority = (int)w };
        }
    }
}

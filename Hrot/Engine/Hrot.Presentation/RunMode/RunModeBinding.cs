using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Actions;
using Hrot.Common.Editing;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ A6 (W-3, 📄 docs/UX/DESIGN_Action_Catalogue.md §9) — the run-mode moves on a moment, as a surface offers them: the catalogue's
/// <see cref="ActionGroup.Run"/> rows for the target (<see cref="RunModeActions"/> — the declaration and the words), each bound by the surface
/// that holds the run choice (the timeline's cursor, the toolbar). ⭐ Built on a click / a menu open, never per frame.
/// </summary>
public static class RunModeBinding
{
    /// <summary>What one row does here: what runs, and why it is greyed (null = it can run).</summary>
    public readonly record struct Binding(Action Run, string? Why = null);

    /// <summary>A composed, bound row: the declared id and words, what runs, why greyed; <see cref="Window"/> is a DAR window's seconds (else null).</summary>
    public readonly record struct Row(int Id, string Label, Action Run, string? Why, int? Window, string? Icon = null)
    {
        /// <summary>The words with a greyed row's reason, as the timeline and the mode menu show it (<i>"label — reason"</i>).</summary>
        public string Shown => Why is null ? Label : $"{Label} — {Why}";

        /// <summary><see cref="Shown"/> after the row's glyph, for a surface whose items carry the glyph in the label (the timeline).</summary>
        public string ShownWithIcon => Icon is { Length: > 0 } i ? $"{i} {Shown}" : Shown;
    }

    /// <summary>The one mode vocabulary (§3 row 9) from the transport's: a DAR and an AAR are both Review.</summary>
    public static EditingMode ModeOf(TransportMode mode) => mode switch
    {
        TransportMode.Preview => EditingMode.Preview,
        TransportMode.Live => EditingMode.Live,
        TransportMode.Review or TransportMode.Replay => EditingMode.Review,
        _ => EditingMode.Edit,
    };

    /// <summary>
    /// The run-mode rows the catalogue offers for <paramref name="context"/>, each through <paramref name="bind"/> — null = this surface does not
    /// offer it in this state (hidden, e.g. <i>Go live from here</i> in a DAR).
    /// </summary>
    public static List<Row> Rows(ActionContext context, Func<ContextMenuItemDto, Binding?> bind, ActionCatalogue? catalogue = null)
    {
        var rows = new List<Row>();
        foreach (var dto in MenuComposer.Compose(context, catalogue))
        {
            if (dto.IsSeparator == true || dto.Id == 0) continue;
            if (bind(dto) is not { } b) continue;
            rows.Add(new Row(dto.Id, dto.Label ?? "", b.Run, b.Why, dto.Priority, dto.Icon));
        }
        return rows;
    }
}

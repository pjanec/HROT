using System.Numerics;
using NodeEditor.Core.Widgets;

namespace Fdp.Presentation.Forms;

/// <summary>
/// ⭐ U-11 / D0 (docs/UX/DESIGN_Details_Forms.md §6.3) — THE ONE status palette: <b>Error</b> (refused, lost, invalid input — and red means error everywhere, Q-V4), <b>Pending</b> (an edit in flight, a conflict band),
/// <b>Warning</b> (a validation warning that is NOT about an edit — what the Blueprint node drawers called <c>EditorColors.Warning</c>), <b>Info</b> (reset-to-type, a note), <b>Muted</b> (locked rows, hints, units) and the
/// <b>ErrorRowTint</b> a refused row's background takes. ⭐ Blueprints' <c>EditorColors</c> is folded in here (its 41 uses route to this class; the class is gone) — a second palette is a defect (R-174).
/// <para>The values live in <see cref="IconPalette"/> (NodeEditor.Core — the pickers and the glyph icons reach them too); this class is the forms' named view of them, so a mark's colour and a text colour can never differ.</para>
/// </summary>
public static class StatusPalette
{
    public static Vector4 Error => IconPalette.Error;
    public static Vector4 Pending => IconPalette.Pending;
    public static Vector4 Warning => IconPalette.Warning;
    public static Vector4 Info => IconPalette.Info;
    public static Vector4 Muted => IconPalette.Muted;
    /// <summary><see cref="Error"/> at 18 % — the refused row's background (§6.3).</summary>
    public static Vector4 ErrorRowTint => IconPalette.Error with { W = 0.18f };
}

using System;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ What an outline click resolves to: a heading and the rows behind it.
///
/// <para>📌 <b><c>Q32</c> ruling 2</b> — <i>"click a <b>global</b> in My Blueprint ⇒ the list of
/// globals / working state. Click a <b>local</b> ⇒ the locals of the <b>currently selected
/// graph</b>."</i> ⭐ Both arms are the same shape; ⛔ what differs is which source the OUTLINE
/// resolved, which is the only half that knows about a host's own sections.</para>
///
/// <para>⭐ A <c>null</c> <see cref="Source"/> means <i>"this selection is not a variable"</i> — a
/// graph, a function, a node — ⇒ the details host lets go of its list rather than leaving a stale one
/// beside an unrelated selection.</para>
/// </summary>
/// <param name="SelectedVariablePath">
/// ⭐⭐ <b>Batch 84 item <c>4a</c> — WHICH ROW was clicked.</b> 📌
/// <c>DESIGN_Variable_Details_And_Editing.md</c> §1: <i>"Clicking any row in Local Variables routes
/// Details to the locals-of-this-graph table <b>with that row highlighted</b>"</i> ⇒ <i>"the routing
/// key is <c>(asset, section)</c> <b>+ a highlight</b>."</i>
/// <para>🔴 <b>Before this, the TYPE could not express it</b> — the record carried a heading and a
/// source and nothing else, so no row could ever be highlighted however the panel drew.</para>
/// </param>
/// <param name="HeadingAtReadTime">
/// ⭐⭐⭐ <b>Batch 84 item <c>4b</c> — a heading resolved WHEN DRAWN, not when clicked.</b>
/// <para>📐 <b>Measured before building, and the handoff's premise was half right:</b> the graph-scoped
/// arm's ROWS already follow the canvas — <c>BlueprintLocalVariableSchemaSource</c> reads the graph
/// through a <c>Func&lt;Graph?&gt;</c> and resolves it at call time. ⛔ <b>The HEADING did not:</b>
/// <c>$"Local Variables — {graph.Name}"</c> was computed once, at click time. ⇒ ⚠⚠ <b>the failure is
/// worse than "stale": switch graph and the rows update while the label keeps naming the OLD graph</b>,
/// so the panel contradicts itself.</para>
/// <para>⭐ A delegate rather than a stored <c>Guid</c> — the same shape the row source already uses,
/// so there is ONE way the graph-scoped arm follows the canvas, not two.</para>
/// </param>
public readonly record struct VariableOutlineSelection(
    string?             Heading,
    IVariableRowSource? Source,
    string?             SelectedVariablePath = null,
    Func<string?>?      HeadingAtReadTime    = null)
{
    /// <summary>⭐ The "not a variable" selection, named rather than spelled out at each call site.</summary>
    public static VariableOutlineSelection None => new(null, null);

    /// <summary>True when this selection carries a list to show.</summary>
    public bool HasRows => Heading != null && Source != null;

    /// <summary>
    /// ⭐⭐ <b>What a panel must render.</b> ⛔ Never <see cref="Heading"/> directly: that is the
    /// click-time snapshot and is the fallback only for arms that genuinely cannot change
    /// (the asset-scoped sections, whose name does not depend on the canvas).
    /// </summary>
    public string? CurrentHeading => HeadingAtReadTime?.Invoke() ?? Heading;
}

/// <summary>
/// ⭐⭐ Implemented by an OUTLINE window: it publishes what the designer picked.
/// ⛔ It does not know who listens, and it must not — <c>U-6</c>'s Details host is one listener today
/// and the shared cross-host outline (sequencing row 61) will be another.
/// </summary>
public interface IVariableOutlineSelectionSource
{
    /// <summary>Raised when the outline selection changes, including to a non-variable.</summary>
    event Action<VariableOutlineSelection>? VariableSelectionChanged;
}

/// <summary>
/// ⭐⭐ Implemented by a DETAILS window: it can show the list an outline resolved.
///
/// <para>📌 <b><c>Q32</c> ruling 6</b> — <i>"the same Details panel is REUSED for every asset
/// type"</i> ⇒ ⭐ the contract is stated here, in the shared assembly, so a BTree or HSM details host
/// implements the same one rather than growing a parallel path.</para>
/// </summary>
public interface IVariableDetailsHost
{
    /// <summary>Show (or, for <see cref="VariableOutlineSelection.None"/>, stop showing) a list.</summary>
    void ShowVariables(VariableOutlineSelection selection);

    /// <summary>
    /// ⭐⭐ Supplies the run state, so the hosted list's ONE Value column switches meaning
    /// *(row 58, <c>Q32</c> ruling 3: "initial when not running, current when running or paused")*.
    ///
    /// <para>⭐ <b>On the contract, not on a constructor</b>, because the registrar is what HOLDS the
    /// debug-session registry and the details host is what NEEDS it — ⛔ threading it through the
    /// composition root would be the seam batches 79–82 each lost a surface to.</para>
    /// </summary>
    void SetRunStateSource(System.Func<VariableRunState> runState);
}

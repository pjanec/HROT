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
public readonly record struct VariableOutlineSelection(string? Heading, IVariableRowSource? Source)
{
    /// <summary>⭐ The "not a variable" selection, named rather than spelled out at each call site.</summary>
    public static VariableOutlineSelection None => new(null, null);

    /// <summary>True when this selection carries a list to show.</summary>
    public bool HasRows => Heading != null && Source != null;
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

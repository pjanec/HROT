namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-11 / Q22-E1 — collapses a continuous edit gesture into <b>one</b> undo entry.
///
/// <para>
/// An ImGui slider drag or text-box entry fires a change per frame / per keystroke. Recording each
/// one would make Ctrl+Z walk back through a drag character by character, and would evict the rest
/// of the undo history in a single gesture. Instead the baseline is captured when the widget becomes
/// <em>active</em> and paired with the final value when it is <em>deactivated after edit</em>.
/// </para>
///
/// <para>
/// ⚠ The baseline must be taken on <c>ImGui.IsItemActivated()</c>, not on
/// <c>IsItemDeactivatedAfterEdit()</c>: the latter fires <em>after</em> the value has already
/// changed, so by then the pre-edit value is gone.
/// </para>
///
/// <para>
/// Deliberately free of any ImGui dependency — the caller passes the two booleans in. That keeps the
/// coalescing rule headlessly testable, which the widgets it serves are not.
/// </para>
/// </summary>
/// <typeparam name="T">The edited value's type (captured by value/reference as-is).</typeparam>
public sealed class ContinuousEditCoalescer<T>
{
    private bool _hasBaseline;
    private T?   _baseline;

    /// <summary>True while a gesture is in flight (baseline captured, not yet committed).</summary>
    public bool IsTracking => _hasBaseline;

    /// <summary>
    /// Captures <paramref name="current"/> as the gesture's baseline, if one is not already held.
    /// Call when the widget reports it just became active. Re-entrant calls during the same gesture
    /// are ignored, so the baseline is the value from <em>before</em> the first change.
    /// </summary>
    public void BeginIfNeeded(bool activated, T current)
    {
        if (!activated || _hasBaseline) return;
        _baseline    = current;
        _hasBaseline = true;
    }

    /// <summary>
    /// Reports the gesture's baseline exactly once, on the frame the widget commits.
    /// </summary>
    /// <param name="deactivatedAfterEdit">The widget's "finished, and the value changed" signal.</param>
    /// <param name="baseline">The value held before the gesture began.</param>
    /// <returns><c>true</c> on the single committing frame; <c>false</c> otherwise.</returns>
    public bool TryCommit(bool deactivatedAfterEdit, out T baseline)
    {
        baseline = default!;
        if (!deactivatedAfterEdit || !_hasBaseline) return false;

        baseline     = _baseline!;
        _baseline    = default;
        _hasBaseline = false;
        return true;
    }

    /// <summary>Drops an in-flight gesture without committing (e.g. the session closed mid-drag).</summary>
    public void Abandon()
    {
        _baseline    = default;
        _hasBaseline = false;
    }
}

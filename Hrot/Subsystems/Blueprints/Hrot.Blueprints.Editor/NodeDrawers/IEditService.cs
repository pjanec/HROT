using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Blueprint node edit command dispatcher — how a drawer or inspector reports an edit.
///
/// <para>
/// BP-11 (Q22-B1): <see cref="RecordPropertyEdit"/> and <see cref="NotifyStructureChanged"/> used to
/// exist only on the concrete <see cref="EditService"/>, so drawers reached them through
/// <c>(_editService as EditService)?.…</c> — and because a downcast against a test double just
/// yields null, an edit that skipped undo looked identical to one that could not be undone. That
/// ambiguity is why every drawer edit was silently non-undoable. They are part of the contract now.
/// </para>
///
/// <para>
/// Implementations that genuinely have nothing to do (headless test doubles) may implement the two
/// added members as no-ops — but they have to say so explicitly.
/// </para>
/// </summary>
public interface IEditService
{
    /// <summary>Marks the asset as having unsaved changes.</summary>
    void MarkDirty(BlueprintAsset asset);

    /// <summary>
    /// Records a property change as an undoable action and marks the asset dirty. The
    /// <paramref name="apply"/> delegate is invoked as part of recording, so callers mutate
    /// <em>through</em> this method rather than before it.
    /// </summary>
    /// <param name="asset">The owning asset (used for dirty-marking).</param>
    /// <param name="description">Short description, shown as the undo entry's label.</param>
    /// <param name="apply">Applies the new value.</param>
    /// <param name="undo">Restores the previous value. The caller must snapshot it beforehand.</param>
    void RecordPropertyEdit(BlueprintAsset asset, string description, Action apply, Action undo);

    /// <summary>
    /// Signals that an edit changed the asset's projected graph <b>structure</b> (a node's pin set or
    /// a field that drives pin projection) rather than a cosmetic value, so derived views re-project.
    /// Data-level: the caller does not know which views exist.
    /// </summary>
    void NotifyStructureChanged(BlueprintAsset asset);
}

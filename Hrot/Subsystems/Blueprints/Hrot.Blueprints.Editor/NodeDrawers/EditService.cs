using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Real implementation of <see cref="IEditService"/> that records property edits as
/// undoable commands on the Blueprint <see cref="CommandHistory"/> and marks the asset dirty.
///
/// <para>
/// The <c>Context</c> property may be swapped at any time (e.g. when the active document
/// changes) so that node drawers always route edits through the currently-open document's
/// command history.  When <c>Context</c> is <c>null</c> the service degrades gracefully:
/// <see cref="MarkDirty"/> is a no-op and <see cref="RecordPropertyEdit"/> applies the
/// change immediately without undo history.
/// </para>
/// </summary>
public sealed class EditService : IEditService
{
    /// <summary>
    /// The active document context.  Set this to the current document's
    /// (<see cref="CommandHistory"/>, dirty-marking delegate) tuple.
    /// </summary>
    public EditServiceContext? Context { get; set; }

    // ── IEditService ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void MarkDirty(BlueprintAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Context?.MarkDirty(asset);
    }

    // ── extended API (used by BlueprintCommandSink) ──────────────────────────

    /// <summary>
    /// Signals that an edit changed the projected graph <b>structure</b> (a node's pin set,
    /// links, or a field that drives pin projection) rather than a cosmetic value. This is a
    /// data-level notification — the caller does NOT know which views derive from the asset.
    /// The composition root (<see cref="EditServiceContext.OnStructureChanged"/>) is responsible
    /// for refreshing the derived views (e.g. the canvas graph model rebuilds its projection).
    /// No-op when there is no active document context.
    /// </summary>
    public void NotifyStructureChanged(BlueprintAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Context?.OnStructureChanged?.Invoke(asset);
    }

    /// <summary>
    /// Records a property change as an undoable command and marks the asset dirty.
    ///
    /// <para>
    /// When <see cref="Context"/> is non-null the command is pushed onto the
    /// history so Ctrl-Z reverses it.  When <see cref="Context"/> is null the
    /// change is applied immediately without undo history.
    /// </para>
    /// </summary>
    /// <param name="asset">The owning asset (used for dirty-marking).</param>
    /// <param name="description">Short description shown in undo history.</param>
    /// <param name="apply">Delegate that applies the new value.</param>
    /// <param name="undo">Delegate that restores the old value.</param>
    public void RecordPropertyEdit(
        BlueprintAsset asset,
        string description,
        Action apply,
        Action undo)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(undo);

        if (Context is { } ctx)
        {
            var cmd = new PropertyEditCommand(description, apply, undo);
            ctx.History.Execute(cmd);   // Execute() calls cmd.Execute() → apply()
            ctx.MarkDirty(asset);
        }
        else
        {
            // No active document — apply without history.
            apply();
        }
    }
}

/// <summary>
/// Holds the per-document state required by <see cref="EditService"/>: the
/// <see cref="CommandHistory"/> and the dirty-marking callback.
/// </summary>
public sealed class EditServiceContext
{
    /// <summary>The command history for the active document.</summary>
    public CommandHistory History { get; }

    /// <summary>Marks the given asset as dirty in the editor.</summary>
    public Action<BlueprintAsset> MarkDirty { get; }

    /// <summary>
    /// Optional observer invoked when an editing surface reports a <b>structural</b> change to the
    /// asset (see <see cref="EditService.NotifyStructureChanged"/>). The document's composition root
    /// wires this to refresh the views that project the asset — e.g. rebuilding the canvas graph
    /// model — so a Details-panel edit updates the canvas without the drawer referencing it.
    /// Null when the document has no derived views to refresh (e.g. headless tests).
    /// </summary>
    public Action<BlueprintAsset>? OnStructureChanged { get; }

    public EditServiceContext(
        CommandHistory history,
        Action<BlueprintAsset> markDirty,
        Action<BlueprintAsset>? onStructureChanged = null)
    {
        History            = history    ?? throw new ArgumentNullException(nameof(history));
        MarkDirty          = markDirty  ?? throw new ArgumentNullException(nameof(markDirty));
        OnStructureChanged = onStructureChanged;
    }
}

/// <summary>
/// An <see cref="IGraphCommand"/> that wraps a property change as a pair of
/// apply/undo delegates, making it first-class in the Blueprint <see cref="CommandHistory"/>.
/// </summary>
internal sealed class PropertyEditCommand : IGraphCommand
{
    private readonly string _description;
    private readonly Action _apply;
    private readonly Action _undo;

    public string Description => _description;

    public PropertyEditCommand(string description, Action apply, Action undo)
    {
        _description = description ?? "";
        _apply       = apply;
        _undo        = undo;
    }

    public void Execute() => _apply();
    public void Undo()    => _undo();
}

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Represents an open document in the AI editor. Tracks the backing asset, its kind,
/// an opaque <see cref="ViewState"/> slot (filled by the canvas in Phase 2 to persist
/// pan/zoom/selection across activations), and a dirty flag.
/// </summary>
public sealed class AiDocument
{
    private bool _isDirty;

    /// <param name="asset">The backing editable asset.</param>
    /// <param name="kind">The asset kind (BTree, HSM, Blueprint, …).</param>
    public AiDocument(IEditableAsset asset, AssetKind kind)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Kind  = kind;
    }

    /// <summary>The backing editable asset.</summary>
    public IEditableAsset Asset { get; }

    /// <summary>The asset kind (BTree, HSM, Blueprint, …).</summary>
    public AssetKind Kind { get; }

    /// <summary>
    /// Opaque view-state slot. The canvas (Phase 2) stores a <c>GraphView</c> instance
    /// here so pan/zoom/selection are preserved when a document is deactivated and
    /// later re-activated. The manager only stores and retrieves this slot — it never
    /// inspects or interprets the value.
    /// </summary>
    public object? ViewState { get; set; }

    /// <summary>True when the document has unsaved changes.</summary>
    public bool IsDirty => _isDirty;

    /// <summary>Marks the document as having unsaved changes.</summary>
    public void MarkDirty()  { _isDirty = true; }

    /// <summary>Clears the dirty flag (called after a successful save).</summary>
    public void MarkClean()  { _isDirty = false; }
}

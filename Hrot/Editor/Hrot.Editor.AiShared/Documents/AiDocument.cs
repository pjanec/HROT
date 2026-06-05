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
    public IEditableAsset Asset { get; private set; }

    /// <summary>The asset kind (BTree, HSM, Blueprint, …).</summary>
    public AssetKind Kind { get; }

    /// <summary>
    /// Replaces the backing asset with a freshly projected version after a hot reload.
    /// The new asset must have the same <see cref="IEditableAsset.AssetId"/> and
    /// <see cref="AssetKind"/>; if not, the call is a no-op.
    /// <para>
    /// Positions and comments are preserved because the projector reads layout data
    /// from the <c>[BTreeLayout]</c>/<c>[HsmLayout]</c> attribute methods on reload,
    /// so the reconciled asset already carries the correct visual positions by
    /// <c>VisualId</c>/<c>StableId</c>.
    /// </para>
    /// </summary>
    public void ReconcileAsset(IEditableAsset newAsset)
    {
        if (newAsset is null) return;
        if (newAsset.AssetId != Asset.AssetId) return;
        if (newAsset.Kind    != Asset.Kind)    return;
        Asset = newAsset;
        // The reload produced a clean (non-dirty) asset; clear any stale dirty flag.
        _isDirty = false;
    }

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

    /// <summary>
    /// Stitches runtime indices (KernelBlobIndex / FlatIndex) from the freshly
    /// assembly-projected <paramref name="fresh"/> asset onto this document's
    /// JSON-loaded editor model (design §6.6 / PU-302).
    /// <para>
    /// Dispatches to <see cref="IStitchableAsset.StitchRuntimeIndices"/> on the
    /// backing asset.  No-op when <see cref="Asset"/> does not implement the interface.
    /// </para>
    /// <para>
    /// <b>Must NOT call MarkDirty</b> (PU-602 constraint).
    /// </para>
    /// </summary>
    public void StitchRuntimeIndices(IEditableAsset? fresh)
    {
        if (Asset is IStitchableAsset stitchable)
            stitchable.StitchRuntimeIndices(fresh);
        // Non-stitchable assets (Blueprint, hand-authored) are a no-op here;
        // ReconcileFromCatalog routes them through ReconcileAsset instead.
    }
}

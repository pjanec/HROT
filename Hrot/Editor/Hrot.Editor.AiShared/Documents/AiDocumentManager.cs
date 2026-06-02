namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// Manages the set of open AI editor documents and the active document.
/// <para>
/// <b>Responsibilities:</b>
/// <list type="bullet">
///   <item>Tracks the open document list across asset kinds.</item>
///   <item><see cref="Open"/> focuses an already-open document or opens a new one and activates it.</item>
///   <item><see cref="Activate"/> sets the active document, invokes the perspective-switch abstraction
///       with the document's kind name, and raises <see cref="ActiveChanged"/>.</item>
///   <item><see cref="Close"/> removes a document and activates the next one in the list (or none).</item>
///   <item>Preserves the opaque <see cref="AiDocument.ViewState"/> across activations — the canvas
///       (Phase 2) stores a <c>GraphView</c> there and the manager does not inspect it.</item>
/// </list>
/// </para>
/// <para>
/// <b>Decoupled from ImGui / WindowManager:</b> perspective switching is delegated to an
/// injected <see cref="IPerspectiveSwitcher"/> (or a plain <see cref="Action{T}"/> overload).
/// No ImGui calls or window construction happen inside this class.
/// </para>
/// </summary>
public sealed class AiDocumentManager
{
    private readonly List<AiDocument> _documents = new();
    private readonly Action<string> _perspectiveSwitchCallback;
    private readonly Action<AiDocument?>? _focusCallback;

    private AiDocument? _active;

    /// <summary>
    /// Initializes the manager.
    /// </summary>
    /// <param name="perspectiveSwitcher">
    ///   Called with the asset-kind name (e.g. <c>"BTree"</c>) whenever a document is activated.
    /// </param>
    /// <param name="focusCallback">
    ///   Optional callback invoked after activation (e.g. to focus the canvas window for the
    ///   active document's kind). Receives the new active document (or <c>null</c> if none).
    /// </param>
    public AiDocumentManager(
        IPerspectiveSwitcher perspectiveSwitcher,
        Action<AiDocument?>? focusCallback = null)
        : this(
            perspectiveSwitcher != null
                ? perspectiveSwitcher.SwitchPerspective
                : throw new ArgumentNullException(nameof(perspectiveSwitcher)),
            focusCallback)
    { }

    /// <summary>
    /// Initializes the manager with a plain callback for the perspective switch.
    /// Convenient for tests and composition roots that prefer a lambda.
    /// </summary>
    /// <param name="perspectiveSwitchCallback">
    ///   Called with the asset-kind name on every activation.
    /// </param>
    /// <param name="focusCallback">
    ///   Optional post-activation focus callback.
    /// </param>
    public AiDocumentManager(
        Action<string> perspectiveSwitchCallback,
        Action<AiDocument?>? focusCallback = null)
    {
        _perspectiveSwitchCallback = perspectiveSwitchCallback
            ?? throw new ArgumentNullException(nameof(perspectiveSwitchCallback));
        _focusCallback = focusCallback;
    }

    /// <summary>The currently active document, or <c>null</c> if nothing is open.</summary>
    public AiDocument? Active => _active;

    /// <summary>All open documents (in open order).</summary>
    public IReadOnlyList<AiDocument> OpenDocuments => _documents;

    /// <summary>
    /// Fires whenever the active document changes (on <see cref="Open"/>, <see cref="Activate"/>,
    /// and <see cref="Close"/>).
    /// </summary>
    public event Action? ActiveChanged;

    /// <summary>
    /// Fires when a new document is opened (not when an already-open document is re-activated).
    /// Subscribers use this to populate <see cref="AiDocument.ViewState"/> via a factory.
    /// <para>
    /// Fired <em>before</em> <see cref="Activate"/> is called for the new document, so the
    /// canvas window will find <see cref="AiDocument.ViewState"/> already populated on the
    /// first activation.
    /// </para>
    /// </summary>
    public event Action<AiDocument>? DocumentOpened;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the document for <paramref name="asset"/>.
    /// <list type="bullet">
    ///   <item>If a document for this asset is already open, focuses it (calls <see cref="Activate"/>).</item>
    ///   <item>Otherwise adds a new <see cref="AiDocument"/> and activates it.</item>
    /// </list>
    /// </summary>
    /// <param name="asset">The asset to open.</param>
    /// <returns>The opened (or existing) document.</returns>
    public AiDocument Open(IEditableAsset asset)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        // Re-focus an already-open document.
        var existing = _documents.FirstOrDefault(d => d.Asset.AssetId == asset.AssetId);
        if (existing is not null)
        {
            Activate(existing);
            return existing;
        }

        // Create a new document, fire DocumentOpened (so factories can populate ViewState),
        // then activate it so the canvas window renders it immediately.
        var doc = new AiDocument(asset, asset.Kind);
        _documents.Add(doc);
        DocumentOpened?.Invoke(doc);
        Activate(doc);
        return doc;
    }

    /// <summary>
    /// Sets <paramref name="doc"/> as the active document:
    /// <list type="number">
    ///   <item>Sets <see cref="Active"/>.</item>
    ///   <item>Invokes the perspective-switch callback with the asset's kind name.</item>
    ///   <item>Invokes the optional focus callback.</item>
    ///   <item>Fires <see cref="ActiveChanged"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="doc">
    ///   The document to activate. Must be in <see cref="OpenDocuments"/>; if it is not,
    ///   the call is a no-op.
    /// </param>
    public void Activate(AiDocument doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        // Guard: only activate documents we own.
        if (!_documents.Contains(doc)) return;

        _active = doc;

        // Switch the editor perspective to match the document's kind.
        _perspectiveSwitchCallback(doc.Kind.ToString());

        // Notify any focus handler (e.g. to bring the canvas to front).
        _focusCallback?.Invoke(_active);

        ActiveChanged?.Invoke();
    }

    /// <summary>
    /// Closes <paramref name="doc"/> and activates the next document in the list
    /// (or the previous one if <paramref name="doc"/> was the last). If no documents
    /// remain, <see cref="Active"/> becomes <c>null</c> and <see cref="ActiveChanged"/> fires.
    /// </summary>
    /// <param name="doc">The document to close.</param>
    public void Close(AiDocument doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        int idx = _documents.IndexOf(doc);
        if (idx < 0) return; // not our document

        _documents.RemoveAt(idx);

        if (_active == doc)
        {
            if (_documents.Count == 0)
            {
                _active = null;
                _perspectiveSwitchCallback(string.Empty);
                _focusCallback?.Invoke(null);
                ActiveChanged?.Invoke();
            }
            else
            {
                // Activate the document that now sits at the same index (or the last one).
                int nextIdx = Math.Min(idx, _documents.Count - 1);
                Activate(_documents[nextIdx]);
            }
        }
        // If the closed doc was not active, no further action needed.
    }
}

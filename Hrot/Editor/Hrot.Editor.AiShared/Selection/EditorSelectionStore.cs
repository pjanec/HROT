using Fdp.Core;

namespace Hrot.Editor.AiShared.Selection;

public sealed class EditorSelectionStore
{
    // Currently-active asset -- the asset whose canvas window has focus.
    private IEditableAsset? _activeAsset;

    // ⭐⭐⭐ L0.1 — the STORAGE IS A SET, keyed by AssetId.
    // 📄 DESIGN_Details_Panel_View_Switching.md §6 `L0.1`: "selection SET on the store —
    //    ActiveSubSelections; ActiveSubSelection becomes the derived single".
    // 📌 R-118: a bridge REPORTS; it does not filter. ⇒ the store must be able to HOLD what a
    //    marquee produces, or the filtering just moves one layer down.
    // ⚠ Never null and never re-allocated for an unchanged selection — see EmptySelection.
    private readonly Dictionary<Guid, IReadOnlyList<IAssetSubSelection>> _subSelectionsByAsset = new();

    /// <summary>
    /// ⭐⭐ The ONE empty instance. 📌 §6 `L0.4`: <i>"return the same list instance when unchanged,
    /// or every view rebuilds per frame."</i> ⛔ A fresh <c>Array.Empty</c> would still be reference-
    /// equal, but a fresh <c>List</c> would not — this makes the guarantee explicit rather than
    /// incidental.
    /// </summary>
    private static readonly IReadOnlyList<IAssetSubSelection> EmptySelection = Array.Empty<IAssetSubSelection>();

    // Set of assets with at least one window currently open.
    private readonly HashSet<Guid> _openAssets = new();

    // ⭐⭐⭐ Batch 95 (95b) — global entity selection, independent of which asset is active AND of
    //    which store you ask. 🔴 It used to be a private field per store, and the editor holds FOUR
    //    stores while exactly ONE of them was ever connected to the selection bridge ⇒ the other
    //    three answered null for ever and every live-value provider bailed on its second line.
    // 📄 AI_Editor_Shared_Infrastructure.md:450 — "SelectedEntity stays global because entities exist
    //    independently of which asset is being edited". ⇒ the fix restores what the design said.
    private readonly SharedEntitySelection _entity;

    public event Action? OnSelectionChanged;

    /// <param name="sharedEntity">
    ///   ⭐⭐ The entity cell this store reads and writes. ⛔ <b>Optional, and a store given none gets
    ///   its OWN</b>, so every standalone and test construction is unchanged. ⚠ A production host with
    ///   more than one store must pass the SAME cell to all of them — 📌 the rail that proves it did is
    ///   on the constructed composition root, not on this signature.
    /// </param>
    public EditorSelectionStore(SharedEntitySelection? sharedEntity = null)
    {
        _entity = sharedEntity ?? new SharedEntitySelection();
        // ⭐ A change made through ANY store reaches every store's subscribers. ⛔ Without this the
        //   cell would be shared and the Details panel would still repaint on its own schedule.
        _entity.Changed += () => OnSelectionChanged?.Invoke();
    }

    /// <summary>
    /// ⭐ The entity cell this store shares. ⚠ Exposed so a rail can assert two stores are on the SAME
    /// bus — 📌 <c>M-22</c>: an argument having been passed is not the claim; one fact reaching both
    /// readers is.
    /// </summary>
    public SharedEntitySelection SharedEntity => _entity;

    /// <summary>The asset whose editor canvas has focus. Set by window-focus handlers.</summary>
    public IEditableAsset? ActiveAsset
    {
        get => _activeAsset;
        set
        {
            if (_activeAsset == value) return;
            _activeAsset = value;
            OnSelectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>L0.1</c> — the FULL sub-selection within the active asset.</b> 📄 §6 <c>L0.1</c>.
    ///
    /// <para>⭐ Never <see langword="null"/> — an empty selection is an empty LIST. 📌 <c>R-118</c>:
    /// <c>null</c> used to mean <i>"nothing"</i> AND <i>"more than one"</i> AND <i>"unresolvable"</i>,
    /// ⛔ three facts flattened into one, which is exactly what the design deletes.</para>
    /// </summary>
    public IReadOnlyList<IAssetSubSelection> ActiveSubSelections
    {
        get => _activeAsset is null ? EmptySelection : GetSubSelections(_activeAsset.AssetId);
        set
        {
            if (_activeAsset is null) return;
            SetSubSelections(_activeAsset.AssetId, value);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The DERIVED single</b> — 📄 §6 <c>L0.1</c>: <i>"<c>ActiveSubSelection</c> becomes the
    /// derived single … every existing reader unchanged."</i>
    ///
    /// <para>⛔⛔ <b><c>Count == 1</c>, deliberately — and this is NOT <c>R-118</c>'s deleted rule
    /// reappearing.</b> 📐 The seven production read sites *(<c>InspectorWindow</c> ×3,
    /// <c>BlueprintDetailsWindow</c> ×4)* each ask <i>"is the selection THIS one node?"</i>. ⭐ Answering
    /// <c>list[0]</c> for a two-node marquee would silently show node 1 of 2 — a behaviour change this
    /// task promises not to make. ⇒ ⭐⭐ <b>the SET carries the whole truth for the new
    /// <c>DetailsContext</c> path; this property preserves today's answer exactly for the old one.</b>
    /// 📌 The genuine home of the <i>"exactly one"</i> rule is <c>L1.4</c>'s PREDICATE
    /// (<c>ctx.Selection is [BlueprintNodeSelection]</c>), which is where the design puts it.</para>
    /// </summary>
    public IAssetSubSelection? ActiveSubSelection
    {
        get
        {
            var all = ActiveSubSelections;
            return all.Count == 1 ? all[0] : null;
        }
        set => ActiveSubSelections = value is null ? EmptySelection : new[] { value };
    }

    /// <summary>Read the full sub-selection for any asset (active or not). ⭐ Never null.</summary>
    public IReadOnlyList<IAssetSubSelection> GetSubSelections(Guid assetId) =>
        _subSelectionsByAsset.GetValueOrDefault(assetId) ?? EmptySelection;

    /// <summary>Read sub-selection for any asset (active or not). ⭐ The derived single — see
    /// <see cref="ActiveSubSelection"/> for why <c>Count == 1</c>.</summary>
    public IAssetSubSelection? GetSubSelection(Guid assetId)
    {
        var all = GetSubSelections(assetId);
        return all.Count == 1 ? all[0] : null;
    }

    /// <summary>
    /// ⭐⭐ Write the full sub-selection for any asset.
    ///
    /// <para>⛔⛔ <b>The no-change guard is what makes a PAN free</b> — 📄 §2b's second sequence:
    /// <i>"AFTER <c>L0.2</c> the same set is written ⇒ <c>Equals(current)</c> ⇒ no event ⇒ unchanged,
    /// no repaint."</i> ⚠ It compares ELEMENTWISE, because the bridges build a fresh list every frame
    /// ⇒ reference equality would never hold and every frame would fire.</para>
    ///
    /// <para>⭐ On no change the STORED INSTANCE IS KEPT, so a reader that caches by reference — and
    /// the context builder in <c>L0.3</c> — sees the same object frame after frame *(§6 <c>L0.4</c>)*.</para>
    /// </summary>
    public void SetSubSelections(Guid assetId, IReadOnlyList<IAssetSubSelection>? selection)
    {
        var incoming = selection is null || selection.Count == 0 ? EmptySelection : selection;
        var current  = _subSelectionsByAsset.GetValueOrDefault(assetId) ?? EmptySelection;
        if (SameSelection(current, incoming)) return;   // ⭐ keeps `current`, does not store `incoming`
        _subSelectionsByAsset[assetId] = incoming;
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Write sub-selection for any asset. Used by windows that are not currently focused.</summary>
    public void SetSubSelection(Guid assetId, IAssetSubSelection? selection) =>
        SetSubSelections(assetId, selection is null ? EmptySelection : new[] { selection });

    /// <summary>
    /// ⭐ Elementwise equality over the two lists. ⚠ ORDER-SENSITIVE, and that is correct rather than
    /// lazy: the bridges enumerate the canvas selection in a stable order, so a differing order IS a
    /// differing selection. ⛔ An order-insensitive compare would need a sort or a set per frame — a
    /// per-frame allocation to answer a question nothing asks.
    /// </summary>
    private static bool SameSelection(
        IReadOnlyList<IAssetSubSelection> a, IReadOnlyList<IAssetSubSelection> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!Equals(a[i], b[i])) return false;
        return true;
    }

    /// <summary>
    /// Globally-selected entity for runtime debug overlay.
    /// ⭐⭐ Batch 95 (<c>95b</c>) — routed through <see cref="SharedEntity"/>, so a selection made on
    /// ANY store is visible from every store. ⛔ The change notification is raised by the cell, not
    /// here, or a store that did not make the change would not repaint.
    /// </summary>
    public Entity? SelectedEntity
    {
        get => _entity.Selected;
        set => _entity.Selected = value;
    }

    // ── WHICH SURFACE the designer is working in (user ruling, 2026-08-18) ──

    /// <summary>
    /// ⭐⭐⭐ <b>The last CONTRIBUTING surface to hold focus</b> — a LATCH, not a live read.
    ///
    /// <para>📌 <b>Why a latch.</b> Clicking INTO the Details panel to edit a value takes focus away
    /// from both contributors. ⛔ A live <i>"who has focus right now?"</i> would answer "neither" and
    /// the panel would flip to whatever the fallback is, mid-edit. ⇒ ⭐ this records who last CLAIMED
    /// it, so a non-contributing window taking focus changes nothing.</para>
    ///
    /// <para>⭐⭐ <b>This is the ordering token</b> the Details panel needed — keyed on FOCUS, which is
    /// observable every frame, instead of on a CLICK, which 📐 measurement showed is not observable at
    /// any layer *(see <see cref="SelectionOrigin"/>)*.</para>
    ///
    /// <para>⚠ <b>Only CONTRIBUTORS notify.</b> The Watch, the Inspector and the Details panel itself
    /// must not — a surface that does not drive the Details panel taking focus would otherwise steal
    /// it.</para>
    /// </summary>
    public SelectionOrigin FocusedSurface { get; private set; } = SelectionOrigin.Unknown;

    /// <summary>
    /// ⭐⭐ <b>Where the CURRENT sub-selection came from.</b> ⚠ Distinct from
    /// <see cref="FocusedSurface"/>, and they answer different questions: this one is durable and says
    /// <i>"who owns this selection"</i>; the latch is volatile and says <i>"who should the panel obey
    /// now"</i>. ⭐ Keeping both is what lets a surface reclaim ITS OWN last selection on regaining
    /// focus without each surface keeping a private copy — 📌 that asymmetry is the root of <c>B8</c>:
    /// the node lived here while the variable arm kept its state privately, so the snapshot recorded
    /// the wrong thing.
    /// </summary>
    public SelectionOrigin ActiveSubSelectionOrigin { get; private set; } = SelectionOrigin.Unknown;

    /// <summary>
    /// ⭐⭐⭐ <b>A contributing surface reports that it holds focus.</b> Called every frame it is
    /// focused — ⛔ deliberately a LEVEL, not an edge: an edge would need a change to detect, and the
    /// whole point is that re-entering a surface with an unchanged selection is the failing gesture.
    ///
    /// <para>⭐ Idempotent and allocation-free, so a per-frame call costs a comparison.
    /// ⚠ <see cref="SelectionOrigin.Unknown"/> is ignored — it means "nobody", and a surface claiming
    /// to be nobody is a bug, not a state.</para>
    /// </summary>
    public void NotifySurfaceFocused(SelectionOrigin origin)
    {
        if (origin == SelectionOrigin.Unknown || FocusedSurface == origin) return;
        FocusedSurface = origin;
        OnSelectionChanged?.Invoke();
    }

    /// <summary>
    /// ⭐⭐ Sets the sub-selection AND records which surface produced it.
    /// ⛔ The origin is recorded even when the selection itself is unchanged: the surface still
    /// asserted ownership, and that assertion is what the panel routes on.
    /// </summary>
    public void SetActiveSubSelection(IAssetSubSelection? selection, SelectionOrigin origin)
    {
        ActiveSubSelectionOrigin = origin;
        ActiveSubSelection       = selection;
    }

    /// <summary>Register that a window for this asset is now open.</summary>
    public void RegisterOpenAsset(Guid assetId) => _openAssets.Add(assetId);

    /// <summary>Unregister; sub-selection is kept until Forget() is called.</summary>
    public void UnregisterOpenAsset(Guid assetId)
    {
        _openAssets.Remove(assetId);
    }

    /// <summary>Evict the sub-selection for the given asset and fire the changed event.</summary>
    public void Forget(Guid assetId)
    {
        _subSelectionsByAsset.Remove(assetId);
        OnSelectionChanged?.Invoke();
    }
}

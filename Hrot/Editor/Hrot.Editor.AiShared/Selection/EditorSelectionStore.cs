using Fdp.Core;

namespace Hrot.Editor.AiShared.Selection;

public sealed class EditorSelectionStore
{
    // Currently-active asset -- the asset whose canvas window has focus.
    private IEditableAsset? _activeAsset;

    // Per-asset sub-selection keyed by AssetId.
    private readonly Dictionary<Guid, IAssetSubSelection?> _subSelectionsByAsset = new();

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

    /// <summary>Sub-selection within the active asset. Read- and write-routed through this property.</summary>
    public IAssetSubSelection? ActiveSubSelection
    {
        get => _activeAsset is null ? null : _subSelectionsByAsset.GetValueOrDefault(_activeAsset.AssetId);
        set
        {
            if (_activeAsset is null) return;
            var current = _subSelectionsByAsset.GetValueOrDefault(_activeAsset.AssetId);
            if (Equals(current, value)) return;
            _subSelectionsByAsset[_activeAsset.AssetId] = value;
            OnSelectionChanged?.Invoke();
        }
    }

    /// <summary>Read sub-selection for any asset (active or not).</summary>
    public IAssetSubSelection? GetSubSelection(Guid assetId) =>
        _subSelectionsByAsset.GetValueOrDefault(assetId);

    /// <summary>Write sub-selection for any asset. Used by windows that are not currently focused.</summary>
    public void SetSubSelection(Guid assetId, IAssetSubSelection? selection)
    {
        var current = _subSelectionsByAsset.GetValueOrDefault(assetId);
        if (Equals(current, selection)) return;
        _subSelectionsByAsset[assetId] = selection;
        OnSelectionChanged?.Invoke();
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

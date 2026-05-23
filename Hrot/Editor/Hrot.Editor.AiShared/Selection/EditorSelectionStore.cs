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

    // Global entity selection -- independent of which asset is active.
    private Entity? _selectedEntity;

    public event Action? OnSelectionChanged;

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

    /// <summary>Globally-selected entity for runtime debug overlay.</summary>
    public Entity? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (_selectedEntity == value) return;
            _selectedEntity = value;
            OnSelectionChanged?.Invoke();
        }
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

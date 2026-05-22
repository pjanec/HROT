using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor;

public sealed class EditorSelectionStore
{
    private BlueprintAsset? _selected;

    public BlueprintAsset? SelectedAsset => _selected;

    public event Action? OnSelectionChanged;

    public void SelectAsset(BlueprintAsset? asset)
    {
        _selected = asset;
        OnSelectionChanged?.Invoke();
    }
}

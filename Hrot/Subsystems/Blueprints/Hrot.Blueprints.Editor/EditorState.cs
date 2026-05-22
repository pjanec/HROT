using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor;

public sealed class EditorState
{
    private readonly Dictionary<Guid, BlueprintAsset> _inMemory = new();

    public void SetInMemoryAsset(BlueprintAsset asset)
        => _inMemory[asset.AssetId] = asset;

    public BlueprintAsset? GetInMemoryAsset(Guid assetId)
        => _inMemory.TryGetValue(assetId, out var a) ? a : null;

    public void RemoveInMemoryAsset(Guid assetId)
        => _inMemory.Remove(assetId);

    public IReadOnlyDictionary<Guid, BlueprintAsset> InMemoryAssets => _inMemory;
}

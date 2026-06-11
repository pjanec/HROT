using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.BTree.Editor.Tests.Comparison;

internal sealed class FakeAsset : IEditableAsset
{
    public Guid AssetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public AssetKind Kind { get; init; }
    public string SourceFilePath { get; init; } = string.Empty;
    public bool IsDirty => false;
    public bool IsEditorOwned => false;
    public event Action? Changed { add { } remove { } }
}

internal sealed class FakeCatalog : IAssetCatalog
{
    private readonly Dictionary<Guid, IEditableAsset> _assets = new();

    public FakeCatalog(params IEditableAsset[] assets)
    {
        foreach (var a in assets)
            _assets[a.AssetId] = a;
    }

    public IReadOnlyList<IEditableAsset> All => _assets.Values.ToList();
    public IEditableAsset? FindByAssetId(Guid assetId) =>
        _assets.TryGetValue(assetId, out var a) ? a : null;
    public IEditableAsset? FindByName(string name) =>
        _assets.Values.FirstOrDefault(a => a.Name == name);
    public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) =>
        Array.Empty<IEditableAsset>();
    public event Action<AssetKind>? Changed { add { } remove { } }
}

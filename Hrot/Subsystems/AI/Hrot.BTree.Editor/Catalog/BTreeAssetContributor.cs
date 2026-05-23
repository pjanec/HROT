using System;
using System.Collections.Generic;
using System.Reflection;
using Fbt;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.BTree.Editor.Catalog;

public sealed class BTreeAssetContributor : IAssetCatalogContributor
{
    private readonly List<IEditableAsset> _assets = new();

    public AssetKind Kind => AssetKind.BTree;
    public event Action? ContributorChanged;

    public IReadOnlyList<IEditableAsset> Enumerate() => _assets;

    // Scans the given assembly for [BTreeDefinition]-annotated methods,
    // invokes them to get BehaviorTreeBlob instances, and projects them
    // into BehaviorTreeAsset instances via BehaviorTreeAssetProjector.
    // Also looks for matching [BTreeLayout] methods to apply layout.
    // Call this after each hot reload.
    public void LoadFrom(Assembly assembly)
    {
        _assets.Clear();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var defAttr = method.GetCustomAttribute<BTreeDefinitionAttribute>();
                if (defAttr is null) continue;
                if (method.GetParameters().Length != 0) continue;
                if (!typeof(BehaviorTreeBlob).IsAssignableFrom(method.ReturnType)) continue;

                BehaviorTreeBlob? blob;
                try { blob = (BehaviorTreeBlob?)method.Invoke(null, null); }
                catch { blob = null; }
                if (blob is null) continue;

                // Derive an AssetId from the tree name via FNV-1a-32.
                var assetId = AssetIdHasher.FromName(defAttr.TreeName);

                // Look for a matching layout method.
                var layout = LayoutDiscovery.TryGetLayout<BTreeLayoutAttribute, BTreeEditorLayout>(
                    assembly, assetId);

                var asset = BehaviorTreeAssetProjector.Project(
                    blob,
                    blob.DebugMetadata,
                    layout,
                    assetId,
                    defAttr.TreeName,
                    string.Empty,
                    false,
                    string.Empty,
                    string.Empty,
                    type.Namespace ?? string.Empty);

                _assets.Add(asset);
            }
        }
        ContributorChanged?.Invoke();
    }
}

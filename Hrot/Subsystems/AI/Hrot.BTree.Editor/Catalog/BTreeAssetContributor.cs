using System;
using System.Collections.Generic;
using System.Reflection;
using Fbt;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.BTree.Editor.Catalog;

public sealed class BTreeAssetContributor : IAssetCatalogContributor
{
    private readonly List<IEditableAsset> _assets = new();
    private readonly BTreeDebugSession? _debugSession;

    public BTreeAssetContributor(BTreeDebugSession? debugSession = null)
    {
        _debugSession = debugSession;
    }

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

                RegisterBlobCore(blob, defAttr.TreeName, assetId, layout, type.Namespace ?? string.Empty);
            }
        }
        ContributorChanged?.Invoke();
    }

    /// <summary>
    /// Registers a single blob directly, projecting it into a BehaviorTreeAsset and
    /// wiring its debug metadata into the attached debug session (if any).
    /// The assetId is derived from <paramref name="treeName"/> via FNV-1a-32.
    /// Use this when the blob is obtained outside of assembly reflection (e.g. tests
    /// or programmatic tree construction).
    /// </summary>
    public void RegisterBlob(BehaviorTreeBlob blob, string treeName, BTreeEditorLayout? layout = null, string ns = "")
    {
        var assetId = AssetIdHasher.FromName(treeName);
        RegisterBlobCore(blob, treeName, assetId, layout, ns);
        ContributorChanged?.Invoke();
    }

    private void RegisterBlobCore(BehaviorTreeBlob blob, string treeName, Guid assetId, BTreeEditorLayout? layout, string ns)
    {
        var asset = BehaviorTreeAssetProjector.Project(
            blob,
            blob.DebugMetadata,
            layout,
            assetId,
            treeName,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            ns);

        _assets.Add(asset);

        // BPF-026: wire debug metadata into the session so node-index symbolication
        // (RunningElementId, StackElementIds) works when Update() is called at runtime.
        _debugSession?.SetDebugMetadata(blob.DebugMetadata, assetId);
    }
}

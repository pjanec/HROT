using System;
using System.Collections.Generic;
using System.Reflection;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Identity;
using Hrot.Editor.AiShared.Layout;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Catalog;

public sealed class HsmAssetContributor : IAssetCatalogContributor
{
    private readonly List<IEditableAsset> _assets = new();
    private readonly Hrot.Hsm.Editor.Debug.HsmDebugSession? _debugSession;

    public HsmAssetContributor(Hrot.Hsm.Editor.Debug.HsmDebugSession? debugSession = null)
    {
        _debugSession = debugSession;
    }

    public AssetKind Kind => AssetKind.Hsm;
    public event Action? ContributorChanged;

    public IReadOnlyList<IEditableAsset> Enumerate() => _assets;

    // Scans the given assembly for [HsmDefinition]-annotated methods,
    // invokes them to get HsmDefinitionBlob instances, and projects them
    // into HsmAsset instances via HsmAssetProjector.
    // Also looks for matching [HsmLayout] methods to apply layout.
    // Call this after each hot reload.
    public void LoadFrom(Assembly assembly)
    {
        _assets.Clear();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var defAttr = method.GetCustomAttribute<Fhsm.Kernel.Attributes.HsmDefinitionAttribute>();
                if (defAttr is null) continue;
                if (method.GetParameters().Length != 0) continue;
                // ⭐ CE-1252 (BV-18, R-1028) — a C#-BUILT machine (the method returns its StateMachineGraph: CivilianHsm,
                //   PlatoonDefend, Surrender …) is compiled here, in memory, so it opens as a read-only graph like a C# BTree
                //   (CE-3160). ⛔ Only blob-returning (generated) methods were listed, so the 12 hand-built machines never reached
                //   the HSM editor. Nothing is written to disk.
                bool returnsGraph = method.ReturnType.FullName == "Fhsm.Compiler.Graph.StateMachineGraph";   // by name, as the BTree side
                if (!returnsGraph && !typeof(HsmDefinitionBlob).IsAssignableFrom(method.ReturnType)) continue;

                HsmDefinitionBlob? blob;
                try
                {
                    var made = method.Invoke(null, null);
                    blob = returnsGraph
                        ? (HsmDefinitionBlob?)made?.GetType().GetMethod("Compile", Type.EmptyTypes)?.Invoke(made, null)
                        : (HsmDefinitionBlob?)made;
                }
                catch { blob = null; }
                if (blob is null) continue;

                // Derive an AssetId: prefer an explicit GUID on the attribute, else hash the machine name.
                Guid assetId;
                if (defAttr.AssetId is not null && Guid.TryParse(defAttr.AssetId, out var parsed))
                    assetId = parsed;
                else
                    assetId = AssetIdHasher.FromName(defAttr.MachineName);

                // Look for a matching layout method.
                var layout = LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, HsmEditorLayout>(
                    assembly, assetId);

                var asset = HsmAssetProjector.Project(
                    blob,
                    blob.Metadata,
                    layout,
                    assetId,
                    defAttr.MachineName,
                    string.Empty,
                    false,
                    type.Namespace ?? string.Empty);

                asset.IsCodeBuilt = returnsGraph;   // ⭐ CE-1252 — read-only in the editor
                _assets.Add(asset);
                // ⭐ CE-1253 (BV-20) — the session maps a running machine's state indices through THIS compile's metadata, so the ids
                //   match the graph on the canvas (a C#-built machine draws new stable ids at every compile).
                _debugSession?.RegisterMachine(defAttr.MachineName, assetId, blob.Metadata);
            }
        }
        ContributorChanged?.Invoke();
    }
}

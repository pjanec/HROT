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
                if (!typeof(HsmDefinitionBlob).IsAssignableFrom(method.ReturnType)) continue;

                HsmDefinitionBlob? blob;
                try { blob = (HsmDefinitionBlob?)method.Invoke(null, null); }
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

                _assets.Add(asset);
            }
        }
        ContributorChanged?.Invoke();
    }
}

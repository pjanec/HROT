using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Catalog;

/// <summary>
/// Implements <see cref="IReferenceCatalogContributor"/> for the HSM subsystem.
/// Exposes machine-scoped events as referenceable sub-elements, and enumerates
/// references from state actions, transition guards, and transition event usages.
/// </summary>
public sealed class HsmReferenceContributor : IReferenceCatalogContributor
{
    /// <inheritdoc/>
    public IReadOnlyList<IAssetSubElement> EnumerateElements(IEditableAsset asset)
    {
        if (asset is not HsmAsset hsmAsset)
            return Array.Empty<IAssetSubElement>();

        var result = new List<IAssetSubElement>(hsmAsset.AllEvents.Count);

        // Machine-scoped events are the referenceable sub-elements of an HSM asset.
        foreach (var evt in hsmAsset.AllEvents)
            result.Add(new HsmEventSubElement(hsmAsset.AssetId, evt.Name));

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<AssetReference> EnumerateReferences(IEditableAsset asset)
    {
        if (asset is not HsmAsset hsmAsset)
            return Array.Empty<AssetReference>();

        var result = new List<AssetReference>();

        // State action references (OnEntry, OnExit, Activity, Timer).
        foreach (var state in hsmAsset.AllStates)
        {
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.OnEntryAction);
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.OnExitAction);
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.ActivityAction);
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.TimerAction);
        }

        // Transition references: event usage (machine-scoped key), guard, action.
        foreach (var t in hsmAsset.AllTransitions)
        {
            var path = $"Transition '{t.Source?.Name}' -> '{t.Target?.Name}'";

            if (t.EventId != 0)
            {
                var evt = hsmAsset.FindEventById(t.EventId);
                if (evt != null)
                    result.Add(new AssetReference(
                        hsmAsset.AssetId, AssetKind.Hsm, t.VisualId, path,
                        $"{hsmAsset.AssetId:D}::{evt.Name}", SubElementKind.EventName));
            }

            AddGuardRef(result, hsmAsset, t.VisualId, path, t.GuardFunction);
            AddActionRef(result, hsmAsset, t.VisualId, path, t.ActionFunction);
        }

        // Global-transition references.
        foreach (var gt in hsmAsset.AllGlobalTransitions)
        {
            var path = $"GlobalTransition -> '{gt.Target?.Name}'";

            if (gt.EventId != 0)
            {
                var evt = hsmAsset.FindEventById(gt.EventId);
                if (evt != null)
                    result.Add(new AssetReference(
                        hsmAsset.AssetId, AssetKind.Hsm, gt.VisualId, path,
                        $"{hsmAsset.AssetId:D}::{evt.Name}", SubElementKind.EventName));
            }

            AddGuardRef(result, hsmAsset, gt.VisualId, path, gt.GuardFunction);
            AddActionRef(result, hsmAsset, gt.VisualId, path, gt.ActionFunction);
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void AddActionRef(
        List<AssetReference> list, HsmAsset asset, Guid elementId, string path, string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        list.Add(new AssetReference(
            asset.AssetId, AssetKind.Hsm, elementId, path, fqn, SubElementKind.ActionFqn));
    }

    private static void AddGuardRef(
        List<AssetReference> list, HsmAsset asset, Guid elementId, string path, string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return;
        list.Add(new AssetReference(
            asset.AssetId, AssetKind.Hsm, elementId, path, fqn, SubElementKind.GuardFqn));
    }
}

/// <summary>
/// Represents a machine-scoped HSM event as a referenceable sub-element.
/// The key format is <c>{AssetId:D}::{EventName}</c> to prevent collisions
/// between identically-named events in different machines.
/// </summary>
internal sealed class HsmEventSubElement : IAssetSubElement
{
    /// <summary>Machine-scoped key: <c>{assetId:D}::{eventName}</c>.</summary>
    public string Key { get; }

    /// <inheritdoc/>
    public SubElementKind Kind => SubElementKind.EventName;

    /// <inheritdoc/>
    public string DisplayName { get; }

    /// <inheritdoc/>
    public Guid? SourceAssetId { get; }

    public HsmEventSubElement(Guid assetId, string eventName)
    {
        SourceAssetId = assetId;
        DisplayName   = eventName;
        Key           = $"{assetId:D}::{eventName}";
    }
}

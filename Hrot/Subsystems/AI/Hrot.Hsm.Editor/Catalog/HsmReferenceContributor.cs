using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.References;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Catalog;

/// <summary>
/// Implements <see cref="IReferenceCatalogContributor"/> for the HSM subsystem.
/// Exposes machine-scoped events <b>and blackboard variables</b> as referenceable sub-elements, and enumerates
/// references from state actions, transition guards, transition event usages <b>and every
/// <c>ExpressionTargetField</c> binding</b>.
///
/// <para>⭐⭐⭐ <b><c>HSM-017</c> (2026-10-06) — THE VARIABLE HALF, AND WHY IT IS HERE RATHER THAN IN A NEW CLASS.</b>
/// 📐 Measured: the rename path runs <c>VariableRenameCommit</c> → <c>RefactorService</c> → the registered
/// contributors, and the complete set of 24 <c>*Contributor</c> classes held exactly ONE that enumerates blackboard
/// variables — <c>BTreeBlackboardVariableContributor</c>. HSM contributed events, action FQNs and guard FQNs and
/// <b>no variable references at all</b>, so renaming an HSM variable rewrote the declaration
/// (<c>HsmAsset.RenameVariable</c> fixes the entry and its aliases) and left every
/// <c>ExpressionTargetField</c> naming the OLD string. ⛔ And unlike BTree there is no build-time net:
/// <c>BTreeJsonGenerator</c> skips a whole asset with a <c>BTREE0002</c> warning when a binding does not resolve,
/// while <c>HsmJsonGenerator</c>'s four codes are parse/storage/shared-type/global-blueprint errors — none of them a
/// dangling-binding check. ⇒ on HSM the rename produced a SILENTLY WRONG MACHINE.</para>
///
/// <para>⭐ <b>Extended rather than mirrored:</b> BTree keeps variables in their own contributor, but the site walk
/// below (states × four slots, transitions, global transitions — each with its element id and display path) already
/// exists here, and both composition roots (<c>EditorSubsystem</c>, <c>CgfSubsystem</c>) already register THIS
/// class. A separate HSM class would have duplicated the walk and needed two more registrations — the exact split
/// that lets one host get a fix and the other not.</para>
///
/// <para>⚠ <b>Scope, stated honestly (<c>R-88</c>):</b> this makes the EDITOR's references consistent. A variable
/// name is additionally load-bearing at RUNTIME for <c>Scope=Behavior</c> / <c>Scope=Entity</c> slot keys and for
/// scenario overrides; those are not this contributor's business and are unchanged.</para>
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

        // ⭐ HSM-017 — and so are its blackboard variables, when the editor owns them.
        if (hsmAsset.IsBlackboardEditorManaged)
            foreach (var v in hsmAsset.BlackboardVariables)
                result.Add(new BlackboardVariableSubElement(hsmAsset.AssetId, v.Name));

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
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.OnEntry?.MethodFqn);
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.OnExit?.MethodFqn);
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.Activity?.MethodFqn);
            AddActionRef(result, hsmAsset, state.StableId, state.Name, state.Timer?.MethodFqn);

            // ⭐ HSM-017 — every slot's output binding. StateNode.Bindings is the model's own list of the four,
            //   so a fifth slot added there is covered here without this walk being edited.
            foreach (var b in state.Bindings)
                AddVariableRef(result, hsmAsset, state.StableId, state.Name, b.ExpressionTargetField);
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

            AddGuardRef(result, hsmAsset, t.VisualId, path, t.Guard?.MethodFqn);
            AddActionRef(result, hsmAsset, t.VisualId, path, t.Action?.MethodFqn);
            AddVariableRef(result, hsmAsset, t.VisualId, path, t.Guard?.ExpressionTargetField);    // HSM-017
            AddVariableRef(result, hsmAsset, t.VisualId, path, t.Action?.ExpressionTargetField);
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

            AddGuardRef(result, hsmAsset, gt.VisualId, path, gt.Guard?.MethodFqn);
            AddActionRef(result, hsmAsset, gt.VisualId, path, gt.Action?.MethodFqn);
            AddVariableRef(result, hsmAsset, gt.VisualId, path, gt.Guard?.ExpressionTargetField);  // HSM-017
            AddVariableRef(result, hsmAsset, gt.VisualId, path, gt.Action?.ExpressionTargetField);
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

    /// <summary>⭐ <c>HSM-017</c> — one binding's output variable. The key comes from
    /// <see cref="BlackboardVariableSubElement.KeyFor"/> rather than an interpolation here, so the reference and
    /// the element it points at cannot be spelled differently.</summary>
    private static void AddVariableRef(
        List<AssetReference> list, HsmAsset asset, Guid elementId, string path, string? expressionTargetField)
    {
        if (string.IsNullOrEmpty(expressionTargetField)) return;
        list.Add(new AssetReference(
            asset.AssetId, AssetKind.Hsm, elementId, path,
            BlackboardVariableSubElement.KeyFor(asset.AssetId, expressionTargetField),
            SubElementKind.BlackboardVariable));
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

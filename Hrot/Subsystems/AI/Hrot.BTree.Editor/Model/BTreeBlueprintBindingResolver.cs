using System;
using Fbt;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.References;

namespace Hrot.BTree.Editor.Model;

/// <summary>
/// ⭐⭐ <b><c>CE-417</c> B-1 (slice 4c) — reconciles a BTree asset's BLUEPRINT bindings against the catalogue on open.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.5 decision ③.
///
/// <list type="number">
/// <item><b>Legacy FQN → id.</b> A binding that still names a generated blueprint by its <c>…_Bp.TickCore</c> (a file
/// written before B-1) is rewritten to the blueprint's <c>BlueprintAssetId</c> + <c>BlueprintName</c> when the catalogue
/// resolves it; the method is then derived, never persisted.</item>
/// <item><b>Stale name → current name.</b> A binding's name is healed from its id after a rename.</item>
/// </list>
///
/// <para>⚠ <b>Id FIRST, unlike the subtree rule.</b> <c>SubtreeReferenceResolver.Resolve</c> prefers the NAME (§7.1a);
/// B-1 rules that a blueprint binding RESOLVES by its id and keeps the name only to heal. ⛔ Name-first would be wrong
/// here: two blueprints may share a name (<c>EnumDemo</c> exists twice in the repo) and a name lookup could silently
/// rebind to the other one. ⭐ The never-erase rule is the same: an id that does not resolve keeps both fields.</para>
/// </summary>
public static class BTreeBlueprintBindingResolver
{
    /// <returns>How many bindings were rewritten. ⚠ Non-zero ⇒ the caller must mark the asset dirty, or the heal is
    /// redone on every open and the file keeps the old form.</returns>
    public static int Resolve(BehaviorTreeAsset asset, IAssetCatalog catalog)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        int healed = 0;
        foreach (var node in asset.Nodes)
        {
            var binding = node.KernelType switch
            {
                NodeType.Action    => node.Action,
                NodeType.Condition => node.Condition,
                _                  => null,
            };
            if (binding is null) continue;

            if (binding.BlueprintAssetId == Guid.Empty)
            {
                // ① a legacy generated FQN — only a generated-blueprint FQN; a hand-written method stays a method.
                if (node.DelegateShape != BTreeActionDelegateShape.AiPrimitiveTickCore) continue;
                if (ComposedBlueprintResolver.Resolve(binding.MethodFqn, catalog) is not { } bp) continue;
                binding.BlueprintAssetId = bp.AssetId;
                binding.BlueprintName    = bp.Name;
                binding.MethodFqn        = null;
                healed++;
                continue;
            }

            // ② id first; heal the name. ⛔ A miss keeps both (dangling — the validator reports it).
            if (catalog.FindByAssetId(binding.BlueprintAssetId) is { Kind: AssetKind.Blueprint } byId
                && !string.Equals(binding.BlueprintName, byId.Name, StringComparison.Ordinal))
            {
                binding.BlueprintName = byId.Name;
                healed++;
            }
        }
        return healed;
    }
}

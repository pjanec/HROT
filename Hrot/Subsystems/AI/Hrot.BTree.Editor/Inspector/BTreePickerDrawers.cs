using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Editing.Types;
using Fdp.Toolkit.Behavior;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using StructEdit.Core;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// ⭐ CE-3378 (N-1) — builds the TYPE CATALOGUE OVERLAY the inspector draws a BTree document's facets with
/// (<see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetEditService"/> / <c>NodePropertiesSource</c>) for a specific BTree asset. Called by the composition root from the
/// <c>ActiveChanged</c> callback whenever the active BTree document switches (SE2) — ⭐ ONCE per document, kept by the host.
/// </summary>
public static class BTreePickerDrawerFactory
{
    /// <summary>
    /// Creates the catalogue overlay for <paramref name="asset"/> on <see cref="TypeCatalogue.Standard"/>:
    /// <list type="bullet">
    ///   <item>⭐ <c>CE-417</c> slice 4b — the ONE <see cref="ActionBindingDrawer"/> for the action/condition binding, a TYPE entry on
    ///         <see cref="BehaviorActionBindingFacet"/>. The BTree host supplies only its method list: ⭐ <c>CE-1167</c> the exporter's
    ///         BTree-hosted methods of the slot's kind ∪ the asset's own (<see cref="ActionBindingMethods"/>, the HSM's rule). ⛔ Was the
    ///         behaviour registry's NAMES — not methods, so a pick wrote a value the build could not resolve. ⛔ Replaces <c>BehaviorHashPickerDrawer</c> + <c>BlackboardFieldPickerDrawer</c> and the
    ///         <c>BTreeFacetFqnContext</c> side channel between them.</item>
    ///   <item>FIELD RULES on <c>string</c>: the subtree asset picker and the behaviour picker.</item>
    /// </list>
    /// </summary>
    public static TypeCatalogue BuildCatalogue(
        BehaviorTreeAsset      asset,
        BehaviorRegistry       registry,
        IActionSchemaExporter? exporter = null,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null,
        Action<NodeEditor.UI.Picker.PickerRequest, Action<NodeEditor.UI.Picker.PickerResult>>? openPicker = null)
    {
        if (asset    is null) throw new ArgumentNullException(nameof(asset));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        // ⭐ CE-1008 (AI_Editor_Shared_Infrastructure §7.1b) — ONE choice field for this document's pickers (searchable where the host
        //   passed its shell picker, plain combos where not)
        var field = new Hrot.Editor.AiShared.Inspector.PickerField(openPicker);
        var bindingDrawer = new ActionBindingDrawer(
            new ActionBindingSources(asset, kind => ActionBindingMethods.For(exporter, ActionHosting.BTree, kind == BindingSlotKind.Guard,
                                                                             BindingsOf(asset, kind)), exporter, catalog), field);

        var types = TypeCatalogue.Standard.With(new FieldTypeEditor(typeof(BehaviorActionBindingFacet), null, bindingDrawer));

        // ⭐⭐ CE-439 — the subtree facet's [AiAssetPicker(BTree)] had NO drawer on this host (only the HSM factory registered
        //   one), so it rendered as plain text. ⚠ Registered only with a catalogue, for the HSM factory's reason: an empty list
        //   reads as "there are no such assets". ⭐ The same per-KIND rule editor as the HSM's (CE-396) — one implementation.
        if (catalog is not null)
            types.AddRule(typeof(Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute),
                new Hrot.Editor.AiShared.Inspector.AiAssetPickerRuleEditor(catalog, field));

        // ⭐ CE-2079 — an SOP order's behaviour: every registered behaviour (any tier, hand-written included).
        types.AddRule(typeof(Hrot.Editor.AiShared.Inspector.AiBehaviorPickerAttribute),
            new FieldTypeEditor(typeof(string), null, new Hrot.Editor.AiShared.Inspector.AiBehaviorPickerDrawer(() => registry.GetRegisteredNames(), field)));
        return types;
    }

    /// <summary>⭐ CE-1167 — the bindings this asset already holds for a slot kind: its conditions' for a guard, its actions' otherwise.</summary>
    public static IEnumerable<Hrot.Editor.AiShared.BehaviorActionBinding?> BindingsOf(BehaviorTreeAsset asset, BindingSlotKind kind)
        => asset.Nodes.Select(n => kind == BindingSlotKind.Guard ? n.Condition : n.Action);
}

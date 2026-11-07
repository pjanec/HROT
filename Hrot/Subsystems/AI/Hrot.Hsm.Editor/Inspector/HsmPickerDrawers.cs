using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Editing.Types;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;

namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> slice 4b — the ONE thing the HSM host supplies to the shared binding drawer: which methods a slot
/// offers.</b> 📄 <c>DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>⭐⭐⭐ <b><c>CE-386</c> — the REGISTERED HSM activities / guards, not only the names this asset already mentions.</b>
/// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.3. 🔴 The list was once built ONLY from the asset's own bindings
/// ⇒ the FIRST binding was unmakeable from the inspector.</para>
///
/// <para>⚠ <b>The asset's own names stay in the union</b>, even with an exporter: an asset may name a method the current
/// assembly no longer exports, and dropping it from the list would hide a dangling binding instead of showing it. ⭐
/// <c>HsmActivity</c>, not <c>Hsm</c>, for an action slot: a bare <c>Hsm</c> entry may be a GUARD, and offering a guard as an
/// activity is the wrong-kind defect <c>CE-396</c> fixed one layer up. ⛔ Was <c>HsmActionPickerDrawer</c> +
/// <c>HsmGuardPickerDrawer</c>.</para>
/// </summary>
public static class HsmBindingMethods
{
    /// <summary>The method names a slot of <paramref name="kind"/> offers (unsorted; the drawer sorts).</summary>
    public static IEnumerable<string> For(HsmAsset asset, IActionSchemaExporter? schema, BindingSlotKind kind)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        // ⭐ CE-1167 — the rule is shared with the BTree host (ActionBindingMethods); the HSM supplies its hosting bit and its bindings
        var hosting = kind == BindingSlotKind.Guard ? ActionHosting.HsmGuard : ActionHosting.HsmActivity;
        return ActionBindingMethods.For(schema, hosting, condition: null, BindingsOf(asset, kind));
    }

    private static IEnumerable<Hrot.Editor.AiShared.BehaviorActionBinding?> BindingsOf(HsmAsset asset, BindingSlotKind kind)
    {
        if (kind == BindingSlotKind.Guard)
        {
            foreach (var t in asset.AllTransitions)       yield return t.Guard;
            foreach (var g in asset.AllGlobalTransitions) yield return g.Guard;
            yield break;
        }
        foreach (var s in asset.AllStates)
            foreach (var b in s.Bindings) yield return b;
        foreach (var t in asset.AllTransitions)       yield return t.Action;
        foreach (var g in asset.AllGlobalTransitions) yield return g.Action;
    }
}

/// <summary>Internal rendering helpers shared by all HSM picker drawers.</summary>
internal static class HsmPickerHelper
{
    /// <summary>⭐ <c>CE-1008</c> (AI_Editor_Shared_Infrastructure §7.1b) — a string choice through the ONE choice field: the searchable
    /// picker where the host passed one, else the plain combo ("(none)" clears).</summary>
    internal static bool Render(ref object value, string id, IReadOnlyList<string> items, PickerField field, EditNode node, string title,
                                Func<string, string>? label = null)
    {
        var current = value as string ?? string.Empty;
        if (!field.Draw(id, node, current, PickerChoices.Flat(items, label), title, out var picked)) return false;
        value = picked ?? string.Empty;
        return true;
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmStateSelectorAttribute"/>. Lists state names from the asset.
/// </summary>
public sealed class HsmStateSelectorDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;
    private readonly PickerField _field;

    /// <param name="field">⭐ CE-1008 — the searchable choice field; null ⇒ a plain combo.</param>
    public HsmStateSelectorDrawer(HsmAsset asset, PickerField? field = null)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _field = field ?? new PickerField(null);
    }

    /// <summary>⭐ CE-1008 — the choice field (the forwarding rail reads it).</summary>
    public PickerField Field => _field;

    public Type TargetType => typeof(string);

    public IReadOnlyList<string> GetItems()
        => _asset.AllStates
                 .Where(s => s != _asset.RootState      // not the synthetic root
                          && !s.Name.StartsWith("__"))  // not compiler-internal pseudo-roots
                 .Select(s => s.Name)
                 .OrderBy(n => n)
                 .ToList();

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        return HsmPickerHelper.Render(ref value, "hsmstate", GetItems(), _field, node, "State");
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmEventPickerAttribute"/>. Lists event names from the asset.
/// </summary>
public sealed class HsmEventPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;
    private readonly PickerField _field;

    /// <param name="field">⭐ CE-1008 — the searchable choice field; null ⇒ a plain combo.</param>
    public HsmEventPickerDrawer(HsmAsset asset, PickerField? field = null)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _field = field ?? new PickerField(null);
    }

    /// <summary>⭐ CE-1008 — the choice field (the forwarding rail reads it).</summary>
    public PickerField Field => _field;

    public Type TargetType => typeof(string);

    public IReadOnlyList<string> GetItems()
        => Choices().Select(c => c.Name).OrderBy(n => n).ToList();

    /// <summary>
    /// The asset's events, then ⭐ <c>CE-2088</c> the ENGINE-RAISED ones it has not declared yet (<c>Sensor.FirstThreat</c> …):
    /// picking one declares it (<c>HsmFacetDispatcher</c>), so an author never types a name or an id.
    /// </summary>
    internal IReadOnlyList<(string Name, ushort Id)> Choices()
    {
        var list = _asset.AllEvents.Select(e => (e.Name, e.EventId)).ToList();
        foreach (var (name, id) in Hrot.AiEditor.Persistence.Emit.HsmEventIds.BuiltIns)
            if (_asset.FindEventById(id) == null && list.All(c => c.Name != name)) list.Add((name, id));
        return list;
    }

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        var choices = Choices();
        var current = value is ushort uid ? choices.FirstOrDefault(c => c.Id == uid).Name ?? string.Empty : string.Empty;
        // ⭐ CE-1008 — the ONE choice field over the event NAMES; the pick maps back to its id (an event has no "(none)": a pick of it is ignored)
        if (!_field.Draw("hsmev", node, current, PickerChoices.Flat(choices.Select(c => c.Name)), "Event", out var picked)
            || string.IsNullOrEmpty(picked)) return false;
        foreach (var (name, id) in choices)
            if (name == picked) { value = id; return true; }
        return false;
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmSyncGroupPickerAttribute"/>. Provides a ushort combo from
/// known sync group IDs in the asset's transitions.
/// </summary>
public sealed class HsmSyncGroupPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmSyncGroupPickerDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(ushort);

    public IReadOnlyList<string> GetItems()
    {
        var ids = new HashSet<ushort>();
        foreach (var t in _asset.AllTransitions)
            if (t.SyncGroupId != 0) ids.Add(t.SyncGroupId);
        return ids.OrderBy(x => x).Select(x => x.ToString()).ToList();
    }

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        var current = value is ushort u ? u : (ushort)0;
        var items   = _asset.AllTransitions
                           .Where(t => t.SyncGroupId != 0)
                           .Select(t => t.SyncGroupId)
                           .Distinct()
                           .OrderBy(x => x)
                           .ToList();
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##hsmsg", current == 0 ? "(none)" : current.ToString()))
        {
            if (ImGuiNET.ImGui.Selectable("(none)", current == 0) && current != 0)
            {
                value   = (ushort)0;
                changed = true;
            }
            foreach (var id in items)
            {
                bool sel = id == current;
                if (ImGuiNET.ImGui.Selectable(id.ToString(), sel) && !sel)
                {
                    value   = id;
                    changed = true;
                }
                if (sel) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}

/// <summary>
/// ⭐ CE-3378 (N-1) — builds the TYPE CATALOGUE OVERLAY the inspector draws an HSM document's facets with
/// (<see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetEditService"/> / <c>NodePropertiesSource</c>) for a specific HSM asset. Called by the composition root from the
/// <c>ActiveChanged</c> callback whenever the active HSM document switches (SE2) — ⭐ ONCE per document, the host keeps it; it is never rebuilt per frame.
/// </summary>
public static class HsmPickerDrawerFactory
{
    /// <summary>
    /// Creates the catalogue overlay for <paramref name="asset"/> on <see cref="TypeCatalogue.Standard"/>:
    /// <list type="bullet">
    ///   <item>⭐ <c>CE-417</c> slice 4b — the ONE <see cref="ActionBindingDrawer"/> for every binding (state entry/exit/
    ///         activity/timer, transition and global-transition guard/action), a TYPE entry on <see cref="BehaviorActionBindingFacet"/>;
    ///         the HSM host supplies only its method list (<see cref="HsmBindingMethods"/>).</item>
    ///   <item>FIELD RULES on <c>string</c>: <see cref="HsmStateSelectorAttribute"/>, <see cref="HsmEventPickerAttribute"/>, the SOP behaviour picker and the asset picker
    ///         (<see cref="Hrot.Editor.AiShared.Inspector.AiAssetPickerRuleEditor"/>, per KIND — CE-396).</item>
    ///   <item>a <see cref="HsmSyncGroupPickerDrawer"/> as the TYPE entry for <c>ushort</c> (sync-group fields).</item>
    /// </list>
    /// </summary>
    /// <param name="exporter">⭐⭐ <c>CE-386</c> — the registered activities/guards and every binding's parameter type.</param>
    /// <param name="catalog">
    /// ⭐⭐ §11.1a — the asset catalogue that feeds the <c>[AiAssetPicker]</c> on <c>StateFacet.SubtreeName</c> and the
    /// binding drawer's blueprint list. ⚠ Optional so headless fixtures need not supply one; ⛔ a production host HAS one
    /// and must pass it, or the field silently offers nothing — the silent-default shape this codebase keeps paying for.
    /// </param>
    public static TypeCatalogue BuildCatalogue(
        HsmAsset               asset,
        IActionSchemaExporter? exporter = null,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null,
        Func<IEnumerable<string>>? behaviourNames = null,
        Action<NodeEditor.UI.Picker.PickerRequest, Action<NodeEditor.UI.Picker.PickerResult>>? openPicker = null)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        // ⭐ CE-1008 (AI_Editor_Shared_Infrastructure §7.1b) — ONE choice field for every picker of this document: the host's searchable
        //   picker (openPicker = its shell PickerRegistry.OpenPicker), or plain combos where it passes none
        var field = new PickerField(openPicker);
        var bindingDrawer = new ActionBindingDrawer(new ActionBindingSources(
            asset, kind => HsmBindingMethods.For(asset, exporter, kind), exporter, catalog), field);

        var types = TypeCatalogue.Standard.With(
            new FieldTypeEditor(typeof(ushort), null, new HsmSyncGroupPickerDrawer(asset)),
            new FieldTypeEditor(typeof(BehaviorActionBindingFacet), null, bindingDrawer));
        types.AddRule(typeof(HsmStateSelectorAttribute), new FieldTypeEditor(typeof(string), null, new HsmStateSelectorDrawer(asset, field)));
        types.AddRule(typeof(HsmEventPickerAttribute), new FieldTypeEditor(typeof(string), null, new HsmEventPickerDrawer(asset, field)));
        // ⭐ CE-2083 — the SOP order's behaviour picker (the BTree's drawer); only with a registry to list, as the catalogue rule.
        if (behaviourNames is not null)
            types.AddRule(typeof(Hrot.Editor.AiShared.Inspector.AiBehaviorPickerAttribute),
                new FieldTypeEditor(typeof(string), null, new Hrot.Editor.AiShared.Inspector.AiBehaviorPickerDrawer(behaviourNames, field)));

        // ⭐⭐ §11.1a — the asset pickers. ⚠ Registered only when a catalogue exists: a drawer over a
        //    null catalogue could only ever draw an empty list, and an empty dropdown reads as
        //    "there are no such assets" rather than "this host wired nothing".
        // ⭐⭐⭐ CE-396 — ONE rule, the drawer PER KIND chosen from the attribute the field carries, so
        //    `[AiAssetPicker(BTree)]` (StateFacet.SubtreeName) and `[AiAssetPicker(Blueprint)]`
        //    (CE-385's activity/guard fields) each offer their own kind.
        if (catalog is not null)
            types.AddRule(typeof(Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute),
                new Hrot.Editor.AiShared.Inspector.AiAssetPickerRuleEditor(catalog, field));
        return types;
    }
}

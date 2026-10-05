using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
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
        var hosting = kind == BindingSlotKind.Guard ? ActionHosting.HsmGuard : ActionHosting.HsmActivity;
        var names   = new HashSet<string>(StringComparer.Ordinal);

        if (schema != null)
            foreach (var kv in schema.All)
                if (kv.Value.Hosting.HasFlag(hosting))
                    names.Add(kv.Key);

        foreach (var b in BindingsOf(asset, kind))
            if (!string.IsNullOrEmpty(b?.MethodFqn)) names.Add(b!.MethodFqn!);
        return names;
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
    internal static bool RenderCombo(ref object value, string id, IReadOnlyList<string> items,
                                     Func<string, string>? label = null)
    {
        var current = value as string ?? string.Empty;
        label ??= s => s;
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo(id, label(current)))
        {
            // Allow clearing.
            if (ImGuiNET.ImGui.Selectable("(none)", string.IsNullOrEmpty(current)) && !string.IsNullOrEmpty(current))
            {
                value = string.Empty;
                changed = true;
            }
            foreach (var name in items)
            {
                bool sel = name == current;
                // ⚠ "##"+name keeps the ImGui id the VALUE, so two entries with the same label stay distinct.
                if (ImGuiNET.ImGui.Selectable($"{label(name)}##{name}", sel) && !sel)
                {
                    value   = name;
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
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmStateSelectorAttribute"/>. Lists state names from the asset.
/// </summary>
public sealed class HsmStateSelectorDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmStateSelectorDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

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
        return HsmPickerHelper.RenderCombo(ref value, "##hsmstate", GetItems());
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmEventPickerAttribute"/>. Lists event names from the asset.
/// </summary>
public sealed class HsmEventPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmEventPickerDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

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
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##hsmev", current))
        {
            foreach (var (name, id) in choices)
            {
                bool sel = name == current;
                if (ImGuiNET.ImGui.Selectable(name, sel) && !sel)
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
/// Factory: builds the <see cref="IReadOnlyDictionary{Type,IImGuiFieldDrawer}"/> consumed by
/// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetEditService"/> for a specific
/// HSM asset. Called by EditorSubsystem from the <c>ActiveChanged</c> callback whenever the
/// active HSM document switches (SE2).
/// </summary>
public static class HsmPickerDrawerFactory
{
    /// <summary>
    /// Creates a fresh custom-drawers map for <paramref name="asset"/>:
    /// <list type="bullet">
    ///   <item>⭐ <c>CE-417</c> slice 4b — the ONE <see cref="ActionBindingDrawer"/> for every binding (state entry/exit/
    ///         activity/timer, transition and global-transition guard/action), keyed by <see cref="BehaviorActionBindingFacet"/>;
    ///         the HSM host supplies only its method list (<see cref="HsmBindingMethods"/>).</item>
    ///   <item>A <see cref="HsmCompositeStringDrawer"/> keyed by <c>typeof(string)</c>, dispatching
    ///         <see cref="HsmStateSelectorAttribute"/>, <see cref="HsmEventPickerAttribute"/> and the asset pickers.</item>
    ///   <item>A <see cref="HsmSyncGroupPickerDrawer"/> keyed by <c>typeof(ushort)</c> for sync-group fields.</item>
    /// </list>
    /// </summary>
    /// <param name="exporter">⭐⭐ <c>CE-386</c> — the registered activities/guards and every binding's parameter type.</param>
    /// <param name="catalog">
    /// ⭐⭐ §11.1a — the asset catalogue that feeds the <c>[AiAssetPicker]</c> on <c>StateFacet.SubtreeName</c> and the
    /// binding drawer's blueprint list. ⚠ Optional so headless fixtures need not supply one; ⛔ a production host HAS one
    /// and must pass it, or the field silently offers nothing — the silent-default shape this codebase keeps paying for.
    /// </param>
    public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(
        HsmAsset               asset,
        IActionSchemaExporter? exporter = null,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        var bindingDrawer = new ActionBindingDrawer(new ActionBindingSources(
            asset, kind => HsmBindingMethods.For(asset, exporter, kind), exporter, catalog));

        var composite = new HsmCompositeStringDrawer()
            .Register<HsmStateSelectorAttribute>(new HsmStateSelectorDrawer(asset))
            .Register<HsmEventPickerAttribute>(new HsmEventPickerDrawer(asset));

        // ⭐⭐ §11.1a — the asset pickers. ⚠ Registered only when a catalogue exists: a drawer over a
        //    null catalogue could only ever draw an empty list, and an empty dropdown reads as
        //    "there are no such assets" rather than "this host wired nothing".
        // ⭐⭐⭐ CE-396 — ONE drawer PER KIND, chosen from the attribute the field carries, so
        //    `[AiAssetPicker(BTree)]` (StateFacet.SubtreeName) and `[AiAssetPicker(Blueprint)]`
        //    (CE-385's activity/guard fields) each offer their own kind. ⛔ The previous
        //    registration hard-wired BTree and would have answered for both.
        if (catalog is not null)
        {
            var byKind = new Dictionary<Hrot.Editor.AiShared.AssetKind,
                                        Hrot.Editor.AiShared.Inspector.AiAssetPickerDrawer>();
            composite.Register<Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute>(attr =>
            {
                if (!byKind.TryGetValue(attr.Kind, out var d))
                    byKind[attr.Kind] = d =
                        new Hrot.Editor.AiShared.Inspector.AiAssetPickerDrawer(catalog, attr.Kind);
                return d;
            });
        }

        return new Dictionary<Type, IImGuiFieldDrawer>
        {
            [typeof(string)]                     = composite,
            [typeof(ushort)]                     = new HsmSyncGroupPickerDrawer(asset),
            [typeof(BehaviorActionBindingFacet)] = bindingDrawer,
        };
    }
}

/// <summary>
/// Composite string drawer for HSM: dispatches to attribute-specific sub-drawers
/// when a recognised HSM picker attribute is present on the <see cref="EditNode"/>'s field.
/// Falls through to a plain text input when no marker attribute matches.
/// </summary>
internal sealed class HsmCompositeStringDrawer : IImGuiFieldDrawer
{
    // ⭐⭐⭐ CE-396 — the registry maps an attribute TYPE to a factory over the attribute INSTANCE,
    //    not to a fixed drawer.
    //
    // 🔴 WHY IT HAD TO CHANGE. `AiAssetPickerAttribute` CARRIES A KIND (`[AiAssetPicker(BTree)]`,
    //    `[AiAssetPicker(Blueprint)]`), and the old registry keyed by type alone ⇒ whichever kind was
    //    registered first answered for EVERY kind. With only `StateFacet.SubtreeName` in the tree
    //    that was invisible; CE-385's blueprint pickers would have silently drawn the BTREE list —
    //    a dropdown full of plausible, wrong names. ⛔ A picker that offers the wrong asset kind is
    //    worse than one that offers nothing: nothing is diagnosable.
    // ⭐ Every other picker here is kind-less and registers through the constant overload unchanged.
    private readonly Dictionary<Type, Func<Attribute, IImGuiFieldDrawer?>> _byAttribute = new();

    public Type TargetType => typeof(string);

    public HsmCompositeStringDrawer Register<TAttribute>(IImGuiFieldDrawer drawer) where TAttribute : Attribute
    {
        _byAttribute[typeof(TAttribute)] = _ => drawer;
        return this;
    }

    /// <summary>⭐ <c>CE-396</c> — register a drawer chosen FROM the attribute instance, for marker
    /// attributes that carry a discriminator (today: <c>AiAssetPickerAttribute.Kind</c>).</summary>
    public HsmCompositeStringDrawer Register<TAttribute>(Func<TAttribute, IImGuiFieldDrawer?> factory)
        where TAttribute : Attribute
    {
        _byAttribute[typeof(TAttribute)] = a => factory((TAttribute)a);
        return this;
    }

    public IImGuiFieldDrawer? Resolve(EditNode node)
    {
        if (node is null) return null;
        foreach (var attr in node.Metadata.CustomAttributes)
        {
            if (_byAttribute.TryGetValue(attr.GetType(), out var factory))
                return factory(attr);
        }
        return null;
    }

    public bool DrawInput(ref object value, EditNode node)
    {
        var sub = Resolve(node);
        if (sub is not null)
            return sub.DrawInput(ref value, node);

        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        var s = value as string ?? string.Empty;
        if (ImGuiNET.ImGui.InputText("##str", ref s, 256))
        {
            value = s;
            return true;
        }
        return false;
    }
}

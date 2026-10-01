using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using StructEdit.Core;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Factory: builds the <see cref="IReadOnlyDictionary{Type,IImGuiFieldDrawer}"/> consumed by
/// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetEditService"/> for a specific
/// BTree asset. Called by EditorSubsystem from the <c>ActiveChanged</c> callback whenever the
/// active BTree document switches (SE2).
/// </summary>
public static class BTreePickerDrawerFactory
{
    /// <summary>
    /// Creates a fresh custom-drawers map for <paramref name="asset"/>:
    /// <list type="bullet">
    ///   <item>⭐ <c>CE-417</c> slice 4b — the ONE <see cref="ActionBindingDrawer"/> for the action/condition binding, keyed by
    ///         <see cref="BehaviorActionBindingFacet"/>. The BTree host supplies only its method list: the behavior
    ///         registry's names. ⛔ Replaces <c>BehaviorHashPickerDrawer</c> + <c>BlackboardFieldPickerDrawer</c> and the
    ///         <c>BTreeFacetFqnContext</c> side channel between them.</item>
    ///   <item>a <see cref="CompositeStringDrawer"/> keyed by <c>typeof(string)</c> for the subtree asset picker.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(
        BehaviorTreeAsset      asset,
        BehaviorRegistry       registry,
        IActionSchemaExporter? exporter = null,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null)
    {
        if (asset    is null) throw new ArgumentNullException(nameof(asset));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        var bindingDrawer = new ActionBindingDrawer(
            new ActionBindingSources(asset, _ => registry.GetRegisteredNames(), exporter, catalog));

        var composite = new CompositeStringDrawer();

        // ⭐⭐ CE-439 — the subtree facet's [AiAssetPicker(BTree)] had NO drawer on this host (only the HSM factory registered
        //   one), so it rendered as plain text. ⚠ Registered only with a catalogue, for the HSM factory's reason: an empty list
        //   reads as "there are no such assets". One kind is used on BTree facets (BTree), so one drawer serves.
        if (catalog is not null)
            composite.Register<Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute>(
                new Hrot.Editor.AiShared.Inspector.AiAssetPickerDrawer(catalog, Hrot.Editor.AiShared.AssetKind.BTree));

        return new Dictionary<Type, IImGuiFieldDrawer>
        {
            [typeof(string)]                     = composite,
            [typeof(BehaviorActionBindingFacet)] = bindingDrawer,
        };
    }
}

/// <summary>
/// Composite string drawer that dispatches to attribute-specific sub-drawers
/// when a recognised picker attribute is present on the <see cref="EditNode"/>'s field.
/// Falls through to a plain text input when no marker attribute matches.
/// </summary>
public sealed class CompositeStringDrawer : IImGuiFieldDrawer
{
    private readonly Dictionary<Type, IImGuiFieldDrawer> _byAttribute = new();

    public Type TargetType => typeof(string);

    /// <summary>
    /// Register a sub-drawer that activates when the attribute type
    /// <typeparamref name="TAttribute"/> is present on the field.
    /// </summary>
    public CompositeStringDrawer Register<TAttribute>(IImGuiFieldDrawer drawer) where TAttribute : Attribute
    {
        _byAttribute[typeof(TAttribute)] = drawer;
        return this;
    }

    /// <summary>
    /// Finds the best sub-drawer based on the field's custom attributes
    /// (stored in <see cref="EditNodeMetadata.CustomAttributes"/>),
    /// or returns null if none matches (fallthrough to default).
    /// </summary>
    public IImGuiFieldDrawer? Resolve(EditNode node)
    {
        if (node is null) return null;
        foreach (var attr in node.Metadata.CustomAttributes)
        {
            if (_byAttribute.TryGetValue(attr.GetType(), out var drawer))
                return drawer;
        }
        return null;
    }

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        var sub = Resolve(node);
        if (sub is not null)
            return sub.DrawInput(ref value, node);

        // Default: plain text input.
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

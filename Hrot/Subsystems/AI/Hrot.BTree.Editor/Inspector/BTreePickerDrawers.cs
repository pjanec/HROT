using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="BehaviorHashPickerAttribute"/>. Shows names from the <see cref="BehaviorRegistry"/>.
/// Headless-safe: <see cref="GetItems"/> is usable without ImGui context;
/// <see cref="DrawInput"/> is guarded.
/// </summary>
public sealed class BehaviorHashPickerDrawer : IImGuiFieldDrawer, Hrot.Editor.AiShared.Inspector.IPickerListSource
{
    private readonly BehaviorRegistry _registry;

    public BehaviorHashPickerDrawer(BehaviorRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Type TargetType => typeof(string);

    /// <summary>Returns all registered behavior method names (sorted).</summary>
    public IReadOnlyList<string> GetItems()
        => _registry.GetRegisteredNames().OrderBy(n => n).ToList();

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;

        var current = value as string ?? string.Empty;
        var items   = GetItems();

        if (items.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("(no behaviors registered)");
            return false;
        }

        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##bhpicker", current))
        {
            foreach (var name in items)
            {
                bool selected = name == current;
                if (ImGuiNET.ImGui.Selectable(name, selected) && !selected)
                {
                    value   = name;
                    changed = true;
                }
                if (selected) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="BlackboardFieldPickerAttribute"/>. Shows field names from the
/// active asset's blackboard schema.
/// </summary>
public sealed class BlackboardFieldPickerDrawer : IImGuiFieldDrawer, Hrot.Editor.AiShared.Inspector.IPickerListSource
{
    private readonly BehaviorTreeAsset _asset;

    public BlackboardFieldPickerDrawer(BehaviorTreeAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(string);

    /// <summary>Returns all blackboard variable names for the active asset.</summary>
    public IReadOnlyList<string> GetItems()
        => _asset.BlackboardVariables.Select(v => v.Name).OrderBy(n => n).ToList();

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;

        var current = value as string ?? string.Empty;
        var items   = GetItems();

        if (items.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("(no blackboard fields)");
            return false;
        }

        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##bbpicker", current))
        {
            foreach (var name in items)
            {
                bool selected = name == current;
                if (ImGuiNET.ImGui.Selectable(name, selected) && !selected)
                {
                    value   = name;
                    changed = true;
                }
                if (selected) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}

/// <summary>
/// Factory: builds the <see cref="IReadOnlyDictionary{Type,IImGuiFieldDrawer}"/> consumed by
/// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetEditService"/> for a specific
/// BTree asset. Called by EditorSubsystem from the <c>ActiveChanged</c> callback whenever the
/// active BTree document switches (SE2).
/// </summary>
public static class BTreePickerDrawerFactory
{
    /// <summary>
    /// Creates a fresh custom-drawers map for <paramref name="asset"/>.
    /// The map contains a single <see cref="CompositeStringDrawer"/> keyed by
    /// <see cref="typeof(string)"/>; the composite dispatches:
    /// <list type="bullet">
    ///   <item><see cref="BehaviorHashPickerAttribute"/> → <see cref="BehaviorHashPickerDrawer"/></item>
    ///   <item><see cref="BlackboardFieldPickerAttribute"/> → <see cref="BlackboardFieldPickerDrawer"/></item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(
        BehaviorTreeAsset asset,
        BehaviorRegistry  registry)
    {
        if (asset    is null) throw new ArgumentNullException(nameof(asset));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        var composite = new CompositeStringDrawer()
            .Register<BehaviorHashPickerAttribute>(new BehaviorHashPickerDrawer(registry))
            .Register<BlackboardFieldPickerAttribute>(new BlackboardFieldPickerDrawer(asset));

        return new Dictionary<Type, IImGuiFieldDrawer>
        {
            [typeof(string)] = composite,
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

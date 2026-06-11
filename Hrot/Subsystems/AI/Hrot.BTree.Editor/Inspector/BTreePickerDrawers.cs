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
/// Mutable context shared between <see cref="BTreeFacetMapper"/> (writer) and
/// <see cref="BlackboardFieldPickerDrawer"/> (reader) so the picker can filter
/// blackboard variables by the DtoType of the currently-selected action's FQN,
/// and so the Promote gesture can bind the newly-created variable back to the node.
///
/// <para>
/// Lifecycle: one instance per open BTree asset.  Created by
/// <see cref="BTreePickerDrawerFactory.BuildDrawers"/> (or externally) and passed
/// to both the drawer factory and <see cref="BTreeFacetMapper"/> so they share the
/// same cell.  <see cref="BTreeFacetMapper.GetFacet"/> writes
/// <see cref="CurrentActionFqn"/> and <see cref="CurrentNodeVisualId"/> before
/// returning the facet; the drawer reads them in the same frame when StructEdit
/// calls <see cref="BlackboardFieldPickerDrawer.DrawInput"/>.
/// </para>
/// </summary>
public sealed class BTreeFacetFqnContext
{
    /// <summary>
    /// The method FQN of the action/condition node currently selected in the inspector,
    /// or <see langword="null"/> when no action/condition node is selected.
    /// Written by <see cref="BTreeFacetMapper.GetFacet"/>; read by
    /// <see cref="BlackboardFieldPickerDrawer.GetItems"/>.
    /// </summary>
    public string? CurrentActionFqn { get; set; }

    /// <summary>
    /// The VisualId (as a GUID string) of the action/condition node currently selected.
    /// Written alongside <see cref="CurrentActionFqn"/> so <see cref="BlackboardFieldPickerDrawer"/>
    /// can call <see cref="BlackboardFieldPickerDrawer.Promote"/> with the correct node id.
    /// </summary>
    public string? CurrentNodeVisualId { get; set; }
}

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
/// active asset's blackboard schema, filtered by the DtoType of the currently-selected
/// action's FQN when a <see cref="BTreeFacetFqnContext"/> is provided.
///
/// <para>Headless-safe: <see cref="GetItems"/> and <see cref="HasNoCompatibleVariables"/>
/// are usable without an ImGui context.</para>
///
/// <para><b>Promote→bind (B-2 / Corrective Task 0):</b> when "Promote to new variable"
/// is clicked, the drawer creates the <c>_auto_{visualId:N}</c> variable via
/// <see cref="Promote"/> and immediately sets <paramref name="value"/> to the new name,
/// returning <c>true</c> so StructEdit's normal write-back flows to
/// <see cref="BTreeFacetMapper.ApplyFacet"/> which persists <c>ExpressionTargetField</c>.</para>
/// </summary>
public sealed class BlackboardFieldPickerDrawer : IImGuiFieldDrawer, Hrot.Editor.AiShared.Inspector.IPickerListSource
{
    private readonly BehaviorTreeAsset       _asset;
    private readonly IActionSchemaExporter?  _exporter;
    private readonly Func<string?>?          _fqnAccessor;
    /// <summary>Shared context that also carries the current node VisualId for Promote.</summary>
    private readonly BTreeFacetFqnContext?   _fqnContext;

    /// <summary>
    /// Constructs a drawer bound to <paramref name="asset"/> without type-filtering support.
    /// All blackboard variables are shown regardless of the selected action's DtoType.
    /// </summary>
    public BlackboardFieldPickerDrawer(BehaviorTreeAsset asset)
        : this(asset, null, null, null)
    {
    }

    /// <summary>
    /// Constructs a drawer bound to <paramref name="asset"/> with optional type-filtering.
    /// When <paramref name="exporter"/> and <paramref name="fqnAccessor"/> are both non-null,
    /// <see cref="GetItems"/> returns only variables whose type matches the action's DtoType.
    /// </summary>
    public BlackboardFieldPickerDrawer(
        BehaviorTreeAsset      asset,
        IActionSchemaExporter? exporter,
        Func<string?>?         fqnAccessor)
        : this(asset, exporter, fqnAccessor, null)
    {
    }

    /// <summary>
    /// Full constructor including the optional <paramref name="fqnContext"/> for Promote→bind.
    /// When <paramref name="fqnContext"/> is supplied the Promote gesture reads
    /// <see cref="BTreeFacetFqnContext.CurrentNodeVisualId"/> to derive the auto-variable name.
    /// </summary>
    public BlackboardFieldPickerDrawer(
        BehaviorTreeAsset      asset,
        IActionSchemaExporter? exporter,
        Func<string?>?         fqnAccessor,
        BTreeFacetFqnContext?  fqnContext)
    {
        _asset       = asset       ?? throw new ArgumentNullException(nameof(asset));
        _exporter    = exporter;
        _fqnAccessor = fqnAccessor;
        _fqnContext  = fqnContext;
    }

    public Type TargetType => typeof(string);

    /// <summary>
    /// Returns the subset of blackboard variable names compatible with the current action's
    /// DtoType.  When no exporter or FQN accessor is configured, all variable names are
    /// returned (unfiltered).
    /// </summary>
    public IReadOnlyList<string> GetItems()
    {
        var entries = _asset.BlackboardVariables.ToList();
        if (_exporter is null || _fqnAccessor is null)
            return entries.Select(v => v.Name).OrderBy(n => n).ToList();

        var fqn = _fqnAccessor();
        return BlackboardFieldPickerAttribute.GetCompatibleVariables(fqn, entries, _exporter);
    }

    /// <summary>
    /// True when the current action's FQN resolves to a known schema entry but no blackboard
    /// variable matches its DtoType.  Used to show a "Promote to new variable" affordance in
    /// the inspector and is testable without an ImGui context.
    /// </summary>
    public bool HasNoCompatibleVariables
    {
        get
        {
            if (_exporter is null || _fqnAccessor is null) return false;
            var fqn = _fqnAccessor();
            if (fqn is null) return false;
            if (_exporter.Lookup(fqn) is null) return false;   // unknown FQN – not an error state
            return GetItems().Count == 0;
        }
    }

    /// <summary>
    /// True when a promote action has been requested via <see cref="TriggerPromote"/>.
    /// Reset by calling <see cref="ResetPromoteRequest"/>.
    /// </summary>
    public bool PromoteRequested { get; private set; }

    /// <summary>Sets <see cref="PromoteRequested"/> to true (headless-testable).</summary>
    public void TriggerPromote() => PromoteRequested = true;

    /// <summary>Clears <see cref="PromoteRequested"/>.</summary>
    public void ResetPromoteRequest() => PromoteRequested = false;

    /// <summary>
    /// Creates a new auto-managed blackboard variable named <c>_auto_{visualId:N}</c> whose
    /// type matches the DtoType of the current action's FQN, and returns the generated name.
    /// Returns <see langword="null"/> when the action FQN is not set, is not found in the
    /// exporter, or the asset already contains a variable with the auto-generated name.
    /// </summary>
    /// <param name="facetVisualId">
    /// The <c>VisualId</c> string from the action facet (a GUID string).
    /// Used to derive the auto-variable name: <c>_auto_{guid:N}</c>.
    /// </param>
    public string? Promote(string facetVisualId)
    {
        if (_exporter is null || _fqnAccessor is null) return null;
        var fqn = _fqnAccessor();
        if (fqn is null) return null;
        var entry = _exporter.Lookup(fqn);
        if (entry is null) return null;

        if (!Guid.TryParse(facetVisualId, out var visualGuid)) return null;
        var varName = $"_auto_{visualGuid:N}";

        // Guard: don't create a duplicate.
        if (_asset.BlackboardVariables.Any(v => v.Name == varName)) return varName;

        _asset.AddVariable(new BlackboardVariableEntry(
            Name:          varName,
            FieldType:     entry.DtoType,
            Comment:       null,
            IsAutoManaged: true));

        return varName;
    }

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;

        var current = value as string ?? string.Empty;
        var items   = GetItems();

        if (items.Count == 0 && HasNoCompatibleVariables)
        {
            ImGuiNET.ImGui.TextDisabled(BlackboardFieldPickerAttribute.NoCompatibleVariablesDisplay);
            if (ImGuiNET.ImGui.SmallButton("Promote to new variable"))
            {
                // Corrective Task 0 / B-2: create the variable AND bind the field in one gesture.
                // _fqnContext?.CurrentNodeVisualId gives us the owning node's VisualId.
                var visualId = _fqnContext?.CurrentNodeVisualId ?? string.Empty;
                var newName  = Promote(visualId);
                if (newName is not null)
                {
                    value = newName;
                    return true;
                }
                // Fallback: queue the flag for any external consumer.
                TriggerPromote();
            }
            return false;
        }

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
    /// When <paramref name="exporter"/> and <paramref name="fqnContext"/> are provided, the
    /// <see cref="BlackboardFieldPickerDrawer"/> filters variables by the current action's DtoType.
    /// </summary>
    public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(
        BehaviorTreeAsset      asset,
        BehaviorRegistry       registry,
        IActionSchemaExporter? exporter    = null,
        BTreeFacetFqnContext?  fqnContext   = null)
    {
        if (asset    is null) throw new ArgumentNullException(nameof(asset));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        Func<string?>? fqnAccessor = fqnContext is not null
            ? () => fqnContext.CurrentActionFqn
            : null;

        var bbDrawer = new BlackboardFieldPickerDrawer(asset, exporter, fqnAccessor, fqnContext);

        var composite = new CompositeStringDrawer()
            .Register<BehaviorHashPickerAttribute>(new BehaviorHashPickerDrawer(registry))
            .Register<BlackboardFieldPickerAttribute>(bbDrawer);

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

using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// CA-02 (Slice 1a) — Details-panel drawer for <see cref="GetComponentNode"/>: a filtered picker
/// over <see cref="IComponentTypeProvider"/> for <see cref="GetComponentNode.ComponentTypeFqn"/>,
/// mirroring <see cref="GetSharedNodeDrawer"/>'s "Shared Type FQN" picker
/// (<see cref="SharedTypePickerLogic"/> is reused as-is — it is a generic string-list
/// filter/contains helper with no shared-struct-specific logic).
/// <para>
/// UNLIKE <see cref="GetSharedNodeDrawer"/>, there is no "Expand to field pins" toggle: a
/// component isn't a single pin value (there is no legacy whole-component read to collapse back
/// to), so selecting a component type ALWAYS (re-)bakes the full <see cref="GetComponentNode.Fields"/>
/// set via <see cref="ComponentFieldReflector"/>. Managed fields are still shown, flagged with a
/// read-only "not persistable / not mutable" caveat (Q#15 constraint), even though this slice
/// (1a, unmanaged) doesn't yet wire a managed read path (CA-05).
/// </para>
/// </summary>
public sealed class GetComponentNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly IComponentTypeProvider _typeProvider;

    public GetComponentNodeDrawer(IEditService editService, IComponentTypeProvider typeProvider)
    {
        _editService  = editService  ?? throw new ArgumentNullException(nameof(editService));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    public bool Handles(Node node) => node is GetComponentNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new GetComponentNodeSession((GetComponentNode)node, parentAsset, _editService, _typeProvider);
}

/// <summary>
/// Edit session for <see cref="GetComponentNode"/>. See <see cref="GetComponentNodeDrawer"/> for
/// the rationale (no collapse toggle — always multi-pin).
/// </summary>
internal sealed class GetComponentNodeSession : INodeEditSession
{
    private readonly GetComponentNode _node;
    private readonly BlueprintAsset   _parent;
    private readonly IEditService     _editService;
    private readonly IComponentTypeProvider _typeProvider;

    // ImGui view-state only (the incremental filter box's current text). Not part of the
    // node's data and not exercised by Draw()-adjacent test hooks below.
    private string _typeFilterText = "";

    public bool IsDirty { get; private set; }

    public GetComponentNodeSession(
        GetComponentNode node, BlueprintAsset parentAsset, IEditService editService, IComponentTypeProvider typeProvider)
    {
        _node         = node;
        _parent       = parentAsset;
        _editService  = editService;
        _typeProvider = typeProvider;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer picking (or otherwise setting) the Component Type.</summary>
    internal void SetComponentTypeFqnForTest(string fqn) => ApplyComponentTypeFqn(fqn);

    /// <summary>Test hook: the full, unfiltered set of FQNs the type provider discovered.</summary>
    internal IReadOnlyList<string> GetAvailableComponentTypesForTest() => _typeProvider.GetComponentTypeFqns();

    /// <summary>Test hook: the discovered FQNs matching <paramref name="filterText"/> (case-insensitive substring).</summary>
    internal IReadOnlyList<string> GetFilteredComponentTypesForTest(string filterText)
        => SharedTypePickerLogic.Filter(_typeProvider.GetComponentTypeFqns(), filterText);

    /// <summary>
    /// Test hook: true when the node's current <see cref="GetComponentNode.ComponentTypeFqn"/> is
    /// non-empty but NOT present in the provider's discovered set (unloaded assembly, typo,
    /// renamed type). The picker must display and preserve such a value rather than blank it.
    /// </summary>
    internal bool IsCurrentComponentTypeFqnUnlistedForTest()
        => !string.IsNullOrEmpty(_node.ComponentTypeFqn)
           && !SharedTypePickerLogic.Contains(_typeProvider.GetComponentTypeFqns(), _node.ComponentTypeFqn);

    /// <summary>Test hook: the current component type's reflected fields (name/type/managed), or empty when unresolved.</summary>
    internal IReadOnlyList<ReflectedComponentField> GetCurrentFieldsForTest()
        => ComponentFieldReflector.TryReflect(_node.ComponentTypeFqn) ?? new List<ReflectedComponentField>();

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyComponentTypeFqn(string fqn)
    {
        if (fqn == _node.ComponentTypeFqn) return;
        _node.ComponentTypeFqn = fqn;
        // A component isn't a single pin value -- always (re-)bake the FULL field set for the newly
        // selected type (unlike GetShared's Q#14 toggle, there is no collapsed shape to preserve).
        var reflected = ComponentFieldReflector.TryReflect(fqn);
        var fields = reflected is { Count: > 0 }
            ? reflected.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList()
            : new List<ComponentFieldDecl>();

        // CA-07a (R1 curated-accessor): APPEND one collection decl per discovered
        // [BlueprintCollection]/[BlueprintCollectionItem] accessor pair, after the scalar fields
        // (append order = Fields order = pin order, kept in lockstep with NodePinSchema/Stage0).
        foreach (var c in ComponentFieldReflector.TryReflectCollections(fqn))
        {
            fields.Add(new ComponentFieldDecl
            {
                Name             = c.Name,
                TypeId           = "",
                IsCollection     = true,
                ElementTypeId    = c.ElementTypeId,
                CountAccessorFqn = c.CountAccessorFqn,
                ItemAccessorFqn  = c.ItemAccessorFqn,
            });
        }

        // Non-null (multi-pin mode) whenever there is ANYTHING to expose -- scalar fields and/or
        // collections; a component with ONLY collections (no scalar fields) must still take the
        // multi-pin path, not fall back to the legacy single-"Value" shape.
        _node.Fields = fields.Count > 0 ? fields : null;
        // CA-05 (Slice 1b): bake whether the picked component TYPE itself is managed (a class) --
        // drives Stage5's GetManagedComponentRO vs GetComponentRO emit choice.
        _node.IsManaged = ComponentFieldReflector.IsManagedComponent(fqn);
        MarkChanged();
    }

    private void MarkChanged()
    {
        IsDirty = true;
        _editService.MarkDirty(_parent);
        // Selecting a component type changes the node's projected pins, so signal a STRUCTURAL
        // change: the canvas graph model re-projects itself. Data-driven -- this drawer never
        // references the canvas; the composition root wires the refresh (see BlueprintDocumentFactory,
        // mirrors SharedNodeDrawers/NotifyStructureChanged).
        (_editService as EditService)?.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Get Component");
        ImGui.Separator();

        DrawComponentTypePicker();
        DrawFieldSummary();

        ImGui.TextDisabled("(self, or an optional Target entity -- BlueprintSharedState is NOT involved: reads view.GetComponentRO<T>)");
        if (string.IsNullOrEmpty(_node.ComponentTypeFqn))
            ImGui.TextColored(EditorColors.Warning, "(pick a Component Type)");
    }

    /// <summary>See <see cref="GetSharedNodeSession.DrawSharedTypePicker"/> for the rationale.</summary>
    private void DrawComponentTypePicker()
    {
        var current  = _node.ComponentTypeFqn ?? "";
        var unlisted = IsCurrentComponentTypeFqnUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Component Type", comboLabel))
        {
            ImGui.InputTextWithHint("##GetComponentTypeFilter", "Filter...", ref _typeFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var fqn in GetFilteredComponentTypesForTest(_typeFilterText))
            {
                bool selected = fqn == current;
                if (ImGui.Selectable(fqn, selected))
                    ApplyComponentTypeFqn(fqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current value not in the discovered type list — kept as-is: {current})");
    }

    /// <summary>
    /// Lists the currently-baked field pins, flagging managed fields with the Q#15 "read/pass-through
    /// only — never persisted or mutated" caveat (managed writes/persistence are out of this slice's
    /// scope entirely; this is purely an informational heads-up for the designer).
    /// <para>
    /// CA-05 (Slice 1b): when the whole COMPONENT is managed (<see cref="GetComponentNode.IsManaged"/>),
    /// every field pin is sourced off a managed instance (<c>view.GetManagedComponentRO&lt;T&gt;</c>)
    /// even if an individual field's OWN type happens to be a primitive/blittable value (e.g. an
    /// <c>int</c> field on a <c>class</c> component) -- so the node-level caveat below fires
    /// independently of the per-FIELD <see cref="ReflectedComponentField.IsManaged"/> loop, which
    /// stays to cover the OTHER case (a managed field embedded in an otherwise-unmanaged struct
    /// component, pre-existing since CA-02).
    /// </para>
    /// </summary>
    private void DrawFieldSummary()
    {
        if (_node.Fields is not { Count: > 0 } fields) return;

        ImGui.TextDisabled($"({fields.Count} field pin(s) + Target(in)/Found(out))");

        if (_node.IsManaged)
            ImGui.TextColored(EditorColors.Warning,
                "  managed component — all fields are read-only: cannot be stored in a Variable/" +
                "WorkingState/Shared, pass to a library/function call only");

        var reflected = ComponentFieldReflector.TryReflect(_node.ComponentTypeFqn);
        if (reflected is null) return;

        foreach (var f in reflected.Where(f => f.IsManaged))
            ImGui.TextColored(EditorColors.Warning,
                $"  {f.Name} ({f.TypeId}): managed — read/pass-through only, never persisted or mutated");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

/// <summary>
/// CA-04/CA-06 — Details-panel drawer for <see cref="SetComponentNode"/>: a filtered picker
/// over a WRITABLE-only <see cref="IComponentTypeProvider"/> (the caller wires
/// <see cref="ReflectionWritableComponentTypeProvider"/> -- see
/// <see cref="BlueprintEditorBootstrap.CreateNodeDrawerRegistry"/>) for
/// <see cref="SetComponentNode.ComponentTypeFqn"/>. No "Expand to field pins" toggle (a component
/// write is always multi-pin/single-pin, never a collapsed legacy shape). Selecting a type bakes
/// ONE of two mutually-exclusive shapes (mirrors <see cref="GetComponentNodeDrawer"/>'s managed
/// branch, CA-05):
/// <list type="bullet">
///   <item>UNMANAGED component (<see cref="ComponentFieldReflector.IsManagedComponent"/> false):
///   <see cref="SetComponentNode.IsManaged"/> = false, <see cref="SetComponentNode.Fields"/> = the
///   FULL reflected field set -- one data-IN pin per field (CA-03/CA-04, unchanged).</item>
///   <item>MANAGED component (CA-06, Slice W2, Q#16-C): <see cref="SetComponentNode.IsManaged"/> =
///   true, <see cref="SetComponentNode.Fields"/> = <c>null</c> (NEVER baked -- per-field managed
///   write is forbidden) -- Stage0/NodePinSchema instead project a SINGLE "Value" data-IN pin typed
///   by the component, fed by a library/function call (or another managed pass-through) that
///   constructs a fresh instance.</item>
/// </list>
/// </summary>
public sealed class SetComponentNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly IComponentTypeProvider _typeProvider;

    public SetComponentNodeDrawer(IEditService editService, IComponentTypeProvider typeProvider)
    {
        _editService  = editService  ?? throw new ArgumentNullException(nameof(editService));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    public bool Handles(Node node) => node is SetComponentNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SetComponentNodeSession((SetComponentNode)node, parentAsset, _editService, _typeProvider);
}

/// <summary>
/// Edit session for <see cref="SetComponentNode"/>. See <see cref="SetComponentNodeDrawer"/> for
/// the rationale (no collapse toggle — always multi-pin; writable-only picker).
/// </summary>
internal sealed class SetComponentNodeSession : INodeEditSession
{
    private readonly SetComponentNode _node;
    private readonly BlueprintAsset   _parent;
    private readonly IEditService     _editService;
    private readonly IComponentTypeProvider _typeProvider;

    // ImGui view-state only (the incremental filter box's current text). Not part of the
    // node's data and not exercised by Draw()-adjacent test hooks below.
    private string _typeFilterText = "";

    public bool IsDirty { get; private set; }

    public SetComponentNodeSession(
        SetComponentNode node, BlueprintAsset parentAsset, IEditService editService, IComponentTypeProvider typeProvider)
    {
        _node         = node;
        _parent       = parentAsset;
        _editService  = editService;
        _typeProvider = typeProvider;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer picking (or otherwise setting) the Component Type.</summary>
    internal void SetComponentTypeFqnForTest(string fqn) => ApplyComponentTypeFqn(fqn);

    /// <summary>Test hook: the full, unfiltered (writable-only) set of FQNs the type provider discovered.</summary>
    internal IReadOnlyList<string> GetAvailableComponentTypesForTest() => _typeProvider.GetComponentTypeFqns();

    /// <summary>Test hook: the discovered FQNs matching <paramref name="filterText"/> (case-insensitive substring).</summary>
    internal IReadOnlyList<string> GetFilteredComponentTypesForTest(string filterText)
        => SharedTypePickerLogic.Filter(_typeProvider.GetComponentTypeFqns(), filterText);

    /// <summary>
    /// Test hook: true when the node's current <see cref="SetComponentNode.ComponentTypeFqn"/> is
    /// non-empty but NOT present in the provider's discovered (writable-only) set (unloaded
    /// assembly, typo, renamed type, or a component that lost its <c>[BlueprintWritable]</c>
    /// attribute). The picker must display and preserve such a value rather than blank it.
    /// </summary>
    internal bool IsCurrentComponentTypeFqnUnlistedForTest()
        => !string.IsNullOrEmpty(_node.ComponentTypeFqn)
           && !SharedTypePickerLogic.Contains(_typeProvider.GetComponentTypeFqns(), _node.ComponentTypeFqn);

    /// <summary>Test hook: the current component type's reflected fields (name/type/managed), or empty when unresolved.</summary>
    internal IReadOnlyList<ReflectedComponentField> GetCurrentFieldsForTest()
        => ComponentFieldReflector.TryReflect(_node.ComponentTypeFqn) ?? new List<ReflectedComponentField>();

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyComponentTypeFqn(string fqn)
    {
        if (fqn == _node.ComponentTypeFqn) return;
        _node.ComponentTypeFqn = fqn;
        // CA-06 (Slice W2, Q#16-C): bake whether the picked component TYPE itself is managed --
        // drives Stage5's whole-replace-via-ECB emit choice (mirrors
        // GetComponentNodeSession.ApplyComponentTypeFqn's CA-05 IsManaged bake).
        _node.IsManaged = ComponentFieldReflector.IsManagedComponent(fqn);
        if (_node.IsManaged)
        {
            // Managed write is WHOLE-REPLACE ONLY -- never bake per-field Fields (Stage2's BP2064
            // rejects a managed node that carries them). Stage0/NodePinSchema project a single
            // "Value" pin from ComponentTypeFqn directly; there is nothing to reflect per-field here.
            _node.Fields = null;
        }
        else
        {
            // Unmanaged (CA-03/CA-04, unchanged): always (re-)bake the FULL field set for the newly
            // selected type -- a component write isn't a single pin value.
            var reflected = ComponentFieldReflector.TryReflect(fqn);
            _node.Fields = reflected is { Count: > 0 }
                ? reflected.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList()
                : null;
        }
        MarkChanged();
    }

    private void MarkChanged()
    {
        IsDirty = true;
        _editService.MarkDirty(_parent);
        // Selecting a component type changes the node's projected pins, so signal a STRUCTURAL
        // change: the canvas graph model re-projects itself. Data-driven -- this drawer never
        // references the canvas; the composition root wires the refresh (see BlueprintDocumentFactory,
        // mirrors SharedNodeDrawers/NotifyStructureChanged).
        (_editService as EditService)?.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Set Component");
        ImGui.Separator();

        DrawComponentTypePicker();
        DrawFieldSummary();

        if (_node.IsManaged)
            ImGui.TextDisabled("(self only, write-if-present -- whole-replace via ecb.SetManagedComponent<T>, guarded by HasManagedComponent<T>; no implicit add)");
        else
            ImGui.TextDisabled("(self only, write-if-present -- writes view.GetComponentRW<T>, guarded by HasComponent<T>; no implicit add)");
        if (string.IsNullOrEmpty(_node.ComponentTypeFqn))
            ImGui.TextColored(EditorColors.Warning, "(pick a Component Type)");
    }

    /// <summary>See <see cref="GetComponentNodeSession.DrawComponentTypePicker"/> for the rationale.</summary>
    private void DrawComponentTypePicker()
    {
        var current  = _node.ComponentTypeFqn ?? "";
        var unlisted = IsCurrentComponentTypeFqnUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Component Type", comboLabel))
        {
            ImGui.InputTextWithHint("##SetComponentTypeFilter", "Filter...", ref _typeFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var fqn in GetFilteredComponentTypesForTest(_typeFilterText))
            {
                bool selected = fqn == current;
                if (ImGui.Selectable(fqn, selected))
                    ApplyComponentTypeFqn(fqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current value not in the discovered type list — kept as-is: {current})");
    }

    /// <summary>
    /// Summarizes the currently-baked pin shape. MANAGED (CA-06, Slice W2): a single "Value" pin,
    /// whole-replace -- feed it from a library/function call (or another managed pass-through) that
    /// constructs a fresh instance; there is no per-field write path for a managed component at all
    /// (architect-forbidden -- snapshot aliasing), so there is nothing further to list. UNMANAGED
    /// (CA-03/CA-04, unchanged): lists the baked field pins, flagging any still-managed FIELD
    /// embedded in the otherwise-unmanaged struct with a "write path not yet wired" caveat (that
    /// per-FIELD case is orthogonal to the node-level <see cref="SetComponentNode.IsManaged"/> flag
    /// this drawer branches on -- see <see cref="ComponentFieldReflector.IsManagedComponent"/>'s doc
    /// comment for the distinction).
    /// </summary>
    private void DrawFieldSummary()
    {
        if (_node.IsManaged)
        {
            ImGui.TextDisabled("(single \"Value\" pin -- whole-component replace; feed it from a " +
                "library/function call that constructs a fresh instance)");
            return;
        }

        if (_node.Fields is not { Count: > 0 } fields) return;

        ImGui.TextDisabled($"({fields.Count} field pin(s) + Written(out))");

        var reflected = ComponentFieldReflector.TryReflect(_node.ComponentTypeFqn);
        if (reflected is null) return;

        foreach (var f in reflected.Where(f => f.IsManaged))
            ImGui.TextColored(EditorColors.Warning,
                $"  {f.Name} ({f.TypeId}): managed — write path not yet wired (CA-06); this pin has no effect until then");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

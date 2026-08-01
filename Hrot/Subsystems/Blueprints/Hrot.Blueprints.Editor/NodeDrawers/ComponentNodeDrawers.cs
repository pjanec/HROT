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
        _node.Fields = reflected is { Count: > 0 }
            ? reflected.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList()
            : null;
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
    /// </summary>
    private void DrawFieldSummary()
    {
        if (_node.Fields is not { Count: > 0 } fields) return;

        ImGui.TextDisabled($"({fields.Count} field pin(s) + Target(in)/Found(out))");

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
/// CA-04 (Slice W1) — Details-panel drawer for <see cref="SetComponentNode"/>: a filtered picker
/// over a WRITABLE-only <see cref="IComponentTypeProvider"/> (the caller wires
/// <see cref="ReflectionWritableComponentTypeProvider"/> -- see
/// <see cref="BlueprintEditorBootstrap.CreateNodeDrawerRegistry"/>) for
/// <see cref="SetComponentNode.ComponentTypeFqn"/>. Mirrors <see cref="GetComponentNodeDrawer"/>
/// exactly: no "Expand to field pins" toggle (a component write is always multi-pin), selecting a
/// type always (re-)bakes the FULL field set. Managed fields are still listed (this batch doesn't
/// special-case managed components out of the picker), flagged with a "write path not yet wired"
/// caveat -- CA-06 builds the managed (ECB whole-replace) write lowering; wiring a managed field's
/// pin here has no compiled effect until then.
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
        // A component write isn't a single pin value -- always (re-)bake the FULL field set for the
        // newly selected type (mirrors GetComponentNodeSession.ApplyComponentTypeFqn).
        var reflected = ComponentFieldReflector.TryReflect(fqn);
        _node.Fields = reflected is { Count: > 0 }
            ? reflected.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList()
            : null;
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
    /// Lists the currently-baked field pins, flagging managed fields with a "write path not yet
    /// wired" caveat -- CA-06 builds the managed (ECB whole-replace) write; this slice (W1) only
    /// lowers the unmanaged per-field write, so a managed field's pin can be wired but has no
    /// compiled effect yet.
    /// </summary>
    private void DrawFieldSummary()
    {
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

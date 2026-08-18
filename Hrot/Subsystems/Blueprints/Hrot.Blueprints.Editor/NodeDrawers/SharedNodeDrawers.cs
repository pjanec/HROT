using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Node-drawer for <see cref="GetSharedNode"/> (Slice 2a-3).
/// <para>
/// Lets the designer author <see cref="GetSharedNode.VariableId"/> (the entity-scoped shared
/// slot name) and <see cref="GetSharedNode.SharedTypeId"/> (the Category-1 shared struct FQN)
/// on an already-placed node, via the same Details-panel <see cref="IBlueprintNodeDrawer"/> /
/// <see cref="INodeEditSession"/> mechanism used by <see cref="FunctionCallNodeDrawer"/> and
/// <see cref="LiteralNodeDrawer"/> — see <see cref="BlueprintEditorBootstrap.CreateNodeDrawerRegistry"/>.
/// </para>
/// <para>
/// <see cref="GetSharedNode.VariableId"/> stays free-text (a designer-chosen, entity-scoped
/// slot name -- there is no fixed set to pick from). <see cref="GetSharedNode.SharedTypeId"/>
/// is instead authored via a filtered picker (incremental filter box + selectable list) over
/// the FQNs an <see cref="ISharedStructTypeProvider"/> discovers -- by default,
/// <see cref="ReflectionSharedStructTypeProvider"/>, which scans loaded assemblies for
/// <c>[BlackboardDtoStruct]</c> value types, the same predicate
/// <see cref="Hrot.Editor.AiShared.Blackboard.BlackboardFieldClassifier"/> uses. If the node's
/// current <c>SharedTypeId</c> isn't in the discovered set (assembly not loaded, typo, renamed
/// type) the picker still shows and preserves it rather than silently blanking it.
/// </para>
/// </summary>
public sealed class GetSharedNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    public GetSharedNodeDrawer(IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _editService  = editService  ?? throw new ArgumentNullException(nameof(editService));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    public bool Handles(Node node) => node is GetSharedNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new GetSharedNodeSession((GetSharedNode)node, parentAsset, _editService, _typeProvider);
}

/// <summary>
/// Node-drawer for <see cref="SetSharedNode"/> (Slice 2a-3). See <see cref="GetSharedNodeDrawer"/>
/// for the rationale (free-text VariableId + picker-driven SharedTypeId).
/// </summary>
public sealed class SetSharedNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    public SetSharedNodeDrawer(IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _editService  = editService  ?? throw new ArgumentNullException(nameof(editService));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    public bool Handles(Node node) => node is SetSharedNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SetSharedNodeSession((SetSharedNode)node, parentAsset, _editService, _typeProvider);
}

/// <summary>
/// Non-ImGui logic shared by <see cref="GetSharedNodeSession"/> and
/// <see cref="SetSharedNodeSession"/> for the "Shared Type FQN" picker, kept separate from
/// <c>Draw()</c> so it is headlessly testable (mirrors the rest of this file's
/// mutation-helpers-vs-Draw split).
/// </summary>
internal static class SharedTypePickerLogic
{
    /// <summary>
    /// Returns the subset of <paramref name="candidates"/> whose text contains
    /// <paramref name="filterText"/> (case-insensitive, substring match). Returns all
    /// candidates unchanged when <paramref name="filterText"/> is null/empty.
    /// </summary>
    internal static IReadOnlyList<string> Filter(IReadOnlyList<string> candidates, string? filterText)
    {
        if (string.IsNullOrEmpty(filterText)) return candidates;
        return candidates
            .Where(c => c.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="value"/> is a non-empty string present (ordinal match) in
    /// <paramref name="candidates"/>.
    /// </summary>
    internal static bool Contains(IReadOnlyList<string> candidates, string? value)
        => !string.IsNullOrEmpty(value) && candidates.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// Q#14 multi-pin: reflects a shared-struct FQN into its per-field decls (Name + field-type FQN + byte
/// offset) so a GetShared/SetShared node can expose one pin per field. Editor-side reflection (net8) —
/// the netstandard2.0 compiler never reflects; it consumes the baked <see cref="SharedFieldDecl"/> list.
/// Returns <c>null</c> when the type can't be resolved in a loaded assembly, isn't a value type, or has a
/// field whose offset can't be computed (non-blittable) — the caller then keeps the legacy whole-struct pin.
/// </summary>
internal static class SharedStructFieldReflector
{
    internal static List<SharedFieldDecl>? TryReflect(string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;
        var type = ResolveType(fqn!);
        if (type is null || !type.IsValueType) return null;

        var decls = new List<SharedFieldDecl>();
        foreach (var f in type.GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            int offset;
            try { offset = (int)System.Runtime.InteropServices.Marshal.OffsetOf(type, f.Name); }
            catch { return null; } // non-blittable field → per-field offset write impossible; keep whole-struct
            decls.Add(new SharedFieldDecl
            {
                Name   = f.Name,
                TypeId = f.FieldType.FullName ?? f.FieldType.Name,
                Offset = offset,
            });
        }
        return decls.Count > 0 ? decls : null;
    }

    private static Type? ResolveType(string fqn)
    {
        var t = Type.GetType(fqn);
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { t = asm.GetType(fqn); } catch { continue; }
            if (t != null) return t;
        }
        return null;
    }
}

/// <summary>
/// Edit session for <see cref="GetSharedNode"/>.
/// <para>
/// Mutation logic lives in <see cref="SetVariableIdForTest"/>/<see cref="SetSharedTypeIdForTest"/>
/// (internal test hooks, mirroring <c>FunctionCallNodeSession.SelectFunctionGraphForTest</c>) so
/// it is exercised headlessly; <see cref="Draw"/> calls the exact same helpers and is the only
/// ImGui-coupled surface (Windows-verifiable only). The "Shared Type FQN" filter/selection logic
/// is likewise headlessly testable via <see cref="GetAvailableSharedTypesForTest"/>,
/// <see cref="GetFilteredSharedTypesForTest"/>, and <see cref="IsCurrentSharedTypeIdUnlistedForTest"/>.
/// </para>
/// </summary>
internal sealed class GetSharedNodeSession : INodeEditSession
{
    private readonly GetSharedNode  _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    // ImGui view-state only (the incremental filter box's current text). Not part of the
    // node's data and not exercised by Draw()-adjacent test hooks below.
    private string _typeFilterText = "";

    public bool IsDirty { get; private set; }

    public GetSharedNodeSession(
        GetSharedNode node, BlueprintAsset parentAsset, IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _node         = node;
        _parent       = parentAsset;
        _editService  = editService;
        _typeProvider = typeProvider;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer typing into the "Slot Name" field.</summary>
    internal void SetVariableIdForTest(string variableId) => ApplyVariableId(variableId);

    /// <summary>
    /// Test hook: simulates the designer selecting (or otherwise setting) the "Shared Type FQN"
    /// value -- used both for picker selections and for the pre-existing free-text mutation path.
    /// </summary>
    internal void SetSharedTypeIdForTest(string sharedTypeId) => ApplySharedTypeId(sharedTypeId);

    /// <summary>Test hook: toggles multi-pin (field) expansion — bakes/clears the per-field decls.</summary>
    internal void SetExpandFieldsForTest(bool expand) => ApplyExpandFields(expand);

    /// <summary>Test hook: true when the node is currently in multi-pin (expanded) mode.</summary>
    internal bool IsExpandedForTest() => _node.Fields is { Count: > 0 };

    /// <summary>Test hook: the full, unfiltered set of FQNs the type provider discovered.</summary>
    internal IReadOnlyList<string> GetAvailableSharedTypesForTest() => _typeProvider.GetSharedStructTypeFqns();

    /// <summary>Test hook: the discovered FQNs matching <paramref name="filterText"/> (case-insensitive substring).</summary>
    internal IReadOnlyList<string> GetFilteredSharedTypesForTest(string filterText)
        => SharedTypePickerLogic.Filter(_typeProvider.GetSharedStructTypeFqns(), filterText);

    /// <summary>
    /// Test hook: true when the node's current <see cref="GetSharedNode.SharedTypeId"/> is
    /// non-empty but NOT present in the provider's discovered set (unloaded assembly, typo,
    /// renamed type). The picker must display and preserve such a value rather than blank it.
    /// </summary>
    internal bool IsCurrentSharedTypeIdUnlistedForTest()
        => !string.IsNullOrEmpty(_node.SharedTypeId)
           && !SharedTypePickerLogic.Contains(_typeProvider.GetSharedStructTypeFqns(), _node.SharedTypeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyVariableId(string variableId)
    {
        if (variableId == _node.VariableId) return;
        RecordChange($"Set Shared Slot '{variableId}'", () => _node.VariableId = variableId);
    }

    private void ApplySharedTypeId(string sharedTypeId)
    {
        if (sharedTypeId == _node.SharedTypeId) return;
        RecordChange($"Set Shared Type '{sharedTypeId}'", () =>
        {
            _node.SharedTypeId = sharedTypeId;
            // Q#14: if the node is in multi-pin (expanded) mode, re-bake the field decls for the new type.
            if (_node.Fields is { Count: > 0 })
                _node.Fields = SharedStructFieldReflector.TryReflect(sharedTypeId);
        });
    }

    /// <summary>
    /// Q#14 multi-pin toggle: when <paramref name="expand"/> is true, reflect the current shared struct's
    /// fields and expose one pin per field; when false, collapse back to the single whole-struct pin. The
    /// designer-facing analog of Unreal's "Split Struct Pin" at node granularity.
    /// </summary>
    private void ApplyExpandFields(bool expand)
    {
        var newFields = expand ? SharedStructFieldReflector.TryReflect(_node.SharedTypeId) : null;
        bool wasExpanded = _node.Fields is { Count: > 0 };
        bool nowExpanded = newFields is { Count: > 0 };
        if (wasExpanded == nowExpanded && !nowExpanded) return;
        RecordChange(expand ? "Expand Struct Pins" : "Collapse Struct Pins",
            () => _node.Fields = newFields);
    }

    /// <summary>
    /// BP-11: the node's undo-relevant state. Snapshotted whole rather than per-field because
    /// changing the shared type also re-bakes <c>Fields</c> — one gesture, two writes, which is why
    /// <c>GraphCommand.SetNodeProperty</c> (one key, one value) cannot carry it. <c>Fields</c> is
    /// captured by reference, sound because every mutation here <em>replaces</em> the list.
    /// </summary>
    private readonly record struct NodeState(string VariableId, string SharedTypeId, List<SharedFieldDecl>? Fields);

    private NodeState Capture() => new(_node.VariableId, _node.SharedTypeId, _node.Fields);

    private void Restore(NodeState s)
    {
        _node.VariableId   = s.VariableId;
        _node.SharedTypeId = s.SharedTypeId;
        _node.Fields       = s.Fields;
    }

    /// <summary>BP-11: runs <paramref name="mutate"/> as an undoable edit on the shared undo stack.</summary>
    private void RecordChange(string label, Action mutate)
    {
        var before = Capture();
        _editService.RecordPropertyEdit(
            _parent, label,
            apply: () => { mutate();        AfterChange(); },
            undo:  () => { Restore(before); AfterChange(); });
    }

    private void AfterChange()
    {
        IsDirty = true;
        // Every shared-node edit (slot-name label, shared type, field expansion) changes the node's
        // projected pins, so signal a STRUCTURAL change: the canvas graph model re-projects itself.
        // Data-driven — this drawer never references the canvas; the composition root wires the refresh
        // (see BlueprintDocumentFactory).
        _editService.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Get Shared");
        ImGui.Separator();

        var variableId = _node.VariableId ?? "";
        if (ImGui.InputText("Slot Name", ref variableId, 256))
            ApplyVariableId(variableId);

        DrawSharedTypePicker();
        DrawExpandFieldsToggle();

        ImGui.TextDisabled("(entity-scoped, self only — reads BlueprintSharedState.TryGetShared<T>)");
        if (string.IsNullOrEmpty(_node.VariableId) || string.IsNullOrEmpty(_node.SharedTypeId))
            ImGui.TextColored(EditorColors.Warning, "(both Slot Name and Shared Type FQN are required)");
    }

    /// <summary>
    /// Filtered combo picker for <see cref="GetSharedNode.SharedTypeId"/>: an incremental
    /// filter box above a selectable list of discovered <c>[BlackboardDtoStruct]</c> FQNs
    /// (mirrors <c>FilteredTypeComboFieldDrawer</c>/<c>BehaviorHashPickerDrawer</c>'s
    /// BeginCombo+Selectable pattern). If the current value isn't in the discovered set it is
    /// still surfaced as a selectable entry so re-opening/closing this combo without picking
    /// anything new never clears <see cref="GetSharedNode.SharedTypeId"/> (design constraint:
    /// never silently lose an unlisted value).
    /// </summary>
    private void DrawSharedTypePicker()
    {
        var current  = _node.SharedTypeId ?? "";
        var unlisted = IsCurrentSharedTypeIdUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Shared Type FQN", comboLabel))
        {
            ImGui.InputTextWithHint("##GetSharedTypeFilter", "Filter...", ref _typeFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var fqn in GetFilteredSharedTypesForTest(_typeFilterText))
            {
                bool selected = fqn == current;
                if (ImGui.Selectable(fqn, selected))
                    ApplySharedTypeId(fqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current value not in the discovered type list — kept as-is: {current})");
    }

    /// <summary>Q#14 multi-pin toggle checkbox (only shown once a shared type is chosen).</summary>
    private void DrawExpandFieldsToggle()
    {
        if (string.IsNullOrEmpty(_node.SharedTypeId)) return;
        bool expanded = _node.Fields is { Count: > 0 };
        if (ImGui.Checkbox("Expand to field pins (multi-pin)", ref expanded))
            ApplyExpandFields(expanded);
        if (_node.Fields is { Count: > 0 } fields)
            ImGui.TextDisabled($"({fields.Count} field pin(s) — set/read individual fields; unset preserved)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

/// <summary>
/// Edit session for <see cref="SetSharedNode"/>. See <see cref="GetSharedNodeSession"/> for the
/// test-hook and picker rationale.
/// </summary>
internal sealed class SetSharedNodeSession : INodeEditSession
{
    private readonly SetSharedNode  _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    private string _typeFilterText = "";

    public bool IsDirty { get; private set; }

    public SetSharedNodeSession(
        SetSharedNode node, BlueprintAsset parentAsset, IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _node         = node;
        _parent       = parentAsset;
        _editService  = editService;
        _typeProvider = typeProvider;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    internal void SetVariableIdForTest(string variableId) => ApplyVariableId(variableId);

    internal void SetSharedTypeIdForTest(string sharedTypeId) => ApplySharedTypeId(sharedTypeId);

    /// <summary>Test hook: toggles multi-pin (field) expansion — bakes/clears the per-field decls.</summary>
    internal void SetExpandFieldsForTest(bool expand) => ApplyExpandFields(expand);

    /// <summary>Test hook: true when the node is currently in multi-pin (expanded) mode.</summary>
    internal bool IsExpandedForTest() => _node.Fields is { Count: > 0 };

    internal IReadOnlyList<string> GetAvailableSharedTypesForTest() => _typeProvider.GetSharedStructTypeFqns();

    internal IReadOnlyList<string> GetFilteredSharedTypesForTest(string filterText)
        => SharedTypePickerLogic.Filter(_typeProvider.GetSharedStructTypeFqns(), filterText);

    internal bool IsCurrentSharedTypeIdUnlistedForTest()
        => !string.IsNullOrEmpty(_node.SharedTypeId)
           && !SharedTypePickerLogic.Contains(_typeProvider.GetSharedStructTypeFqns(), _node.SharedTypeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyVariableId(string variableId)
    {
        if (variableId == _node.VariableId) return;
        RecordChange($"Set Shared Slot '{variableId}'", () => _node.VariableId = variableId);
    }

    private void ApplySharedTypeId(string sharedTypeId)
    {
        if (sharedTypeId == _node.SharedTypeId) return;
        RecordChange($"Set Shared Type '{sharedTypeId}'", () =>
        {
            _node.SharedTypeId = sharedTypeId;
            // Q#14: if the node is in multi-pin (expanded) mode, re-bake the field decls for the new type.
            if (_node.Fields is { Count: > 0 })
                _node.Fields = SharedStructFieldReflector.TryReflect(sharedTypeId);
        });
    }

    /// <summary>
    /// Q#14 multi-pin toggle: when <paramref name="expand"/> is true, reflect the current shared struct's
    /// fields and expose one pin per field; when false, collapse back to the single whole-struct pin. The
    /// designer-facing analog of Unreal's "Split Struct Pin" at node granularity.
    /// </summary>
    private void ApplyExpandFields(bool expand)
    {
        var newFields = expand ? SharedStructFieldReflector.TryReflect(_node.SharedTypeId) : null;
        bool wasExpanded = _node.Fields is { Count: > 0 };
        bool nowExpanded = newFields is { Count: > 0 };
        if (wasExpanded == nowExpanded && !nowExpanded) return;
        RecordChange(expand ? "Expand Struct Pins" : "Collapse Struct Pins",
            () => _node.Fields = newFields);
    }

    /// <summary>
    /// BP-11: the node's undo-relevant state. Snapshotted whole rather than per-field because
    /// changing the shared type also re-bakes <c>Fields</c> — one gesture, two writes, which is why
    /// <c>GraphCommand.SetNodeProperty</c> (one key, one value) cannot carry it. <c>Fields</c> is
    /// captured by reference, sound because every mutation here <em>replaces</em> the list.
    /// </summary>
    private readonly record struct NodeState(string VariableId, string SharedTypeId, List<SharedFieldDecl>? Fields);

    private NodeState Capture() => new(_node.VariableId, _node.SharedTypeId, _node.Fields);

    private void Restore(NodeState s)
    {
        _node.VariableId   = s.VariableId;
        _node.SharedTypeId = s.SharedTypeId;
        _node.Fields       = s.Fields;
    }

    /// <summary>BP-11: runs <paramref name="mutate"/> as an undoable edit on the shared undo stack.</summary>
    private void RecordChange(string label, Action mutate)
    {
        var before = Capture();
        _editService.RecordPropertyEdit(
            _parent, label,
            apply: () => { mutate();        AfterChange(); },
            undo:  () => { Restore(before); AfterChange(); });
    }

    private void AfterChange()
    {
        IsDirty = true;
        // Every shared-node edit (slot-name label, shared type, field expansion) changes the node's
        // projected pins, so signal a STRUCTURAL change: the canvas graph model re-projects itself.
        // Data-driven — this drawer never references the canvas; the composition root wires the refresh
        // (see BlueprintDocumentFactory).
        _editService.NotifyStructureChanged(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Set Shared");
        ImGui.Separator();

        var variableId = _node.VariableId ?? "";
        if (ImGui.InputText("Slot Name", ref variableId, 256))
            ApplyVariableId(variableId);

        DrawSharedTypePicker();
        DrawExpandFieldsToggle();

        ImGui.TextDisabled("(entity-scoped, self only — writes BlueprintSharedState.TrySetShared<T>)");
        if (string.IsNullOrEmpty(_node.VariableId) || string.IsNullOrEmpty(_node.SharedTypeId))
            ImGui.TextColored(EditorColors.Warning, "(both Slot Name and Shared Type FQN are required)");
    }

    /// <summary>See <see cref="GetSharedNodeSession.DrawSharedTypePicker"/> for the rationale.</summary>
    private void DrawSharedTypePicker()
    {
        var current  = _node.SharedTypeId ?? "";
        var unlisted = IsCurrentSharedTypeIdUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Shared Type FQN", comboLabel))
        {
            ImGui.InputTextWithHint("##SetSharedTypeFilter", "Filter...", ref _typeFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var fqn in GetFilteredSharedTypesForTest(_typeFilterText))
            {
                bool selected = fqn == current;
                if (ImGui.Selectable(fqn, selected))
                    ApplySharedTypeId(fqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current value not in the discovered type list — kept as-is: {current})");
    }

    /// <summary>Q#14 multi-pin toggle checkbox (only shown once a shared type is chosen).</summary>
    private void DrawExpandFieldsToggle()
    {
        if (string.IsNullOrEmpty(_node.SharedTypeId)) return;
        bool expanded = _node.Fields is { Count: > 0 };
        if (ImGui.Checkbox("Expand to field pins (multi-pin)", ref expanded))
            ApplyExpandFields(expanded);
        if (_node.Fields is { Count: > 0 } fields)
            ImGui.TextDisabled($"({fields.Count} field pin(s) — set/read individual fields; unset preserved)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

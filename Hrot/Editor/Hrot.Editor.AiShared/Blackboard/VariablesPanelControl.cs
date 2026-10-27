using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Blackboard;

public interface IVariablesSchemaSource
{
    bool IsReadOnly { get; }
    IReadOnlyList<VariableViewModel> Variables { get; }
    string? GetRefactorKey(string variableName);
    void AddVariable(BlackboardVariableEntry entry);
    void RemoveVariable(string name);
    void RemoveVariables(IReadOnlyList<string> names);
    void RenameVariable(string oldName, string newName);
    void MoveVariable(int sourceIndex, int destIndex);
    int CountNodesReferencingVariable(string name);
    
    /// <summary>
    /// ⭐⭐⭐ <b>Batch 98 (<c>98a</c>) — WHERE AN INITIAL-VALUE EDIT LANDS, in the vocabulary all three
    /// hosts already share.</b>
    ///
    /// <para>🔴🔴 <b>The defect.</b> 📐 Measured: <c>VariableEditCommit.CommitInitialValue</c> resolved
    /// its write target through <c>PerspectiveWorkspaceRegistrar.DeclarationOwnerOf</c>, which
    /// type-tests <c>store.ActiveAsset is IBlackboardManagedAsset</c> — ⛔ <b>and
    /// <c>BlueprintAsset</c> is not one.</b> ⇒ in <b>PLANNING</b>, the ordinary authoring state, the
    /// target is the initial value, the owner was <b>always <c>null</c> on Blueprint</b>, and
    /// <b>OK refused on every Blueprint variable, every time.</b></para>
    ///
    /// <para>⭐⭐ <b>Why HERE and not on <c>IBlackboardManagedAsset</c>.</b> 📌 <c>95a</c> and
    /// <c>R-108</c> both keep the two vocabularies apart on purpose: <c>IBlackboardManagedAsset</c> is
    /// the <b>AI blackboard's</b> interface, and a <c>BlueprintAsset</c> speaks
    /// <c>VariableDecl</c>/<c>ParameterDecl</c> with a persisted <c>Guid Id</c>. ⛔ Widening it to
    /// swallow blueprints is explicitly forbidden by this batch's handoff. ⭐ <b>This interface is
    /// already the one thing all three hosts implement</b> — it carries <c>RenameVariable</c>,
    /// <c>RemoveVariable</c> and <c>MoveVariable</c> for exactly the same reason.</para>
    ///
    /// <para>⛔⛔ <b>NO DEFAULT BODY, deliberately.</b> 📌 <c>U-5</c>/<c>BP-230</c>, stated in this very
    /// file: <i>"a default body is the interface volunteering to lie on an implementer's behalf."</i>
    /// ⚠ That is precisely how <c>UpdateVariableRole</c> shipped as <c>{ }</c> and how a blueprint's
    /// Role combo landed in an empty body for two batches. ⭐ Every implementer answers, and a new one
    /// <b>cannot compile</b> without deciding.</para>
    ///
    /// <para>⭐ <c>null</c> clears the authored default — byte-stable, exactly as
    /// <c>IBlackboardManagedAsset.UpdateVariableDefaultValueJson</c> defines it. ⚠ An unknown name is
    /// a <b>no-op</b>, not a throw: the row may have been deleted under an open dialog.</para>
    /// </summary>
    void UpdateVariableDefaultValueJson(string name, string? defaultValueJson);

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — where the PROPERTIES form's OK lands.</b>
    ///
    /// <para>📌 <c>R-108</c>/<c>R-109</c>: <i>"'Properties…' must open the DECLARATION, not the
    /// value"</i>, and it is a <b>CUSTOM</b> form. ⇒ it needs somewhere to put the declaration's
    /// members, and <see cref="UpdateVariableDefaultValueJson"/> reaches exactly one of them.</para>
    ///
    /// <para>⭐ <b>ONE method, not five setters.</b> The properties are committed together by one OK,
    /// so five calls would be five chances to fire the dirty callback five times — ⛔ and a host that
    /// implemented four of them would look finished.</para>
    ///
    /// <para>⚠ <c>Variables.VariablePropertyValues</c> carries <c>null</c> for <i>"this kind does not
    /// have it"</i>, and an implementer <b>must leave those members alone</b> — ⛔ coercing <c>null</c>
    /// to <c>""</c> erases a comment the form never showed.</para>
    ///
    /// <para>⛔⛔ <b><c>Name</c> and <c>Type</c> are NOT part of this, and that is <c>R-109</c>.</b>
    /// A rename is an OPERATION and goes through <c>VariableRenameCommit</c> *(the refactor service —
    /// 📌 <c>M-15</c>)*; a retype is a MIGRATION *(<c>StructureHash</c> moves — <c>R-24</c>)*.</para>
    ///
    /// <para>⛔ <b>NO DEFAULT BODY</b> — 📌 <c>U-5</c>/<c>BP-230</c>, stated above.</para>
    /// </summary>
    void UpdateVariableProperties(string name, Variables.VariablePropertyValues values);

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — reads the declaration's KIND and its non-value members.</b>
    ///
    /// <para>⚠ <b>Why <see cref="Variables"/>' view model is not enough.</b> 📐 <c>VariableViewModel</c>
    /// carries <c>DefaultValueJson</c> and <b>not</b> Tooltip, Category, IsEditable or IsExposedOnSpawn
    /// — and those are exactly the members <c>VariablePropertySchema.For(BlueprintVariable)</c> says the
    /// form must show. ⛔ Widening the view model would put four members on the projection <b>every
    /// table row builds</b>, for a form that opens on one row at a time.</para>
    ///
    /// <para>⭐ <b>The KIND rides along</b> because this source is the only thing that knows which
    /// carrier it read — 📌 <c>95a</c>: the asset is exactly what cannot be type-tested.</para>
    ///
    /// <para>⛔ <b>NO DEFAULT BODY.</b> ⚠ <c>null</c> for an unknown name is correct and expected.</para>
    /// </summary>
    Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name);

    // Aliasing
    IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements { get; }
    void AddAlias(string name, BlackboardAliasBinding binding);
    void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId);
    IReadOnlyDictionary<Guid, int>? GetParallelRegionMap();
}

public sealed class BTreeHsmSchemaSource : IVariablesSchemaSource
{
    private readonly IBlackboardManagedAsset _asset;
    private readonly BlackboardWindowViewModel _vm;
    private readonly bool _isReadOnly;

    public BTreeHsmSchemaSource(IBlackboardManagedAsset asset, BlackboardWindowViewModel vm, bool isReadOnly)
    {
        _asset = asset;
        _vm = vm;
        _isReadOnly = isReadOnly;
    }

    public bool IsReadOnly => _isReadOnly;
    public IReadOnlyList<VariableViewModel> Variables => _vm.Variables;
    public string? GetRefactorKey(string variableName) => null;
    // ⭐ CE-1277 — a read-only table (a code-built graph, a blackboard that failed to load) REFUSES the edit itself, not only its button.
    public void AddVariable(BlackboardVariableEntry entry) { if (!_isReadOnly) _asset.AddVariable(entry); }
    public void RemoveVariable(string name) { if (!_isReadOnly) _asset.RemoveVariable(name); }
    public void RemoveVariables(IReadOnlyList<string> names) { if (!_isReadOnly) _asset.RemoveVariables(names); }
    public void RenameVariable(string oldName, string newName) { if (!_isReadOnly) _asset.RenameVariable(oldName, newName); }
    public void MoveVariable(int sourceIndex, int destIndex) { if (!_isReadOnly) _asset.MoveVariable(sourceIndex, destIndex); }
    public int CountNodesReferencingVariable(string name) => _asset.CountNodesReferencingVariable(name);
    

    /// <summary>
    /// ⭐ 99a — an AI blackboard entry carries only DefaultValue and Comment of the properties set
    /// *(📌 <c>VariablePropertySchema.For(BlackboardEntry)</c>: four properties, not eight)*, and the
    /// asset already owns both writes. ⚠ <c>null</c> members are LEFT ALONE, per the contract.
    /// </summary>
    public void UpdateVariableProperties(
        string name, Hrot.Editor.AiShared.Variables.VariablePropertyValues values)
    {
        if (values is null) return;
        if (values.DefaultValueJson is not null)
            _asset.UpdateVariableDefaultValueJson(name, values.DefaultValueJson);
        if (values.Comment is not null) _asset.UpdateVariableComment(name, values.Comment);
    }

    /// <summary>
    /// ⭐ 99a — the entry IS the carrier here, so this reads it directly.
    /// ⚠ Tooltip/Category/IsEditable/IsExposedOnSpawn stay <c>null</c>: the entry has <b>no member for
    /// any of them</b>, and the schema agrees. ⛔ Inventing them would draw controls with nowhere to save.
    /// </summary>
    public Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name)
    {
        foreach (var v in _asset.BlackboardVariables)
            if (string.Equals(v.Name, name, StringComparison.Ordinal))
                return new Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot(
                    Hrot.Editor.AiShared.Variables.VariableDeclarationKind.BlackboardEntry,
                    new Hrot.Editor.AiShared.Variables.VariablePropertyValues(
                        DefaultValueJson: v.DefaultValueJson ?? "",
                        Comment:          v.Comment ?? ""),
                    TypeId: v.FieldType?.FullName ?? "");
        return null;
    }

    // ⭐ 98a — the asset already owns this exact call (and its dirty marking); this source forwards.
    //   ⛔ Nothing is re-implemented here: BTree/HSM's initial-value write was never the broken half.
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
        => _asset.UpdateVariableDefaultValueJson(name, defaultValueJson);

    public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements => _vm.UnboundRequirements;
    public void AddAlias(string name, BlackboardAliasBinding binding) => _asset.AddAlias(name, binding);
    public void RemoveAlias(string name, Guid reqAssetId, Guid reqElemId) => _asset.RemoveAlias(name, reqAssetId, reqElemId);
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => _asset.GetParallelRegionMap();
}

public sealed record VariablesPanelSection(
    string SectionName,
    string TableId,
    IVariablesSchemaSource Schema,
    int TotalInlineBytes,
    int InlineBudget,
    PackWarning Warning,
    bool AliasingEnabled
);

public sealed class VariablesPanelControl
{
    /// <summary>⭐ <c>CE-1277</c> — whether the table offers "Add variable": not on a read-only one (a code-built graph).</summary>
    internal static bool AddOffered(IVariablesSchemaSource schema) => !schema.IsReadOnly;

    private readonly IRefactorService _refactorService;
    private readonly IEditableAsset _assetBase;
    private readonly IReadOnlyList<VariableTypeChoice> _typeChoices;

    private string? _renameActiveVarName;
    private readonly byte[] _renameBuf = new byte[256];

    private bool _openAddPopup;
    private readonly byte[] _addNameBuf = new byte[256];
    private readonly byte[] _addCommentBuf = new byte[512];
    private int _addTypeIndex;
    private string? _addValidationError;
    private IVariablesSchemaSource? _addPopupSchema;

    private string? _pendingRemoveName;
    private int _pendingRemoveRefCount;
    private IVariablesSchemaSource? _removePopupSchema;

    private bool _openRemoveUnusedPopup;
    private IVariablesSchemaSource? _removeUnusedPopupSchema;

    /// <summary>⭐ <c>CE-1062</c> (§13.10c E5) — the active document's view, for the undo stack (set by
    /// <see cref="Hrot.Editor.AiShared.Windows.BlackboardAuthoringWindow"/> from the canvas the registrar derives). Null ⇒ the edit applies
    /// without an undo entry.</summary>
    public Func<NodeEditor.Core.Commands.UndoStack?>? UndoView { get; set; }

    private readonly Hrot.Presentation.Behavior.ParamsDefaultsModel _paramsDefaults =
        new(Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build());

    /// <summary>⭐ <c>CE-1061</c> U5a + <c>CE-1062</c> — choose the DTO as ONE undo entry: the new DTO with no overrides; the undo restores
    /// the old pair.</summary>
    public void ChooseParamsDto(IParamsDtoAsset asset, string typeId)
    {
        string? oldType = asset.ParamsDtoTypeId, oldDefaults = asset.CurrentParamsDefaults;
        Hrot.Editor.AiShared.Documents.AssetEdits.Run(UndoView?.Invoke(), $"Params DTO: {ParamsDtoChooser.SimpleName(typeId)}",
            () => asset.SetParams(typeId, null), () => asset.SetParams(oldType, oldDefaults));
    }

    /// <summary>⭐ <c>CE-1061</c> U5b + <c>CE-1062</c> — write new default overrides as ONE undo entry.</summary>
    public void CommitParamsDefaults(IParamsDtoAsset asset, string? json, string label)
    {
        string? type = asset.ParamsDtoTypeId, old = asset.CurrentParamsDefaults;
        Hrot.Editor.AiShared.Documents.AssetEdits.Run(UndoView?.Invoke(), label,
            () => asset.SetParams(type, json), () => asset.SetParams(type, old));
    }

    /// <summary>⭐ <c>CE-1061</c> U5b (R-3001, §13.10c) — the asset's params defaults: the DTO's own (its initialisers) with this asset's
    /// overrides, edited per field; an edit writes only that key, Reset removes it.</summary>
    private void DrawParamsDefaults(IParamsDtoAsset asset, string tableId, bool readOnly)
    {
        var dto = string.IsNullOrWhiteSpace(asset.ParamsDtoTypeId) ? null : BlackboardTypeHelper.ResolveClrType(asset.ParamsDtoTypeId!);
        _paramsDefaults.Sync(dto, asset.CurrentParamsDefaults);
        if (_paramsDefaults.Session == null) return;
        if (!ImGui.CollapsingHeader($"Params defaults — {ParamsDtoChooser.SimpleName(asset.ParamsDtoTypeId!)}##pd_{tableId}", ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (Hrot.Presentation.Behavior.ParamsDefaultsForm.Draw($"##pdf_{tableId}", _paramsDefaults, null, readOnly) is { } commit)
            CommitParamsDefaults(asset, commit.Json, commit.Label);
    }

    // ⭐ CE-1061 — the DTO choices, rebuilt only when the registry or the current DTO changes (the panel draws every frame).
    private Hrot.Map.Definitions.Behavior.ParamsDtoRegistry? _choicesRegistry;
    private string? _choicesCurrent;
    private IReadOnlyList<ParamsDtoChoice> _choices = Array.Empty<ParamsDtoChoice>();

    private IReadOnlyList<ParamsDtoChoice> ChoicesFor(string? current)
    {
        var registry = Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Default;
        if (!ReferenceEquals(registry, _choicesRegistry) || !string.Equals(current, _choicesCurrent, StringComparison.Ordinal))
        {
            _choices = ParamsDtoChooser.Choices(registry, current);
            _choicesRegistry = registry;
            _choicesCurrent = current;
        }
        return _choices;
    }

    public VariablesPanelControl(IRefactorService refactorService, IEditableAsset assetBase, IReadOnlyList<VariableTypeChoice> typeChoices)
    {
        _refactorService = refactorService;
        _assetBase = assetBase;
        _typeChoices = typeChoices;
    }

    public void DrawSingle(VariablesPanelSection section,
        Func<string, FieldDecoration?>? rowDecoration = null,
        IReadOnlyDictionary<string, string>? liveValues = null)
    {
        DrawSection(section, rowDecoration, liveValues);
        DrawPopups();
    }

    public void DrawDual(VariablesPanelSection topSection, VariablesPanelSection bottomSection,
        Func<string, FieldDecoration?>? rowDecoration = null)
    {
        if (ImGui.CollapsingHeader($"{topSection.SectionName} ({topSection.TotalInlineBytes} B)", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSection(topSection, rowDecoration);

        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader($"{bottomSection.SectionName} ({bottomSection.TotalInlineBytes} B)", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSection(bottomSection, rowDecoration);

        DrawPopups();
    }

    private void DrawSection(VariablesPanelSection section,
        Func<string, FieldDecoration?>? rowDecoration = null,
        IReadOnlyDictionary<string, string>? liveValues = null)
    {
        var schema = section.Schema;

        // Memory budget header.
        // ⭐ CE-314: ONE line, always. The two-line Inline/Heavy form reported a Blackboard1024 budget
        //   that P4-① deleted — an authoring surface for storage that cannot exist.
        ImGui.TextColored(BudgetColor(section.TotalInlineBytes, section.InlineBudget), $"Memory: {section.TotalInlineBytes} / {section.InlineBudget} B");

        if (section.Warning == PackWarning.InlineMemoryExceeded)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), "Memory budget exceeded!");

        // ⭐ CE-1061 (U5a, R-3001, DESIGN_Orders_Params_Entity_Classes §13.10b) — name the asset's params DTO: hand-written C# or a
        //   .params.json DTO, from the one registry. The reserved Params row (and its field rows) follow the choice.
        if (_assetBase is IParamsDtoAsset dtoAsset)
        {
            var picked = ParamsDtoChooser.Draw(section.TableId, dtoAsset.ParamsDtoTypeId, ChoicesFor(dtoAsset.ParamsDtoTypeId), schema.IsReadOnly);
            if (picked != null) ChooseParamsDto(dtoAsset, picked);
            DrawParamsDefaults(dtoAsset, section.TableId, schema.IsReadOnly);
        }

        ImGui.Separator();

        // ⭐ CE-1277 — a read-only table (a code-built graph) offers no Add: the button is greyed with the reason.
        if (!AddOffered(schema)) ImGui.BeginDisabled();
        bool addClicked = ImGui.Button($"[+] Add variable...##add_{section.TableId}");
        if (!AddOffered(schema))
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Read-only here (a graph built in C# is edited in its builder method). Current values stay visible.");
        }
        if (addClicked && AddOffered(schema))
        {
            _openAddPopup = true;
            _addPopupSchema = schema;
            Array.Clear(_addNameBuf, 0, _addNameBuf.Length);
            Array.Clear(_addCommentBuf, 0, _addCommentBuf.Length);
            _addTypeIndex = 0;
            _addValidationError = null;
        }

        // Only count hand-authored unused vars for the "Remove unused" button.
        if (schema.Variables.Any(v => v.IsUnused && !v.IsAutoManaged) && !schema.IsReadOnly)
        {
            ImGui.SameLine();
            if (ImGui.Button($"[ Remove unused ]##rmvu_{section.TableId}"))
            {
                _openRemoveUnusedPopup = true;
                _removeUnusedPopupSchema = schema;
            }
        }

        // Split into hand-authored and node-owned groups.
        var mainVars     = schema.Variables.Where(v => !v.IsAutoManaged).ToList();
        var nodeOwnedVars = schema.Variables.Where(v => v.IsAutoManaged).ToList();

        if (mainVars.Count == 0)
        {
            ImGui.TextDisabled("No variables declared.");
            // even if empty, show unbound
        }
        else
        {
            DrawTable(section, mainVars, rowDecoration, liveValues);
        }

        // Node-Owned Allocations sub-group. Rendered at normal opacity: these rows are
        // auto-managed (created/removed by the owning node, so Name/Type/Role/delete are
        // read-only) but the Scope cell is now authorable (Slice-1 shared working-state), so
        // the section must be legible and interactive. The "Node-Owned Allocations" header plus
        // the per-row "Auto-allocated by node…" tooltip carry the "these are special" signal
        // that the old blanket 50% dim used to — without making the live Scope combo hard to use.
        if (nodeOwnedVars.Count > 0)
        {
            ImGui.Spacing();
            if (ImGui.CollapsingHeader($"Node-Owned Allocations ({nodeOwnedVars.Count})##no_{section.TableId}",
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawNodeOwnedTable(section, nodeOwnedVars);
            }
        }

        if (section.AliasingEnabled && section.Schema.UnboundRequirements.Count > 0)
        {
            ImGui.Separator();
            if (ImGui.CollapsingHeader("UNBOUND SUB-TREE REQUIREMENTS", ImGuiTreeNodeFlags.DefaultOpen))
            {
                for (int i = 0; i < schema.UnboundRequirements.Count; i++)
                {
                    var req = schema.UnboundRequirements[i];
                    ImGui.PushID(1000 + i);
                    // Use Selectable (which has an item ID) as the drag handle. A bare Text item
                    // has no ID, and BeginDragDropSource then fails an ImGui assertion ("Expression: 0"
                    // — Cannot BeginDragDropSource() for an item with no ID) the moment it is clicked.
                    ImGui.Selectable($"[*] {req.DtoTypeName}  --  Required by: {req.RequiredByPath}");

                    if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                    {
                        unsafe
                        {
                            int src = i;
                            ImGui.SetDragDropPayload("BB_UNBOUND_DRAG", (IntPtr)(&src), sizeof(int));
                        }
                        ImGui.Text($"{req.DtoTypeName}  (from {req.RequiringAssetName})");
                        ImGui.EndDragDropSource();
                    }
                    if (ImGui.BeginPopupContextItem("##unbound_ctx"))
                    {
                        if (ImGui.MenuItem("Promote to new variable")) { } // deferred 1.5d
                        ImGui.EndPopup();
                    }
                    ImGui.PopID();
                }
            }
        }
    }

    // ── B-4: headless-testable alias drop predicate ───────────────────────────

    /// <summary>
    /// Returns true when a BB_UNBOUND_DRAG alias drop is accepted onto <paramref name="targetRow"/>.
    /// Auto-managed variables are never valid alias targets (B-4 §3.7).
    /// The DTO type must also match the target's field type.
    /// </summary>
    public static bool IsAliasDropAccepted(VariableViewModel targetRow, Type draggedDtoType)
    {
        if (targetRow.IsAutoManaged) return false;
        return draggedDtoType == targetRow.FieldType;
    }

    private void DrawTable(VariablesPanelSection section,
        List<VariableViewModel> rows,
        Func<string, FieldDecoration?>? rowDecoration = null,
        IReadOnlyDictionary<string, string>? liveValues = null)
    {
        var schema = section.Schema;
        // ⭐ CE-2135 (B3a, R-3000) — no Role column: a variable is a variable; the behaviour's JSON params are the reserved
        //   read-only "Params" row (its named DTO). ⛔ HISTORY: an Input/State combo per row (U-5/BP-230).
        if (ImGui.BeginTable(section.TableId, 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Name",  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Type",  ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableSetupColumn("Bytes", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##rmv", ImGuiTableColumnFlags.WidthFixed, 24f);
            ImGui.TableHeadersRow();

            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                ImGui.TableNextRow();
                FieldDecoration? dec = rowDecoration?.Invoke(row.Name);
                if (dec != null)
                {
                    // Apply row background tint for decorated variables.
                    uint rowColor = 0;
                    if (dec.IsAdded)        rowColor = ImGui.GetColorU32(new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 0.15f));
                    else if (dec.IsRemoved) rowColor = ImGui.GetColorU32(new System.Numerics.Vector4(0.9f, 0.2f, 0.2f, 0.15f));
                    else if (dec.IsRetyped) rowColor = ImGui.GetColorU32(new System.Numerics.Vector4(0.3f, 0.5f, 1.0f, 0.15f));
                    else if (dec.IsRenamed) rowColor = ImGui.GetColorU32(new System.Numerics.Vector4(1.0f, 0.85f, 0.3f, 0.15f));
                    if (rowColor != 0)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, rowColor);
                }
                ImGui.TableNextColumn();
                ImGui.PushID(rowIdx);

                bool isRenaming = _renameActiveVarName == row.Name;

                if (row.IsUnused)
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.6f);

                if (isRenaming)
                {
                    ImGui.SetNextItemWidth(-1f);
                    ImGui.SetKeyboardFocusHere();
                    if (ImGui.InputText("##rename_bb", _renameBuf, (uint)_renameBuf.Length, ImGuiInputTextFlags.EnterReturnsTrue) || ImGui.IsKeyPressed(ImGuiKey.Tab))
                    {
                        CommitRename(schema, row.Name);
                    }
                    else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                    {
                        _renameActiveVarName = null;
                    }
                }
                else
                {
                    string label = row.IsUnused ? $"o {row.Name}" : row.Name;
                    ImGui.Selectable(label, false, ImGuiSelectableFlags.None, new System.Numerics.Vector2(0f, 0f));

                    if (ImGui.IsItemHovered())
                    {
                        if (row.IsUnused) ImGui.SetTooltip("Not referenced by any node -- consider removing.");
                        if (row.IsReadOnly) ImGui.SetTooltip("The behaviour's params: its named DTO (ParamsDtoTypeId). Reserved — not renamed or removed here.");
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !schema.IsReadOnly && !row.IsReadOnly)
                        {
                            _renameActiveVarName = row.Name;
                            Array.Clear(_renameBuf, 0, _renameBuf.Length);
                            System.Text.Encoding.UTF8.GetBytes(row.Name, 0, row.Name.Length, _renameBuf, 0);
                        }
                    }
                }

                // NOTE: the comment TextDisabled is intentionally placed AFTER the drag-drop
                // source and target blocks. Dear ImGui's BeginDragDropSource/BeginDragDropTarget
                // must attach to the Selectable (which has an item ID). If TextDisabled were
                // emitted first it would become the last submitted item, and BeginDragDropSource
                // would fire an IM_ASSERT("Cannot BeginDragDropSource() for an item with no ID").
                if (!isRenaming && !schema.IsReadOnly && ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                {
                    unsafe { int src = rowIdx; ImGui.SetDragDropPayload($"DRAG_{section.TableId}", (IntPtr)(&src), sizeof(int)); }
                    ImGui.Text(row.Name);
                    ImGui.EndDragDropSource();
                }

                if (!schema.IsReadOnly && ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var reorderPayload = ImGui.AcceptDragDropPayload($"DRAG_{section.TableId}");
                        if (reorderPayload.NativePtr != null)
                        {
                            int srcIdx = *(int*)reorderPayload.Data;
                            schema.MoveVariable(srcIdx, rowIdx);
                        }

                        if (section.AliasingEnabled)
                        {
                            var aliasPayload = ImGui.AcceptDragDropPayload("BB_UNBOUND_DRAG");
                            if (aliasPayload.NativePtr != null)
                            {
                                int srcIdx = *(int*)aliasPayload.Data;
                                if (srcIdx >= 0 && srcIdx < schema.UnboundRequirements.Count)
                                {
                                    var req = schema.UnboundRequirements[srcIdx];
                                    // B-4 §3.7: auto-managed vars are excluded from alias drop-targets.
                                    if (IsAliasDropAccepted(row, req.DtoType))
                                    {
                                        var newBinding = new BlackboardAliasBinding(req.RequiringAssetId, req.RequiringElementId, req.RequiringAssetName, req.RequiredByPath, req.DtoType);
                                        var map = schema.GetParallelRegionMap();
                                        if (_assetBase is IBlackboardManagedAsset bbma)
                                        {
                                            if (!BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(bbma, row.Name, newBinding, map))
                                                schema.AddAlias(row.Name, newBinding);
                                        }
                                        else
                                        {
                                            schema.AddAlias(row.Name, newBinding);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                // Emit the inline comment AFTER the drag-drop blocks so the Selectable
                // remains the last ID-bearing item when drag operations are initiated.
                if (!isRenaming && row.Comment is not null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"  // {row.Comment}");
                }

                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.TypeName);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.ByteSize.ToString());
                ImGui.TableNextColumn();
                {
                    // BATCH-11: live value column ("—" when no provider or no matching entity).
                    if (liveValues != null && liveValues.TryGetValue(row.Name, out var lv))
                        ImGui.TextUnformatted(lv);
                    else
                        ImGui.TextDisabled("—");
                }
                // ⛔ CE-3193 — the Scope column (and Role before it, CE-2135) is gone: a variable has no kind to show.
                ImGui.TableNextColumn();
                if (!schema.IsReadOnly && !row.IsReadOnly && ImGui.SmallButton("[x]"))
                {
                    _pendingRemoveName = row.Name;
                    _pendingRemoveRefCount = schema.CountNodesReferencingVariable(row.Name);
                    _removePopupSchema = schema;
                }

                if (section.AliasingEnabled && row.AliasedBy != null && row.AliasedBy.Count > 0)
                {
                    ImGui.TableSetColumnIndex(0);
                    for (int ai = 0; ai < row.AliasedBy.Count; ai++)
                    {
                        var (assetName, assetId, elementId) = row.AliasedBy[ai];
                        ImGui.PushID(10000 + rowIdx * 100 + ai);
                        ImGui.Indent(12f);
                        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.85f, 1f, 1f), $"<aliased by {assetName}>");
                        ImGui.Unindent(12f);
                        if (ImGui.BeginPopupContextItem("##alias_ctx"))
                        {
                            if (ImGui.MenuItem($"Remove alias: {assetName}"))
                            {
                                schema.RemoveAlias(row.Name, assetId, elementId);
                            }
                            ImGui.EndPopup();
                        }
                        ImGui.PopID();
                    }
                }

                if (row.IsUnused) ImGui.PopStyleVar();
                ImGui.PopID();

                // ⭐ CE-1061 (U3, DESIGN_Orders_Params_Entity_Classes §13.10b) — the params DTO's fields under the reserved Params row,
                //   read-only: what a Params.<field> binding reaches. Value = the asset's override, else the DTO's own default.
                if (row.ParamsFields is { Count: > 0 } fields)
                    for (int fi = 0; fi < fields.Count; fi++)
                    {
                        var f = fields[fi];
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Indent(14f);
                        ImGui.TextDisabled("." + f.Name);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"A field of the params DTO — bind it as Params.{f.Name}. Defined by the DTO, not here.");
                        ImGui.Unindent(14f);
                        ImGui.TableNextColumn(); ImGui.TextDisabled(f.TypeName);
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                        // ⭐ §13.10c E7 — the EFFECTIVE default; "*" where this asset overrides the DTO's own (⚠ not "●": the editor
                        //   font has no glyph for it).
                        if (f.ValueJson is { } v)
                        {
                            if (f.IsOverridden) ImGui.TextUnformatted("* " + v);
                            else ImGui.TextDisabled(v);
                        }
                        else ImGui.TextDisabled("—");
                        ImGui.TableNextColumn();
                    }
            }
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Renders the "Node-Owned Allocations" table for auto-managed variables (B-4 §3.6).
    /// This table is always dimmed (caller wraps in PushStyleVar Alpha). Name, Type and Role
    /// stay read-only here — auto-managed vars are removed by the command sink when the owning
    /// node is deleted. ⛔ CE-3193 — no Scope cell: working state is derived from the bindings, nothing to author.
    /// </summary>
    private void DrawNodeOwnedTable(VariablesPanelSection section, List<VariableViewModel> rows)
    {
        var schema = section.Schema;
        string tableId = $"##no_tbl_{section.TableId}";
        if (ImGui.BeginTable(tableId, 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Name",  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Type",  ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableSetupColumn("Bytes", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableHeadersRow();

            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushID(50000 + rowIdx);
                ImGui.TextUnformatted(row.Name);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Auto-allocated by node. Removed when the owning node is deleted.");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.TypeName);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.ByteSize.ToString());


                ImGui.PopID();
            }
            ImGui.EndTable();
        }
    }

    private void DrawPopups()
    {
        if (_openAddPopup) { ImGui.OpenPopup("AddVariable##bb"); _openAddPopup = false; }
        if (_openRemoveUnusedPopup) { ImGui.OpenPopup("confirm_remove_unused"); _openRemoveUnusedPopup = false; }
        if (_pendingRemoveName != null && _removePopupSchema != null) { ImGui.OpenPopup("RemoveVariable##bb"); }

        bool addOpen = true;
        if (ImGui.BeginPopupModal("AddVariable##bb", ref addOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Name:"); ImGui.SameLine(); ImGui.InputText("##add_name", _addNameBuf, (uint)_addNameBuf.Length);
            ImGui.Text("Type:"); ImGui.SameLine();
            string comboStr = string.Join('\0', _typeChoices.Select(c => c.Display)) + "\0\0";
            ImGui.Combo("##add_type", ref _addTypeIndex, comboStr);
            ImGui.Text("Comment:"); ImGui.SameLine(); ImGui.InputText("##add_comment", _addCommentBuf, (uint)_addCommentBuf.Length);

            if (_addValidationError != null) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), _addValidationError);

            if (ImGui.Button("Add") && _addPopupSchema != null)
            {
                 string name = Fdp.Presentation.Utils.ImGuiBufferText.DecodeTrimmed(_addNameBuf);
                 string comment = Fdp.Presentation.Utils.ImGuiBufferText.DecodeTrimmed(_addCommentBuf);
                 var existing = _addPopupSchema.Variables.Select(x => new BlackboardVariableEntry(x.Name, x.FieldType, x.Comment)).ToList();
                 _addValidationError = BlackboardNameValidator.Validate(name, existing);
                 if (_addValidationError == null)
                 {
                     // Collision-safe resolution: index into the SAME ordered choice list that
                     // built the combo, never a reverse name lookup -- two structs from different
                     // namespaces can share a short display name (e.g. two distinct "Foo" DTOs).
                     Type fieldType = (_addTypeIndex >= 0 && _addTypeIndex < _typeChoices.Count)
                         ? _typeChoices[_addTypeIndex].Type
                         : typeof(int);
                     _addPopupSchema.AddVariable(new BlackboardVariableEntry(name, fieldType, string.IsNullOrEmpty(comment) ? null : comment));
                     ImGui.CloseCurrentPopup();
                     _addPopupSchema = null;
                 }
            }
            ImGui.SameLine();
            // ⭐ CE-1014 — Esc means Cancel.
            if (ImGui.Button("Cancel") || Hrot.Editor.AiShared.Shell.ModalKeys.EscapePressed()) { ImGui.CloseCurrentPopup(); _addPopupSchema = null; }
            ImGui.EndPopup();
        }

        bool rmOpen = true;
        if (_pendingRemoveName != null && ImGui.BeginPopupModal("RemoveVariable##bb", ref rmOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Remove variable '{_pendingRemoveName}'?");
            if (_pendingRemoveRefCount > 0)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.7f, 0.2f, 1f), $"Warning: {_pendingRemoveRefCount} node(s) reference this variable.");
            if (ImGui.Button("Remove") && _removePopupSchema != null)
            {
                _removePopupSchema.RemoveVariable(_pendingRemoveName);
                _pendingRemoveName = null;
                _removePopupSchema = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _pendingRemoveName = null;
                _removePopupSchema = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        bool rmuOpen = true;
        if (ImGui.BeginPopupModal("confirm_remove_unused", ref rmuOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (_removeUnusedPopupSchema != null)
            {
                // B-4: exclude auto-managed vars from the "Remove unused" bulk operation.
                var unused = _removeUnusedPopupSchema.Variables.Where(v => v.IsUnused && !v.IsAutoManaged).ToList();
                int totalBytes = unused.Sum(v => v.ByteSize);
                ImGui.Text($"Remove {unused.Count} unused variables?");
                ImGui.Text($"This will free {totalBytes} bytes from the blackboard.");
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.7f, 0.2f, 1f), "This cannot be undone.");

                if (ImGui.Button("Remove"))
                {
                    _removeUnusedPopupSchema.RemoveVariables(unused.Select(v => v.Name).ToList());
                    ImGui.CloseCurrentPopup();
                    _removeUnusedPopupSchema = null;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) { ImGui.CloseCurrentPopup(); _removeUnusedPopupSchema = null; }
            }
            ImGui.EndPopup();
        }
    }

    private static System.Numerics.Vector4 BudgetColor(int used, int budget)
    {
        if (budget <= 0 || used < budget * 8 / 10) return new System.Numerics.Vector4(1f, 1f, 1f, 1f);
        if (used < budget) return new System.Numerics.Vector4(1f, 0.75f, 0f, 1f);
        return new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f);
    }

    private void CommitRename(IVariablesSchemaSource schema, string oldName)
    {
        string newName = Fdp.Presentation.Utils.ImGuiBufferText.DecodeTrimmed(_renameBuf);
        _renameActiveVarName = null;
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;
        // ⭐ CE-3182 — a rename obeys the Add rule (an identifier, not a keyword, unique): a '.' would make every binding path
        //   (Var.Field) ambiguous, and a non-identifier cannot be the block struct's field name anyway.
        if (BlackboardNameValidator.Validate(newName) != null || schema.Variables.Any(v => v.Name == newName)) return;

        var fromKey = schema.GetRefactorKey(oldName) ?? $"{_assetBase.AssetId:D}::{oldName}";
        var toKey   = schema.GetRefactorKey(newName) ?? $"{_assetBase.AssetId:D}::{newName}";
        var preview = _refactorService.PreviewRename(fromKey, toKey, new RefactorOptions());
        if (!preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
            _refactorService.ApplyRename(preview);

        schema.RenameVariable(oldName, newName);
    }
}

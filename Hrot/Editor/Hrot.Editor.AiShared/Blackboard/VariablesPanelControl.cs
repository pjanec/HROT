using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
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
    public void AddVariable(BlackboardVariableEntry entry) => _asset.AddVariable(entry);
    public void RemoveVariable(string name) => _asset.RemoveVariable(name);
    public void RemoveVariables(IReadOnlyList<string> names) => _asset.RemoveVariables(names);
    public void RenameVariable(string oldName, string newName) => _asset.RenameVariable(oldName, newName);
    public void MoveVariable(int sourceIndex, int destIndex) => _asset.MoveVariable(sourceIndex, destIndex);
    public int CountNodesReferencingVariable(string name) => _asset.CountNodesReferencingVariable(name);
    
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
    int TotalHeavyBytes,
    int HeavyBudget,
    bool RequiresHeavyComponent,
    PackWarning Warning,
    bool AliasingEnabled
);

public sealed class VariablesPanelControl
{
    private readonly IRefactorService _refactorService;
    private readonly IEditableAsset _assetBase;
    private readonly IReadOnlyList<string> _knownTypeNames;

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

    public VariablesPanelControl(IRefactorService refactorService, IEditableAsset assetBase, IReadOnlyList<string> knownTypeNames)
    {
        _refactorService = refactorService;
        _assetBase = assetBase;
        _knownTypeNames = knownTypeNames;
    }

    public void DrawSingle(VariablesPanelSection section,
        Func<string, FieldDecoration?>? rowDecoration = null)
    {
        DrawSection(section, rowDecoration);
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
        Func<string, FieldDecoration?>? rowDecoration = null)
    {
        var schema = section.Schema;

        // Memory budget header
        if (section.RequiresHeavyComponent)
        {
            ImGui.TextColored(BudgetColor(section.TotalInlineBytes, section.InlineBudget), $"Inline: {section.TotalInlineBytes} / {section.InlineBudget} B");
            ImGui.SameLine();
            ImGui.TextColored(BudgetColor(section.TotalHeavyBytes, section.HeavyBudget), $"  Heavy: {section.TotalHeavyBytes} / {section.HeavyBudget} B");
        }
        else
        {
            ImGui.TextColored(BudgetColor(section.TotalInlineBytes, section.InlineBudget), $"Memory: {section.TotalInlineBytes} / {section.InlineBudget} B");
        }

        if (section.Warning == PackWarning.InlineMemoryExceeded)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), "Inline memory exceeded!");
        else if (section.Warning == PackWarning.HeavyMemoryExceeded)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), "Heavy memory exceeded!");

        ImGui.Separator();

        if (ImGui.Button($"[+] Add variable...##add_{section.TableId}") && !schema.IsReadOnly)
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
            DrawTable(section, mainVars, rowDecoration);
        }

        // Node-Owned Allocations sub-group (dimmed, read-only).
        if (nodeOwnedVars.Count > 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            if (ImGui.CollapsingHeader($"Node-Owned Allocations ({nodeOwnedVars.Count})##no_{section.TableId}",
                ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawNodeOwnedTable(section, nodeOwnedVars);
            }
            ImGui.PopStyleVar();
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
        Func<string, FieldDecoration?>? rowDecoration = null)
    {
        var schema = section.Schema;
        if (ImGui.BeginTable(section.TableId, 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 90f);
            ImGui.TableSetupColumn("Bytes", ImGuiTableColumnFlags.WidthFixed, 50f);
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
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !schema.IsReadOnly)
                        {
                            _renameActiveVarName = row.Name;
                            Array.Clear(_renameBuf, 0, _renameBuf.Length);
                            System.Text.Encoding.UTF8.GetBytes(row.Name, 0, row.Name.Length, _renameBuf, 0);
                        }
                    }

                    if (row.Comment is not null)
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled($"  // {row.Comment}");
                    }
                }

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

                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.TypeName);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(row.ByteSize.ToString());
                ImGui.TableNextColumn();
                if (!schema.IsReadOnly && ImGui.SmallButton("[x]"))
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
            }
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// Renders the read-only "Node-Owned Allocations" table for auto-managed variables (B-4 §3.6).
    /// This table is always dimmed (caller wraps in PushStyleVar Alpha) and has no
    /// edit controls — auto-managed vars are removed by the command sink when the owning node is deleted.
    /// </summary>
    private void DrawNodeOwnedTable(VariablesPanelSection section, List<VariableViewModel> rows)
    {
        string tableId = $"##no_tbl_{section.TableId}";
        if (ImGui.BeginTable(tableId, 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
            string comboStr = string.Join('\0', _knownTypeNames) + "\0\0";
            ImGui.Combo("##add_type", ref _addTypeIndex, comboStr);
            ImGui.Text("Comment:"); ImGui.SameLine(); ImGui.InputText("##add_comment", _addCommentBuf, (uint)_addCommentBuf.Length);

            if (_addValidationError != null) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f), _addValidationError);

            if (ImGui.Button("Add") && _addPopupSchema != null)
            {
                 string name = System.Text.Encoding.UTF8.GetString(_addNameBuf).TrimEnd('\0').Trim();
                 string comment = System.Text.Encoding.UTF8.GetString(_addCommentBuf).TrimEnd('\0').Trim();
                 var existing = _addPopupSchema.Variables.Select(x => new BlackboardVariableEntry(x.Name, x.FieldType, x.Comment)).ToList();
                 _addValidationError = BlackboardNameValidator.Validate(name, existing);
                 if (_addValidationError == null)
                 {
                     string typeName = (_addTypeIndex >= 0 && _addTypeIndex < _knownTypeNames.Count) ? _knownTypeNames[_addTypeIndex] : "int";
                     Type? fieldType = BlackboardTypeHelper.GetPrimitiveType(typeName) ?? typeof(int);
                     _addPopupSchema.AddVariable(new BlackboardVariableEntry(name, fieldType, string.IsNullOrEmpty(comment) ? null : comment));
                     ImGui.CloseCurrentPopup();
                     _addPopupSchema = null;
                 }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) { ImGui.CloseCurrentPopup(); _addPopupSchema = null; }
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
        string newName = System.Text.Encoding.UTF8.GetString(_renameBuf).TrimEnd('\0').Trim();
        _renameActiveVarName = null;
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;

        var fromKey = schema.GetRefactorKey(oldName) ?? $"{_assetBase.AssetId:D}::{oldName}";
        var toKey   = schema.GetRefactorKey(newName) ?? $"{_assetBase.AssetId:D}::{newName}";
        var preview = _refactorService.PreviewRename(fromKey, toKey, new RefactorOptions());
        if (!preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
            _refactorService.ApplyRename(preview);

        schema.RenameVariable(oldName, newName);
    }
}

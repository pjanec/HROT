using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Presentation.Editing;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐ CE-3401 (<c>docs/DESIGN_Struct_Editor.md</c> §12a) — the blackboard's declarations as ONE <see cref="IFieldDeclarations"/> (<see cref="SchemaSourceDeclarations"/>) that ALSO answers the <i>Live</i> column
/// (<see cref="IFieldLiveValues"/>) and routes a rename through the refactor service first (the references in other assets move with it — what the old table's rename did before it called the source).
/// ⛔ The node-owned rows are left out (<c>hideAutoManaged</c>): the host lists them around the table.
/// </summary>
public sealed class BlackboardDeclarations : SchemaSourceDeclarations, IFieldLiveValues
{
    private readonly BTreeHsmSchemaSource _source;
    private readonly IRefactorService _refactor;
    private readonly IEditableAsset _asset;
    private IReadOnlyDictionary<string, string>? _values;
    private string?[] _last = Array.Empty<string?>();
    private int _lastCount = -1;
    private long _epoch;

    public BlackboardDeclarations(BTreeHsmSchemaSource source, IRefactorService refactor, IEditableAsset asset, IReadOnlyList<VariableTypeChoice> types)
        : base(source, id => Resolve(types, id), hideAutoManaged: true)
    {
        _source = source;
        _refactor = refactor;
        _asset = asset;
    }

    private static Type? Resolve(IReadOnlyList<VariableTypeChoice> types, string id)
    {
        for (int i = 0; i < types.Count; i++) if (types[i].Display == id) return types[i].Type;
        return null;
    }

    public long Epoch => _epoch;

    public bool TryLiveText(string name, out string text)
    {
        if (_values != null && _values.TryGetValue(name, out var t)) { text = t; return true; }
        text = "";
        return false;
    }

    /// <summary>Hands in this frame's live values; <see cref="Epoch"/> moves only when one row's text differs from the last frame's. ⭐ Index loops, no allocation unless something changed.</summary>
    public void SetLive(IReadOnlyDictionary<string, string>? values)
    {
        _values = values;
        var vars = _source.Variables;
        bool changed = _lastCount != vars.Count;
        if (_last.Length < vars.Count) _last = new string?[Math.Max(vars.Count, _last.Length * 2)];
        for (int i = 0; i < vars.Count; i++)
        {
            string? now = null;
            if (values != null && values.TryGetValue(vars[i].Name, out var t)) now = t;
            if (!string.Equals(_last[i], now, StringComparison.Ordinal)) { _last[i] = now; changed = true; }
        }
        _lastCount = vars.Count;
        if (changed) _epoch++;
    }

    public override bool Rename(string name, string newName)
    {
        var names = new List<string>();
        foreach (var v in _source.Variables) names.Add(v.Name);
        // ⭐ CE-3182 — a refused name (not an identifier, a keyword, taken) never reaches the refactor service; the base says why.
        if (!string.IsNullOrEmpty(newName) && newName != name && BlackboardNameValidator.ValidateRename(newName, names) == null)
        {
            var fromKey = _source.GetRefactorKey(name) ?? $"{_asset.AssetId:D}::{name}";
            var toKey = _source.GetRefactorKey(newName) ?? $"{_asset.AssetId:D}::{newName}";
            var preview = _refactor.PreviewRename(fromKey, toKey, new RefactorOptions());
            if (!System.Linq.Enumerable.Any(preview.Issues, i => i.Severity == RefactorIssueSeverity.Error)) _refactor.ApplyRename(preview);
        }
        return base.Rename(name, newName);
    }
}

/// <summary>
/// ⭐⭐ CE-3401 (<c>docs/DESIGN_Struct_Editor.md</c> §12a) — the BTree / HSM blackboard's Variables panel: the ONE Define table (<see cref="FieldTable"/>) over <see cref="BlackboardDeclarations"/>, and what is the blackboard's own drawn
/// around it — the params-DTO chooser and defaults, <i>Remove unused</i>, the node-owned allocations group, the unbound sub-tree requirements (the drag sources of an alias), and, in the table's one trailing cell
/// (<see cref="FieldTableSettings.RowTail"/>), the alias drop target + the unused / aliased mark. ⛔ No byte column, no <i>(N B)</i> totals, no memory line (D11); the Live column is the table's.
/// One instance per asset (it keeps the table, the rename box, the selection). Replaces <c>VariablesPanelControl</c>'s table drawing.
/// </summary>
public sealed class VariablesPanelHost
{
    private const string TableId = "##bb_vars";
    private static readonly string OwnedId = "no_" + TableId, OwnedTableId = "##no_tbl_" + TableId;

    private readonly IEditableAsset _asset;
    private readonly BTreeHsmSchemaSource _source;
    private readonly BlackboardDeclarations _decls;
    private readonly FieldTable _table = new();
    private readonly FieldTableSettings _settings;

    // per-frame inputs
    private bool _aliasing;
    private bool _readOnly;
    private Func<string, FieldDecoration?>? _decoration;
    private IReadOnlyList<UnboundRequirementViewModel> _unbound = Array.Empty<UnboundRequirementViewModel>();

    // what the rows mean, rebuilt when the source's revision moves
    private int _builtRevision = -1;
    private VariableViewModel[] _main = Array.Empty<VariableViewModel>();
    private int _mainCount;
    private VariableViewModel[] _owned = Array.Empty<VariableViewModel>();
    private int _ownedCount;
    private string?[] _tailLabel = Array.Empty<string?>();
    private int _unusedCount;
    private string _ownedHeader = "";
    private int _ownedHeaderFor = -1;

    // Remove unused
    private bool _openRemoveUnused;

    // the unbound requirements' drag-handle texts (built when a requirement differs from the last frame's)
    private string[] _unboundLabel = Array.Empty<string>();
    private string[] _unboundFor = Array.Empty<string>();
    private int _unboundCount = -1;

    /// <summary>⭐ <c>CE-1062</c> (§13.10c E5) — the active document's view, for the undo stack (set by
    /// <see cref="Hrot.Editor.AiShared.Windows.BlackboardAuthoringWindow"/> from the canvas the registrar derives). Null ⇒ the edit applies
    /// without an undo entry.</summary>
    public Func<NodeEditor.Core.Commands.UndoStack?>? UndoView { get; set; }

    // frame-rail probes
    /// <summary>The Define table (the rails read its probes).</summary>
    internal FieldTable Table => _table;
    internal BTreeHsmSchemaSource Source => _source;
    internal BlackboardDeclarations Declarations => _decls;
    internal FieldTableSettings Settings => _settings;
    /// <summary>The Remove-unused button was drawn last frame.</summary>
    internal bool RemoveUnusedDrawn { get; private set; }
    internal Vector2 RemoveUnusedMin { get; private set; }
    internal Vector2 RemoveUnusedMax { get; private set; }
    /// <summary>The node-owned allocations rows listed last frame.</summary>
    internal int OwnedListed { get; private set; }
    /// <summary>The unbound requirement handles drawn last frame.</summary>
    internal int UnboundDrawn { get; private set; }
    private Vector2[] _handleMin = new Vector2[4], _handleMax = new Vector2[4];
    /// <summary>Where the drag handle of an unbound requirement was drawn (a rail drags from there onto a row's tail cell).</summary>
    internal (Vector2 Min, Vector2 Max) UnboundHandle(int i) => i >= 0 && i < _handleMin.Length ? (_handleMin[i], _handleMax[i]) : default;
    /// <summary>Why the last rename was refused (the line under the row while its box is open); null otherwise.</summary>
    public string? RenameRefusal => _table.RenamingName != null && _table.Error is { } e ? e.Text : null;

    private readonly Hrot.Presentation.Behavior.ParamsDefaultsModel _paramsDefaults =
        new(Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build());

    /// <param name="refactorService">The refactor service a rename goes through.</param>
    /// <param name="asset">The asset whose blackboard this is.</param>
    /// <param name="typeChoices">What the type picker offers (the same list the old Add popup took).</param>
    public VariablesPanelHost(IRefactorService refactorService, IEditableAsset asset, IReadOnlyList<VariableTypeChoice> typeChoices)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _source = new BTreeHsmSchemaSource(asset as IBlackboardManagedAsset ?? throw new ArgumentException("A blackboard host needs an IBlackboardManagedAsset.", nameof(asset)),
            new BlackboardWindowViewModel(true, true, 0, 0, PackWarning.None, Array.Empty<VariableViewModel>(), typeChoices, Array.Empty<UnboundRequirementViewModel>()), false);
        _decls = new BlackboardDeclarations(_source, refactorService, asset, typeChoices);
        var choices = new List<TypeChoice>(typeChoices.Count);
        for (int i = 0; i < typeChoices.Count; i++) choices.Add(new TypeChoice(typeChoices[i].Display, typeChoices[i].Type, FieldTableSettings.GroupOf(typeChoices[i].Type)));
        var feat = _source.Features;
        _settings = new FieldTableSettings
        {
            AddLabel = "Add variable", NewNameStem = "variable", ValueHeader = "Default", UsedBy = feat.UsedByColumn, ShowLive = feat.LiveValue,
            AllowStructs = true, AllowLists = false, ListsOffReason = "a blackboard holds values and DTO structures, not lists",
            TypeChoices = choices, RowTail = DrawTail, RowTailWidth = 104f,
        };
    }

    /// <summary>⭐ CE-1277 — whether the table offers Add: not on a read-only one (a code-built graph).</summary>
    internal static bool AddOffered(IVariablesSchemaSource schema) => !schema.IsReadOnly;

    // ── the params DTO (kept from the old panel: the chooser and the defaults form belong to the asset, not to the table) ────────────────────

    /// <summary>⭐ <c>CE-1061</c> U5a + <c>CE-1062</c> — choose the DTO as ONE undo entry: the new DTO with no overrides; the undo restores the old pair.</summary>
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

    private static readonly string PdId = "pd_" + TableId, PdForm = "##pdf_" + TableId;
    private string? _pdFor, _pdHeader;
    private Type? _pdDto;

    private void DrawParamsDefaults(IParamsDtoAsset asset, bool readOnly)
    {
        // ⭐ R-2084 — the DTO's type and the header text are resolved when the DTO id changes, not every frame
        string? tid = asset.ParamsDtoTypeId;
        if (_pdDto == null || !string.Equals(tid, _pdFor, StringComparison.Ordinal))
        {
            _pdFor = tid;
            _pdDto = string.IsNullOrWhiteSpace(tid) ? null : BlackboardTypeHelper.ResolveClrType(tid!);
            _pdHeader = string.IsNullOrWhiteSpace(tid) ? "" : $"Params defaults — {ParamsDtoChooser.SimpleName(tid!)}";
        }
        _paramsDefaults.Sync(_pdDto, asset.CurrentParamsDefaults);
        if (_paramsDefaults.Session == null) return;
        if (!SectionHeader.Draw(PdId, _pdHeader!, "section/params", ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (Hrot.Presentation.Behavior.ParamsDefaultsForm.Draw(PdForm, _paramsDefaults, null, readOnly) is { } commit)
            CommitParamsDefaults(asset, commit.Json, commit.Label);
    }

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

    // ── the frame ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Draws the panel at the cursor.</summary>
    /// <param name="vm">This frame's view model (a new one each frame is fine — the table rebuilds only when a row really differs).</param>
    /// <param name="isReadOnly">A read-only blackboard (a failed load, a code-built graph): the table refuses edits, Add is greyed.</param>
    /// <param name="aliasingEnabled">Offer the alias drop target and the unbound requirements.</param>
    /// <param name="rowDecoration">The comparison session's per-row tint, or null.</param>
    /// <param name="liveValues">The selected entity's live blackboard values by name, or null (the Live column shows "—").</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments — the steady frame must not depend on tiering
    public void Draw(BlackboardWindowViewModel vm, bool isReadOnly, bool aliasingEnabled = true,
        Func<string, FieldDecoration?>? rowDecoration = null, IReadOnlyDictionary<string, string>? liveValues = null)
    {
        _readOnly = isReadOnly;
        _aliasing = aliasingEnabled;
        _decoration = rowDecoration;
        _unbound = vm.UnboundRequirements;
        _source.Update(vm, isReadOnly);
        if (_source.Revision != _builtRevision) Rebuild();
        _decls.SetLive(liveValues);

        // ⭐ CE-1061 (U5a, R-3001, DESIGN_Orders_Params_Entity_Classes §13.10b) — name the asset's params DTO: hand-written C# or a
        //   .params.json DTO, from the one registry. The reserved Params row (and its field rows) follow the choice.
        if (_asset is IParamsDtoAsset dtoAsset)
        {
            var picked = ParamsDtoChooser.Draw(TableId, dtoAsset.ParamsDtoTypeId, ChoicesFor(dtoAsset.ParamsDtoTypeId), isReadOnly);
            if (picked != null) ChooseParamsDto(dtoAsset, picked);
            DrawParamsDefaults(dtoAsset, isReadOnly);
        }

        DrawRemoveUnused();
        ImGui.Separator();
        _table.Draw(_decls, _settings, null);
        DrawNodeOwned();
        DrawUnbound();
        DrawPopups();
    }

    private void Rebuild()
    {
        _builtRevision = _source.Revision;
        var vars = _source.Variables;
        if (_main.Length < vars.Count) { _main = new VariableViewModel[vars.Count]; _tailLabel = new string?[vars.Count]; }
        if (_owned.Length < vars.Count) _owned = new VariableViewModel[vars.Count];
        _mainCount = _ownedCount = _unusedCount = 0;
        for (int i = 0; i < vars.Count; i++)
        {
            var v = vars[i];
            if (v.IsAutoManaged) { _owned[_ownedCount++] = v; continue; }
            if (v.IsUnused && !v.IsReadOnly) _unusedCount++;
            _tailLabel[_mainCount] = v.AliasedBy.Count > 0 ? "aliased x" + v.AliasedBy.Count + "##a" : v.IsUnused && !v.IsReadOnly ? "unused##a" : null;
            _main[_mainCount++] = v;
        }
    }

    // ── around the table ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawRemoveUnused()
    {
        RemoveUnusedDrawn = false;
        // Only hand-authored unused variables count for the bulk delete (the node-owned ones go with their node).
        if (!_source.Features.RemoveUnused || _unusedCount == 0 || _readOnly) return;
        RemoveUnusedDrawn = true;
        if (ImGui.Button("[ Remove unused ]##rmvu")) _openRemoveUnused = true;
        RemoveUnusedMin = ImGui.GetItemRectMin(); RemoveUnusedMax = ImGui.GetItemRectMax();
    }

    private void DrawPopups()
    {
        if (_openRemoveUnused) { ImGui.OpenPopup("confirm_remove_unused"); _openRemoveUnused = false; }
        bool open = true;
        if (!ImGui.BeginPopupModal("confirm_remove_unused", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        var unused = new List<string>();
        for (int i = 0; i < _mainCount; i++) if (_main[i].IsUnused && !_main[i].IsReadOnly) unused.Add(_main[i].Name);
        ImGui.Text($"Remove {unused.Count} unused variables?");
        ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), "This cannot be undone.");
        if (ImGui.Button("Remove")) { _source.RemoveVariables(unused); ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel") || Hrot.Editor.AiShared.Shell.ModalKeys.EscapePressed()) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    /// <summary>The rows a node allocated for itself: created and removed with the node, so they are only listed here (name and type).</summary>
    private void DrawNodeOwned()
    {
        OwnedListed = 0;
        if (_ownedCount == 0) return;
        ImGui.Spacing();
        if (_ownedHeaderFor != _ownedCount) { _ownedHeaderFor = _ownedCount; _ownedHeader = $"Node-Owned Allocations ({_ownedCount})"; }
        if (!SectionHeader.Draw(OwnedId, _ownedHeader, "section/allocations", ImGuiTreeNodeFlags.DefaultOpen)) return;
        if (!ImGui.BeginTable(OwnedTableId, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV)) return;
        for (int i = 0; i < _ownedCount; i++)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(_owned[i].Name);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Auto-allocated by node. Removed when the owning node is deleted.");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(_owned[i].TypeName);
            OwnedListed++;
        }
        ImGui.EndTable();
    }

    private void DrawUnbound()
    {
        UnboundDrawn = 0;
        if (!_aliasing || _unbound.Count == 0) return;
        ImGui.Separator();
        if (!SectionHeader.Draw("hdr.unbound", "UNBOUND SUB-TREE REQUIREMENTS", "form/warning", ImGuiTreeNodeFlags.DefaultOpen)) return;
        SyncUnboundLabels();
        if (_handleMin.Length < _unbound.Count) { _handleMin = new Vector2[_unbound.Count]; _handleMax = new Vector2[_unbound.Count]; }
        for (int i = 0; i < _unbound.Count; i++)
        {
            var req = _unbound[i];
            ImGui.PushID(1000 + i);
            // Use Selectable (which has an item ID) as the drag handle: a bare Text item has no ID, and BeginDragDropSource then fails an ImGui assertion.
            ImGui.Selectable(_unboundLabel[i]);
            if (i < _handleMin.Length) { _handleMin[i] = ImGui.GetItemRectMin(); _handleMax[i] = ImGui.GetItemRectMax(); }
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                unsafe { int src = i; ImGui.SetDragDropPayload("BB_UNBOUND_DRAG", (IntPtr)(&src), sizeof(int)); }
                ImGui.Text($"{req.DtoTypeName}  (from {req.RequiringAssetName})");
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginPopupContextItem("##unbound_ctx"))
            {
                if (ImGui.MenuItem("Promote to new variable")) { } // deferred 1.5d
                ImGui.EndPopup();
            }
            ImGui.PopID();
            UnboundDrawn++;
        }
    }

    private void SyncUnboundLabels()
    {
        int n = _unbound.Count;
        if (_unboundLabel.Length < n) { _unboundLabel = new string[n]; _unboundFor = new string[n]; _unboundCount = -1; }
        for (int i = 0; i < n; i++)
        {
            string key = _unbound[i].RequiredByPath;
            if (!string.Equals(_unboundFor[i], key, StringComparison.Ordinal) || _unboundLabel[i] == null)
            {
                _unboundFor[i] = key;
                _unboundLabel[i] = $"[*] {_unbound[i].DtoTypeName}  --  Required by: {key}";
            }
        }
        _unboundCount = n;
    }

    // ── the table's trailing cell (§12a ②) ───────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly uint NoColor = 0;

    /// <summary>
    /// What one row gets in the trailing cell: the comparison tint (the row's background), and — for a hand-authored row, when aliasing is on — the alias DROP TARGET carrying the <i>unused</i> / <i>aliased xN</i> mark
    /// (a right click lists the aliases to remove). ⛔ A node-owned row never reaches the table; a reserved Params row (read-only) takes no alias.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]  // R-2084: tier-0 JIT boxes struct arguments (FieldRow) — the steady frame must not depend on tiering
    private void DrawTail(FieldRow row, int i)
    {
        if (i < 0 || i >= _mainCount) return;
        var v = _main[i];
        if (_decoration != null && _decoration(v.Name) is { } dec)
        {
            // row background tint for decorated variables
            uint c = dec.IsAdded ? ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.2f, 0.15f))
                   : dec.IsRemoved ? ImGui.GetColorU32(new Vector4(0.9f, 0.2f, 0.2f, 0.15f))
                   : dec.IsRetyped ? ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 1.0f, 0.15f))
                   : dec.IsRenamed ? ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.3f, 0.15f)) : NoColor;
            if (c != NoColor) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, c);
        }
        string? mark = _tailLabel[i];
        bool zone = _aliasing && !_readOnly && !v.IsReadOnly && (_unbound.Count > 0 || v.AliasedBy.Count > 0);
        if (!zone)
        {
            if (mark != null) { ImGui.AlignTextToFramePadding(); ImGui.TextDisabled(mark); }
            return;
        }
        if (mark == null) mark = "##a";
        ImGui.Selectable(mark, false, ImGuiSelectableFlags.None, new Vector2(0f, ImGui.GetFrameHeight()));
        if (ImGui.IsItemHovered() && v.IsUnused) ImGui.SetTooltip("Not referenced by any node -- consider removing.");
        if (ImGui.BeginDragDropTarget())
        {
            AcceptAlias(v);
            ImGui.EndDragDropTarget();
        }
        if (v.AliasedBy.Count > 0 && ImGui.BeginPopupContextItem("##alias_ctx"))
        {
            for (int ai = 0; ai < v.AliasedBy.Count; ai++)
            {
                var (assetName, assetId, elementId) = v.AliasedBy[ai];
                if (ImGui.MenuItem($"Remove alias: {assetName}")) _source.RemoveAlias(v.Name, assetId, elementId);
            }
            ImGui.EndPopup();
        }
    }

    private unsafe void AcceptAlias(VariableViewModel row)
    {
        var payload = ImGui.AcceptDragDropPayload("BB_UNBOUND_DRAG");
        if (payload.NativePtr == null) return;
        int srcIdx = *(int*)payload.Data;
        if (srcIdx < 0 || srcIdx >= _source.UnboundRequirements.Count) return;
        var req = _source.UnboundRequirements[srcIdx];
        // B-4 §3.7: auto-managed vars are excluded from alias drop-targets.
        if (!IsAliasDropAccepted(row, req.DtoType)) return;
        var binding = new BlackboardAliasBinding(req.RequiringAssetId, req.RequiringElementId, req.RequiringAssetName, req.RequiredByPath, req.DtoType);
        if (_asset is IBlackboardManagedAsset bbma
            && BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(bbma, row.Name, binding, _source.GetParallelRegionMap())) return;
        _source.AddAlias(row.Name, binding);
    }

    // ── headless-testable alias drop predicate ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when a BB_UNBOUND_DRAG alias drop is accepted onto <paramref name="targetRow"/>. Auto-managed variables are never valid alias targets (B-4 §3.7); the DTO type must also match the target's field type.
    /// </summary>
    public static bool IsAliasDropAccepted(VariableViewModel targetRow, Type draggedDtoType)
    {
        if (targetRow.IsAutoManaged) return false;
        return draggedDtoType == targetRow.FieldType;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Editor.Variables;

public sealed class BlueprintEditableAssetAdapter : IEditableAsset
{
    private readonly BlueprintAsset _asset;
    public BlueprintEditableAssetAdapter(BlueprintAsset asset) { _asset = asset; }
    public Guid AssetId => _asset.AssetId;
    public string Name => _asset.Name;
    public AssetKind Kind => AssetKind.Blueprint;
    public string SourceFilePath => string.Empty;
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
    public event Action? Changed { add { } remove { } }
    public BlueprintAsset Asset => _asset;
}

public sealed class BlueprintVariableSchemaSource : IVariablesSchemaSource
{
    private readonly BlueprintAsset _asset;
    private readonly Action _onChanged;
    private readonly bool _isParams;

    public BlueprintVariableSchemaSource(BlueprintAsset asset, bool isParams, Action onChanged)
    {
        _asset = asset;
        _isParams = isParams;
        _onChanged = onChanged;
    }

    public bool IsReadOnly => false;

    public string? GetRefactorKey(string variableName)
    {
        if (_isParams)
        {
            var sanitizedName = Hrot.Blueprints.Core.Compiler.Emit.Sanitizer.SanitizeName(_asset.Name);
            var bpId = Fdp.Toolkit.Blueprints.BlueprintIdHash.Compute(_asset.AssetId);
            return $"Hrot.AI.Behaviors.Generated.{sanitizedName}_{bpId:X8}_Bp+Params::{variableName}";
        }
        return $"{_asset.AssetId:D}::{variableName}";
    }

    private IEnumerable<BlackboardVariableEntry> Entries => _isParams 
        ? GetOrdered(_asset.Parameters, _asset.ParameterOrder).Select(p => new BlackboardVariableEntry(p.Name, Type.GetType(p.Type.TypeId) ?? typeof(int), p.Comment ?? ""))
        : GetOrdered(_asset.WorkingState, _asset.WorkingStateOrder).Select(v => new BlackboardVariableEntry(v.Name, Type.GetType(v.Type.TypeId) ?? typeof(int), v.Comment ?? ""));

    private IEnumerable<T> GetOrdered<T>(List<T> items, List<Guid>? order)
    {
        if (order == null || order.Count == 0) return items;
        var dict = items.ToDictionary(GetId);
        var result = new List<T>();
        foreach (var id in order)
        {
            if (dict.TryGetValue(id, out var item))
            {
                result.Add(item);
                dict.Remove(id);
            }
        }
        result.AddRange(dict.Values);
        return result;

        Guid GetId(T item)
        {
            if (item is ParameterDecl p) return p.Id;
            if (item is VariableDecl v) return v.Id;
            return Guid.Empty;
        }
    }

    public IReadOnlyList<VariableViewModel> Variables
    {
        get
        {
            var list = new List<VariableViewModel>();
            foreach (var e in Entries)
            {
                list.Add(new VariableViewModel(
                    e.Name,
                    BlackboardTypeHelper.GetDisplayName(e.FieldType),
                    GetPayloadByteSize(e.FieldType),
                    e.FieldType,
                    e.Comment,
                    Array.Empty<(string, Guid, Guid)>(),
                    false // Not dynamically tracking unused in Blueprint right now
                ));
            }
            return list;
        }
    }

    public void AddVariable(BlackboardVariableEntry entry)
    {
        var typeRef = new BlueprintTypeRef { TypeId = entry.FieldType.FullName ?? entry.FieldType.Name };
        if (_isParams)
        {
            var p = new ParameterDecl { Id = Guid.NewGuid(), Name = entry.Name, Type = typeRef, Comment = entry.Comment };
            _asset.Parameters.Add(p);
            _asset.ParameterOrder ??= _asset.Parameters.Where(x => x != p).Select(x => x.Id).ToList();
            _asset.ParameterOrder.Add(p.Id);
        }
        else
        {
            var v = new VariableDecl { Id = Guid.NewGuid(), Name = entry.Name, Type = typeRef, Comment = entry.Comment };
            _asset.WorkingState.Add(v);
            _asset.WorkingStateOrder ??= _asset.WorkingState.Where(x => x != v).Select(x => x.Id).ToList();
            _asset.WorkingStateOrder.Add(v.Id);
        }
        _onChanged?.Invoke();
    }

    public void MoveVariable(int sourceIndex, int destIndex)
    {
        if (_isParams)
        {
            if (_asset.ParameterOrder == null)
            {
                _asset.ParameterOrder = _asset.Parameters.Select(p => p.Id).ToList();
            }
            var item = _asset.ParameterOrder[sourceIndex];
            _asset.ParameterOrder.RemoveAt(sourceIndex);
            _asset.ParameterOrder.Insert(destIndex, item);
        }
        else
        {
            if (_asset.WorkingStateOrder == null)
            {
                _asset.WorkingStateOrder = _asset.WorkingState.Select(v => v.Id).ToList();
            }
            var item = _asset.WorkingStateOrder[sourceIndex];
            _asset.WorkingStateOrder.RemoveAt(sourceIndex);
            _asset.WorkingStateOrder.Insert(destIndex, item);
        }
        _onChanged?.Invoke();
    }

    public void RemoveVariable(string name)
    {
        if (_isParams) _asset.Parameters.RemoveAll(x => x.Name == name);
        else _asset.WorkingState.RemoveAll(x => x.Name == name);
        _onChanged?.Invoke();
    }

    public void RemoveVariables(IReadOnlyList<string> names)
    {
        var set = new HashSet<string>(names);
        if (_isParams) _asset.Parameters.RemoveAll(x => set.Contains(x.Name));
        else _asset.WorkingState.RemoveAll(x => set.Contains(x.Name));
        _onChanged?.Invoke();
    }

    public void RenameVariable(string oldName, string newName)
    {
        if (_isParams)
        {
            var match = _asset.Parameters.FirstOrDefault(x => x.Name == oldName);
            if (match != null) match.Name = newName;
        }
        else
        {
            var match = _asset.WorkingState.FirstOrDefault(x => x.Name == oldName);
            if (match != null) match.Name = newName;
        }
        _onChanged?.Invoke();
    }

    public int CountNodesReferencingVariable(string name) => 0;

    public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements => Array.Empty<UnboundRequirementViewModel>();
    public void AddAlias(string name, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string name, Guid reqAssetId, Guid reqElemId) { }
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;

    private static int GetPayloadByteSize(Type type)
    {
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        if (type == typeof(System.Numerics.Vector2)) return 8;
        if (type == typeof(System.Numerics.Vector3)) return 12;
        if (type == typeof(System.Numerics.Vector4) || type == typeof(System.Numerics.Quaternion)) return 16;
        return 8; // fallback
    }
}

public sealed class BlueprintVariablesWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly IRefactorService _refactorService;
    private VariablesPanelControl? _variablesControl;
    private IEditableAsset? _lastAsset;

    public BlueprintVariablesWindow(EditorSelectionStore selectionStore, DirtyTracker dirtyTracker, IRefactorService refactorService)
    {
        _selectionStore = selectionStore;
        _dirtyTracker = dirtyTracker;
        _refactorService = refactorService;
    }

    public override string Title => "Variables";

    public override void DrawUI()
    {
        if (_selectionStore.SelectedAsset == null)
        {
            ImGui.TextDisabled("No blueprint selected.");
            return;
        }

        var asset = _selectionStore.SelectedAsset;
        var adapter = new BlueprintEditableAssetAdapter(asset);

        if (_variablesControl == null || _lastAsset?.AssetId != asset.AssetId)
        {
            _variablesControl = new VariablesPanelControl(_refactorService, adapter, BlackboardTypeHelper.DefaultKnownTypeNames);
            _lastAsset = adapter;
        }

        var paramsSchema = new BlueprintVariableSchemaSource(asset, true, () => _dirtyTracker.MarkDirty(asset.AssetId));
        var stateSchema = new BlueprintVariableSchemaSource(asset, false, () => _dirtyTracker.MarkDirty(asset.AssetId));

        var paramsVars = paramsSchema.Variables;
        int paramsInline = paramsVars.Sum(v => v.ByteSize);
        var paramsSection = new VariablesPanelSection(
            "Parameters (Sync In)",
            "##bp_params",
            paramsSchema,
            paramsInline,
            100, // 100B per budget spec 1g-03
            0,
            128, // MaxHeavyBytes not used in BP, just give a number
            false,
            paramsInline > 100 ? (PackWarning)1 : (PackWarning)0,
            false // aliasing-off
        );

        var stateVars = stateSchema.Variables;
        int stateInline = stateVars.Sum(v => v.ByteSize);
        int stateBudget = asset.TierHint switch
        {
            BlackboardTierHint.Auto => 1024,
            BlackboardTierHint.Force1024 => 1024,
            BlackboardTierHint.Force4096 => 4096,
            BlackboardTierHint.Force16384 => 16384,
            _ => 1024
        };

        var stateSection = new VariablesPanelSection(
            "Working State",
            "##bp_state",
            stateSchema,
            stateInline,
            stateBudget,
            0,
            128, // arbitrary max heavy
            false,
            stateInline > stateBudget ? (PackWarning)2 : (PackWarning)0,
            false // aliasing-off
        );

        _variablesControl.DrawDual(paramsSection, stateSection);
    }
}

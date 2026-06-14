using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---- Minimal mutable stub with alias backing for aliasing tests ------------

internal sealed class AliasMutableAsset : IEditableAsset, IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars = new();
    private readonly Dictionary<string, List<BlackboardAliasBinding>> _aliases = new();

    public Guid      AssetId        { get; } = Guid.NewGuid();
    public string    Name           { get; set; } = "TestAsset";
    public AssetKind Kind           => AssetKind.BTree;
    public string    SourceFilePath => "/test.cs";
    public bool      IsDirty        => false;
    public bool      IsEditorOwned  => true;
    public bool      IsBlackboardEditorManaged { get; set; } = true;
    public void SetBlackboardEditorManaged(bool managed) => IsBlackboardEditorManaged = managed;

    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;

    public int ChangedCount { get; private set; }
    public event Action? Changed;
    private void Fire() { ChangedCount++; Changed?.Invoke(); }

    public void AddVariable(BlackboardVariableEntry entry)
    { _vars.Add(entry); Fire(); }

    public void RemoveVariable(string name)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars.RemoveAt(i);
        _aliases.Remove(name);
        Fire();
    }

    public void UpdateVariableComment(string name, string? comment)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars[i] = _vars[i] with { Comment = comment };
        Fire();
    }

    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars[i] = _vars[i] with { DefaultValueJson = defaultValueJson };
        Fire();
    }

    public void MoveVariable(int sourceIndex, int destIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _vars.Count) return;
        if (destIndex   < 0 || destIndex   >= _vars.Count) return;
        if (sourceIndex == destIndex) return;
        var entry = _vars[sourceIndex];
        _vars.RemoveAt(sourceIndex);
        _vars.Insert(destIndex, entry);
        Fire();
    }

    public void RenameVariable(string oldName, string newName)
    {
        int i = _vars.FindIndex(v => v.Name == oldName);
        if (i < 0) return;
        _vars[i] = _vars[i] with { Name = newName };
        if (_aliases.TryGetValue(oldName, out var list))
        {
            _aliases.Remove(oldName);
            _aliases[newName] = list;
        }
        Fire();
    }

    public int CountNodesReferencingVariable(string name) => 0;

    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        _aliases.TryGetValue(variableName, out var list)
            ? list.AsReadOnly()
            : Array.Empty<BlackboardAliasBinding>();

    public void AddAlias(string variableName, BlackboardAliasBinding binding)
    {
        if (!_aliases.TryGetValue(variableName, out var list))
        {
            list = new List<BlackboardAliasBinding>();
            _aliases[variableName] = list;
        }
        if (list.Exists(a => a.RequiringAssetId == binding.RequiringAssetId
                          && a.RequiringElementId == binding.RequiringElementId))
            return;
        list.Add(binding);
        Fire();
    }

    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId)
    {
        if (!_aliases.TryGetValue(variableName, out var list)) return;
        int idx = list.FindIndex(a => a.RequiringAssetId == requiringAssetId
                                   && a.RequiringElementId == requiringElementId);
        if (idx < 0) return;
        list.RemoveAt(idx);
        Fire();
    }

    public void RemoveVariables(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        bool removed = false;
        foreach (var n in names)
        {
            int i = _vars.FindIndex(v => v.Name == n);
            if (i < 0) continue;
            _vars.RemoveAt(i);
            _aliases.Remove(n);
            removed = true;
        }
        if (removed) Fire();
    }
}

// ---- Tests ------------------------------------------------------------------

/// <summary>
/// Tests for TASK-BB-1d-01, 1d-02, 1d-05 alias binding model and BuildViewModel integration.
/// </summary>
public sealed class BlackboardAliasingTests
{
    // ---- Helpers ------------------------------------------------------------

    private static BlackboardAliasBinding MakeBinding(
        string assetName = "Shoot_BT",
        string path = "Shoot_BT > Action#1") =>
        new(
            RequiringAssetId:    Guid.NewGuid(),
            RequiringElementId:  Guid.NewGuid(),
            RequiringAssetName:  assetName,
            RequiredByPath:      path,
            DtoType:             typeof(float));

    private static AliasMutableAsset MakeAssetWithVar(string varName, Type? type = null)
    {
        var asset = new AliasMutableAsset { IsBlackboardEditorManaged = true };
        asset.AddVariable(new BlackboardVariableEntry(varName, type ?? typeof(float), null));
        return asset;
    }

    private static AggregationResult AggResult(params DtoRequirement[] reqs) =>
        new(reqs, Array.Empty<AggregationWarning>());

    // ---- AddAlias_stores_binding_for_variable --------------------------------

    [Fact]
    public void AddAlias_stores_binding_for_variable()
    {
        var asset   = MakeAssetWithVar("speed");
        var binding = MakeBinding();

        asset.AddAlias("speed", binding);

        var result = asset.GetAliasesFor("speed");
        Assert.Single(result);
        Assert.Equal(binding.RequiringAssetId, result[0].RequiringAssetId);
    }

    // ---- AddAlias_does_not_duplicate_same_requirement -----------------------

    [Fact]
    public void AddAlias_does_not_duplicate_same_requirement()
    {
        var asset   = MakeAssetWithVar("speed");
        var binding = MakeBinding();

        asset.AddAlias("speed", binding);
        asset.AddAlias("speed", binding); // same (assetId, elementId) -- must be ignored

        Assert.Single(asset.GetAliasesFor("speed"));
    }

    // ---- RemoveAlias_removes_binding ----------------------------------------

    [Fact]
    public void RemoveAlias_removes_binding()
    {
        var asset   = MakeAssetWithVar("speed");
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RemoveAlias("speed", binding.RequiringAssetId, binding.RequiringElementId);

        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- RemoveAlias_noop_when_not_found ------------------------------------

    [Fact]
    public void RemoveAlias_noop_when_not_found()
    {
        var asset = MakeAssetWithVar("speed");

        // Should not throw; no aliases exist.
        asset.RemoveAlias("speed", Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- RemoveVariable_clears_its_aliases ----------------------------------

    [Fact]
    public void RemoveVariable_clears_its_aliases()
    {
        var asset   = MakeAssetWithVar("speed");
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RemoveVariable("speed");

        // Variable gone; aliases must also be gone (no KeyNotFoundException on access).
        Assert.Empty(asset.GetAliasesFor("speed"));
    }

    // ---- RenameVariable_renames_alias_key -----------------------------------

    [Fact]
    public void RenameVariable_renames_alias_key()
    {
        var asset   = MakeAssetWithVar("speed");
        var binding = MakeBinding();
        asset.AddAlias("speed", binding);

        asset.RenameVariable("speed", "velocity");

        Assert.Empty(asset.GetAliasesFor("speed"));
        var result = asset.GetAliasesFor("velocity");
        Assert.Single(result);
        Assert.Equal(binding.RequiringAssetId, result[0].RequiringAssetId);
    }

    // ---- BuildViewModel_aliased_requirement_absent_from_unbound_list --------

    [Fact]
    public void BuildViewModel_aliased_requirement_absent_from_unbound_list()
    {
        var asset      = MakeAssetWithVar("speed");
        var assetId    = Guid.NewGuid();
        var elementId  = Guid.NewGuid();
        var binding    = new BlackboardAliasBinding(assetId, elementId, "Shoot_BT", "Shoot_BT > Action#1", typeof(float));
        asset.AddAlias("speed", binding);

        var req    = new DtoRequirement(typeof(float), "Shoot_BT > Action#1", assetId, elementId);
        var aggRes = AggResult(req);

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, null, aggRes);

        // The requirement has been aliased, so it must not appear in UnboundRequirements.
        Assert.Empty(vm.UnboundRequirements);
    }

    // ---- BuildViewModel_unaliased_requirement_present_in_unbound_list -------

    [Fact]
    public void BuildViewModel_unaliased_requirement_present_in_unbound_list()
    {
        var asset = MakeAssetWithVar("speed");
        var req   = new DtoRequirement(typeof(float), "Shoot_BT > Action#1", Guid.NewGuid(), Guid.NewGuid());
        var aggRes = AggResult(req);

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, null, aggRes);

        Assert.Single(vm.UnboundRequirements);
        Assert.Equal(req.DtoType.Name, vm.UnboundRequirements[0].DtoTypeName);
    }

    // ---- BuildViewModel_variable_row_shows_aliased_by_name ------------------

    [Fact]
    public void BuildViewModel_variable_row_shows_aliased_by_name()
    {
        var asset     = MakeAssetWithVar("speed");
        var assetId   = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        var binding   = new BlackboardAliasBinding(assetId, elementId, "Shoot_BT", "Shoot_BT > Action#1", typeof(float));
        asset.AddAlias("speed", binding);

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Single(vm.Variables);
        var row = vm.Variables[0];
        Assert.Single(row.AliasedBy);
        Assert.Equal("Shoot_BT", row.AliasedBy[0].AssetName);
        Assert.Equal(assetId,    row.AliasedBy[0].AssetId);
        Assert.Equal(elementId,  row.AliasedBy[0].ElementId);
    }

    // ---- BuildViewModel_variable_row_aliased_by_empty_when_no_aliases -------

    [Fact]
    public void BuildViewModel_variable_row_aliased_by_empty_when_no_aliases()
    {
        var asset = MakeAssetWithVar("speed");

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Single(vm.Variables);
        Assert.Empty(vm.Variables[0].AliasedBy);
    }
}

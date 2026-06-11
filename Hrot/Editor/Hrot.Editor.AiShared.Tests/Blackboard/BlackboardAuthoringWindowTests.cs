using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---- Minimal mutable stub for BlackboardAuthoringWindow view-model tests ---

file sealed class MutableBbAssetForWindowTests : IEditableAsset, IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars = new();

    public Guid   AssetId        { get; } = Guid.NewGuid();
    public string Name           { get; set; } = "TestAsset";
    public AssetKind Kind        => AssetKind.BTree;
    public string SourceFilePath => "/test.cs";
    public bool   IsDirty        => false;
    public bool   IsEditorOwned  => true;
    public bool   IsBlackboardEditorManaged { get; set; } = true;

    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;

    public event Action? Changed;

    public void AddVariable(BlackboardVariableEntry entry)
    { _vars.Add(entry); Changed?.Invoke(); }

    public void RemoveVariable(string name)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars.RemoveAt(i);
        Changed?.Invoke();
    }

    public void UpdateVariableComment(string name, string? comment)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars[i] = _vars[i] with { Comment = comment };
        Changed?.Invoke();
    }

    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars[i] = _vars[i] with { DefaultValueJson = defaultValueJson };
        Changed?.Invoke();
    }

    public void MoveVariable(int sourceIndex, int destIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _vars.Count) return;
        if (destIndex   < 0 || destIndex   >= _vars.Count) return;
        if (sourceIndex == destIndex) return;
        var entry = _vars[sourceIndex];
        _vars.RemoveAt(sourceIndex);
        _vars.Insert(destIndex, entry);
        Changed?.Invoke();
    }

    public void RenameVariable(string oldName, string newName)
    {
        int i = _vars.FindIndex(v => v.Name == oldName);
        if (i < 0) return;
        _vars[i] = _vars[i] with { Name = newName };
        Changed?.Invoke();
    }

    public int CountNodesReferencingVariable(string name) => 0;
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) => Array.Empty<BlackboardAliasBinding>();
    public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
    public void RemoveVariables(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        bool removed = false;
        foreach (var n in names)
        {
            int i = _vars.FindIndex(v => v.Name == n);
            if (i < 0) continue;
            _vars.RemoveAt(i);
            removed = true;
        }
        if (removed) Changed?.Invoke();
    }
}

// ---- Tests ------------------------------------------------------------------

/// <summary>
/// Tests for <see cref="BlackboardAuthoringWindow.BuildViewModel"/> covering
/// TASK-BB-1c-03 (unbound requirements panel) and TASK-BB-1c-05 (memory budget indicator).
/// </summary>
public sealed class BlackboardAuthoringWindowTests
{
    // ---- Helpers ------------------------------------------------------------

    private static AggregationResult AggResult(params DtoRequirement[] reqs) =>
        new(reqs, Array.Empty<AggregationWarning>());

    private static DtoRequirement Req(Type dtoType, string path) =>
        new(dtoType, path, Guid.NewGuid(), Guid.NewGuid());

    // ---- TASK-BB-1c-03: Unbound requirements panel --------------------------

    [Fact]
    public void BuildViewModel_no_aggregation_result_yields_empty_unbound_list()
    {
        var asset = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Empty(vm.UnboundRequirements);
    }

    [Fact]
    public void BuildViewModel_aggregation_result_with_requirements_yields_unbound_rows()
    {
        var asset  = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        var aggRes = AggResult(
            Req(typeof(int), "BT > Action#1"),
            Req(typeof(float), "BT > Action#2"));

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggRes);

        Assert.Equal(2, vm.UnboundRequirements.Count);
    }

    [Fact]
    public void BuildViewModel_aggregation_result_requirement_DtoTypeName_uses_type_Name()
    {
        var asset  = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        var aggRes = AggResult(Req(typeof(int), "any > path"));

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggRes);

        // typeof(int).Name is "Int32".
        Assert.Equal("Int32", vm.UnboundRequirements[0].DtoTypeName);
    }

    [Fact]
    public void BuildViewModel_aggregation_result_requirement_RequiredByPath_preserved()
    {
        const string path  = "OrcGuard_BT > Sequence#3 > FireAtTarget";
        var asset  = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        var aggRes = AggResult(Req(typeof(float), path));

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggRes);

        Assert.Equal(path, vm.UnboundRequirements[0].RequiredByPath);
    }

    // ---- TASK-BB-1c-05: Memory budget indicator -----------------------------

    [Fact]
    public void BuildViewModel_budget_inline_only_when_no_aggregation()
    {
        var asset = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.False(vm.RequiresHeavyComponent);
    }

    [Fact]
    public void BuildViewModel_budget_inline_budget_is_100()
    {
        var vm = BlackboardAuthoringWindow.BuildViewModel(
            new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true });

        Assert.Equal(100, vm.InlineBudget);
    }

    [Fact]
    public void BuildViewModel_budget_heavy_is_928()
    {
        var vm = BlackboardAuthoringWindow.BuildViewModel(
            new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true });

        Assert.Equal(928, vm.HeavyBudget);
    }

    [Fact]
    public void BuildViewModel_requires_heavy_false_when_all_fit_inline()
    {
        var asset = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        asset.AddVariable(new BlackboardVariableEntry("a", typeof(int), null));
        asset.AddVariable(new BlackboardVariableEntry("b", typeof(int), null));

        // One aggregated int: 8 + 4 = 12 B <= 100 B => all inline.
        var aggRes = AggResult(Req(typeof(int), "path"));
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggRes);

        Assert.False(vm.RequiresHeavyComponent);
    }

    [Fact]
    public void BuildViewModel_requires_heavy_true_when_aggregated_overflow_inline()
    {
        var asset = new MutableBbAssetForWindowTests { IsBlackboardEditorManaged = true };
        // 25 ints = 100 B inline (exactly at the budget ceiling).
        for (int i = 0; i < 25; i++)
            asset.AddVariable(new BlackboardVariableEntry($"m{i}", typeof(int), null));

        // One aggregated int: 100 + 4 = 104 B > 100 B => spills to heavy.
        var aggRes = AggResult(Req(typeof(int), "BT > Action#1"));
        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, aggregationResult: aggRes);

        Assert.True(vm.RequiresHeavyComponent);
    }
}

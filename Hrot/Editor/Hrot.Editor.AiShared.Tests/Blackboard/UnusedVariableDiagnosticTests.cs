using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---- Stub asset with configurable per-variable reference counts ----

internal sealed class RefCountBbAsset : IEditableAsset, IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars = new();
    private readonly Dictionary<string, int> _refCounts = new();

    public Guid      AssetId        { get; } = Guid.NewGuid();
    public string    Name           { get; set; } = "RefCountAsset";
    public AssetKind Kind           => AssetKind.BTree;
    public string    SourceFilePath => "/stub.cs";
    public bool      IsDirty        => false;
    public bool      IsEditorOwned  => true;
    public bool      IsBlackboardEditorManaged { get; set; } = true;
    public void SetBlackboardEditorManaged(bool managed) => IsBlackboardEditorManaged = managed;

    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;

    public event Action? Changed;

    public void AddVariableWithRefCount(string name, Type type, int refCount)
    {
        _vars.Add(new BlackboardVariableEntry(name, type, null));
        _refCounts[name] = refCount;
    }

    public void AddVariable(BlackboardVariableEntry entry)    { _vars.Add(entry); Changed?.Invoke(); }
    public void RemoveVariable(string name)                   { Changed?.Invoke(); }
    public void RemoveVariables(IReadOnlyList<string> names)  { Changed?.Invoke(); }
    public void UpdateVariableComment(string name, string? comment) { Changed?.Invoke(); }
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson) { Changed?.Invoke(); }
    public void MoveVariable(int sourceIndex, int destIndex)  { Changed?.Invoke(); }
    public void RenameVariable(string oldName, string newName) { Changed?.Invoke(); }

    public int CountNodesReferencingVariable(string name) =>
        _refCounts.TryGetValue(name, out int c) ? c : 0;

    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        Array.Empty<BlackboardAliasBinding>();

    public void AddAlias(string variableName, BlackboardAliasBinding binding)                       { }
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId)    { }
}

// ---- Tests for 1f-03 (IsUnused flag in VariableViewModel) ----

public sealed class UnusedVariableDiagnosticTests
{
    [Fact]
    public void BuildViewModel_SetsIsUnused_WhenZeroReferences()
    {
        var asset = new RefCountBbAsset();
        asset.AddVariableWithRefCount("speed", typeof(float), 0);

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.True(vm.Variables[0].IsUnused,
            "variable with 0 node references must be flagged IsUnused");
    }

    [Fact]
    public void BuildViewModel_ClearsIsUnused_WhenOneReference()
    {
        var asset = new RefCountBbAsset();
        asset.AddVariableWithRefCount("health", typeof(int), 1);

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.False(vm.Variables[0].IsUnused,
            "variable with at least one node reference must not be flagged IsUnused");
    }

    [Fact]
    public void BuildViewModel_MultipleVars_OnlyUnusedOnesMarked()
    {
        var asset = new RefCountBbAsset();
        asset.AddVariableWithRefCount("usedVar",   typeof(float), 3);
        asset.AddVariableWithRefCount("unusedVar", typeof(int),   0);
        asset.AddVariableWithRefCount("alsoUsed",  typeof(float), 1);

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        var rows = vm.Variables.ToDictionary(v => v.Name);
        Assert.False(rows["usedVar"].IsUnused,   "usedVar has 3 refs");
        Assert.True(rows["unusedVar"].IsUnused,  "unusedVar has 0 refs");
        Assert.False(rows["alsoUsed"].IsUnused,  "alsoUsed has 1 ref");
    }
}

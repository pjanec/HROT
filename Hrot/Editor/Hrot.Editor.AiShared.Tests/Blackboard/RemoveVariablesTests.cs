using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---- Stub for RemoveVariables batch-removal tests ----

internal sealed class RemoveVarsBbAsset : IEditableAsset, IBlackboardManagedAsset
{
    private readonly List<BlackboardVariableEntry> _vars = new();

    public Guid      AssetId        { get; } = Guid.NewGuid();
    public string    Name           { get; set; } = "TestAsset";
    public AssetKind Kind           => AssetKind.BTree;
    public string    SourceFilePath => "/test.cs";
    public bool      IsDirty        => false;
    public bool      IsEditorOwned  => true;
    public bool      IsBlackboardEditorManaged { get; set; } = true;

    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;

    public int    ChangedCount { get; private set; }
    public event Action? Changed;
    private void Fire() { ChangedCount++; Changed?.Invoke(); }

    public void AddVariable(BlackboardVariableEntry entry)    { _vars.Add(entry); Fire(); }

    public void RemoveVariable(string name)
    {
        int i = _vars.FindIndex(v => v.Name == name);
        if (i < 0) return;
        _vars.RemoveAt(i);
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
            removed = true;
        }
        if (removed) Fire();
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

    public void MoveVariable(int sourceIndex, int destIndex)  { Fire(); }
    public void RenameVariable(string oldName, string newName) { Fire(); }

    public int CountNodesReferencingVariable(string name) => 0;
    public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) =>
        Array.Empty<BlackboardAliasBinding>();
    public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
    public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }

    public void ResetChangedCount() => ChangedCount = 0;
}

public sealed class RemoveVariablesTests
{
    // ---- Tests for IBlackboardManagedAsset.RemoveVariables (TASK-1d-04) ----

    private static RemoveVarsBbAsset BuildWithVars(params string[] names)
    {
        var a = new RemoveVarsBbAsset();
        foreach (var n in names)
            a.AddVariable(new BlackboardVariableEntry(n, typeof(float), null));
        a.ResetChangedCount();
        return a;
    }

    private static int ChangeCount(RemoveVarsBbAsset a) => a.ChangedCount;

    [Fact]
    public void RemoveVariables_RemovesListedNames()
    {
        var asset = BuildWithVars("a", "b", "c");

        asset.RemoveVariables(new[] { "a", "c" });

        Assert.Single(asset.BlackboardVariables);
        Assert.Equal("b", asset.BlackboardVariables[0].Name);
    }

    [Fact]
    public void RemoveVariables_FiresChangedExactlyOnce_WhenSomethingRemoved()
    {
        var asset = BuildWithVars("x", "y");

        asset.RemoveVariables(new[] { "x", "y" });

        Assert.Equal(1, asset.ChangedCount);
    }

    [Fact]
    public void RemoveVariables_DoesNotFireChanged_WhenNoNamesMatch()
    {
        var asset = BuildWithVars("a", "b");

        asset.RemoveVariables(new[] { "z" });

        Assert.Equal(0, asset.ChangedCount);
        Assert.Equal(2, asset.BlackboardVariables.Count);
    }

    [Fact]
    public void RemoveVariables_DoesNotFireChanged_WhenListEmpty()
    {
        var asset = BuildWithVars("a");

        asset.RemoveVariables(Array.Empty<string>());

        Assert.Equal(0, asset.ChangedCount);
        Assert.Single(asset.BlackboardVariables);
    }

    [Fact]
    public void RemoveVariables_SkipsNamesNotFound()
    {
        var asset = BuildWithVars("keep", "remove");

        asset.RemoveVariables(new[] { "remove", "notexist" });

        Assert.Single(asset.BlackboardVariables);
        Assert.Equal("keep", asset.BlackboardVariables[0].Name);
        Assert.Equal(1, asset.ChangedCount);
    }

    [Fact]
    public void RemoveVariables_RemoveAll_LeavesEmptyList()
    {
        var asset = BuildWithVars("a", "b", "c");

        asset.RemoveVariables(new[] { "a", "b", "c" });

        Assert.Empty(asset.BlackboardVariables);
        Assert.Equal(1, asset.ChangedCount);
    }

    [Fact]
    public void RemoveVariables_RemovesAliasKeys()
    {
        // Use AliasMutableAsset which backs alias bindings per variable.
        var asset = new AliasMutableAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("y", typeof(int), null));
        var binding = new BlackboardAliasBinding(
            Guid.NewGuid(), Guid.NewGuid(), "Sub_BT", "/Sub_BT.cs", typeof(float));
        asset.AddAlias("x", binding);

        asset.RemoveVariables(new[] { "x" });

        Assert.Empty(asset.GetAliasesFor("x"));
        Assert.Single(asset.BlackboardVariables);
        Assert.Equal("y", asset.BlackboardVariables[0].Name);
    }
}

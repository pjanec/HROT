using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---- Minimal mutable stub for model mutation tests -------------------------

file sealed class MutableBbAsset : IEditableAsset, IBlackboardManagedAsset
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
        Fire();
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
        if (removed) Fire();
    }
}

// ---- Tests ------------------------------------------------------------------

public sealed class BlackboardAddRemoveTests
{
    // ---- TASK-BB-1b-02: AddVariable -----------------------------------------

    [Fact]
    public void AddVariable_appends_entry_to_list()
    {
        var asset = new MutableBbAsset();
        var entry = new BlackboardVariableEntry("speed", typeof(float), null);

        asset.AddVariable(entry);

        Assert.Single(asset.BlackboardVariables);
        Assert.Equal("speed", asset.BlackboardVariables[0].Name);
    }

    [Fact]
    public void AddVariable_fires_Changed()
    {
        var asset = new MutableBbAsset();
        asset.AddVariable(new BlackboardVariableEntry("hp", typeof(int), null));

        Assert.Equal(1, asset.ChangedCount);
    }

    [Fact]
    public void AddVariable_duplicate_name_still_appends_at_model_level()
    {
        var asset = new MutableBbAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));

        // Model does not enforce uniqueness; validation is at the UI level.
        Assert.Equal(2, asset.BlackboardVariables.Count);
    }

    // ---- TASK-BB-1b-02: RemoveVariable --------------------------------------

    [Fact]
    public void RemoveVariable_removes_correct_entry_and_fires_Changed()
    {
        var asset = new MutableBbAsset();
        asset.AddVariable(new BlackboardVariableEntry("a", typeof(int), null));
        asset.AddVariable(new BlackboardVariableEntry("b", typeof(int), null));
        int before = asset.ChangedCount;

        asset.RemoveVariable("a");

        Assert.Single(asset.BlackboardVariables);
        Assert.Equal("b", asset.BlackboardVariables[0].Name);
        Assert.Equal(before + 1, asset.ChangedCount);
    }

    [Fact]
    public void RemoveVariable_unknown_name_is_noop_no_exception()
    {
        var asset = new MutableBbAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        int before = asset.ChangedCount;

        // Should not throw and should not fire Changed.
        asset.RemoveVariable("nonexistent");

        Assert.Single(asset.BlackboardVariables);
        Assert.Equal(before, asset.ChangedCount);
    }

    // ---- TASK-BB-1b-02: MoveVariable ----------------------------------------

    [Fact]
    public void MoveVariable_reorders_correctly()
    {
        var asset = new MutableBbAsset();
        asset.AddVariable(new BlackboardVariableEntry("a", typeof(int), null));
        asset.AddVariable(new BlackboardVariableEntry("b", typeof(int), null));
        asset.AddVariable(new BlackboardVariableEntry("c", typeof(int), null));

        // Move index 0 ("a") to index 2.
        asset.MoveVariable(0, 2);

        Assert.Equal("b", asset.BlackboardVariables[0].Name);
        Assert.Equal("c", asset.BlackboardVariables[1].Name);
        Assert.Equal("a", asset.BlackboardVariables[2].Name);
    }

    // ---- BlackboardNameValidator --------------------------------------------

    [Fact]
    public void Validate_null_name_returns_error()
    {
        var result = BlackboardNameValidator.Validate(null);
        Assert.NotNull(result);
    }

    [Fact]
    public void Validate_empty_name_returns_error()
    {
        var result = BlackboardNameValidator.Validate("");
        Assert.NotNull(result);
    }

    [Fact]
    public void Validate_name_starting_with_digit_returns_error()
    {
        var result = BlackboardNameValidator.Validate("123bad");
        Assert.NotNull(result);
    }

    [Fact]
    public void Validate_valid_name_with_empty_existing_returns_null()
    {
        var result = BlackboardNameValidator.Validate("my_var", new List<BlackboardVariableEntry>());
        Assert.Null(result);
    }

    [Fact]
    public void Validate_duplicate_name_returns_error()
    {
        var existing = new List<BlackboardVariableEntry>
        {
            new BlackboardVariableEntry("speed", typeof(float), null),
        };
        var result = BlackboardNameValidator.Validate("speed", existing);
        Assert.NotNull(result);
    }

    [Fact]
    public void Validate_csharp_keyword_returns_error()
    {
        var result = BlackboardNameValidator.Validate("float");
        Assert.NotNull(result);
    }

    [Fact]
    public void Validate_underscore_prefix_is_valid()
    {
        var result = BlackboardNameValidator.Validate("_private");
        Assert.Null(result);
    }

    // ---- BlackboardTypeHelper -----------------------------------------------

    [Fact]
    public void GetPrimitiveType_int_returns_typeof_int()
    {
        Assert.Equal(typeof(int), BlackboardTypeHelper.GetPrimitiveType("int"));
    }

    [Fact]
    public void GetPrimitiveType_Vector3_returns_typeof_Vector3()
    {
        Assert.Equal(typeof(Vector3), BlackboardTypeHelper.GetPrimitiveType("Vector3"));
    }

    [Fact]
    public void GetPrimitiveType_unknown_returns_null()
    {
        Assert.Null(BlackboardTypeHelper.GetPrimitiveType("unknown"));
    }

    // ---- BuildViewModel: KnownTypeNames population --------------------------

    [Fact]
    public void BuildViewModel_with_explicit_knownTypeNames_populates_field()
    {
        var typeNames = new[] { "int", "float" };
        var asset = new MutableBbAsset { IsBlackboardEditorManaged = true };

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset, typeNames);

        Assert.Equal(typeNames, vm.KnownTypeNames);
    }

    [Fact]
    public void BuildViewModel_without_knownTypeNames_uses_default_list()
    {
        var asset = new MutableBbAsset { IsBlackboardEditorManaged = true };

        var vm = BlackboardAuthoringWindow.BuildViewModel(asset);

        Assert.Contains("float", vm.KnownTypeNames);
        Assert.Contains("int",   vm.KnownTypeNames);
        Assert.Contains("Vector3", vm.KnownTypeNames);
    }
}

using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

public sealed class BlackboardVariableWiringTests
{
    // ---- BB-1b-05: SubElementKind.BlackboardVariable ----

    [Fact]
    public void SubElementKind_has_BlackboardVariable_value()
    {
        Assert.True(Enum.IsDefined(typeof(SubElementKind), SubElementKind.BlackboardVariable));
    }

    [Fact]
    public void SubElementKind_BlackboardField_still_exists()
    {
        Assert.True(Enum.IsDefined(typeof(SubElementKind), SubElementKind.BlackboardField));
    }

    [Fact]
    public void SubElementKind_BlackboardVariable_is_distinct_from_BlackboardField()
    {
        Assert.NotEqual((int)SubElementKind.BlackboardField, (int)SubElementKind.BlackboardVariable);
    }

    // ---- BB-1b-05: BlackboardVariableEntry record ----

    [Fact]
    public void BlackboardVariableEntry_stores_name_and_type()
    {
        var entry = new BlackboardVariableEntry("speed", typeof(float), null);

        Assert.Equal("speed",        entry.Name);
        Assert.Equal(typeof(float),  entry.FieldType);
        Assert.Null(entry.Comment);
    }

    [Fact]
    public void BlackboardVariableEntry_stores_non_null_comment()
    {
        var entry = new BlackboardVariableEntry("count", typeof(int), "The number of targets.");

        Assert.Equal("The number of targets.", entry.Comment);
    }

    [Fact]
    public void BlackboardVariableEntry_equality_is_value_based()
    {
        var a = new BlackboardVariableEntry("hp", typeof(int), null);
        var b = new BlackboardVariableEntry("hp", typeof(int), null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void BlackboardVariableEntry_inequality_on_different_name()
    {
        var a = new BlackboardVariableEntry("hp", typeof(int), null);
        var b = new BlackboardVariableEntry("mp", typeof(int), null);

        Assert.NotEqual(a, b);
    }

    // ---- BB-1b-05: IBlackboardManagedAsset interface ----

    private sealed class StubManagedAsset : IBlackboardManagedAsset
    {
        public bool IsBlackboardEditorManaged { get; set; }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; set; }
            = Array.Empty<BlackboardVariableEntry>();

        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void UpdateVariableComment(string name, string? comment) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName) => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }

    [Fact]
    public void IBlackboardManagedAsset_default_values_are_safe()
    {
        var stub = new StubManagedAsset();

        Assert.False(stub.IsBlackboardEditorManaged);
        Assert.Empty(stub.BlackboardVariables);
    }

    [Fact]
    public void IBlackboardManagedAsset_managed_flag_can_be_set()
    {
        var stub = new StubManagedAsset { IsBlackboardEditorManaged = true };

        Assert.True(stub.IsBlackboardEditorManaged);
    }

    [Fact]
    public void IBlackboardManagedAsset_variables_round_trip()
    {
        var vars = new[]
        {
            new BlackboardVariableEntry("x", typeof(float), null),
            new BlackboardVariableEntry("y", typeof(float), null),
        };
        var stub = new StubManagedAsset { BlackboardVariables = vars };

        Assert.Equal(2, stub.BlackboardVariables.Count);
        Assert.Equal("x", stub.BlackboardVariables[0].Name);
        Assert.Equal("y", stub.BlackboardVariables[1].Name);
    }
}

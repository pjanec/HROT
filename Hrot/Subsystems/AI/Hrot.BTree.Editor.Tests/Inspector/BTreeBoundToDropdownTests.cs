using System;
using System.Collections.Generic;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// Tests for IBTreeSyncableAsset.GetVariablesOfType -- the "Bound to" dropdown
/// data source that filters master blackboard variables by display type name (1e-02).
/// </summary>
public sealed class BTreeBoundToDropdownTests
{
    // ---- Helpers ----

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "T",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(Guid.NewGuid(), "Host", "/Host.cs", true, "BB", "Ctx", EmptyBlob());

    // ---- Tests ----

    // T1: GetVariablesOfType returns only matching variables
    [Fact]
    public void GetVariablesOfType_ReturnsOnlyMatchingType()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("Speed",   typeof(int),   null));
        asset.AddVariable(new BlackboardVariableEntry("Health",  typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("Count",   typeof(int),   null));

        var result = asset.GetVariablesOfType("int");

        result.Should().HaveCount(2);
        result.Should().Contain(v => v.Name == "Speed");
        result.Should().Contain(v => v.Name == "Count");
        result.Should().NotContain(v => v.Name == "Health");
    }

    // T2: Returns empty list when no variables of that type
    [Fact]
    public void GetVariablesOfType_ReturnsEmpty_WhenNoMatch()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("Speed",  typeof(float), null));

        asset.GetVariablesOfType("int").Should().BeEmpty();
    }

    // T3: Returns empty list when asset has no variables at all
    [Fact]
    public void GetVariablesOfType_ReturnsEmpty_WhenNoVariables()
    {
        var asset = MakeAsset();
        asset.GetVariablesOfType("int").Should().BeEmpty();
    }

    // T4: float display name matches correctly
    [Fact]
    public void GetVariablesOfType_Float_ReturnsFloatVariables()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("X", typeof(float), null));

        var result = asset.GetVariablesOfType("float");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("X");
    }

    // T5: CLR type name "Int32" does NOT match int variable (display name is "int")
    [Fact]
    public void GetVariablesOfType_Int32_DoesNotMatchIntVariables()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("Count", typeof(int), null));

        // "Int32" is the CLR name; display name of typeof(int) is "int"
        asset.GetVariablesOfType("Int32").Should().BeEmpty();
    }

    // T6: "Single" (CLR name of float) does NOT match float variable
    [Fact]
    public void GetVariablesOfType_Single_DoesNotMatchFloatVariables()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("Speed", typeof(float), null));

        // CLR name "Single" should not match; display name is "float"
        asset.GetVariablesOfType("Single").Should().BeEmpty();
    }

    // T7: Unknown type name returns empty
    [Fact]
    public void GetVariablesOfType_UnknownTypeName_ReturnsEmpty()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("X", typeof(int), null));

        asset.GetVariablesOfType("Vector99").Should().BeEmpty();
    }

    // T8: GetVariablesOfType is case-sensitive
    [Fact]
    public void GetVariablesOfType_IsCaseSensitive()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("X", typeof(int), null));

        // "INT" (uppercase) should not match "int"
        asset.GetVariablesOfType("INT").Should().BeEmpty();
    }
}

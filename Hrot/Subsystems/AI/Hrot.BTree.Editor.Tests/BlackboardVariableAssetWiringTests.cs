using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BlackboardVariableAssetWiringTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(),
            "TestTree",
            "/trees/TestTree.cs",
            true,
            "MyBlackboard",
            "MyContext",
            EmptyBlob());

    // ---- BB-1b-05: BehaviorTreeAsset implements IBlackboardManagedAsset ----

    [Fact]
    public void BehaviorTreeAsset_implements_IBlackboardManagedAsset()
    {
        var asset = MakeAsset();
        asset.Should().BeAssignableTo<IBlackboardManagedAsset>();
    }

    [Fact]
    public void IsBlackboardEditorManaged_defaults_to_false()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged.Should().BeFalse();
    }

    [Fact]
    public void IsBlackboardEditorManaged_can_be_set()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.IsBlackboardEditorManaged.Should().BeTrue();
    }

    [Fact]
    public void BlackboardVariables_is_empty_initially()
    {
        var asset = MakeAsset();
        asset.BlackboardVariables.Should().BeEmpty();
    }

    [Fact]
    public void SetBlackboardVariables_stores_variables_in_declaration_order()
    {
        var asset = MakeAsset();
        var vars = new[]
        {
            new BlackboardVariableEntry("speed", typeof(float),  null),
            new BlackboardVariableEntry("count", typeof(int),    null),
            new BlackboardVariableEntry("alive", typeof(bool),   "Is the unit alive?"),
        };

        asset.SetBlackboardVariables(vars);

        asset.BlackboardVariables.Should().HaveCount(3);
        asset.BlackboardVariables.Select(v => v.Name)
            .Should().ContainInOrder("speed", "count", "alive");
    }

    [Fact]
    public void SetBlackboardVariables_marks_asset_dirty()
    {
        var asset = MakeAsset();

        asset.SetBlackboardVariables(new[] { new BlackboardVariableEntry("x", typeof(float), null) });

        asset.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void SetBlackboardVariables_fires_Changed_event()
    {
        var asset   = MakeAsset();
        var raised  = false;
        asset.Changed += () => raised = true;

        asset.SetBlackboardVariables(new[] { new BlackboardVariableEntry("x", typeof(float), null) });

        raised.Should().BeTrue();
    }

    [Fact]
    public void SetBlackboardVariables_replaces_previous_list()
    {
        var asset = MakeAsset();
        asset.SetBlackboardVariables(new[] { new BlackboardVariableEntry("old", typeof(float), null) });

        asset.SetBlackboardVariables(new[] { new BlackboardVariableEntry("new", typeof(int), null) });

        asset.BlackboardVariables.Should().HaveCount(1);
        asset.BlackboardVariables[0].Name.Should().Be("new");
    }

    [Fact]
    public void SetBlackboardVariables_with_empty_list_clears_variables()
    {
        var asset = MakeAsset();
        asset.SetBlackboardVariables(new[] { new BlackboardVariableEntry("x", typeof(float), null) });

        asset.SetBlackboardVariables(Array.Empty<BlackboardVariableEntry>());

        asset.BlackboardVariables.Should().BeEmpty();
    }

    [Fact]
    public void IBlackboardManagedAsset_interface_accessible_through_cast()
    {
        var asset = MakeAsset() as IBlackboardManagedAsset;
        asset.Should().NotBeNull();

        var entry = new BlackboardVariableEntry("hp", typeof(int), null);
        ((BehaviorTreeAsset)asset!).SetBlackboardVariables(new[] { entry });

        asset.BlackboardVariables.Should().HaveCount(1);
        asset.BlackboardVariables[0].Name.Should().Be("hp");
    }

    // ---- RemoveVariables (batch removal, 1f-04) ----

    [Fact]
    public void RemoveVariables_RemovesNamedVars_OnBehaviorTreeAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("a", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("b", typeof(int), null));
        asset.AddVariable(new BlackboardVariableEntry("c", typeof(bool), null));

        asset.RemoveVariables(new[] { "a", "c" });

        asset.BlackboardVariables.Should().HaveCount(1);
        asset.BlackboardVariables[0].Name.Should().Be("b");
    }

    [Fact]
    public void RemoveVariables_FiresChangedOnce_OnBehaviorTreeAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        asset.AddVariable(new BlackboardVariableEntry("y", typeof(float), null));
        int count = 0;
        asset.Changed += () => count++;

        asset.RemoveVariables(new[] { "x", "y" });

        count.Should().Be(1);
    }

    [Fact]
    public void RemoveVariables_EmptyList_DoesNotFireChanged_OnBehaviorTreeAsset()
    {
        var asset = MakeAsset();
        asset.AddVariable(new BlackboardVariableEntry("x", typeof(float), null));
        int count = 0;
        asset.Changed += () => count++;

        asset.RemoveVariables(Array.Empty<string>());

        count.Should().Be(0);
        asset.BlackboardVariables.Should().HaveCount(1);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Fbt;
using FluentAssertions;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.References;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BlackboardRenameTests
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

    // ---- TASK-BB-1b-03: RenameVariable on model -----------------------------

    [Fact]
    public void RenameVariable_updates_name_in_list()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("speed", typeof(float), null),
        });

        asset.RenameVariable("speed", "velocity");

        asset.BlackboardVariables.Single().Name.Should().Be("velocity");
    }

    [Fact]
    public void RenameVariable_fires_Changed()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("hp", typeof(int), null),
        });

        int count = 0;
        asset.Changed += () => count++;

        asset.RenameVariable("hp", "health");

        count.Should().Be(1);
    }

    [Fact]
    public void RenameVariable_unknown_name_is_noop_no_exception()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("x", typeof(float), null),
        });
        int count = 0;
        asset.Changed += () => count++;

        asset.RenameVariable("nonexistent", "y");

        asset.BlackboardVariables.Single().Name.Should().Be("x");
        count.Should().Be(0);
    }

    // ---- TASK-BB-1b-03c: Catalog contributor key format --------------------

    [Fact]
    public void BTreeBlackboardVariableContributor_key_uses_double_colon_delimiter()
    {
        var asset = MakeAsset();
        var assetId = asset.AssetId;
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("speed", typeof(float), null),
        });

        var contributor = new BTreeBlackboardVariableContributor();
        var elements = contributor.EnumerateElements(asset);

        elements.Should().HaveCount(1);
        string expectedKey = $"{assetId:D}::speed";
        elements[0].Key.Should().Be(expectedKey);
    }

    [Fact]
    public void BTreeBlackboardVariableContributor_element_kind_is_BlackboardVariable()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("hp", typeof(int), null),
        });

        var contributor = new BTreeBlackboardVariableContributor();
        var elements = contributor.EnumerateElements(asset);

        elements[0].Kind.Should().Be(SubElementKind.BlackboardVariable);
    }

    [Fact]
    public void BTreeBlackboardVariableContributor_returns_empty_when_not_managed()
    {
        var asset = MakeAsset();
        // IsBlackboardEditorManaged stays false (default)

        var contributor = new BTreeBlackboardVariableContributor();
        var elements = contributor.EnumerateElements(asset);

        elements.Should().BeEmpty();
    }

    [Fact]
    public void BTreeBlackboardVariableContributor_enumerates_references_from_action_nodes()
    {
        var asset = MakeAsset();
        var assetId = asset.AssetId;
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("speed", typeof(float), null),
        });

        var nodeId = Guid.NewGuid();
        var nodes = new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId     = nodeId,
                KernelType   = NodeType.Action,
                DisplayLabel = "AimAndFire",
                Action = new BTreeActionPayload
                {
                    MethodFqn            = "Combat.Actions.AimAndFire",
                    ExpressionTargetField = "speed",
                    DelegateShape        = BTreeActionDelegateShape.ThreeParamReusable,
                },
            },
        };
        asset.ReplaceAll(nodes, new List<BTreeEditorPill>(), EmptyBlob());

        var contributor = new BTreeBlackboardVariableContributor();
        var refs = contributor.EnumerateReferences(asset);

        refs.Should().HaveCount(1);
        refs[0].TargetKey.Should().Be($"{assetId:D}::speed");
        refs[0].HostElementId.Should().Be(nodeId);
        refs[0].TargetKind.Should().Be(SubElementKind.BlackboardVariable);
    }

    [Fact]
    public void BTreeBlackboardVariableContributor_skips_nodes_without_expression_target()
    {
        var asset = MakeAsset();
        asset.IsBlackboardEditorManaged = true;
        asset.SetBlackboardVariables(new[]
        {
            new BlackboardVariableEntry("x", typeof(float), null),
        });

        var nodes = new List<BTreeEditorNode>
        {
            new BTreeEditorNode
            {
                VisualId   = Guid.NewGuid(),
                KernelType = NodeType.Action,
                Action     = new BTreeActionPayload
                {
                    ExpressionTargetField = null,   // no binding
                    DelegateShape         = BTreeActionDelegateShape.FourParamFull,
                },
            },
        };
        asset.ReplaceAll(nodes, new List<BTreeEditorPill>(), EmptyBlob());

        var contributor = new BTreeBlackboardVariableContributor();
        var refs = contributor.EnumerateReferences(asset);

        refs.Should().BeEmpty();
    }
}

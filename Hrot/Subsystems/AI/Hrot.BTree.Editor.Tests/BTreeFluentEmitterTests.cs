using System;
using System.Collections.Generic;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeFluentEmitterDeterminismTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeSimpleTree()
    {
        var assetId = new Guid("a1b2c3d4-0000-0000-0000-000000000001");
        var asset = new BehaviorTreeAsset(
            assetId, "TestTree", "/trees/TestTree.cs", true,
            "Hrot.Game.BlackboardType", "Hrot.Game.ContextType",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");

        var rootId     = new Guid("11111111-0000-0000-0000-000000000001");
        var sequenceId = new Guid("22222222-0000-0000-0000-000000000001");
        var actionId   = new Guid("33333333-0000-0000-0000-000000000001");

        var root = new BTreeEditorNode
        {
            VisualId = rootId, KernelType = NodeType.Root, KernelBlobIndex = 0,
        };
        var seq = new BTreeEditorNode
        {
            VisualId = sequenceId, KernelType = NodeType.Sequence, KernelBlobIndex = 1,
        };
        var action = new BTreeEditorNode
        {
            VisualId = actionId, KernelType = NodeType.Action, KernelBlobIndex = 2,
            Action = new BTreeActionPayload
            {
                MethodFqn = "Hrot.Game.Combat.CombatActions.DoSomething",
                DelegateShape = BTreeActionDelegateShape.FourParamFull,
            },
        };

        root.ChildVisualIds.Add(sequenceId);
        seq.ChildVisualIds.Add(actionId);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(action);

        return asset;
    }

    // ── Determinism: same input => same output ──────────────────────────────

    [Fact]
    public void Emit_is_deterministic_across_two_calls()
    {
        var asset    = MakeSimpleTree();
        var emitter  = new BTreeFluentEmitter();

        string first  = emitter.Emit(asset);
        string second = emitter.Emit(asset);

        first.Should().Be(second);
    }

    [Fact]
    public void Emit_contains_file_header_marker()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("HROT_EDITOR_GENERATED");
    }

    [Fact]
    public void Emit_contains_asset_id()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain(asset.AssetId.ToString("D"));
    }

    [Fact]
    public void Emit_contains_BTreeDefinition_attribute()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("[BTreeDefinition(");
    }

    [Fact]
    public void Emit_contains_BTreeLayout_attribute()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("[BTreeLayout(");
    }

    [Fact]
    public void Emit_contains_class_name_derived_from_asset_name()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("class TestTree");
    }

    [Fact]
    public void Emit_contains_CreateBuilder_method()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("CreateBuilder()");
    }

    [Fact]
    public void Emit_contains_action_method_reference()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("CombatActions.DoSomething");
    }

    [Fact]
    public void Emit_contains_correct_namespace()
    {
        var asset   = MakeSimpleTree();
        var emitter = new BTreeFluentEmitter();

        string code = emitter.Emit(asset);

        code.Should().Contain("namespace Hrot.AI.Behaviors.Trees");
    }

    [Fact]
    public void Emit_wait_node_has_float_suffix()
    {
        var assetId = new Guid("b1b2c3d4-0000-0000-0000-000000000001");
        var asset = new BehaviorTreeAsset(
            assetId, "WaitTree", "/trees/WaitTree.cs", true,
            "BB", "Ctx", EmptyBlob(), "My.NS");

        var rootId  = new Guid("10000000-0000-0000-0000-000000000001");
        var waitId  = new Guid("20000000-0000-0000-0000-000000000001");

        var root = new BTreeEditorNode { VisualId = rootId, KernelType = NodeType.Root, KernelBlobIndex = 0 };
        var wait = new BTreeEditorNode
        {
            VisualId = waitId, KernelType = NodeType.Wait, KernelBlobIndex = 1,
            Wait = new BTreeWaitPayload { Duration = 1.5f },
        };

        root.ChildVisualIds.Add(waitId);
        asset.AddNode(root);
        asset.AddNode(wait);

        var emitter = new BTreeFluentEmitter();
        string code = emitter.Emit(asset);

        code.Should().Contain("1.5f");
    }
}

// ── Roslyn parse-validity tests ──────────────────────────────────────────────

public sealed class BTreeFluentEmitterRoslynTests
{
    private static BehaviorTreeBlob EmptyBlob() => new BehaviorTreeBlob
    {
        TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
        MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
        IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
    };

    private static BehaviorTreeAsset MakeComplexTree()
    {
        var assetId = new Guid("cc000001-0000-0000-0000-000000000001");
        var asset = new BehaviorTreeAsset(
            assetId, "ComplexTree", "/trees/ComplexTree.cs", true,
            "Hrot.Game.BlackboardType", "Hrot.Game.ContextType",
            EmptyBlob(), "Hrot.AI.Behaviors.Trees");

        var rootId    = new Guid("cc100001-0000-0000-0000-000000000001");
        var seqId     = new Guid("cc200001-0000-0000-0000-000000000001");
        var action1Id = new Guid("cc300001-0000-0000-0000-000000000001");
        var selId     = new Guid("cc400001-0000-0000-0000-000000000001");
        var action2Id = new Guid("cc500001-0000-0000-0000-000000000001");
        var action3Id = new Guid("cc600001-0000-0000-0000-000000000001");

        var root  = new BTreeEditorNode { VisualId = rootId,    KernelType = NodeType.Root,     KernelBlobIndex = 0 };
        var seq   = new BTreeEditorNode { VisualId = seqId,     KernelType = NodeType.Sequence, KernelBlobIndex = 1 };
        var act1  = new BTreeEditorNode
        {
            VisualId = action1Id, KernelType = NodeType.Action, KernelBlobIndex = 2,
            Action = new BTreeActionPayload { MethodFqn = "My.NS.Actions.ActionOne", DelegateShape = BTreeActionDelegateShape.FourParamFull },
        };
        var sel   = new BTreeEditorNode { VisualId = selId,     KernelType = NodeType.Selector, KernelBlobIndex = 3 };
        var act2  = new BTreeEditorNode
        {
            VisualId = action2Id, KernelType = NodeType.Action, KernelBlobIndex = 4,
            Action = new BTreeActionPayload { MethodFqn = "My.NS.Actions.ActionTwo", DelegateShape = BTreeActionDelegateShape.FourParamFull },
        };
        var act3  = new BTreeEditorNode
        {
            VisualId = action3Id, KernelType = NodeType.Action, KernelBlobIndex = 5,
            Action = new BTreeActionPayload { MethodFqn = "My.NS.Actions.ActionThree", DelegateShape = BTreeActionDelegateShape.FourParamFull },
        };

        root.ChildVisualIds.Add(seqId);
        seq.ChildVisualIds.Add(action1Id);
        seq.ChildVisualIds.Add(selId);
        sel.ChildVisualIds.Add(action2Id);
        sel.ChildVisualIds.Add(action3Id);

        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(act1);
        asset.AddNode(sel);
        asset.AddNode(act2);
        asset.AddNode(act3);

        // Add an Inverter pill on the Sequence node to exercise composite+pill emit path.
        asset.AddPill(new BTreeEditorPill
        {
            VisualId         = new Guid("cc700001-0000-0000-0000-000000000001"),
            HostNodeVisualId = seqId,
            DecoratorType    = NodeType.Inverter,
            StackIndex       = 0,
        });

        return asset;
    }

    [Fact]
    public void EmitComposite_ComplexTreeWithPill_ProducesValidCSharp()
    {
        var asset   = MakeComplexTree();
        var emitter = new BTreeFluentEmitter();
        string code = emitter.Emit(asset);

        var diagnostics = CSharpSyntaxTree.ParseText(code).GetDiagnostics();

        diagnostics.Should().BeEmpty("the emitted C# source must parse without errors or warnings");
    }
}

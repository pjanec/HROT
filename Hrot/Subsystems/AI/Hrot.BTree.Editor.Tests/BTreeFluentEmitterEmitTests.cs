using System;
using System.Text.RegularExpressions;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Emit;
using Hrot.BTree.Editor.Model;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// BPF-018: EmitSubtree must use SubtreeName (not SubtreeAssetId Guid).
/// BPF-027: EmitComposite with multiple children must produce no stray comma.
/// </summary>
public sealed class BTreeFluentEmitterEmitTests
{
    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName = "T", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeBaseAsset(string name = "TestTree")
    {
        var assetId = Guid.NewGuid();
        return new BehaviorTreeAsset(
            assetId, name, "/trees/TestTree.cs", true,
            "Hrot.Game.BB", "Hrot.Game.Ctx",
            EmptyBlob(), "Hrot.AI.Trees");
    }

    private static BTreeEditorNode MakeRoot(Guid id) =>
        new() { VisualId = id, KernelType = NodeType.Root, KernelBlobIndex = 0 };

    private static BTreeEditorNode MakeSequence(Guid id) =>
        new() { VisualId = id, KernelType = NodeType.Sequence, KernelBlobIndex = 1 };

    private static BTreeEditorNode MakeAction(Guid id, string fqn = "Hrot.Game.Actions.DoStuff") =>
        new()
        {
            VisualId = id, KernelType = NodeType.Action, KernelBlobIndex = 2,
            Action = new BTreeActionPayload
            {
                MethodFqn = fqn,
                DelegateShape = BTreeActionDelegateShape.FourParamFull,
            },
        };

    // ── BPF-018: EmitSubtree ──────────────────────────────────────────────────

    [Fact]
    public void EmitSubtree_EmitsTreeNameNotGuid()
    {
        const string subtreeName = "PatrolTree";
        var asset = MakeBaseAsset();
        var root   = MakeRoot(Guid.NewGuid());
        var seq    = MakeSequence(Guid.NewGuid());
        var subtreeNode = new BTreeEditorNode
        {
            VisualId = Guid.NewGuid(),
            KernelType = NodeType.Subtree,
            KernelBlobIndex = 2,
            Subtree = new BTreeSubtreePayload
            {
                SubtreeName    = subtreeName,
                SubtreeAssetId = Guid.NewGuid(), // should NOT appear in emitted code
                IsResolved     = true,
            },
        };
        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(subtreeNode.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(subtreeNode);

        string code = new BTreeFluentEmitter().Emit(asset);

        code.Should().Contain($"\"{subtreeName}\"",
            because: "EmitSubtree should emit the tree name string");
        code.Should().NotMatchRegex(
            @"\""[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\"".*Subtree",
            because: "EmitSubtree must not emit the resolved Guid as the subtree argument");
    }

    [Fact]
    public void EmitSubtree_UnresolvedSubtree_EmitsEmptyString()
    {
        var asset = MakeBaseAsset();
        var root  = MakeRoot(Guid.NewGuid());
        var seq   = MakeSequence(Guid.NewGuid());
        var subtreeNode = new BTreeEditorNode
        {
            VisualId = Guid.NewGuid(), KernelType = NodeType.Subtree, KernelBlobIndex = 2,
            Subtree = null, // unresolved -> payload is null
        };
        root.ChildVisualIds.Add(seq.VisualId);
        seq.ChildVisualIds.Add(subtreeNode.VisualId);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(subtreeNode);

        string code = new BTreeFluentEmitter().Emit(asset);

        code.Should().Contain(".Subtree(\"\",",
            because: "null subtree payload emits empty string placeholder");
    }

    // ── BPF-027: EmitComposite stray comma ───────────────────────────────────

    [Fact]
    public void EmitComposite_WithTwoChildren_ProducesNoStrayComma()
    {
        var asset  = MakeBaseAsset();
        var rootId = Guid.NewGuid();
        var seqId  = Guid.NewGuid();
        var act1Id = Guid.NewGuid();
        var act2Id = Guid.NewGuid();

        var root = MakeRoot(rootId);
        var seq  = MakeSequence(seqId);
        var act1 = MakeAction(act1Id, "Hrot.Game.Actions.First");
        var act2 = MakeAction(act2Id, "Hrot.Game.Actions.Second");

        root.ChildVisualIds.Add(seqId);
        seq.ChildVisualIds.Add(act1Id);
        seq.ChildVisualIds.Add(act2Id);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(act1);
        asset.AddNode(act2);

        string code = new BTreeFluentEmitter().Emit(asset);

        // The stray comma pattern was: a semicolon (from isLast=true terminator) immediately
        // followed (with optional whitespace) by a comma on the same or next line.
        code.Should().NotMatchRegex(@";\s*,",
            because: "a semicolon followed by a comma is invalid C# and indicates a stray separator");
    }

    [Fact]
    public void EmitComposite_WithTwoChildren_ContainsStatementLambdaBrace()
    {
        var asset  = MakeBaseAsset();
        var rootId = Guid.NewGuid();
        var seqId  = Guid.NewGuid();
        var act1Id = Guid.NewGuid();
        var act2Id = Guid.NewGuid();

        var root = MakeRoot(rootId);
        var seq  = MakeSequence(seqId);
        var act1 = MakeAction(act1Id, "Hrot.Game.Actions.First");
        var act2 = MakeAction(act2Id, "Hrot.Game.Actions.Second");

        root.ChildVisualIds.Add(seqId);
        seq.ChildVisualIds.Add(act1Id);
        seq.ChildVisualIds.Add(act2Id);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(act1);
        asset.AddNode(act2);

        string code = new BTreeFluentEmitter().Emit(asset);

        // Statement lambda form opens a block with "{"
        code.Should().Contain("seq =>");
        code.Should().MatchRegex(@"seq\s*=>\s*\n\s*\{",
            because: "composite body should use a statement lambda block");
    }

    [Fact]
    public void EmitComposite_ChildrenUseReceiver_NotLeadingDot()
    {
        var asset  = MakeBaseAsset();
        var rootId = Guid.NewGuid();
        var seqId  = Guid.NewGuid();
        var act1Id = Guid.NewGuid();
        var act2Id = Guid.NewGuid();

        var root = MakeRoot(rootId);
        var seq  = MakeSequence(seqId);
        var act1 = MakeAction(act1Id, "Hrot.Game.Actions.First");
        var act2 = MakeAction(act2Id, "Hrot.Game.Actions.Second");

        root.ChildVisualIds.Add(seqId);
        seq.ChildVisualIds.Add(act1Id);
        seq.ChildVisualIds.Add(act2Id);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(act1);
        asset.AddNode(act2);

        string code = new BTreeFluentEmitter().Emit(asset);

        // Children inside the composite body should be called as seq.Action(...)
        code.Should().MatchRegex(@"seq\.Action\(",
            because: "children inside a composite statement lambda use the lambda parameter as receiver");
    }

    [Fact]
    public void EmitComposite_WithOneChild_ProducesNoStrayComma()
    {
        var asset  = MakeBaseAsset();
        var rootId = Guid.NewGuid();
        var seqId  = Guid.NewGuid();
        var actId  = Guid.NewGuid();

        var root = MakeRoot(rootId);
        var seq  = MakeSequence(seqId);
        var act  = MakeAction(actId);

        root.ChildVisualIds.Add(seqId);
        seq.ChildVisualIds.Add(actId);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(act);

        string code = new BTreeFluentEmitter().Emit(asset);

        code.Should().NotMatchRegex(@";\s*,",
            because: "single-child composite must also produce no stray comma");
    }

    [Fact]
    public void EmitComposite_NestedComposite_ProducesNoStrayComma()
    {
        var asset  = MakeBaseAsset();
        var rootId = Guid.NewGuid();
        var seqId  = Guid.NewGuid();
        var selId  = Guid.NewGuid();
        var act1Id = Guid.NewGuid();
        var act2Id = Guid.NewGuid();

        var root = MakeRoot(rootId);
        var seq  = MakeSequence(seqId);
        var sel  = new BTreeEditorNode { VisualId = selId, KernelType = NodeType.Selector, KernelBlobIndex = 3 };
        var act1 = MakeAction(act1Id, "Hrot.Game.Actions.A");
        var act2 = MakeAction(act2Id, "Hrot.Game.Actions.B");

        root.ChildVisualIds.Add(seqId);
        seq.ChildVisualIds.Add(selId);
        sel.ChildVisualIds.Add(act1Id);
        sel.ChildVisualIds.Add(act2Id);
        asset.AddNode(root);
        asset.AddNode(seq);
        asset.AddNode(sel);
        asset.AddNode(act1);
        asset.AddNode(act2);

        string code = new BTreeFluentEmitter().Emit(asset);

        code.Should().NotMatchRegex(@";\s*,",
            because: "nested composites must produce no stray comma at any depth");
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

/// <summary>
/// B-4 lifecycle tests: deleting a BTree Action/Condition node that owns an
/// auto-managed variable removes that variable from the asset.
/// </summary>
public sealed class BTreeNodeAutoVarDeleteTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

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
            Guid.NewGuid(), "TestTree", "/TestTree.cs", true,
            "BB", "Ctx", EmptyBlob());

    private static (BTreeCommandSink sink, BehaviorTreeAsset asset) MakeSink()
    {
        var asset = MakeAsset();
        var graph = new StubGraph();
        var sink  = new BTreeCommandSink(asset, graph);
        return (sink, asset);
    }

    /// <summary>
    /// Directly adds an Action node with a pre-configured Action payload to the asset,
    /// then optionally creates an auto-managed variable.
    /// Uses the internal <c>AddNode</c> method (accessible via InternalsVisibleTo).
    /// Returns the NodeId.
    /// </summary>
    private static NodeId AddActionNodeWithVar(
        BehaviorTreeAsset asset,
        string? expressionTargetField,
        bool isAutoManaged)
    {
        var visualId = Guid.NewGuid();
        var node = new BTreeEditorNode
        {
            VisualId        = visualId,
            KernelType      = NodeType.Action,
            KernelBlobIndex = -1,
            DisplayLabel    = "Action",
            Action          = new BTreeActionPayload
            {
                MethodFqn             = "Ns.TestAction",
                ExpressionTargetField = expressionTargetField,
            },
        };
        asset.AddNode(node);   // internal; accessible via InternalsVisibleTo

        if (expressionTargetField != null)
        {
            asset.AddVariable(new BlackboardVariableEntry(
                expressionTargetField,
                typeof(float),
                null,
                IsAutoManaged: isAutoManaged));
        }
        return new NodeId(visualId);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteActionNode_WithAutoManagedVar_RemovesVar()
    {
        var (sink, asset) = MakeSink();
        string varName = "_auto_actionNode1";
        var nodeId = AddActionNodeWithVar(asset, varName, isAutoManaged: true);

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeId }));

        asset.BlackboardVariables.Should().BeEmpty(
            "deleting the owning Action node must remove its auto-managed variable");
    }

    [Fact]
    public void DeleteActionNode_SharedVar_DoesNotRemoveVar()
    {
        // A shared (hand-authored, IsAutoManaged=false) variable must NOT be deleted.
        var (sink, asset) = MakeSink();
        string varName = "sharedVar";
        var nodeId = AddActionNodeWithVar(asset, varName, isAutoManaged: false);

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == varName);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeId }));

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == varName,
            "a shared (non-auto-managed) variable must NOT be deleted when its referencing node is removed");
    }

    [Fact]
    public void DeleteActionNode_NoExpressionTargetField_NoVarRemoved()
    {
        var (sink, asset) = MakeSink();
        asset.AddVariable(new BlackboardVariableEntry("unrelated", typeof(int), null));
        var nodeId = AddActionNodeWithVar(asset, expressionTargetField: null, isAutoManaged: false);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeId }));

        asset.BlackboardVariables.Should().ContainSingle(v => v.Name == "unrelated",
            "nodes without ExpressionTargetField must not affect other variables");
    }

    [Fact]
    public void DeleteActionNode_AutoManagedVar_AssetIsMarkedDirty()
    {
        // Re-pack is triggered whenever the asset is marked dirty after variable removal.
        var (sink, asset) = MakeSink();
        asset.ClearDirty();
        var nodeId = AddActionNodeWithVar(asset, "_auto_x", isAutoManaged: true);
        asset.ClearDirty();

        sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeId }));

        asset.IsDirty.Should().BeTrue(
            "removing an auto-managed variable must mark the asset dirty (triggers re-pack on next BuildViewModel)");
    }

    [Fact]
    public void DeleteTwoNodes_EachWithOwnAutoVar_BothVarsRemoved()
    {
        var (sink, asset) = MakeSink();
        var id1 = AddActionNodeWithVar(asset, "_auto_n1", isAutoManaged: true);
        var id2 = AddActionNodeWithVar(asset, "_auto_n2", isAutoManaged: true);

        asset.BlackboardVariables.Should().HaveCount(2);

        sink.Apply(new GraphCommand.RemoveNodes(new[] { id1, id2 }));

        asset.BlackboardVariables.Should().BeEmpty(
            "both auto-managed variables must be removed when both nodes are deleted");
    }

    // ── Minimal stub graph ───────────────────────────────────────────────────

    private sealed class StubGraph : IGraphModel
    {
        private readonly Dictionary<PinId, IPinModel> _pins = new();

        public GraphId Id => GraphId.NewId();
        public string DisplayName => "stub";
        public GraphKindDescriptor Kind => new("stub", "stub", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

        public IPinModel?  FindPin(PinId id)   => _pins.GetValueOrDefault(id);
        public INodeModel? FindNode(NodeId id)  => null;
        public ILinkModel? FindLink(LinkId id)  => null;
    }
}

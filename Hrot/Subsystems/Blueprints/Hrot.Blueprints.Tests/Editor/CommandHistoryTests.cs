using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class CommandHistoryTests
{
    private static Graph MakeGraph() => new Graph { Id = Guid.NewGuid() };
    private static Node MakeNode()   => new BranchNode { Id = Guid.NewGuid() };

    // SC1
    [Fact]
    public void AddNode_ThenUndo_RestoresNodeCount()
    {
        var graph   = MakeGraph();
        var node    = MakeNode();
        var history = new CommandHistory();
        history.Execute(new AddNodeCommand(graph, node));
        Assert.Equal(1, graph.Nodes.Count);
        history.Undo();
        Assert.Equal(0, graph.Nodes.Count);
    }

    // SC2
    [Fact]
    public void AddNode_Undo_Redo()
    {
        var graph   = MakeGraph();
        var node    = MakeNode();
        var history = new CommandHistory();
        history.Execute(new AddNodeCommand(graph, node));
        history.Undo();
        history.Redo();
        Assert.Equal(1, graph.Nodes.Count);
    }

    // SC3
    [Fact]
    public void CommandHistory_CanUndo_CanRedo()
    {
        var graph   = MakeGraph();
        var node    = MakeNode();
        var history = new CommandHistory();
        history.Execute(new AddNodeCommand(graph, node));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    // SC4
    [Fact]
    public void CommandHistory_Execute_After_Undo_Discards_Redo()
    {
        var graph   = MakeGraph();
        var nodeA   = MakeNode();
        var nodeB   = MakeNode();
        var nodeC   = MakeNode();
        var history = new CommandHistory();
        history.Execute(new AddNodeCommand(graph, nodeA));
        history.Execute(new AddNodeCommand(graph, nodeB));
        history.Undo();  // B undone
        history.Execute(new AddNodeCommand(graph, nodeC));
        Assert.False(history.CanRedo);
    }

    // SC5
    [Fact]
    public void CommandHistory_Clear_ResetsAll()
    {
        var graph   = MakeGraph();
        var node    = MakeNode();
        var history = new CommandHistory();
        history.Execute(new AddNodeCommand(graph, node));
        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}

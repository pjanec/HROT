using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Undo for wire edits: <see cref="LinkEditCommand"/> (add/remove/replace) and
/// <see cref="DeleteNodeCommand"/> (now restores incident links) must round-trip through
/// <see cref="CommandHistory"/>. Regression for the "Undo did nothing after deleting a wire" bug —
/// link add/remove used to mutate Graph.Links directly, outside the history.
/// </summary>
public sealed class GraphCommandsUndoTests
{
    private static Link L(Guid from, Guid to)
        => new Link { FromNodeId = from, FromPinId = Guid.NewGuid(), ToNodeId = to, ToPinId = Guid.NewGuid() };

    [Fact]
    public void LinkEditCommand_Remove_Undo_RestoresLinkAtOriginalPosition()
    {
        var a = L(Guid.NewGuid(), Guid.NewGuid());
        var b = L(Guid.NewGuid(), Guid.NewGuid());
        var graph = new Graph { Links = { a, b } };

        var cmd = new LinkEditCommand(graph, new[] { a }, null, "Remove");
        cmd.Execute();
        Assert.Equal(new[] { b }, graph.Links.ToArray());

        cmd.Undo();
        Assert.Equal(new[] { a, b }, graph.Links.ToArray());   // restored at original index (order preserved)
    }

    [Fact]
    public void LinkEditCommand_Add_Undo_RemovesAddedLink()
    {
        var a = L(Guid.NewGuid(), Guid.NewGuid());
        var b = L(Guid.NewGuid(), Guid.NewGuid());
        var graph = new Graph { Links = { a } };

        var cmd = new LinkEditCommand(graph, null, new[] { b }, "Add");
        cmd.Execute();
        Assert.Contains(b, graph.Links);

        cmd.Undo();
        Assert.DoesNotContain(b, graph.Links);
        Assert.Contains(a, graph.Links);
    }

    [Fact]
    public void LinkEditCommand_Replace_Undo_RestoresOldDropsNew()
    {
        var target = Guid.NewGuid();
        var old = L(Guid.NewGuid(), target);
        var @new = L(Guid.NewGuid(), target);
        var graph = new Graph { Links = { old } };

        var cmd = new LinkEditCommand(graph, new[] { old }, new[] { @new }, "Replace");
        cmd.Execute();
        Assert.Contains(@new, graph.Links);
        Assert.DoesNotContain(old, graph.Links);

        cmd.Undo();
        Assert.Contains(old, graph.Links);
        Assert.DoesNotContain(@new, graph.Links);
    }

    [Fact]
    public void DeleteNodeCommand_Undo_RestoresNodeAndIncidentLinks()
    {
        var node = new ReturnNode { Id = Guid.NewGuid() };
        var other = Guid.NewGuid();
        var incident  = L(other, node.Id);        // wired into the node
        var unrelated = L(other, Guid.NewGuid());
        var graph = new Graph { Nodes = { node }, Links = { incident, unrelated } };

        var cmd = new DeleteNodeCommand(graph, node);
        cmd.Execute();
        Assert.DoesNotContain(node, graph.Nodes);
        Assert.DoesNotContain(incident, graph.Links);
        Assert.Contains(unrelated, graph.Links);

        cmd.Undo();
        Assert.Contains(node, graph.Nodes);
        Assert.Contains(incident, graph.Links);   // wire restored with the node
    }

    [Fact]
    public void CommandHistory_LinkRemove_UndoRedo()
    {
        var a = L(Guid.NewGuid(), Guid.NewGuid());
        var graph = new Graph { Links = { a } };
        var history = new CommandHistory();

        history.Execute(new LinkEditCommand(graph, new[] { a }, null, "Remove"));
        Assert.Empty(graph.Links);

        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Contains(a, graph.Links);           // Ctrl-Z restores the deleted wire

        Assert.True(history.CanRedo);
        history.Redo();
        Assert.Empty(graph.Links);
    }
}

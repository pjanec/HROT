using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Commands;

public class UndoStackTests
{
    private sealed class FakeSink : IGraphCommandSink
    {
        public List<GraphCommand> Log { get; } = new();
        public bool NextFails { get; set; }

        public GraphCommandResult Apply(GraphCommand command)
        {
            if (NextFails) return new GraphCommandResult(false, "forced");
            Log.Add(command);
            return new GraphCommandResult(true, null);
        }
    }

    [Fact]
    public void ApplyAndRecord_Then_Undo_AppliesInverse()
    {
        var sink = new FakeSink();
        var stack = new UndoStack(sink);

        var nodeId = NodeId.NewId();
        var forward = new GraphCommand.RemoveNodes(new[] { nodeId });
        var inverse = new GraphCommand.MoveNodes(Array.Empty<NodeMove>());

        stack.ApplyAndRecord(forward, inverse, "test");
        sink.Log.Count.Should().Be(1);

        stack.Undo();
        // Undo applies the inverse
        sink.Log.Count.Should().Be(2);
        sink.Log[1].Should().Be(inverse);
    }

    [Fact]
    public void Undo_Then_Redo_ReappliesForward()
    {
        var sink = new FakeSink();
        var stack = new UndoStack(sink);

        var nodeId = NodeId.NewId();
        var forward = new GraphCommand.RemoveNodes(new[] { nodeId });
        var inverse = new GraphCommand.MoveNodes(Array.Empty<NodeMove>());

        stack.ApplyAndRecord(forward, inverse, "test");
        stack.Undo();
        sink.Log.Clear();

        stack.Redo();
        sink.Log.Count.Should().Be(1);
        sink.Log[0].Should().Be(forward);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var sink = new FakeSink();
        var stack = new UndoStack(sink);

        var nodeId = NodeId.NewId();
        var forward = new GraphCommand.RemoveNodes(new[] { nodeId });
        var inverse = new GraphCommand.MoveNodes(Array.Empty<NodeMove>());

        stack.ApplyAndRecord(forward, inverse, "op1");
        stack.ApplyAndRecord(forward, inverse, "op2");
        stack.Undo();

        stack.CanUndo.Should().BeTrue();
        stack.CanRedo.Should().BeTrue();

        stack.Clear();
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
        stack.UndoCount.Should().Be(0);
        stack.RedoCount.Should().Be(0);
    }

    [Fact]
    public void MaxEntries_Trims_OldestEntries()
    {
        var sink = new FakeSink();
        var stack = new UndoStack(sink, maxEntries: 3);

        var nodeId = NodeId.NewId();
        var forward = new GraphCommand.RemoveNodes(new[] { nodeId });
        var inverse = new GraphCommand.MoveNodes(Array.Empty<NodeMove>());

        // Push 5 entries
        for (int i = 0; i < 5; i++)
            stack.ApplyAndRecord(forward, inverse, $"op{i}");

        // Should be trimmed to 3
        stack.UndoCount.Should().Be(3);
        // Most recent label should be op4
        stack.UndoLabel.Should().Be("op4");
    }
}

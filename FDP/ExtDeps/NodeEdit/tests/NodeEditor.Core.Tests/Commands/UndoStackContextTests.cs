using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Commands;

/// <summary>
/// BP-24 — the optional per-entry editing context (<see cref="UndoStack.ContextProvider"/> /
/// <see cref="UndoStack.ContextRestorer"/>).
///
/// <para>
/// The host's sink can retarget between graphs while the stack stays fixed, so each entry
/// captures the context it was recorded in and the stack restores that context before replaying.
/// The restorer must run <b>before</b> the sink applies — restoring after would mutate the wrong
/// graph first and switch second.
/// </para>
/// </summary>
public class UndoStackContextTests
{
    private sealed class FakeSink : IGraphCommandSink
    {
        public List<string> Trace { get; } = new();

        public GraphCommandResult Apply(GraphCommand command)
        {
            Trace.Add("apply");
            return new GraphCommandResult(true, null);
        }
    }

    private static (GraphCommand fwd, GraphCommand inv) SomeCommands()
        => (new GraphCommand.RemoveNodes(new[] { NodeId.NewId() }),
            new GraphCommand.MoveNodes(Array.Empty<NodeMove>()));

    [Fact]
    public void Undo_RestoresTheEntrysContext_WhenItDiffers_BeforeApplying()
    {
        var sink  = new FakeSink();
        var stack = new UndoStack(sink);

        object current = "graphA";
        stack.ContextProvider = () => current;
        stack.ContextRestorer = ctx => { sink.Trace.Add($"restore:{ctx}"); current = ctx!; };

        var (fwd, inv) = SomeCommands();
        stack.ApplyAndRecord(fwd, inv, "edit in A");   // captured context: graphA

        current = "graphB";                            // the canvas switched away
        sink.Trace.Clear();

        stack.Undo().Should().BeTrue();

        sink.Trace.Should().Equal("restore:graphA", "apply");
        current.Should().Be("graphA");
    }

    [Fact]
    public void Undo_DoesNotInvokeTheRestorer_WhenTheContextMatches()
    {
        var sink  = new FakeSink();
        var stack = new UndoStack(sink);

        stack.ContextProvider = () => "graphA";
        var restored = false;
        stack.ContextRestorer = _ => restored = true;

        var (fwd, inv) = SomeCommands();
        stack.ApplyAndRecord(fwd, inv, "edit");
        stack.Undo();

        restored.Should().BeFalse();
    }

    [Fact]
    public void Redo_RestoresTheContextToo()
    {
        var sink  = new FakeSink();
        var stack = new UndoStack(sink);

        object current = "graphA";
        stack.ContextProvider = () => current;
        stack.ContextRestorer = ctx => current = ctx!;

        var (fwd, inv) = SomeCommands();
        stack.ApplyAndRecord(fwd, inv, "edit in A");
        stack.Undo();

        current = "graphB";
        stack.Redo().Should().BeTrue();

        current.Should().Be("graphA");
    }

    [Fact]
    public void EntriesRecordedWithoutHooks_NeverTriggerRestoration()
    {
        var sink  = new FakeSink();
        var stack = new UndoStack(sink);

        var (fwd, inv) = SomeCommands();
        stack.ApplyAndRecord(fwd, inv, "recorded before hooks were set");   // context: null

        var restored = false;
        stack.ContextProvider = () => "graphB";
        stack.ContextRestorer = _ => restored = true;

        stack.Undo();

        // A null context means "no opinion", not "the default context" — restoring to null
        // would be a spurious switch.
        restored.Should().BeFalse();
    }

    [Fact]
    public void ContextsAreComparedByValue_NotReference()
    {
        var sink  = new FakeSink();
        var stack = new UndoStack(sink);

        // Boxed Guids: a reference comparison would always differ and always "restore".
        var graphA = Guid.NewGuid();
        stack.ContextProvider = () => graphA;
        var restored = false;
        stack.ContextRestorer = _ => restored = true;

        var (fwd, inv) = SomeCommands();
        stack.ApplyAndRecord(fwd, inv, "edit");
        stack.Undo();

        restored.Should().BeFalse();
    }
}

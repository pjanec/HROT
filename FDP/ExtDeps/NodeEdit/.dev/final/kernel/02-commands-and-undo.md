# Kernel 02 — Commands and Undo

All editor mutations are commands. The editor never mutates host data
directly. The host applies commands via `IGraphCommandSink` and produces
durable changes; the editor produces inverse commands for undo.

---

## File: `NodeEditor.Core/Commands/GraphCommand.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Commands;

/// <summary>
/// Base class for all editor-initiated mutations. Discriminated by sealed
/// records. The host's <c>IGraphCommandSink</c> pattern-matches on these
/// to apply to its data store.
/// </summary>
public abstract record GraphCommand
{
    /// <summary>Move a set of nodes to new positions. One Batch entry per drag gesture.</summary>
    public sealed record MoveNodes(IReadOnlyList<NodeMove> Moves) : GraphCommand;

    /// <summary>Create a new node.</summary>
    public sealed record AddNode(
        NodeKindKey Kind,
        Vector2 Position,
        IReadOnlyDictionary<string, object?>? InitialProperties) : GraphCommand;

    /// <summary>Remove nodes (and their incident links). Host handles link cleanup.</summary>
    public sealed record RemoveNodes(IReadOnlyList<NodeId> Nodes) : GraphCommand;

    /// <summary>Create a new link between two pins.</summary>
    public sealed record AddLink(PinId From, PinId To) : GraphCommand;

    /// <summary>Remove specific links by id.</summary>
    public sealed record RemoveLinks(IReadOnlyList<LinkId> Links) : GraphCommand;

    /// <summary>Re-point one end of an existing link to a new pin.</summary>
    public sealed record ReplaceLinkEndpoint(
        LinkId Link,
        LinkEndpoint Endpoint,
        PinId NewPin) : GraphCommand;

    /// <summary>Set the default value on an input data pin.</summary>
    public sealed record SetPinDefault(PinId Pin, object? NewValue) : GraphCommand;

    /// <summary>Set a host-defined property on a node (used by Details panel).</summary>
    public sealed record SetNodeProperty(NodeId Node, string Key, object? Value) : GraphCommand;

    /// <summary>Toggle node collapsed state.</summary>
    public sealed record SetNodeCollapsed(NodeId Node, bool Collapsed) : GraphCommand;

    /// <summary>Toggle advanced-pins shown state on a node.</summary>
    public sealed record SetNodeAdvancedShown(NodeId Node, bool Shown) : GraphCommand;

    /// <summary>Toggle whether a node is disabled.</summary>
    public sealed record SetNodeDisabled(NodeId Node, bool Disabled) : GraphCommand;

    /// <summary>Add a comment box.</summary>
    public sealed record AddComment(
        string Text,
        Vector2 Position,
        Vector2 Size,
        Vector4 Color,
        bool MoveWithContents) : GraphCommand;

    /// <summary>Update one or more properties of a comment.</summary>
    public sealed record UpdateComment(
        CommentId Id,
        string? Text,
        Vector2? Position,
        Vector2? Size,
        Vector4? Color,
        int? ZOrder,
        bool? MoveWithContents) : GraphCommand;

    /// <summary>Remove a comment.</summary>
    public sealed record RemoveComment(CommentId Id) : GraphCommand;

    /// <summary>Insert a reroute waypoint into a link at the given canvas position.</summary>
    public sealed record InsertReroute(LinkId Link, Vector2 Position) : GraphCommand;

    /// <summary>Move an existing reroute waypoint.</summary>
    public sealed record MoveReroute(LinkId Link, int WaypointIndex, Vector2 NewPosition) : GraphCommand;

    /// <summary>Remove a reroute waypoint.</summary>
    public sealed record RemoveReroute(LinkId Link, int WaypointIndex) : GraphCommand;

    /// <summary>Promote a pin's current default value to a variable.</summary>
    public sealed record PromoteToVariable(
        PinId Pin,
        string VariableName,
        bool IsLocal,
        string? CategoryPath) : GraphCommand;

    /// <summary>Refactor: collapse a selection of nodes into a function call.</summary>
    public sealed record CollapseToFunction(
        IReadOnlyList<NodeId> Nodes,
        string FunctionName,
        bool Pure,
        string? CategoryPath) : GraphCommand;

    /// <summary>Refactor: collapse a selection of nodes into a macro call.</summary>
    public sealed record CollapseToMacro(
        IReadOnlyList<NodeId> Nodes,
        string MacroName,
        string? CategoryPath) : GraphCommand;

    /// <summary>Refactor: collapse a selection of nodes inside a comment box.</summary>
    public sealed record CollapseToComment(
        IReadOnlyList<NodeId> Nodes,
        string CommentText) : GraphCommand;

    /// <summary>Refactor: expand a function/macro call node, inlining its body.</summary>
    public sealed record ExpandNode(NodeId Node) : GraphCommand;

    /// <summary>Multi-step command. The host should treat the contents atomically.</summary>
    public sealed record Batch(string Label, IReadOnlyList<GraphCommand> Commands) : GraphCommand;
}

/// <summary>One element of a multi-node move.</summary>
public readonly record struct NodeMove(NodeId Node, Vector2 NewPosition);

/// <summary>Which end of a link an operation refers to.</summary>
public enum LinkEndpoint
{
    /// <summary>The "from" / output end.</summary>
    Source,

    /// <summary>The "to" / input end.</summary>
    Target,
}
```

---

## File: `NodeEditor.Core/Commands/UndoStack.cs`

```csharp
using NodeEditor.Core.Interfaces;

namespace NodeEditor.Core.Commands;

/// <summary>
/// Undo/redo stack for editor commands. Owned by the editor; not the host.
///
/// The editor produces inverse commands by snapshotting affected state
/// BEFORE applying mutation. The host's command sink is consulted to apply
/// the inverse on undo/redo.
///
/// Important semantics:
/// <list type="bullet">
///   <item>One user action = one stack entry. Multi-step ops wrap in <c>Batch</c>.</item>
///   <item>Transient drag updates do not push entries; only the final committed move does.</item>
///   <item>On wholesale external change the stack is cleared.</item>
/// </list>
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();
    private readonly IGraphCommandSink _sink;
    private readonly int _maxEntries;

    /// <summary>Create an undo stack feeding mutations to the given sink.</summary>
    public UndoStack(IGraphCommandSink sink, int maxEntries = 256)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _maxEntries = maxEntries;
    }

    /// <summary>Total entries in undo direction.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>Total entries in redo direction.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>True if an undo is available.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True if a redo is available.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Label of the next undoable action, or null if none.</summary>
    public string? UndoLabel => _undo.TryPeek(out var e) ? e.Label : null;

    /// <summary>Label of the next redoable action, or null if none.</summary>
    public string? RedoLabel => _redo.TryPeek(out var e) ? e.Label : null;

    /// <summary>
    /// Apply a command via the sink, recording an inverse for undo.
    /// The caller supplies the inverse explicitly because only it knows
    /// the pre-mutation state.
    /// </summary>
    public GraphCommandResult ApplyAndRecord(
        GraphCommand forward,
        GraphCommand inverse,
        string label)
    {
        var result = _sink.Apply(forward);
        if (!result.Success) return result;

        _undo.Push(new UndoEntry(label, forward, inverse));
        _redo.Clear();
        TrimToMax();
        return result;
    }

    /// <summary>Pop the undo stack, apply the inverse, push to redo.</summary>
    public bool Undo()
    {
        if (!_undo.TryPop(out var entry)) return false;
        var r = _sink.Apply(entry.Inverse);
        if (!r.Success)
        {
            // Restore stack to avoid loss; the inverse couldn't apply.
            _undo.Push(entry);
            return false;
        }

        _redo.Push(entry);
        return true;
    }

    /// <summary>Pop the redo stack, re-apply the forward, push to undo.</summary>
    public bool Redo()
    {
        if (!_redo.TryPop(out var entry)) return false;
        var r = _sink.Apply(entry.Forward);
        if (!r.Success)
        {
            _redo.Push(entry);
            return false;
        }

        _undo.Push(entry);
        return true;
    }

    /// <summary>Clear both stacks. Called when external wholesale change occurs.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void TrimToMax()
    {
        if (_undo.Count <= _maxEntries) return;

        // Pop from the bottom of the stack (oldest). Stack<T> has no API
        // for this; rebuild via reverse.
        var keep = new Stack<UndoEntry>(_maxEntries);
        var arr = _undo.ToArray();              // top-first
        for (int i = 0; i < _maxEntries; i++)
            keep.Push(arr[_maxEntries - 1 - i]); // restore top-first order

        _undo.Clear();
        foreach (var e in keep.Reverse()) _undo.Push(e);
    }

    private readonly record struct UndoEntry(string Label, GraphCommand Forward, GraphCommand Inverse);
}
```

---

## Notes on inverse-generation

For each forward command, the editor's command-issuer must build the
inverse before applying. The inverse-generation logic lives in command
construction helpers (typically in `GraphView` or a `CommandBuilder`).

Examples:

| Forward | Inverse |
|---|---|
| `AddNode(kind, pos)` | `RemoveNodes([newId])` (newId returned by sink result via a result payload — host contract extension required) |
| `RemoveNodes([n1, n2])` | `AddNode` for each + `AddLink` for each removed incident link |
| `MoveNodes(moves)` | `MoveNodes` with original positions |
| `SetPinDefault(pin, newVal)` | `SetPinDefault(pin, oldVal)` |
| `AddLink(from, to)` | `RemoveLinks([newLinkId])` |
| `RemoveLinks([l])` | `AddLink(from, to)` (with reroutes preserved as separate `InsertReroute` series, wrapped in Batch) |
| `Batch(label, [a, b, c])` | `Batch(label, [c.inv, b.inv, a.inv])` (reversed order) |

**Issue**: the host needs to return the newly created `NodeId` / `LinkId`
to the editor for the inverse to reference. The current `GraphCommandResult`
doesn't carry that.

**Solution**: extend `GraphCommandResult`:

```csharp
public readonly record struct GraphCommandResult(
    bool Success,
    string? Message,
    IReadOnlyDictionary<string, object?>? Payload);
```

The host populates `Payload["createdNodeId"]` after `AddNode`,
`Payload["createdLinkId"]` after `AddLink`, etc. The editor reads these
when building the inverse.

Document this contract in `IGraphCommandSink.Apply` xmldoc, but enforce
nothing — naive hosts that don't populate payload simply lose redo for
those commands.

Alternative: the editor pre-generates the ID and includes it in the command:

```csharp
public sealed record AddNode(
    NodeId AssignedId,     // editor-generated
    NodeKindKey Kind,
    Vector2 Position,
    IReadOnlyDictionary<string, object?>? InitialProperties) : GraphCommand;
```

**Preferred approach**: editor-generated IDs. Cleaner, no host-side
extension needed. Update the GraphCommand definitions above accordingly:

```csharp
public sealed record AddNode(
    NodeId AssignedId,
    NodeKindKey Kind,
    Vector2 Position,
    IReadOnlyDictionary<string, object?>? InitialProperties) : GraphCommand;

public sealed record AddLink(
    LinkId AssignedId,
    PinId From,
    PinId To) : GraphCommand;

public sealed record AddComment(
    CommentId AssignedId,
    string Text,
    Vector2 Position,
    Vector2 Size,
    Vector4 Color,
    bool MoveWithContents) : GraphCommand;
```

The editor calls `IdGenerator.New*Id()` to mint IDs, then includes them in
the command. Host honors the supplied ID.

**Use this revised form** in implementation. The plainer form above shows
the structure; the actually-implemented form has explicit AssignedId
fields.

---

## File: `NodeEditor.Core/Commands/CommandBuilder.cs`

```csharp
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Commands;

/// <summary>
/// Helper for building commands and their inverses from view-state snapshots.
/// Centralizes the snapshot-then-apply pattern so view-model code stays clean.
/// </summary>
public sealed class CommandBuilder
{
    private readonly IGraphModel _model;

    public CommandBuilder(IGraphModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    /// <summary>Build a forward MoveNodes command and its inverse.</summary>
    public (GraphCommand Forward, GraphCommand Inverse) MoveNodes(
        IReadOnlyList<(NodeId Id, Vector2 NewPos)> moves)
    {
        var forward = new List<NodeMove>(moves.Count);
        var inverse = new List<NodeMove>(moves.Count);
        foreach (var (id, newPos) in moves)
        {
            var node = _model.FindNode(id);
            if (node is null) continue;
            forward.Add(new NodeMove(id, newPos));
            inverse.Add(new NodeMove(id, node.Position));
        }

        return (new GraphCommand.MoveNodes(forward),
                new GraphCommand.MoveNodes(inverse));
    }

    /// <summary>Build a forward SetPinDefault and its inverse.</summary>
    public (GraphCommand Forward, GraphCommand Inverse) SetPinDefault(
        PinId pin, object? newValue)
    {
        var pinModel = _model.FindPin(pin);
        var oldValue = pinModel?.Default?.Value;
        return (new GraphCommand.SetPinDefault(pin, newValue),
                new GraphCommand.SetPinDefault(pin, oldValue));
    }

    /// <summary>Build a forward AddNode (with new id) and its inverse RemoveNodes.</summary>
    public (GraphCommand Forward, GraphCommand Inverse) AddNode(
        NodeKindKey kind,
        Vector2 position,
        IReadOnlyDictionary<string, object?>? initialProps = null)
    {
        var newId = IdGenerator.NewNodeId();
        return (new GraphCommand.AddNode(newId, kind, position, initialProps),
                new GraphCommand.RemoveNodes(new[] { newId }));
    }

    /// <summary>Build a forward AddLink (with new id) and its inverse RemoveLinks.</summary>
    public (GraphCommand Forward, GraphCommand Inverse) AddLink(PinId from, PinId to)
    {
        var newId = IdGenerator.NewLinkId();
        return (new GraphCommand.AddLink(newId, from, to),
                new GraphCommand.RemoveLinks(new[] { newId }));
    }

    /// <summary>Build a Batch from a sequence of (forward, inverse) pairs.</summary>
    public (GraphCommand Forward, GraphCommand Inverse) Batch(
        string label,
        IReadOnlyList<(GraphCommand Forward, GraphCommand Inverse)> steps)
    {
        var forwards = new List<GraphCommand>(steps.Count);
        var inverses = new List<GraphCommand>(steps.Count);
        foreach (var (f, inv) in steps)
        {
            forwards.Add(f);
            inverses.Add(inv);
        }
        inverses.Reverse(); // undo in reverse order

        return (new GraphCommand.Batch(label, forwards),
                new GraphCommand.Batch(label, inverses));
    }
}
```

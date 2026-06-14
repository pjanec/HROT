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

    /// <summary>
    /// Build a forward <see cref="GraphCommand.AddAttachment"/> (with a new random id)
    /// and its inverse <see cref="GraphCommand.RemoveAttachments"/>.
    /// </summary>
    public (GraphCommand Forward, GraphCommand Inverse) AddAttachment(
        NodeId host,
        AttachmentCategory category,
        string? glyph,
        string? label,
        string? tooltip,
        int stackIndex,
        IReadOnlyDictionary<string, object?>? hostProps)
    {
        var newId = IdGenerator.NewAttachmentId();
        return (new GraphCommand.AddAttachment(newId, host, category, glyph, label, tooltip, stackIndex, hostProps),
                new GraphCommand.RemoveAttachments(new[] { newId }));
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

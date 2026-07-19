namespace Hrot.Blueprints.Core.Compiler.Ir;

public enum IrGraphKind
{
    Function,
    Event,
    AiPrimitiveMain,
    Construction,
}

public sealed record IrGraph
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public IrGraphKind Kind { get; init; }

    /// <summary>
    /// Q#14: for an Event graph, the event identity it handles (the <c>EventEntryNode.EventTypeId</c> — a
    /// typed event's FQN or a custom event's name). The emitter keys <c>BlueprintDefinition.EventHandlers</c>
    /// by this (NOT the graph name, which is a C# method-name suffix and can't hold an FQN) and reinterprets
    /// the dispatched payload as <c>global::{EventTypeFqn}</c>. Null for non-Event graphs.
    /// </summary>
    public string? EventTypeFqn { get; init; }
    public IReadOnlyList<IrField> Inputs { get; init; } = Array.Empty<IrField>();
    public IReadOnlyList<IrField> Outputs { get; init; } = Array.Empty<IrField>();
    public IReadOnlyList<IrBlock> Blocks { get; init; } = Array.Empty<IrBlock>();
    public IrBlockId Entry { get; init; }
    /// <summary>
    /// Maps every authored exec node to the probe id of its containing block
    /// (many-to-one: multiple exec nodes can share a block, all mapping to the
    /// block's SourceNodeId).  Data nodes are absent.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid> BreakpointTargets { get; init; } = new Dictionary<Guid, Guid>();
}

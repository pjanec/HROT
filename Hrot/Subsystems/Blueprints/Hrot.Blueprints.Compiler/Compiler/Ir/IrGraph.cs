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

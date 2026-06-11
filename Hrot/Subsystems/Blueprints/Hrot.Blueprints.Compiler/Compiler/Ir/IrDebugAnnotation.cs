namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrDebugAnnotation
{
    public Guid GraphId { get; init; }
    public Guid? NodeId { get; init; }
    public Guid? PinId { get; init; }
    public string? Synthesized { get; init; }
    public string? NodeKind { get; init; }
    public string? DisplayName { get; init; }
    /// <summary>
    /// Carries the authored node ID through lowering passes that synthesize new
    /// statements without a direct node association (e.g. WaitLowering_Instance).
    /// </summary>
    public Guid? OriginNodeId { get; init; }

    /// <summary>
    /// Set by Stage5_Schedule on the FIRST (entry/effect) statement of each EXEC node.
    /// <para>Used by <c>DebugProbeInsertion</c> to insert a per-node <c>NodeEnter</c> probe
    /// immediately before that statement, keyed to this node's id.  Data-dep statements
    /// produced by <c>ResolveDataPin</c> are never tagged (pure data nodes such as
    /// GetVariable, Literal, and pure FunctionCall get no probe).</para>
    /// </summary>
    public Guid? ExecEntryNodeId { get; init; }
}

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
}

namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrDebugAnnotation
{
    public Guid GraphId { get; init; }
    public Guid? NodeId { get; init; }
    public Guid? PinId { get; init; }
    public string? Synthesized { get; init; }
    public string? NodeKind { get; init; }
    public string? DisplayName { get; init; }
}

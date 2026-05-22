namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrDebugAnnotation
{
    public Guid GraphId { get; init; }
    public Guid? NodeId { get; init; }
    public Guid? PinId { get; init; }
    public string? Synthesized { get; init; }
}

namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrStatement
{
    public IrValue? ResultValue { get; init; }
    public IrOperation Operation { get; init; } = null!;
    public IrDebugAnnotation Debug { get; init; } = null!;
}

namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrTypeRef
{
    public string FullName { get; init; } = "";
    public bool IsArray { get; init; }
    public IrTypeRef? ElementType { get; init; }
    public bool IsUnmanaged { get; init; }
    public int SizeBytes { get; init; }
    public bool IsEntityHandle { get; init; }
}

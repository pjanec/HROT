namespace Hrot.Blueprints.Core.Compiler.Ir;

public sealed record IrTypeRef
{
    public string FullName { get; init; } = "";
    public bool IsArray { get; init; }
    public IrTypeRef? ElementType { get; init; }
    public bool IsUnmanaged { get; init; }
    public int SizeBytes { get; init; }
    public bool IsEntityHandle { get; init; }

    /// <summary>
    /// False when <see cref="SizeBytes"/> is a placeholder guess rather than the real type size — set for a
    /// project struct accepted via the AN2 "trust the FQN" field-type fallback (the reflection-less compiler
    /// can't know its size). Consumers that lay out state by summing field sizes (StateFields descriptor
    /// offsets) must, when ANY field is unreliable, fall back to a runtime layout query instead of the
    /// baked offsets. Default true (every known/curated type has a correct size).
    /// </summary>
    public bool SizeReliable { get; init; } = true;
}

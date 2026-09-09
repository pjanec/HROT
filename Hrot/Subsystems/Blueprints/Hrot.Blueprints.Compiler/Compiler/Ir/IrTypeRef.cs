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

    /// <summary>
    /// FC-2/LV-1 (Q#19-B) — when &gt; 0, this is a FIXED-CAPACITY LIST type: <see cref="FullName"/> is
    /// the per-class generated wrapper name (`__List_{Elem}_{N}`, `_`-prefixed so
    /// <c>TypeRefToCSharp</c> emits it as a local generated type), <see cref="ElementType"/> is the
    /// element, and <see cref="SizeBytes"/> is the computed wrapper size — carried with
    /// <see cref="SizeReliable"/> = false (review F3: the alignment heuristic over-pads composite
    /// types, so state layout must use the runtime <c>Marshal.OffsetOf</c> path). 0 = not a list.
    /// </summary>
    public int Capacity { get; init; }

    /// <summary>FC-2/LV-1 — declared initial logical length (0…<see cref="Capacity"/>), seeded by the generated <c>InitDefault</c>. Only meaningful when <see cref="Capacity"/> &gt; 0.</summary>
    public int InitialLength { get; init; }
}

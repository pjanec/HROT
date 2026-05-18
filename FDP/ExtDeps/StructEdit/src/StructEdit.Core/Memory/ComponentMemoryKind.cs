namespace StructEdit.Core.Memory;

/// <summary>
/// Describes how a component type's memory should be managed during an edit session.
/// </summary>
public enum ComponentMemoryKind
{
    /// <summary>Class or record class — managed heap reference.</summary>
    ManagedReference,

    /// <summary>Value type satisfying the <c>unmanaged</c> constraint — all fields blittable.</summary>
    UnmanagedBlittableStruct,

    /// <summary>Value type with at least one managed field — cannot be pinned.</summary>
    NonBlittableStruct,

    /// <summary>Type cannot be classified by this implementation.</summary>
    Unsupported
}

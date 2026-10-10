using System;

namespace Fbt.Kernel
{
    /// <summary>
    /// Marks a user-defined struct as a blackboard DTO type that should appear in the
    /// Add-Variable type picker in the HROT BTree and HSM editors, even before any action
    /// method references the struct. Applied to struct declarations only.
    /// The kernel ignores this attribute at runtime; it is used solely by the editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class BlackboardDtoStructAttribute : Attribute { }

    /// <summary>
    /// Annotates the first ref parameter of an action method as read-only access to the
    /// blackboard field. The kernel ignores this attribute at runtime; the editor schema
    /// exporter reads it to record the access pattern as ReadOnly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class BlackboardReadOnlyAttribute : Attribute { }

    /// <summary>
    /// Annotates the first ref parameter of an action method as read-write access to the
    /// blackboard field. The kernel ignores this attribute at runtime; the editor schema
    /// exporter reads it to record the access pattern as ReadWrite.
    /// Unannotated parameters are treated as ReadWrite by the editor (conservative default).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class BlackboardReadWriteAttribute : Attribute { }

    /// <summary>
    /// Marks a struct as a UNIT MEMORY (Q87, R-237): one instance per unit, shared by every behaviour that touches it,
    /// created on first touch with <c>new T()</c> and living as long as the unit. Declare the defaults as field
    /// initializers AND a parameterless constructor (<c>public Foo() {}</c>) — C# applies initializers only through a
    /// constructor call (CS8983 without one). Reached at runtime through <c>Fdp.Toolkit.Behavior.UnitMemory</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class UnitMemoryAttribute : Attribute { }
}

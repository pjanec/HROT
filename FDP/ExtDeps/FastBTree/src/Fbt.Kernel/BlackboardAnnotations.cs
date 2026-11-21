using System;

namespace Fbt.Kernel
{
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
}

using System;

namespace Fbt
{
    /// <summary>
    /// Applied by the Fbt.SourceGen source generator to the emitted registrar class.
    /// Used by FbtAutoDiscovery to locate the registrar via reflection at runtime.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FbtRegistrarAttribute : Attribute { }
}

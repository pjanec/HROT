using System;

namespace Fhsm.Kernel.Attributes
{
    /// <summary>
    /// Applied by the Fhsm source generator to the emitted <c>HsmActionRegistrar</c> class.
    /// Used by <c>AiHotReloadCoordinator.ScanForRegistrars</c> to locate the registrar via
    /// reflection at runtime, enabling attribute-driven hot-reload discovery.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class HsmActionRegistrarAttribute : Attribute { }
}

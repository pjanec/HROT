using System;

namespace Fhsm.Kernel.Attributes
{
    /// <summary>
    /// Marks a method as an HSM guard.
    /// Signature: bool MethodName(void* instance, void* context, ushort eventId)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class HsmGuardAttribute : Attribute
    {
        /// <summary>
        /// Unique name for this guard. If null, uses method name.
        /// </summary>
        public string? Name { get; set; }
        
        /// <summary>
        /// If true, this guard uses RNG (Architect Q4).
        /// Triggers debug-only AccessCount increment.
        /// </summary>
        public bool UsesRNG { get; set; }
    }
}

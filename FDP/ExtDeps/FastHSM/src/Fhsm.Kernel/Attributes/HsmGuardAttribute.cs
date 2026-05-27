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

        /// <summary>
        /// The DTO type associated with this HSM guard. Used by the schema exporter when the
        /// method uses void* parameters (unsafe interop) and the DTO type cannot be inferred
        /// from the parameter signature. When null, the exporter skips this method.
        /// </summary>
        public Type? DtoType { get; set; }
    }
}

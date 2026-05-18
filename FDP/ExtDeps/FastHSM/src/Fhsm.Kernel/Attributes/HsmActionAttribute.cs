using System;

namespace Fhsm.Kernel.Attributes
{
    /// <summary>
    /// Marks a method as an HSM action.
    /// Signature: void MethodName(void* instance, void* context, ushort eventId)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class HsmActionAttribute : Attribute
    {
        /// <summary>
        /// Unique name for this action. If null, uses method name.
        /// </summary>
        public string? Name { get; set; }
    }
}

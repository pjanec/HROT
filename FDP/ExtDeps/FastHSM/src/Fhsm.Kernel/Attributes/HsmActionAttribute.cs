using System;
using Fhsm.Kernel.Data;

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

        /// <summary>
        /// Output lane for AI command routing. <see cref="CommandLane.None"/> means
        /// no explicit lane -- the editor infers it from context.
        /// </summary>
        public CommandLane Lane { get; set; } = CommandLane.None;

        /// <summary>
        /// The DTO type associated with this HSM action. Used by the schema exporter when the
        /// method uses void* parameters (unsafe interop) and the DTO type cannot be inferred
        /// from the parameter signature. When null, the exporter skips this method.
        /// </summary>
        public Type? DtoType { get; set; }
    }
}
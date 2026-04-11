using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// Marks an event type with a stable ID for serialization.
    /// Required for all event types used with FdpEventBus.
    /// IDs must be unique across all event types in the application.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class EventIdAttribute : Attribute
    {
        /// <summary>
        /// Stable event type ID. Must be unique and consistent across sessions.
        /// </summary>
        public int Id { get; }

        public EventIdAttribute(int id)
        {
            if (id < 0)
                throw new ArgumentException("Event ID must be non-negative", nameof(id));
            
            Id = id;
        }
    }
}

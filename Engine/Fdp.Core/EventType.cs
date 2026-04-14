using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Fdp.Kernel
{
    /// <summary>
    /// Provides stable type IDs for event types.
    /// Each event type T must have [EventId] attribute.
    /// </summary>
    public static class EventType<T> where T : unmanaged
    {
        /// <summary>
        /// Stable event type ID from [EventId] attribute.
        /// Cached on first access.
        /// </summary>
        public static readonly int Id = EventTypeRegistry.Register<T>();
    }

    /// <summary>
    /// Internal registry for event type IDs.
    /// Validates [EventId] attributes and prevents ID collisions.
    /// </summary>
    internal static class EventTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, int> _typeToId = new();
        private static readonly ConcurrentDictionary<int, Type> _idToType = new();

        /// <summary>
        /// Registers an event type and returns its ID.
        /// Throws if [EventId] attribute is missing or ID is already used.
        /// </summary>
        public static int Register<T>()
        {
            var type = typeof(T);
            
            return _typeToId.GetOrAdd(type, t =>
            {
                // Get [EventId] attribute
                var attr = t.GetCustomAttribute<EventIdAttribute>();
                if (attr == null)
                {
                    throw new InvalidOperationException(
                        $"Event type '{t.Name}' is missing required [EventId] attribute. " +
                        "All event types must be decorated with [EventId(uniqueId)].");
                }

                int id = attr.Id;

                // Check for ID collision
                if (_idToType.TryGetValue(id, out var existingType))
                {
                    throw new InvalidOperationException(
                        $"Event ID {id} is already used by type '{existingType.Name}'. " +
                        $"Cannot register '{t.Name}' with the same ID. " +
                        "Each event type must have a unique ID.");
                }

                // Register reverse lookup
                _idToType.TryAdd(id, t);

                return id;
            });
        }

        /// <summary>
        /// Gets the type associated with an event ID.
        /// Returns null if ID is not registered.
        /// Used for debugging and deserialization.
        /// </summary>
        public static Type GetType(int id)
        {
            _idToType.TryGetValue(id, out var type);
            return type!;
        }

        /// <summary>
        /// Clears the registry. Used for testing only.
        /// </summary>
        internal static void ClearForTesting()
        {
            _typeToId.Clear();
            _idToType.Clear();
        }
    }
}

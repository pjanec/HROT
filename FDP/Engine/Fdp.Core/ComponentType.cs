using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fdp.Core
{
    /// <summary>
    /// Static registry for component types.
    /// Assigns unique IDs to each component type at first access.
    /// Thread-safe via lock for registration.
    /// </summary>
    public static class ComponentType<T> where T : unmanaged
    {
        /// <summary>
        /// Gets the unique component type ID.
        /// Assigned on first access in registration order.
        /// JIT will inline this to a constant when T is known.
        /// </summary>
        public static int ID
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ComponentTypeRegistry.GetOrRegister<T>();
        }
        
        /// <summary>
        /// Size of component in bytes.
        /// </summary>
        public static int Size => Unsafe.SizeOf<T>();
        
        /// <summary>
        /// Checks if this is a tag component (zero-size).
        /// Empty structs in C# are 1 byte, so we check for size == 1.
        /// </summary>
        public static bool IsTag => Size == 1;
    }
    
    /// <summary>
    /// Static registry for MANAGED component types (Tier 2).
    /// Uses the same ComponentTypeRegistry as unmanaged types.
    /// </summary>
    public static class ManagedComponentType<T> where T : class
    {
        /// <summary>
        /// Gets the unique component type ID.
        /// Uses the same ID space as unmanaged components.
        /// </summary>
        public static int ID
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ComponentTypeRegistry.GetOrRegisterManaged(typeof(T));
        }
        
        /// <summary>
        /// Size is reference size (IntPtr).
        /// </summary>
        public static int Size => IntPtr.Size;
        
        /// <summary>
        /// Managed types are never tags.
        /// </summary>
        public static bool IsTag => false;
    }
    
    /// <summary>
    /// Global component type registry.
    /// Tracks all registered component types and assigns IDs.
    /// </summary>
    public static class ComponentTypeRegistry
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<Type, int> _typeToId = new Dictionary<Type, int>();
        private static readonly Dictionary<int, Type> _idToType = new Dictionary<int, Type>();
        private static readonly Dictionary<int, bool> _isSnapshotable = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> _isRecordable = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> _isSaveable = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> _needsClone = new Dictionary<int, bool>();
        /// <summary>Tracks which IDs were assigned via an explicit [ComponentId] attribute.</summary>
        private static readonly HashSet<int> _explicitIds = new HashSet<int>();
        
        /// <summary>
        /// Checks if a type is a C# record (immutable by design).
        /// Records have compiler-generated EqualityContract property.
        /// </summary>
        public static bool IsRecordType(Type type)
        {
            // C# records (both record class and record struct) have EqualityContract
            return type.GetProperty("EqualityContract", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null;
        }
        
        /// <summary>
        /// Registers a component type and returns its ID.
        /// Thread-safe via lock.
        /// </summary>
        internal static int GetOrRegister<T>() where T : unmanaged
        {
            return GetOrRegisterManaged(typeof(T));
        }
        
        /// <summary>
        /// Registers a managed component type and returns its ID.
        /// Thread-safe via lock. Used for both Tier 1 and Tier 2.
        /// Public to allow external code (e.g. TkbTemplate) to resolve IDs
        /// before the component type is registered with an EntityRepository.
        /// </summary>
        public static int GetOrRegisterManaged(Type type)
        {
            lock (_lock)
            {
                // Check if already registered
                if (_typeToId.TryGetValue(type, out int existingId))
                {
                    return existingId;
                }

                // Determine component ID: explicit [ComponentId] attribute required for ALL types.
                // Now supports structs, classes, and interfaces.
                var attr = type.GetCustomAttribute<ComponentIdAttribute>();

                int id;
                if (attr != null)
                {
                    id = attr.Id;

                    // Collision detection: both types explicitly claim the same ID — programmer error.
                    if (_idToType.ContainsKey(id))
                    {
                        var occupant = _idToType[id];
                        throw new InvalidOperationException(
                            $"Component ID collision: {occupant.Name} and {type.Name} both declare [ComponentId({id})]");
                    }

                    _explicitIds.Add(id);
                }
                else
                {
                    // All component types (structs, classes, and interfaces) MUST have a
                    // [ComponentId] attribute.  Auto-assignment has been removed to ensure
                    // deterministic IDs when multiple binaries are merged into one process.
                    throw new InvalidOperationException(
                        $"Component type '{type.Name}' is missing a [ComponentId] attribute. " +
                        $"Add [ComponentId(GlobalComponentIds.YourComponent)] to '{type.FullName}' " +
                        $"and register the constant in GlobalComponentIds.cs.");
                }

                if (type.IsValueType && !type.IsEnum)
                {
                    ValidateUnmanagedLayout(type);
                }

                _typeToId[type] = id;
                _idToType[id] = type;
                _isSnapshotable[id] = true;  // Default: snapshotable
                _isRecordable[id] = true;    // Default: recordable
                _isSaveable[id] = true;      // Default: saveable
                _needsClone[id] = false;     // Default: shallow copy

                return id;
            }
        }
        
        /// <summary>
        /// Sets whether a component type should be included in snapshots.
        /// Must be called AFTER registration.
        /// </summary>
        public static void SetSnapshotable(int typeId, bool snapshotable)
        {
            lock (_lock)
            {
                if (!_idToType.ContainsKey(typeId))
                    throw new ArgumentOutOfRangeException(nameof(typeId), $"Component type ID {typeId} is not registered.");

                _isSnapshotable[typeId] = snapshotable;
            }
        }
        
        /// <summary>
        /// Checks if a component type is snapshotable.
        /// Returns true by default for registered types.
        /// </summary>
        public static bool IsSnapshotable(int typeId)
        {
            lock (_lock)
            {
                return _isSnapshotable.TryGetValue(typeId, out bool v) && v;
            }
        }
        
        /// <summary>
        /// Gets all component type IDs that are snapshotable.
        /// </summary>
        public static int[] GetSnapshotableTypeIds()
        {
            lock (_lock)
            {
                return _isSnapshotable
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToArray();
            }
        }

        /// <summary>
        /// Sets whether a component type should be included in FlightRecorder.
        /// </summary>
        public static void SetRecordable(int typeId, bool value)
        {
            lock (_lock)
            {
                if (!_idToType.ContainsKey(typeId))
                    throw new ArgumentOutOfRangeException(nameof(typeId), $"Component type ID {typeId} is not registered.");
                _isRecordable[typeId] = value;
            }
        }

        /// <summary>
        /// Checks if a component type is recordable.
        /// </summary>
        public static bool IsRecordable(int typeId)
        {
            lock (_lock)
            {
                return _isRecordable.TryGetValue(typeId, out bool v) && v;
            }
        }

        /// <summary>
        /// Gets all component type IDs that are recordable.
        /// </summary>
        public static IEnumerable<int> GetRecordableTypeIds()
        {
            lock (_lock)
            {
                return _isRecordable
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToArray(); // materialise inside the lock
            }
        }

        /// <summary>
        /// Sets whether a component type should be included in SaveGame.
        /// </summary>
        public static void SetSaveable(int typeId, bool value)
        {
            lock (_lock)
            {
                if (!_idToType.ContainsKey(typeId))
                    throw new ArgumentOutOfRangeException(nameof(typeId), $"Component type ID {typeId} is not registered.");
                _isSaveable[typeId] = value;
            }
        }

        /// <summary>
        /// Checks if a component type is saveable.
        /// </summary>
        public static bool IsSaveable(int typeId)
        {
            lock (_lock)
            {
                return _isSaveable.TryGetValue(typeId, out bool v) && v;
            }
        }

        /// <summary>
        /// Gets all component type IDs that are saveable.
        /// </summary>
        public static IEnumerable<int> GetSaveableTypeIds()
        {
            lock (_lock)
            {
                return _isSaveable
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToArray(); // materialise inside the lock
            }
        }

        /// <summary>
        /// Sets whether a component type needs deep cloning for snapshots.
        /// </summary>
        public static void SetNeedsClone(int typeId, bool value)
        {
            lock (_lock)
            {
                if (!_idToType.ContainsKey(typeId))
                    throw new ArgumentOutOfRangeException(nameof(typeId), $"Component type ID {typeId} is not registered.");
                _needsClone[typeId] = value;
            }
        }

        /// <summary>
        /// Checks if a component type needs deep cloning.
        /// </summary>
        public static bool NeedsClone(int typeId)
        {
            lock (_lock)
            {
                return _needsClone.TryGetValue(typeId, out bool v) && v;
            }
        }
        
        /// <summary>
        /// Registers a managed component type (no unmanaged constraint).
        /// Deprecated - use GetOrRegisterManaged instead.
        /// </summary>
        internal static int Register<T>(Type type)
        {
            return GetOrRegisterManaged(type);
        }
        
        /// <summary>
        /// Gets the component ID for a type (returns -1 if not registered).
        /// </summary>
        public static int GetId(Type type)
        {
            lock (_lock)
            {
                if (_typeToId.TryGetValue(type, out int id))
                    return id;
                return -1;
            }
        }
        
        /// <summary>
        /// Gets the component type for a given ID.
        /// </summary>
        public static Type? GetType(int id)
        {
            lock (_lock)
            {
                _idToType.TryGetValue(id, out var type);
                return type;
            }
        }
        
        /// <summary>
        /// Gets all registered component type IDs.
        /// </summary>
        public static int[] GetAllTypeIds()
        {
            lock (_lock)
            {
                return _idToType.Keys.ToArray();
            }
        }

        /// <summary>
        /// Gets total number of registered component types.
        /// </summary>
        public static int RegisteredCount
        {
            get
            {
                lock (_lock)
                {
                    return _typeToId.Count;
                }
            }
        }
        
        /// <summary>
        /// Clears all registrations (for testing only).
        /// WARNING: Do not use in production - only for unit tests.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _typeToId.Clear();
                _idToType.Clear();
                _isSnapshotable.Clear();
                _isRecordable.Clear();
                _isSaveable.Clear();
                _needsClone.Clear();
                _explicitIds.Clear();
            }
        }
        
        /// <summary>
        /// Returns all registered types ordered by their ID.
        /// Used for serialization to persist the ID-Type mapping.
        /// </summary>
        public static Type[] GetAllTypes()
        {
            lock (_lock)
            {
                return _idToType
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => kvp.Value)
                    .ToArray();
            }
        }

        /// <summary>
        /// Returns a snapshot of all registered component types.
        /// The list is ordered by component ID.
        /// </summary>
        public static IReadOnlyList<Type> GetAllRegistered()
        {
            lock (_lock)
            {
                return _idToType
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => kvp.Value)
                    .ToArray();
            }
        }

        /// <summary>
        /// Returns all registered component type IDs (supports sparse/non-sequential IDs).
        /// </summary>
        public static int[] GetAllIds()
        {
            lock (_lock)
            {
                return _idToType.Keys.ToArray();
            }
        }

        private static void ValidateUnmanagedLayout(Type type)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var field in fields)
            {
                if (field.FieldType != typeof(bool))
                    continue;

                var marshalAs = field.GetCustomAttribute<MarshalAsAttribute>();
                if (marshalAs != null && marshalAs.Value == UnmanagedType.I1)
                    continue;

                // CRITICAL ECS MEMORY ALIGNMENT:
                // By default, the .NET interop marshaller assumes a C# bool is a 4-byte Win32 BOOL, 
                // whereas the CLR's internal managed memory model (Unsafe.SizeOf<T>) treats it as 1 byte.
                // If this attribute is omitted, tools that dynamically evaluate physical layout using 
                // Marshal.OffsetOf()—such as the zero-allocation StructEdit memory slicer or the 
                // ComponentLayoutHasher for the Flight Recorder—will calculate incorrect byte offsets 
                // for all subsequent fields. This layout rupture leads to instant ArgumentOutOfRangeExceptions 
                // in the UI and silent memory corruption in binary serialization schemas. 
                // Enforcing UnmanagedType.I1 guarantees the interop layout and managed layout are mathematically identical.
                throw new InvalidOperationException(
                    $"CRITICAL ECS LAYOUT ERROR: The unmanaged component '{type.FullName}' contains a boolean field '{field.Name}' without an explicit 1-byte layout contract. You must decorate this field with [MarshalAs(UnmanagedType.I1)] to prevent memory alignment corruption in the Flight Recorder and StructEdit buffers.");
            }
        }
    }
}

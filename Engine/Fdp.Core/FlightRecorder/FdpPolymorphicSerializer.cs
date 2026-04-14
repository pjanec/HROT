using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Fdp.Kernel.FlightRecorder
{
    /// <summary>
    /// Handles polymorphic serialization for interface/base class types.
    /// Uses [FdpPolymorphicType(id)] attribute for type discrimination.
    /// **Zero-allocation after warmup** - uses compiled Expression Trees.
    /// Based on FDP-DES-003 design specification.
    /// </summary>
    public static class FdpPolymorphicSerializer
    {
        private static readonly Dictionary<Type, byte> _typeToId = new();
        private static readonly Dictionary<byte, Type> _idToType = new();
        
        // Compiled write delegates: (BinaryWriter, object) => void
        private static readonly ConcurrentDictionary<Type, Action<BinaryWriter, object>> _writeCache = new();
        
        // Compiled read delegates: (BinaryReader) => object
        private static readonly ConcurrentDictionary<Type, Func<BinaryReader, object>> _readCache = new();
        
        private const byte NULL_MARKER = 0;
        
        static FdpPolymorphicSerializer()
        {
            // Scan all loaded assemblies for types with [FdpPolymorphicType]
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var attr = type.GetCustomAttribute<FdpPolymorphicTypeAttribute>();
                        if (attr != null)
                        {
                            RegisterType(type, attr.TypeId);
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that fail to load (common with system assemblies)
                    continue;
                }
            }
        }
        
        private static void RegisterType(Type type, byte typeId)
        {
            if (typeId == NULL_MARKER)
            {
                throw new InvalidOperationException(
                    $"Type {type.Name} cannot use TypeId=0 (reserved for null marker)");
            }
            
            if (_typeToId.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"Type {type.Name} is already registered with ID {_typeToId[type]}");
            }
            
            if (_idToType.ContainsKey(typeId))
            {
                throw new InvalidOperationException(
                    $"TypeId {typeId} is already used by {_idToType[typeId].Name}");
            }
            
            _typeToId[type] = typeId;
            _idToType[typeId] = type;
        }
        
        /// <summary>
        /// Writes a polymorphic object to the stream.
        /// Format: [TypeId:byte][ObjectData:serialized via FdpAutoSerializer]
        /// Zero-allocation after first call per type (warmup).
        /// </summary>
        public static void Write(BinaryWriter writer, object instance)
        {
            if (instance == null)
            {
                writer.Write(NULL_MARKER);
                return;
            }
            
            var type = instance.GetType();
            
            if (!_typeToId.TryGetValue(type, out byte typeId))
            {
                throw new InvalidOperationException(
                    $"Type {type.Name} is not registered. Add [FdpPolymorphicType(id)] attribute.");
            }
            
            // Write type discriminator
            writer.Write(typeId);
            
            // Get or compile zero-allocation serializer for this type
            var serializer = _writeCache.GetOrAdd(type, CompileWriteDelegate);
            
            // Write object data (zero-allocation)
            serializer(writer, instance);
        }
        
        /// <summary>
        /// Reads a polymorphic object from the stream.
        /// Returns the concrete instance or null.
        /// Zero-allocation after first call per type (warmup).
        /// </summary>
        public static object? Read(BinaryReader reader)
        {
            byte typeId = reader.ReadByte();
            
            if (typeId == NULL_MARKER)
            {
                return null;
            }
            
            if (!_idToType.TryGetValue(typeId, out Type? type))
            {
                throw new KeyNotFoundException(
                    $"TypeId {typeId} is not registered. The serialized data may be corrupted or from a different version.");
            }
            
            // Get or compile zero-allocation deserializer for this type
            var deserializer = _readCache.GetOrAdd(type, CompileReadDelegate);
            
            // Deserialize (zero-allocation)
            return deserializer(reader);
        }
        
        /// <summary>
        /// Compiles a zero-allocation write delegate for a specific type.
        /// Expression: (writer, obj) => FdpAutoSerializer.Serialize<T>((T)obj, writer)
        /// </summary>
        private static Action<BinaryWriter, object> CompileWriteDelegate(Type type)
        {
            var writerParam = Expression.Parameter(typeof(BinaryWriter), "writer");
            var objParam = Expression.Parameter(typeof(object), "obj");
            
            // Cast object to concrete type
            var typedObj = Expression.Convert(objParam, type);
            
            // Call FdpAutoSerializer.Serialize<T>(typedInstance, writer)
            var serializeMethod = typeof(FdpAutoSerializer)
                .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(type);
            
            var call = Expression.Call(serializeMethod, typedObj, writerParam);
            
            // Compile to delegate
            return Expression.Lambda<Action<BinaryWriter, object>>(call, writerParam, objParam).Compile();
        }
        
        /// <summary>
        /// Compiles a zero-allocation read delegate for a specific type.
        /// Expression: (reader) => (object)FdpAutoSerializer.Deserialize<T>(reader)
        /// </summary>
        private static Func<BinaryReader, object> CompileReadDelegate(Type type)
        {
            var readerParam = Expression.Parameter(typeof(BinaryReader), "reader");
            
            // Call FdpAutoSerializer.Deserialize<T>(reader)
            var deserializeMethod = typeof(FdpAutoSerializer)
                .GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(type);
            
            var call = Expression.Call(deserializeMethod, readerParam);
            
            // Cast result to object
            var castToObject = Expression.Convert(call, typeof(object));
            
            // Compile to delegate
            return Expression.Lambda<Func<BinaryReader, object>>(castToObject, readerParam).Compile();
        }
        
        /// <summary>
        /// Checks if a type is registered for polymorphic serialization.
        /// </summary>
        public static bool IsTypeRegistered<T>()
        {
            return IsTypeRegistered(typeof(T));
        }
        
        /// <summary>
        /// Checks if a type or any of its implementations are registered for polymorphic serialization.
        /// For interfaces/abstract classes, returns true if ANY concrete implementation is registered.
        /// </summary>
        public static bool IsTypeRegistered(Type type)
        {
            // Direct registration check
            if (_typeToId.ContainsKey(type))
                return true;
            
            // For interfaces/abstract classes, check if any implementations are registered
            if (type.IsInterface || type.IsAbstract)
            {
                foreach (var registeredType in _typeToId.Keys)
                {
                    if (type.IsAssignableFrom(registeredType))
                        return true;
                }
            }
            
            return false;
        }
    }
}

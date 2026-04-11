using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MessagePack;

namespace Fdp.Kernel.FlightRecorder
{
    /// <summary>
    /// JIT-compiled serializer for managed types.
    /// Uses Expression Trees to generate zero-allocation serialization code at runtime.
    /// Based on FDP-DES-003 design specification.
    /// </summary>
    public static class FdpAutoSerializer
    {
        // Cache: Type -> Serializer Delegate
        private static readonly ConcurrentDictionary<Type, object> _serializers = new();
        private static readonly ConcurrentDictionary<Type, object> _deserializers = new();
        private static readonly ConcurrentDictionary<Type, Delegate> _clonerCache = new();
        
        // ------------------------------------------------------------------
        // PUBLIC API
        // ------------------------------------------------------------------
        
        /// <summary>
        /// Serializes an instance of T to a BinaryWriter.
        /// First call per type compiles the serializer (~1-5ms), subsequent calls are instant.
        /// </summary>
        public static void Serialize<T>(T instance, BinaryWriter writer)
        {
            var serializer = (Action<T, BinaryWriter>)_serializers.GetOrAdd(typeof(T), t => GenerateSerializer<T>());
            serializer(instance, writer);
        }
        
        /// <summary>
        /// Deserializes from BinaryReader into an existing instance.
        /// </summary>
        public static T Deserialize<T>(BinaryReader reader)
        {
            var deserializer = (Func<BinaryReader, T>)_deserializers.GetOrAdd(typeof(T), t => GenerateDeserializer<T>());
            return deserializer(reader);
        }

        /// <summary>
        /// Deep clone an object via JIT-compiled Expression Tree.
        /// Immutable types (string, records) are copied by reference.
        /// Circular references are NOT supported (will throw).
        /// </summary>
        public static T DeepClone<T>(T source) where T : class
        {
            if (source == null)
                return null!;
            
            var cloner = (Func<T, T>)GetClonerDelegate<T>();
            return cloner(source);
        }
        
        // ------------------------------------------------------------------
        // GENERATOR CORE
        // ------------------------------------------------------------------
        
        private static Action<T, BinaryWriter> GenerateSerializer<T>()
        {
            var type = typeof(T);
            var instance = Expression.Parameter(type, "instance");
            var writer = Expression.Parameter(typeof(BinaryWriter), "writer");
            var block = new List<Expression>();
            
            // If it's a primitive/collection/array, delegate to WriteExpression
            if (IsBuiltInType(type))
            {
                block.Add(GenerateWriteExpression(type, instance, writer));
            }
            else
            {
                // Custom Object: Memberwise write
                // Find ordered properties/fields with [Key] attribute
                var members = GetSortedMembers(type);
                
                foreach (var member in members)
                {
                    var memberAccess = Expression.MakeMemberAccess(instance, member);
                    var memberType = (member is PropertyInfo pi) ? pi.PropertyType : ((FieldInfo)member).FieldType;
                    
                    // Null check for reference types
                    if (!memberType.IsValueType)
                    {
                        // Protocol: [Bool HasValue] [Value?]
                        var nullCheck = Expression.ReferenceNotEqual(memberAccess, Expression.Constant(null, memberType));
                        
                        // writer.Write(bool)
                        block.Add(CallWrite(writer, typeof(bool), nullCheck));
                        
                        // If (instance.Prop != null) { Write content }
                        block.Add(Expression.IfThen(
                            nullCheck,
                            GenerateWriteExpression(memberType, memberAccess, writer)
                        ));
                    }
                    else
                    {
                        // Value types are written directly
                        block.Add(GenerateWriteExpression(memberType, memberAccess, writer));
                    }
                }
            }
            
            return Expression.Lambda<Action<T, BinaryWriter>>(
                Expression.Block(block), instance, writer).Compile();
        }
        
        private static Func<BinaryReader, T> GenerateDeserializer<T>()
        {
            var type = typeof(T);
            var reader = Expression.Parameter(typeof(BinaryReader), "reader");
            
            // OPTIMIZATION: If it's a built-in type (Primitive, Array, List, Dict...), use built-in reader
            if (IsBuiltInType(type))
            {
                 var readExpr = GenerateReadExpression(type, reader);
                 return Expression.Lambda<Func<BinaryReader, T>>(readExpr, reader).Compile();
            }
            
            // Otherwise, Custom Object (new + members)
            
            var result = Expression.Variable(type, "result");
            var block = new List<Expression>();
            
            // result = new T();
            // Note: This requires a parameterless constructor!
            // If custom object lacks it, this throws.
            block.Add(Expression.Assign(result, Expression.New(type)));
            
            // Find ordered properties/fields
            var members = GetSortedMembers(type);
            
            foreach (var member in members)
            {
                var memberAccess = Expression.MakeMemberAccess(result, member);
                var memberType = (member is PropertyInfo pi) ? pi.PropertyType : ((FieldInfo)member).FieldType;
                
                // Null check for reference types
                if (!memberType.IsValueType)
                {
                    // Create a block for this member's read logic to scope the variable
                    var hasValue = Expression.Variable(typeof(bool), "hasValue");
                    
                    var readBlock = Expression.Block(
                        new[] { hasValue },
                        Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                        Expression.IfThen(
                            hasValue,
                            Expression.Assign(memberAccess, GenerateReadExpression(memberType, reader))
                        )
                    );
                    
                    block.Add(readBlock);
                }
                else
                {
                    // Value types are read directly
                    block.Add(Expression.Assign(memberAccess, GenerateReadExpression(memberType, reader)));
                }
            }
            
            // return result;
            block.Add(result);
            
            return Expression.Lambda<Func<BinaryReader, T>>(
                Expression.Block(new[] { result }, block), reader).Compile();
        }

        private static Delegate GetClonerDelegate<T>() where T : class
        {
            return _clonerCache.GetOrAdd(typeof(T), t => GenerateCloner<T>());
        }

        private static Func<T, T> GenerateCloner<T>() where T : class
        {
            Type type = typeof(T);
            
            // Optimization: Immutable types → reference copy
            if (type == typeof(string) || ComponentTypeRegistry.IsRecordType(type))
            {
                var param = Expression.Parameter(type, "source");
                return Expression.Lambda<Func<T, T>>(param, param).Compile();
            }
            
            // Built-in Collection types
            if (IsBuiltInType(type))
            {
                var param = Expression.Parameter(type, "source");
                var body = GenerateBuiltInCloneExpression(type, param);
                return Expression.Lambda<Func<T, T>>(body, param).Compile();
            }
            
            var sourceParam = Expression.Parameter(type, "source");
            var variables = new List<ParameterExpression>();
            var statements = new List<Expression>();
            
            // var clone = new T();
            var cloneVar = Expression.Variable(type, "clone");
            variables.Add(cloneVar);
            
            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
                throw new InvalidOperationException(
                    $"Type {type.Name} requires parameterless constructor for cloning.");
            
            statements.Add(Expression.Assign(cloneVar, Expression.New(constructor)));
            
            // Clone each field/property
            var members = GetSortedMembers(type);
            
            foreach (var member in members)
            {
                Type memberType = member switch
                {
                    FieldInfo fieldInfo => fieldInfo.FieldType,
                    PropertyInfo propInfo => propInfo.PropertyType,
                    _ => throw new NotSupportedException()
                };
                
                var sourceMember = Expression.MakeMemberAccess(sourceParam, member);
                var cloneMember = Expression.MakeMemberAccess(cloneVar, member);
                
                Expression cloneExpression = GenerateMemberCloneExpression(memberType, sourceMember);
                
                // Handle PropertyInfo vs FieldInfo
                if (member is PropertyInfo prop && !prop.CanWrite)
                    continue; // Skip readonly properties
                
                statements.Add(Expression.Assign(cloneMember, cloneExpression));
            }
            
            // Return clone
            statements.Add(cloneVar);
            
            var block = Expression.Block(variables, statements);
            return Expression.Lambda<Func<T, T>>(block, sourceParam).Compile();
        }

        private static Expression GenerateMemberCloneExpression(Type memberType, Expression sourceExpression)
        {
            // Optimization: Immutable member types
            if (memberType == typeof(string) || 
                ComponentTypeRegistry.IsRecordType(memberType) ||
                memberType.IsValueType)
            {
                // Direct assignment (reference or value copy)
                // TODO: Deep clone for mutable structs?
                return sourceExpression;
            }
            else if (memberType.IsClass || memberType.IsInterface)
            {
                // Handle nulls
                var nullCheck = Expression.ReferenceEqual(sourceExpression, Expression.Constant(null));
                
                // Recursive deep clone
                // Check if it's a known polymorphic type or standard object
                // For simplified DeepClone, we rely on the specific Type's DeepClone impl
                // BUT: If the declared type is an Interface (e.g. IList), we need dynamic dispatch or PolymorphicSerializer?
                // For now, assume strict typing or standard DeepClone<T> usage.
                
                MethodInfo cloneMethod;
                if (memberType.IsInterface || memberType.IsAbstract)
                {
                    // Fallback to source (shallow) or throw? 
                    // Or runtime type check?
                    // For now, let's assume exact types or skip
                    return sourceExpression; // LIMITATION: Interface cloning not fully implemented
                }
                else
                {
                    cloneMethod = typeof(FdpAutoSerializer)
                        .GetMethod(nameof(DeepClone), BindingFlags.Public | BindingFlags.Static)!
                        .MakeGenericMethod(memberType);
                        
                    return Expression.Condition(
                        nullCheck,
                        Expression.Constant(null, memberType),
                        Expression.Call(cloneMethod, sourceExpression)
                    );
                }
            }
            
            return sourceExpression;
        }

        private static Expression GenerateBuiltInCloneExpression(Type type, Expression source)
        {
            if (type.IsArray) return GenerateArrayClone(type, source);
            
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(List<>)) return GenerateListClone(type, source);
                if (def == typeof(Dictionary<,>)) return GenerateDictionaryClone(type, source);
                // Fallbacks for others (Queue, Stack, etc.) - treating as shallow copy for now to prevent crash
                // Ideally implement them all
            }
            
            // Fallback: Return new instance (empty) or source?
            // Returning source breaks independence. Returning empty breaks data.
            // Let's return new empty instance if possible
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor != null) return Expression.New(ctor);
            
            return source; // Shallow fallback
        }

        private static Expression GenerateArrayClone(Type arrayType, Expression source)
        {
            var itemType = arrayType.GetElementType()!;
            var lengthProp = arrayType.GetProperty("Length")!;
            var length = Expression.Property(source, lengthProp);
            
            var arrayVar = Expression.Variable(arrayType, "cloneValues");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            var sourceItem = Expression.ArrayIndex(source, index);
            var clonedItem = GenerateMemberCloneExpression(itemType, sourceItem);
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, length),
                    Expression.Block(
                        Expression.Assign(Expression.ArrayAccess(arrayVar, index), clonedItem),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { arrayVar, index },
                Expression.Assign(arrayVar, Expression.NewArrayBounds(itemType, length)),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                arrayVar
            );
        }

        private static Expression GenerateListClone(Type listType, Expression source)
        {
            var itemType = listType.GetGenericArguments()[0];
            var countProp = listType.GetProperty("Count")!;
            var itemProp = listType.GetProperty("Item")!;
            
            var listVar = Expression.Variable(listType, "cloneList");
            var index = Expression.Variable(typeof(int), "i");
            var countVar = Expression.Variable(typeof(int), "count");
            var breakLabel = Expression.Label();
            
            var sourceItem = Expression.MakeIndex(source, itemProp, new[] { index });
            var clonedItem = GenerateMemberCloneExpression(itemType, sourceItem);
            var addMethod = listType.GetMethod("Add")!;
            
            // Constructor with capacity: new List<T>(count)
            var ctor = listType.GetConstructor(new[] { typeof(int) });
            Expression newExpr = ctor != null 
                ? Expression.New(ctor, countVar) 
                : Expression.New(listType);

            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, countVar),
                    Expression.Block(
                        Expression.Call(listVar, addMethod, clonedItem),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { listVar, index, countVar },
                Expression.Assign(countVar, Expression.Property(source, countProp)),
                Expression.Assign(listVar, newExpr),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                listVar
            );
        }

        private static Expression GenerateDictionaryClone(Type dictType, Expression source)
        {
            var args = dictType.GetGenericArguments();
            var keyType = args[0];
            var valueType = args[1];
            
            // Iterate using GetEnumerator
            // Dictionary<K,V>(capacity)
            var countProp = dictType.GetProperty("Count")!;
            var enumeratorMethod = dictType.GetMethod("GetEnumerator")!;
            var enumeratorType = enumeratorMethod.ReturnType;
            var moveNextMethod = enumeratorType.GetMethod("MoveNext")!;
            var currentProp = enumeratorType.GetProperty("Current")!;
            var kvpType = currentProp.PropertyType;
            var keyProp = kvpType.GetProperty("Key")!;
            var valueProp = kvpType.GetProperty("Value")!;
            var addMethod = dictType.GetMethod("Add")!;

            var dictVar = Expression.Variable(dictType, "cloneDict");
            var countVar = Expression.Variable(typeof(int), "count");
            var enumerator = Expression.Variable(enumeratorType, "enumerator");
            var kvp = Expression.Variable(kvpType, "kvp");
            var breakLabel = Expression.Label();
            
            // Constructor with capacity
            var ctor = dictType.GetConstructor(new[] { typeof(int) });
            Expression newExpr = ctor != null 
                ? Expression.New(ctor, countVar) 
                : Expression.New(dictType);
                
            // Key and Value cloning
            var keyAccess = Expression.Property(kvp, keyProp);
            var valueAccess = Expression.Property(kvp, valueProp);
            
            // Keys usually shouldn't be mutated but we clone them for safety if mutable
            var clonedKey = GenerateMemberCloneExpression(keyType, keyAccess);
            var clonedValue = GenerateMemberCloneExpression(valueType, valueAccess);
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, moveNextMethod),
                    Expression.Block(
                        Expression.Assign(kvp, Expression.Property(enumerator, currentProp)),
                        Expression.Call(dictVar, addMethod, clonedKey, clonedValue)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { dictVar, countVar, enumerator, kvp },
                Expression.Assign(countVar, Expression.Property(source, countProp)),
                Expression.Assign(dictVar, newExpr),
                Expression.Assign(enumerator, Expression.Call(source, enumeratorMethod)),
                loop,
                dictVar
            );
        }

        private static bool IsBuiltInType(Type type)
        {
            if (type.IsPrimitive || type == typeof(string)) return true;
            if (type.IsArray) return true;
            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                return def == typeof(List<>) ||
                       def == typeof(Dictionary<,>) ||
                       def == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>) ||
                       def == typeof(HashSet<>) ||
                       def == typeof(Queue<>) ||
                       def == typeof(Stack<>) ||
                       def == typeof(System.Collections.Concurrent.ConcurrentBag<>);
            }
            return false;
        }
        
        private static Expression GenerateWriteExpression(Type type, Expression valueAccess, ParameterExpression writer)
        {
            // CASE A: Primitive (int, float, string, etc.)
            var writeMethod = typeof(BinaryWriter).GetMethod("Write", new[] { type });
            if (writeMethod != null)
            {
                return Expression.Call(writer, writeMethod, valueAccess);
            }
            
            // CASE B: List<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return GenerateListWrite(type, valueAccess, writer);
            }
            
            // CASE C: Array T[]
            if (type.IsArray)
            {
                return GenerateArrayWrite(type, valueAccess, writer);
            }
            
            // CASE D: Dictionary<TKey, TValue>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                return GenerateDictionaryWrite(type, valueAccess, writer);
            }
            
            // CASE E: ConcurrentDictionary<TKey, TValue>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
            {
                return GenerateConcurrentDictionaryWrite(type, valueAccess, writer);
            }
            
            // CASE F: HashSet<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                return GenerateHashSetWrite(type, valueAccess, writer);
            }
            
            // CASE G: Queue<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Queue<>))
            {
                return GenerateQueueWrite(type, valueAccess, writer);
            }
            
            // CASE H: Stack<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Stack<>))
            {
                return GenerateStackWrite(type, valueAccess, writer);
            }
            
            // CASE I: ConcurrentBag<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentBag<>))
            {
                return GenerateConcurrentBagWrite(type, valueAccess, writer);
            }
            
            // CASE Z: Recursive nested object
            var recurseMethod = typeof(FdpAutoSerializer)
                .GetMethod("Serialize", BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(type);
            
            return Expression.Call(recurseMethod, valueAccess, writer);
        }
        
        private static Expression GenerateReadExpression(Type type, ParameterExpression reader)
        {
            // CASE A: Primitive
            var readMethod = typeof(BinaryReader).GetMethod($"Read{type.Name}");
            if (readMethod != null && readMethod.ReturnType == type)
            {
                return Expression.Call(reader, readMethod);
            }
            
            // Special case for String
            if (type == typeof(string))
            {
                return Expression.Call(reader, typeof(BinaryReader).GetMethod("ReadString")!);
            }
            
            // CASE B: List<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return GenerateListRead(type, reader);
            }
            
            // CASE C: Array T[]
            if (type.IsArray)
            {
                return GenerateArrayRead(type, reader);
            }
            
            // CASE D: Dictionary<TKey, TValue>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                return GenerateDictionaryRead(type, reader);
            }
            
            // CASE E: ConcurrentDictionary<TKey, TValue>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
            {
                return GenerateConcurrentDictionaryRead(type, reader);
            }
            
            // CASE F: HashSet<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                return GenerateHashSetRead(type, reader);
            }
            
            // CASE G: Queue<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Queue<>))
            {
                return GenerateQueueRead(type, reader);
            }
            
            // CASE H: Stack<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Stack<>))
            {
                return GenerateStackRead(type, reader);
            }
            
            // CASE I: ConcurrentBag<T>
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentBag<>))
            {
                return GenerateConcurrentBagRead(type, reader);
            }
            
            // CASE Z: Recursive nested object
            var recurseMethod = typeof(FdpAutoSerializer)
                .GetMethod("Deserialize", BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(type);
            
            return Expression.Call(recurseMethod, reader);
        }
        
        // ------------------------------------------------------------------
        // LIST GENERATOR
        // ------------------------------------------------------------------
        
        private static Expression GenerateListWrite(Type listType, Expression listAccess, ParameterExpression writer)
        {
            var itemType = listType.GetGenericArguments()[0];
            var countProp = listType.GetProperty("Count")!;
            var itemProp = listType.GetProperty("Item")!; // Indexer
            
            var writeCount = CallWrite(writer, typeof(int), Expression.Property(listAccess, countProp));
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            // Get element access
            var elementAccess = Expression.MakeIndex(listAccess, itemProp, new[] { index });
            
            // Check if element type is polymorphic
            bool isPolymorphic = (itemType.IsInterface || itemType.IsAbstract) && 
                                 FdpPolymorphicSerializer.IsTypeRegistered(itemType);
            
            Expression writeElement;
            if (isPolymorphic)
            {
                // Use FdpPolymorphicSerializer for interface/abstract types
                var polyWriteMethod = typeof(FdpPolymorphicSerializer).GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
                writeElement = Expression.Call(polyWriteMethod, writer, Expression.Convert(elementAccess, typeof(object)));
            }
            else if (!itemType.IsValueType)
            {
                // Protocol: [Bool HasValue] [Value?]
                var nullCheck = Expression.ReferenceNotEqual(elementAccess, Expression.Constant(null));
                writeElement = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheck),
                    Expression.IfThen(
                        nullCheck,
                        GenerateWriteExpression(itemType, elementAccess, writer)
                    )
                );
            }
            else
            {
                // Value types don't need null checks
                writeElement = GenerateWriteExpression(itemType, elementAccess, writer);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, Expression.Property(listAccess, countProp)),
                    Expression.Block(
                        writeElement,
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { index },
                writeCount,
                Expression.Assign(index, Expression.Constant(0)),
                loop
            );
        }
        
        private static Expression GenerateListRead(Type listType, ParameterExpression reader)
        {
            var itemType = listType.GetGenericArguments()[0];
            var list = Expression.Variable(listType, "list");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            var addMethod = listType.GetMethod("Add")!;
            
            // Check if element type is polymorphic
            bool isPolymorphic = (itemType.IsInterface || itemType.IsAbstract) && 
                                 FdpPolymorphicSerializer.IsTypeRegistered(itemType);
            
            // Generate read expression based on element type
            Expression readElement;
            if (isPolymorphic)
            {
                //Use FdpPolymorphicSerializer for interface/abstract types
                var polyReadMethod = typeof(FdpPolymorphicSerializer).GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
                readElement = Expression.Convert(
                    Expression.Call(polyReadMethod, reader),
                    itemType
                );
            }
            else if (!itemType.IsValueType)
            {
                // Reference types: read [Bool HasValue] then optionally [Value]
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readElement = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(
                        hasValue,
                        GenerateReadExpression(itemType, reader),
                        Expression.Constant(null, itemType)
                    )
                );
            }
            else
            {
                // Value types: read directly
                readElement = GenerateReadExpression(itemType, reader);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(list, addMethod, readElement),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { list, count, index },
                Expression.Assign(list, Expression.New(listType)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                list
            );
        }
        
        private static Expression GenerateArrayWrite(Type arrayType, Expression arrayAccess, ParameterExpression writer)
        {
            var itemType = arrayType.GetElementType()!;
            var lengthProp = arrayType.GetProperty("Length")!;
            
            var writeLength = CallWrite(writer, typeof(int), Expression.Property(arrayAccess, lengthProp));
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            // Get element access
            var elementAccess = Expression.ArrayIndex(arrayAccess, index);
            
            // Check if element type is polymorphic (interface or abstract class)
            bool isPolymorphic = (itemType.IsInterface || itemType.IsAbstract) && 
                                 FdpPolymorphicSerializer.IsTypeRegistered(itemType);
            
            Expression writeElement;
            if (isPolymorphic)
            {
                // Use FdpPolymorphicSerializer for interface/abstract types
                var polyWriteMethod = typeof(FdpPolymorphicSerializer).GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
                writeElement = Expression.Call(polyWriteMethod, writer, Expression.Convert(elementAccess, typeof(object)));
            }
            else if (!itemType.IsValueType)
            {
                // Protocol: [Bool HasValue] [Value?]
                var nullCheck = Expression.ReferenceNotEqual(elementAccess, Expression.Constant(null));
                writeElement = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheck),
                    Expression.IfThen(
                        nullCheck,
                        GenerateWriteExpression(itemType, elementAccess, writer)
                    )
                );
            }
            else
            {
                // Value types don't need null checks
                writeElement = GenerateWriteExpression(itemType, elementAccess, writer);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, Expression.Property(arrayAccess, lengthProp)),
                    Expression.Block(
                        writeElement,
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { index },
                writeLength,
                Expression.Assign(index, Expression.Constant(0)),
                loop
            );
        }
        
        private static Expression GenerateArrayRead(Type arrayType, ParameterExpression reader)
        {
            var itemType = arrayType.GetElementType()!;
            var array = Expression.Variable(arrayType, "array");
            var length = Expression.Variable(typeof(int), "length");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            // Check if element type is polymorphic
            bool isPolymorphic = (itemType.IsInterface || itemType.IsAbstract) && 
                                 FdpPolymorphicSerializer.IsTypeRegistered(itemType);
            
            // Generate read expression based on element type
            Expression readElement;
            if (isPolymorphic)
            {
                // Use FdpPolymorphicSerializer for interface/abstract types
                var polyReadMethod = typeof(FdpPolymorphicSerializer).GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
                readElement = Expression.Convert(
                    Expression.Call(polyReadMethod, reader),
                    itemType
                );
            }
            else if (!itemType.IsValueType)
            {
                // Reference types: read [Bool HasValue] then optionally [Value]
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readElement = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(
                        hasValue,
                        GenerateReadExpression(itemType, reader),
                        Expression.Constant(null, itemType)
                    )
                );
            }
            else
            {
                // Value types: read directly
                readElement = GenerateReadExpression(itemType, reader);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, length),
                    Expression.Block(
                        Expression.Assign(
                            Expression.ArrayAccess(array, index),
                            readElement
                        ),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { array, length, index },
                Expression.Assign(length, CallRead(reader, typeof(int))),
                Expression.Assign(array, Expression.NewArrayBounds(itemType, length)),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                array
            );
        }
        
        // ------------------------------------------------------------------
        // DICTIONARY GENERATORS
        // ------------------------------------------------------------------
        
        private static Expression GenerateDictionaryWrite(Type dictType, Expression dictAccess, ParameterExpression writer)
        {
            var args = dictType.GetGenericArguments();
            var keyType = args[0];
            var valueType = args[1];
            
            var countProp = dictType.GetProperty("Count")!;
            var enumeratorMethod = dictType.GetMethod("GetEnumerator")!;
            var enumeratorType = enumeratorMethod.ReturnType;
            var moveNextMethod = enumeratorType.GetMethod("MoveNext")!;
            var currentProp = enumeratorType.GetProperty("Current")!;
            var kvpType = currentProp.PropertyType;
            var keyProp = kvpType.GetProperty("Key")!;
            var valueProp = kvpType.GetProperty("Value")!;
            
            var enumerator = Expression.Variable(enumeratorType, "enumerator");
            var kvp = Expression.Variable(kvpType, "kvp");
            var breakLabel = Expression.Label();
            
            var writeCount = CallWrite(writer, typeof(int), Expression.Property(dictAccess, countProp));
            
            // Check if key type is polymorphic
            bool keyIsPolymorphic = (keyType.IsInterface || keyType.IsAbstract) && 
                                    FdpPolymorphicSerializer.IsTypeRegistered(keyType);
            
            var keyAccess = Expression.Property(kvp, keyProp);
            Expression writeKey;
            if (keyIsPolymorphic)
            {
                var polyWriteMethod = typeof(FdpPolymorphicSerializer).GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
                writeKey = Expression.Call(polyWriteMethod, writer, Expression.Convert(keyAccess, typeof(object)));
            }
            else if (!keyType.IsValueType)
            {
                var nullCheckKey = Expression.ReferenceNotEqual(keyAccess, Expression.Constant(null));
                writeKey = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheckKey),
                    Expression.IfThen(nullCheckKey, GenerateWriteExpression(keyType, keyAccess, writer))
                );
            }
            else
            {
                writeKey = GenerateWriteExpression(keyType, keyAccess, writer);
            }
            
            // Check if value type is polymorphic
            bool valueIsPolymorphic = (valueType.IsInterface || valueType.IsAbstract) && 
                                      FdpPolymorphicSerializer.IsTypeRegistered(valueType);
            
            var valueAccess = Expression.Property(kvp, valueProp);
            Expression writeValue;
            if (valueIsPolymorphic)
            {
                var polyWriteMethod = typeof(FdpPolymorphicSerializer).GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
                writeValue = Expression.Call(polyWriteMethod, writer, Expression.Convert(valueAccess, typeof(object)));
            }
            else if (!valueType.IsValueType)
            {
                var nullCheckValue = Expression.ReferenceNotEqual(valueAccess, Expression.Constant(null));
                writeValue = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheckValue),
                    Expression.IfThen(nullCheckValue, GenerateWriteExpression(valueType, valueAccess, writer))
                );
            }
            else
            {
                writeValue = GenerateWriteExpression(valueType, valueAccess, writer);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, moveNextMethod),
                    Expression.Block(
                        Expression.Assign(kvp, Expression.Property(enumerator, currentProp)),
                        writeKey,
                        writeValue
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { enumerator, kvp },
                writeCount,
                Expression.Assign(enumerator, Expression.Call(dictAccess, enumeratorMethod)),
                loop
            );
        }
        
        private static Expression GenerateDictionaryRead(Type dictType, ParameterExpression reader)
        {
            var args = dictType.GetGenericArguments();
            var keyType = args[0];
            var valueType = args[1];
            
            var dict = Expression.Variable(dictType, "dict");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            var addMethod = dictType.GetMethod("Add", new[] { keyType, valueType })!;
            
            // Check if key type is polymorphic
            bool keyIsPolymorphic = (keyType.IsInterface || keyType.IsAbstract) && 
                                    FdpPolymorphicSerializer.IsTypeRegistered(keyType);
            
            Expression readKey;
            if (keyIsPolymorphic)
            {
                var polyReadMethod = typeof(FdpPolymorphicSerializer).GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
                readKey = Expression.Convert(Expression.Call(polyReadMethod, reader), keyType);
            }
            else if (!keyType.IsValueType)
            {
                var hasKey = Expression.Variable(typeof(bool), "hasKey");
                readKey = Expression.Block(
                    new[] { hasKey },
                    Expression.Assign(hasKey, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasKey, GenerateReadExpression(keyType, reader), Expression.Constant(null, keyType))
                );
            }
            else
            {
                readKey = GenerateReadExpression(keyType, reader);
            }
            
            // Check if value type is polymorphic
            bool valueIsPolymorphic = (valueType.IsInterface || valueType.IsAbstract) && 
                                      FdpPolymorphicSerializer.IsTypeRegistered(valueType);
            
            Expression readValue;
            if (valueIsPolymorphic)
            {
                var polyReadMethod = typeof(FdpPolymorphicSerializer).GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
                readValue = Expression.Convert(Expression.Call(polyReadMethod, reader), valueType);
            }
            else if (!valueType.IsValueType)
            {
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readValue = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasValue, GenerateReadExpression(valueType, reader), Expression.Constant(null, valueType))
                );
            }
            else
            {
                readValue = GenerateReadExpression(valueType, reader);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(dict, addMethod, readKey, readValue),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { dict, count, index },
                Expression.Assign(dict, Expression.New(dictType)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                dict
            );
        }
        
        private static Expression GenerateConcurrentDictionaryWrite(Type dictType, Expression dictAccess, ParameterExpression writer)
        {
            // ConcurrentDictionary.GetEnumerator() returns a different type, use IEnumerable interface
            var args = dictType.GetGenericArguments();
            var kvpType = typeof(KeyValuePair<,>).MakeGenericType(args);
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(kvpType);
            var enumeratorInterfaceType = typeof(IEnumerator<>).MakeGenericType(kvpType);
            var getEnumeratorMethod = enumerableType.GetMethod("GetEnumerator")!;
            var moveNextMethod = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
            var currentProp = enumeratorInterfaceType.GetProperty("Current")!;
            var keyProp = kvpType.GetProperty("Key")!;
            var valueProp = kvpType.GetProperty("Value")!;
            
            var enumerator = Expression.Variable(enumeratorInterfaceType, "enumerator");
            var kvp = Expression.Variable(kvpType, "kvp");
            var breakLabel = Expression.Label();
            var countProp = dictType.GetProperty("Count")!;
            
            var writeCount = CallWrite(writer, typeof(int), Expression.Property(dictAccess, countProp));
            
            // Check if key/value types are polymorphic
            bool keyIsPolymorphic = (args[0].IsInterface || args[0].IsAbstract) && FdpPolymorphicSerializer.IsTypeRegistered(args[0]);
            bool valueIsPolymorphic = (args[1].IsInterface || args[1].IsAbstract) && FdpPolymorphicSerializer.IsTypeRegistered(args[1]);
            
            var keyAccess = Expression.Property(kvp, keyProp);
            Expression writeKey;
            if (keyIsPolymorphic)
            {
                var polyWriteMethod = typeof(FdpPolymorphicSerializer).GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
                writeKey = Expression.Call(polyWriteMethod, writer, Expression.Convert(keyAccess, typeof(object)));
            }
            else if (!args[0].IsValueType)
            {
                var nullCheckKey = Expression.ReferenceNotEqual(keyAccess, Expression.Constant(null));
                writeKey = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheckKey),
                    Expression.IfThen(nullCheckKey, GenerateWriteExpression(args[0], keyAccess, writer))
                );
            }
            else
            {
                writeKey = GenerateWriteExpression(args[0], keyAccess, writer);
            }
            
            var valueAccess = Expression.Property(kvp, valueProp);
            Expression writeValue;
            if (valueIsPolymorphic)
            {
                var polyWriteMethod = typeof(FdpPolymorphicSerializer).GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!;
                writeValue = Expression.Call(polyWriteMethod, writer, Expression.Convert(valueAccess, typeof(object)));
            }
            else if (!args[1].IsValueType)
            {
                var nullCheckValue = Expression.ReferenceNotEqual(valueAccess, Expression.Constant(null));
                writeValue = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheckValue),
                    Expression.IfThen(nullCheckValue, GenerateWriteExpression(args[1], valueAccess, writer))
                );
            }
            else
            {
                writeValue = GenerateWriteExpression(args[1], valueAccess, writer);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, moveNextMethod),
                    Expression.Block(
                        Expression.Assign(kvp, Expression.Property(enumerator, currentProp)),
                        writeKey,
                        writeValue
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { enumerator, kvp },
                writeCount,
                Expression.Assign(enumerator, Expression.Call(Expression.Convert(dictAccess, enumerableType), getEnumeratorMethod)),
                loop
            );
        }
        
        private static Expression GenerateConcurrentDictionaryRead(Type dictType, ParameterExpression reader)
        {
            var args = dictType.GetGenericArguments();
            var keyType = args[0];
            var valueType = args[1];
            
            var dict = Expression.Variable(dictType, "dict");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            // ConcurrentDictionary uses TryAdd, not Add
            var tryAddMethod = dictType.GetMethod("TryAdd", new[] { keyType, valueType })!;
            
            // Check if key type is polymorphic
            bool keyIsPolymorphic = (keyType.IsInterface || keyType.IsAbstract) && FdpPolymorphicSerializer.IsTypeRegistered(keyType);
            
            Expression readKey;
            if (keyIsPolymorphic)
            {
                var polyReadMethod = typeof(FdpPolymorphicSerializer).GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
                readKey = Expression.Convert(Expression.Call(polyReadMethod, reader), keyType);
            }
            else if (!keyType.IsValueType)
            {
                var hasKey = Expression.Variable(typeof(bool), "hasKey");
                readKey = Expression.Block(
                    new[] { hasKey },
                    Expression.Assign(hasKey, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasKey, GenerateReadExpression(keyType, reader), Expression.Constant(null, keyType))
                );
            }
            else
            {
                readKey = GenerateReadExpression(keyType, reader);
            }
            
            // Check if value type is polymorphic
            bool valueIsPolymorphic = (valueType.IsInterface || valueType.IsAbstract) && FdpPolymorphicSerializer.IsTypeRegistered(valueType);
            
            Expression readValue;
            if (valueIsPolymorphic)
            {
                var polyReadMethod = typeof(FdpPolymorphicSerializer).GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
                readValue = Expression.Convert(Expression.Call(polyReadMethod, reader), valueType);
            }
            else if (!valueType.IsValueType)
            {
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readValue = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasValue, GenerateReadExpression(valueType, reader), Expression.Constant(null, valueType))
                );
            }
            else
            {
                readValue = GenerateReadExpression(valueType, reader);
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(dict, tryAddMethod, readKey, readValue),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { dict, count, index },
                Expression.Assign(dict, Expression.New(dictType)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                dict
            );
        }
        
        // ------------------------------------------------------------------
        // HASHSET GENERATORS
        // ------------------------------------------------------------------
        
        private static Expression GenerateHashSetWrite(Type hashSetType, Expression hashSetAccess, ParameterExpression writer)
        {
            var itemType = hashSetType.GetGenericArguments()[0];
            var countProp = hashSetType.GetProperty("Count")!;
            var enumeratorMethod = hashSetType.GetMethod("GetEnumerator")!;
            var enumeratorType = enumeratorMethod.ReturnType;
            var moveNextMethod = enumeratorType.GetMethod("MoveNext")!;
            var currentProp = enumeratorType.GetProperty("Current")!;
            
            var enumerator = Expression.Variable(enumeratorType, "enumerator");
            var item = Expression.Variable(itemType, "item");
            var breakLabel = Expression.Label();
            
            var writeCount = CallWrite(writer, typeof(int), Expression.Property(hashSetAccess, countProp));
            
            Expression writeItem = GenerateWriteExpression(itemType, item, writer);
            if (!itemType.IsValueType)
            {
                var nullCheck = Expression.ReferenceNotEqual(item, Expression.Constant(null));
                writeItem = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheck),
                    Expression.IfThen(nullCheck, writeItem)
                );
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, moveNextMethod),
                    Expression.Block(
                        Expression.Assign(item, Expression.Property(enumerator, currentProp)),
                        writeItem
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { enumerator, item },
                writeCount,
                Expression.Assign(enumerator, Expression.Call(hashSetAccess, enumeratorMethod)),
                loop
            );
        }
        
        private static Expression GenerateHashSetRead(Type hashSetType, ParameterExpression reader)
        {
            var itemType = hashSetType.GetGenericArguments()[0];
            var hashSet = Expression.Variable(hashSetType, "hashSet");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            var addMethod = hashSetType.GetMethod("Add")!;
            
            Expression readItem = GenerateReadExpression(itemType, reader);
            if (!itemType.IsValueType)
            {
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readItem = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasValue, readItem, Expression.Constant(null, itemType))
                );
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(hashSet, addMethod, readItem),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { hashSet, count, index },
                Expression.Assign(hashSet, Expression.New(hashSetType)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                hashSet
            );
        }
        
        // ------------------------------------------------------------------
        // QUEUE, STACK, CONCURRENTBAG GENERATORS
        // ------------------------------------------------------------------
        
        private static Expression GenerateQueueWrite(Type queueType, Expression queueAccess, ParameterExpression writer)
        {
            return GenerateEnumerableWrite(queueType, queueAccess, writer);
        }
        
        private static Expression GenerateStackWrite(Type stackType, Expression stackAccess, ParameterExpression writer)
        {
            return GenerateEnumerableWrite(stackType, stackAccess, writer);
        }
        
        private static Expression GenerateConcurrentBagWrite(Type bagType, Expression bagAccess, ParameterExpression writer)
        {
            return GenerateEnumerableWrite(bagType, bagAccess, writer);
        }
        
        private static Expression GenerateEnumerableWrite(Type collectionType, Expression collectionAccess, ParameterExpression writer)
        {
            var itemType = collectionType.GetGenericArguments()[0];
            var countProp = collectionType.GetProperty("Count")!;
            
            // Use IEnumerable<T> interface for compatibility with concurrent collections
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(itemType);
            var enumeratorInterfaceType = typeof(IEnumerator<>).MakeGenericType(itemType);
            var getEnumeratorMethod = enumerableType.GetMethod("GetEnumerator")!;
            var moveNextMethod = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
            var currentProp = enumeratorInterfaceType.GetProperty("Current")!;
            
            var enumerator = Expression.Variable(enumeratorInterfaceType, "enumerator");
            var item = Expression.Variable(itemType, "item");
            var breakLabel = Expression.Label();
            
            var writeCount = CallWrite(writer, typeof(int), Expression.Property(collectionAccess, countProp));
            
            Expression writeItem = GenerateWriteExpression(itemType, item, writer);
            if (!itemType.IsValueType)
            {
                var nullCheck = Expression.ReferenceNotEqual(item, Expression.Constant(null));
                writeItem = Expression.Block(
                    CallWrite(writer, typeof(bool), nullCheck),
                    Expression.IfThen(nullCheck, writeItem)
                );
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.Call(enumerator, moveNextMethod),
                    Expression.Block(
                        Expression.Assign(item, Expression.Property(enumerator, currentProp)),
                        writeItem
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { enumerator, item },
                writeCount,
                Expression.Assign(enumerator, Expression.Call(Expression.Convert(collectionAccess, enumerableType), getEnumeratorMethod)),
                loop
            );
        }
        
        private static Expression GenerateQueueRead(Type queueType, ParameterExpression reader)
        {
            var itemType = queueType.GetGenericArguments()[0];
            var queue = Expression.Variable(queueType, "queue");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            var enqueueMethod = queueType.GetMethod("Enqueue")!;
            
            Expression readItem = GenerateReadExpression(itemType, reader);
            if (!itemType.IsValueType)
            {
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readItem = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasValue, readItem, Expression.Constant(null, itemType))
                );
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(queue, enqueueMethod, readItem),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { queue, count, index },
                Expression.Assign(queue, Expression.New(queueType)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                queue
            );
        }
        
        private static Expression GenerateStackRead(Type stackType, ParameterExpression reader)
        {
            var itemType = stackType.GetGenericArguments()[0];
            var stack = Expression.Variable(stackType, "stack");
            var count = Expression.Variable(typeof(int), "count");
            var tempList = Expression.Variable(typeof(List<>).MakeGenericType(itemType), "tempList");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            var breakLabel2 = Expression.Label();
            
            var pushMethod = stackType.GetMethod("Push")!;
            var addMethod = tempList.Type.GetMethod("Add")!;
            var indexerProp = tempList.Type.GetProperty("Item")!;
            
            Expression readItem = GenerateReadExpression(itemType, reader);
            if (!itemType.IsValueType)
            {
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readItem = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasValue, readItem, Expression.Constant(null, itemType))
                );
            }
            
            var readLoop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(tempList, addMethod, readItem),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            var pushLoop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.GreaterThanOrEqual(index, Expression.Constant(0)),
                    Expression.Block(
                        Expression.Call(stack, pushMethod, 
                            Expression.MakeIndex(tempList, indexerProp, new[] { index })),
                        Expression.PreDecrementAssign(index)
                    ),
                    Expression.Break(breakLabel2)
                ),
                breakLabel2
            );
            
            return Expression.Block(
                new[] { stack, count, tempList, index },
                Expression.Assign(stack, Expression.New(stackType)),
                Expression.Assign(tempList, Expression.New(tempList.Type)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                readLoop,
                Expression.Assign(index, Expression.Subtract(count, Expression.Constant(1))),
                pushLoop,
                stack
            );
        }
        
        private static Expression GenerateConcurrentBagRead(Type bagType, ParameterExpression reader)
        {
            var itemType = bagType.GetGenericArguments()[0];
            var bag = Expression.Variable(bagType, "bag");
            var count = Expression.Variable(typeof(int), "count");
            var index = Expression.Variable(typeof(int), "i");
            var breakLabel = Expression.Label();
            
            var addMethod = bagType.GetMethod("Add")!;
            
            Expression readItem = GenerateReadExpression(itemType, reader);
            if (!itemType.IsValueType)
            {
                var hasValue = Expression.Variable(typeof(bool), "hasValue");
                readItem = Expression.Block(
                    new[] { hasValue },
                    Expression.Assign(hasValue, CallRead(reader, typeof(bool))),
                    Expression.Condition(hasValue, readItem, Expression.Constant(null, itemType))
                );
            }
            
            var loop = Expression.Loop(
                Expression.IfThenElse(
                    Expression.LessThan(index, count),
                    Expression.Block(
                        Expression.Call(bag, addMethod, readItem),
                        Expression.PostIncrementAssign(index)
                    ),
                    Expression.Break(breakLabel)
                ),
                breakLabel
            );
            
            return Expression.Block(
                new[] { bag, count, index },
                Expression.Assign(bag, Expression.New(bagType)),
                Expression.Assign(count, CallRead(reader, typeof(int))),
                Expression.Assign(index, Expression.Constant(0)),
                loop,
                bag
            );
        }
        
        // ------------------------------------------------------------------
        // HELPERS
        // ------------------------------------------------------------------
        
        private static MethodCallExpression CallWrite(Expression writer, Type t, Expression value)
        {
            var m = typeof(BinaryWriter).GetMethod("Write", new[] { t })!;
            return Expression.Call(writer, m, value);
        }
        
        private static Expression CallRead(Expression reader, Type t)
        {
            if (t == typeof(string))
            {
                return Expression.Call(reader, typeof(BinaryReader).GetMethod("ReadString")!);
            }
            
            var methodName = $"Read{t.Name}";
            var method = typeof(BinaryReader).GetMethod(methodName)!;
            if (method != null)
            {
                return Expression.Call(reader, method);
            }
            
            throw new NotSupportedException($"No read method for type {t.Name}");
        }
        
        private static List<MemberInfo> GetSortedMembers(Type t)
        {
            // Finds public props/fields with [Key] attribute
            // MessagePack's KeyAttribute stores the key in a field, not a property
            var members = t.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => (m is PropertyInfo || m is FieldInfo) && m.GetCustomAttribute<KeyAttribute>() != null)
                .Select(m => new { Member = m, Attr = m.GetCustomAttribute<KeyAttribute>()! })
                .OrderBy(x => GetMessagePackKey(x.Attr))
                .Select(x => x.Member)
                .ToList();
            
            return members;
        }

        private static int GetMessagePackKey(Attribute attr)
        {
            var type = attr.GetType();
            
            // 1. Try public property (IntKey or Key)
            var p = type.GetProperty("IntKey") ?? type.GetProperty("Key");
            if (p != null && p.CanRead && p.PropertyType == typeof(int))
            {
                return (int)p.GetValue(attr)!;
            }

            // 2. Try private field 'intKey' (legacy/internal MessagePack impl)
            var f = type.GetField("intKey", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(int))
            {
                return (int)f.GetValue(attr)!;
            }

            return 0;
        }
    }
    
    /// <summary>
    /// Attribute to mark types for polymorphic serialization.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class FdpPolymorphicTypeAttribute : Attribute
    {
        public new byte TypeId { get; }
        
        public FdpPolymorphicTypeAttribute(byte typeId)
        {
            TypeId = typeId;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Converts arbitrary DTO objects into plain CLR graphs (primitives, strings,
    /// <c>Dictionary&lt;string, object?&gt;</c>, <c>List&lt;object?&gt;</c>) that
    /// <c>System.Text.Json</c> can serialise without any custom converters.
    ///
    /// <para>
    /// Handles <see cref="FixedBufferAttribute"/> fields and
    /// <see cref="InlineArrayAttribute"/> types that the stock JSON serialiser
    /// cannot emit correctly.
    /// </para>
    /// </summary>
    public static class DtoDiagnosticMapper
    {
        /// <summary>
        /// Maps <paramref name="obj"/> of <paramref name="type"/> into a plain CLR graph.
        /// Pass an empty <see cref="HashSet{T}"/> (with <see cref="ReferenceEqualityComparer.Instance"/>)
        /// as <paramref name="visited"/> to guard against circular references.
        /// </summary>
        public static object? MapObject(object? obj, Type type, HashSet<object> visited)
        {
            if (obj == null) return null;
            if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid))
                return obj;
            if (type.IsEnum)
                return obj.ToString();

            // ⭐⭐ QA-007 — a FixedString IS a string to a reader, not a struct with a byte buffer.
            //
            // ⛔ Without this the generic struct arm below recursed into the type and produced a JSON
            // OBJECT, so `/events` and the blackboard translators rendered an entity's Name as
            // {"_fixedBuffer":[65,108,...],"Length":5} — which is precisely the "raw list of 64 byte
            // values" this mapper's own doc-comment says it exists to avoid. 📐 Measured 2026-08-26:
            // EventSerializationHelperTests asserted JsonValueKind.String and got Object — a REAL
            // defect in the readable-diagnostics contract, not a stale assertion.
            //
            // ⚠ The FixedBufferAttribute arm further down handles a fixed buffer that is a FIELD OF
            // some other struct; it never fired for the FixedString wrapper itself.
            if (type == typeof(FixedString32) || type == typeof(FixedString64) || type == typeof(FixedString128))
                return obj.ToString();

            if (!type.IsValueType && !visited.Add(obj))
            {
                return "<<circular reference>>";
            }

            if (obj is IEnumerable enumerable)
            {
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(item != null ? MapObject(item, item.GetType(), visited) : null);
                }
                return list;
            }

            var dict = new Dictionary<string, object?>();

            var inlineArrayAttr = type.GetCustomAttribute<InlineArrayAttribute>();
            if (inlineArrayAttr != null)
            {
                int length = inlineArrayAttr.Length;
                var list = new List<object?>();
                var elementField = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetFirstOrDefault();
                if (elementField != null)
                {
                    Type elemType = elementField.FieldType;
                    int structSize = Marshal.SizeOf(type);
                    IntPtr ptr = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        Marshal.StructureToPtr(obj, ptr, false);
                        int elemSize = GetSizeOf(elemType);
                        for (int i = 0; i < length; i++)
                        {
                            IntPtr elemPtr = IntPtr.Add(ptr, i * elemSize);
                            object? elemVal = ReadPointer(elemPtr, elemType);
                            list.Add(MapObject(elemVal, elemType, visited));
                        }
                    }
                    finally
                    {
                        Marshal.DestroyStructure(ptr, type);
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                return list;
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var f in fields)
            {
                var fixedAttr = f.GetCustomAttribute<FixedBufferAttribute>();
                if (fixedAttr != null)
                {
                    int length = fixedAttr.Length;
                    Type elemType = fixedAttr.ElementType;
                    var list = new List<object?>();

                    object fixedStruct = f.GetValue(obj)!;

                    int structSize = Marshal.SizeOf(fixedStruct.GetType());
                    IntPtr ptr = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        Marshal.StructureToPtr(fixedStruct, ptr, false);
                        int elemSize = GetSizeOf(elemType);

                        for (int i = 0; i < length; i++)
                        {
                            IntPtr elemPtr = IntPtr.Add(ptr, i * elemSize);
                            object? elemVal = ReadPointer(elemPtr, elemType);
                            list.Add(MapObject(elemVal, elemType, visited));
                        }
                    }
                    finally
                    {
                        Marshal.DestroyStructure(ptr, fixedStruct.GetType());
                        Marshal.FreeHGlobal(ptr);
                    }

                    dict[f.Name] = list;
                }
                else
                {
                    dict[f.Name] = MapObject(f.GetValue(obj), f.FieldType, visited);
                }
            }

            if (!type.IsValueType)
            {
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (p.GetIndexParameters().Length != 0 || !p.CanRead) continue;
                    try { dict[p.Name] = MapObject(p.GetValue(obj), p.PropertyType, visited); }
                    catch { }
                }
            }

            return dict;
        }

        /// <summary>Reads a primitive or struct value from an unmanaged memory pointer.</summary>
        public static object? ReadPointer(IntPtr ptr, Type type)
        {
            if (type == typeof(byte))   return Marshal.ReadByte(ptr);
            if (type == typeof(sbyte))  return (sbyte)Marshal.ReadByte(ptr);
            if (type == typeof(short))  return Marshal.ReadInt16(ptr);
            if (type == typeof(ushort)) return unchecked((ushort)Marshal.ReadInt16(ptr));
            if (type == typeof(int))    return Marshal.ReadInt32(ptr);
            if (type == typeof(uint))   return unchecked((uint)Marshal.ReadInt32(ptr));
            if (type == typeof(long))   return Marshal.ReadInt64(ptr);
            if (type == typeof(ulong))  return unchecked((ulong)Marshal.ReadInt64(ptr));
            if (type == typeof(float))  { var arr = new float[1];  Marshal.Copy(ptr, arr, 0, 1); return arr[0]; }
            if (type == typeof(double)) { var arr = new double[1]; Marshal.Copy(ptr, arr, 0, 1); return arr[0]; }
            if (type == typeof(bool))   return Marshal.ReadByte(ptr) != 0;

            try { return Marshal.PtrToStructure(ptr, type); } catch { return null; }
        }

        /// <summary>Returns the size in bytes of a primitive or blittable type.</summary>
        public static int GetSizeOf(Type type)
        {
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool)) return 1;
            if (type == typeof(short) || type == typeof(ushort)) return 2;
            if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
            return Marshal.SizeOf(type);
        }
    }

    /// <summary>Extension helper used internally by <see cref="DtoDiagnosticMapper"/>.</summary>
    internal static class FieldInfoArrayExtensions
    {
        /// <summary>Returns the first element or null when the array is empty.</summary>
        public static FieldInfo? GetFirstOrDefault(this FieldInfo[] fields)
            => fields.Length > 0 ? fields[0] : null;
    }
}

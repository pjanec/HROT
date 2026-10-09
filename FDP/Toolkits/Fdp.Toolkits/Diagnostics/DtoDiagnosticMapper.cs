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
        private static string NonFinite(double v) => double.IsNaN(v) ? "NaN" : v > 0 ? "Infinity" : "-Infinity";

        public static object? MapObject(object? obj, Type type, HashSet<object> visited)
        {
            if (obj == null) return null;
            // ⭐ CE-3144 — a non-finite float is not a JSON number, so it leaves this graph as the DebugApi's sentinel string
            //   ("NaN" / "Infinity" / "-Infinity", the NonFinite*SentinelConverter spelling). 🔴 Returned raw, it reached
            //   ToJsonString and threw: measured live, a PeekAndFire behaviour block holding -Infinity made every
            //   GET /entities and GET /entities/{id} on the brain node answer 500 from the unit's first exposure on.
            if (obj is double d && !double.IsFinite(d)) return NonFinite(d);
            if (obj is float fl && !float.IsFinite(fl)) return NonFinite(fl);
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
                    // ⭐ CE-476 — read each element EXACTLY, from the boxed array, at the element's own stride.
                    // 🔴 This marshalled the array (Marshal.SizeOf / StructureToPtr), which throws for an element
                    //    the marshaller cannot size — measured: a blueprint behaviour's root block carries an
                    //    [InlineArray] of an ENUM (HillAttackSlot), Marshal.SizeOf throws for every enum type, and
                    //    every GET /entities on that node answered 500. Rail:
                    //    EventSerializationHelperTests.MapObject_InlineArrayOfAnEnum_ReadsEveryElementExactly.
                    Type elemType = elementField.FieldType;
                    foreach (var elemVal in ReadBufferElements(obj, elemType, length))
                        list.Add(MapObject(elemVal, elemType, visited));
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

                    // ⭐ CE-2030 — the same exact reader as the [InlineArray] arm. ⛔ It marshalled the buffer, where a
                    //   `fixed bool` element is widened to 4 bytes and every element after the first read the wrong bytes.
                    foreach (var elemVal in ReadBufferElements(f.GetValue(obj)!, elemType, length))
                        list.Add(MapObject(elemVal, elemType, visited));

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

        /// <summary>
    /// ⭐⭐ <c>CE-2030</c> — the <paramref name="length"/> elements of a BUFFER struct — an <c>[InlineArray]</c> or a compiler
    /// <c>fixed T X[N]</c> buffer — read EXACTLY from the boxed value at the element's own stride (<c>CE-476</c>'s reader, the
    /// runtime's own layout, no marshalling). The ONE such reader: this mapper's two arms and the ImGui property tree use it.
    /// </summary>
    public static List<object?> ReadBufferElements(object bufferStruct, Type elementType, int length)
    {
        var element = InlineArrayElementOpen.MakeGenericMethod(bufferStruct.GetType(), elementType);
        var list = new List<object?>(length);
        for (int i = 0; i < length; i++)
            list.Add(element.Invoke(null, new[] { bufferStruct, (object)i }));
        return list;
    }

    private static readonly MethodInfo InlineArrayElementOpen =
            typeof(DtoDiagnosticMapper).GetMethod(nameof(InlineArrayElement), BindingFlags.NonPublic | BindingFlags.Static)!;

        // Element `index` of a boxed [InlineArray] — the runtime's own layout, no marshalling.
        private static object? InlineArrayElement<TArray, TElement>(object box, int index) where TArray : struct
        {
            TArray array = (TArray)box;
            return Unsafe.Add(ref Unsafe.As<TArray, TElement>(ref array), index);
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

        /// <summary>The managed size of <paramref name="type"/> — ⭐ CE-2030: <see cref="Fdp.Core.TypeLayout.SizeOf"/>, the stride the
        /// memory being read actually has. ⛔ It was <c>Marshal.SizeOf</c> for a struct: the interop layout, where a bool is 4.</summary>
        public static int GetSizeOf(Type type) => Fdp.Core.TypeLayout.SizeOf(type);
    }

    /// <summary>Extension helper used internally by <see cref="DtoDiagnosticMapper"/>.</summary>
    internal static class FieldInfoArrayExtensions
    {
        /// <summary>Returns the first element or null when the array is empty.</summary>
        public static FieldInfo? GetFirstOrDefault(this FieldInfo[] fields)
            => fields.Length > 0 ? fields[0] : null;
    }
}

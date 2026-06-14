using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core.FlightRecorder;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Describes the JSON shape (field names + JSON type names) of a CLR type
    /// using the same member ordering as <see cref="FdpAutoSerializer"/>.
    /// </summary>
    public static class JsonShapeDescriber
    {
        /// <summary>Describes a single field in a JSON schema.</summary>
        public record FieldDescriptor(string Name, string Type);

        /// <summary>
        /// Returns a list of <see cref="FieldDescriptor"/> for <paramref name="type"/>,
        /// ordered the same way <see cref="FdpAutoSerializer"/> serializes them.
        /// </summary>
        public static IReadOnlyList<FieldDescriptor> Describe(Type type)
        {
            var members = FdpAutoSerializer.GetSortedMembers(type);
            var result  = new List<FieldDescriptor>(members.Count);
            foreach (var m in members)
            {
                var clrType = m switch
                {
                    FieldInfo    fi => fi.FieldType,
                    PropertyInfo pi => pi.PropertyType,
                    _               => typeof(object)
                };
                result.Add(new FieldDescriptor(m.Name, MapClrTypeToJsonTypeName(clrType)));
            }
            return result;
        }

        private static string MapClrTypeToJsonTypeName(Type t)
        {
            // Unwrap Nullable<T>
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null)
                return MapClrTypeToJsonTypeName(underlying);

            if (t == typeof(bool))   return "boolean";
            if (t == typeof(string)) return "string";
            if (t.IsEnum)            return "string";   // StrictStringEnumConverter convention

            if (t == typeof(byte)    || t == typeof(sbyte)   ||
                t == typeof(short)   || t == typeof(ushort)  ||
                t == typeof(int)     || t == typeof(uint)    ||
                t == typeof(long)    || t == typeof(ulong)   ||
                t == typeof(float)   || t == typeof(double)  ||
                t == typeof(decimal))
                return "number";

            if (t == typeof(Vector2)     || t == typeof(Vector3) ||
                t == typeof(Vector4)     || t == typeof(Quaternion))
                return "object";

            if (t.IsArray) return "array";
            if (t.IsGenericType)
            {
                var genDef = t.GetGenericTypeDefinition();
                if (genDef == typeof(List<>)    ||
                    genDef == typeof(IList<>)   ||
                    genDef == typeof(ICollection<>) ||
                    genDef == typeof(IEnumerable<>))
                    return "array";
            }
            if (typeof(IEnumerable).IsAssignableFrom(t) && t != typeof(string))
                return "array";

            return "object";
        }
    }
}

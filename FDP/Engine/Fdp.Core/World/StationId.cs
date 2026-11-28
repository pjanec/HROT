using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ <c>CE-11027</c> S12-1 (<c>docs/DESIGN_Player_Join.md</c> §12.2, Q114 A / A′, R-11011) — a STATION: one operator seat. A node may host several (the editor is the all-in-one server and the gateway runs one seat per
    /// connected client), so a station is the node plus a seat <see cref="Index"/>: ONE <see cref="int"/>, the node in the LOW 16 bits and the index in the HIGH 16.
    /// <para>⭐ Memory and JSON are an <see cref="int"/>'s, and for index 0 the number equals the node's — so a node that hosts a single station (a Player node) is its own station id and every recording, wire sample and
    /// config written before this type reads back unchanged. <see cref="LayoutHashAsAttribute"/> keeps the recorded layout hash of every field of this type equal to the <see cref="NodeId"/> it replaced.</para>
    /// <para>The index is <c>0..<see cref="MaxIndex"/></c>; <see cref="Of"/> throws outside it. A node id above 16 bits cannot be packed and throws too.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(StationIdJsonConverter))]
    [TypeConverter(typeof(StationIdTypeConverter))]
    [LayoutHashAs(typeof(NodeId))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct StationId : IEquatable<StationId>, IComparable<StationId>
    {
        /// <summary>The highest seat index.</summary>
        public const int MaxIndex = 1024;

        /// <summary>The packed station: <c>node | index &lt;&lt; 16</c>.</summary>
        public readonly int Value;

        private StationId(int value) => Value = value;

        /// <summary>The station <paramref name="index"/> of <paramref name="node"/>.</summary>
        public static StationId Of(NodeId node, int index)
        {
            if ((uint)index > MaxIndex) throw new ArgumentOutOfRangeException(nameof(index), index, "a station index is 0.." + MaxIndex);
            if ((uint)node.Value > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(node), node.Value, "a node id of a station fits 16 bits");
            return new StationId(node.Value | (index << 16));
        }

        /// <summary>The node's station 0.</summary>
        public static StationId Of(NodeId node) => Of(node, 0);

        /// <summary>The node hosting this station (the low 16 bits).</summary>
        public NodeId Node => new(Value & 0xFFFF);

        /// <summary>The seat on that node (the high 16 bits).</summary>
        public int Index => (int)((uint)Value >> 16);

        public static implicit operator int(StationId id) => id.Value;
        /// <summary>⭐ Explicit: a raw number (already packed) becomes a station only on purpose.</summary>
        public static explicit operator StationId(int value) => new(value);
        /// <summary>⭐ EXPLICIT (CE-11027): a node's own station 0 — the single-station node's id is unchanged. ⛔ Never implicit: a <c>(NodeId)</c> hop on a station that arrived as a plain int silently drops its seat; say <see cref="Of(NodeId)"/> for a real node, <c>(StationId)value</c> for a station int.</summary>
        public static explicit operator StationId(NodeId node) => Of(node, 0);

        public bool Equals(StationId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is StationId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(StationId other) => Value.CompareTo(other.Value);
        public static bool operator ==(StationId a, StationId b) => a.Value == b.Value;
        public static bool operator !=(StationId a, StationId b) => a.Value != b.Value;
        // mixed with a raw int (the same bytes): exact, not ambiguous
        public static bool operator ==(StationId a, int b) => a.Value == b;
        public static bool operator !=(StationId a, int b) => a.Value != b;
        public static bool operator ==(int a, StationId b) => a == b.Value;
        public static bool operator !=(int a, StationId b) => a != b.Value;

        /// <summary>"station 3" for index 0, "station 3/2" otherwise.</summary>
        public override string ToString()
            => Index == 0 ? "station " + Node.Value.ToString(CultureInfo.InvariantCulture)
                          : "station " + Node.Value.ToString(CultureInfo.InvariantCulture) + "/" + Index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary><see cref="StationId"/> exactly as an <see cref="int"/>: a plain JSON number.</summary>
    public sealed class StationIdJsonConverter : JsonConverter<StationId>
    {
        public override StationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => (StationId)JsonSerializer.Deserialize<int>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, StationId value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Value, options);
    }

    /// <summary><see cref="StationId"/> from / to a string or an <see cref="int"/> (the packed number), as <see cref="NodeIdTypeConverter"/> does for a node.</summary>
    public sealed class StationIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(int) || base.CanConvertFrom(context, sourceType);
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || destinationType == typeof(int) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
        {
            string s => (StationId)int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture),
            int i => (StationId)i,
            _ => base.ConvertFrom(context, culture, value),
        };

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is StationId id)
            {
                if (destinationType == typeof(string)) return id.Value.ToString(CultureInfo.InvariantCulture);
                if (destinationType == typeof(int)) return id.Value;
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

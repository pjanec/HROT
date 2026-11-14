using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ A TEMPLATE TYPE — the id of a TKB template (a vehicle, a weapon, a munition, a prop). 📄 <c>docs/DESIGN_Semantic_Value_Types.md</c> SV-2
    /// (CE-4200).
    /// <para>
    /// The TYPE carries the meaning a bare <see cref="long"/> cannot: since entity network ids became <see cref="int"/>, a template id must
    /// not be swappable with one by accident. ⭐ Memory and JSON are a <see cref="long"/>'s exactly (one 8-byte field; a plain number), so
    /// recordings, blobs and scenario files are byte-identical. Conversion to <see cref="long"/> is implicit (every API that takes a raw template
    /// id keeps compiling); conversion FROM <see cref="long"/> is explicit — ⛔ and there is none from <see cref="int"/>, so an entity id can
    /// never silently become a template id.
    /// </para>
    /// <para>The NAME ("BMP-2") needs the TKB: <see cref="ToString"/> is the number, and a tool shows the name through the TKB
    /// (<c>DtoJsonSchemaExtractor</c> marks the field <c>format: tkbTypeId</c>).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(TkbTypeIdJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct TkbTypeId : IEquatable<TkbTypeId>, IComparable<TkbTypeId>
    {
        /// <summary>The template id.</summary>
        public readonly long Value;

        public TkbTypeId(long value) => Value = value;

        /// <summary>No template (<c>0</c>).</summary>
        public static readonly TkbTypeId None = default;

        /// <summary><c>true</c> when no template is named.</summary>
        public bool IsNone => Value == 0;

        public static implicit operator long(TkbTypeId id) => id.Value;
        /// <summary>⭐ Explicit: a raw number becomes a template id only on purpose. ⛔ No conversion from <see cref="int"/> (an entity id).</summary>
        public static explicit operator TkbTypeId(long value) => new(value);

        public bool Equals(TkbTypeId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is TkbTypeId o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(TkbTypeId other) => Value.CompareTo(other.Value);
        public static bool operator ==(TkbTypeId a, TkbTypeId b) => a.Value == b.Value;
        public static bool operator !=(TkbTypeId a, TkbTypeId b) => a.Value != b.Value;
        // mixed with a raw long (the same bytes): exact, not ambiguous — a template id compared with a number not yet typed
        public static bool operator ==(TkbTypeId a, long b) => a.Value == b;
        public static bool operator !=(TkbTypeId a, long b) => a.Value != b;
        public static bool operator ==(long a, TkbTypeId b) => a == b.Value;
        public static bool operator !=(long a, TkbTypeId b) => a != b.Value;
        public static bool operator <(TkbTypeId a, TkbTypeId b) => a.Value < b.Value;
        public static bool operator >(TkbTypeId a, TkbTypeId b) => a.Value > b.Value;
        public static bool operator <=(TkbTypeId a, TkbTypeId b) => a.Value <= b.Value;
        public static bool operator >=(TkbTypeId a, TkbTypeId b) => a.Value >= b.Value;

        /// <summary>The number (the name needs the TKB).</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>The number, formatted with <paramref name="provider"/> (the call shape a plain <see cref="long"/> had).</summary>
        public string ToString(IFormatProvider? provider) => Value.ToString(provider);
    }

    /// <summary><see cref="TkbTypeId"/> exactly as a <see cref="long"/>: a plain JSON number, read from a number or a numeric string.</summary>
    public sealed class TkbTypeIdJsonConverter : JsonConverter<TkbTypeId>
    {
        public override TkbTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<long>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, TkbTypeId value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Value, options);
    }
}

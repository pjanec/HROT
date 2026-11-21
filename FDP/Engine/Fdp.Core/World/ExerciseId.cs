using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ AN EXERCISE — a <see cref="Guid"/> that a tool can OPEN (the exercise inventory, its recordings); ONE type for the concept.
    /// 📄 <c>docs/DESIGN_Semantic_Value_Types.md</c> SV-6 (CE-4204). ⛔ Correlation tokens (<c>RequestId</c>, <c>TransactionId</c>, <c>SessionId</c>) nobody navigates to stay plain <see cref="Guid"/>.
    /// <para>
    /// ⭐ Memory and JSON are a <see cref="Guid"/>'s exactly (one 16-byte field; a plain GUID string), so recordings, blobs and config files are byte-identical.
    /// Conversion to <see cref="Guid"/> is implicit (every API taking a raw GUID keeps compiling); conversion FROM <see cref="Guid"/> is explicit.
    /// </para>
    /// <para>On the DDS wire it travels as a plain <see cref="Guid"/> (the IDL has no wrapper type); the type stops at the topic boundary.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(ExerciseIdJsonConverter))]
    [TypeConverter(typeof(ExerciseIdTypeConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct ExerciseId : IEquatable<ExerciseId>, IComparable<ExerciseId>
    {
        /// <summary>The GUID.</summary>
        public readonly Guid Value;

        public ExerciseId(Guid value) => Value = value;

        /// <summary>No exercise (<see cref="Guid.Empty"/>).</summary>
        public static readonly ExerciseId Empty = default;

        /// <summary>A fresh random id.</summary>
        public static ExerciseId NewId() => new(Guid.NewGuid());

        public static implicit operator Guid(ExerciseId id) => id.Value;
        /// <summary>⭐ Explicit: a raw GUID becomes this id only on purpose.</summary>
        public static explicit operator ExerciseId(Guid value) => new(value);

        public bool Equals(ExerciseId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is ExerciseId o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ExerciseId other) => Value.CompareTo(other.Value);
        public static bool operator ==(ExerciseId a, ExerciseId b) => a.Value == b.Value;
        public static bool operator !=(ExerciseId a, ExerciseId b) => a.Value != b.Value;
        // mixed with a raw Guid (the same bytes): exact, not ambiguous
        public static bool operator ==(ExerciseId a, Guid b) => a.Value == b;
        public static bool operator !=(ExerciseId a, Guid b) => a.Value != b;
        public static bool operator ==(Guid a, ExerciseId b) => a == b.Value;
        public static bool operator !=(Guid a, ExerciseId b) => a != b.Value;

        /// <summary>The GUID's text.</summary>
        public override string ToString() => Value.ToString();

        /// <summary>The GUID formatted with <paramref name="format"/> (the call shape a plain <see cref="Guid"/> had).</summary>
        public string ToString(string? format) => Value.ToString(format);

        /// <summary>The GUID formatted with <paramref name="format"/> and <paramref name="provider"/>.</summary>
        public string ToString(string? format, IFormatProvider? provider) => Value.ToString(format, provider);
    }

    /// <summary><see cref="ExerciseId"/> exactly as a <see cref="Guid"/>: a plain JSON GUID string.</summary>
    public sealed class ExerciseIdJsonConverter : JsonConverter<ExerciseId>
    {
        public override ExerciseId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetGuid());

        public override void Write(Utf8JsonWriter writer, ExerciseId value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);

        // a dictionary key / property name
        public override ExerciseId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(Guid.Parse(reader.GetString()!));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, ExerciseId value, JsonSerializerOptions options)
            => writer.WritePropertyName(value.Value.ToString());
    }

    /// <summary>⭐ <see cref="ExerciseId"/> from / to a string or a <see cref="Guid"/> — what the command-line parser and the configuration binder use to fill a typed property.</summary>
    public sealed class ExerciseIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(Guid) || base.CanConvertFrom(context, sourceType);
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || destinationType == typeof(Guid) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
        {
            string s => new ExerciseId(Guid.Parse(s.Trim())),
            Guid g => new ExerciseId(g),
            _ => base.ConvertFrom(context, culture, value),
        };

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is ExerciseId id)
            {
                if (destinationType == typeof(string)) return id.Value.ToString();
                if (destinationType == typeof(Guid)) return id.Value;
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

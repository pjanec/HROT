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
    /// ⭐⭐ A RELATIVE ANGLE — radians (the SI unit, AQ107 I): a steering angle, a turret traverse or gun elevation, a head turn, a body's pitch and roll, a formation offset.
    /// 📄 <c>docs/DESIGN_Semantic_Value_Types.md</c> SV-4 (CE-4202). Beside <see cref="Bearing"/>, which is the WORLD compass (degrees, 0 = north): the pair says relative vs world.
    /// <para>
    /// The unit is in the TYPE, not in a name suffix: a tool shows the angle in degrees (<see cref="ToString"/> — <c>12.5°</c>) and edits it as degrees, and a
    /// radian can no longer be swapped with a degree by accident. ⭐ Memory and JSON are a <see cref="float"/>'s exactly (one 4-byte field; a plain number — radians),
    /// so recordings, blobs and config files are byte-identical. Conversion to <see cref="float"/> is implicit (every use in arithmetic and trig keeps compiling);
    /// conversion FROM <see cref="float"/> is explicit. <c>Angle ± Angle</c> and <c>−Angle</c> stay an <see cref="Angle"/>.
    /// </para>
    /// <para>⛔ NOT an <see cref="Angle"/>: a WORLD direction (a heading, a bearing — <see cref="Bearing"/>), an angular RATE (rad/s), a ratio, a field authored in degrees (<c>*Deg</c>) that has its own unit.
    /// On the DDS wire an angle travels as a plain <c>float</c>; the type stops at the topic boundary.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(AngleJsonConverter))]
    [TypeConverter(typeof(AngleTypeConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct Angle : IEquatable<Angle>, IComparable<Angle>
    {
        /// <summary>The angle, radians.</summary>
        public readonly float Radians;

        public Angle(float radians) => Radians = radians;

        /// <summary>No angle (zero).</summary>
        public static readonly Angle Zero = default;

        /// <summary>An angle from degrees.</summary>
        public static Angle FromDegrees(float degrees) => new(degrees * (MathF.PI / 180f));

        /// <summary>The angle in degrees.</summary>
        public float Degrees => Radians * (180f / MathF.PI);

        public static implicit operator float(Angle a) => a.Radians;
        /// <summary>⭐ Explicit: a raw number becomes an angle only on purpose (its unit is radians).</summary>
        public static explicit operator Angle(float radians) => new(radians);

        public static Angle operator +(Angle a, Angle b) => new(a.Radians + b.Radians);
        public static Angle operator -(Angle a, Angle b) => new(a.Radians - b.Radians);
        public static Angle operator -(Angle a) => new(-a.Radians);

        public bool Equals(Angle other) => Radians == other.Radians;
        public override bool Equals(object? obj) => obj is Angle o && Equals(o);
        public override int GetHashCode() => Radians.GetHashCode();
        public int CompareTo(Angle other) => Radians.CompareTo(other.Radians);
        public static bool operator ==(Angle a, Angle b) => a.Radians == b.Radians;
        public static bool operator !=(Angle a, Angle b) => a.Radians != b.Radians;

        /// <summary><c>12.5°</c> — degrees, one decimal, invariant culture.</summary>
        public override string ToString() => Degrees.ToString("0.0", CultureInfo.InvariantCulture) + "°";
    }

    /// <summary><see cref="Angle"/> exactly as a <see cref="float"/>: a plain JSON number (radians).</summary>
    public sealed class AngleJsonConverter : JsonConverter<Angle>
    {
        public override Angle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<float>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Angle value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Radians, options);
    }

    /// <summary>⭐ <see cref="Angle"/> from / to a string or a <see cref="float"/> (radians) — what the configuration binder uses to fill a typed property.</summary>
    public sealed class AngleTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(float) || base.CanConvertFrom(context, sourceType);
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || destinationType == typeof(float) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
        {
            string s => new Angle(float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)),
            float f => new Angle(f),
            _ => base.ConvertFrom(context, culture, value),
        };

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is Angle a)
            {
                if (destinationType == typeof(string)) return a.Radians.ToString(CultureInfo.InvariantCulture);
                if (destinationType == typeof(float)) return a.Radians;
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

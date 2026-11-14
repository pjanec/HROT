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
    /// ⭐⭐ A POINT in simulation time — seconds of sim time since the exercise started, a <see cref="double"/> (a <c>float</c> resolves only ~4 ms at 10 h).
    /// 📄 <c>docs/DESIGN_Semantic_Value_Types.md</c> SV-1 (CE-4201). Beside <see cref="SimDuration"/>: the pair says <i>when</i> vs <i>how long</i>.
    /// <para>
    /// ⭐ Memory and JSON are a <see cref="double"/>'s exactly (one 8-byte field; a plain number — seconds). <c>instant − instant</c> is a <see cref="SimDuration"/>,
    /// <c>instant ± duration</c> an instant. ⛔ <c>instant + instant</c> does not compile (the operator exists and is an <see cref="ObsoleteAttribute"/> ERROR — without it the
    /// implicit conversion to <see cref="double"/> would let it through). Conversion to <see cref="double"/> is implicit (comparisons and arithmetic against the
    /// <c>double</c> clocks keep compiling); conversion FROM <see cref="double"/> is explicit — a raw number becomes an instant only on purpose.
    /// </para>
    /// <para>⛔ NOT a <see cref="SimInstant"/>: a duration or timeout (<see cref="SimDuration"/>), a wall-clock time, a frame number, a threshold that happens to be named <c>…At</c>.
    /// On the DDS wire an instant travels as a plain <c>double</c>; the type stops at the topic boundary.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(SimInstantJsonConverter))]
    [TypeConverter(typeof(SimInstantTypeConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct SimInstant : IEquatable<SimInstant>, IComparable<SimInstant>, IFormattable
    {
        /// <summary>Seconds of sim time.</summary>
        public readonly double Seconds;

        public SimInstant(double seconds) => Seconds = seconds;

        /// <summary>Time zero.</summary>
        public static readonly SimInstant Zero = default;

        public static implicit operator double(SimInstant t) => t.Seconds;
        /// <summary>⭐ Explicit: a raw number becomes an instant only on purpose.</summary>
        public static explicit operator SimInstant(double seconds) => new(seconds);

        public static SimDuration operator -(SimInstant a, SimInstant b) => new((float)(a.Seconds - b.Seconds));
        public static SimInstant operator +(SimInstant a, SimDuration d) => new(a.Seconds + d.Seconds);
        public static SimInstant operator -(SimInstant a, SimDuration d) => new(a.Seconds - d.Seconds);
        /// <summary>⛔ Two instants do not add. (Declared only so overload resolution lands here and fails, instead of quietly going through <see cref="double"/>.)</summary>
        [Obsolete("SimInstant + SimInstant is meaningless: add a SimDuration to an instant, or subtract two instants.", true)]
        public static SimInstant operator +(SimInstant a, SimInstant b) => throw new NotSupportedException();

        public static bool operator <(SimInstant a, SimInstant b) => a.Seconds < b.Seconds;
        public static bool operator >(SimInstant a, SimInstant b) => a.Seconds > b.Seconds;
        public static bool operator <=(SimInstant a, SimInstant b) => a.Seconds <= b.Seconds;
        public static bool operator >=(SimInstant a, SimInstant b) => a.Seconds >= b.Seconds;
        public static bool operator ==(SimInstant a, SimInstant b) => a.Seconds == b.Seconds;
        public static bool operator !=(SimInstant a, SimInstant b) => a.Seconds != b.Seconds;

        public bool Equals(SimInstant other) => Seconds == other.Seconds;
        public override bool Equals(object? obj) => obj is SimInstant o && Equals(o);
        public override int GetHashCode() => Seconds.GetHashCode();
        public int CompareTo(SimInstant other) => Seconds.CompareTo(other.Seconds);

        /// <summary><c>t=123.5 s</c> — seconds of sim time, one decimal, invariant culture (a tool shows the scenario clock through <c>ScenarioClock</c>).</summary>
        public override string ToString() => "t=" + Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        /// <summary>⭐ With a format (<c>{t:0.0}</c>, <c>ToString("F3")</c>, <c>:R</c>) the plain NUMBER of seconds in that format — exactly what the <see cref="double"/> this replaced printed, so interpolations and
        /// hand-built JSON do not change; with none, <see cref="ToString()"/>.</summary>
        public string ToString(string? format, IFormatProvider? provider) => string.IsNullOrEmpty(format) ? ToString() : Seconds.ToString(format, provider);
    }

    /// <summary>
    /// ⭐⭐ A LENGTH of simulation time — seconds, a <see cref="float"/> (a timeout, a cooldown, a dwell, an age). 📄 SV-1 (CE-4201). Memory and JSON are a <see cref="float"/>'s.
    /// Implicit both ways with <see cref="float"/> (so every existing duration call site keeps compiling); <c>duration ± duration</c>, <c>−duration</c> stay a duration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(SimDurationJsonConverter))]
    [TypeConverter(typeof(SimDurationTypeConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct SimDuration : IEquatable<SimDuration>, IComparable<SimDuration>, IFormattable
    {
        /// <summary>Seconds.</summary>
        public readonly float Seconds;

        public SimDuration(float seconds) => Seconds = seconds;

        /// <summary>No time (zero).</summary>
        public static readonly SimDuration Zero = default;

        public static implicit operator float(SimDuration d) => d.Seconds;
        public static implicit operator SimDuration(float seconds) => new(seconds);

        public static SimDuration operator +(SimDuration a, SimDuration b) => new(a.Seconds + b.Seconds);
        public static SimDuration operator -(SimDuration a, SimDuration b) => new(a.Seconds - b.Seconds);
        public static SimDuration operator -(SimDuration a) => new(-a.Seconds);

        public bool Equals(SimDuration other) => Seconds == other.Seconds;
        public override bool Equals(object? obj) => obj is SimDuration o && Equals(o);
        public override int GetHashCode() => Seconds.GetHashCode();
        public int CompareTo(SimDuration other) => Seconds.CompareTo(other.Seconds);
        public static bool operator ==(SimDuration a, SimDuration b) => a.Seconds == b.Seconds;
        public static bool operator !=(SimDuration a, SimDuration b) => a.Seconds != b.Seconds;

        /// <summary>⭐ With a format the plain number of seconds in that format (what the <see cref="float"/> this replaced printed); with none, <see cref="ToString()"/>.</summary>
        public string ToString(string? format, IFormatProvider? provider) => string.IsNullOrEmpty(format) ? ToString() : Seconds.ToString(format, provider);

        /// <summary><c>1 min 12 s</c> above a minute, else <c>12.5 s</c>; invariant culture.</summary>
        public override string ToString()
        {
            float s = MathF.Abs(Seconds);
            if (s < 60f) return Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            int total = (int)MathF.Round(s);
            string sign = Seconds < 0 ? "-" : "";
            return total >= 3600
                ? $"{sign}{total / 3600} h {total % 3600 / 60} min {total % 60} s"
                : $"{sign}{total / 60} min {total % 60} s";
        }
    }

    /// <summary><see cref="SimInstant"/> exactly as a <see cref="double"/>: a plain JSON number (seconds).</summary>
    public sealed class SimInstantJsonConverter : JsonConverter<SimInstant>
    {
        // ⭐ CE-3441 — "never" is a real value (SquadUnitSense.AskedAt = -∞), but JSON has no infinity: System.Text.Json REFUSES it and every scenario
        //   golden snapshot threw on its first frame. A non-finite value goes out as a marker string and comes back from it — ONE place for every
        //   SimInstant field, and a finite value stays a plain number (golden files unchanged).
        public override SimInstant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String ? new(NonFinite.Parse(reader.GetString())) : new(reader.GetDouble());
        public override void Write(Utf8JsonWriter writer, SimInstant value, JsonSerializerOptions options)
        {
            if (double.IsFinite(value.Seconds)) writer.WriteNumberValue(value.Seconds);
            else NonFinite.Write(writer, value.Seconds);
        }
    }

    /// <summary>⭐ CE-3441 — the markers a non-finite time is written as (JSON has no infinity): <c>"-inf"</c>, <c>"+inf"</c>, <c>"nan"</c>.</summary>
    internal static class NonFinite
    {
        public const string MinusInf = "-inf", PlusInf = "+inf", NaN = "nan";
        public static string Marker(double v) => double.IsNaN(v) ? NaN : v < 0 ? MinusInf : PlusInf;
        // written RAW: the default encoder escapes '+' ("\u002Binf") — legal, but not what a golden file or a reader should see
        private static readonly byte[] s_minus = "\"-inf\""u8.ToArray(), s_plus = "\"+inf\""u8.ToArray(), s_nan = "\"nan\""u8.ToArray();
        public static void Write(Utf8JsonWriter writer, double v)
            => writer.WriteRawValue(double.IsNaN(v) ? s_nan : v < 0 ? s_minus : s_plus, skipInputValidation: true);
        public static double Parse(string? s) => s switch
        {
            MinusInf => double.NegativeInfinity,
            PlusInf => double.PositiveInfinity,
            NaN => double.NaN,
            _ => double.Parse(s ?? throw new JsonException("a time is null"), NumberStyles.Float, CultureInfo.InvariantCulture),
        };
    }

    /// <summary><see cref="SimDuration"/> exactly as a <see cref="float"/>: a plain JSON number (seconds).</summary>
    public sealed class SimDurationJsonConverter : JsonConverter<SimDuration>
    {
        public override SimDuration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String ? new((float)NonFinite.Parse(reader.GetString())) : new(reader.GetSingle());
        public override void Write(Utf8JsonWriter writer, SimDuration value, JsonSerializerOptions options)
        {
            if (float.IsFinite(value.Seconds)) writer.WriteNumberValue(value.Seconds);
            else NonFinite.Write(writer, value.Seconds);   // ⭐ CE-3441 — as SimInstant: "forever" survives JSON
        }
    }

    /// <summary>⭐ <see cref="SimInstant"/> from / to a string or a <see cref="double"/> (seconds) — what the configuration binder uses.</summary>
    public sealed class SimInstantTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(double) || base.CanConvertFrom(context, sourceType);
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || destinationType == typeof(double) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
        {
            string s => new SimInstant(double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)),
            double d => new SimInstant(d),
            _ => base.ConvertFrom(context, culture, value),
        };

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is SimInstant t)
            {
                if (destinationType == typeof(string)) return t.Seconds.ToString(CultureInfo.InvariantCulture);
                if (destinationType == typeof(double)) return t.Seconds;
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    /// <summary><see cref="SimDuration"/> from / to a string or a <see cref="float"/> (seconds).</summary>
    public sealed class SimDurationTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || sourceType == typeof(float) || base.CanConvertFrom(context, sourceType);
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || destinationType == typeof(float) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
        {
            string s => new SimDuration(float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)),
            float f => new SimDuration(f),
            _ => base.ConvertFrom(context, culture, value),
        };

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is SimDuration d)
            {
                if (destinationType == typeof(string)) return d.Seconds.ToString(CultureInfo.InvariantCulture);
                if (destinationType == typeof(float)) return d.Seconds;
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

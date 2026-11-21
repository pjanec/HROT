using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ A WORLD COMPASS BEARING — degrees, 0 = north, 90 = east, clockwise (the <see cref="WorldRotation"/> convention).
    /// 📄 <c>docs/DESIGN_Scenario_Rules.md</c> §9.8.10 SR-143.
    /// <para>
    /// The unit and the reference are in the TYPE, not in a name suffix: a tool draws it as an arrow at its owner, the relocation walker
    /// adds the heading difference. Four bytes, a plain number in JSON, implicit both ways with <see cref="float"/> so arithmetic and
    /// comparisons keep compiling. ⛔ A RELATIVE angle (steering, an offset, an arc's half-width, a body yaw) stays a <see cref="float"/> —
    /// it does not turn with a macro.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(BearingJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public struct Bearing : IEquatable<Bearing>
    {
        /// <summary>Compass degrees (not necessarily normalised — <see cref="Normalized"/> is).</summary>
        public float Degrees;

        public Bearing(float degrees) => Degrees = degrees;

        public static implicit operator float(Bearing b) => b.Degrees;
        public static implicit operator Bearing(float degrees) => new(degrees);

        /// <summary>The same bearing in [0, 360).</summary>
        public readonly Bearing Normalized => new(((Degrees % 360f) + 360f) % 360f);

        /// <summary>The level world rotation facing this bearing.</summary>
        public readonly WorldRotation ToRotation() => WorldRotation.FromHeadingDeg(Degrees);

        public readonly bool Equals(Bearing other) => Degrees == other.Degrees;
        public override readonly bool Equals(object? obj) => obj is Bearing b && Equals(b);
        public override readonly int GetHashCode() => Degrees.GetHashCode();

        /// <summary><c>045.0°</c> — normalised, one decimal, invariant culture.</summary>
        public override readonly string ToString()
            => Normalized.Degrees.ToString("000.0", CultureInfo.InvariantCulture) + "°";
    }

    /// <summary><see cref="Bearing"/> as the bare number of degrees, exactly as the <see cref="float"/> field it replaces.</summary>
    public sealed class BearingJsonConverter : JsonConverter<Bearing>
    {
        public override Bearing Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<float>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, Bearing value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Degrees, options);
    }
}

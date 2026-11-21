using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ A PLACE IN THE WORLD — local ENU metres (X = east, Y = north, Z = up), what code writes (components, lists, the recorder).
    /// 📄 <c>docs/DESIGN_Scenario_Rules.md</c> §9.8.9 SR-141.
    /// <para>
    /// The TYPE carries the meaning a bare <see cref="Vector3"/> cannot: tools show it on the map, the editor picks it on the map, the
    /// relocation walker moves it — and a direction or a velocity stays a <see cref="Vector3"/>. ⭐ Memory and JSON are
    /// <see cref="Vector3"/>'s exactly (three floats; <c>[x, y, z]</c> through whatever <see cref="Vector3"/> converter the options carry),
    /// so recordings, blobs and scenario files are byte-identical; implicit conversions both ways keep every call site compiling.
    /// </para>
    /// <para>⭐ A difference of two points is a <see cref="Vector3"/> (a displacement); a point plus a displacement is a point.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(WorldPointJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public struct WorldPoint : IEquatable<WorldPoint>
    {
        /// <summary>Metres east of the scenario origin.</summary>
        public float X;
        /// <summary>Metres north of the scenario origin.</summary>
        public float Y;
        /// <summary>Metres up (above the vertical datum).</summary>
        public float Z;

        public WorldPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        public WorldPoint(Vector3 v) { X = v.X; Y = v.Y; Z = v.Z; }

        /// <summary>The origin.</summary>
        public static WorldPoint Zero => default;

        /// <summary>This point as a <see cref="Vector3"/> (for maths that wants one explicitly).</summary>
        public readonly Vector3 AsVector3 => new(X, Y, Z);

        /// <summary>The ground-plane part (X, Y).</summary>
        public readonly Vector2 XY => new(X, Y);

        public static implicit operator Vector3(WorldPoint p) => new(p.X, p.Y, p.Z);
        public static implicit operator WorldPoint(Vector3 v) => new(v.X, v.Y, v.Z);

        // ⭐ Vector3's own operators are not candidates for two WorldPoint operands (C# looks only at the operand types), so the
        //   arithmetic a point takes part in is declared here — with the result types of the geometry, not of the storage.
        public static Vector3 operator -(WorldPoint a, WorldPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static WorldPoint operator +(WorldPoint p, Vector3 d) => new(p.X + d.X, p.Y + d.Y, p.Z + d.Z);
        public static WorldPoint operator +(Vector3 d, WorldPoint p) => new(p.X + d.X, p.Y + d.Y, p.Z + d.Z);
        public static WorldPoint operator -(WorldPoint p, Vector3 d) => new(p.X - d.X, p.Y - d.Y, p.Z - d.Z);
        /// <summary>A sum of points (a centroid's numerator) is not a place — it is a <see cref="Vector3"/>.</summary>
        public static Vector3 operator +(WorldPoint a, WorldPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator *(WorldPoint p, float s) => new(p.X * s, p.Y * s, p.Z * s);
        public static Vector3 operator *(float s, WorldPoint p) => new(p.X * s, p.Y * s, p.Z * s);
        public static Vector3 operator /(WorldPoint p, float s) => new(p.X / s, p.Y / s, p.Z / s);
        public static Vector3 operator -(WorldPoint p) => new(-p.X, -p.Y, -p.Z);
        /// <summary>A bare <see cref="Vector3"/> (a place not yet typed) minus a point is the displacement between them — declared so the mixed form
        /// is not ambiguous between <see cref="Vector3"/>'s operator and the point-point one above.</summary>
        public static Vector3 operator -(Vector3 a, WorldPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public readonly bool Equals(WorldPoint other) => X == other.X && Y == other.Y && Z == other.Z;
        public override readonly bool Equals(object? obj) => obj is WorldPoint p && Equals(p);
        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
        public static bool operator ==(WorldPoint a, WorldPoint b) => a.Equals(b);
        public static bool operator !=(WorldPoint a, WorldPoint b) => !a.Equals(b);
        // mixed with a bare Vector3 (the same bytes): a place compared with a value not yet typed — exact, not ambiguous
        public static bool operator ==(WorldPoint a, Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(WorldPoint a, Vector3 b) => !(a == b);
        public static bool operator ==(Vector3 a, WorldPoint b) => b == a;
        public static bool operator !=(Vector3 a, WorldPoint b) => !(b == a);

        /// <summary><c>(x, y, z) m</c>, invariant culture, one decimal.</summary>
        public override readonly string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"({X:0.0}, {Y:0.0}, {Z:0.0}) m");
    }

    /// <summary>
    /// <see cref="WorldPoint"/> exactly as <see cref="Vector3"/>: it hands the value to the options' own <see cref="Vector3"/> handling,
    /// so a file (the <c>[x, y, z]</c> array converter) and a debug dump (its NaN-safe converter) read and write the same bytes they do
    /// for a <see cref="Vector3"/> — by construction, not by a copy of either converter.
    /// </summary>
    public sealed class WorldPointJsonConverter : JsonConverter<WorldPoint>
    {
        public override WorldPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<Vector3>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, WorldPoint value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, (Vector3)value, options);
    }
}

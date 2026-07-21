using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core.Serialization.Converters
{
    /// <summary>
    /// Shared numeric formatter for the compact vector/quaternion converters below.
    /// <para>
    /// These converters emit their components via <see cref="Utf8JsonWriter.WriteRawValue(string,bool)"/>,
    /// whose default input validation REJECTS non-finite tokens: <c>float.ToString("G9")</c> renders NaN /
    /// ±Infinity as the bare words <c>NaN</c>/<c>Infinity</c>/<c>-Infinity</c>, which are not valid JSON, so
    /// the writer throws and — during a whole-scenario save — takes the editor down. A single stray non-finite
    /// component (an uninitialised transform, a divide-by-zero upstream) must never crash serialization, so we
    /// clamp non-finite values to <c>0</c>. Finite values are unchanged (identical G9 output as before).
    /// </para>
    /// </summary>
    internal static class VectorJsonFormat
    {
        public static string F(float v) =>
            float.IsFinite(v) ? v.ToString("G9", CultureInfo.InvariantCulture) : "0";
    }

    /// <summary>
    /// Serializes/deserializes <see cref="Vector2"/> as a compact single-line JSON array
    /// <c>[x, y]</c> rather than the default verbose object form.
    /// Used in <see cref="FdpJsonOptionsRegistry.DefaultRelaxed"/> so all <c>Vector2</c>-typed
    /// component fields are written as arrays without changing any component definitions.
    /// </summary>
    public class Vector2ArrayConverter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // reader is positioned at StartArray.
            reader.Read(); float x = reader.GetSingle();
            reader.Read(); float y = reader.GetSingle();
            reader.Read(); // EndArray
            return new Vector2(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            // WriteRawValue bypasses the global indentation rules so the array stays on one line
            // even when WriteIndented = true is set on the enclosing JsonSerializerOptions.
            string x = VectorJsonFormat.F(value.X);
            string y = VectorJsonFormat.F(value.Y);
            writer.WriteRawValue($"[{x}, {y}]");
        }
    }

    /// <summary>
    /// Serializes/deserializes <see cref="Vector3"/> as a compact single-line JSON array
    /// <c>[x, y, z]</c> rather than the default verbose object form.
    /// </summary>
    public class Vector3ArrayConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = reader.GetSingle();
            reader.Read(); float y = reader.GetSingle();
            reader.Read(); float z = reader.GetSingle();
            reader.Read(); // EndArray
            return new Vector3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            string x = VectorJsonFormat.F(value.X);
            string y = VectorJsonFormat.F(value.Y);
            string z = VectorJsonFormat.F(value.Z);
            writer.WriteRawValue($"[{x}, {y}, {z}]");
        }
    }

    /// <summary>
    /// Serializes/deserializes <see cref="Quaternion"/> as a compact single-line JSON array
    /// <c>[x, y, z, w]</c> rather than the default verbose object form.
    /// </summary>
    public class QuaternionArrayConverter : JsonConverter<Quaternion>
    {
        public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = reader.GetSingle();
            reader.Read(); float y = reader.GetSingle();
            reader.Read(); float z = reader.GetSingle();
            reader.Read(); float w = reader.GetSingle();
            reader.Read(); // EndArray
            return new Quaternion(x, y, z, w);
        }

        public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
        {
            string x = VectorJsonFormat.F(value.X);
            string y = VectorJsonFormat.F(value.Y);
            string z = VectorJsonFormat.F(value.Z);
            string w = VectorJsonFormat.F(value.W);
            writer.WriteRawValue($"[{x}, {y}, {z}, {w}]");
        }
    }

    /// <summary>
    /// Serializes/deserializes <see cref="Vector4"/> as a compact single-line JSON array
    /// <c>[x, y, z, w]</c> rather than the default verbose object form.
    /// </summary>
    public class Vector4ArrayConverter : JsonConverter<Vector4>
    {
        public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = reader.GetSingle();
            reader.Read(); float y = reader.GetSingle();
            reader.Read(); float z = reader.GetSingle();
            reader.Read(); float w = reader.GetSingle();
            reader.Read(); // EndArray
            return new Vector4(x, y, z, w);
        }

        public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
        {
            string x = VectorJsonFormat.F(value.X);
            string y = VectorJsonFormat.F(value.Y);
            string z = VectorJsonFormat.F(value.Z);
            string w = VectorJsonFormat.F(value.W);
            writer.WriteRawValue($"[{x}, {y}, {z}, {w}]");
        }
    }
}

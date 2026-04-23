using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// Serializes/deserializes <see cref="Vector2"/> as a compact single-line JSON array
    /// <c>[x, y]</c> rather than the default verbose object form.
    /// Used in <see cref="FdpAutoSerializer._fieldAwareOptions"/> so all <c>Vector2</c>-typed
    /// component fields are written as arrays without changing any component definitions.
    /// </summary>
    internal sealed class Vector2ArrayConverter : JsonConverter<Vector2>
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
            string x = value.X.ToString("G9", CultureInfo.InvariantCulture);
            string y = value.Y.ToString("G9", CultureInfo.InvariantCulture);
            writer.WriteRawValue($"[{x}, {y}]");
        }
    }

    /// <summary>
    /// Serializes/deserializes <see cref="Vector3"/> as a compact single-line JSON array
    /// <c>[x, y, z]</c> rather than the default verbose object form.
    /// </summary>
    internal sealed class Vector3ArrayConverter : JsonConverter<Vector3>
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
            string x = value.X.ToString("G9", CultureInfo.InvariantCulture);
            string y = value.Y.ToString("G9", CultureInfo.InvariantCulture);
            string z = value.Z.ToString("G9", CultureInfo.InvariantCulture);
            writer.WriteRawValue($"[{x}, {y}, {z}]");
        }
    }

    /// <summary>
    /// Serializes/deserializes <see cref="Quaternion"/> as a compact single-line JSON array
    /// <c>[x, y, z, w]</c> rather than the default verbose object form.
    /// </summary>
    internal sealed class QuaternionArrayConverter : JsonConverter<Quaternion>
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
            string x = value.X.ToString("G9", CultureInfo.InvariantCulture);
            string y = value.Y.ToString("G9", CultureInfo.InvariantCulture);
            string z = value.Z.ToString("G9", CultureInfo.InvariantCulture);
            string w = value.W.ToString("G9", CultureInfo.InvariantCulture);
            writer.WriteRawValue($"[{x}, {y}, {z}, {w}]");
        }
    }
}

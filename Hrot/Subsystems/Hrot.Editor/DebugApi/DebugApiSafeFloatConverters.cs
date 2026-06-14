using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// JSON converter for <see cref="float"/> that serializes non-finite values
    /// (<c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>) as their string sentinel equivalents
    /// (<c>"NaN"</c>, <c>"Infinity"</c>, <c>"-Infinity"</c>) instead of emitting the named
    /// literals that are rejected by standard-JSON parsers (including Node's <c>JSON.parse</c>).
    ///
    /// <para>Used exclusively by the DebugApi scoped options — does NOT affect the shared
    /// <see cref="Fdp.Core.Serialization.FdpJsonOptionsRegistry"/> singletons.</para>
    /// </summary>
    internal sealed class NonFiniteFloatSentinelConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                return s switch
                {
                    "NaN"       => float.NaN,
                    "Infinity"  => float.PositiveInfinity,
                    "-Infinity" => float.NegativeInfinity,
                    _           => float.Parse(s!, CultureInfo.InvariantCulture),
                };
            }
            return reader.GetSingle();
        }

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        {
            if (float.IsNaN(value))              { writer.WriteStringValue("NaN"); return; }
            if (float.IsPositiveInfinity(value)) { writer.WriteStringValue("Infinity"); return; }
            if (float.IsNegativeInfinity(value)) { writer.WriteStringValue("-Infinity"); return; }
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// JSON converter for <see cref="double"/> that serializes non-finite values as string sentinels.
    /// See <see cref="NonFiniteFloatSentinelConverter"/> for rationale.
    /// </summary>
    internal sealed class NonFiniteDoubleSentinelConverter : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                return s switch
                {
                    "NaN"       => double.NaN,
                    "Infinity"  => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    _           => double.Parse(s!, CultureInfo.InvariantCulture),
                };
            }
            return reader.GetDouble();
        }

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsNaN(value))              { writer.WriteStringValue("NaN"); return; }
            if (double.IsPositiveInfinity(value)) { writer.WriteStringValue("Infinity"); return; }
            if (double.IsNegativeInfinity(value)) { writer.WriteStringValue("-Infinity"); return; }
            writer.WriteNumberValue(value);
        }
    }

    // ── NaN-safe vector converters ────────────────────────────────────────────
    //
    // The shared VectorArrayConverters use WriteRawValue($"[{x}, {y}, ...]") which
    // produces the named literal "NaN" for non-finite components.  The DebugApi scoped
    // options replace them with these converters that emit a JSON string sentinel for
    // any non-finite component instead of a raw float literal.
    //
    // Format: each component is written as a JSON string if it is non-finite, or as a
    // JSON number otherwise.  The array stays compact (one line, no WriteIndented).

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Shared write/read helpers for the NaN-safe vector converters below:
    /// emits a JSON string sentinel for non-finite component values so the output is
    /// valid standard JSON accepted by Node's <c>JSON.parse</c> and any RFC-8259 parser.
    /// </summary>
    internal static class DebugApiConverterHelpers
    {
        internal static void WriteComponent(Utf8JsonWriter writer, float v)
        {
            if (float.IsNaN(v))              { writer.WriteStringValue("NaN"); return; }
            if (float.IsPositiveInfinity(v)) { writer.WriteStringValue("Infinity"); return; }
            if (float.IsNegativeInfinity(v)) { writer.WriteStringValue("-Infinity"); return; }
            writer.WriteNumberValue(v);
        }

        internal static float ReadComponent(ref Utf8JsonReader r) =>
            r.TokenType == JsonTokenType.String ? ParseSentinel(r.GetString()!) : r.GetSingle();

        internal static float ParseSentinel(string s) => s switch
        {
            "NaN"       => float.NaN,
            "Infinity"  => float.PositiveInfinity,
            "-Infinity" => float.NegativeInfinity,
            _           => float.Parse(s, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>NaN-safe <see cref="Vector2"/> converter for the DebugApi serialization scope.</summary>
    internal sealed class DebugApiVector2SafeConverter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float y = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); // EndArray
            return new Vector2(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            DebugApiConverterHelpers.WriteComponent(writer, value.X);
            DebugApiConverterHelpers.WriteComponent(writer, value.Y);
            writer.WriteEndArray();
        }
    }

    /// <summary>NaN-safe <see cref="Vector3"/> converter for the DebugApi serialization scope.</summary>
    internal sealed class DebugApiVector3SafeConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float y = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float z = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); // EndArray
            return new Vector3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            DebugApiConverterHelpers.WriteComponent(writer, value.X);
            DebugApiConverterHelpers.WriteComponent(writer, value.Y);
            DebugApiConverterHelpers.WriteComponent(writer, value.Z);
            writer.WriteEndArray();
        }
    }

    /// <summary>NaN-safe <see cref="Vector4"/> converter for the DebugApi serialization scope.</summary>
    internal sealed class DebugApiVector4SafeConverter : JsonConverter<Vector4>
    {
        public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float y = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float z = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float w = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); // EndArray
            return new Vector4(x, y, z, w);
        }

        public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            DebugApiConverterHelpers.WriteComponent(writer, value.X);
            DebugApiConverterHelpers.WriteComponent(writer, value.Y);
            DebugApiConverterHelpers.WriteComponent(writer, value.Z);
            DebugApiConverterHelpers.WriteComponent(writer, value.W);
            writer.WriteEndArray();
        }
    }

    /// <summary>NaN-safe <see cref="Quaternion"/> converter for the DebugApi serialization scope.</summary>
    internal sealed class DebugApiQuaternionSafeConverter : JsonConverter<Quaternion>
    {
        public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Read(); float x = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float y = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float z = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); float w = DebugApiConverterHelpers.ReadComponent(ref reader);
            reader.Read(); // EndArray
            return new Quaternion(x, y, z, w);
        }

        public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            DebugApiConverterHelpers.WriteComponent(writer, value.X);
            DebugApiConverterHelpers.WriteComponent(writer, value.Y);
            DebugApiConverterHelpers.WriteComponent(writer, value.Z);
            DebugApiConverterHelpers.WriteComponent(writer, value.W);
            writer.WriteEndArray();
        }
    }
}

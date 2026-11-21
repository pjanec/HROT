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

        /// <summary>
        /// Reads <paramref name="count"/> components of a vector/quaternion in EITHER shape:
        /// the compact array these converters write (<c>[x, y, z]</c>) or the named-field object
        /// (<c>{"X":…,"Y":…,"Z":…}</c>) that plain System.Text.Json and the editor's StructEdit
        /// surfaces produce. Missing object fields read as 0; unknown fields are skipped.
        /// </summary>
        /// <remarks>
        /// HN-002 — the read side used to accept only the array, so a caller could not write back
        /// what <c>GET /entities/{id}</c> had just handed it, and the object form a caller reached
        /// for instead only worked by accident of default serialization. Accepting both here keeps
        /// ONE pair of converters authoritative for the whole DebugApi scope, in both directions.
        /// </remarks>
        internal static void ReadComponents(ref Utf8JsonReader reader, scoped Span<float> components, string typeName)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                        throw new JsonException(
                            $"{typeName} needs {components.Length} components; the array had fewer.");
                    components[i] = ReadComponent(ref reader);
                }
                reader.Read(); // EndArray (or the first surplus element — see below)
                if (reader.TokenType != JsonTokenType.EndArray)
                    throw new JsonException(
                        $"{typeName} needs exactly {components.Length} components; the array had more.");
                return;
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                components.Clear();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    var name = reader.GetString();
                    reader.Read();
                    int index = name?.ToUpperInvariant() switch
                    {
                        "X" => 0, "Y" => 1, "Z" => 2, "W" => 3,
                        _   => -1,
                    };
                    if (index >= 0 && index < components.Length)
                        components[index] = ReadComponent(ref reader);
                    else
                        reader.Skip();
                }
                return;
            }

            throw new JsonException(
                $"{typeName} must be an array [x, y, …] or an object {{\"X\":…}}, "
                + $"but the value was {reader.TokenType}.");
        }
    }

    /// <summary>NaN-safe <see cref="Vector2"/> converter for the DebugApi serialization scope.</summary>
    internal sealed class DebugApiVector2SafeConverter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Span<float> c = stackalloc float[2];
            DebugApiConverterHelpers.ReadComponents(ref reader, c, nameof(Vector2));
            return new Vector2(c[0], c[1]);
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
            Span<float> c = stackalloc float[3];
            DebugApiConverterHelpers.ReadComponents(ref reader, c, nameof(Vector3));
            return new Vector3(c[0], c[1], c[2]);
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
            Span<float> c = stackalloc float[4];
            DebugApiConverterHelpers.ReadComponents(ref reader, c, nameof(Vector4));
            return new Vector4(c[0], c[1], c[2], c[3]);
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
            Span<float> c = stackalloc float[4];
            DebugApiConverterHelpers.ReadComponents(ref reader, c, nameof(Quaternion));
            return new Quaternion(c[0], c[1], c[2], c[3]);
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

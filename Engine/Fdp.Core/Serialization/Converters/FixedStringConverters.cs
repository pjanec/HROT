using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core.Serialization.Converters
{
    /// <summary>
    /// Serializes/deserializes <see cref="FixedString32"/> as a plain JSON string.
    /// Prevents the default struct serialization that would produce
    /// <c>{ "Length": N, "IsEmpty": false, ... }</c> instead of the string value.
    /// </summary>
    public class FixedString32Converter : JsonConverter<FixedString32>
    {
        public override FixedString32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new FixedString32(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, FixedString32 value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    /// <summary>
    /// Serializes/deserializes <see cref="FixedString64"/> as a plain JSON string.
    /// Prevents the default struct serialization that would produce
    /// <c>{ "Length": N, "IsEmpty": false, ... }</c> instead of the string value.
    /// </summary>
    public class FixedString64Converter : JsonConverter<FixedString64>
    {
        public override FixedString64 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new FixedString64(reader.GetString() ?? string.Empty);

        public override void Write(Utf8JsonWriter writer, FixedString64 value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}

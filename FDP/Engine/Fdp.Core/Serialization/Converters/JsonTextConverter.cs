using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Fdp.Core.Serialization.Converters
{
    /// <summary>
    /// ⭐ <c>CE-4295</c> (R-4159) — marks a <see cref="string"/> property that HOLDS JSON (a behaviour's params, a task's params, a trigger's
    /// params). The C# type stays a string — runtime, wire and every parser are untouched — and only its JSON form changes: written as the
    /// object / array it contains instead of an escaped string, read from either form. 📄 <c>docs/DESIGN_Readable_Scenario_Json.md</c> §2.
    /// <para>⭐ Attached to the PROPERTY, never added to <see cref="FdpJsonOptionsRegistry"/>: of the many users of the shared options only the
    /// marked fields change.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonTextAttribute : JsonConverterAttribute
    {
        public JsonTextAttribute() : base(typeof(JsonTextConverter)) { }
    }

    /// <summary>
    /// ⭐ <c>CE-4295</c> — the converter behind <see cref="JsonTextAttribute"/>.
    /// <list type="bullet">
    /// <item><b>Write:</b> a string whose text is a JSON object or array is written as that value, VERBATIM (<c>WriteRawValue</c>) — so
    /// string → object → string gives back the same text. Anything else (empty, a scalar such as a trigger's <c>"40"</c>, text that is not
    /// valid JSON) is written as the string it is — an author's text is never lost, and a scalar never changes its JSON type.</item>
    /// <item><b>Read:</b> a string (an old file) gives that string; an object / array (a new file) gives its raw text; a bare scalar gives its
    /// text; <c>null</c> gives <c>null</c>.</item>
    /// </list>
    /// </summary>
    public sealed class JsonTextConverter : JsonConverter<string>
    {
        public override bool HandleNull => false;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Null:
                    return null;
                default:
                    using (var doc = JsonDocument.ParseValue(ref reader))
                        return doc.RootElement.GetRawText();
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (IsStructured(value)) writer.WriteRawValue(value, skipInputValidation: true);
            else writer.WriteStringValue(value);
        }

        /// <summary>⭐ The text a JSON-holding field carries, read from a JSON TREE (a scenario walked as nodes, a request body): a string node gives
        /// itself (an old file), an object / array its JSON (a new one), <c>null</c> gives <c>null</c>. ⭐ The ONE reader for the tree form —
        /// the save path, the slot walk, the debug API and the tests all use it.</summary>
        public static string? TextOf(JsonNode? node)
            => node switch { null => null, JsonValue v when v.TryGetValue<string>(out var s) => s, _ => node.ToJsonString() };

        /// <summary>⭐ The node a JSON-holding field is written as in a tree: the object / array it holds, else the string itself — the same rule
        /// as <see cref="Write"/>.</summary>
        public static JsonNode? NodeOf(string? text)
            => text is null ? null : IsStructured(text) ? JsonNode.Parse(text) : JsonValue.Create(text);

        /// <summary>Is <paramref name="text"/> a valid JSON object or array (the form written as itself)?</summary>
        public static bool IsStructured(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i == text.Length || (text[i] != '{' && text[i] != '[')) return false;
            try
            {
                using var doc = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}

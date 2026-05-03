using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fdp.Toolkit.Serialization
{
    /// <summary>
    /// Post-processing utilities for JSON aesthetic formatting.
    ///
    /// <para>
    /// Provides <see cref="FlattenNumericArrays"/> which takes any JSON string (minified
    /// or indented) and returns a pretty-printed version where purely-numeric arrays are
    /// collapsed to a single line.  This is the canonical implementation replacing the
    /// private <c>WriteFormattedToken</c> / <c>IsPureNumericArray</c> pair that was
    /// previously embedded in <c>ScenarioFileService</c>.
    /// </para>
    /// </summary>
    public static class JsonAestheticFormatter
    {
        /// <summary>
        /// Returns a human-readable, indented JSON string in which purely-numeric arrays
        /// are collapsed to a single line (e.g. <c>[1.0, 2.0, 3.0]</c>).
        ///
        /// <para>
        /// Mixed arrays (containing strings, objects, or null) are written in the normal
        /// expanded form. Non-array tokens are written with standard indentation.
        /// </para>
        ///
        /// <para>
        /// The method is a pure function: same input always produces the same output,
        /// with no side effects or shared state.
        /// </para>
        /// </summary>
        /// <param name="rawJson">Any valid JSON string, minified or already indented.</param>
        /// <returns>Pretty-printed JSON with numeric arrays on single lines.</returns>
        public static string FlattenNumericArrays(string rawJson)
        {
            var rootToken  = JToken.Parse(rawJson);
            var sb         = new StringBuilder();
            using var sw   = new StringWriter(sb);
            using var jsonWriter = new JsonTextWriter(sw)
            {
                Formatting = Formatting.Indented,
            };
            WriteFormattedToken(rootToken, jsonWriter);
            jsonWriter.Flush();
            return sb.ToString();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void WriteFormattedToken(JToken token, JsonTextWriter writer)
        {
            if (token is JObject obj)
            {
                writer.WriteStartObject();
                foreach (var prop in obj.Properties())
                {
                    writer.WritePropertyName(prop.Name);
                    WriteFormattedToken(prop.Value, writer);
                }
                writer.WriteEndObject();
                return;
            }

            if (token is JArray array)
            {
                if (IsPureNumericArray(array))
                {
                    // Collapse numeric arrays to a single line by emitting raw JSON.
                    // This overrides the JsonTextWriter's indentation for this token.
                    var elements = new string[array.Count];
                    for (int i = 0; i < array.Count; i++)
                        elements[i] = array[i]!.ToString(Formatting.None);
                    writer.WriteRawValue($"[{string.Join(", ", elements)}]");
                    return;
                }

                writer.WriteStartArray();
                foreach (var item in array)
                    WriteFormattedToken(item, writer);
                writer.WriteEndArray();
                return;
            }

            token.WriteTo(writer);
        }

        private static bool IsPureNumericArray(JArray array)
        {
            if (array.Count == 0)
                return false;

            foreach (var item in array)
            {
                if (item == null)
                    return false;

                if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float)
                    return false;
            }

            return true;
        }
    }
}

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Kernel.FlightRecorder.Metadata
{
    public static class MetadataSerializer
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true
            // By default System.Text.Json ignores JSON properties that don't map to class members,
            // providing forward compatibility (new fields in JSON from newer versions are ignored).
        };

        public static string Serialize(RecordingMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            return JsonSerializer.Serialize(metadata, _options);
        }

        public static RecordingMetadata Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new RecordingMetadata();
            try 
            {
                return JsonSerializer.Deserialize<RecordingMetadata>(json, _options) ?? new RecordingMetadata();
            }
            catch (JsonException)
            {
                // Return default on error or throw? 
                // The instructions say "Handle forward/backward compatibility gracefully".
                // If the JSON is malformed, maybe we should throw or return empty.
                // I'll return a new empty metadata object if it fails, to be safe, or just let it throw.
                // Usually for a serializer utility, letting it throw invalid JSON is better.
                throw;
            }
        }
    }
}

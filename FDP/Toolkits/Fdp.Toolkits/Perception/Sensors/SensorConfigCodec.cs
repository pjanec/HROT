using System;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Sensors
{
    /// <summary>
    /// ⭐ R-186 M′ — a sensor's config on the wire: the TKB's OWN <see cref="SensorEntryDto"/>, as JSON, sent only on
    /// spawn / change (docs/DESIGN_Sensors_And_Doctrine.md §5.2). ⭐ The same options the TKB loader uses, so a sensor
    /// written in a TKB file and one sent by a behaviour are read by one rule.
    /// </summary>
    public static class SensorConfigCodec
    {
        /// <summary>The format tag of a payload holding a <see cref="SensorEntryDto"/>.</summary>
        public const string KindSensorEntry = "SensorEntry";

        public static string Encode(SensorEntryDto entry)
            => JsonSerializer.Serialize(entry, FdpJsonOptionsRegistry.DefaultRelaxed);

        /// <summary>
        /// Reads a payload. ⛔ Refuses (false + the reason) an unknown format, malformed JSON, or an entry that does not
        /// carry the sub-record of its own kind — a sensor is never built half-configured.
        /// </summary>
        public static bool TryDecode(string kind, string json, out SensorEntryDto? entry, out string? error)
        {
            entry = null;
            if (kind != KindSensorEntry) { error = $"unknown sensor config format '{kind}'"; return false; }
            try
            {
                entry = JsonSerializer.Deserialize<SensorEntryDto>(json, FdpJsonOptionsRegistry.DefaultRelaxed);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                error = $"malformed sensor config: {ex.Message}";
                return false;
            }
            if (entry is null || !entry.IsWellFormed)
            {
                error = $"sensor config does not carry the parameters of its kind '{entry?.Kind}'";
                entry = null;
                return false;
            }
            error = null;
            return true;
        }
    }
}

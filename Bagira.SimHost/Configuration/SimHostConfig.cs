using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagira.SimHost.Configuration
{
    /// <summary>
    /// Geodetic origin for WGS84 projection (config sub-object).
    /// </summary>
    public class GeodeticOriginConfig
    {
        public double Latitude  { get; set; } = 32.0853;
        public double Longitude { get; set; } = 34.7818;
        public double Altitude  { get; set; } = 10.0;
    }

    /// <summary>
    /// JSON-backed configuration for the SimHost application (TASK-S5.2).
    /// Call <see cref="Load"/> at startup; a <c>config.json</c> with defaults is
    /// generated automatically when the file does not yet exist.
    /// </summary>
    public class SimHostConfig
    {
        /// <summary>CycloneDDS domain ID (default 0).</summary>
        public int DomainId { get; set; } = 0;

        /// <summary>Target simulation rate in Hz (default 60).</summary>
        public int SimulationRateHz { get; set; } = 60;

        /// <summary>Geodetic reference origin for the WGS84 transform.</summary>
        public GeodeticOriginConfig GeodeticOrigin { get; set; } = new();

        // ── Persistence ───────────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Load config from <paramref name="filePath"/>.
        /// If the file does not exist the defaults are written and returned.
        /// </summary>
        public static SimHostConfig Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[SimHost] Config file not found: {filePath}, using defaults");
                var defaultConfig = new SimHostConfig();
                Save(defaultConfig, filePath);
                return defaultConfig;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<SimHostConfig>(json, _jsonOptions)
                       ?? new SimHostConfig();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SimHost] Failed to parse config: {ex.Message}, using defaults");
                return new SimHostConfig();
            }
        }

        /// <summary>Persist <paramref name="config"/> as indented JSON to <paramref name="filePath"/>.</summary>
        public static void Save(SimHostConfig config, string filePath)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(filePath, json);
            Console.WriteLine($"[SimHost] Config saved to: {filePath}");
        }
    }
}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.SimHost
{
    /// <summary>
    /// Geodetic reference origin for WGS-84 flat-earth projection (absorbed from SimHostConfig, DB-MOD1-09).
    /// </summary>
    public sealed record GeodeticOriginConfig
    {
        public double Latitude  { get; init; } = 32.0853;
        public double Longitude { get; init; } = 34.7818;
        public double Altitude  { get; init; } = 10.0;
    }

    /// <summary>
    /// JSON-serialisable configuration record for a SimHost node deployment.
    ///
    /// <para>
    /// Each deployed node role (Brain, MuscleGround, AllInOne, …) may use its own
    /// <c>NodeConfiguration</c> loaded from a role-specific JSON file in the
    /// <c>Config/</c> directory. When the file is absent or unparseable, all
    /// properties fall back to their default values.
    /// </para>
    ///
    /// <para>
    /// Use <see cref="LoadFrom"/> to resolve a configuration from disk, or
    /// call <see cref="Parse"/> to deserialise from a JSON string directly.
    /// </para>
    ///
    /// <para><b>DDS URI override:</b> If <see cref="CycloneDdsConfigPath"/> is non-empty,
    /// <see cref="ApplyEnvironment"/> sets the <c>CYCLONEDDS_URI</c> environment variable
    /// before the DDS participant is created — provided the variable is not already set
    /// by the process environment (external override wins).
    /// </para>
    /// </summary>
    public sealed record NodeConfiguration
    {
        // ── DDS / transport ───────────────────────────────────────────────────

        /// <summary>
        /// Path to the CycloneDDS XML configuration file.
        /// When non-empty, used to set <c>CYCLONEDDS_URI</c> at startup.
        /// </summary>
        public string CycloneDdsConfigPath  { get; init; } = string.Empty;

        /// <summary>DDS domain ID (default <c>42</c>).</summary>
        public uint DdsDomainId { get; init; } = 42;

        // ── Asset paths ───────────────────────────────────────────────────────

        /// <summary>
        /// File-system path to the road-network blob. Empty string means no road network.
        /// </summary>
        public string RoadNetworkBlobPath   { get; init; } = string.Empty;

        /// <summary>
        /// File-system path to the doctrine registry JSON. Empty string means use built-in.
        /// </summary>
        public string DoctrineRegistryPath  { get; init; } = string.Empty;

        /// <summary>
        /// File-system path to the entity template database. Empty string means use built-in.
        /// </summary>
        public string EntityTemplatePath    { get; init; } = string.Empty;

        // ── Simulation ────────────────────────────────────────────────────────

        /// <summary>Target simulation loop rate in Hz (default 60). Absorbed from SimHostConfig (DB-MOD1-09).</summary>
        public int SimulationRateHz { get; init; } = 60;

        /// <summary>Geodetic reference origin for WGS-84 projection (default: Tel Aviv area). Absorbed from SimHostConfig (DB-MOD1-09).</summary>
        public GeodeticOriginConfig GeodeticOrigin { get; init; } = new();

        // ── Persistence ───────────────────────────────────────────────────────

        /// <summary>
        /// Root directory for all node-local temporary data: pre-fetched scenario files,
        /// checkpoints, and exercise recording manifests.
        ///
        /// <para>
        /// Scenario staging uses sub-directories of the form
        /// <c>{LocalTempRoot}/{ScenarioId}/</c>.
        /// Checkpoint storage uses <c>{LocalTempRoot}/checkpoints/</c> so that both
        /// are co-located and share the same root for capacity planning and cleanup.
        /// Override in <c>config.json</c> on nodes where <c>C:\FDP_Temp</c> is not
        /// the correct volume (e.g. Linux deployments, non-<c>C:</c> drives).
        /// </para>
        /// </summary>
        public string LocalTempRoot { get; init; } = @"C:\FDP_Temp";

        // ── Serialisation ─────────────────────────────────────────────────────

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented       = true,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Loads a <see cref="NodeConfiguration"/> from <paramref name="filePath"/>.
        /// Returns a default-valued instance if the file does not exist or cannot be parsed.
        /// Never throws.
        /// </summary>
        /// <param name="filePath">Absolute or relative path to the JSON file.</param>
        public static NodeConfiguration LoadFrom(string filePath)
        {
            if (!File.Exists(filePath))
                return new NodeConfiguration();

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<NodeConfiguration>(json, _jsonOptions)
                       ?? new NodeConfiguration();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[NodeConfiguration] Failed to parse '{filePath}': {ex.Message} — using defaults.");
                return new NodeConfiguration();
            }
        }

        /// <summary>
        /// Deserialises a <see cref="NodeConfiguration"/> from a raw JSON string.
        /// Returns defaults if the string is null, empty, or invalid.
        /// </summary>
        public static NodeConfiguration Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new NodeConfiguration();

            try
            {
                return JsonSerializer.Deserialize<NodeConfiguration>(json, _jsonOptions)
                       ?? new NodeConfiguration();
            }
            catch
            {
                return new NodeConfiguration();
            }
        }

        // ── Environment ───────────────────────────────────────────────────────

        /// <summary>
        /// Applies configuration side-effects to the process environment.
        ///
        /// <para>
        /// Sets <c>CYCLONEDDS_URI</c> to <see cref="CycloneDdsConfigPath"/> when:
        /// <list type="bullet">
        ///   <item>The path is non-empty.</item>
        ///   <item>The environment variable is not already set (external override wins).</item>
        /// </list>
        /// </para>
        /// </summary>
        public void ApplyEnvironment()
        {
            if (!string.IsNullOrWhiteSpace(CycloneDdsConfigPath)
                && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CYCLONEDDS_URI")))
            {
                Environment.SetEnvironmentVariable("CYCLONEDDS_URI", CycloneDdsConfigPath);
                Console.WriteLine($"[NodeConfiguration] CYCLONEDDS_URI={CycloneDdsConfigPath}");
            }
        }
    }
}

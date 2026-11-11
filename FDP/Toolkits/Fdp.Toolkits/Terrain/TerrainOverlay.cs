using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ O-2 (CE-4416, DESIGN_Terrain_Authoring §18a) — <c>overlay.json</c>: the header of a terrain overlay folder.</summary>
    public sealed record TerrainOverlayHeader(int Format, string ContentHash, string BaseName, string BaseHash, string ScenarioId, string World)
    {
        public const int CurrentFormat = 1;
        public const string FileName = "overlay.json";
        public const string DefaultWorldFileName = "overlay.world.geojson";
        /// <summary>⭐ T-7a (CE-4423) — the overlay's road segments (the terrain's <c>roadNetworks</c> format), appended after the base's.</summary>
        public const string RoadsFileName = "overlay.roads.json";

        /// <summary>⭐ T-7a — the overlay's own SUMO net (a file of the overlay folder), or null: the base's is then the one.</summary>
        public string? SumoNet { get; init; }
        /// <summary>⭐ T-7a — whether the overlay carries <see cref="RoadsFileName"/>.</summary>
        public bool HasRoads { get; init; }

        public static TerrainOverlayHeader Parse(string json, string where)
        {
            JsonObject obj;
            try { obj = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException($"[Terrain] Overlay header '{where}' is not a JSON object."); }
            catch (System.Text.Json.JsonException e) { throw new ArgumentException($"[Terrain] Overlay header '{where}' is not valid JSON: {e.Message}", e); }
            int format = obj["format"]?.GetValue<int>() ?? throw new ArgumentException($"[Terrain] Overlay header '{where}' has no 'format'.");
            if (format != CurrentFormat)
                throw new ArgumentException($"[Terrain] Overlay header '{where}' is format {format}; this build reads format {CurrentFormat}.");
            var baseNode = obj["base"] as JsonObject;
            return new TerrainOverlayHeader(
                format,
                (string?)obj["contentHash"] ?? "",
                (string?)baseNode?["name"] ?? "",
                (string?)baseNode?["contentHash"] ?? "",
                (string?)obj["scenario"] ?? "",
                (string?)obj["world"] ?? DefaultWorldFileName)
            { SumoNet = (string?)obj["sumoNet"], HasRoads = (bool?)obj["hasRoads"] ?? false };
        }
    }

    /// <summary>⭐ O-2 — a terrain overlay read from its folder: the header, the overlay's own world (indices from 0, <see cref="TerrainWorld.SourceFolder"/> = <see cref="Folder"/>).</summary>
    public sealed record TerrainOverlay(TerrainOverlayHeader Header, TerrainWorld World, string Folder);
}

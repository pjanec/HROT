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
        /// <summary>⭐ T-7b — the overlay's bridges (<c>bridges[]</c>): which structure each is, its road segments and its SUMO edges.</summary>
        public System.Collections.Generic.IReadOnlyList<OverlayBridge> Bridges { get; init; } = Array.Empty<OverlayBridge>();

        private static OverlayBridge[] ParseBridges(JsonArray? rows)
        {
            if (rows == null) return Array.Empty<OverlayBridge>();
            var list = new System.Collections.Generic.List<OverlayBridge>(rows.Count);
            foreach (var n in rows)
            {
                if (n is not JsonObject r || (string?)r["key"] is not { } key) continue;
                var segs = new System.Collections.Generic.List<int>(); var edges = new System.Collections.Generic.List<string>();
                if (r["segments"] is JsonArray sa) foreach (var x in sa) segs.Add(x!.GetValue<int>());
                if (r["sumoEdges"] is JsonArray ea) foreach (var x in ea) edges.Add((string)x!);
                list.Add(new OverlayBridge(key, (bool?)r["snapped"] ?? false, segs.ToArray(), edges.ToArray()));
            }
            return list.ToArray();
        }

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
            { SumoNet = (string?)obj["sumoNet"], HasRoads = (bool?)obj["hasRoads"] ?? false, Bridges = ParseBridges(obj["bridges"] as JsonArray) };
        }
    }

    /// <summary>⭐ T-7b (CE-4423) — one row of <c>overlay.json</c> <c>bridges[]</c>: the Bridge feature's key (= its structure building's label), whether it is road, and the road-net segment indices and SUMO edge ids it added.</summary>
    public sealed record OverlayBridge(string Key, bool Snapped, int[] Segments, string[] SumoEdges);

    /// <summary>⭐ T-7b — the resident overlay's bridges, published by <c>TerrainResidency</c> as a managed singleton on its commit (empty when the terrain has no overlay or the overlay no bridge) for the <c>BridgeClosureSystem</c>.</summary>
    public sealed class TerrainBridges
    {
        public static readonly TerrainBridges None = new(Array.Empty<OverlayBridge>());
        public TerrainBridges(System.Collections.Generic.IReadOnlyList<OverlayBridge> items) => Items = items;
        public System.Collections.Generic.IReadOnlyList<OverlayBridge> Items { get; }
    }

    /// <summary>⭐ O-2 — a terrain overlay read from its folder: the header, the overlay's own world (indices from 0, <see cref="TerrainWorld.SourceFolder"/> = <see cref="Folder"/>).</summary>
    public sealed record TerrainOverlay(TerrainOverlayHeader Header, TerrainWorld World, string Folder);
}

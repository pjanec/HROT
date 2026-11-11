using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>One bridge and the road nodes its two ends snapped to (<see cref="Snapped"/> false = no road within reach: a deck, not road).</summary>
    public sealed record BridgePlan(string Key, bool Snapped, int NodeA, int NodeB, Vector2 PosA, Vector2 PosB, int Segment);

    /// <summary>
    /// ⭐ T-7a (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k) — a Bridge feature's ROAD: each end snapped to the nearest road NODE of the base road net within <see cref="SnapMetres"/>, joined by one straight
    /// segment APPENDED after the base's (so every base segment index stays). Pure and deterministic. The vehicles' net reads it through <c>RoadNetworkLoader</c> (append), SUMO's through <see cref="IBridgeNet"/>.
    /// </summary>
    public static class BridgeRoads
    {
        public const float SnapMetres = 25f;
        /// <summary>The bridge road's speed limit (m/s) and lane width (m) — a residential lane.</summary>
        public const float SpeedLimit = 14f, LaneWidth = 3.5f;

        /// <summary>
        /// Plans <paramref name="bridges"/> (key, first and last point — the banks) over <paramref name="roads"/>, keys in ordinal order. A bridge is road only when BOTH ends snap and to two DIFFERENT nodes; its segment index is
        /// <c>roads.SegmentCount + k</c> for the k-th snapped bridge.
        /// </summary>
        public static List<BridgePlan> Plan(IEnumerable<(string Key, Vector2 A, Vector2 B)> bridges, TerrainRoads? roads)
        {
            var list = new List<(string Key, Vector2 A, Vector2 B)>(bridges);
            list.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            var plans = new List<BridgePlan>(list.Count);
            int next = roads?.SegmentCount ?? 0;
            foreach (var (key, a, b) in list)
            {
                int na = -1, nb = -1; Vector2 pa = default, pb = default;
                bool ok = roads != null && roads.TryNearestNode(a, SnapMetres, out na, out pa) && roads.TryNearestNode(b, SnapMetres, out nb, out pb) && na != nb;
                plans.Add(ok ? new BridgePlan(key, true, na, nb, pa, pb, next++) : new BridgePlan(key, false, -1, -1, default, default, -1));
            }
            return plans;
        }

        /// <summary>The overlay's roads file (<see cref="TerrainOverlayHeader.RoadsFileName"/>): the terrain's road-network format with no nodes of its own and one straight Hermite segment per snapped bridge naming the base nodes by index.</summary>
        public static string RoadsJson(IReadOnlyList<BridgePlan> plans)
        {
            static JsonObject P(Vector2 v) => new() { ["x"] = Math.Round(v.X, 3), ["y"] = Math.Round(v.Y, 3) };
            var segments = new JsonArray();
            foreach (var p in plans)
            {
                if (!p.Snapped) continue;
                var d = p.PosB - p.PosA;
                segments.Add(new JsonObject
                {
                    ["id"] = p.Segment, ["startNodeId"] = p.NodeA, ["endNodeId"] = p.NodeB,
                    ["controlPoints"] = new JsonObject { ["p0"] = P(p.PosA), ["t0"] = P(d), ["p1"] = P(p.PosB), ["t1"] = P(d) },
                    ["speedLimit"] = SpeedLimit, ["laneWidth"] = LaneWidth, ["laneCount"] = 1,
                });
            }
            var doc = new JsonObject
            {
                ["$meta"] = new JsonObject { ["docType"] = "Fdp.RoadNetwork", ["schemaVersion"] = 1 },
                ["nodes"] = new JsonArray(), ["segments"] = segments,
            };
            return doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        }
    }

    /// <summary>One bridge as the SUMO net builder gets it: the two road-node positions (terrain local metres) its edges join.</summary>
    public sealed record BridgeNetRequest(string Key, Vector2 A, Vector2 B);

    /// <summary>
    /// ⭐ T-7a — builds the overlay's SUMO net: the base net plus a two-way residential edge pair per bridge (<c>netconvert -s &lt;base&gt; -e bridges.edg.xml</c>). A seam, because the locator of netconvert and the net's
    /// placement (local metres ⇄ net metres) live above the FDP toolkits (<c>Hrot.Traffic.BridgeNetBuilder</c>); the compile passes it in (like <see cref="IUniqueBuildings"/>).
    /// </summary>
    public interface IBridgeNet
    {
        /// <summary>
        /// Writes <paramref name="outNetPath"/>. Returns, per request, the SUMO edge ids it added (empty when that bridge's ends found no junction), or null (with <paramref name="warning"/>) when no net could be made.
        /// </summary>
        IReadOnlyList<IReadOnlyList<string>>? Build(TerrainDefinition definition, string terrainFolder, IReadOnlyList<BridgeNetRequest> bridges, string outNetPath, out string? warning);
    }
}

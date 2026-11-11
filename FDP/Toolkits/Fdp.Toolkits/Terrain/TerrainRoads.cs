using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using CarKinem.Road;
using Fdp.Core.Serialization;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ T-5 (CE-3431, docs/DESIGN_Terrain_Authoring.md §18h H-5) — the terrain's road network as the block expander needs it: the road segments' centre lines as short straight edges. ⭐ ONE reader for both callers —
    /// the scenario-save <c>OverlayCompiler</c> and the editor's <c>LiveOverlay</c> both build it from the terrain folder's FIRST declared road network (the file <c>TerrainResidency.Prepare</c> publishes as the road graph),
    /// so a block that aligns to a road turns the same way in both (⛔ never a second reader). Pure; the file is read once per terrain.
    /// </summary>
    public sealed class TerrainRoads
    {
        private const int SamplesPerSegment = 8;
        private readonly List<(Vector2 A, Vector2 B)> _edges = new();
        private readonly List<Vector2> _nodes = new();
        private int _segments;

        /// <summary>⭐ T-7a — how many road NODES the file declares (a hand-built <see cref="FromEdges"/> road has none: only edges). A node's index is its position in the file, as <c>RoadNetworkLoader</c> numbers it.</summary>
        public int NodeCount => _nodes.Count;

        /// <summary>⭐ T-7a — how many road SEGMENTS the file declares (the index the first appended segment takes).</summary>
        public int SegmentCount => _segments;

        /// <summary>⭐ T-7a (CE-4423) — the road node nearest <paramref name="p"/> within <paramref name="radius"/> metres (ties: the lowest index), or false when none is in reach. Nodes only — a <see cref="FromEdges"/> road has none.</summary>
        public bool TryNearestNode(Vector2 p, float radius, out int index, out Vector2 position)
        {
            index = -1; position = default; float best = radius * radius;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float d2 = Vector2.DistanceSquared(p, _nodes[i]);
                if (d2 < best || (index < 0 && d2 <= best)) { best = d2; index = i; position = _nodes[i]; }
            }
            return index >= 0;
        }

        /// <summary>How many straight edges the road centre lines were cut into.</summary>
        public int EdgeCount => _edges.Count;

        /// <summary>The roads of a terrain whose definition is at <paramref name="definitionPath"/>; null when it declares no road network or the file is missing.</summary>
        public static TerrainRoads? Load(string definitionPath, TerrainDefinition definition)
        {
            if (definition.RoadNetworks.Count == 0) return null;
            string rel = definition.RoadNetworks[0];
            string path = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(definitionPath))!, rel));
            if (!File.Exists(path)) return null;
            var json = JsonSerializer.Deserialize<RoadNetworkJson>(Fdp.Core.ProgressStream.ReadAllText(path), FdpJsonOptionsRegistry.DefaultRelaxed);
            return json == null ? null : FromJson(json);
        }

        /// <summary>The roads of the terrain in <paramref name="terrainFolder"/> (its <c>terrain.json</c>); null when none.</summary>
        public static TerrainRoads? ForFolder(string? terrainFolder)
        {
            if (string.IsNullOrEmpty(terrainFolder)) return null;
            string def = Path.Combine(terrainFolder, TerrainCatalog.DefinitionFileName);
            return File.Exists(def) ? Load(def, TerrainFolderWorld.ReadDefinition(def)) : null;
        }

        /// <summary>Roads from a parsed road-network document: each Hermite segment sampled into <see cref="SamplesPerSegment"/> straight edges.</summary>
        public static TerrainRoads FromJson(RoadNetworkJson json)
        {
            var r = new TerrainRoads();
            foreach (var n in json.Nodes) r._nodes.Add(n.Position.ToVector2());
            r._segments = json.Segments.Length;
            foreach (var s in json.Segments)
            {
                var p0 = s.ControlPoints.P0.ToVector2(); var t0 = s.ControlPoints.T0.ToVector2();
                var p1 = s.ControlPoints.P1.ToVector2(); var t1 = s.ControlPoints.T1.ToVector2();
                var prev = p0;
                for (int i = 1; i <= SamplesPerSegment; i++)
                {
                    float t = i / (float)SamplesPerSegment, t2 = t * t, t3 = t2 * t;
                    var q = (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * t0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * t1;
                    if (q != prev) r._edges.Add((prev, q));
                    prev = q;
                }
            }
            return r;
        }

        /// <summary>Roads from straight edges (a rail's hand-built road).</summary>
        public static TerrainRoads FromEdges(IEnumerable<(Vector2 A, Vector2 B)> edges)
        {
            var r = new TerrainRoads();
            foreach (var e in edges) if (e.A != e.B) r._edges.Add(e);
            return r;
        }

        /// <summary>
        /// The heading a block placed at <paramref name="at"/> with <paramref name="headingDeg"/> turns to: the direction of the nearest road edge within <paramref name="radius"/> metres — ⭐ of the edge's two directions
        /// (it and its opposite), the one nearer the placed heading, so a block turns at most 90°. False when no edge is in reach (the heading stays as placed).
        /// </summary>
        public bool TryAlign(Vector2 at, float radius, float headingDeg, out float alignedDeg)
        {
            alignedDeg = headingDeg;
            float best = radius * radius; bool found = false; Vector2 dir = default;
            foreach (var (a, b) in _edges)
            {
                var ab = b - a;
                float t = Math.Clamp(Vector2.Dot(at - a, ab) / ab.LengthSquared(), 0f, 1f);
                float d2 = Vector2.DistanceSquared(at, a + ab * t);
                if (d2 <= best) { best = d2; dir = ab; found = true; }   // <= : the LAST of equally near edges wins, in the file's order — deterministic
            }
            if (!found) return false;
            float ang = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
            float diff = ang - headingDeg;
            diff -= 360f * MathF.Floor((diff + 180f) / 360f);          // (-180, 180]
            if (MathF.Abs(diff) > 90f) ang += 180f;
            alignedDeg = MathF.Round(ang * 1000f) / 1000f;
            return true;
        }
    }
}

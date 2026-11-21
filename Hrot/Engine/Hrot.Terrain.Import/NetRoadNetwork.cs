using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐ CE-6001 (docs/DESIGN_Terrain_Authoring.md §18k / §18l) — the VEHICLES' road network (<c>roadNetworks</c>, the CarKinem road-graph JSON the terrain's definition names) from the terrain's SUMO net, so an imported terrain has BOTH nets and
/// §18k's bridges (T-7a: ends snapped to the base's road NODES, appended segments) have something to snap to. One node per junction, a straight Hermite segment per stretch of each edge's first lane (its shape points are nodes too), the reverse of an
/// edge already written skipped; positions are terrain-local (net − anchor). Pure and deterministic.
/// </summary>
internal static class NetRoadNetwork
{
    public static JsonObject Read(string netPath, double anchorX, double anchorY)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        var junctions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var edges = new List<(string From, string To, List<(double X, double Y)> Shape, double Speed, double Width, int Lanes)>();
        using (var r = XmlReader.Create(netPath, settings))
        {
            string? from = null, to = null; int lanes = 0; List<(double, double)>? shape = null; double speed = 14, width = 3.5; bool inEdge = false;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.Name == "junction" && r.GetAttribute("id") is { } jid && !jid.StartsWith(':'))
                    junctions[jid] = (D(r.GetAttribute("x")), D(r.GetAttribute("y")));
                else if (r.NodeType == XmlNodeType.Element && r.Name == "edge")
                {
                    inEdge = r.GetAttribute("function") != "internal" && r.GetAttribute("from") != null;
                    from = r.GetAttribute("from"); to = r.GetAttribute("to"); lanes = 0; shape = null;
                    if (r.IsEmptyElement) inEdge = false;
                }
                else if (inEdge && r.NodeType == XmlNodeType.Element && r.Name == "lane")
                {
                    lanes++;
                    if (shape != null) continue;
                    speed = r.GetAttribute("speed") is { } sp ? D(sp) : 14; width = r.GetAttribute("width") is { } w ? D(w) : 3.5;
                    shape = (r.GetAttribute("shape") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => { var c = p.Split(','); return (D(c[0]), D(c[1])); }).ToList();
                }
                else if (inEdge && r.NodeType == XmlNodeType.EndElement && r.Name == "edge")
                {
                    if (from != null && to != null && shape is { Count: >= 2 }) edges.Add((from, to, shape.Select(p => (p.Item1, p.Item2)).ToList(), speed, width, Math.Max(1, lanes)));
                    inEdge = false;
                }
            }
        }

        var nodes = new JsonArray(); var segments = new JsonArray();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Node(string key, double x, double y)
        {
            if (ids.TryGetValue(key, out int id)) return id;
            id = nodes.Count; ids[key] = id;
            nodes.Add(new JsonObject { ["id"] = id, ["position"] = new JsonObject { ["x"] = Math.Round(x - anchorX, 3), ["y"] = Math.Round(y - anchorY, 3) } });
            return id;
        }
        var written = new HashSet<(string, string)>();
        foreach (var e in edges)
        {
            if (!junctions.TryGetValue(e.From, out var a) || !junctions.TryGetValue(e.To, out var b) || written.Contains((e.To, e.From))) continue;   // the reverse of an edge already there
            written.Add((e.From, e.To));
            var chain = new List<(string Key, double X, double Y)> { ("j" + e.From, a.X, a.Y) };
            for (int i = 1; i < e.Shape.Count - 1; i++) chain.Add((e.From + ">" + e.To + "#" + i, e.Shape[i].X, e.Shape[i].Y));
            chain.Add(("j" + e.To, b.X, b.Y));
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                int n0 = Node(chain[i].Key, chain[i].X, chain[i].Y), n1 = Node(chain[i + 1].Key, chain[i + 1].X, chain[i + 1].Y);
                if (n0 == n1) continue;
                double dx = Math.Round(chain[i + 1].X - chain[i].X, 3), dy = Math.Round(chain[i + 1].Y - chain[i].Y, 3);
                JsonObject P(double x, double y) => new() { ["x"] = Math.Round(x - anchorX, 3), ["y"] = Math.Round(y - anchorY, 3) };
                segments.Add(new JsonObject
                {
                    ["id"] = segments.Count, ["startNodeId"] = n0, ["endNodeId"] = n1,
                    ["controlPoints"] = new JsonObject { ["p0"] = P(chain[i].X, chain[i].Y), ["t0"] = new JsonObject { ["x"] = dx, ["y"] = dy }, ["p1"] = P(chain[i + 1].X, chain[i + 1].Y), ["t1"] = new JsonObject { ["x"] = dx, ["y"] = dy } },
                    ["speedLimit"] = Math.Round(e.Speed, 2), ["laneWidth"] = Math.Round(e.Width, 2), ["laneCount"] = e.Lanes,
                });
            }
        }
        return new JsonObject { ["$meta"] = new JsonObject { ["docType"] = "Fdp.RoadNetwork", ["schemaVersion"] = 1 }, ["nodes"] = nodes, ["segments"] = segments };
    }

    private static double D(string? s) => double.Parse(s ?? "0", CultureInfo.InvariantCulture);
}

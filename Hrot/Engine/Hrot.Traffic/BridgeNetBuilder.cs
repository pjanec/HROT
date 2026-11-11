using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml;
using System.Linq;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Hrot.Terrain.InputSet;
using Sim.Ingest;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ T-7a (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k) — the overlay's SUMO net: the base's <c>.net.xml</c> plus a two-way residential edge pair per bridge, made by
/// <c>netconvert -s &lt;base&gt; -e bridges.edg.xml</c> (measured: every normal edge and lane id of the base survives, 0.2–0.5 s). Each bridge's two road-node positions (terrain local metres) are put into the net's
/// metres through the SAME <see cref="SumoNetPlacement"/> the traffic loader uses, then snapped to the nearest junction within <see cref="SnapMetres"/>; a bridge whose end finds none (or both the same) adds no edge.
/// The netconvert locator is <see cref="Netconvert"/> — the one. The generated-on comment is stripped: two compiles are the same bytes.
/// </summary>
public sealed class BridgeNetBuilder : IBridgeNet
{
    public const double SnapMetres = 30.0;

    /// <summary>The ids of the two edges a bridge adds (there, back).</summary>
    public static (string There, string Back) EdgeIds(string key) => ("bridge-" + key, "bridge-" + key + "-r");

    public IReadOnlyList<IReadOnlyList<string>>? Build(TerrainDefinition definition, string terrainFolder, IReadOnlyList<BridgeNetRequest> bridges, string outNetPath, out string? warning)
    {
        warning = null;
        if (definition.SumoNet is not { } declared) { warning = "the terrain declares no SUMO net"; return null; }
        string baseNet = Path.Combine(terrainFolder, declared.File);
        if (!File.Exists(baseNet)) { warning = $"the base net '{baseNet}' does not exist"; return null; }
        var located = Netconvert.Locate();
        if (located.Path == null || located.Problem != null) { warning = located.Problem ?? "netconvert not found"; return null; }

        var model = NetworkParser.ParseXml(File.ReadAllText(baseNet));
        var placement = SumoNetPlacement.For(model, declared, TerrainGeoFrame.Of(definition.Origin, definition.Utm));
        var (junctions, incoming) = ReadNet(baseNet);

        var edges = new StringBuilder("<edges>\n");
        var cons = new StringBuilder("<connections>\n");
        var result = new List<IReadOnlyList<string>>(bridges.Count);
        int added = 0;
        foreach (var b in bridges)
        {
            var (na, nb) = (Nearest(junctions, placement, b.A), Nearest(junctions, placement, b.B));
            if (na == null || nb == null || na == nb) { result.Add(Array.Empty<string>()); continue; }
            var (there, back) = EdgeIds(b.Key);
            // a residential lane (highway.residential: priority 3, 14 m/s, 1 lane) — written out, not by type: not every net declares the type
            edges.Append(CultureInfo.InvariantCulture, $" <edge id=\"{there}\" from=\"{na}\" to=\"{nb}\" priority=\"3\" numLanes=\"1\" speed=\"14.00\"/>\n");
            edges.Append(CultureInfo.InvariantCulture, $" <edge id=\"{back}\" from=\"{nb}\" to=\"{na}\" priority=\"3\" numLanes=\"1\" speed=\"14.00\"/>\n");
            // ⚠ MEASURED (T-7a): netconvert -s keeps an existing edge's connections as given, so a new edge LEAVING a junction gets no connection from the edges already arriving there — without these a vehicle
            //   could leave the bridge but never enter it. Every vehicle lane arriving at a bridge end gets an explicit connection onto the bridge's lane 0.
            foreach (var (edge, lane) in incoming.GetValueOrDefault(na) ?? new()) cons.Append(CultureInfo.InvariantCulture, $" <connection from=\"{edge}\" to=\"{there}\" fromLane=\"{lane}\" toLane=\"0\"/>\n");
            foreach (var (edge, lane) in incoming.GetValueOrDefault(nb) ?? new()) cons.Append(CultureInfo.InvariantCulture, $" <connection from=\"{edge}\" to=\"{back}\" fromLane=\"{lane}\" toLane=\"0\"/>\n");
            result.Add(new[] { there, back }); added++;
        }
        edges.Append("</edges>\n"); cons.Append("</connections>\n");
        if (added == 0) { warning = "no bridge end found a junction of the SUMO net within " + SnapMetres + " m"; return null; }

        string work = outNetPath + ".work";
        Directory.CreateDirectory(work);
        try
        {
            string edg = Path.Combine(work, "bridges.edg.xml");
            string con = Path.Combine(work, "bridges.con.xml");
            File.WriteAllText(edg, edges.ToString()); File.WriteAllText(con, cons.ToString());
            var (code, _, err) = Netconvert.Run(located.Path, new[] { "-s", baseNet, "-e", edg, "-x", con, "-o", outNetPath, "--no-turnarounds", "true", "--seed", "42" }, null, Netconvert.SumoHomeOf(located.Path));
            if (code != 0 || !File.Exists(outNetPath)) { warning = $"netconvert exited {code}: {Tail(err)}"; return null; }
            Netconvert.StripGeneratedHeader(outNetPath);
            return result;
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } }
    }

    private static string Tail(string s) { s = s.Trim(); return s.Length <= 300 ? s : s[^300..]; }

    /// <summary>The non-internal junctions of the net (id, position in NET metres) and, per junction id, the (edge, lane index) of every VEHICLE lane arriving at it — streamed, the net may be megabytes.</summary>
    private static (List<(string Id, double X, double Y)> Junctions, Dictionary<string, List<(string Edge, int Lane)>> Incoming) ReadNet(string netPath)
    {
        var junctions = new List<(string, double, double)>();
        var incoming = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
        using var r = XmlReader.Create(netPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        r.MoveToContent();
        while (!r.EOF)
        {
            if (r.NodeType != XmlNodeType.Element) { r.Read(); continue; }
            if (r.Name == "junction")
            {
                string? id = r.GetAttribute("id"), type = r.GetAttribute("type");
                if (id != null && !id.StartsWith(':') && type != "internal"
                    && double.TryParse(r.GetAttribute("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.TryParse(r.GetAttribute("y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                    junctions.Add((id, x, y));
                r.Read(); continue;
            }
            if (r.Name == "edge")
            {
                var e = (System.Xml.Linq.XElement)System.Xml.Linq.XNode.ReadFrom(r);   // ReadFrom leaves the reader after the element
                string? id = (string?)e.Attribute("id"), to = (string?)e.Attribute("to"), function = (string?)e.Attribute("function");
                if (id == null || to == null || id.StartsWith(':') || function is "internal" or "crossing" or "walkingarea") continue;
                foreach (var lane in e.Elements("lane"))
                {
                    string? allow = (string?)lane.Attribute("allow"), disallow = (string?)lane.Attribute("disallow");
                    bool vehicles = allow == null ? disallow == null || !disallow.Split(' ').Contains("passenger") : allow.Split(' ').Any(t => t != "pedestrian");
                    if (!vehicles || !int.TryParse((string?)lane.Attribute("index"), out int index)) continue;
                    if (!incoming.TryGetValue(to, out var list)) incoming[to] = list = new();
                    list.Add((id, index));
                }
                continue;
            }
            r.Read();
        }
        return (junctions, incoming);
    }

    private static string? Nearest(List<(string Id, double X, double Y)> junctions, SumoNetPlacement placement, Vector2 local)
    {
        var (nx, ny) = placement.ToNet(local.X, local.Y);
        string? best = null; double bd = SnapMetres * SnapMetres;
        foreach (var (id, x, y) in junctions)
        {
            double d2 = (x - nx) * (x - nx) + (y - ny) * (y - ny);
            if (d2 < bd || (best == null && d2 <= bd)) { bd = d2; best = id; }
        }
        return best;
    }
}

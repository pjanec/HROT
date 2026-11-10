using System.Globalization;
using System.Xml;
using Hrot.Terrain.InputSet;
using Fdp.Modules.Geographic.Transforms;

namespace Hrot.Terrain.Osm;

/// <summary>⭐ E-2: a STREAMING <see cref="XmlReader"/> into the compact <see cref="OsmModel"/> — a 50 MB file never exists as a DOM.</summary>
public static class OsmXmlReader
{
    public static OsmModel Read(string path, UtmFrame frame)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return Read(fs, frame);
    }

    public static OsmModel Read(Stream stream, UtmFrame frame)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true, IgnoreComments = true, XmlResolver = null };
        using var r = XmlReader.Create(stream, settings);
        var model = new OsmModel();
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            switch (r.Name)
            {
                case "meta": model.DataTimestamp ??= r.GetAttribute("osm_base"); break;
                case "node": ReadNode(r, frame, model); break;
                case "way": ReadWay(r, model); break;
                case "relation": ReadRelation(r, model); break;
            }
        }
        return model;
    }

    private static long Id(XmlReader r) => long.Parse(r.GetAttribute("id") ?? throw new InvalidDataException("an OSM element without an id"), CultureInfo.InvariantCulture);
    private static double D(string? s) => double.Parse(s ?? throw new InvalidDataException("a node without lat / lon"), CultureInfo.InvariantCulture);

    private static void ReadNode(XmlReader r, UtmFrame frame, OsmModel model)
    {
        long id = Id(r);
        string? lat = r.GetAttribute("lat"), lon = r.GetAttribute("lon");
        Dictionary<string, string>? tags = null;
        if (!r.IsEmptyElement)
        {
            using var sub = r.ReadSubtree();
            sub.Read();
            while (sub.Read())
                if (sub.NodeType == XmlNodeType.Element && sub.Name == "tag") (tags ??= new())[sub.GetAttribute("k") ?? ""] = sub.GetAttribute("v") ?? "";
        }
        if (lat == null || lon == null) return;   // a deleted / geometry-less node
        var (e, n) = Utm.FromGeodetic(D(lat), D(lon), frame.Zone, frame.South);
        model.Nodes[id] = (e - frame.E0, n - frame.N0);
        if (tags != null) model.NodeTags[id] = tags;
    }

    private static void ReadWay(XmlReader r, OsmModel model)
    {
        long id = Id(r);
        var nodes = new List<long>();
        Dictionary<string, string>? tags = null;
        if (!r.IsEmptyElement)
        {
            using var sub = r.ReadSubtree();
            sub.Read();
            while (sub.Read())
            {
                if (sub.NodeType != XmlNodeType.Element) continue;
                if (sub.Name == "nd") nodes.Add(long.Parse(sub.GetAttribute("ref")!, CultureInfo.InvariantCulture));
                else if (sub.Name == "tag") (tags ??= new())[sub.GetAttribute("k") ?? ""] = sub.GetAttribute("v") ?? "";
            }
        }
        var way = new OsmWay(id, nodes.ToArray(), (IReadOnlyDictionary<string, string>?)tags ?? OsmTags.None);
        model.Ways.Add(way);
        model.WayById[id] = way;
    }

    private static void ReadRelation(XmlReader r, OsmModel model)
    {
        long id = Id(r);
        var members = new List<OsmMember>();
        Dictionary<string, string>? tags = null;
        if (!r.IsEmptyElement)
        {
            using var sub = r.ReadSubtree();
            sub.Read();
            while (sub.Read())
            {
                if (sub.NodeType != XmlNodeType.Element) continue;
                if (sub.Name == "member")
                {
                    string t = sub.GetAttribute("type") ?? "";
                    members.Add(new OsmMember(t.Length > 0 ? t[0] : '?', long.Parse(sub.GetAttribute("ref")!, CultureInfo.InvariantCulture), sub.GetAttribute("role") ?? ""));
                }
                else if (sub.Name == "tag") (tags ??= new())[sub.GetAttribute("k") ?? ""] = sub.GetAttribute("v") ?? "";
            }
        }
        model.Relations.Add(new OsmRelation(id, members.ToArray(), (IReadOnlyDictionary<string, string>?)tags ?? OsmTags.None));
    }
}

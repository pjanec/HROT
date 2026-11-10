using System.Xml;

namespace Hrot.Terrain.Osm;

/// <summary>
/// ⭐ CE-4384 ① — the parts of a split Overpass answer merged into ONE OSM XML file: every node, way and relation once (by its type and id — the parts overlap at their
/// edges, and a way crossing them comes back in each), nodes first, then ways, then relations (the order OSM files and netconvert expect); the first part's
/// <c>&lt;note&gt;</c> / <c>&lt;meta osm_base&gt;</c> kept.
/// </summary>
public static class OsmXmlMerge
{
    public static void Merge(IReadOnlyList<string> parts, string destination)
    {
        var seen = new HashSet<(char, long)>();
        var nodes = new List<string>(); var ways = new List<string>(); var relations = new List<string>(); var head = new List<string>();
        string version = "0.6", generator = "Overpass API";
        bool first = true;
        foreach (var part in parts)
        {
            using var r = XmlReader.Create(part, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            r.MoveToContent();
            if (first) { version = r.GetAttribute("version") ?? version; generator = r.GetAttribute("generator") ?? generator; }
            if (!r.Read()) { first = false; continue; }
            while (!r.EOF)
            {
                if (r.NodeType != XmlNodeType.Element) { r.Read(); continue; }
                string name = r.Name;
                if (name is "node" or "way" or "relation")
                {
                    long id = long.Parse(r.GetAttribute("id") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    string xml = r.ReadOuterXml();
                    if (!seen.Add((name[0], id))) continue;
                    (name == "node" ? nodes : name == "way" ? ways : relations).Add(xml);
                }
                else if (first && name is "note" or "meta") head.Add(r.ReadOuterXml());
                else r.Skip();
            }
            first = false;
        }
        string tmp = destination + ".merge-" + Guid.NewGuid().ToString("N");
        using (var w = new StreamWriter(tmp, false, new System.Text.UTF8Encoding(false)))
        {
            w.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            w.WriteLine($"<osm version=\"{Esc(version)}\" generator=\"{Esc(generator)}\">");
            foreach (var l in head) w.WriteLine(l);
            foreach (var l in nodes) w.WriteLine(l);
            foreach (var l in ways) w.WriteLine(l);
            foreach (var l in relations) w.WriteLine(l);
            w.WriteLine("</osm>");
        }
        File.Move(tmp, destination, overwrite: true);
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";
}

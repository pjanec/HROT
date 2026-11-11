using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Hrot.Tools.TerrainSynth;

/// <summary>Writes the road network as SUMO plain XML and, when SUMO is installed, builds roads.net.xml from it.</summary>
public static class SumoWriter
{
    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Fixed2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static (string Type, int Priority) TypeOf(RoadClass c) => c switch
    {
        RoadClass.Motorway => ("highway.motorway", 13),
        RoadClass.Trunk => ("highway.trunk", 12),
        RoadClass.Primary => ("highway.primary", 11),
        RoadClass.Secondary => ("highway.secondary", 10),
        RoadClass.Tertiary => ("highway.tertiary", 9),
        RoadClass.Unclassified => ("highway.unclassified", 4),
        RoadClass.Residential => ("highway.residential", 3),
        _ => ("railway.rail", 15),
    };

    private static readonly (RoadClass Class, int Lanes, double Speed, double Width)[] ClassTable =
    {
        (RoadClass.Motorway, 2, 36, 15),
        (RoadClass.Trunk, 2, 28, 10),
        (RoadClass.Primary, 2, 25, 8),
        (RoadClass.Secondary, 2, 22, 7),
        (RoadClass.Tertiary, 2, 19, 6.5),
        (RoadClass.Unclassified, 1, 14, 5),
        (RoadClass.Residential, 1, 14, 5),
        (RoadClass.Rail, 1, 33, 4),
    };

    /// <summary>
    /// Writes roads.nod.xml, roads.edg.xml and roads.typ.xml. Every non-oneway edge also gets a reverse edge "id-r".
    /// </summary>
    public static void WritePlain(RoadNetwork net, string roadsDir)
    {
        Directory.CreateDirectory(roadsDir);
        var utf8 = new UTF8Encoding(false);

        var nod = new StringBuilder("<nodes>\n");
        foreach (var n in net.Nodes)
            nod.Append("    <node id=\"").Append(n.Id).Append("\" x=\"").Append(Fixed2(n.X)).Append("\" y=\"").Append(Fixed2(n.Y)).Append("\"/>\n");
        nod.Append("</nodes>\n");
        File.WriteAllText(Path.Combine(roadsDir, "roads.nod.xml"), nod.ToString(), utf8);

        var edg = new StringBuilder("<edges>\n");
        foreach (var e in net.Edges)
        {
            string type = TypeOf(e.Class).Type;
            edg.Append("    <edge id=\"").Append(e.Id).Append("\" from=\"").Append(e.From).Append("\" to=\"").Append(e.To)
               .Append("\" type=\"").Append(type).Append("\"/>\n");
            if (!e.Oneway)
                edg.Append("    <edge id=\"").Append(e.Id).Append("-r\" from=\"").Append(e.To).Append("\" to=\"").Append(e.From)
                   .Append("\" type=\"").Append(type).Append("\"/>\n");
        }
        edg.Append("</edges>\n");
        File.WriteAllText(Path.Combine(roadsDir, "roads.edg.xml"), edg.ToString(), utf8);

        var typ = new StringBuilder("<types>\n");
        foreach (var (c, lanes, speed, width) in ClassTable)
        {
            var (type, priority) = TypeOf(c);
            typ.Append("    <type id=\"").Append(type).Append("\" numLanes=\"").Append(I(lanes)).Append("\" speed=\"").Append(N(speed))
               .Append("\" priority=\"").Append(I(priority)).Append("\" width=\"").Append(N(width)).Append('"');
            if (c == RoadClass.Rail) typ.Append(" allow=\"rail\"");
            typ.Append("/>\n");
        }
        typ.Append("</types>\n");
        File.WriteAllText(Path.Combine(roadsDir, "roads.typ.xml"), typ.ToString(), utf8);
    }

    /// <summary>The netconvert HROT uses (the shared locator, <see cref="Netconvert"/>); null when there is none of the right version.</summary>
    public static string? FindNetconvert() => Netconvert.Locate() is { Problem: null, Path: { } p } ? p : null;

    /// <summary>Runs netconvert on the plain files in roadsDir to write roads.net.xml. False (with one warning line in log) when it is missing or fails.</summary>
    public static bool TryNetconvert(string roadsDir, TextWriter log)
    {
        var found = Netconvert.Locate();
        if (found.Problem != null || found.Path == null)
        {
            log.WriteLine("WARNING: " + found.Problem + "; roads.net.xml was not written, the plain files are complete");
            return false;
        }
        var (code, _, err) = Netconvert.Run(found.Path, new[] { "-n", "roads.nod.xml", "-e", "roads.edg.xml", "-t", "roads.typ.xml",
                                                               "--offset.disable-normalization", "true", "-o", "roads.net.xml" }, roadsDir);
        if (code != 0 || !File.Exists(Path.Combine(roadsDir, "roads.net.xml")))
        {
            log.WriteLine("WARNING: netconvert failed with exit code " + I(code) + ": " + err.Trim().Replace("\r", "").Replace("\n", " | "));
            return false;
        }
        Netconvert.StripGeneratedHeader(Path.Combine(roadsDir, "roads.net.xml"));
        return true;
    }
}

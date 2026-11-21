using System.Globalization;
using System.Xml.Linq;
using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class SumoWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "terrain-synth-tests", "sumo " + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Plain_FilesParseAndCountsMatch()
    {
        var net = RoadNetwork.Generate(SynthProfile.Full, new Rng(1));
        SumoWriter.WritePlain(net, _dir);

        var nod = XDocument.Load(Path.Combine(_dir, "roads.nod.xml"));
        var edg = XDocument.Load(Path.Combine(_dir, "roads.edg.xml"));
        var typ = XDocument.Load(Path.Combine(_dir, "roads.typ.xml"));
        Assert.Equal(net.Nodes.Count, nod.Descendants("node").Count());

        int twoWay = net.Edges.Count(e => !e.Oneway);
        var edges = edg.Descendants("edge").ToList();
        Assert.Equal(net.Edges.Count + twoWay, edges.Count);
        var ids = edges.Select(e => (string)e.Attribute("id")!).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var e in net.Edges.Where(e => !e.Oneway))
        {
            var rev = edges.Single(x => (string)x.Attribute("id")! == e.Id + "-r");
            Assert.Equal(e.To, (string)rev.Attribute("from")!);
            Assert.Equal(e.From, (string)rev.Attribute("to")!);
        }
        Assert.DoesNotContain(net.Edges.Where(e => e.Oneway), e => ids.Contains(e.Id + "-r"));

        var types = typ.Descendants("type").ToDictionary(t => (string)t.Attribute("id")!);
        Assert.Equal(8, types.Count);
        Assert.Equal("rail", (string)types["railway.rail"].Attribute("allow")!);
        Assert.Equal("2", (string)types["highway.motorway"].Attribute("numLanes")!);
        Assert.Equal("13", (string)types["highway.motorway"].Attribute("priority")!);
        Assert.Equal("3", (string)types["highway.residential"].Attribute("priority")!);
        Assert.Equal("15", (string)types["highway.motorway"].Attribute("width")!);
        foreach (var e in edges) Assert.Contains((string)e.Attribute("type")!, types.Keys);
    }

    [Fact]
    public void Plain_UsesInvariantCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            SumoWriter.WritePlain(RoadNetwork.Generate(SynthProfile.Tiny, new Rng(1)), _dir);
        }
        finally { CultureInfo.CurrentCulture = old; }
        var typ = File.ReadAllText(Path.Combine(_dir, "roads.typ.xml"));
        Assert.Contains("width=\"6.5\"", typ);
        Assert.DoesNotContain("6,5", typ);
        Assert.DoesNotContain(",", File.ReadAllText(Path.Combine(_dir, "roads.nod.xml")).Replace("\"", ""));
    }

    [Fact]
    public void Netconvert_Missing_WarnsAndReturnsFalse()
    {
        string home = Path.Combine(_dir, "sumo home with spaces");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        SumoWriter.WritePlain(RoadNetwork.Generate(SynthProfile.Tiny, new Rng(1)), _dir);
        string? oldHome = Environment.GetEnvironmentVariable("SUMO_HOME");
        string? oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("SUMO_HOME", home);
            Environment.SetEnvironmentVariable("PATH", "");
            Environment.SetEnvironmentVariable("HROT_SUMO_NO_LOCAL", "1");   // the repo's .local/sumo install must not be found here
            Environment.SetEnvironmentVariable("HROT_SUMO_HOME", null);
            var log = new StringWriter();
            Assert.False(SumoWriter.TryNetconvert(_dir, log));
            Assert.Contains("netconvert not found", log.ToString());
            Assert.Single(log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.False(File.Exists(Path.Combine(_dir, "roads.net.xml")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HROT_SUMO_NO_LOCAL", null);
            Environment.SetEnvironmentVariable("SUMO_HOME", oldHome);
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public void Netconvert_WhenPresent_WritesNet()
    {
        if (SumoWriter.FindNetconvert() == null)
        {
            Console.WriteLine("SKIP");
            return;
        }
        var net = RoadNetwork.Generate(SynthProfile.Tiny, new Rng(1));
        SumoWriter.WritePlain(net, _dir);
        var log = new StringWriter();
        Assert.True(SumoWriter.TryNetconvert(_dir, log), log.ToString());
        string path = Path.Combine(_dir, "roads.net.xml");
        Assert.True(File.Exists(path));
        var doc = XDocument.Load(path);
        Assert.Equal("0.00,0.00", (string)doc.Root!.Element("location")!.Attribute("netOffset")!);
        Assert.NotEmpty(doc.Root.Elements("junction"));
        int edgeCount = doc.Root.Elements("edge").Count(e => (string?)e.Attribute("function") != "internal");
        Assert.True(edgeCount >= 2 * net.Edges.Count(e => !e.Oneway), $"net edges {edgeCount}");
    }
}

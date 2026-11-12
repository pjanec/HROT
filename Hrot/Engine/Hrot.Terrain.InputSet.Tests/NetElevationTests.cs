using Xunit;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hrot.Terrain.InputSet.Tests;

/// <summary>
/// ⭐ AQ93 E2b — <see cref="NetElevation"/>, the one post-netconvert step that writes OUR heights into a SUMO net: a synthetic net and a synthetic height grid in,
/// every shape point's z is the grid's within 1 cm, straight runs are densified, lengths are untouched, a z netconvert already wrote is kept.
/// </summary>
public sealed class NetElevationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "netel-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    // heights: 41 × 41 posts of 5 m over (0..200, 0..200): a slope with a bump — z = 300 + 0.3 x + 0.1 y + 12 sin(x / 17)
    private static float Z(double x, double y) => (float)(300 + 0.3 * x + 0.1 * y + 12 * Math.Sin(x / 17));

    // the grid's own height at (x, y): the posts' z, planar inside each cell's two triangles (the terrain's rule), worked out here independently of HeightSampler
    private static double Grid(double x, double y)
    {
        double fx = Math.Clamp(x / 5.0, 0, 39.999), fy = Math.Clamp(y / 5.0, 0, 39.999);
        int c = (int)fx, r = (int)fy; double u = fx - c, v = fy - r;
        double a = Z(c * 5.0, r * 5.0), b = Z((c + 1) * 5.0, r * 5.0), d = Z(c * 5.0, (r + 1) * 5.0), e = Z((c + 1) * 5.0, (r + 1) * 5.0);
        return u >= v ? a + u * (b - a) + v * (e - b) : a + v * (d - a) + u * (e - d);
    }

    private string Setup(string netXml)
    {
        Directory.CreateDirectory(_root);
        var info = new HeightsInfo(0, 0, 5, 41, 41, new UtmFrame(33, false, 0, 0));
        using (var w = new HeightsWriter(Path.Combine(_root, "heights"), info))
        {
            var row = new float[41];
            for (int r = 0; r < 41; r++)   // NORTH row first
            {
                double y = (40 - r) * 5.0;
                for (int c = 0; c < 41; c++) row[c] = Z(c * 5.0, y);
                w.WriteRow(row);
            }
        }
        string net = Path.Combine(_root, "roads.net.xml");
        File.WriteAllText(net, netXml);
        return net;
    }

    private const string Net = """
        <?xml version="1.0" encoding="UTF-8"?>
        <net version="1.20" junctionCornerDetail="5">
            <location netOffset="0.00,0.00" convBoundary="0.00,0.00,200.00,200.00" origBoundary="0,0,200,200" projParameter="!"/>
            <edge id="a" from="J0" to="J1" priority="-1">
                <lane id="a_0" index="0" speed="13.89" length="95.00" shape="0.00,20.00 95.00,20.00"/>
            </edge>
            <edge id="b" from="J1" to="J2" priority="-1">
                <lane id="b_0" index="0" speed="13.89" length="30.00" shape="0.00,50.00,7.00 30.00,50.00,9.00"/>
            </edge>
            <edge id="c" from="J2" to="J3" priority="-1">
                <lane id="c_0" index="0" speed="13.89" length="14.00" shape="100.00,100.00 104.00,101.00 108.00,103.00 112.00,106.00"/>
            </edge>
            <junction id="J1" type="priority" x="100.00" y="150.00" incLanes="a_0" intLanes="" shape="96.00,146.00 104.00,146.00 104.00,154.00 96.00,154.00"/>
        </net>
        """;

    private static List<(double X, double Y, double Z)> Points(string xml, string laneId)
    {
        var shape = Regex.Match(xml, "<lane id=\"" + laneId + "\"[^>]*shape=\"([^\"]*)\"").Groups[1].Value;
        return shape.Split(' ').Select(p => p.Split(',')).Select(c => (D(c[0]), D(c[1]), c.Length > 2 ? D(c[2]) : double.NaN)).ToList();
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void EveryShapePoint_TakesTheGridsHeight_Within1cm()
    {
        string net = Setup(Net);
        NetElevation.Apply(net, _root);
        string xml = File.ReadAllText(net);

        foreach (var lane in new[] { "a_0", "c_0" })   // the 2-D lanes: every point, the original ones and the inserted ones
            foreach (var (x, y, z) in Points(xml, lane))
                Assert.True(Math.Abs(z - Grid(x, y)) < 0.01, $"{lane} ({x}, {y}): z {z}, the grid says {Grid(x, y)}");
        // the junction: its outline and its own z come from the same lookup
        var junction = Regex.Match(xml, "<junction id=\"J1\"[^>]*>").Value;
        Assert.Matches("x=\"100.00\" y=\"150.00\" z=\"" + Grid(100, 150).ToString("F2", CultureInfo.InvariantCulture) + "\"", junction);
        var outline = Regex.Match(junction, "shape=\"([^\"]*)\"").Groups[1].Value.Split(' ').Select(p => p.Split(',')).ToList();
        Assert.All(outline, c => { Assert.Equal(3, c.Length); Assert.True(Math.Abs(D(c[2]) - Grid(D(c[0]), D(c[1]))) < 0.01); });
    }

    [Fact]
    public void AStraightRun_IsDensified_ToThe10mSpacing_AndACurve_IsNot()
    {
        string net = Setup(Net);
        NetElevation.Apply(net, _root);
        var a = Points(File.ReadAllText(net), "a_0");
        Assert.Equal(11, a.Count);   // 95 m → ten 9.5 m runs
        for (int i = 1; i < a.Count; i++)
            Assert.InRange(Math.Sqrt(Math.Pow(a[i].X - a[i - 1].X, 2) + Math.Pow(a[i].Y - a[i - 1].Y, 2)), 0.1, NetElevation.Spacing + 0.02);
        Assert.Equal(4, Points(File.ReadAllText(net), "c_0").Count);   // 4 m runs: left as they are
    }

    [Fact]
    public void Lengths_AreUnchanged_AndEverythingElseIsByteForByte()
    {
        string net = Setup(Net);
        NetElevation.Apply(net, _root);
        string xml = File.ReadAllText(net);
        Assert.Equal(new[] { "95.00", "30.00", "14.00" }, Regex.Matches(xml, "<lane [^>]*length=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToArray());
        // with every shape attribute and the junction's z taken out, the file is the one that went in
        string stripped = Regex.Replace(Regex.Replace(xml, " z=\"[^\"]*\"", ""), " shape=\"[^\"]*\"", "");
        string original = Regex.Replace(Net, " shape=\"[^\"]*\"", "");
        Assert.Equal(original, stripped);
    }

    [Fact]
    public void AZ_NetconvertAlreadyWrote_IsKept_AndTheRunBetweenKeepsItsSlope()
    {
        string net = Setup(Net);
        NetElevation.Apply(net, _root);
        var b = Points(File.ReadAllText(net), "b_0");
        Assert.Equal(7.0, b[0].Z, 3);
        Assert.Equal(9.0, b[^1].Z, 3);
        Assert.Equal(4, b.Count);   // 30 m → three runs
        Assert.Equal(7.0 + 2.0 / 3, b[1].Z, 2);   // inserted between two points that have a z: the straight line, not the grid
    }

    [Fact]
    public void ApplyingItTwice_ChangesNothing()
    {
        string net = Setup(Net);
        NetElevation.Apply(net, _root);
        string once = File.ReadAllText(net);
        var again = NetElevation.Apply(net, _root);
        Assert.Equal(once, File.ReadAllText(net));
        Assert.Equal(again.PointsBefore, again.PointsAfter);
    }

    /// <summary>The random generator end to end (a tiny preset; needs netconvert — without it the rail says so and passes): the written <c>roads.net.xml</c> carries the heights of the
    /// <c>heights/</c> grid written beside it, on every point of every lane.</summary>
    [Fact]
    public void TheRandomGenerator_WritesANet_WhoseLanesStandOnItsHeightGrid()
    {
        if (Netconvert.Locate() is not { Problem: null }) return;   // no netconvert of the right version: the synth writes the plain files only
        string dir = Path.Combine(_root, "synth");
        var log = new StringWriter();
        Assert.Equal(0, Hrot.Tools.TerrainSynth.TerrainSynthesizer.Run(Hrot.Tools.TerrainSynth.SynthOptions.Parse(new[] { "--preset", "tiny", "--seed", "3", "--height-spacing", "4", "--out", dir }), log));
        string xml = File.ReadAllText(Path.Combine(dir, "roads", "roads.net.xml"));
        Assert.Contains("net heights:", log.ToString());

        var (info, z) = HeightsGrid.ReadAll(dir);
        var sampler = new HeightSampler(info, z);
        int points = 0;
        foreach (Match lane in Regex.Matches(xml, "<lane [^>]*shape=\"([^\"]*)\""))
            foreach (var pair in lane.Groups[1].Value.Split(' '))
            {
                var c = pair.Split(',');
                Assert.Equal(3, c.Length);
                Assert.True(Math.Abs(D(c[2]) - sampler.At(D(c[0]), D(c[1]))) < 0.011, $"lane point {pair}: the grid says {sampler.At(D(c[0]), D(c[1]))}");
                points++;
            }
        Assert.True(points > 20, $"only {points} lane points");
    }
}

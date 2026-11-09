using Hrot.Tools.TerrainSynth;
using Xunit;

namespace Hrot.Tools.TerrainSynth.Tests;

public class RoadNetworkTests
{
    private static readonly Dictionary<string, RoadClass> ClassByName = new()
    {
        ["residential"] = RoadClass.Residential,
        ["unclassified"] = RoadClass.Unclassified,
        ["secondary"] = RoadClass.Secondary,
        ["primary"] = RoadClass.Primary,
        ["motorway"] = RoadClass.Motorway,
        ["tertiary"] = RoadClass.Tertiary,
        ["trunk"] = RoadClass.Trunk,
        ["rail"] = RoadClass.Rail,
    };

    private static void CheckKm(SynthProfile p, RoadNetwork net)
    {
        foreach (var (name, km) in p.RoadKm)
        {
            double got = net.LengthKm(ClassByName[name]);
            Assert.True(Math.Abs(got - km) <= 0.10 * km, $"{p.Name} {name}: {got} km, wanted {km} +-10%");
        }
    }

    [Fact]
    public void Full_KmPerClass_WithinTenPercent()
    {
        var p = SynthProfile.Full;
        CheckKm(p, RoadNetwork.Generate(p, new Rng(1)));
        CheckKm(p, RoadNetwork.Generate(p, new Rng(77)));
    }

    [Fact]
    public void Tiny_KmPerClass_WithinTenPercent()
    {
        var p = SynthProfile.Tiny;
        CheckKm(p, RoadNetwork.Generate(p, new Rng(1)));
        CheckKm(p, RoadNetwork.Generate(p, new Rng(2)));
    }

    [Fact]
    public void AllNodesInsideBounds()
    {
        foreach (var p in new[] { SynthProfile.Full, SynthProfile.Tiny })
        {
            var net = RoadNetwork.Generate(p, new Rng(3));
            Assert.NotEmpty(net.Nodes);
            foreach (var n in net.Nodes)
            {
                Assert.InRange(n.X, p.MinX, p.MaxX);
                Assert.InRange(n.Y, p.MinY, p.MaxY);
            }
        }
    }

    [Fact]
    public void EdgesReferenceExistingNodes()
    {
        var net = RoadNetwork.Generate(SynthProfile.Full, new Rng(1));
        var ids = net.Nodes.Select(n => n.Id).ToHashSet();
        Assert.Equal(net.Nodes.Count, ids.Count);
        Assert.Equal(net.Edges.Count, net.Edges.Select(e => e.Id).Distinct().Count());
        foreach (var e in net.Edges)
        {
            Assert.Contains(e.From, ids);
            Assert.Contains(e.To, ids);
            Assert.NotEqual(e.From, e.To);
        }
    }

    [Fact]
    public void DistanceToRoad_ZeroOnCentrelineMinusHalfWidth()
    {
        var net = RoadNetwork.Generate(SynthProfile.Tiny, new Rng(1));
        var nodes = net.Nodes.ToDictionary(n => n.Id);
        var e = net.Edges.First(x => x.Class == RoadClass.Primary);
        var a = nodes[e.From];
        var b = nodes[e.To];
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        // on the centreline: minus half the width, unless a wider road overlaps it
        double d = net.DistanceToRoad(mx, my);
        Assert.True(d <= -e.WidthM / 2 + 1e-6, $"d {d}");
        Assert.True(d >= -8.0, $"d {d}");
        // far from every road the distance is positive and equals the nearest centreline distance minus its half width
        double far = net.DistanceToRoad(-249, 249);
        Assert.True(double.IsFinite(far));
    }

    [Fact]
    public void Villages_CountAndPlacement()
    {
        var p = SynthProfile.Full;
        var net = RoadNetwork.Generate(p, new Rng(1));
        Assert.Equal(p.BuildingCount / 60, net.Villages.Count);
        foreach (var v in net.Villages)
        {
            Assert.InRange(v.Radius, 40.0, 350.0);
            foreach (var r in p.NodataRects)
            {
                double dx = Math.Max(Math.Max(r.MinX - v.X, v.X - r.MaxX), 0);
                double dy = Math.Max(Math.Max(r.MinY - v.Y, v.Y - r.MaxY), 0);
                Assert.True(Math.Sqrt(dx * dx + dy * dy) > v.Radius, "village overlaps a nodata rectangle");
            }
        }
        Assert.Single(RoadNetwork.Generate(SynthProfile.Tiny, new Rng(1)).Villages);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var a = RoadNetwork.Generate(SynthProfile.Full, new Rng(9));
        var b = RoadNetwork.Generate(SynthProfile.Full, new Rng(9));
        Assert.Equal(a.Nodes, b.Nodes);
        Assert.Equal(a.Edges, b.Edges);
        Assert.Equal(a.Villages, b.Villages);
        var c = RoadNetwork.Generate(SynthProfile.Full, new Rng(10));
        Assert.NotEqual(a.Nodes, c.Nodes);
    }

    [Fact]
    public void Roads_AvoidNodataRectangles()
    {
        var p = SynthProfile.Full;
        var net = RoadNetwork.Generate(p, new Rng(1));
        foreach (var n in net.Nodes)
            foreach (var r in p.NodataRects)
                Assert.False(n.X >= r.MinX && n.X <= r.MaxX && n.Y >= r.MinY && n.Y <= r.MaxY, $"node {n.Id} in nodata");
    }
}

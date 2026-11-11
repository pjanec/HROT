using System.Numerics;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Traffic;
using Hrot.Terrain.InputSet;
using Sim.Ingest;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ T-7b (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k-3) — SUMO's half of a fallen bridge: the engine's <c>CloseEdge</c> (a vendored HROT addition) through the session the stepping thread drives. A hand-made net: a lead-in road
/// <c>pre</c> (x −100..0), then TWO ways to the far side — the direct edge <c>ab</c> (the "bridge", x 0..100 along y = 0) and a detour <c>ac</c>/<c>cb</c> round a point 80 m north — then <c>post</c>. A car handed to SUMO from the
/// lead-in to the far end drives over <c>ab</c>; when <c>ab</c> is closed while it is still on <c>pre</c> it is re-routed over the detour. Needs netconvert (scripts/install-sumo.sh).
/// </summary>
public sealed class BridgeClosureSumoTests : IDisposable
{
    private const int Body = 9100;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bcs-" + Guid.NewGuid().ToString("N")[..10]);
    private static readonly bool HasNetconvert = Netconvert.Locate() is { Path: not null, Problem: null };

    private string BuildNet()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "n.nod.xml"), """<nodes><node id="S" x="-100" y="0"/><node id="A" x="0" y="0"/><node id="B" x="100" y="0"/><node id="C" x="50" y="80"/><node id="D" x="200" y="0"/></nodes>""");
        File.WriteAllText(Path.Combine(_root, "e.edg.xml"), """
            <edges><edge id="pre" from="S" to="A" numLanes="1" speed="14"/><edge id="ab" from="A" to="B" numLanes="1" speed="14"/><edge id="ac" from="A" to="C" numLanes="1" speed="14"/>
                   <edge id="cb" from="C" to="B" numLanes="1" speed="14"/><edge id="post" from="B" to="D" numLanes="1" speed="14"/></edges>
            """);
        var loc = Netconvert.Locate();
        string net = Path.Combine(_root, "n.net.xml");
        var (code, _, err) = Netconvert.Run(loc.Path!, new[] { "-n", Path.Combine(_root, "n.nod.xml"), "-e", Path.Combine(_root, "e.edg.xml"), "-o", net, "--offset.disable-normalization", "true", "--no-turnarounds", "true" }, null, Netconvert.SumoHomeOf(loc.Path!));
        Assert.True(code == 0, err);
        return net;
    }

    /// <summary>Drives a car from the lead-in to the far end; closes <c>ab</c> after 3 s when <paramref name="close"/>; returns how far north of the straight road it went.</summary>
    private float Drive(bool close)
    {
        string net = BuildNet();
        var placement = SumoNetPlacement.For(NetworkParser.ParseXml(File.ReadAllText(net)), new TerrainSumoNet("n.net.xml", 0, 0), null);
        int n = 0;
        using var session = new TrafficSession(new TrafficSessionKey(net, _root, "h", new TrafficDemand { TargetCars = 0, StepSeconds = 0.5f }, 0),
            new TrafficServeOptions(() => Interlocked.Increment(ref n), Participant: null, Source: 1), Array.Empty<TrafficVehicleType>(), 0.0);
        session.SetHandovers(new[] { new TrafficHandover(Body, 1, -90.0, 0.0, 0.0, 0.0, 190.0, 0.0, VType: null) });
        float maxNorth = 0f; bool closed = false; Vector2 last = default;
        for (double t = 0.5; t <= 60.0; t += 0.5)
        {
            session.StepTo(t);
            if (close && !closed && t >= 3.0) { session.SetClosedEdges(new[] { "ab" }); closed = true; }
            var poses = new List<TrafficCarPose>();
            session.Source.Pump();
            new TrafficMirror(placement, owner: 1).Resolve(session.Source, session.Lanes, session.Time - 0.5, poses);
            foreach (var p in poses) if (p.Id == TrafficShadowFollowSystem.ShadowIdOf(Body)) { maxNorth = MathF.Max(maxNorth, p.Y); last = new Vector2(p.X, p.Y); }
        }
        if (close) { Assert.Equal(1, session.Rerouted); Assert.Contains("ab", session.ClosedEdges); }
        Assert.True(last.X > -90f, "the shadow was never seen driving");
        return maxNorth;
    }

    /// <summary>
    /// (b) With <c>ab</c> open the car takes it (never more than a few metres off the y = 0 road); closed while the car is still on the lead-in it takes the detour (≈ 80 m north) — the session reports one car re-routed.
    /// 🔴 Red-proof: make <c>CloseEdge</c> a no-op ⇒ the car still drives <c>ab</c>.
    /// </summary>
    [Fact]
    public void ACarWhoseRouteCrossesTheClosedEdge_IsReroutedAtTheClosure()
    {
        if (!HasNetconvert) return;
        Assert.InRange(Drive(close: false), -1f, 5f);
        Assert.True(Drive(close: true) > 40f, "closed, the car went round by the detour");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}

using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Traffic;
using Hrot.Map.Common.Services;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ AQ93 §14 G3 (R-4023, SumoSharp H13) — through the session the stepping thread drives, over demo-city's real net: a hand-over
/// spawns the shadow <c>hrot:&lt;id&gt;</c>, SUMO drives it to the destination and it vanishes there; revoking the hand-over takes it back.
/// </summary>
/// <remarks>⭐ A start and destination known to be connected: a moving SUMO car's pose, and where that same car is 20 s later.</remarks>
public sealed class AVehicleHandedToSumoArrivesTests
{
    private const long Body = 9001;

    [Fact]
    public void TheShadow_DrivesToTheDestination_AndVanishesThere()
    {
        var cache = Load(out var world);
        using var _ = world;
        using var session = Session(cache);
        var (start, heading, dest) = ARouteACarDrove(session, cache);

        session.SetHandovers(new[] { Handover(cache, start, heading, dest, intent: 1) });
        bool seen = false;
        Vector2 last = default;
        double end = session.Time + 200.0;
        for (double t = session.Time + 0.5; t < end && (!seen || Shadow(session, cache) is not null); t += 0.5)   // every SUMO step
        {
            session.StepTo(t);
            if (Shadow(session, cache) is { } p) { seen = true; last = new Vector2(p.X, p.Y); }
        }

        Assert.True(seen, "SUMO never drove the shadow — the hand-over was refused");
        Assert.Null(Shadow(session, cache));
        Assert.True(Vector2.Distance(last, dest) < TrafficShadowFollowSystem.ArrivalSlackMetres,
            $"the shadow was last seen {Vector2.Distance(last, dest):0.0} m from the destination");
        Assert.Contains(Body, session.Shadows);   // arrived for this intent: not respawned while the intent stands
    }

    [Fact]
    public void RevokingTheHandover_TakesTheShadowBack()
    {
        var cache = Load(out var world);
        using var _ = world;
        using var session = Session(cache);
        var (start, heading, dest) = ARouteACarDrove(session, cache);

        session.SetHandovers(new[] { Handover(cache, start, heading, dest, intent: 1) });
        session.StepTo(session.Time + 2.0);
        Assert.NotNull(Shadow(session, cache));

        session.SetHandovers(Array.Empty<TrafficHandover>());
        session.StepTo(session.Time + 2.0);
        Assert.Null(Shadow(session, cache));
        Assert.Empty(session.Shadows);
    }

    private static (Vector2 Start, float Heading, Vector2 Dest) ARouteACarDrove(TrafficSession s, TrafficNetCache cache)
    {
        s.StepTo(20.0); s.StepTo(40.0);
        var a = Resolve(s, cache, s.Time - 0.5);
        s.StepTo(s.Time + 20.0);
        var b = Resolve(s, cache, s.Time - 0.5);
        foreach (var p0 in a)
        {
            var p1 = b.FirstOrDefault(p => p.Handle.Equals(p0.Handle));
            if (p1.Id is not null && Vector2.Distance(new(p0.X, p0.Y), new(p1.X, p1.Y)) > 150f)
                return (new Vector2(p0.X, p0.Y), p0.YawRad, new Vector2(p1.X, p1.Y));
        }
        throw new Xunit.Sdk.XunitException("no car drove 150 m in 20 s — the rail proves nothing");
    }

    private static TrafficHandover Handover(TrafficNetCache cache, Vector2 start, float heading, Vector2 dest, uint intent)
    {
        var (x, y) = cache.Placement.ToNet(start.X, start.Y);
        var (dx, dy) = cache.Placement.ToNet(dest.X, dest.Y);
        return new TrafficHandover(Body, intent, x, y, heading, 0.0, dx, dy, VType: null);   // anchored net: local and net headings agree
    }

    private static TrafficCarPose? Shadow(TrafficSession s, TrafficNetCache cache)
    {
        var shadow = Resolve(s, cache, s.Time - 0.5).FirstOrDefault(c => c.Id == TrafficShadowFollowSystem.ShadowIdOf(Body));
        return shadow.Id is null ? null : shadow;
    }

    private static List<TrafficCarPose> Resolve(TrafficSession s, TrafficNetCache cache, double at)
    {
        var into = new List<TrafficCarPose>();
        s.Source.Pump();
        new TrafficMirror(cache.Placement, owner: 1).Resolve(s.Source, s.Lanes, at, into);
        return into;
    }

    private static TrafficSession Session(TrafficNetCache cache)
    {
        long n = 0;
        return new TrafficSession(new TrafficSessionKey(cache.Path, Path.GetDirectoryName(cache.Path)!, cache.NetHash, TrafficDemand.Default, 0),
            new TrafficServeOptions(() => Interlocked.Increment(ref n), Participant: null, Source: 1), Array.Empty<TrafficVehicleType>(), 0.0);
    }

    private static TrafficNetCache Load(out EntityRepository world)
    {
        world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TrafficRoadSceneTests.TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return TrafficNetCache.Of(world)!;
    }
}

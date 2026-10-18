using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Traffic;
using Hrot.Map.Common.Services;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ AQ93 §13 (E4, R-4027) — over demo-city's real net, through the session the stepping thread drives: a SUMO car stops behind
/// one of our vehicles parked in its lane, and — the control, same seed, no bodies — the same car drives through that point.
/// </summary>
public sealed class SumoCarsBrakeForOurBodiesTests
{
    private const float Ahead = 80f;        // metres in front of the car — past its stopping distance (~28 m at 16 m/s, measured)
    private const float TruckLength = 7f, TruckWidth = 2.5f;

    [Fact]
    public void ACarStopsBehindOurParkedTruck_AndWithoutItDrivesOn()
    {
        var cache = Load(out var world);
        using var _ = world;

        using var withBodies = new TrafficSession(Key(cache), Options(), Array.Empty<TrafficVehicleType>(), startTime: 0.0);
        using var control = new TrafficSession(Key(cache), Options(), Array.Empty<TrafficVehicleType>(), startTime: 0.0);
        foreach (var s in new[] { withBodies, control }) { s.StepTo(20.0); s.StepTo(40.0); }

        // Moving cars and a truck parked 80 m ahead of each, in its lane.
        var a = Resolve(withBodies, cache, 39.0);
        var b = Resolve(withBodies, cache, 39.5);
        var probes = b.Select(p1 => (p1, p0: a.FirstOrDefault(p => p.Handle.Equals(p1.Handle))))
            .Where(x => x.p0.Id is not null && Vector2.Distance(Pos(x.p0), Pos(x.p1)) > 2f)
            .Take(6)
            .Select(x => (car: x.p1, forward: Forward(x.p1), at: Pos(x.p1) + Ahead * Forward(x.p1)))
            .ToList();
        Assert.True(probes.Count > 0, "no moving car to put a truck in front of — the rail proves nothing");
        withBodies.SetBodies(probes.Select((p, i) =>
        {
            var (nx, ny) = cache.Placement.ToNet(p.at.X, p.at.Y);
            return new TrafficBody(90_000 + i, TrafficBodyKind.Vehicle, nx, ny, 0, 0, TruckLength, TruckWidth);
        }).ToArray());

        for (double t = 45.0; t <= 70.0; t += 5.0) { withBodies.StepTo(t); control.StepTo(t); }
        Assert.True(withBodies.Feed is { ObstacleCount: > 0 }, "SUMO registered no lane obstacle for the trucks");

        var after = Resolve(withBodies, cache, 69.5);
        var afterControl = Resolve(control, cache, 69.5);
        int stoppedInLane = 0, passedInControl = 0;
        foreach (var p in probes)
        {
            var now = after.FirstOrDefault(c => c.Handle.Equals(p.car.Handle));
            if (now.Id is not null && InLine(p, now))
            {
                float along = Vector2.Dot(Pos(now) - p.at, p.forward);
                Assert.True(along < -TruckLength / 2f, $"car {p.car.Id} drove through our truck (along {along:0.0} m)");
                stoppedInLane++;
            }
            var free = afterControl.FirstOrDefault(c => c.Handle.Equals(p.car.Handle));
            if (free.Id is null || Vector2.Dot(Pos(free) - p.at, p.forward) > 0f) passedInControl++;
        }
        Assert.True(stoppedInLane > 0, "no car stayed in its lane behind a truck — inconclusive");
        Assert.True(passedInControl > 0, "without the trucks no car got past their points — the control proves nothing");
    }

    // A car still on the straight line it was driving (it did not turn off before reaching the truck).
    private static bool InLine((TrafficCarPose car, Vector2 forward, Vector2 at) p, TrafficCarPose now)
    {
        var d = Pos(now) - Pos(p.car);
        return MathF.Abs(d.X * p.forward.Y - d.Y * p.forward.X) < 2f;
    }

    private static Vector2 Pos(TrafficCarPose p) => new(p.X, p.Y);
    private static Vector2 Forward(TrafficCarPose p) => new(MathF.Cos(p.YawRad), MathF.Sin(p.YawRad));

    private static List<TrafficCarPose> Resolve(TrafficSession s, TrafficNetCache cache, double at)
    {
        var into = new List<TrafficCarPose>();
        s.Source.Pump();
        new TrafficMirror(cache.Placement, owner: 1).Resolve(s.Source, s.Lanes, at, into);
        return into;
    }

    private static TrafficServeOptions Options()
    {
        long n = 0;
        return new TrafficServeOptions(() => Interlocked.Increment(ref n), Participant: null, Source: 1);
    }

    private static TrafficSessionKey Key(TrafficNetCache cache)
        => new(cache.Path, Path.GetDirectoryName(cache.Path)!, cache.NetHash, TrafficDemand.Default, 0);

    private static TrafficNetCache Load(out EntityRepository world)
    {
        world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TrafficRoadSceneTests.TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return TrafficNetCache.Of(world)!;
    }
}

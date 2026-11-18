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
    private const int Body = 9001;

    /// <summary>⭐ <c>CE-9000</c> — the convoy's escort (priority: the blue-light twin, SumoSharp H17) is driven to its destination exactly like an
    /// ordinary shadow. 🔴 Red-proof: a priority spawn refused (e.g. the twin's type unknown) ⇒ "never drove" ⇒ red.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheShadow_DrivesToTheDestination_AndVanishesThere(bool priority)
    {
        var cache = Load(out var world);
        using var _ = world;
        using var session = Session(cache);
        var (start, heading, dest) = ARouteACarDrove(session, cache);

        session.SetHandovers(new[] { Handover(cache, start, heading, dest, intent: 1) with { Priority = priority } });
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

    /// <summary>⭐ <c>CE-9000</c> (C-6) — a stretched convoy's escort is slowed through its shadow's top speed (H17 <c>SetExternalVehicleMaxSpeed</c>) and
    /// freed again when the cap goes. 🔴 Red-proof: the session never applying <c>MaxSpeed</c> ⇒ the capped window drives at full speed ⇒ red.</summary>
    [Fact]
    public void AStretchedConvoysEscort_IsCappedThroughItsShadow_AndFreedAgain()
    {
        var cache = Load(out var world);
        using var _ = world;
        using var session = Session(cache);
        var (start, heading, dest) = ARouteACarDrove(session, cache);
        var h = Handover(cache, start, heading, dest, intent: 1) with { Priority = true };

        session.SetHandovers(new[] { h });
        session.StepTo(session.Time + 4.0);
        session.SetHandovers(new[] { h with { MaxSpeed = 1.5 } });
        session.StepTo(session.Time + 4.0);   // braking down to the cap
        var a = Shadow(session, cache); Assert.NotNull(a);
        session.StepTo(session.Time + 4.0);
        var b = Shadow(session, cache); Assert.NotNull(b);
        float capped = Vector2.Distance(new(a!.Value.X, a.Value.Y), new(b!.Value.X, b.Value.Y)) / 4f;

        session.SetHandovers(new[] { h with { MaxSpeed = null } });
        session.StepTo(session.Time + 8.0);
        var c = Shadow(session, cache);
        session.StepTo(session.Time + 4.0);
        var d = Shadow(session, cache);
        float freed = c is { } cc && d is { } dd ? Vector2.Distance(new(cc.X, cc.Y), new(dd.X, dd.Y)) / 4f : float.MaxValue;   // arrived ⇒ it drove on
        Assert.True(capped <= 1.5f + 0.2f, $"capped at 1.5 m/s, it drove {capped:0.00} m/s");
        Assert.True(freed > capped + 1f, $"freed, it still drove {freed:0.00} m/s");
    }

    /// <summary>⭐ <c>CE-9020</c> ① (mode 1 → 2) — a destination OFF the net (beside the road the car drove, farther than SUMO's snap radius):
    /// sent as it is, SUMO refuses the hand-over; through <see cref="TrafficHandoverSystem.Gather"/> it is sent the nearest road point, SUMO drives
    /// the shadow there and it vanishes within the arrival slack of that point (where <see cref="TrafficShadowFollowSystem"/> reads it as the
    /// traffic leg's arrival). 🔴 Red-proof: <c>Gather</c> sending the destination as it is ⇒ refused ⇒ "never drove" ⇒ red.</summary>
    [Fact]
    public void CE9020_ADestinationOffTheNet_IsDrivenToTheNearestRoadPoint()
    {
        var cache = Load(out var world);
        using var _ = world;
        using var session = Session(cache);
        var (start, heading, dest) = ARouteACarDrove(session, cache);
        var (off, road) = BesideTheRoad(cache, dest, heading);

        session.SetHandovers(new[] { Handover(cache, start, heading, off, intent: 1) });   // the destination as it is
        session.StepTo(session.Time + 1.0);
        Assert.Null(Shadow(session, cache));                                                 // refused: no lane near it

        world.RegisterComponent<SimTransform>(); world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
        var e = world.CreateEntity();
        var fwd = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        world.AddComponent(e, new SimTransform { Position = new Vector3(start - fwd * 2.5f, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, heading) });
        world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(Body));
        world.AddComponent(e, new Fdp.Toolkit.Navigation.NavigationIntent
            { Mode = Fdp.Toolkit.Navigation.NavigationMode.Traffic, IntentId = 2, FinalDestination = new Vector3(off, 0) });
        session.SetHandovers(TrafficHandoverSystem.Gather(world, cache.Placement, null, cache.Model));

        bool seen = false;
        Vector2 last = default;
        double end = session.Time + 200.0;
        for (double t = session.Time + 0.5; t < end && (!seen || Shadow(session, cache) is not null); t += 0.5)
        {
            session.StepTo(t);
            if (Shadow(session, cache) is { } p) { seen = true; last = new Vector2(p.X, p.Y); }
        }
        Assert.True(seen, "SUMO never drove the shadow — the road point was refused too");
        Assert.True(Vector2.Distance(last, road) < TrafficShadowFollowSystem.ArrivalSlackMetres,
            $"the shadow was last seen {Vector2.Distance(last, road):0.0} m from the road point");
        Assert.True(Vector2.Distance(last, off) > TrafficShadowFollowSystem.ArrivalSlackMetres, "the destination itself was off the road — the rail proves nothing");
    }

    // A point to the RIGHT of the road at `at` (the side of its own lane), farther from every lane than the arrival slack, whose road point
    // is still on that road (within 12 m of `at`).
    private static (Vector2 Off, Vector2 Road) BesideTheRoad(TrafficNetCache cache, Vector2 at, float heading)
    {
        var right = new Vector2(MathF.Sin(heading), -MathF.Cos(heading));
        for (float d = 30f; d <= 80f; d += 5f)
        {
            var off = at + right * d;
            if (TrafficRoadPoint.Near(cache.Model, cache.Placement, off, out var road, out bool onNet) && !onNet
                && Vector2.Distance(off, road) > TrafficShadowFollowSystem.ArrivalSlackMetres + 5f && Vector2.Distance(road, at) < 12f)
                return (off, road);
        }
        throw new Xunit.Sdk.XunitException("no point beside the road the car drove is off the net — the rail proves nothing");
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

    /// <summary>
    /// ⭐⭐ <c>CE-1972</c> (the user, `2026-11-17`: "the convoy leader swings from one lane to the other and back, and the two followers copy it") — through SumoSharp on
    /// demo-city: the escort's priority shadow, slowed on and off by the C-6 cap, with its column fed BEHIND it as the editor feeds it (three trucks and the gap squares
    /// AT REST, as <c>TrafficBodies.AddConvoyGaps</c> tells them — R-5063), holds its lane: no step drifts sideways faster than 0.5 m/s. 📐 With the squares carrying the
    /// follower's 12 m/s the same run swerves on 146 of 180 samples at ±2 m/s (B6 evasion: SumoSharp projects a moving obstacle forward into a slower vehicle) — live,
    /// the lead's x swung ±1 m every ~9 s (cv-red-lights). Red-proof is the feed's own rail: <c>TrafficBodyFeedTests.CE9002_*</c> asserts the squares at rest.
    /// </summary>
    [Fact]
    public void CE1972_ACappedEscortsShadow_WithItsColumnFedBehindIt_HoldsItsLane()
    {
        const bool gaps = true, capped = true, still = true;
        var cache = Load(out var world);
        using var _ = world;
        using var session = Session(cache);
        var (start, heading, dest) = ARouteACarDrove(session, cache);
        var h0 = Handover(cache, start, heading, dest, intent: 1) with { Priority = true };
        session.SetHandovers(new[] { h0 });
        var log = new System.Text.StringBuilder();
        TrafficCarPose? prev = null; float maxLat = 0f; int wob = 0, n = 0;
        double t0 = session.Time, end = session.Time + 60.0;
        for (double t = session.Time + 0.5; t < end; t += 0.5)
        {
            if (capped) session.SetHandovers(new[] { ((int)((t - t0) / 4.5) % 2 == 1) ? h0 with { MaxSpeed = 4.0 } : h0 });
            // the column behind the lead, as the editor feeds it: the lead entity is where the shadow was resolved (one step behind)
            var lead = Resolve(session, cache, session.Time - 0.5).FirstOrDefault(x => x.Id == TrafficShadowFollowSystem.ShadowIdOf(Body));
            var bodies = new List<TrafficBody>();
            if (lead.Id is not null)
            {
                var fwd = new Vector2(MathF.Cos(lead.YawRad), MathF.Sin(lead.YawRad));
                var lp = new Vector2(lead.X, lead.Y);
                var vel = still ? Vector2.Zero : fwd * 12f;
                for (int m = 1; m <= 3; m++)
                {
                    var fp = lp - fwd * (22f * m);   // 15 m gaps, 7 m vehicles
                    var (bx, by) = cache.Placement.ToNet(fp.X, fp.Y);
                    bodies.Add(new TrafficBody(9100 + m, TrafficBodyKind.Vehicle, bx, by, vel.X, vel.Y, 7f, 3.5f));
                    if (gaps)
                    {
                        var a = fp + fwd * 3.5f; var b = lp - fwd * (22f * (m - 1)) - fwd * 3.5f;
                        for (int k = 0; k < 5; k++) { var c = Vector2.Lerp(a, b, (k + 0.5f) / 5f); var (cx, cy) = cache.Placement.ToNet(c.X, c.Y); bodies.Add(new TrafficBody(-(m * 64 + k + 1), TrafficBodyKind.Vehicle, cx, cy, vel.X, vel.Y, 3f, 3.5f)); }
                    }
                }
            }
            session.SetBodies(bodies.ToArray());
            session.StepTo(t);
            foreach (double sub in new[] { -0.75, -0.5 })
            {
                var c = Resolve(session, cache, session.Time + sub).FirstOrDefault(x => x.Id == TrafficShadowFollowSystem.ShadowIdOf(Body));
                if (c.Id is null) { prev = null; continue; }
                if (prev is { } p0)
                {
                    var d = new Vector2(c.X - p0.X, c.Y - p0.Y) / 0.25f;
                    var nrm = new Vector2(-MathF.Sin(c.YawRad), MathF.Cos(c.YawRad));
                    float lat = Vector2.Dot(d, nrm); n++;
                    if (MathF.Abs(lat) > 0.5f) wob++;
                    maxLat = MathF.Max(maxLat, MathF.Abs(lat));
                    if (log.Length < 900 && d.Length() > 2f) log.Append($"{lat:0.0} ");
                }
                prev = c;
            }
        }
        Assert.True(n > 100, $"only {n} samples of the shadow");
        Assert.True(wob == 0, $"the shadow drifted sideways on {wob} of {n} samples (max {maxLat:0.00} m/s): {log}");
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
        int n = 0;
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

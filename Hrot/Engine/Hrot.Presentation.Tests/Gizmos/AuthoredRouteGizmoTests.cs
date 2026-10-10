using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3123</c> (R-228) — <see cref="AuthoredRouteGizmo"/> draws the route a mover follows: the cases of the retired
/// <c>SimHostTrajectoryLayerTests</c> (ROUTES1-T011), ported to what the gizmo emits. Which movers it draws for is the Path family
/// policy's job (selected / pinned / all — <c>MapCullingPolicyTests</c>), so these rails call <c>Draw</c> on the mover directly.
/// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
/// </summary>
public sealed class AuthoredRouteGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();
    private readonly AuthoredRouteGizmo _gizmo = new();

    public AuthoredRouteGizmoTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NavState>();
        _w.RegisterComponent<PersonalRouteRef>();
        _w.RegisterComponent<RouteTrajectoryCache>();
        _w.RegisterManagedComponent<RoutePlan>();
    }

    public void Dispose() => _w.Dispose();

    private static RoutePlan Plan(int count)
    {
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            for (int i = 0; i < count; i++)
                wps.Add(new RouteWaypoint { Position = new Vector3(i * 10f, 0f, i * 20f) });
        });
        return plan;
    }

    private Entity Mover(NavState nav)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform());
        _w.AddComponent(e, nav);
        return e;
    }

    private (int lines, int dots, DebugPrimitive[] all) Drawn()
    {
        var all = _draw.GetFrame().ToArray().Where(p => p.DebugLayer == DebugTraceLayers.Paths).ToArray();
        return (all.Count(p => p.Shape == DebugPrimitiveShape.Line), all.Count(p => p.Shape == DebugPrimitiveShape.Sphere), all);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(2, 1)]
    public void CE3123_APersonalRoute_DrawsItsSegmentsAndWaypoints(int waypoints, int segments)
    {
        var route = _w.CreateEntity();
        _w.SetManagedComponent(route, Plan(waypoints));
        var mover = Mover(new NavState { Mode = KinematicsMode.None });
        _w.AddComponent(mover, new PersonalRouteRef { RouteEntity = route });

        _gizmo.Draw(_w, mover, _draw);

        var (lines, dots, all) = Drawn();
        Assert.Equal(segments, lines);
        Assert.Equal(waypoints, dots);
        Assert.All(all, p => Assert.Equal(AuthoredRouteGizmo.PersonalColor, p.Color));
        // a waypoint stores X = east, Z = north
        Assert.Contains(all, p => p.Shape == DebugPrimitiveShape.Sphere && p.SphereCenter == new Vector3(10, 20, 0));
    }

    [Fact]
    public void CE3123_ASharedRoute_IsFoundByTheTrajectoryTheMoverDrives_AndStaysStableAcrossFrames()
    {
        var other = _w.CreateEntity();
        _w.AddComponent(other, new RouteTrajectoryCache { TrajectoryId = 4, CompiledVersion = 1 });
        _w.SetManagedComponent(other, Plan(5));
        var route = _w.CreateEntity();
        _w.AddComponent(route, new RouteTrajectoryCache { TrajectoryId = 5, CompiledVersion = 1 });
        _w.SetManagedComponent(route, Plan(3));
        var mover = Mover(new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = 5 });

        for (int frame = 0; frame < 3; frame++)
        {
            _draw.Clear();
            _gizmo.Draw(_w, mover, _draw);
            var (lines, dots, all) = Drawn();
            Assert.Equal(2, lines);
            Assert.Equal(3, dots);
            Assert.All(all, p => Assert.Equal(AuthoredRouteGizmo.SharedColor, p.Color));
        }
    }

    [Fact]
    public void CE3123_AMoverWithNoRoute_DrawsNothing()
    {
        var mover = Mover(new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });
        _gizmo.Draw(_w, mover, _draw);
        Assert.Empty(Drawn().all);
    }

    [Fact]
    public void CE3123_AWorldWithoutRouteComponents_DrawsNothingAndDoesNotThrow()
    {
        using var bare = new EntityRepository();
        bare.RegisterComponent<SimTransform>();
        bare.RegisterComponent<NavState>();
        var e = bare.CreateEntity();
        bare.AddComponent(e, new SimTransform());
        bare.AddComponent(e, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = 3 });
        _gizmo.Draw(bare, e, _draw);
        Assert.Empty(Drawn().all);
    }
}

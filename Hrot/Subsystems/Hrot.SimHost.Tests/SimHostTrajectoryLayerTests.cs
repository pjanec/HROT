using System;
using System.Numerics;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Visualization;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.Vis2D.Abstractions;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Unit tests for the <see cref="SimHostTrajectoryLayer"/> route-rendering extension —
/// ROUTES1-T011.
///
/// Uses <see cref="SimHostTrajectoryLayer.TestHook_SkipRaylibCalls"/> so that Raylib
/// is never invoked. Draw-call counter fields are used to assert correct rendering
/// without a GPU context.
/// </summary>
public class SimHostTrajectoryLayerTests : IDisposable
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly TrajectoryPoolManager _pool     = new();
    private readonly EntityRepository      _repo;
    private readonly InspectorState        _inspector;

    public SimHostTrajectoryLayerTests()
    {
        _repo      = CreateWorld();
        _inspector = new InspectorState();
    }

    public void Dispose() => _pool.Dispose();

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NavState>();
        repo.RegisterComponent<PersonalRouteRef>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterManagedComponent<RoutePlan>();
        return repo;
    }

    private SimHostTrajectoryLayer CreateLayer()
        => new SimHostTrajectoryLayer(_pool, (ISimulationView)_repo, _inspector)
        {
            TestHook_SkipRaylibCalls = true,
        };

    private static readonly RenderContext AnyCtx = new RenderContext { VisibleLayersMask = uint.MaxValue };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RoutePlan MakePlan(int count)
    {
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            for (int i = 0; i < count; i++)
                wps.Add(new RouteWaypoint { Position = new Vector3(i * 10f, 0f, i * 10f) });
        });
        return plan;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Personal route rendering (path 2)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the selected vehicle has a <see cref="PersonalRouteRef"/> pointing to a
    /// route entity with a 4-waypoint plan, the layer must draw 3 segments and 4 circles.
    /// </summary>
    [Fact]
    public void Draw_PersonalRouteRef_FourWaypoints_RendersThreeLinesAndFourCircles()
    {
        // ── Arrange ───────────────────────────────────────────────────────────
        var routeEntity = _repo.CreateEntity();
        _repo.SetManagedComponent(routeEntity, MakePlan(4));

        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState { Mode = KinematicsMode.None }); // mode doesn't matter for personal route
        _repo.AddComponent(vehicle, new PersonalRouteRef { RouteEntity = routeEntity });

        _inspector.SelectedEntity = vehicle;

        // ── Act ───────────────────────────────────────────────────────────────
        var layer = CreateLayer();
        layer.Draw(AnyCtx);

        // ── Assert ────────────────────────────────────────────────────────────
        // 4 waypoints → 3 segments + 4 circles.
        Assert.Equal(3, layer.TestHook_LineDrawCount);
        Assert.Equal(4, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// A 2-waypoint personal route must render 1 segment and 2 circles (minimum valid plan).
    /// </summary>
    [Fact]
    public void Draw_PersonalRouteRef_TwoWaypoints_RendersOneLineAndTwoCircles()
    {
        var routeEntity = _repo.CreateEntity();
        _repo.SetManagedComponent(routeEntity, MakePlan(2));

        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState { Mode = KinematicsMode.None });
        _repo.AddComponent(vehicle, new PersonalRouteRef { RouteEntity = routeEntity });

        _inspector.SelectedEntity = vehicle;

        var layer = CreateLayer();
        layer.Draw(AnyCtx);

        Assert.Equal(1, layer.TestHook_LineDrawCount);
        Assert.Equal(2, layer.TestHook_CircleDrawCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Shared route rendering (path 3)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the selected vehicle follows a <see cref="KinematicsMode.CustomTrajectory"/>
    /// and a route entity's <see cref="RouteTrajectoryCache.TrajectoryId"/> matches,
    /// the layer must draw the shared route waypoints.
    /// </summary>
    [Fact]
    public void Draw_SharedRouteMatchingTrajectoryId_RendersWaypoints()
    {
        // ── Arrange ───────────────────────────────────────────────────────────
        var routeEntity = _repo.CreateEntity();
        _repo.AddComponent(routeEntity, new RouteTrajectoryCache { TrajectoryId = 5, CompiledVersion = 1 });
        _repo.SetManagedComponent(routeEntity, MakePlan(3));

        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState
        {
            Mode         = KinematicsMode.CustomTrajectory,
            TrajectoryId = 5,
        });
        // No PersonalRouteRef — falls through to shared route path.

        _inspector.SelectedEntity = vehicle;

        // ── Act ───────────────────────────────────────────────────────────────
        var layer = CreateLayer();
        layer.Draw(AnyCtx);

        // ── Assert ────────────────────────────────────────────────────────────
        // 3 waypoints → 2 segments + 3 circles.
        Assert.Equal(2, layer.TestHook_LineDrawCount);
        Assert.Equal(3, layer.TestHook_CircleDrawCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // No routes
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the selected vehicle has no <see cref="PersonalRouteRef"/> and is not
    /// in <see cref="KinematicsMode.CustomTrajectory"/>, no route draw calls are made.
    /// </summary>
    [Fact]
    public void Draw_NoRouteNoTrajectory_ZeroRouteDrawCalls()
    {
        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState { Mode = KinematicsMode.None, TrajectoryId = 0 });

        _inspector.SelectedEntity = vehicle;

        var layer = CreateLayer();
        layer.Draw(AnyCtx);

        Assert.Equal(0, layer.TestHook_LineDrawCount);
        Assert.Equal(0, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// When no entity is selected (<see cref="IInspectorContext.SelectedEntity"/> is null),
    /// no draw calls are made.
    /// </summary>
    [Fact]
    public void Draw_NoSelection_ZeroDrawCalls()
    {
        _inspector.SelectedEntity = null;

        var layer = CreateLayer();
        layer.Draw(AnyCtx); // must not throw

        Assert.Equal(0, layer.TestHook_LineDrawCount);
        Assert.Equal(0, layer.TestHook_CircleDrawCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Cached query stability (CT-3, ROUTES1-BATCH-04)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calling <see cref="SimHostTrajectoryLayer.Draw"/> multiple consecutive times on
    /// a scene with a shared-route entity must produce identical draw counts each call,
    /// proving the cached <c>_routeQuery</c> field remains stable and functional across
    /// repeated frame renders (CT-3).
    /// </summary>
    [Fact]
    public void Draw_MultipleConsecutiveDraws_SharedRoute_StableDrawCounts()
    {
        // ── Arrange ───────────────────────────────────────────────────────────
        var routeEntity = _repo.CreateEntity();
        _repo.AddComponent(routeEntity, new RouteTrajectoryCache { TrajectoryId = 7, CompiledVersion = 1 });
        _repo.SetManagedComponent(routeEntity, MakePlan(3));

        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState
        {
            Mode         = KinematicsMode.CustomTrajectory,
            TrajectoryId = 7,
        });

        _inspector.SelectedEntity = vehicle;

        var layer = CreateLayer();

        // ── Act: draw three frames ────────────────────────────────────────────
        layer.Draw(AnyCtx);
        int lines1   = layer.TestHook_LineDrawCount;
        int circles1 = layer.TestHook_CircleDrawCount;

        layer.Draw(AnyCtx);
        int lines2   = layer.TestHook_LineDrawCount;
        int circles2 = layer.TestHook_CircleDrawCount;

        layer.Draw(AnyCtx);
        int lines3   = layer.TestHook_LineDrawCount;
        int circles3 = layer.TestHook_CircleDrawCount;

        // ── Assert ────────────────────────────────────────────────────────────
        // 3 waypoints → 2 lines + 3 circles every frame.
        Assert.Equal(2, lines1);
        Assert.Equal(3, circles1);
        Assert.Equal(lines1,   lines2);
        Assert.Equal(circles1, circles2);
        Assert.Equal(lines1,   lines3);
        Assert.Equal(circles1, circles3);
    }

    /// <summary>
    /// A personal-route scene drawn multiple times must yield identical draw counts
    /// each call — verifying the layer's cached query does not interfere with the
    /// personal-route path (CT-3 regression coverage).
    /// </summary>
    [Fact]
    public void Draw_MultipleConsecutiveDraws_PersonalRoute_StableDrawCounts()
    {
        var routeEntity = _repo.CreateEntity();
        _repo.SetManagedComponent(routeEntity, MakePlan(4));

        var vehicle = _repo.CreateEntity();
        _repo.AddComponent(vehicle, new NavState { Mode = KinematicsMode.None });
        _repo.AddComponent(vehicle, new PersonalRouteRef { RouteEntity = routeEntity });

        _inspector.SelectedEntity = vehicle;

        var layer = CreateLayer();

        layer.Draw(AnyCtx);
        int lines1 = layer.TestHook_LineDrawCount;

        layer.Draw(AnyCtx);
        int lines2 = layer.TestHook_LineDrawCount;

        // 4 waypoints → 3 segments each draw.
        Assert.Equal(3, lines1);
        Assert.Equal(lines1, lines2);
    }
}

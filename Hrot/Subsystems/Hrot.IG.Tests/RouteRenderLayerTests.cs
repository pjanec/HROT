using System.Numerics;
using Hrot.ScenarioEditor.Rendering;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D.Abstractions;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="RouteRenderLayer"/> — ROUTES1-T010.
///
/// Uses <see cref="RouteRenderLayer.TestHook_SkipRaylibCalls"/> so that Raylib is
/// never invoked. Counter fields (<see cref="RouteRenderLayer.TestHook_LineDrawCount"/>
/// and <see cref="RouteRenderLayer.TestHook_CircleDrawCount"/>) are used to assert
/// correct rendering without a GPU context.
/// </summary>
public class RouteRenderLayerTests
{
    // ── Constants (§CODE-STANDARDS §1) ────────────────────────────────────────

    /// <summary>RenderContext with the road_graphs layer (bit 4) switched on.</summary>
    private static readonly RenderContext LayerVisible =
        new RenderContext { VisibleLayersMask = 1u << RouteRenderLayer.RoadGraphsLayerBitIndex };

    /// <summary>RenderContext with all layers switched off.</summary>
    private static readonly RenderContext LayerHidden =
        new RenderContext { VisibleLayersMask = 0u };

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<TkbIdentity>();
        repo.RegisterManagedComponent<RoutePlan>();
        return repo;
    }

    /// <summary>
    /// Builds a <see cref="RouteRenderLayer"/> connected to <paramref name="repo"/>
    /// with <c>TestHook_SkipRaylibCalls = true</c>.
    /// </summary>
    private static RouteRenderLayer CreateLayer(
        EntityRepository repo,
        IInspectorContext? inspector = null)
    {
        var view  = (ISimulationView)repo;
        var query = repo.Query().With<TkbIdentity>().WithManaged<RoutePlan>().Build();
        return new RouteRenderLayer(view, query, inspector)
        {
            TestHook_SkipRaylibCalls = true,
        };
    }

    /// <summary>
    /// Creates a route entity with a <see cref="TkbIdentity"/> of
    /// <see cref="TkbEntityTypes.TacGraphic_Route"/> and the given <see cref="RoutePlan"/>.
    /// </summary>
    private static Entity CreateRouteEntity(EntityRepository repo, RoutePlan plan)
    {
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new TkbIdentity { TkbType = TkbEntityTypes.TacGraphic_Route });
        repo.SetManagedComponent(entity, plan);
        return entity;
    }

    private static RoutePlan MakePlan(int count, bool isLoop = false)
    {
        var plan = new RoutePlan { IsLoop = isLoop };
        plan.Mutate(wps =>
        {
            for (int i = 0; i < count; i++)
                wps.Add(new RouteWaypoint { Position = new Vector3(i * 10f, 0f, i * 10f) });
        });
        return plan;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Line / circle draw counts
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A non-looping route with 4 waypoints must produce exactly 3 line segments
    /// (n−1) and 4 vertex circles (n).
    /// </summary>
    [Fact]
    public void Draw_FourWaypointRoute_ThreeLinesAndFourCircles()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);
        CreateRouteEntity(repo, MakePlan(4));

        layer.Draw(LayerVisible);

        Assert.Equal(3, layer.TestHook_LineDrawCount);
        Assert.Equal(4, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// A looping route with 4 waypoints must produce 4 line segments (n, one extra closing
    /// the loop) and 4 vertex circles.
    /// </summary>
    [Fact]
    public void Draw_FourWaypointLoop_FourLinesAndFourCircles()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);
        CreateRouteEntity(repo, MakePlan(4, isLoop: true));

        layer.Draw(LayerVisible);

        Assert.Equal(4, layer.TestHook_LineDrawCount);
        Assert.Equal(4, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// When the road_graphs layer bit is hidden, <see cref="RouteRenderLayer.Draw"/>
    /// must not produce any draw calls.
    /// </summary>
    [Fact]
    public void Draw_LayerHidden_ZeroDrawCalls()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);
        CreateRouteEntity(repo, MakePlan(4));

        layer.Draw(LayerHidden);

        Assert.Equal(0, layer.TestHook_LineDrawCount);
        Assert.Equal(0, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// A route entity whose <see cref="RoutePlan"/> has an empty waypoint list
    /// must not produce any draw calls.
    /// </summary>
    [Fact]
    public void Draw_EmptyRoutePlan_ZeroDrawCalls()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);
        CreateRouteEntity(repo, new RoutePlan()); // no waypoints added

        layer.Draw(LayerVisible);

        Assert.Equal(0, layer.TestHook_LineDrawCount);
        Assert.Equal(0, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// An entity with a <see cref="TkbIdentity"/> type other than
    /// <see cref="TkbEntityTypes.TacGraphic_Route"/> must be silently skipped.
    /// </summary>
    [Fact]
    public void Draw_WrongTkbType_EntitySkipped_ZeroDrawCalls()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);

        // Use a different TKB type (Infantry unit).
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });
        var plan = MakePlan(3);
        repo.SetManagedComponent(entity, plan);

        layer.Draw(LayerVisible);

        Assert.Equal(0, layer.TestHook_LineDrawCount);
        Assert.Equal(0, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// When the route entity is selected in <see cref="IInspectorContext"/>,
    /// the draw-call counts must be identical to the unselected case
    /// (only the colour changes, not the geometry).
    /// </summary>
    [Fact]
    public void Draw_SelectedRoute_DrawCountsMatchUnselected()
    {
        var repo      = CreateRepo();
        var inspector = new InspectorState();
        var layer     = CreateLayer(repo, inspector);
        var entity    = CreateRouteEntity(repo, MakePlan(3));

        // Select the entity.
        inspector.SelectedEntity = entity;
        layer.Draw(LayerVisible);

        // 3 waypoints → 2 lines, 3 circles — regardless of selection colour.
        Assert.Equal(2, layer.TestHook_LineDrawCount);
        Assert.Equal(3, layer.TestHook_CircleDrawCount);
    }

    /// <summary>
    /// Two route entities in the same world must both be rendered; their draw
    /// counts accumulate.
    /// </summary>
    [Fact]
    public void Draw_TwoRouteEntities_CountsAreCumulative()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);
        CreateRouteEntity(repo, MakePlan(3)); // 2 lines, 3 circles
        CreateRouteEntity(repo, MakePlan(2)); // 1 line,  2 circles

        layer.Draw(LayerVisible);

        Assert.Equal(3, layer.TestHook_LineDrawCount);   // 2 + 1
        Assert.Equal(5, layer.TestHook_CircleDrawCount); // 3 + 2
    }

    /// <summary>
    /// A route with exactly one waypoint has no segments (n−1 = 0 lines) and a
    /// single vertex circle. Also exercises the null-safe <c>?.Count ?? 0</c>
    /// guard path with the minimum non-empty waypoint list (CT-1 regression
    /// coverage).
    /// </summary>
    [Fact]
    public void Draw_SingleWaypointRoute_ZeroLinesOneCircle()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);
        CreateRouteEntity(repo, MakePlan(1));

        layer.Draw(LayerVisible);

        Assert.Equal(0, layer.TestHook_LineDrawCount);
        Assert.Equal(1, layer.TestHook_CircleDrawCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PickEntity — OC1-CORRECTIVE-02
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// OC1-CORRECTIVE-02 SC1: clicking directly on a route segment (within pick radius)
    /// must return the route entity.
    /// </summary>
    [Fact]
    public void PickEntity_ClickOnSegment_ReturnsRouteEntity()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);

        // Two waypoints: (0,0,0) and (0,0,100) → canvas: (0,0)→(0,100).
        var plan  = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(0f, 0f, 0f) });
            wps.Add(new RouteWaypoint { Position = new Vector3(0f, 0f, 100f) });
        });
        var entity = CreateRouteEntity(repo, plan);

        // Click near the midpoint of the segment — (0, 50) is exactly on it.
        var result = layer.PickEntity(new Vector2(0f, 50f));

        Assert.Equal(entity, result);
    }

    /// <summary>
    /// OC1-CORRECTIVE-02 SC2: clicking far from any segment must return null.
    /// </summary>
    [Fact]
    public void PickEntity_ClickFarFromRoute_ReturnsNull()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);

        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(0f, 0f, 0f) });
            wps.Add(new RouteWaypoint { Position = new Vector3(0f, 0f, 100f) });
        });
        CreateRouteEntity(repo, plan);

        // Click well outside pick radius (100 units away in X).
        var result = layer.PickEntity(new Vector2(100f, 50f));

        Assert.Null(result);
    }

    /// <summary>
    /// OC1-CORRECTIVE-02 SC3: PickEntity on an empty world returns null.
    /// </summary>
    [Fact]
    public void PickEntity_NoRoutes_ReturnsNull()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);

        var result = layer.PickEntity(Vector2.Zero);

        Assert.Null(result);
    }

    /// <summary>
    /// OC1-CORRECTIVE-02 SC4: for a looping route, the closing segment (last→first)
    /// must also be pickable.
    /// </summary>
    [Fact]
    public void PickEntity_ClickOnLoopClosingSegment_ReturnsRouteEntity()
    {
        var repo  = CreateRepo();
        var layer = CreateLayer(repo);

        // Triangle loop: (0,0)→(100,0)→(50,100)→back to (0,0) in canvas space.
        // In ECS: pos.Z = canvas Y.
        var plan = new RoutePlan { IsLoop = true };
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(0f,   0f, 0f)   }); // canvas (0,0)
            wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 0f)   }); // canvas (100,0)
            wps.Add(new RouteWaypoint { Position = new Vector3(50f,  0f, 100f) }); // canvas (50,100)
        });
        var entity = CreateRouteEntity(repo, plan);

        // Midpoint of the closing segment (50,100)→(0,0): midpoint ~(25,50) in canvas.
        var result = layer.PickEntity(new Vector2(25f, 50f));

        Assert.Equal(entity, result);
    }
}

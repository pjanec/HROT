using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Tkb;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Tests for ROUTES1-T003: TKB Blueprint for TacGraphic_Route.
///
/// Verifies that <see cref="NedTkbCatalog.RegisterAll"/> correctly registers the
/// <c>TacGraphic_Route</c> blueprint and that applying it to an entity produces
/// the expected ECS component layout.
/// </summary>
public class TacGraphicRouteBlueprintTests
{
    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void TacGraphicRoute_IsRegisteredInTkbCatalog()
    {
        var db = BuildDatabase();
        Assert.True(db.TryGetByType(TkbEntityTypes.TacGraphic_Route, out _),
            "TacGraphic_Route (8802) must be registered in NedTkbCatalog.");
    }

    [Fact]
    public void TkbType_TacGraphicRoute_Is8802()
    {
        Assert.Equal(8802L, TkbEntityTypes.TacGraphic_Route);
    }

    // ── Template application ─────────────────────────────────────────────────

    [Fact]
    public void TacGraphicRoute_Blueprint_AddsRoutePlan()
    {
        using var world = CreateWorld();
        var template = BuildDatabase().GetByType(TkbEntityTypes.TacGraphic_Route);
        var entity   = world.CreateEntity();

        template.ApplyTo(world, entity);

        Assert.True(world.HasManagedComponent<RoutePlan>(entity),
            "TacGraphic_Route blueprint must attach a RoutePlan managed component.");
    }

    [Fact]
    public void TacGraphicRoute_Blueprint_AddsSimTransform()
    {
        using var world = CreateWorld();
        var template = BuildDatabase().GetByType(TkbEntityTypes.TacGraphic_Route);
        var entity   = world.CreateEntity();

        template.ApplyTo(world, entity);

        Assert.True(world.HasComponent<SimTransform>(entity),
            "TacGraphic_Route blueprint must attach a SimTransform component.");
    }

    [Fact]
    public void TacGraphicRoute_Blueprint_DoesNotAddEditablePolyline()
    {
        using var world = CreateWorld();
        var template = BuildDatabase().GetByType(TkbEntityTypes.TacGraphic_Route);
        var entity   = world.CreateEntity();

        template.ApplyTo(world, entity);

        Assert.False(world.HasManagedComponent<Hrot.IG.Components.EditablePolyline>(entity),
            "TacGraphic_Route blueprint must NOT attach EditablePolyline.");
    }

    [Fact]
    public void TacGraphicRoute_Blueprint_SpawnsRoutePlanWithEmptyWaypoints()
    {
        using var world = CreateWorld();
        var template = BuildDatabase().GetByType(TkbEntityTypes.TacGraphic_Route);
        var entity   = world.CreateEntity();

        template.ApplyTo(world, entity);

        var plan = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity);
        Assert.NotNull(plan.Waypoints);
        Assert.Empty(plan.Waypoints);
        Assert.False(plan.IsLoop);
        Assert.Equal(0, plan.Version);
    }

    [Fact]
    public void TacGraphicRoute_Blueprint_EachSpawnGetsIndependentRoutePlan()
    {
        // AddManagedComponent uses a factory so each entity gets its own instance.
        using var world = CreateWorld();
        var template = BuildDatabase().GetByType(TkbEntityTypes.TacGraphic_Route);
        var entity1  = world.CreateEntity();
        var entity2  = world.CreateEntity();

        template.ApplyTo(world, entity1);
        template.ApplyTo(world, entity2);

        var plan1 = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity1);
        var plan2 = ((ISimulationView)world).GetManagedComponentRO<RoutePlan>(entity2);

        // Different instances — mutations to one must not affect the other.
        plan1.Mutate(wps => wps.Add(new RouteWaypoint()));
        Assert.Equal(0, plan2.Version);
    }

    // ── road_graphs layer predicate ───────────────────────────────────────────

    [Fact]
    public void TkbType_TacGraphicRoute_MatchesRoadGraphsPredicate()
    {
        // The road_graphs map layer predicate filters entities where
        // TkbIdentity.TkbType == TkbEntityTypes.TacGraphic_Route (8802).
        // This test confirms the constant matches the expected value.
        Assert.Equal(8802L, TkbEntityTypes.TacGraphic_Route);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TkbDatabase BuildDatabase()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        RouteTkbExtensions.ApplyRoutePlanToBlueprint(db);
        return db;
    }

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        // Register all components that the route template references.
        world.RegisterComponent<SimTransform>();
        world.RegisterManagedComponent<RoutePlan>();
        // Additional components used by other blueprints in NedTkbCatalog
        // (ApplyTo silently skips unregistered component types, so we only
        // need to register what we actually assert on).
        world.RegisterManagedComponent<Hrot.IG.Components.EditablePolyline>();
        return world;
    }
}

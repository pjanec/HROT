using System.Linq;
using System.Numerics;
using Bagira.Map.Common;
using Bagira.Map.Common.Components;
using Bagira.Map.Common.Events;
using CarKinem.Commands;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Bagira.SimHost.Systems.Routing;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.SimHost.Tests;

/// <summary>
/// Unit tests for <see cref="PersonalRouteAuthoringSystem"/> — ROUTES1-T008.
/// </summary>
public class PersonalRouteAuthoringSystemTests
{
    private readonly EntityRepository _repo;
    private readonly PersonalRouteAuthoringSystem _system;

    public PersonalRouteAuthoringSystemTests()
    {
        _repo = CreateWorld();
        _system = new PersonalRouteAuthoringSystem();
        _system.Create(_repo);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<PersonalRouteRef>();
        repo.RegisterComponent<PartMetadata>();
        repo.RegisterComponent<TkbIdentity>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterManagedComponent<RoutePlan>();
        repo.RegisterEvent<CmdAppendPersonalWaypoint>();
        repo.RegisterEvent<CmdFollowTrajectory>();
        return repo;
    }

    private Entity CreateVehicle(Vector3 position)
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, new SimTransform { Position = position });
        return entity;
    }

    private void PublishWaypoint(Entity vehicle, Vector3 clickPos)
    {
        _repo.Bus.Publish(new CmdAppendPersonalWaypoint
        {
            VehicleEntity = vehicle,
            WorldPosition = clickPos,
        });
    }

    private void Tick()
    {
        _repo.Bus.SwapBuffers();
        _system.Run();
    }

    private Entity? FindChildRouteEntity(Entity vehicle)
    {
        if (!_repo.HasComponent<PersonalRouteRef>(vehicle))
            return null;
        return _repo.GetComponent<PersonalRouteRef>(vehicle).RouteEntity;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FirstWaypoint_NoExistingRoute_SpawnsChildEntityWithTwoWaypoints()
    {
        var vehiclePos  = new Vector3(10f, 0f, 20f);
        var clickedPos  = new Vector3(50f, 0f, 60f);
        var vehicle     = CreateVehicle(vehiclePos);

        PublishWaypoint(vehicle, clickedPos);
        Tick();

        // Vehicle must have PersonalRouteRef attached.
        Assert.True(_repo.HasComponent<PersonalRouteRef>(vehicle),
            "Vehicle must have PersonalRouteRef after first personal waypoint.");

        var childEntity = FindChildRouteEntity(vehicle)!.Value;
        Assert.True(_repo.IsAlive(childEntity), "Child route entity must be alive.");
        Assert.True(_repo.HasManagedComponent<RoutePlan>(childEntity),
            "Child route entity must have RoutePlan component.");

        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(childEntity);
        Assert.Equal(2, plan.Waypoints.Count);
        Assert.Equal(vehiclePos, plan.Waypoints[0].Position);
        Assert.Equal(clickedPos, plan.Waypoints[1].Position);
    }

    [Fact]
    public void FirstWaypoint_ChildEntity_HasCorrectPartMetadata()
    {
        var vehicle = CreateVehicle(Vector3.Zero);
        PublishWaypoint(vehicle, new Vector3(10f, 0f, 10f));
        Tick();

        var childEntity = FindChildRouteEntity(vehicle)!.Value;
        Assert.True(_repo.HasComponent<PartMetadata>(childEntity));
        var meta = _repo.GetComponent<PartMetadata>(childEntity);
        Assert.Equal(vehicle, meta.ParentEntity);
    }

    [Fact]
    public void FirstWaypoint_ChildEntity_HasTkbIdentityForRoute()
    {
        var vehicle = CreateVehicle(Vector3.Zero);
        PublishWaypoint(vehicle, new Vector3(10f, 0f, 10f));
        Tick();

        var childEntity = FindChildRouteEntity(vehicle)!.Value;
        Assert.True(_repo.HasComponent<TkbIdentity>(childEntity));
        var id = _repo.GetComponent<TkbIdentity>(childEntity);
        Assert.Equal(TkbEntityTypes.TacGraphic_Route, id.TkbType);
    }

    [Fact]
    public void SecondWaypoint_ExistingRoute_AppendsWaypointNoNewEntity()
    {
        var vehicle = CreateVehicle(new Vector3(0f, 0f, 0f));

        PublishWaypoint(vehicle, new Vector3(10f, 0f, 10f));
        Tick();

        var firstChildEntity = FindChildRouteEntity(vehicle)!.Value;

        PublishWaypoint(vehicle, new Vector3(20f, 0f, 20f));
        Tick();

        // Must still point to the same child entity.
        var secondChildEntity = FindChildRouteEntity(vehicle)!.Value;
        Assert.Equal(firstChildEntity, secondChildEntity);

        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(firstChildEntity);
        Assert.Equal(3, plan.Waypoints.Count);
        Assert.Equal(new Vector3(20f, 0f, 20f), plan.Waypoints[2].Position);
    }

    [Fact]
    public void VersionIncrements_OnEachMutation()
    {
        var vehicle = CreateVehicle(Vector3.Zero);

        PublishWaypoint(vehicle, new Vector3(5f, 0f, 5f));
        Tick();

        var childEntity = FindChildRouteEntity(vehicle)!.Value;
        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(childEntity);
        int versionAfterFirst = plan.Version;
        Assert.True(versionAfterFirst >= 1, "Version must be at least 1 after first Mutate.");

        PublishWaypoint(vehicle, new Vector3(10f, 0f, 10f));
        Tick();

        Assert.Equal(versionAfterFirst + 1, plan.Version);
    }

    [Fact]
    public void DeadVehicleEntity_IsIgnored_NoThrow()
    {
        var vehicle = CreateVehicle(Vector3.Zero);
        _repo.DestroyEntity(vehicle);

        // Publish to a dead entity.
        _repo.Bus.Publish(new CmdAppendPersonalWaypoint
        {
            VehicleEntity = vehicle,
            WorldPosition = new Vector3(10f, 0f, 10f),
        });

        var ex = Record.Exception(() => Tick());
        Assert.Null(ex);
    }

    [Fact]
    public void NullVehicleEntity_IsIgnored_NoThrow()
    {
        _repo.Bus.Publish(new CmdAppendPersonalWaypoint
        {
            VehicleEntity = Entity.Null,
            WorldPosition = new Vector3(10f, 0f, 10f),
        });

        var ex = Record.Exception(() => Tick());
        Assert.Null(ex);
    }

    [Fact]
    public void VehicleWithoutSimTransform_UsesZeroAsInitialPosition()
    {
        // Create vehicle with no SimTransform.
        var vehicle = _repo.CreateEntity();
        PublishWaypoint(vehicle, new Vector3(30f, 0f, 30f));
        Tick();

        var childEntity = FindChildRouteEntity(vehicle)!.Value;
        var plan = ((ISimulationView)_repo).GetManagedComponentRO<RoutePlan>(childEntity);
        Assert.Equal(Vector3.Zero, plan.Waypoints[0].Position);
    }
}

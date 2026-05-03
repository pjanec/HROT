using System;
using System.Numerics;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Systems.Routing;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Unit tests for <see cref="RouteTrajectorySyncSystem"/> â€” ROUTES1-T006.
/// </summary>
public class RouteTrajectorySyncSystemTests : IDisposable
{
    private readonly TrajectoryPoolManager _pool = new();
    private readonly EntityRepository _repo;
    private readonly RouteTrajectorySyncSystem _system;

    public RouteTrajectorySyncSystemTests()
    {
        _repo = CreateWorld();
        _system = new RouteTrajectorySyncSystem(_pool);
            }

    public void Dispose() => _pool.Dispose();

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static EntityRepository CreateWorld()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<RoutePlan>();
        repo.RegisterComponent<RouteTrajectoryCache>();
        repo.RegisterComponent<TkbIdentity>();
        repo.RegisterEvent<DestructionOrder>();
        return repo;
    }

    private static RoutePlan MakePlan(int waypointCount, bool isLoop = false)
    {
        var plan = new RoutePlan { IsLoop = isLoop };
        plan.Mutate(wps =>
        {
            for (int i = 0; i < waypointCount; i++)
                wps.Add(new RouteWaypoint { Position = new Vector3(i * 10f, 0f, i * 10f), TargetSpeed = 5f });
        });
        return plan;
    }

    private Entity CreateRouteEntity(RoutePlan plan)
    {
        var entity = _repo.CreateEntity();
        _repo.SetManagedComponent(entity, plan);
        return entity;
    }

    // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void FirstTick_RouteWith4Waypoints_RegistersTrajectoryAndPopulatesCache()
    {
        var plan = MakePlan(4);
        var entity = CreateRouteEntity(plan);

        _system.Execute(_repo, 0.016f);

        Assert.True(_repo.HasComponent<RouteTrajectoryCache>(entity));
        var cache = _repo.GetComponent<RouteTrajectoryCache>(entity);
        Assert.True(cache.TrajectoryId > 0, "TrajectoryId must be a positive integer after sync.");
        Assert.True(_pool.TryGetTrajectory(cache.TrajectoryId, out _),
            "Trajectory must be findable in the pool.");
    }

    [Fact]
    public void FirstTick_CompiledVersionMatchesRoutePlanVersion()
    {
        var plan = MakePlan(4);
        var entity = CreateRouteEntity(plan);

        _system.Execute(_repo, 0.016f);

        var cache = _repo.GetComponent<RouteTrajectoryCache>(entity);
        Assert.Equal(plan.Version, cache.CompiledVersion);
    }

    [Fact]
    public void SecondTick_NoVersionChange_DoesNotReRegisterTrajectory()
    {
        var plan = MakePlan(3);
        var entity = CreateRouteEntity(plan);

        _system.Execute(_repo, 0.016f);
        var firstId = _repo.GetComponent<RouteTrajectoryCache>(entity).TrajectoryId;

        _system.Execute(_repo, 0.016f);
        var secondId = _repo.GetComponent<RouteTrajectoryCache>(entity).TrajectoryId;

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void AfterMutate_NewVersionCausesOldTrajectoryRemovedAndNewRegistered()
    {
        var plan = MakePlan(3);
        var entity = CreateRouteEntity(plan);

        _system.Execute(_repo, 0.016f);
        var firstId = _repo.GetComponent<RouteTrajectoryCache>(entity).TrajectoryId;

        // Mutate: adds a waypoint and bumps version.
        plan.Mutate(wps => wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 100f), TargetSpeed = 5f }));

        _system.Execute(_repo, 0.016f);
        var newCache = _repo.GetComponent<RouteTrajectoryCache>(entity);

        Assert.NotEqual(firstId, newCache.TrajectoryId);
        Assert.True(newCache.TrajectoryId > 0);
        Assert.False(_pool.TryGetTrajectory(firstId, out _),
            "Old trajectory must have been removed from pool.");
        Assert.True(_pool.TryGetTrajectory(newCache.TrajectoryId, out _),
            "New trajectory must be present in pool.");
        Assert.Equal(plan.Version, newCache.CompiledVersion);
    }

    [Fact]
    public void DestroyRouteEntity_TrajectoryRemovedFromPool()
    {
        var plan = MakePlan(2);
        var entity = CreateRouteEntity(plan);

        _system.Execute(_repo, 0.016f);
        var cachedId = _repo.GetComponent<RouteTrajectoryCache>(entity).TrajectoryId;
        Assert.True(_pool.TryGetTrajectory(cachedId, out _));

        // Simulate ELM: publish DestructionOrder and swap so the system can read it.
        _repo.Bus.Publish(new DestructionOrder { Entity = entity });
        _repo.Bus.SwapBuffers();
        _repo.DestroyEntity(entity);

        // Second tick: system reads DestructionOrder event and frees the pool entry.
        _system.Execute(_repo, 0.016f);

        Assert.False(_pool.TryGetTrajectory(cachedId, out _),
            "Pool entry must be freed when route entity is destroyed.");
    }

    [Fact]
    public void RouteWith0Waypoints_DoesNotThrow_TrajectoryIdIsZero()
    {
        var plan = new RoutePlan { IsLoop = false };
        var entity = CreateRouteEntity(plan);

        var ex = Record.Exception(() => _system.Execute(_repo, 0.016f));
        Assert.Null(ex);

        var cache = _repo.GetComponent<RouteTrajectoryCache>(entity);
        Assert.Equal(0, cache.TrajectoryId);
    }

    [Fact]
    public void RouteWith1Waypoint_DoesNotThrow_TrajectoryIdIsZero()
    {
        var plan = new RoutePlan { IsLoop = false };
        plan.Mutate(wps => wps.Add(new RouteWaypoint { Position = Vector3.Zero, TargetSpeed = 5f }));
        var entity = CreateRouteEntity(plan);

        var ex = Record.Exception(() => _system.Execute(_repo, 0.016f));
        Assert.Null(ex);

        var cache = _repo.GetComponent<RouteTrajectoryCache>(entity);
        Assert.Equal(0, cache.TrajectoryId);
    }
}

using Hrot.ScenarioEditor.Gizmos;
using Hrot.Map.Common.Components;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="IgApplication"/> (headless).
/// ⛔ R-3067 / AQ109 X3 — the context-action routing rail (IG_DeleteEntity) went with ExCon's pushed context menu.
/// </summary>
public class IgApplicationTests : System.IDisposable
{
    private readonly IgApplication _app;

    public IgApplicationTests()
    {
        _app = new IgApplication();
        // Headless mode: no Raylib window; DDS uses domain 230 to avoid collisions.
        _app.InitializeEmbedded(headless: true, domainIdOverride: 230);
    }

    public void Dispose() => _app.Dispose();


    // Route edit commit safety guard (CT-1, ROUTES1-BATCH-04)

    /// <summary>
    /// When a route entity is destroyed between the start of editing and the
    /// commit, the gizmo commit handler must silently discard the update
    /// (the <c>World.IsAlive</c> guard added in CT-1) rather than crashing.
    ///
    /// Steps: create route entity -> activate RouteWaypointGizmo -> destroy entity
    /// -> trigger OnCommit -> assert no exception.
    /// </summary>
    [Fact]
    public void CommitHandler_EntityDestroyedBeforeCommit_DropsUpdateSilently()
    {
        // -- Arrange --
        const long networkId = 9001L;

        // Register a route entity with a minimal RoutePlan and SimTransform.
        var routeEntity = _app.World.CreateEntity();
        _app.World.RegisterManagedComponent<RoutePlan>(); // idempotent if already registered
        var plan = new RoutePlan();
        plan = plan.WithWaypoints(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(0f,  0f, 0f),   TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 100f), TargetSpeed = 5f });
        });
        _app.World.SetManagedComponent(routeEntity, plan);
        _app.World.AddComponent(routeEntity, new NetworkIdentity { Value = networkId });
        _app.World.AddComponent(routeEntity, default(SimTransform));
        _app.TestHook_EntityMap.Register(networkId, routeEntity);

        // Directly instantiate a RouteWaypointGizmo for the entity.
        var gizmo = new RouteWaypointGizmo(_app.World, routeEntity, networkId, onRemove: () => { });

        // -- Act: destroy the entity BEFORE committing --
        _app.World.DestroyEntity(routeEntity);

        // Trigger OnCommit -- the WriteBackAndPublish guard must detect the dead
        // entity via World.IsAlive and return early without throwing.
        var ex = Record.Exception(() => gizmo.OnCommit(Vector3.Zero));

        // -- Assert --
        Assert.Null(ex);

        gizmo.Dispose();
    }
}
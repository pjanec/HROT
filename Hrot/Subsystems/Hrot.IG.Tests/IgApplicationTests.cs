using Hrot.ScenarioEditor.Tools;
using Hrot.Map.Common.Components;
using Fdp.Kernel;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Raylib_cs;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for <see cref="IgApplication"/> context-action routing (BUG2-E002).
/// Uses <see cref="IgApplication.TestHook_ExecuteLocalContextAction"/> to invoke the
/// private action handler without a Raylib window.
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

    /// <summary>
    /// When <c>IG_DeleteEntity</c> is executed for an entity with <see cref="NetworkIdentity"/>,
    /// a <see cref="DestroyEntityCommand"/> must be published to the event bus with the
    /// correct <c>NetworkId</c>.
    /// </summary>
    [Fact]
    public void ExecuteLocalContextAction_IgDeleteEntity_PublishesDestroyCommand()
    {
        // Arrange: entity with NetworkIdentity registered in the entity map.
        const long networkId = 77L;
        var entity = _app.World.CreateEntity();
        _app.World.AddComponent(entity, new NetworkIdentity { Value = networkId });
        _app.TestHook_EntityMap.Register(networkId, entity);

        // Act: invoke the IG_DeleteEntity action.
        _app.TestHook_ExecuteLocalContextAction(entity, "IG_DeleteEntity");

        // Assert: DestroyEntityCommand published with the correct NetworkId.
        _app.World.Bus.SwapBuffers();
        DestroyEntityCommand? captured = null;
        foreach (var cmd in ((ISimulationView)_app.World).ConsumeManagedEvents<DestroyEntityCommand>())
            captured = cmd;

        Assert.NotNull(captured);
        Assert.Equal(networkId, captured!.NetworkId);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // Route edit commit safety guard (CT-1, ROUTES1-BATCH-04)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// When a route entity is destroyed between the start of editing and the
    /// right-click commit, the commit handler must silently discard the update
    /// (the <c>World.IsAlive</c> guard added in CT-1) rather than crashing.
    ///
    /// Steps: create route entity â†’ activate RouteEditTool â†’ destroy entity
    /// â†’ trigger right-click commit â†’ assert no exception.
    /// </summary>
    [Fact]
    public void CommitHandler_EntityDestroyedBeforeCommit_DropsUpdateSilently()
    {
        // â”€â”€ Arrange â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        const long networkId = 9001L;

        // Register a route entity with a minimal RoutePlan.
        var routeEntity = _app.World.CreateEntity();
        _app.World.RegisterManagedComponent<RoutePlan>(); // idempotent if already registered
        var plan = new RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new RouteWaypoint { Position = new Vector3(0f,  0f, 0f),   TargetSpeed = 5f });
            wps.Add(new RouteWaypoint { Position = new Vector3(100f, 0f, 100f), TargetSpeed = 5f });
        });
        _app.World.SetManagedComponent(routeEntity, plan);
        _app.TestHook_EntityMap.Register(networkId, routeEntity);

        // Activate the route edit tool for that entity.
        _app.TestHook_ActivateRouteEditToolForNetworkId(networkId);
        Assert.NotNull(_app.TestHook_ActiveRouteEditTool);

        // â”€â”€ Act: destroy the entity BEFORE committing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _app.World.DestroyEntity(routeEntity);

        // Trigger right-click commit â€” the onCommit lambda must detect the dead
        // entity via World.IsAlive and return early without throwing.
        var ex = Record.Exception(() =>
            _app.TestHook_ActiveRouteEditTool!.HandleClick(Vector2.Zero, MouseButton.Right));

        // â”€â”€ Assert â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Assert.Null(ex);
    }
}

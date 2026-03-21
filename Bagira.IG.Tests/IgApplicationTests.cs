using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.IG.Tests;

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
}

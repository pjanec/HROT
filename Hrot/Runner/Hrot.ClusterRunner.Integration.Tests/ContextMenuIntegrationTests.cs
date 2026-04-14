using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

public class ContextMenuIntegrationTests
{
    private const int MenuTimeoutFrames = 100;

    [Fact]
    public void ContextMenu_SelectionEvent_PushesMenuToIG()
    {
        using var harness = new HrotRunnerHarness();

        var igWorld = harness.Ig.App.World;
        var menuEntity = igWorld.CreateEntity();
        const int networkId = 4242;

        igWorld.AddComponent(menuEntity, new NetworkIdentity(networkId));
        igWorld.SetManagedComponent(menuEntity, new ContextMenuState { IsOpen = false });

        using var participant = new DdsParticipant((uint)harness.DomainId);
        using var selectionWriter = new DdsWriter<SelectionChangedEvent>(participant, "SelectionChangedEvent");

        selectionWriter.Write(new SelectionChangedEvent
        {
            MapId = 0,
            SelectedEntityIds = new List<int> { networkId }
        });

        bool menuUpdated = harness.PumpUntil(
            () => HasPropertiesAction(igWorld, menuEntity),
            MenuTimeoutFrames);

        Assert.True(menuUpdated, "Context menu actions did not reach IG in time.");
    }

    private static bool HasPropertiesAction(EntityRepository world, Entity entity)
    {
        var view = (ISimulationView)world;
        if (!view.HasManagedComponent<ContextMenuState>(entity))
            return false;

        var state = view.GetManagedComponentRO<ContextMenuState>(entity);
        if (state.Actions == null || state.Actions.Count == 0)
            return false;

        foreach (var action in state.Actions)
        {
            if (action.Label == "Properties...")
                return true;
        }

        return false;
    }
}

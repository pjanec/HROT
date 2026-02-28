using System.Collections.Generic;
using Bagira.BDC.SSTM;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

public class ContextMenuIntegrationTests
{
    private const int MenuTimeoutFrames = 100;

    [Fact]
    public void ContextMenu_SelectionEvent_PushesMenuToIG()
    {
        using var harness = new BagiraRunnerHarness();

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

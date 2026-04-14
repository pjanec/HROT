using System;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Defaults;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Events;
using Hrot.ScenarioEditor.Tools;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

public class WorldResetTests
{
    [Fact]
    public void FlushForWorldReset_ClearsSelection()
    {
        // Arrange: create a world with one entity having SelectionState
        var world = new EntityRepository();
        world.RegisterComponent<SelectionState>();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        var selection = new DefaultSelectionState();
        // Construct StandardInteractionTool in stub mode (no real canvas)
        var tool = new StandardInteractionTool(world, null!, null!, selection);
        tool.TestHook_SelectEntity(entity, augment: false);
        Assert.NotNull(selection.PrimarySelected);

        // Act
        tool.FlushForWorldReset();

        // Assert
        Assert.Null(selection.PrimarySelected);
        Assert.Empty(selection.SelectedEntities);
    }

    [Fact]
    public void WorldResetEvent_IsPlainClass()
    {
        // Ensure WorldResetEvent can be instantiated and is a reference type
        var evt = new WorldResetEvent();
        Assert.NotNull(evt);
    }
}

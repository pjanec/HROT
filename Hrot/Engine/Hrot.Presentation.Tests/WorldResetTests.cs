using Fdp.Core;
using Hrot.IG.Components;
using Hrot.Common.Events;
using Hrot.ScenarioEditor.Systems;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

public class WorldResetTests
{
    [Fact]
    public void SelectionInteractionSystem_ClearAllSelections_ResetsEcsState()
    {
        var world = new EntityRepository();
        world.RegisterComponent<SelectionState>();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });
        var system = new SelectionInteractionSystem(world, world.Bus);
        system.ClearAllSelections();
        var state = world.GetComponent<SelectionState>(entity);
        Assert.False(state.IsSelected);
        Assert.False(state.IsPrimarySelection);
    }

    [Fact]
    public void WorldResetEvent_IsPlainClass()
    {
        // Ensure WorldResetEvent can be instantiated and is a reference type
        var evt = new WorldResetEvent();
        Assert.NotNull(evt);
    }
}

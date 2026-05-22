using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fbt;
using Hrot.Blueprints.Tests.MockSystems;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Tests for TASK-TH-010: BehaviorRegistry wiring and MockDispatcherSystem infrastructure.
/// SC1: fixture.BehaviorRegistry != null.
/// SC3: MockLocomotionDispatcher counts invocations when entity has ActiveAction != 0.
/// SC4: NextStatus lambda controls the Status written back to the channel.
/// </summary>
[Collection("DebugProbe")]
public sealed class MockDispatcherSystemTests
{
    // SC1: fixture exposes BehaviorRegistry (non-null after construction).
    // Note: HsmActionDispatcher is a static class and has no instance property; the
    // ClearAll() call in Dispose() is verified implicitly by Dispose_WithNoAlcsLoaded tests.
    [Fact]
    public void Fixture_HasBehaviorRegistry()
    {
        using var fixture = new BlueprintTestFixture();
        Assert.NotNull(fixture.BehaviorRegistry);
    }

    // SC3: Add MockLocomotionDispatcher, create entity with ActiveAction=1, TickFrame.
    // Assert InvokeCount == 1.
    [Fact]
    public void MockLocomotionDispatcher_WhenEntityHasActiveAction_IncreasesInvokeCount()
    {
        using var fixture = new BlueprintTestFixture();
        fixture.World.RegisterComponent<LocomotionChannel>();

        var dispatcher = new MockLocomotionDispatcher();
        fixture.AddSimulationSystem(dispatcher);

        var entity = fixture.World.CreateEntity();
        fixture.World.AddComponent(entity, new LocomotionChannel { ActiveAction = 1 });

        fixture.TickFrame(0.016f);

        Assert.Equal(1, dispatcher.InvokeCount);
    }

    // SC4: NextStatus = _ => NodeStatus.Running.
    // After TickFrame, entity's LocomotionChannel.Status == NodeStatus.Running.
    [Fact]
    public void MockLocomotionDispatcher_NextStatusLambda_WritesStatusToChannel()
    {
        using var fixture = new BlueprintTestFixture();
        fixture.World.RegisterComponent<LocomotionChannel>();

        var dispatcher = new MockLocomotionDispatcher { NextStatus = _ => NodeStatus.Running };
        fixture.AddSimulationSystem(dispatcher);

        var entity = fixture.World.CreateEntity();
        fixture.World.AddComponent(entity, new LocomotionChannel { ActiveAction = 1 });

        fixture.TickFrame(0.016f);

        Assert.Equal(NodeStatus.Running, fixture.World.GetComponentRO<LocomotionChannel>(entity).Status);
    }
}

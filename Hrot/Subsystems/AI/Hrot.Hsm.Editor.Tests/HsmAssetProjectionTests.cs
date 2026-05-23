using System;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public class HsmAssetProjectionTests
{
    // ---- helpers ----

    private static (HsmDefinitionBlob blob, MachineMetadata metadata) Compile(HsmBuilder builder)
    {
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, metadata);
    }

    private static HsmAsset Project(HsmDefinitionBlob blob, MachineMetadata metadata,
        string name = "TestMachine")
    {
        return HsmAssetProjector.Project(
            blob, metadata, null,
            Guid.NewGuid(), name, "", false, "");
    }

    // ---- tests ----

    [Fact]
    public void Project_empty_machine_returns_asset_with_zero_states()
    {
        // Arrange: empty machine - no user states, no events
        var builder = new HsmBuilder("EmptyMachine");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata, "EmptyMachine");

        // Assert: only the implicit compiler root state is present; no user content
        asset.AllStates.Should().HaveCount(1);
        asset.AllTransitions.Should().BeEmpty();
        asset.AllGlobalTransitions.Should().BeEmpty();
        asset.AllEvents.Should().BeEmpty();
        asset.RootState.Should().NotBeNull();
        asset.Name.Should().Be("EmptyMachine");
    }

    [Fact]
    public void Project_single_state_machine_has_one_state_node()
    {
        // Arrange: one user state, no transitions
        var builder = new HsmBuilder("SingleState");
        builder.State("OnlyState").Initial();
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert: compiler root + one user state
        asset.AllStates.Should().HaveCount(2);
        asset.AllStates.Should().Contain(s => s.Name == "OnlyState");
    }

    [Fact]
    public void Project_state_names_resolved_from_metadata()
    {
        // Arrange: declare target state first so GoTo resolves correctly
        var builder = new HsmBuilder("NameMachine");
        builder.Event("OnTimeout", 1);
        builder.State("Active").Final();
        builder.State("Idle").Initial().On("OnTimeout").GoTo("Active");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert: both user state names appear in the projected asset
        asset.AllStates.Should().Contain(s => s.Name == "Idle");
        asset.AllStates.Should().Contain(s => s.Name == "Active");
    }

    [Fact]
    public void Project_parent_child_hierarchy_is_correct()
    {
        // Arrange: composite state with two children
        var builder = new HsmBuilder("HierarchyMachine");
        builder.State("Parent").Initial()
            .Child("Child1", c => c.Initial())
            .Child("Child2", c => { });
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert
        var parentNode = asset.AllStates.First(s => s.Name == "Parent");
        var child1 = asset.AllStates.First(s => s.Name == "Child1");
        var child2 = asset.AllStates.First(s => s.Name == "Child2");

        parentNode.Children.Should().Contain(child1);
        parentNode.Children.Should().Contain(child2);
        child1.Parent.Should().Be(parentNode);
        child2.Parent.Should().Be(parentNode);
    }

    [Fact]
    public void Project_initial_flag_is_propagated()
    {
        // Arrange: declare target state first so GoTo resolves correctly
        var builder = new HsmBuilder("InitialMachine");
        builder.Event("OnGo", 1);
        builder.State("Active");
        builder.State("Idle").Initial().On("OnGo").GoTo("Active");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert
        var idle = asset.AllStates.First(s => s.Name == "Idle");
        idle.IsInitial.Should().BeTrue();

        var active = asset.AllStates.First(s => s.Name == "Active");
        active.IsInitial.Should().BeFalse();
    }

    [Fact]
    public void Project_final_flag_is_propagated()
    {
        // Arrange: declare target state first so GoTo resolves correctly
        var builder = new HsmBuilder("FinalMachine");
        builder.Event("OnDone", 1);
        builder.State("Done").Final();
        builder.State("Running").Initial().On("OnDone").GoTo("Done");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert
        var done = asset.AllStates.First(s => s.Name == "Done");
        done.IsFinal.Should().BeTrue();

        var running = asset.AllStates.First(s => s.Name == "Running");
        running.IsFinal.Should().BeFalse();
    }

    [Fact]
    public void Project_transitions_are_projected()
    {
        // Arrange: declare target state first so GoTo resolves correctly
        var builder = new HsmBuilder("TransMachine");
        builder.Event("OnTimeout", 1);
        builder.State("Active");
        builder.State("Idle").Initial().On("OnTimeout").GoTo("Active");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert
        asset.AllTransitions.Should().HaveCount(1);

        var t = asset.AllTransitions[0];
        t.Source.Name.Should().Be("Idle");
        t.Target.Name.Should().Be("Active");
        t.EventId.Should().Be(1);
    }

    [Fact]
    public void Project_transition_event_name_resolved_from_metadata()
    {
        // Arrange: declare target state first so GoTo resolves correctly
        var builder = new HsmBuilder("EventNameMachine");
        builder.Event("OnTimeout", 1);
        builder.State("Active");
        builder.State("Idle").Initial().On("OnTimeout").GoTo("Active");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert
        var t = asset.AllTransitions[0];
        t.EventName.Should().Be("OnTimeout");
    }

    [Fact]
    public void Project_global_transitions_are_projected()
    {
        // Arrange
        var builder = new HsmBuilder("GlobalMachine");
        builder.Event("OnDeath", 99);
        builder.State("Alive").Initial();
        builder.State("Dead").Final();
        builder.GlobalTransition("OnDeath", "Dead");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert
        asset.AllGlobalTransitions.Should().HaveCount(1);

        var gt = asset.AllGlobalTransitions[0];
        gt.EventId.Should().Be(99);
        gt.EventName.Should().Be("OnDeath");
        gt.Target.Name.Should().Be("Dead");
    }

    [Fact]
    public void Project_events_are_populated_from_metadata()
    {
        // Arrange: two events registered
        var builder = new HsmBuilder("EventsMachine");
        builder.Event("Trigger1", 1);
        builder.Event("Trigger2", 2);
        builder.State("A").Initial();
        builder.State("B");
        var (blob, metadata) = Compile(builder);

        // Act
        var asset = Project(blob, metadata);

        // Assert: both events appear in AllEvents, sorted by ID
        asset.AllEvents.Should().HaveCount(2);
        asset.AllEvents[0].EventId.Should().Be(1);
        asset.AllEvents[0].Name.Should().Be("Trigger1");
        asset.AllEvents[1].EventId.Should().Be(2);
        asset.AllEvents[1].Name.Should().Be("Trigger2");
    }
}

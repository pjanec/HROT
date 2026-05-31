using System;
using System.Linq;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Layout;
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

    // ---- BPF-017: ActionNames keyed by hash ID ---------------------------

    [Fact]
    public void BuildMachineMetadata_ActionNames_KeyedByHashId_MatchingBlobActionId()
    {
        // Arrange: one state with a known entry action
        var builder = new HsmBuilder("ActionMachine");
        builder.State("Idle").Initial().OnEntry("AttackAction");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);

        // Find the state in the blob that has a non-empty OnEntryActionId
        ushort actionHashId = 0xFFFF;
        foreach (var s in blob.States)
        {
            if (s.OnEntryActionId != 0xFFFF) { actionHashId = s.OnEntryActionId; break; }
        }
        actionHashId.Should().NotBe((ushort)0xFFFF, "at least one state must have an entry action");

        // Assert: ActionNames must map that hash ID to the original action name.
        metadata.ActionNames.Should().ContainKey(actionHashId);
        metadata.ActionNames[actionHashId].Should().Be("AttackAction");
    }

    [Fact]
    public void BuildMachineMetadata_ActionNames_MultipleActions_AllKeyedByHashId()
    {
        var builder = new HsmBuilder("MultiActionMachine");
        builder.State("Idle").Initial().OnEntry("OnEnterIdle").OnExit("OnExitIdle");
        builder.State("Active").OnEntry("OnEnterActive");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);

        // Every action name must be resolvable via GetActionName using the blob's stored ID.
        foreach (var state in blob.States.ToArray())
        {
            if (state.OnEntryActionId != 0xFFFF)
                metadata.GetActionName(state.OnEntryActionId)
                    .Should().NotStartWith("Action_",
                    "GetActionName should return the real name, not Action_<id>");
            if (state.OnExitActionId != 0xFFFF)
                metadata.GetActionName(state.OnExitActionId)
                    .Should().NotStartWith("Action_");
        }
    }

    // ---- BPF-025: StableIds assigned from metadata, not positional -------

    [Fact]
    public void Project_state_StableIds_come_from_metadata_not_layout_position()
    {
        var idleId = new Guid("a0000000-0000-0000-0000-000000000001");
        var busyId = new Guid("a0000000-0000-0000-0000-000000000002");
        var doneId = new Guid("a0000000-0000-0000-0000-000000000003");

        var builder = new HsmBuilder("StableMachine");
        builder.State("Idle", idleId).Initial();
        builder.State("Busy", busyId);
        builder.State("Done", doneId).Final();
        var (blob, metadata) = Compile(builder);
        var asset = Project(blob, metadata);

        asset.AllStates.First(s => s.Name == "Idle").StableId.Should().Be(idleId);
        asset.AllStates.First(s => s.Name == "Busy").StableId.Should().Be(busyId);
        asset.AllStates.First(s => s.Name == "Done").StableId.Should().Be(doneId);
    }

    [Fact]
    public void Project_layout_applied_by_StableId_not_flat_position()
    {
        var idleId = new Guid("b0000000-0000-0000-0000-000000000001");
        var busyId = new Guid("b0000000-0000-0000-0000-000000000002");

        var builder = new HsmBuilder("LayoutMachine");
        builder.State("Idle", idleId).Initial();
        builder.State("Busy", busyId);
        var (blob, metadata) = Compile(builder);

        var layout = new HsmEditorLayoutBuilder()
            .State(idleId.ToString("D"), new Vector2(100f, 200f))
            .State(busyId.ToString("D"), new Vector2(300f, 400f))
            .Build();

        var asset = HsmAssetProjector.Project(blob, metadata, layout,
            Guid.NewGuid(), "LayoutMachine", "", false, "");

        asset.AllStates.First(s => s.Name == "Idle").Position.Should().Be(new Vector2(100f, 200f));
        asset.AllStates.First(s => s.Name == "Busy").Position.Should().Be(new Vector2(300f, 400f));
    }
}

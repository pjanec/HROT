using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>HSM-009</c> — an author can DECLARE an event.</b>
///
/// <para>📐 Before this, <c>EventDefinition</c> was constructed in exactly two places, both loaders
/// (<c>HsmAssetProjector</c> from a compiled blob, <c>HsmAssetMapper</c> from JSON) plus <c>CE-2088</c>'s
/// <c>EnsureEvent</c> for an engine-raised event the author PICKS. ⇒ a machine with no events could never acquire
/// one, so a transition's <c>[HsmEventPicker]</c> had nothing to offer and authoring a triggered transition meant
/// hand-editing the <c>.hsm.json</c>.</para>
///
/// <para>⚠ These rails drive the MODEL operations the window's buttons call, not ImGui.</para>
/// </summary>
public class HsmEventAuthoringTests
{
    private static HsmAsset MakeAsset(out TransitionNode transition)
    {
        var root = new StateNode("__root__");
        var idle = new StateNode("Idle") { IsInitial = true, Parent = root };
        var busy = new StateNode("Busy") { Parent = root };
        root.Children.Add(idle);
        root.Children.Add(busy);

        transition = new TransitionNode { VisualId = Guid.NewGuid(), Source = idle, Target = busy };

        return new HsmAsset(
            Guid.NewGuid(), "TestAsset", "", true, "",
            new HsmDefinitionBlob(),
            new MachineMetadata(),
            root,
            new List<StateNode> { idle, busy },
            new List<TransitionNode> { transition },
            new List<GlobalTransitionNode>(),
            new List<RegionNode>(),
            new List<EventDefinition>());
    }

    // ---- create ----

    [Fact]
    public void An_empty_machine_can_acquire_its_first_event()
    {
        var asset = MakeAsset(out _);
        asset.AllEvents.Should().BeEmpty();

        var created = asset.CreateEvent("OnSight");

        created.Should().NotBeNull();
        asset.AllEvents.Should().ContainSingle(e => e.Name == "OnSight");
        asset.FindEventById(created!.EventId).Should().BeSameAs(created);
    }

    [Fact]
    public void A_created_event_never_takes_id_zero_because_zero_means_no_event()
    {
        var asset = MakeAsset(out _);

        asset.CreateEvent("OnSight")!.EventId.Should().Be(1);
        asset.CreateEvent("OnLostSight")!.EventId.Should().Be(2);
    }

    [Fact]
    public void Ids_fill_the_lowest_free_slot_after_a_delete()
    {
        var asset = MakeAsset(out _);
        var first  = asset.CreateEvent("A")!;
        var second = asset.CreateEvent("B")!;

        asset.RemoveEvent(first.EventId).Should().BeTrue();
        var third = asset.CreateEvent("C")!;

        third.EventId.Should().Be(first.EventId, "the freed id is the lowest one available");
        second.EventId.Should().Be(2);
    }

    [Fact]
    public void A_duplicate_name_is_refused_case_insensitively()
    {
        var asset = MakeAsset(out _);
        asset.CreateEvent("OnSight").Should().NotBeNull();

        asset.CreateEvent("onsight").Should().BeNull();
        asset.AllEvents.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string? name)
    {
        var asset = MakeAsset(out _);

        asset.CreateEvent(name).Should().BeNull();
        asset.AllEvents.Should().BeEmpty();
    }

    [Fact]
    public void An_engine_raised_name_takes_its_RESERVED_id_not_a_sequential_one()
    {
        // ⭐ HsmEventIds is the one assignment and rebinds this name at emit; allocating a sequential id here
        //   would make the editor and the emitter disagree about the same event.
        Hrot.AiEditor.Persistence.Emit.HsmEventIds
            .TryGetBuiltIn("Sensor.FirstThreat", out ushort reserved).Should().BeTrue();

        var asset = MakeAsset(out _);

        var created = asset.CreateEvent("Sensor.FirstThreat");

        created!.EventId.Should().Be(reserved);
    }

    // ---- delete ----

    [Fact]
    public void Deleting_an_event_removes_it_from_the_list_and_the_id_index()
    {
        var asset = MakeAsset(out _);
        var ev = asset.CreateEvent("OnSight")!;

        asset.RemoveEvent(ev.EventId).Should().BeTrue();

        asset.AllEvents.Should().BeEmpty();
        asset.FindEventById(ev.EventId).Should().BeNull();
    }

    [Fact]
    public void Deleting_an_unknown_event_is_a_no_op()
    {
        var asset = MakeAsset(out _);

        asset.RemoveEvent(123).Should().BeFalse();
    }

    [Fact]
    public void A_transition_that_referenced_a_deleted_event_is_LEFT_ALONE_and_reported()
    {
        // ⛔ Rewriting EventId to 0 would silently turn a triggered transition into a completion transition.
        //    The dangling reference is a DIAGNOSTIC, not a silent repair.
        var asset = MakeAsset(out var transition);
        var ev = asset.CreateEvent("OnSight")!;
        transition.EventId = ev.EventId;

        asset.RemoveEvent(ev.EventId);

        transition.EventId.Should().Be(ev.EventId, "the transition keeps naming it");
        new Hrot.Hsm.Editor.Validation.HsmValidator().Validate(asset)
            .Should().Contain(d => d.Code == Hrot.Hsm.Editor.Validation.HsmDiagnosticCode.EventReferenceDangling);
    }

    // ---- rename ----

    [Fact]
    public void Renaming_an_event_moves_the_declaration_and_every_reference_follows_by_id()
    {
        var asset = MakeAsset(out var transition);
        var ev = asset.CreateEvent("OnSight")!;
        transition.EventId = ev.EventId;

        asset.RenameEvent(ev.EventId, "OnContact").Should().BeTrue();

        asset.FindEventByName("OnContact").Should().NotBeNull();
        asset.FindEventByName("OnSight").Should().BeNull();
        transition.EventId.Should().Be(ev.EventId, "references are by id, so nothing had to be rewritten");
    }

    [Fact]
    public void Renaming_to_a_taken_name_is_refused()
    {
        var asset = MakeAsset(out _);
        var a = asset.CreateEvent("A")!;
        asset.CreateEvent("B");

        asset.RenameEvent(a.EventId, "B").Should().BeFalse();
        a.Name.Should().Be("A");
    }

    [Fact]
    public void Renaming_to_an_engine_raised_name_is_refused()
    {
        // ⛔ The model would keep the sequential id while the emitter rebinds the name to the reserved one.
        var asset = MakeAsset(out _);
        var a = asset.CreateEvent("A")!;

        asset.RenameEvent(a.EventId, "Sensor.FirstThreat").Should().BeFalse();
        a.Name.Should().Be("A");
    }

    [Fact]
    public void Renaming_an_unknown_event_is_a_no_op()
    {
        var asset = MakeAsset(out _);

        asset.RenameEvent(99, "Whatever").Should().BeFalse();
    }
}

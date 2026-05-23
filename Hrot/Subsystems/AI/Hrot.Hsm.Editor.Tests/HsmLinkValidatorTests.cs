using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public class HsmLinkValidatorTests
{
    // ---- helpers ----

    private static HsmAsset BuildAndProject(HsmBuilder builder, string name = "TestMachine")
    {
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flat     = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flat);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), name, "", false, "");
    }

    private static PinId OutputPin(StateNode s) =>
        new(StateNode.DeriveOutputPinId(s.StableId));

    private static PinId InputPin(StateNode s) =>
        new(StateNode.DeriveInputPinId(s.StableId));

    // ---- tests ----

    [Fact]
    public void Validate_normal_transition_returns_Valid()
    {
        var builder = new HsmBuilder("M");
        builder.Event("Go", 1);
        builder.State("Idle").Initial();
        builder.State("Running");
        var asset = BuildAndProject(builder);

        var idle    = asset.AllStates[0];
        var running = asset.AllStates[1];
        var validator = new HsmLinkValidator(asset);

        var result = validator.Validate(OutputPin(idle), InputPin(running));

        result.Verdict.Should().Be(LinkValidity.Valid);
    }

    [Fact]
    public void Validate_from_final_state_returns_Invalid()
    {
        var builder = new HsmBuilder("M");
        builder.State("Active").Initial();
        builder.State("Done").Final();
        var asset = BuildAndProject(builder);

        var done   = asset.AllStates.First(s => s.IsFinal);
        var active = asset.AllStates.First(s => !s.IsFinal);
        var validator = new HsmLinkValidator(asset);

        var result = validator.Validate(OutputPin(done), InputPin(active));

        result.Verdict.Should().Be(LinkValidity.Invalid);
    }

    [Fact]
    public void Validate_to_history_state_returns_Invalid()
    {
        var builder = new HsmBuilder("M");
        var parent = builder.State("Parent");
        parent.Child("Child1", sb => sb.Initial());
        parent.Child("H",      sb => sb.History());
        var asset = BuildAndProject(builder);

        var historyState = asset.AllStates.First(s => s.IsHistory);
        var child1       = asset.AllStates.First(s => s.Name == "Child1");
        var validator    = new HsmLinkValidator(asset);

        var result = validator.Validate(OutputPin(child1), InputPin(historyState));

        result.Verdict.Should().Be(LinkValidity.Invalid);
    }

    [Fact]
    public void Validate_self_transition_returns_Valid()
    {
        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);

        var idle      = asset.AllStates.First(s => s.Name == "Idle");
        var validator = new HsmLinkValidator(asset);

        var result = validator.Validate(OutputPin(idle), InputPin(idle));

        result.Verdict.Should().Be(LinkValidity.Valid);
    }

    [Fact]
    public void Validate_unknown_pin_id_returns_Invalid()
    {
        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);

        var validator = new HsmLinkValidator(asset);
        var unknownPin = new PinId(Guid.NewGuid());
        var idle = asset.AllStates.First();

        var result = validator.Validate(unknownPin, InputPin(idle));

        result.Verdict.Should().Be(LinkValidity.Invalid);
    }
}

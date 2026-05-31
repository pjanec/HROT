using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Emit;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public class HsmFluentEmitterTests
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

    private static string EmitAsset(HsmAsset asset) => new HsmFluentEmitter().Emit(asset);

    // ---- tests ----

    [Fact]
    public void Emit_is_deterministic()
    {
        var builder = new HsmBuilder("M");
        builder.Event("Tick", 1);
        builder.State("Idle");
        var asset = BuildAndProject(builder);

        var emitter = new HsmFluentEmitter();
        string first  = emitter.Emit(asset);
        string second = emitter.Emit(asset);

        first.Should().Be(second);
    }

    [Fact]
    public void Emit_contains_editor_generated_marker()
    {
        var builder = new HsmBuilder("M");
        builder.State("S");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain(FluentCSharpEmitterBase.EditorGeneratedMarker);
    }

    [Fact]
    public void Emit_contains_asset_id()
    {
        var builder = new HsmBuilder("M");
        builder.State("S");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain(asset.AssetId.ToString("D"));
    }

    [Fact]
    public void Emit_contains_event_declaration()
    {
        var builder = new HsmBuilder("M");
        builder.Event("Activated", 5);
        builder.State("Idle");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain("builder.Event(\"Activated\", 5");
    }

    [Fact]
    public void Emit_contains_state_name()
    {
        var builder = new HsmBuilder("M");
        builder.State("Running");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain("builder.State(\"Running\"");
    }

    [Fact]
    public void Emit_contains_layout_method()
    {
        var builder = new HsmBuilder("M");
        builder.State("S");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain("public static HsmEditorLayout Layout()");
    }

    [Fact]
    public void Emit_contains_compile_method_with_definition_attribute()
    {
        var builder = new HsmBuilder("M");
        builder.State("S");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain("[HsmDefinition(");
        code.Should().Contain("public static HsmDefinitionBlob Compile()");
    }

    [Fact]
    public void Emit_contains_global_transition()
    {
        var builder = new HsmBuilder("M");
        builder.Event("Reset", 1);
        builder.State("Active");
        builder.GlobalTransition("Reset", "Active");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain("builder.GlobalTransition(");
    }

    [Fact]
    public void Emit_contains_initial_flag_for_initial_state()
    {
        var builder = new HsmBuilder("M");
        builder.State("Start").Initial();
        builder.State("Other");
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain(".Initial()");
    }

    [Fact]
    public void Emit_contains_final_flag_for_final_state()
    {
        var builder = new HsmBuilder("M");
        builder.State("Running").Initial();
        builder.State("Done").Final();
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().Contain(".Final()");
    }

    // ---- BPF-022: DeferEvent emission -----------------------------------

    [Fact]
    public void Emit_contains_DeferEvent_calls_for_each_deferred_id()
    {
        var builder = new HsmBuilder("M");
        builder.Event("Tick",  1);
        builder.Event("Fire",  2);
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);

        // Manually populate DeferredEventIds on the "Idle" state (projector
        // does not populate this yet; emitter must emit what is present).
        var idle = asset.AllStates.First(s => s.Name == "Idle");
        idle.DeferredEventIds.Add(2);
        idle.DeferredEventIds.Add(1);

        string code = EmitAsset(asset);

        // Both IDs must appear, in ascending order.
        code.Should().Contain(".DeferEvent(1)");
        code.Should().Contain(".DeferEvent(2)");
        var idx1 = code.IndexOf(".DeferEvent(1)", StringComparison.Ordinal);
        var idx2 = code.IndexOf(".DeferEvent(2)", StringComparison.Ordinal);
        idx1.Should().BeLessThan(idx2, "lower event ID must be emitted first");
    }

    [Fact]
    public void Emit_omits_DeferEvent_when_no_deferred_ids()
    {
        var builder = new HsmBuilder("M");
        builder.State("Idle").Initial();
        var asset = BuildAndProject(builder);

        string code = EmitAsset(asset);

        code.Should().NotContain(".DeferEvent(");
    }

    // ---- BPF-011: deferred events round-trip (builder -> blob -> projector) ----

    [Fact]
    public void HsmDeferredEvents_RoundTrip_BlobToProjectorToEmit()
    {
        // Build a machine with a state that defers two events.
        var builder = new HsmBuilder("M");
        builder.Event("Tick",  1);
        builder.Event("Fire",  2);
        builder.State("Idle").Initial().DeferEvent(1).DeferEvent(2);

        var asset = BuildAndProject(builder);

        // Projector must populate DeferredEventIds from metadata.DeferredEventsByState.
        var idle = asset.AllStates.First(s => s.Name == "Idle");
        idle.DeferredEventIds.Should().BeEquivalentTo(new ushort[] { 1, 2 });

        // Emitter must emit the DeferEvent calls in ascending order.
        string code = EmitAsset(asset);
        code.Should().Contain(".DeferEvent(1)");
        code.Should().Contain(".DeferEvent(2)");
        var idx1 = code.IndexOf(".DeferEvent(1)", StringComparison.Ordinal);
        var idx2 = code.IndexOf(".DeferEvent(2)", StringComparison.Ordinal);
        idx1.Should().BeLessThan(idx2, "lower event ID must be emitted first");
    }
}

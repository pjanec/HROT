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
}

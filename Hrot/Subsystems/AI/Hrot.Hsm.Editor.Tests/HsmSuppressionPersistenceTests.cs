using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Layout;
using Fhsm.Compiler;
using Hrot.Hsm.Editor.Emit;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmSuppressionPersistenceTests
{
    private static (HsmDefinitionBlob blob, MachineMetadata metadata) Compile(HsmBuilder builder)
    {
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, metadata);
    }

    private static HsmAsset MakeAsset(string name = "MasterAI")
    {
        var builder = new HsmBuilder(name);
        builder.State("Idle").Initial();
        var (blob, metadata) = Compile(builder);
        return Hrot.Hsm.Editor.Model.HsmAssetProjector.Project(
            blob, metadata, null, Guid.NewGuid(), name, $"/trees/{name}.cs", true, "Hrot.AI.HSM");
    }

    [Fact]
    public void Emit_Outputs_Suppressions()
    {
        var original = MakeAsset("TestSubtree");
        original.SetConflictSuppressed("v1", "w1_w2", true);
        original.SetUnusedWarningSuppressed("v3", true);

        var emitter = new HsmFluentEmitter();
        var code = emitter.Emit(original);

        code.Should().Contain(".SuppressBlackboardConflict(\"v1\", \"w1_w2\")");
        code.Should().Contain(".SuppressUnusedWarning(\"v3\")");
    }

    [Fact]
    public void Projector_Loads_Suppressions()
    {
        var builder = new HsmBuilder("TestSubtree");
        builder.State("Idle").Initial();
        var (blob, metadata) = Compile(builder);

        var layout = new HsmEditorLayoutBuilder()
            .SuppressBlackboardConflict("vB", "k1_k2")
            .SuppressUnusedWarning("vA")
            .Build();

        var asset = HsmAssetProjector.Project(
            blob,
            metadata,
            layout,
            Guid.NewGuid(),
            "Tree1",
            "p.cs",
            true,
            "H");

        asset.IsConflictSuppressed("vB", "k1_k2").Should().BeTrue();
        asset.IsUnusedWarningSuppressed("vA").Should().BeTrue();
        asset.IsConflictSuppressed("vA", "k").Should().BeFalse();
    }
}
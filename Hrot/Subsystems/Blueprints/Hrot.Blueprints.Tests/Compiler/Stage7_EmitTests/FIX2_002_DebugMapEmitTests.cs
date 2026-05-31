using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for FIX2-002: DebugMap fields must be populated during blueprint emit.
/// </summary>
public sealed class FIX2_002_DebugMapEmitTests
{
    private static CompileOptions DebugOptions => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// FIX2-002 SC: Compile an Instance blueprint with a variable and a graph.
    /// The resulting DebugMap must have non-empty AssetName, GeneratedSourcePath,
    /// Graphs, Pins, StateLayout.Fields, and at least one entry with a non-empty NodeKind.
    /// </summary>
    [Fact]
    public void DebugMap_CompiledAsset_HasNonEmptyPinsAndGraphs()
    {
        var asset = BlueprintAssetBuilder
            .Instance("DebugMapTest")
            .WithVariable("Health", typeof(float))
            .WithGraph("Tick", g => g
                .Entry()
                .Branch("", b => b.Return(), b => b.Return()))
            .Build();

        var compiler = new BlueprintCompiler();
        var result   = compiler.Compile(asset, DebugOptions);

        Assert.True(result.Succeeded,
            string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var map = result.DebugMap;
        Assert.NotNull(map);

        Assert.Equal("DebugMapTest", map.AssetName);
        Assert.NotEmpty(map.GeneratedSourcePath);
        Assert.NotEmpty(map.Graphs);
        Assert.NotEmpty(map.Pins);
        Assert.NotEmpty(map.StateLayout.Fields);
        Assert.True(map.Entries.Any(e => !string.IsNullOrEmpty(e.NodeKind)),
            "At least one DebugMapEntry must have a non-empty NodeKind after emit.");

        // Verify the Health variable appears in the state layout with sensible offset/size.
        var healthField = map.StateLayout.Fields.SingleOrDefault(f => f.Name == "Health");
        Assert.NotNull(healthField);
        Assert.Equal(4, healthField.SizeBytes);
        Assert.True(healthField.OffsetBytes >= 16,
            "Health variable must start after the 16-byte BlueprintLatentCursor.");
    }
}

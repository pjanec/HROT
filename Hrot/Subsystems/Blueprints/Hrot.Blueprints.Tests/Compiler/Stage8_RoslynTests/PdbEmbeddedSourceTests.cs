using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Tests.Builders;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests that verify PDB is emitted with embedded source when the option is set.
/// </summary>
public sealed class PdbEmbeddedSourceTests
{
    private static CompileOptions OptionsWithPdb() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EmitPdbWithEmbeddedSource: true);

    private static CompileOptions OptionsNoPdb() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EmitPdbWithEmbeddedSource: false);

    [Fact]
    public void WithPdbOption_PdbIsNonNull()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, OptionsWithPdb());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.PortablePdb);
        Assert.True(result.PortablePdb!.Length > 0, "PDB should be non-empty.");
    }

    [Fact]
    public void WithoutPdbOption_PdbIsNullOrEmpty()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, OptionsNoPdb());

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        // When PDB is not requested, either null or empty.
        var pdb = result.PortablePdb;
        Assert.True(pdb == null || pdb.Length == 0,
            "PDB should be null or empty when EmitPdbWithEmbeddedSource=false.");
    }

    [Fact]
    public void PdbContainsEmbeddedSourceSignature()
    {
        // PDB with embedded source must be large enough to actually contain source text.
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, OptionsWithPdb());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PortablePdb);
        // A PDB with non-trivially embedded source should be > 500 bytes.
        Assert.True(result.PortablePdb!.Length > 500,
            "PDB with embedded source should be substantial.");
    }
}

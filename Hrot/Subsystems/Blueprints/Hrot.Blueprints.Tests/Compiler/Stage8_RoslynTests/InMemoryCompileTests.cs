using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using InMemoryRoslynCompiler = Hrot.Blueprints.Core.Compiler.Roslyn.InMemoryRoslynCompiler;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for Stage8 in-memory Roslyn compilation. Covers BP7001.
/// </summary>
public sealed class InMemoryCompileTests
{
    private static MetadataReferenceResolver MakeResolver() =>
        MetadataReferenceResolver.ForRuntimeAssemblies(
            AppDomain.CurrentDomain.GetAssemblies());

    [Fact]
    [CoversDiagnosticCode("BP7001")]
    public void Stage8_InvalidGeneratedSource_EmitsBP7001()
    {
        var sink = new DiagnosticSink();
        var resolver = MakeResolver();
        var compiler = new InMemoryRoslynCompiler(resolver);

        // Deliberately invalid C# to trigger Roslyn error.
        Assert.ThrowsAny<Exception>(() =>
            compiler.Compile("this is NOT valid C#!", "broken.g.cs", "BrokenAssembly", sink));

        Assert.True(sink.HasErrors, "Sink should have errors after BP7001.");
        Assert.Contains(sink.All, d => d.Code == "BP7001");
    }

    [Fact]
    public void Stage8_ValidGeneratedSource_ProducesAssemblyBytes()
    {
        // Use a trivial C# class as generated source.
        const string validSource =
            "namespace Blueprint { public static class TestBp { public const int BlueprintId = 42; } }";

        var sink     = new DiagnosticSink();
        var resolver = MakeResolver();
        var compiler = new InMemoryRoslynCompiler(resolver);

        var (pe, pdb) = compiler.Compile(validSource, "TestBp.g.cs", "Blueprint.TestBp", sink);

        Assert.False(sink.HasErrors, "Valid source should compile without errors.");
        Assert.NotNull(pe);
        Assert.True(pe!.Length > 0, "PE should be non-empty.");
    }

    [Fact]
    public void Stage8_FullPipelineToRoslyn_Succeeds_ForLibraryAsset()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var opts  = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EmitPdbWithEmbeddedSource: true);

        var result = new BlueprintCompiler().Compile(asset, opts);

        Assert.True(result.Succeeded,
            $"End-to-end Roslyn compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.PortablePe);
        Assert.True(result.PortablePe!.Length > 0);
    }
}

using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Tests.Builders;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐ <b>Batch 52 §1 — both halves of the PDB defect, and the only honest way to check either.</b>
///
/// <para>
/// ⛔ <b>The bug.</b> <c>BlueprintCompiler.RoslynFinalizer</c> is installed by
/// <c>Hrot.Blueprints.Core</c>'s <c>[ModuleInitializer]</c>, which fires only when <b>that assembly</b>
/// is first loaded. Nothing in <c>PdbEmbeddedSourceTests</c> referenced a type in it, so the finalizer
/// was null, so <c>EmitPdbWithEmbeddedSource: true</c> produced no PDB — <b>and said nothing</b>. The
/// tests were green for two batches purely because other tests in the same run had loaded the assembly
/// first.
/// </para>
///
/// <para>
/// ⚠⚠ <b>Neither test here can be trusted from a full-suite run.</b> Both would pass without either
/// fix the moment anything else loaded <c>Hrot.Blueprints.Core</c> — which is the whole defect,
/// restated. ⭐ <b>They earn their keep only when run in isolation:</b>
/// </para>
/// <code>
/// dotnet test … --filter "FullyQualifiedName~RoslynFinalizerIsWiredTests.TheFinalizerIsInstalledBeforeAnyTestRuns"
/// </code>
/// <para>
/// ⭐ <b>Measured Batch 52:</b> red before <c>TestAssemblyModuleInit</c> existed, green after. The
/// class-by-class isolation sweep in the batch report is the broad instrument; this is the point one.
/// </para>
/// </summary>
[Collection("RoslynFinalizer")]
public sealed class RoslynFinalizerIsWiredTests
{
    /// <summary>
    /// ⭐ <b>§1a — the load-order half.</b> <c>TestAssemblyModuleInit</c> runs
    /// <c>Hrot.Blueprints.Core</c>'s module constructor before any test in this assembly, so the
    /// finalizer is installed no matter which tests the runner was asked for, or in what order.
    /// </summary>
    [Fact]
    public void TheFinalizerIsInstalledBeforeAnyTestRuns()
    {
        Assert.NotNull(BlueprintCompiler.RoslynFinalizer);
    }

    /// <summary>
    /// ⭐⭐ <b>§1b — the compiler half. <c>BP1672</c>: asking for a PDB that cannot be produced is now
    /// a refusal, not a shrug.</b>
    ///
    /// <para>
    /// ⚠ <b>The finalizer is a mutable static, so this test removes it and puts it back.</b> That is
    /// deliberate and it is the only way to reach the arm at all — the alternative was to leave the
    /// arm reachable only from a process that has never loaded <c>Hrot.Blueprints.Core</c>, i.e. only
    /// from the accident this batch just abolished. ⛔ The swap is why the whole class carries
    /// <c>[Collection]</c>: xUnit runs collections in parallel, and a concurrent test compiling with
    /// <c>EmitPdbWithEmbeddedSource: true</c> would see the null and fail. Every other test that sets
    /// that option is in the same collection.
    /// </para>
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1672")]
    public void RequestingAPdbWithNoFinalizerIsRefusedRatherThanIgnored()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var saved = BlueprintCompiler.RoslynFinalizer;
        try
        {
            BlueprintCompiler.RoslynFinalizer = null;
            var result = new BlueprintCompiler().Compile(asset, PdbOptions(true));

            // ⛔ Before this batch every one of these four assertions failed the other way: Succeeded
            // was true, the diagnostic list was empty, and both byte arrays were null.
            Assert.False(result.Succeeded);
            Assert.Contains(DiagnosticCodes.BP1672, result.Diagnostics.Select(d => d.Code));
            Assert.Null(result.PortablePdb);
            Assert.Null(result.PortablePe);
        }
        finally
        {
            BlueprintCompiler.RoslynFinalizer = saved;
        }
    }

    /// <summary>
    /// ⭐ <b>The rail is scoped to the request.</b> A compile that never asked for a PDB is unaffected
    /// by the finalizer's absence — ⛔ otherwise <c>BP1672</c> would break the source generator, which
    /// runs under netstandard2.0 with no Roslyn finalizer and passes
    /// <c>EmitPdbWithEmbeddedSource: false</c> for exactly that reason.
    /// </summary>
    [Fact]
    public void NotAskingForAPdbIsUnaffectedByTheFinalizerBeingAbsent()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var saved = BlueprintCompiler.RoslynFinalizer;
        try
        {
            BlueprintCompiler.RoslynFinalizer = null;
            var result = new BlueprintCompiler().Compile(asset, PdbOptions(false));

            Assert.True(result.Succeeded,
                $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
            Assert.DoesNotContain(DiagnosticCodes.BP1672, result.Diagnostics.Select(d => d.Code));
        }
        finally
        {
            BlueprintCompiler.RoslynFinalizer = saved;
        }
    }

    /// <summary>
    /// ⭐ <b>And the fixed path still works</b> — with the finalizer installed, the same request that
    /// <c>BP1672</c> refuses produces real bytes. ⚠ This is the assertion
    /// <c>PdbEmbeddedSourceTests</c> was making all along; it is repeated here so the three arms of
    /// the decision sit together.
    /// </summary>
    [Fact]
    public void WithTheFinalizerInstalledTheRequestIsHonoured()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, PdbOptions(true));

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.PortablePdb);
        Assert.NotNull(result.PortablePe);
    }

    private static CompileOptions PdbOptions(bool wantPdb) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EmitPdbWithEmbeddedSource: wantPdb);
}

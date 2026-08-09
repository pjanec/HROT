using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>BP-117 — the real deliverable: prove <c>return default;</c> compiles through real Roslyn,
/// for both a scalar and a <c>ValueTuple</c> return shape.</b>
///
/// <para>
/// <see cref="BP117_LibraryFallThroughTests"/> (Stage5_ScheduleTests) proves the IR-level facts:
/// <c>SealFallThrough</c> reports <see cref="Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticCodes.BP1657"/>
/// as a <b>Warning</b> and sets <see cref="Hrot.Blueprints.Core.Compiler.Ir.IrTerm_Return.ReturnsDefault"/>.
/// None of that proves the emitted C# actually compiles -- <c>BlueprintCompiler.Compile(...).Succeeded</c>
/// only means Stage 7 finished emitting text, not that Roslyn accepts it. This file closes that gap by
/// running the generated source through <see cref="BlueprintTestFixture"/>'s real-Roslyn
/// <c>CompileAndLoad</c> path, which throws with the Roslyn diagnostics if the generated C# does not
/// compile.
/// </para>
/// <para>
/// ⭐ <b>Why this was impossible before the severity ruling:</b> when BP1657 was an Error,
/// <c>BlueprintCompiler.Compile</c> aborted at Stage 5 (<c>sink.HasErrors</c> short-circuits every later
/// stage -- see <c>BlueprintCompiler.Compile</c>), so Stage 7/8 never ran and <c>return default;</c>
/// never reached a compiler. Now that BP1657 is a Warning, the pipeline reaches Stage 7 emit and Stage 8
/// Roslyn, so this is the first time the code path can be proven at all.
/// </para>
/// <para>
/// ⚠ <see cref="BlueprintTestFixture.CompileAndLoad(BlueprintAsset, CompilerMode)"/> does not treat
/// warnings as errors, so the BP1657 warning itself does not block these compiles -- exactly the point.
/// </para>
/// <para>
/// Fixture shape mirrors <c>BP117_LibraryFallThroughTests.MakeNoReturnGraph</c> /
/// <c>MakeLibraryAsset</c> exactly: a Library graph whose only node is an
/// <see cref="EventEntryNode"/> with an unlinked exec-out pin and NO <see cref="ReturnNode"/> anywhere,
/// so the exec chain runs off the end and drives <c>SealFallThrough</c> rather than
/// <c>BuildReturnTerminator</c>.
/// </para>
/// </summary>
public sealed class BP117_ReturnDefaultRoslynTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// Builds a graph "FallOff": a single <see cref="EventEntryNode"/> whose exec-out pin is left
    /// UNLINKED (and NO <see cref="ReturnNode"/> anywhere in the graph) -- the exec chain runs off the
    /// end at Entry itself, driving <c>SealFallThrough</c>. Identical shape to
    /// <c>BP117_LibraryFallThroughTests.MakeNoReturnGraph</c>.
    /// </summary>
    private static Graph MakeNoReturnGraph(Guid id, params (string Name, string TypeId)[] outputs)
    {
        var entryId = Guid.NewGuid();
        var entryEx = Guid.NewGuid();

        var nodes = new List<Node>
        {
            new EventEntryNode
            {
                Id = entryId,
                Pins = new List<Pin>
                {
                    new() { Id = entryEx, Name = "ExecOut", Direction = "Out",
                            IsExec = true, TypeRef = new() },
                },
            },
        };

        return new Graph
        {
            Id = id, Name = "FallOff", Kind = GraphKind.Function,
            Inputs = new(),
            Outputs = outputs.Select(o => new ParameterDecl
            {
                Id = Guid.NewGuid(), Name = o.Name, Type = new BlueprintTypeRef { TypeId = o.TypeId },
            }).ToList(),
            Nodes = nodes, Links = new List<Link>(), // no links at all -- Entry's ExecOut dangles
        };
    }

    private static BlueprintAsset MakeLibraryAsset(Graph graph) => new()
    {
        AssetId          = Guid.NewGuid(), Name = "Bp117RoslynLibraryAsset",
        Dispatch         = BlueprintDispatchKind.Library,
        Parameters       = new(),
        WorkingState     = new(),
        Variables        = new(),
        EventDispatchers = new(),
        CustomEvents     = new(),
        CallablePeers    = new(),
        Graphs           = new() { graph },
        Header           = new Header(),
    };

    /// <summary>
    /// BP-117 Roslyn fact 1 (the CS0126 regression lock): a Library graph declaring ONE output,
    /// whose exec chain ends with no Return node, must compile through real Roslyn. Before BP1657 was
    /// a Warning, this exact shape was never reachable past Stage 5 -- and before the underlying
    /// BP-117 fix at all, the generated <c>return;</c> was Roslyn CS0126 against a method returning
    /// <c>System.Int32</c>.
    /// </summary>
    [Fact]
    public void LibraryGraphFallingOffTheEnd_CompilesThroughRoslyn()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(), ("Result", "System.Int32"));
        var asset = MakeLibraryAsset(graph);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Throws with the Roslyn diagnostics if the generated C# does not compile (e.g. CS0126).
        fixture.CompileAndLoad(asset, DefaultOptions());
    }

    /// <summary>
    /// ⭐ BP-117 Roslyn fact 2 -- the important one: two declared outputs, the exact
    /// <c>(bool, bool)</c> <c>ValueTuple</c> shape from the original field bug report
    /// ("error CS0126: An object of a type convertible to '(bool, bool)' is required"). Proves
    /// <c>return default;</c> is valid C# for a tuple return type, not just a scalar -- the tuple
    /// arity is exactly what a scalar-only proof would miss.
    /// </summary>
    [Fact]
    public void LibraryGraphFallingOffTheEnd_TwoOutputs_CompilesThroughRoslyn()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(),
            ("First", "System.Boolean"), ("Second", "System.Boolean"));
        var asset = MakeLibraryAsset(graph);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Throws with the Roslyn diagnostics if the generated C# does not compile (e.g. CS0126
        // against the (bool, bool) ValueTuple return type).
        fixture.CompileAndLoad(asset, DefaultOptions());
    }

    /// <summary>
    /// BP-117 Roslyn fact 3: the generated C# source text itself contains <c>return default;</c> --
    /// not merely that Roslyn happens to accept whatever was emitted. Reaches the generated source
    /// the same way <c>BP73_MultipleFunctionOutputsTests</c> does: calling
    /// <see cref="Hrot.Blueprints.Core.Compiler.BlueprintCompiler.Compile"/> directly and reading
    /// <c>CompileResult.GeneratedSource</c> -- both already public, so no production visibility had
    /// to change to reach this.
    /// </summary>
    [Fact]
    public void LibraryGraphFallingOffTheEnd_EmitsReturnDefault()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(), ("Result", "System.Int32"));
        var asset = MakeLibraryAsset(graph);

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ",
                result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.NotNull(result.GeneratedSource);
        Assert.Contains("return default;", result.GeneratedSource!);
    }
}

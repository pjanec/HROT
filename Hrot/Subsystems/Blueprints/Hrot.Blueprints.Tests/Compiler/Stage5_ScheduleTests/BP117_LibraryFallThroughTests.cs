using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>BP-117 — <c>SealFallThrough</c> must not synthesize a void return for an outputs-declaring
/// Library graph.</b>
///
/// <para>
/// BP-104 fixed the EXPLICIT-<see cref="ReturnNode"/> path (<c>BuildReturnTerminator</c>) to be
/// outputs-driven, but left <c>SealFallThrough</c> — the implicit-return path taken when an exec
/// chain runs off the end with NO <see cref="ReturnNode"/> at all — emitting a bare void
/// <see cref="IrTerm_Return"/> for every non-status-returning dispatch kind. For a Library graph
/// that DECLARES outputs, that method compiles to a return type of <c>T</c> or a
/// <c>ValueTuple</c> (e.g. <c>(bool, bool)</c>), so the emitted <c>return;</c> is Roslyn
/// <b>CS0126</b> — reported against generated code the author never wrote.
/// </para>
/// <para>
/// The fix: when the falling-off graph is a Library with <c>Outputs.Count &gt; 0</c>,
/// <c>SealFallThrough</c> now reports <see cref="DiagnosticCodes.BP1657"/> as a
/// <b>Warning</b> (not an Error — user ruling: "warning+return default is a perfect solution")
/// AND sets <see cref="IrTerm_Return.ReturnsDefault"/> so the emitter writes
/// <c>return default;</c> instead of <c>return;</c> — valid C#, plus a diagnostic, because a
/// silently-defaulted return value deserves a nudge even though it must not be fatal. Unreal
/// itself silently returns defaults off a graph's dangling exec path, so an Error here would be
/// stricter than authors coming from that background expect.
/// </para>
/// <para>
/// ⭐ Warning (rather than Error) also matters structurally, not just ergonomically: as an Error
/// the compile pipeline aborted before Stage 7/8 emit, so the <c>return default;</c> code path
/// could never be exercised by any test — it existed only on paper. As a Warning the pipeline
/// keeps going all the way to real Roslyn compilation, which is what finally lets
/// <c>BP117_ReturnDefaultRoslynTests</c> (Stage8_RoslynTests) prove the emitted C# actually
/// compiles, for both a scalar and a <c>ValueTuple</c> return shape.
/// </para>
/// <para>
/// ⚠ Unlike <see cref="BP104_LibraryReturnTerminatorTests"/> (which exercises
/// <c>BuildReturnTerminator</c> via an EXPLICIT <see cref="ReturnNode"/>), every fixture here has
/// NO Return node at all — the exec chain simply ends, so <c>SealFallThrough</c> is the path under
/// test.
/// </para>
/// </summary>
public sealed class BP117_LibraryFallThroughTests
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

    private static (IrAsset ir, DiagnosticSink sink) RunSchedule(BlueprintAsset asset)
    {
        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir = Stage5_Schedule.Run(typed, ctx);
        return (ir, sink);
    }

    /// <summary>
    /// Builds a graph "FallOff": a single <see cref="EventEntryNode"/> whose exec-out pin is left
    /// UNLINKED (and NO <see cref="ReturnNode"/> anywhere in the graph) -- the exec chain runs off
    /// the end at Entry itself, driving <c>SealFallThrough</c> rather than <c>BuildReturnTerminator</c>.
    /// The graph declares whatever outputs the caller passes, independent of any node wiring, since
    /// no Return node exists to consume them.
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
        AssetId          = Guid.NewGuid(), Name = "Bp117LibraryAsset",
        Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
        Parameters       = new(),
        WorkingState     = new(),
        Variables        = new(),
        EventDispatchers = new(),
        CustomEvents     = new(),
        CallablePeers    = new(),
        Graphs           = new() { graph },
        Header           = new Header(),
    };

    private static BlueprintAsset MakeInstanceAsset(Graph graph) => new()
    {
        AssetId          = Guid.NewGuid(), Name = "Bp117InstanceAsset",
        Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
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
    /// BP-117 fact 1 (defect-locking): a Library graph declaring one output, whose exec chain ends
    /// with no Return node, must report <see cref="DiagnosticCodes.BP1657"/> as a
    /// <b>Warning</b> — not an Error. This is a deliberate user ruling ("warning+return default is
    /// a perfect solution"), not merely a downgrade: Unreal silently returns defaults off such a
    /// path, so a hard Error would be stricter than authors expect, and — the reason this is
    /// asserted explicitly rather than left unchecked — Warning is what lets the pipeline keep
    /// going all the way to Stage 8 emit, so the <c>return default;</c> it produces can actually be
    /// proven through real Roslyn (see <c>BP117_ReturnDefaultRoslynTests</c>). Asserting the exact
    /// severity here, rather than just the diagnostic code, is the regression lock: an assertion
    /// that accepted any severity would silently keep passing if someone flipped this back to
    /// Error and re-broke that guarantee.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1657")]
    public void LibraryWithDeclaredOutput_NoReturnNode_EmitsBP1657()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(), ("Result", "System.Int32"));

        var (_, sink) = RunSchedule(MakeLibraryAsset(graph));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1657
            && d.Severity == DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// BP-117 additivity guard (new): the same fixture that reports BP1657 must NOT abort the
    /// compile pipeline — the entire point of the Warning ruling is that a Library graph falling
    /// off the end with declared outputs keeps compiling (through Stage 5 here, and all the way
    /// through Roslyn emit elsewhere) rather than being treated as fatal. Pins the "does not fail
    /// the compile" half of the contract independently of the severity check above, since a test
    /// only checking the diagnostic's severity string would not by itself prove the sink stays
    /// error-free.
    /// </summary>
    [Fact]
    public void LibraryWithDeclaredOutput_NoReturnNode_DoesNotFailTheCompile()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(), ("Result", "System.Int32"));

        var (_, sink) = RunSchedule(MakeLibraryAsset(graph));

        Assert.False(sink.HasErrors);
    }

    /// <summary>
    /// BP-117 fact 2: the same fixture's terminator is <see cref="IrTerm_Return"/> with a null
    /// value AND <see cref="IrTerm_Return.ReturnsDefault"/> set -- the emitter must write
    /// <c>return default;</c>, not <c>return;</c> (CS0126) and not a value it never resolved.
    /// </summary>
    [Fact]
    public void LibraryWithDeclaredOutput_NoReturnNode_TerminatorReturnsDefault()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(), ("Result", "System.Int32"));

        var (ir, _) = RunSchedule(MakeLibraryAsset(graph));

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
        Assert.Null(retTerm.Value);
        Assert.True(retTerm.ReturnsDefault);
    }

    /// <summary>
    /// BP-117 fact 3: two declared outputs -- the exact <c>(bool, bool)</c> tuple shape from the
    /// field bug report. Same two assertions as fact 2, pinned independently since the tuple arity
    /// is the shape that produced the original CS0126. The severity assertion below is
    /// deliberately explicit (<c>Warning</c>, not just "some diagnostic") for the same regression
    /// reason as fact 1 -- a Warning here is what let this exact tuple shape be proven through real
    /// Roslyn in <c>BP117_ReturnDefaultRoslynTests.LibraryGraphFallingOffTheEnd_TwoOutputs_CompilesThroughRoslyn</c>.
    /// </summary>
    [Fact]
    public void LibraryWithTwoDeclaredOutputs_NoReturnNode_AlsoReturnsDefault()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid(),
            ("First", "System.Boolean"), ("Second", "System.Boolean"));

        var (ir, sink) = RunSchedule(MakeLibraryAsset(graph));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1657
            && d.Severity == DiagnosticSeverity.Warning);

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
        Assert.Null(retTerm.Value);
        Assert.True(retTerm.ReturnsDefault);
    }

    /// <summary>
    /// BP-117 additivity guard: a Library graph declaring ZERO outputs, no Return node, must keep
    /// getting <see cref="IrTerm_ReturnStatus"/>(Success) -- the deliberate BP-104/SealFallThrough
    /// behaviour for the status-returning case -- and must NOT emit BP1657. This is the branch the
    /// fix must leave untouched.
    /// </summary>
    [Fact]
    public void LibraryWithZeroOutputs_NoReturnNode_StillReturnsStatus_NotDefault()
    {
        var graph = MakeNoReturnGraph(Guid.NewGuid()); // no outputs declared

        var (ir, sink) = RunSchedule(MakeLibraryAsset(graph));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1657);

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_ReturnStatus>(lastBlock.Terminator);
        Assert.Equal(NodeStatus.Success, retTerm.Status);
    }

    /// <summary>
    /// BP-117 additivity guard: an Instance asset (void method) with no Return node must keep
    /// getting a plain void <see cref="IrTerm_Return"/> with <see cref="IrTerm_Return.ReturnsDefault"/>
    /// FALSE -- <c>return;</c> is correct there, so the fix must not touch this path at all.
    /// </summary>
    [Fact]
    public void InstanceWithNoReturnNode_StillReturnsVoid_NotDefault()
    {
        // Instance graphs don't declare function-style Outputs; the graph-level Outputs list stays
        // empty here to match how Instance ("Tick"-style) graphs are actually shaped.
        var graph = MakeNoReturnGraph(Guid.NewGuid());

        var (ir, sink) = RunSchedule(MakeInstanceAsset(graph));

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1657);

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
        Assert.Null(retTerm.Value);
        Assert.False(retTerm.ReturnsDefault);
    }
}

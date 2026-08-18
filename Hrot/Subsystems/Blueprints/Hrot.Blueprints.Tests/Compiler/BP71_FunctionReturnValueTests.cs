using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;
using DiagnosticSeverity    = Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSeverity;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>BP-71 — a Function graph's return value must be wirable.</b>
/// Decisions: <c>Architect_Question_24_Function_Return_Value_Wiring.md</c> (A1 + B1 + C3 + D1′).
///
/// <para>
/// <b>Why this file exists at all — trap #9.</b> Before BP-71 the Return node's value pin was
/// declared <c>Direction=="Out"</c> by both projections and consumed as an <em>input</em> by
/// <c>Stage5.BuildReturnTerminator</c>. Both halves were individually correct and individually
/// test-locked (two tests asserted the <c>"Out"</c> contract in prose), yet the feature was
/// unusable: the canvas maps Direction straight through and rejects same-direction links, so the
/// pin could never be wired. <b>No test performed the designer's gesture</b> — placing a Return node
/// in a Function graph with an output and connecting a value to it — and no shipped asset did
/// either (0 of 92). This suite crosses that seam deliberately: it asserts the editor's link
/// validator ACCEPTS the wire, not merely that a pin exists.
/// </para>
/// </summary>
public sealed class BP71_FunctionReturnValueTests
{
    // ── shared helpers ────────────────────────────────────────────────────────

    private static CompileOptions MakeOptions() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static IReadOnlyList<Diagnostic> RunStage2(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, MakeOptions());
        Stage2_Validate.Run(asset, ctx);
        return sink.All;
    }

    private static BlueprintAsset MakeAsset(params Graph[] graphs) => new BlueprintAsset
    {
        AssetId          = Guid.NewGuid(),
        Name             = "TestAsset",
        Dispatch         = BlueprintDispatchKind.Instance,
        Parameters       = new(),
        WorkingState     = new(),
        Variables        = new(),
        EventDispatchers = new(),
        CustomEvents     = new(),
        CallablePeers    = new(),
        Graphs           = new List<Graph>(graphs),
        Header           = new Header(),
    };

    private static ParameterDecl Out(string name, string typeId) => new()
    {
        Id   = Guid.NewGuid(),
        Name = name,
        Type = new BlueprintTypeRef { TypeId = typeId },
    };

    /// <summary>
    /// Function graph: Entry → (Literal) → Return, where the Return node carries an authored value
    /// pin in <paramref name="valueDirection"/>. When <paramref name="wireValue"/> is true a data
    /// link is added from the Literal's out-pin into that value pin.
    /// </summary>
    private static Graph MakeReturningGraph(
        string name, Guid id, string typeId,
        string valueDirection = "In", bool wireValue = true, bool includeValuePin = true)
    {
        var entryId = Guid.NewGuid();
        var litId   = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var entryExec = Guid.NewGuid();
        var litOut    = Guid.NewGuid();
        var retExecIn = Guid.NewGuid();
        var retValue  = Guid.NewGuid();

        var retPins = new List<Pin>
        {
            new() { Id = retExecIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
        };
        if (includeValuePin)
        {
            retPins.Add(new Pin
            {
                Id = retValue, Name = "Result", Direction = valueDirection, IsExec = false,
                TypeRef = new BlueprintTypeRef { TypeId = typeId },
            });
        }

        var links = new List<Link>
        {
            new() { FromNodeId = entryId, FromPinId = entryExec, ToNodeId = retId, ToPinId = retExecIn },
        };
        if (includeValuePin && wireValue)
        {
            // NOTE the direction of travel: the link ARRIVES at the Return node, whichever
            // Direction string its value pin carries. That asymmetry was the whole bug.
            links.Add(new Link
            {
                FromNodeId = litId, FromPinId = litOut, ToNodeId = retId, ToPinId = retValue,
            });
        }

        return new Graph
        {
            Id      = id,
            Name    = name,
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new List<ParameterDecl> { Out("Result", typeId) },
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryExec, Name = "ExecOut", Direction = "Out",
                                IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id = litId, TypeId = typeId, ValueJson = "7",
                    Pins = new List<Pin>
                    {
                        new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = typeId } },
                    },
                },
                new ReturnNode { Id = retId, Pins = retPins },
            },
            Links = links,
        };
    }

    // =====================================================================
    // A1 — the gesture that was impossible: wiring a value into Return
    // =====================================================================

    /// <summary>
    /// <b>The seam-crossing test.</b> The editor's own link validator must ACCEPT a data link from a
    /// producer's out-pin into the Return node's value pin. Before BP-71 the value pin was an
    /// Output, so <c>BlueprintLinkValidator</c> rejected this with "Cannot connect pins of the same
    /// direction" — the function's return value was unauthorable, and nothing tested it.
    /// </summary>
    [Fact]
    public void LinkValidator_AcceptsValueWiredIntoReturnNode()
    {
        // wireValue: false — we are validating the gesture the designer is ABOUT to make. With the
        // link already present the single-data-input rule would (correctly) refuse a second source,
        // which would mask what this test is actually asserting.
        var graph = MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32",
                                       wireValue: false);
        var asset = MakeAsset(graph);

        var model     = new BlueprintGraphModel(asset, graph);
        var validator = new BlueprintLinkValidator(model, new BlueprintTypeSystem(
            NullPinDefaultValueEditorRegistry.Instance));

        var literal  = graph.Nodes.OfType<LiteralNode>().Single();
        var ret      = graph.Nodes.OfType<ReturnNode>().Single();
        var fromPin  = literal.Pins.Single(p => !p.IsExec);
        var valuePin = ret.Pins.Single(p => !p.IsExec);

        var result = validator.Validate(new PinId(fromPin.Id), new PinId(valuePin.Id));

        Assert.Equal(LinkValidity.Valid, result.Verdict);   // BP-71: this is the whole point
        Assert.Null(result.Reason);
    }

    /// <summary>
    /// The regression guard for the actual defect: a Return value pin in the legacy <c>"Out"</c>
    /// direction is rejected by the canvas, because a producer's out-pin and it are both Outputs.
    /// This is *why* the projection had to change rather than the validator gaining an exception.
    /// </summary>
    [Fact]
    public void LinkValidator_RejectsValueWiredIntoLegacyOutDirectionPin()
    {
        var graph = MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32",
                                       valueDirection: "Out", wireValue: false);
        var asset = MakeAsset(graph);

        var model     = new BlueprintGraphModel(asset, graph);
        var validator = new BlueprintLinkValidator(model, new BlueprintTypeSystem(
            NullPinDefaultValueEditorRegistry.Instance));

        var literal  = graph.Nodes.OfType<LiteralNode>().Single();
        var ret      = graph.Nodes.OfType<ReturnNode>().Single();
        var fromPin  = literal.Pins.Single(p => !p.IsExec);
        var valuePin = ret.Pins.Single(p => !p.IsExec);

        var result = validator.Validate(new PinId(fromPin.Id), new PinId(valuePin.Id));

        Assert.Equal(LinkValidity.Invalid, result.Verdict);   // same-direction: the bug, as evidence
        Assert.Contains("same direction", result.Reason ?? "");
    }

    /// <summary>
    /// Stage 0 reconstructs the value pin as <c>"In"</c> for a pin-less (JSON-loaded) Return node,
    /// so the editor and the compiler agree on the shape an on-disk asset rehydrates into.
    /// </summary>
    [Fact]
    public void Stage0_RehydratesReturnValuePin_AsDataIn()
    {
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();
        var graph = new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = "Compute",
            Kind    = GraphKind.Function,
            Outputs = new List<ParameterDecl> { Out("Result", "System.Single") },
            Nodes   = new List<Node>
            {
                new EventEntryNode { Id = entryId },   // Pins: [] — the JSON shape
                new ReturnNode     { Id = retId   },
            },
        };
        var asset = MakeAsset(graph);

        Stage0_Rehydrate.Run(asset, MakeOptions());

        var ret      = asset.Graphs[0].Nodes.OfType<ReturnNode>().Single();
        var valuePin = Assert.Single(ret.Pins.Where(p => !p.IsExec));
        Assert.Equal("In",     valuePin.Direction);   // BP-71: wirable direction
        Assert.Equal("Result", valuePin.Name);
        Assert.Equal("System.Single", valuePin.TypeRef.TypeId);
    }

    // =====================================================================
    // B1 — both directions still resolve, so nothing on disk breaks
    // =====================================================================

    [Theory]
    [InlineData("In")]    // the new, wirable form
    [InlineData("Out")]   // legacy hand-authored JSON
    public void Stage5_ResolvesWiredReturnValue_InEitherPinDirection(string direction)
    {
        var graph = MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32",
                                       valueDirection: direction);
        var asset = MakeAsset(graph);

        var term = ScheduleAndGetReturnTerminator(asset);

        Assert.NotNull(term.Value);   // a real value, resolved through the link
    }

    // =====================================================================
    // C3 — an unwired return is an ERROR, and still emits compilable C#
    // =====================================================================

    /// <summary>
    /// The emit half of C3: an unwired value pin must produce a DECLARED <c>default(T)</c> temp, not
    /// the dangling dummy that made the emitter write <c>return __t7;</c> with no <c>var __t7</c>
    /// (CS0103 with no BP diagnostic — BP-69's shape).
    /// </summary>
    [Fact]
    public void Stage5_UnwiredReturnValue_EmitsDeclaredDefaultRatherThanDanglingTemp()
    {
        var graph = MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32",
                                       wireValue: false);
        var asset = MakeAsset(graph);

        var (term, block) = ScheduleAndGetReturnBlock(asset);

        Assert.NotNull(term.Value);
        // The returned value must be produced by a statement in the same block, so the emitter
        // declares it. A `default` IrOp_Const is exactly that statement.
        var producing = block.Statements.FirstOrDefault(
            s => s.ResultValue.HasValue && s.ResultValue.Value.Index == term.Value!.Value.Index);
        Assert.NotNull(producing);
        var constOp = Assert.IsType<IrOp_Const>(producing!.Operation);
        Assert.Equal("default", constOp.CSharpLiteral);
    }

    [Fact]
    [CoversDiagnosticCode("BP1655")]
    public void BP1655_UnwiredReturnValue_IsAnError()
    {
        var asset = MakeAsset(MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32",
                                                 wireValue: false));

        var diags = RunStage2(asset);

        var d = Assert.Single(diags, x => x.Code == "BP1655");
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Compute", d.Message);
        Assert.Contains("Result",  d.Message);
    }

    [Fact]
    [CoversDiagnosticCode("BP1655")]
    public void BP1655_DoesNotFire_WhenTheValueIsWired()
    {
        var asset = MakeAsset(MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32"));

        Assert.Empty(RunStage2(asset).Where(x => x.Code == "BP1655"));
    }

    /// <summary>
    /// A legacy <c>"Out"</c> value pin that IS wired must not be reported as unwired — B1 keeps it
    /// compiling, so flagging it would turn a working asset into an error.
    /// </summary>
    [Fact]
    public void BP1655_DoesNotFire_ForAWiredLegacyOutDirectionPin()
    {
        var asset = MakeAsset(MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32",
                                                 valueDirection: "Out"));

        Assert.Empty(RunStage2(asset).Where(x => x.Code == "BP1655"));
    }

    /// <summary>
    /// A graph with no links at all is an unauthored stub (the on-disk `"Pins": []` / `"Links": []`
    /// shape, e.g. shipped `SquadState.GetThreatLevel`), not a designer error. Stage 5's
    /// <c>default(T)</c> covers correctness there. Mirrors <c>V_GraphStructure</c>'s exemption.
    /// </summary>
    [Fact]
    public void BP1655_DoesNotFire_OnAnUnauthoredLinklessGraph()
    {
        var graph = MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32", wireValue: false);
        graph.Links.Clear();
        var asset = MakeAsset(graph);

        Assert.Empty(RunStage2(asset).Where(x => x.Code == "BP1655"));
    }

    // =====================================================================
    // D1′ — multi-output is "not supported YET", and says so
    // =====================================================================

    [Fact]
    [CoversDiagnosticCode("BP1656")]
    public void BP1656_MoreThanOneOutput_IsAnError_ThatNamesBP73()
    {
        var graph = MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32");
        graph.Outputs.Add(Out("Second", "System.Single"));
        var asset = MakeAsset(graph);

        var d = Assert.Single(RunStage2(asset), x => x.Code == "BP1656");
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // The wording matters: N-output is scheduled work (BP-73), not a permanent prohibition.
        // A designer who hits this must be told it is coming, not that they are wrong.
        Assert.Contains("BP-73", d.Message);
        Assert.Contains("NOT SUPPORTED YET", d.Message);
    }

    [Fact]
    [CoversDiagnosticCode("BP1656")]
    public void BP1656_DoesNotFire_ForASingleOutput()
    {
        var asset = MakeAsset(MakeReturningGraph("Compute", Guid.NewGuid(), "System.Int32"));

        Assert.Empty(RunStage2(asset).Where(x => x.Code == "BP1656"));
    }

    // ── scheduling helpers ────────────────────────────────────────────────────

    private static IrTerm_Return ScheduleAndGetReturnTerminator(BlueprintAsset asset)
        => ScheduleAndGetReturnBlock(asset).Term;

    private static (IrTerm_Return Term, IrBlock Block) ScheduleAndGetReturnBlock(BlueprintAsset asset)
    {
        var opts = MakeOptions();
        Stage0_Rehydrate.Run(asset, opts);

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);
        Stage2_Validate.Run(asset, ctx);

        var normalized = Stage3_Normalize.Run(asset, ctx);
        var typed      = Stage4_TypeResolve.Run(normalized, ctx);
        var ir         = Stage5_Schedule.Run(typed, ctx);

        var graph = ir.Graphs.Single(g => g.Name == "Compute");
        foreach (var b in graph.Blocks)
            if (b.Terminator is IrTerm_Return r)
                return (r, b);

        throw new InvalidOperationException(
            "No IrTerm_Return found; terminators: " +
            string.Join(", ", graph.Blocks.Select(b => b.Terminator?.GetType().Name ?? "null")));
    }
}

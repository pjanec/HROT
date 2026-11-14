using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using ParameterDecl         = Hrot.Blueprints.Core.Assets.ParameterDecl;
using DiagnosticSeverity    = Hrot.Blueprints.Core.Compiler.Diagnostics.DiagnosticSeverity;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>Part A — <see cref="DiagnosticCodes.BP4005"/>: the "no resolver case" trap is now an error, not
/// a silent default.</b> <c>Stage5_Schedule.ResolveNodeOutput</c>'s <c>default:</c> arm used to emit
/// <c>IrOp_Const("default", …)</c> for any data-out pin whose producer had no <c>case</c> — silently,
/// every tick, with a clean build. It now reports BP4005 as an Error first.
///
/// <para>
/// ⭐ <b>The trigger used here is a genuine designer error, not an artificial one.</b>
/// <see cref="ComponentForEachNode"/>'s <c>CurrentItem</c>/<c>CurrentIndex</c> out-pins are
/// loop-locals: <c>ScheduleComponentForEachNode</c> publishes them into the pin caches, schedules the
/// loop body INLINE, then explicitly removes both entries again (the <c>savedKeys</c>/
/// <c>savedStmtKeys</c> save-restore) before the outer chain resumes at "Completed". There is no
/// <c>case ComponentForEachNode</c> in <c>ResolveNodeOutput</c> at all — a consumer outside the body
/// that wires <c>CurrentItem</c> finds it in neither cache and falls into <c>default:</c>. Wiring a
/// loop variable to a consumer outside the loop is exactly the mistake a designer can make on the
/// canvas; BP4005 is what makes the build refuse it instead of quietly printing 0 forever.
/// </para>
/// </summary>
/// <remarks>
/// <b>Part B — <see cref="GraphKind.Macro"/> and the fail-loud net.</b> Three properties locked:
/// round-trips as a JSON string, is SKIPPED (not compiled, not errored) by Stage 5, and is never
/// tick-eligible for <c>InstanceEmitter</c>.
/// </remarks>
public sealed class BP4005_And_MacroGraphKindTests
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

    // ═════════════════════════════════════════════════════════════════════════
    // Part A — BP4005
    // ═════════════════════════════════════════════════════════════════════════

    private static (IrAsset ir, DiagnosticSink sink) RunStage5(BlueprintAsset bp)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        var typed = new TypedAsset(bp,
            new Dictionary<Guid, IrTypeRef>(),
            new Dictionary<Guid, IrTypeRef>());

        var ir = Stage5_Schedule.Run(typed, ctx);
        return (ir, sink);
    }

    /// <summary>
    /// Builds: Entry → ComponentForEachNode → (Completed) → SetVariable → Return, where
    /// <c>SetVariable</c>'s "Value" data-IN is wired FROM the ForEach's "CurrentItem" data-OUT — the
    /// load-bearing wire, and it sits on the far side of "Completed", i.e. OUTSIDE the loop body.
    /// The loop's "Body" exec-out is deliberately left unwired: this test is about a consumer outside
    /// the loop, not about what runs inside it.
    /// </summary>
    private static BlueprintAsset MakeForEachCurrentItemLeakGraph()
    {
        var assetId  = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var collSrcId = Guid.NewGuid();
        var cfeId    = Guid.NewGuid();
        var setVarId = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        var pinEntryOut     = Guid.NewGuid();
        var pinCollSrcOut   = Guid.NewGuid();
        var pinCfeIn        = Guid.NewGuid();
        var pinCfeCollection = Guid.NewGuid();
        var pinCfeBody      = Guid.NewGuid();
        var pinCfeCompleted = Guid.NewGuid();
        var pinCfeCurrentItem  = Guid.NewGuid();
        var pinCfeCurrentIndex = Guid.NewGuid();
        var pinCfeCount        = Guid.NewGuid();
        var pinSetVarIn     = Guid.NewGuid();
        var pinSetVarOut    = Guid.NewGuid();
        var pinSetVarValue  = Guid.NewGuid();
        var pinRetIn        = Guid.NewGuid();

        var graph = new Graph
        {
            Id     = graphId,
            Name   = "G",
            Kind   = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes  = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                // Pure-data source for the "Collection" in-pin — ResolveNodeOutput's LiteralNode case
                // doesn't consult Pins at all, so this needs no pins of its own.
                new LiteralNode
                {
                    Id        = collSrcId,
                    TypeId    = "System.Object",
                    ValueJson = "null",
                },
                new ComponentForEachNode
                {
                    Id               = cfeId,
                    ComponentTypeFqn = "Hrot.Tests.FakeComponent",
                    CountAccessorFqn = "Hrot.Tests.FakeComponentOps.Count",
                    ItemAccessorFqn  = "Hrot.Tests.FakeComponentOps.Item",
                    ElementTypeFqn   = "System.Int32",
                    Pins = new()
                    {
                        new Pin { Id = pinCfeIn,        Name = "In",         Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinCfeCollection, Name = "Collection", Direction = "In",  IsExec = false, TypeRef = new() { TypeId = "System.Object" } },
                        new Pin { Id = pinCfeBody,      Name = "Body",       Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinCfeCompleted, Name = "Completed",  Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinCfeCurrentItem,  Name = "CurrentItem",  Direction = "Out", IsExec = false, TypeRef = new() { TypeId = "System.Int32" } },
                        new Pin { Id = pinCfeCurrentIndex, Name = "CurrentIndex", Direction = "Out", IsExec = false, TypeRef = new() { TypeId = "System.Int32" } },
                        new Pin { Id = pinCfeCount,        Name = "Count",        Direction = "Out", IsExec = false, TypeRef = new() { TypeId = "System.Int32" } },
                    },
                },
                new SetVariableNode
                {
                    Id         = setVarId,
                    VariableId = "Leaked",
                    Pins = new()
                    {
                        new Pin { Id = pinSetVarIn,    Name = "In",    Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSetVarOut,   Name = "Out",   Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinSetVarValue, Name = "Value", Direction = "In",  IsExec = false, TypeRef = new() { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id   = retId,
                    Pins = new() { new Pin { Id = pinRetIn, Name = "In", Direction = "In", IsExec = true, TypeRef = new() } },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = cfeId, ToPinId = pinCfeIn },
                new() { FromNodeId = collSrcId, FromPinId = pinCollSrcOut, ToNodeId = cfeId, ToPinId = pinCfeCollection },
                // "Body" is deliberately left unwired -- nothing runs inside the loop for this test.
                new() { FromNodeId = cfeId, FromPinId = pinCfeCompleted, ToNodeId = setVarId, ToPinId = pinSetVarIn },
                new() { FromNodeId = setVarId, FromPinId = pinSetVarOut, ToNodeId = retId, ToPinId = pinRetIn },
                // ⭐ The load-bearing wire: a loop-local, consumed OUTSIDE the loop.
                new() { FromNodeId = cfeId, FromPinId = pinCfeCurrentItem, ToNodeId = setVarId, ToPinId = pinSetVarValue },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = assetId,
            Name     = "ForEachLeak",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph },
            Header = new Header(),
        };
    }

    /// <summary>
    /// ⭐⭐ The HARD REQUIREMENT: wiring <c>ComponentForEachNode.CurrentItem</c> to a consumer outside
    /// the loop body must produce BP4005 as an Error, naming both the source node kind and the pin.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP4005")]
    public void ForEach_CurrentItemConsumedOutsideBody_EmitsBP4005_Error()
    {
        var (_, sink) = RunStage5(MakeForEachCurrentItemLeakGraph());

        var bp4005 = sink.All.Where(d => d.Code == DiagnosticCodes.BP4005).ToList();
        Assert.NotEmpty(bp4005);
        Assert.All(bp4005, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));

        // Message must name the source node KIND and the PIN -- both are what a designer needs to
        // find the offending wire.
        Assert.Contains(bp4005, d =>
            d.Message.Contains(nameof(ComponentForEachNode)) &&
            d.Message.Contains("CurrentItem"));
    }

    /// <summary>
    /// Negative control: the SAME loop, but nothing wires <c>CurrentItem</c> outside the body (the
    /// link is simply removed, leaving SetVariable's "Value" pin unconnected -- BP4001, not BP4005).
    /// No BP4005 should fire -- proves the diagnostic tracks the specific cross-scope wire, not
    /// "any ComponentForEachNode in the graph".
    /// </summary>
    [Fact]
    public void ForEach_CurrentItemNotConsumedOutsideBody_NoBP4005()
    {
        var asset = MakeForEachCurrentItemLeakGraph();
        var graph = asset.Graphs[0];
        var cfe   = graph.Nodes.OfType<ComponentForEachNode>().Single();
        var currentItemPinId = cfe.Pins.First(p => p.Name == "CurrentItem").Id;

        graph.Links.RemoveAll(l => l.FromNodeId == cfe.Id && l.FromPinId == currentItemPinId);

        var (_, sink) = RunStage5(asset);

        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP4005);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Part B — GraphKind.Macro
    // ═════════════════════════════════════════════════════════════════════════

    private static ParameterDecl Out(string name, string typeId) => new()
    {
        Id   = Guid.NewGuid(),
        Name = name,
        Type = new BlueprintTypeRef { TypeId = typeId },
    };

    /// <summary>Entry → Literal(<paramref name="literal"/>) → Return("Result"). Same shape for both
    /// a Function graph and a (structurally identical) Macro graph -- the only difference is
    /// <paramref name="kind"/>, so any behavioral difference in the compile is attributable to Kind
    /// alone, not to shape.</summary>
    private static Graph MakeLiteralReturningGraph(string name, GraphKind kind, string literal)
    {
        var entryId = Guid.NewGuid();
        var litId   = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var entryExec = Guid.NewGuid();
        var litOut    = Guid.NewGuid();
        var retExecIn = Guid.NewGuid();
        var retValue  = Guid.NewGuid();

        return new Graph
        {
            Id      = Guid.NewGuid(),
            Name    = name,
            Kind    = kind,
            Inputs  = new(),
            Outputs = new List<ParameterDecl> { Out("Result", "System.Int32") },
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryExec, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id = litId, TypeId = "System.Int32", ValueJson = literal,
                    Pins = new List<Pin>
                    {
                        new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id = retId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                        new() { Id = retValue,  Name = "Result", Direction = "In", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = entryExec, ToNodeId = retId, ToPinId = retExecIn },
                new() { FromNodeId = litId,   FromPinId = litOut,    ToNodeId = retId, ToPinId = retValue },
            },
        };
    }

    /// <summary>
    /// 1. Round-trip fact: <see cref="GraphKind.Macro"/> serializes as the STRING "Macro" (not a
    /// number), and survives a Serialize → Deserialize round trip. This is what makes the addition
    /// additive on disk -- an old asset that never saw "Macro" needs no migration.
    /// </summary>
    [Fact]
    public void GraphKind_Macro_SerializesAsString_AndRoundTrips()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "MacroRoundTrip",
            Dispatch = BlueprintDispatchKind.Library,
            Graphs   = new List<Graph>
            {
                new Graph
                {
                    Id = Guid.NewGuid(), Name = "SomeMacro", Kind = GraphKind.Macro,
                    Inputs = new(), Outputs = new(), Nodes = new(), Links = new(),
                },
            },
            Header = new Header(),
        };

        var json = BlueprintJsonServices.Serialize(asset);

        Assert.Contains("\"Macro\"", json);   // a string, not e.g. "Kind\":3"

        var reloaded = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(reloaded);
        Assert.Equal(GraphKind.Macro, reloaded!.Graphs.Single().Kind);
    }

    /// <summary>
    /// 2. ⭐ A macro graph is SKIPPED by Stage 5, not compiled and not errored. Declaring a macro is
    /// legal on its own -- a macro-library asset can declare macros with no call sites of its own --
    /// so the compile must succeed cleanly with the Function graph's method present and NOTHING
    /// generated from the macro graph. ⚠ The error case (a macro reaching Stage 5 AS a compilation
    /// target) cannot arise until the expansion pass and a macro-call node exist; that is not this
    /// test.
    /// </summary>
    [Fact]
    public void Compile_FunctionGraphPlusMacroGraph_Succeeds_MacroNeverEmitted()
    {
        var functionGraph = MakeLiteralReturningGraph("Compute", GraphKind.Function, "424242");
        var macroGraph    = MakeLiteralReturningGraph("SomeMacro", GraphKind.Macro, "999999");

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "MacroLibraryAsset",
            Dispatch = BlueprintDispatchKind.Library,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new List<Graph> { functionGraph, macroGraph },
            Header = new Header(),
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var src = result.GeneratedSource!;
        Assert.Contains("Compute(", src);       // the Function graph's method (LibraryEmitter: plain graph.Name)
        Assert.DoesNotContain("SomeMacro", src); // the macro's name never appears anywhere in the output
        Assert.DoesNotContain("999999", src);    // nor does its body content
    }

    /// <summary>
    /// 3. ⭐ A macro is never tick-eligible. <c>InstanceEmitter</c> picks the tick graph as
    /// <c>Kind == IrGraphKind.Function &amp;&amp; Name == "Tick"</c>, else the first Function graph.
    /// A Macro graph named "Tick" produces no <see cref="IrGraph"/> at all (Stage 5 skips it), so it
    /// can never satisfy that lookup -- the ordinary Function graph must win the Tick body, even
    /// though the macro got there first in <c>asset.Graphs</c> and shares its name. This is asserted
    /// explicitly so a future refactor (e.g. widening the Kind filter) cannot quietly regress it.
    /// </summary>
    [Fact]
    public void Compile_MacroNamedTickPlusFunctionGraph_TickBodyComesFromFunctionGraph()
    {
        var macroNamedTick = MakeLiteralReturningGraph("Tick", GraphKind.Macro, "777777");
        var otherFunction  = MakeLiteralReturningGraph("Other", GraphKind.Function, "555555");

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "MacroTickAsset",
            Dispatch = BlueprintDispatchKind.Instance,
            Parameters = new(), WorkingState = new(), Variables = new(),
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            // Macro-named-"Tick" listed FIRST -- if Kind were ever ignored by the tick lookup, this
            // ordering is exactly what would let it win by accident.
            Graphs = new List<Graph> { macroNamedTick, otherFunction },
            Header = new Header(),
        };

        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var src = result.GeneratedSource!;

        // The macro's marker never appears anywhere -- it was never even lowered to IR.
        Assert.DoesNotContain("777777", src);

        // The ordinary Function graph's marker DOES appear -- and since it is the only Function
        // graph the compile produced, it can only have landed inside Tick()'s body (InstanceEmitter
        // emits a lone non-"Tick"-named Function graph as the Tick body itself, not as a separate
        // Func_ helper, when it is the ONLY Function graph in the asset).
        Assert.Contains("555555", src);
        Assert.Contains("public static void Tick(", src);
    }
}

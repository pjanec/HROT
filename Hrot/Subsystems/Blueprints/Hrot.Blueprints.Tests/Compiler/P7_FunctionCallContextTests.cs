using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
// Disambiguate: both Hrot.Blueprints.Core.Assets and Fdp.Toolkit.Blueprints define BlueprintDispatchKind.
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using Probes               = Hrot.Blueprints.Tests.Compiler.P7ProbeHelpers;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// P7 proof-test helpers. MUST be a top-level (non-nested) public static class: a nested type's
/// <c>Type.FullName</c> uses "+" for the nested separator (e.g.
/// <c>Outer+Inner</c>), which is valid for <see cref="System.Type.GetType(string)"/> reflection
/// lookups but NOT valid emitted C# source syntax for a <c>global::Outer+Inner.Method(...)</c>
/// call -- the real Roslyn compile in <see cref="P7_FunctionCallContextTests"/>'s E2E test would
/// fail with CS0119/CS0103. <see cref="FunctionCallNode.TargetTypeId"/> must resolve to an FQN
/// that is valid BOTH as a reflection lookup key AND as source-code syntax.
/// </summary>
public static class P7ProbeHelpers
{
    /// <summary>P7 -- trailing `Entity self` + `ISimulationView view` (both recognized).</summary>
    public static int Probe(int x, Fdp.Core.Entity self, Fdp.ModuleHost.Abstractions.ISimulationView view)
        => x + self.Index * 1000 + (view.IsAlive(self) ? 1 : 0);

    /// <summary>P7 -- trailing `Entity self` only (no view param).</summary>
    public static int ProbeSelfOnly(int x, Fdp.Core.Entity self) => x + self.Index;

    /// <summary>P7 -- trailing `ISimulationView` only (any param name; type alone recognizes it).</summary>
    public static int ProbeViewOnly(int x, Fdp.ModuleHost.Abstractions.ISimulationView simView)
        => x + (simView.Tick > 0 ? 1 : 0);

    /// <summary>
    /// P7 regression guard: trailing <c>Entity</c> param NOT named "self" must NOT be recognized as
    /// engine context -- it stays an ordinary wireable data pin (existing behavior).
    /// </summary>
    public static int ProbeEntityNotNamedSelf(int x, Fdp.Core.Entity target) => x + target.Index;

    /// <summary>
    /// P7 no-context regression helper. Deliberately NOT <c>System.Math.Abs</c> -- that method is
    /// overloaded (int/long/float/double/decimal/sbyte/short) and CLR reflection's
    /// <c>GetMethods().FirstOrDefault(m => m.Name == "Abs")</c> does not guarantee which overload
    /// is returned, which can silently pick a non-<c>int</c> overload and break an unrelated test
    /// with a type-coercion diagnostic. A single-overload helper is deterministic.
    /// </summary>
    public static int Identity(int x) => x;

    /// <summary>
    /// Impure VOID CLR helper -- used by <c>ImpureCallAndImplicitCastEmitTests</c> to lock the
    /// "void impure FunctionCall emits a bare statement" fix (no data-out pin => Stage5 must NOT
    /// synthesize an uncompilable <c>var __tN = VoidProbe(...)</c>).
    /// </summary>
    public static void VoidProbe(int x) { }
}

/// <summary>
/// P7 -- context-aware FunctionCall proof tests.
/// <para>
/// A blueprint FunctionCall to a C# helper whose signature ends with the recognized trailing
/// engine-context parameters (<c>Entity self</c> and/or a read-only <c>ISimulationView</c>) has
/// those trailing params auto-appended by the compiler and hidden from the visual data-IN pins.
/// See <c>Stage0_Rehydrate.EnrichClrFunctionCallPins</c> (pin omission),
/// <c>NodePinSchema.FunctionCallPins</c> (editor pin projection -- covered separately in
/// <c>NodePinSchemaEnrichmentTests</c>), and <c>Stage5_Schedule.ResolveFunctionCallTrailingContext</c>
/// / <c>StatementEmitter.AppendContextArgs</c> (emit-time append) for the implementation.
/// </para>
/// Covers, end-to-end through the real compiler pipeline:
/// <list type="bullet">
///   <item>Stage0 rehydration omits self/view from the node's Pins.</item>
///   <item>Stage5 IR carries <c>AppendSelfArg</c>/<c>AppendViewArg</c> and only the explicit
///     args in <c>IrOp_PureCall.Args</c>.</item>
///   <item>Full compile-and-run: the emitted C# appends <c>self</c>/<c>view</c>, and the helper
///     receives the REAL entity + a real read-only view at runtime.</item>
///   <item>Regression: a helper with no trailing context still gets an unmodified call.</item>
/// </list>
/// </summary>
public sealed class P7_FunctionCallContextTests
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
    /// Builds an Instance-dispatch asset with a single "Tick" Function graph:
    /// <c>EventEntry --exec--&gt; SetVariable(Result) --exec--&gt; Return</c>, with
    /// <c>Result</c> data-wired from a pure FunctionCallNode targeting
    /// <paramref name="targetTypeId"/>.<paramref name="methodName"/>, itself data-wired from a
    /// Literal(<paramref name="xValue"/>).
    /// <para>
    /// The FunctionCallNode's <c>Pins</c> list is left EMPTY so <see cref="Stage0_Rehydrate"/>
    /// rehydrates it via CLR reflection -- exactly the real-world "loaded from .bp.json" path
    /// (Pins are never persisted). This is deliberate: it proves the omission/append behavior
    /// through the actual pin-rehydration pass, not a hand-authored shortcut.
    /// </para>
    /// </summary>
    private static (BlueprintAsset asset, Guid callNodeId, Guid resultVarId) BuildProbeAsset(
        string targetTypeId, string methodName, int xValue)
    {
        var assetId     = Guid.NewGuid();
        var resultVarId = Guid.NewGuid();
        var graphId     = Guid.NewGuid();

        var entryId  = Guid.NewGuid();
        var litId    = Guid.NewGuid();
        var callId   = Guid.NewGuid();
        var setVarId = Guid.NewGuid();
        var returnId = Guid.NewGuid();

        var entryExecOut = Guid.NewGuid();
        var litOut        = Guid.NewGuid();
        // Placeholder link-pin GUIDs for the FunctionCallNode's (not-yet-hydrated) In/Out pins --
        // Stage0's AssignLinkGuids binds these positionally to the rehydrated pin list (mirrors
        // Stage0_RehydrateTests.BuildTwoNodeAsset's "authored link-pin GUID" convention).
        var callXIn       = Guid.NewGuid();
        var callReturnOut = Guid.NewGuid();
        var setExecIn  = Guid.NewGuid();
        var setExecOut = Guid.NewGuid();
        var setValueIn = Guid.NewGuid();
        var retExecIn  = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "Tick",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryExecOut, Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id        = litId,
                    TypeId    = "System.Int32",
                    ValueJson = xValue.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = litOut, Name = "value", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                // Pins EMPTY -- Stage0_Rehydrate resolves this via CLR reflection (the real path).
                new FunctionCallNode
                {
                    Id            = callId,
                    TargetTypeId  = targetTypeId,
                    MethodName    = methodName,
                    IsPure        = true,
                    TargetGraphId = "",
                    Pins          = new List<Pin>(),
                },
                new SetVariableNode
                {
                    Id         = setVarId,
                    VariableId = resultVarId.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = setExecIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new() { Id = setExecOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new() { Id = setValueIn, Name = "value",   Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id   = returnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId,  FromPinId = entryExecOut, ToNodeId = setVarId, ToPinId = setExecIn  },
                new() { FromNodeId = setVarId, FromPinId = setExecOut,   ToNodeId = returnId,  ToPinId = retExecIn },
                new() { FromNodeId = litId,    FromPinId = litOut,       ToNodeId = callId,    ToPinId = callXIn },
                new() { FromNodeId = callId,   FromPinId = callReturnOut, ToNodeId = setVarId, ToPinId = setValueIn },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "P7ProbeTest",
            Dispatch         = BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new List<VariableDecl>
            {
                new() { Id = resultVarId, Name = "Result", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new List<Graph> { graph },
            Header           = new Header(),
        };

        return (asset, callId, resultVarId);
    }

    // -----------------------------------------------------------------------
    // Test 1: Stage0 rehydration omits self/view from the node's Pins
    // -----------------------------------------------------------------------

    [Fact]
    public void Stage0_ProbeFunctionCall_RehydratesOnlyXPin_SelfAndViewOmitted()
    {
        var (asset, callId, _) = BuildProbeAsset(
            typeof(Probes).FullName!, nameof(Probes.Probe), xValue: 7);

        Stage0_Rehydrate.Run(asset, DefaultOptions());

        var callNode = asset.Graphs[0].Nodes.Single(n => n.Id == callId);
        var dataIn  = callNode.Pins.Where(p => !p.IsExec && p.Direction == "In").ToList();
        var dataOut = callNode.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();

        var single = Assert.Single(dataIn);
        Assert.Equal("x", single.Name);
        Assert.Equal("System.Int32", single.TypeRef?.TypeId);

        var ret = Assert.Single(dataOut);
        Assert.Equal("Return", ret.Name);
    }

    // -----------------------------------------------------------------------
    // Test 2: Stage5 IR appends self/view without adding them to Args
    // -----------------------------------------------------------------------

    [Fact]
    public void Stage5_ProbeFunctionCall_EmitsPureCall_AppendsSelfAndView_ArgsCountIsOne()
    {
        var (asset, callId, _) = BuildProbeAsset(
            typeof(Probes).FullName!, nameof(Probes.Probe), xValue: 7);

        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);

        Stage0_Rehydrate.Run(asset, opts);
        Stage2_Validate.Run(asset, ctx);
        var norm  = Stage3_Normalize.Run(asset, ctx);
        var typed = Stage4_TypeResolve.Run(norm, ctx);
        var ir    = Stage5_Schedule.Run(typed, ctx);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Code}: {d.Message}"))}");

        var tickIrGraph = ir.Graphs.First(g => g.Name == "Tick");
        var pureCallStmt = tickIrGraph.Blocks
            .SelectMany(b => b.Statements)
            .FirstOrDefault(s => s.Operation is IrOp_PureCall);
        Assert.NotNull(pureCallStmt);

        var op = (IrOp_PureCall)pureCallStmt!.Operation;
        Assert.True(op.AppendSelfArg, "Probe(int,Entity,ISimulationView) must set AppendSelfArg.");
        Assert.True(op.AppendViewArg, "Probe(int,Entity,ISimulationView) must set AppendViewArg.");
        Assert.Single(op.Args); // only "x" -- self/view are NOT in Args (appended at emit time).
        Assert.Contains(typeof(Probes).FullName!, op.MethodFqn);
    }

    // -----------------------------------------------------------------------
    // Test 3: full compile-and-run -- the helper receives the REAL self/view
    // -----------------------------------------------------------------------

    [Fact]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void E2E_ProbeFunctionCall_CompileAndRun_ReceivesRealSelfAndView()
        => E2E_ProbeFunctionCall_CompileAndRun_Body();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void E2E_ProbeFunctionCall_CompileAndRun_Body()
    {
        const int x = 7;
        var (asset, _, resultVarId) = BuildProbeAsset(
            typeof(Probes).FullName!, nameof(Probes.Probe), xValue: x);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var stateView = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(stateView);
        Assert.True(stateView.HasValue, "Expected a valid BlueprintStateView.");

        bool found = stateView.Value.TryGetField<int>("Result", out var result);
        Assert.True(found, "State field 'Result' not found in blueprint state.");

        // Probe(x, self, view) => x + self.Index * 1000 + (view.IsAlive(self) ? 1 : 0).
        // A freshly created + attached entity is alive, so this proves BOTH self (its real Index)
        // and view (a real ISimulationView whose IsAlive(self) call succeeds) were threaded
        // through correctly -- not defaults, not swapped, not omitted.
        var expected = x + entity.Index * 1000 + 1;
        Assert.Equal(expected, result);
    }

    // -----------------------------------------------------------------------
    // Test 4: regression -- a helper with NO trailing context is unaffected
    // -----------------------------------------------------------------------

    [Fact]
    public void Stage5_NoContextFunctionCall_Identity_AppendSelfAndViewBothFalse_NoRegression()
    {
        var (asset, _, _) = BuildProbeAsset(
            typeof(Probes).FullName!, nameof(Probes.Identity), xValue: 7);

        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);

        Stage0_Rehydrate.Run(asset, opts);
        Stage2_Validate.Run(asset, ctx);
        var norm  = Stage3_Normalize.Run(asset, ctx);
        var typed = Stage4_TypeResolve.Run(norm, ctx);
        var ir    = Stage5_Schedule.Run(typed, ctx);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Code}: {d.Message}"))}");

        var tickIrGraph = ir.Graphs.First(g => g.Name == "Tick");
        var pureCallStmt = tickIrGraph.Blocks
            .SelectMany(b => b.Statements)
            .FirstOrDefault(s => s.Operation is IrOp_PureCall);
        Assert.NotNull(pureCallStmt);

        var op = (IrOp_PureCall)pureCallStmt!.Operation;
        Assert.False(op.AppendSelfArg);
        Assert.False(op.AppendViewArg);
        Assert.Single(op.Args); // just "x" -- Identity(int) has exactly one parameter.
    }

    [Fact]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void E2E_NoContextFunctionCall_Identity_CompileAndRun_NoRegression()
        => E2E_NoContextFunctionCall_Identity_CompileAndRun_Body();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void E2E_NoContextFunctionCall_Identity_CompileAndRun_Body()
    {
        var (asset, _, resultVarId) = BuildProbeAsset(
            typeof(Probes).FullName!, nameof(Probes.Identity), xValue: 7);

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var stateView = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(stateView);
        Assert.True(stateView.HasValue);

        bool found = stateView.Value.TryGetField<int>("Result", out var result);
        Assert.True(found);
        Assert.Equal(7, result); // Identity(7) == 7 -- unaffected by the P7 context machinery.
    }
}

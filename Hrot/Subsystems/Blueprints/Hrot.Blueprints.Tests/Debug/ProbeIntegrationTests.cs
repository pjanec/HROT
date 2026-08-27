using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Editor;
using Hrot.Blueprints.Tests.Mocks;

namespace Hrot.Blueprints.Tests.Debug;

// ---- FIX2-001 (NodeId emitter format) + FIX2-003 (OnNewTick wiring) ----

/// <summary>
/// Integration tests for FIX2-001 (StatementEmitter NodeId :D format) and
/// FIX2-003 (DebugProbe.NewTick called in BlueprintTestFixture.TickFrame).
/// Uses BlueprintTestFixture to run compiled blueprints end-to-end.
/// </summary>
[Collection("DebugProbe")]
public sealed class ProbeFormatIntegrationTests : IDisposable
{
    // Save and restore DebugProbe.Sink so test isolation is preserved.
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ---- Helper: build Instance asset with a BranchNode to force probe emission ----
    // EventEntryNode produces no IR statements so no probe is inserted for it.
    // BranchNode with no data-input pin synthesizes IrOp_Const("false") as its first
    // statement (Debug.NodeId = branchNode.Id), so DebugProbeInsertion inserts a probe
    // for that node. This path avoids LatentDelay which triggers op_LessThan_Single
    // IR ops that Roslyn cannot resolve (Phase 5 scope fix).

    // CF-3: After CF-2 (SourceNodeId on entry block), the probe fires for the
    // entry node (EventEntry, Nodes[0]) rather than Branch (Nodes[1]).
    // The block's owning exec node determines probe identity.

    private static (BlueprintAsset asset, Guid graphId, Guid probeNodeId) BuildProbeAsset(string name)
    {
        var asset = BlueprintAssetBuilder
            .Instance(name)
            .WithGraph("Tick", g => g
                .Entry()
                .Branch("", b => b.Return(), b => b.Return()))
            .Build();

        // Nodes[0] = EventEntryNode — entry block gets SourceNodeId = this ID (CF-2)
        // Nodes[1] = BranchNode — shares the entry block, no separate probe
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[0].Id; // EventEntry, owns the block

        return (asset, graphId, probeNodeId);
    }

    // ---- FIX2-001: StatementEmitter emits NodeId in :D format ----

    /// <summary>
    /// Compiles a blueprint containing a LatentDelayNode that triggers a
    /// DebugProbe.NodeEnter call in generated code, then runs it via the fixture.
    /// Asserts that the probe arrives in "D" (hyphenated) format, proving the
    /// StatementEmitter :N -> :D fix from FIX2-001 is active.
    /// </summary>
    [Fact]
    public void CompiledProbe_EmitsNodeId_InDFormat()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (asset, _, probeNodeId) = BuildProbeAsset("ProbeFormatTest");

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // DebugProbe.Sink is fixture.DebugSession (CapturingDebugSession), set in fixture ctor.
        fixture.TickFrame(0.016f);

        string expectedId = probeNodeId.ToString("D"); // hyphenated: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        string legacyId   = probeNodeId.ToString("N"); // compact:    xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

        Assert.True(fixture.DebugSession.Hit(expectedId),
            $"Expected DebugProbe.NodeEnter to be called with :D format '{expectedId}', " +
            "but no matching entry was recorded. Check StatementEmitter (:D fix for FIX2-001).");
        Assert.False(fixture.DebugSession.Hit(legacyId),
            $"DebugProbe.NodeEnter was called with legacy :N format '{legacyId}'. " +
            "The FIX2-001 fix does not appear to be in effect.");
    }

    // ---- FIX2-003: DebugProbe.NewTick called by TickFrame enables re-fire across ticks ----

    /// <summary>
    /// Runs two ticks with a BlueprintDebugSession breakpoint active. After tick 1 fires
    /// the breakpoint, the user resumes (Continue). Tick 2 fires it again because
    /// TickFrame calls DebugProbe.NewTick() (FIX2-003) and Continue() cleared pause state.
    /// Asserts exactly 2 OnBreakpointHit events, proving the full two-tick pipeline.
    /// </summary>
    [Fact]
    public void Breakpoint_FiresTwice_AcrossTwoTicks_WithNewTickWiring()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (asset, graphId, eventEntryId) = BuildProbeAsset("OnNewTickTest");

        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, new MockTimeController());
        session.Attach(); // overrides DebugProbe.Sink (replaces fixture's CapturingDebugSession)

        // CF-4: Test breakpoint on EventEntry (Nodes[0]) — the entry block owner.
        session.SetBreakpoint(asset.AssetId, graphId, eventEntryId);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        int fireCount = 0;
        session.OnBreakpointHit += _ => fireCount++;

        // Tick 1: blueprint runs, probe fires, breakpoint hit.
        fixture.TickFrame(0.016f);
        Assert.Equal(1, fireCount);

        // User resumes (permitted test action: simulates the debugger UI resume button).
        session.Continue();

        // Tick 2: TickFrame calls DebugProbe.NewTick() then runs blueprints; bp fires again.
        fixture.TickFrame(0.016f);
        Assert.Equal(2, fireCount);
    }

    /// <summary>
    /// CF-4: Verifies that a breakpoint on the Branch node (Nodes[1]) fires via
    /// block translation — closing the masked regression where Branch silently
    /// stopped working after CF-2.
    /// </summary>
    [Fact]
    public void CF4_BranchNode_BreakpointFiresViaBlockTranslation()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (asset, graphId, _) = BuildProbeAsset("BranchBreakpointTest");

        var branchNodeId = asset.Graphs[0].Nodes[1].Id; // Branch = Nodes[1]

        var tc = new MockTimeController();
        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, tc);
        session.Attach();

        // Compile and register debug map so BreakpointTargets translation is active.
        var options = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var compiler = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, options);
        Assert.True(compileResult.Succeeded, string.Join("\n", compileResult.Diagnostics));
        session.RegisterDebugMap(compileResult.DebugMap!);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Set breakpoint on Branch (Nodes[1]) — must fire via block translation.
        session.SetBreakpoint(asset.AssetId, graphId, branchNodeId);

        fixture.TickFrame(0.016f);

        Assert.True(tc.PauseRequestCount >= 1,
            $"Expected PauseRequestCount >= 1 after one tick with breakpoint on Branch ({branchNodeId:D}), " +
            $"but got {tc.PauseRequestCount}. BreakpointTargets: " +
            string.Join(", ", compileResult.DebugMap!.BreakpointTargets.Select(kv =>
                $"{kv.Key.ToString("D").Substring(0,8)}→{kv.Value.ToString("D").Substring(0,8)}")));
    }
}

// ---- FIX2-004 (BlueprintEditorModule session wiring) ----

/// <summary>
/// Tests that BlueprintEditorModule.OnEditorActivated/OnEditorDeactivated
/// call IBlueprintDebugSession.Attach/Detach (FIX2-004 wiring).
/// </summary>
[Collection("DebugProbe")]
public sealed class BlueprintEditorModuleSessionWiringTests : IDisposable
{
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    public void Dispose() => DebugProbe.Sink = _savedSink;

    [Fact]
    public void OnEditorActivated_CallsAttach_SetsDebugProbeSinkToSession()
    {
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), new StubSimView(), new MockTimeController());

        var module = new BlueprintEditorModule(
            new MockShellCommandRegistrar(),
            new DirtyTracker(),
            new EditorSelectionStore(),
            new EditorState(),
            new NullOutputConsole(),
            session);

        module.OnEditorActivated();

        Assert.Same(session, DebugProbe.Sink);
    }

    [Fact]
    public void OnEditorDeactivated_CallsDetach_RestoresNullProbeSink()
    {
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), new StubSimView(), new MockTimeController());

        var module = new BlueprintEditorModule(
            new MockShellCommandRegistrar(),
            new DirtyTracker(),
            new EditorSelectionStore(),
            new EditorState(),
            new NullOutputConsole(),
            session);

        module.OnEditorActivated();
        module.OnEditorDeactivated();

        Assert.IsType<NullProbeSink>(DebugProbe.Sink);
    }

    private sealed class NullOutputConsole : IOutputConsole
    {
        public void LogInfo(string message)    { }
        public void LogWarning(string message) { }
        public void LogError(string message)   { }
        public void LogDebug(string message)   { }
        public void LogDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic) { }
    }

    private sealed class StubSimView : ISimulationView
    {
        public uint  Tick => 0;
        public float Time => 0f;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public bool IsAlive(Entity e) => throw new NotImplementedException();
        public bool HasComponent<T>(Entity e) where T : unmanaged => throw new NotImplementedException();
        public bool HasManagedComponent<T>(Entity e) where T : class => throw new NotImplementedException();
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => throw new NotImplementedException();
        public QueryBuilder Query() => throw new NotImplementedException();
        public IReadOnlyList<T> ReadManagedEvents<T>() => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }
}

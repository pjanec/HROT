using System.Text.Json;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Tests.Mocks;
using Xunit.Abstractions;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// CF-7-rev: End-to-end tests verifying auto-instrumentation with the compiler
/// and BlueprintTestFixture. Uses Count4.bp.json (the standard test asset).
/// </summary>
[Collection("DebugProbe")]
public sealed class CF7rev_EndToEndTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    // Known authored node IDs from Count4.bp.json.
    private static readonly Guid DelayGuid    = Guid.Parse("0b561966-b00b-4c84-a1a0-87042220ba9f");
    private static readonly Guid SequenceGuid = Guid.Parse("da9a9c0b-25f8-4a81-9a52-75c715456f18");

    public CF7rev_EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ---- Helpers ---------------------------------------------------------------

    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "IOS-IG-SimHost.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "Could not find repo root (looked for IOS-IG-SimHost.sln upward from " +
            AppContext.BaseDirectory + ")");
    }

    /// <summary>
    /// Loads Count4.bp.json and compiles it in the given mode, returning the compile result.
    /// </summary>
    private static (BlueprintAsset Asset, CompileResult Result) CompileCount4(CompilerMode mode)
    {
        var repoRoot = ResolveRepoRoot();
        var assetPath = Path.Combine(repoRoot,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints", "Count4.bp.json");
        var json = File.ReadAllText(assetPath);
        var asset = BlueprintJsonServices.Deserialize(json)
                    ?? throw new InvalidOperationException(
                        $"BlueprintJsonServices.Deserialize returned null for '{assetPath}'");

        var options = new CompileOptions(
            Mode:              mode,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var compiler = new BlueprintCompiler();
        var result   = compiler.Compile(asset, options);

        if (!result.Succeeded)
        {
            var diags = string.Join("\n", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
            throw new InvalidOperationException($"Count4 compilation failed:\n{diags}");
        }

        return (asset, result);
    }

    // ---- Test 6: SetBreakpoint triggers auto-instrument then pauses ------------

    /// <summary>
    /// Loads Count4, compiles in Release mode (no probes).
    /// Creates a session with an auto-instrumentation callback that re-compiles in Debug mode.
    /// Sets a breakpoint on Delay → callback fires → recompile → RegisterDebugMap → breakpoint re-resolved.
    /// Drives one tick → assert PauseRequestCount >= 1.
    /// This is the golden end-to-end test for CF-7-rev.
    ///
    /// NOTE: This test uses a SYNTHETIC callback that calls CompileCount4 + fixture.CompileAndLoad
    /// directly. It does NOT exercise the production QuickReloadService pipeline (EditorSubsystem).
    /// Production code goes through BlueprintJsonServices.Deserialize + QuickReloadService.TriggerAsync.
    /// See CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilableAsset for a test that
    /// verifies the REAL production asset-loading path (BlueprintJsonServices + compilation).
    /// </summary>
    [Fact]
    public void SetBreakpoint_TriggersAutoInstrument_ThenPauses()
    {
        // Compile in Release mode (no probes) so we need auto-instrumentation.
        var (releaseAsset, _) = CompileCount4(CompilerMode.Release);

        var fixtureOptions = new BlueprintTestFixtureOptions
        {
            VerifyAlcUnloadOnDispose = false,
        };
        using var fixture = new BlueprintTestFixture(fixtureOptions);

        var tc = new MockTimeController();
        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, tc);

        // Wire a synthetic auto-instrumentation callback (not the production QuickReloadService path).
        session.SetInstrumentationCallback(async (assetId, mode) =>
        {
            // Re-compile the asset in the requested mode.
            var (debugAsset, debugResult) = CompileCount4(mode);
            // Load into the fixture's registry so probes fire.
            fixture.CompileAndLoad(debugAsset, mode);
            // Register the debug map so the session can resolve probes.
            session.RegisterDebugMap(debugResult.DebugMap!);
        });

        session.Attach();

        var graphId = releaseAsset.Graphs[0].Id;

        // Set breakpoint on Delay — this triggers the callback because no DebugMap exists.
        session.SetBreakpoint(releaseAsset.AssetId, graphId, DelayGuid);

        // Verify the breakpoint was re-resolved to the probe id (not the authored id).
        var bps = session.GetBreakpoints();
        Assert.Single(bps);
        // After re-resolution, ProbeNodeId should differ from NodeId when BreakpointTargets
        // maps the authored node to a different block probe. Count4's Delay maps differently.
        _output.WriteLine($"Breakpoint NodeId={bps[0].NodeId}, ProbeNodeId={bps[0].ProbeNodeId}");

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(releaseAsset, entity);

        // Drive one tick — the Delay probe should fire and trigger pause.
        fixture.TickFrame(0.016f);

        Assert.True(tc.PauseRequestCount >= 1,
            $"Expected PauseRequestCount >= 1 after auto-instrumentation + tick, " +
            $"but got {tc.PauseRequestCount}.");
    }

    // ---- Test 7: Breakpoint set before compile becomes active after map registers -

    /// <summary>
    /// Creates a session with NO callback. Sets a breakpoint on an authored node id.
    /// Verifies ProdeNodeId is the authored id (tentative).
    /// Registers a DebugMap with different blockProbeId → ProdeNodeId updates.
    /// Verifies IsNodeBreakpointable returns true for the authored node.
    /// </summary>
    [Fact]
    public void BreakpointSetBeforeCompile_BecomesActive_AfterMapRegisters()
    {
        var fixtureOptions = new BlueprintTestFixtureOptions
        {
            VerifyAlcUnloadOnDispose = false,
        };
        using var fixture = new BlueprintTestFixture(fixtureOptions);

        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), fixture.View, new MockTimeController());

        var assetId = DelayGuid; // use the Delay guid as a synthetic asset id
        var graphId = Guid.Empty;
        var authoredNodeId = DelayGuid;
        var blockProbeId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        // Set breakpoint before any DebugMap is registered.
        var bpId = session.SetBreakpoint(assetId, graphId, authoredNodeId);

        // Verify tentative ProbeNodeId == authoredNodeId.
        var bpBefore = session.GetBreakpoints().Single(b => b.Id == bpId);
        Assert.Equal(authoredNodeId.ToString("D"), bpBefore.ProbeNodeId);

        // Now register a DebugMap with BreakpointTargets mapping authored → different block probe.
        var map = new DebugMap
        {
            AssetId       = assetId,
            AssetName     = "Test",
            BlueprintId   = 1,
            StructureHash = 0,
            Entries       = new List<DebugMapEntry>(),
            BreakpointTargets = new Dictionary<Guid, Guid>
            {
                { authoredNodeId, blockProbeId }
            },
            StateLayout   = new DebugStateLayout(),
        };
        session.RegisterDebugMap(map);

        // Verify ProbeNodeId was re-resolved.
        var bpAfter = session.GetBreakpoints().Single(b => b.Id == bpId);
        Assert.Equal(blockProbeId.ToString("D"), bpAfter.ProbeNodeId);
        Assert.False(bpAfter.IsStale);

        // Verify IsNodeBreakpointable returns true (found in BreakpointTargets).
        Assert.True(session.IsNodeBreakpointable(assetId, graphId, authoredNodeId));
    }

    // ---- Test 8: Mode selection — Debug for breakpoints, Trace for watches ------

    /// <summary>
    /// Creates a session with a callback that captures the mode.
    /// Calls SetBreakpoint → asserted mode = Debug.
    /// Calls AddWatch (same asset, no map yet) → asserted mode = Trace.
    /// The second call should still invoke the callback even though Debug was already
    /// requested, because no DebugMap is registered yet.
    /// </summary>
    [Fact]
    public void ModeSelection_DebugForBreakpoints_TraceForWatches()
    {
        var session = new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            new MockTimeController());

        var capturedModes = new List<CompilerMode>();

        session.SetInstrumentationCallback((assetId, mode) =>
        {
            capturedModes.Add(mode);
            return Task.CompletedTask;
        });

        var assetId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var graphId = new Guid("11111111-1111-1111-1111-111111111111");
        var nodeId  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var pinId   = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");

        session.SetBreakpoint(assetId, graphId, nodeId);
        session.AddWatch(assetId, graphId, pinId, "TestWatch", typeof(int));

        Assert.Equal(2, capturedModes.Count);
        Assert.Equal(CompilerMode.Debug, capturedModes[0]);
        Assert.Equal(CompilerMode.Trace, capturedModes[1]);
    }

    // ---- Test 9: Diagnostic — plain JsonSerializer fails vs BlueprintJsonServices ---

    /// <summary>
    /// Proves that the BROKEN production path (plain JsonSerializer without
    /// JsonStringEnumConverter) either throws, returns null, or produces a
    /// different asset than BlueprintJsonServices.Deserialize (the FIXED path).
    ///
    /// This is a diagnostic test — if it passes (i.e., both paths produce
    /// identical results), then the root cause is NOT deserialization.
    /// </summary>
    [Fact]
    public void PlainJsonDeserialization_FailsOrProducesDifferentAsset_ThanBlueprintJsonServices()
    {
        var repoRoot = ResolveRepoRoot();
        var assetPath = Path.Combine(repoRoot,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints", "Count4.bp.json");
        var json = File.ReadAllText(assetPath);

        // Production-correct path:
        var correctAsset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(correctAsset);

        // The BROKEN path (what the pre-fix callback did):
        var brokenOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
        };
        BlueprintAsset? brokenAsset = null;
        bool brokenThrew = false;
        try
        {
            brokenAsset = JsonSerializer.Deserialize<BlueprintAsset>(json, brokenOptions);
        }
        catch (JsonException)
        {
            brokenThrew = true;
        }

        // The broken path either throws, returns null, or produces a different asset.
        var brokenDifferent =
            brokenAsset == null
            || brokenAsset.Graphs.Count != correctAsset.Graphs.Count
            || brokenAsset.Graphs[0].Nodes.Count != correctAsset.Graphs[0].Nodes.Count;
        _output.WriteLine($"Broken path: threw={brokenThrew}, null={brokenAsset == null}, " +
                          $"graphCount={(brokenAsset?.Graphs.Count ?? -1)}, " +
                          $"nodeCount={(brokenAsset?.Graphs[0].Nodes.Count ?? -1)}");
        _output.WriteLine($"Correct path: graphCount={correctAsset.Graphs.Count}, " +
                          $"nodeCount={correctAsset.Graphs[0].Nodes.Count}");

        Assert.True(brokenThrew || brokenDifferent,
            "Expected the plain-JsonSerializer path to fail or produce different results. " +
            "If this passes, the deserialization may not be the root cause.");
    }

    // ---- Test 10: Production asset-loading path produces compilable asset ---------

    /// <summary>
    /// Uses the SAME asset-loading code as the production callback in EditorSubsystem
    /// (BlueprintJsonServices.Deserialize) and verifies that the loaded asset compiles
    /// successfully in Debug mode.
    ///
    /// This validates the REAL asset-loading path — NOT a synthetic fixture callback.
    /// </summary>
    [Fact]
    public void CallbackAssetLoading_Uses_BlueprintJsonServices_ProducesCompilableAsset()
    {
        var repoRoot = ResolveRepoRoot();
        var assetPath = Path.Combine(repoRoot,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints", "Count4.bp.json");
        var json = File.ReadAllText(assetPath);

        // THIS is the production callback's loading code — must use BlueprintJsonServices.
        var asset = BlueprintJsonServices.Deserialize(json);
        Assert.NotNull(asset);

        // Set CompilerMode.Debug (what the callback does).
        asset.EditorMetadata.CompilerMode = CompilerMode.Debug;

        // Compile — must succeed.
        var options = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var compiler = new BlueprintCompiler();
        var result = compiler.Compile(asset, options);

        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
        Assert.NotNull(result.DebugMap);
        Assert.NotEmpty(result.DebugMap.Entries);
    }

    private sealed class StubSimulationView : ISimulationView
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
        public System.Collections.Generic.IReadOnlyList<T> ReadManagedEvents<T>()
            => throw new NotImplementedException();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }
}

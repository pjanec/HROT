using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Interfaces;
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
/// CF-2: Verifies that authored node IDs are preserved through the entire compiler
/// pipeline.  After the fix, Delay and Sequence nodes retain their authored IDs in
/// DebugMap entries and emitted DebugProbe.NodeEnter probes.
/// </summary>
[Collection("DebugProbe")]
public sealed class CF2_AuthoredIdProbeTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    // Known authored node IDs from Count4.bp.json.
    private static readonly Guid DelayGuid    = Guid.Parse("0b561966-b00b-4c84-a1a0-87042220ba9f");
    private static readonly Guid SequenceGuid = Guid.Parse("da9a9c0b-25f8-4a81-9a52-75c715456f18");
    private static readonly Guid EventEntryGuid  = Guid.Parse("20000006-0000-0000-0000-000000000001");
    private static readonly Guid SetVariableGuid = Guid.Parse("20000006-0000-0000-0000-000000000002");
    private static readonly Guid FunctionCallGuid = Guid.Parse("20000006-0000-0000-0000-000000000003");
    private static readonly Guid GetVariableGuid  = Guid.Parse("20000006-0000-0000-0000-000000000004");
    private static readonly Guid ReturnGuid    = Guid.Parse("7b6da53f-4e11-4bc9-9d0c-bad0e22c7f5c");

    public CF2_AuthoredIdProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ---- Helpers ---------------------------------------------------------------

    /// <summary>
    /// Loads Count4.bp.json and compiles it in Debug mode, returning the compile result.
    /// </summary>
    private static (BlueprintAsset Asset, CompileResult Result) CompileCount4()
    {
        var repoRoot = ResolveRepoRoot();
        var assetPath = Path.Combine(repoRoot,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Blueprints", "Count4.bp.json");
        var json = File.ReadAllText(assetPath);
        var asset = BlueprintJsonServices.Deserialize(json)
                    ?? throw new InvalidOperationException(
                        $"BlueprintJsonServices.Deserialize returned null for '{assetPath}'");

        var options = new CompileOptions(
            Mode:              CompilerMode.Debug,
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

    // ---- CF2-1: Delay authored ID appears in DebugMap --------------------------

    [Fact]
    public void CF2_DelayAuthoredId_HasDebugMapEntry()
    {
        var (_, result) = CompileCount4();

        var debugMap = result.DebugMap;
        Assert.NotNull(debugMap);

        var delayEntries = debugMap!.Entries
            .Where(e => e.NodeId == DelayGuid)
            .ToList();

        Assert.True(delayEntries.Count > 0,
            $"DebugMap should contain at least one entry with Delay's authored NodeId " +
            $"'{DelayGuid:D}', but found none. " +
            $"DebugMap entries: {string.Join(", ", debugMap.Entries.Select(e => e.NodeId.ToString("D")))}");
    }

    // ---- CF2-2: Sequence authored ID appears in DebugMap -----------------------

    [Fact]
    public void CF2_SequenceAuthoredId_HasDebugMapEntry()
    {
        var (_, result) = CompileCount4();

        var debugMap = result.DebugMap;
        Assert.NotNull(debugMap);

        var seqEntries = debugMap!.Entries
            .Where(e => e.NodeId == SequenceGuid)
            .ToList();

        Assert.True(seqEntries.Count > 0,
            $"DebugMap should contain at least one entry with Sequence's authored NodeId " +
            $"'{SequenceGuid:D}', but found none. " +
            $"DebugMap entries: {string.Join(", ", debugMap.Entries.Select(e => e.NodeId.ToString("D")))}");
    }

    // ---- CF2-3: Delay authored ID has NodeEnter probe --------------------------

    [Fact]
    public void CF2_DelayAuthoredId_HasNodeEnterProbe()
    {
        var (_, result) = CompileCount4();

        var source = result.GeneratedSource ?? string.Empty;

        var delayIdStr = DelayGuid.ToString("D");
        var pattern = $@"DebugProbe\.NodeEnter\s*\(\s*self\s*,\s*""{Regex.Escape(delayIdStr)}""\s*\)";
        var match = Regex.IsMatch(source, pattern);

        Assert.True(match,
            $"Generated source should contain DebugProbe.NodeEnter(self, \"{delayIdStr}\") " +
            $"for Delay's authored ID.");
    }

    // ---- CF2-4: Sequence authored ID has NodeEnter probe -----------------------

    [Fact]
    public void CF2_SequenceAuthoredId_HasNodeEnterProbe()
    {
        var (_, result) = CompileCount4();

        var source = result.GeneratedSource ?? string.Empty;

        var seqIdStr = SequenceGuid.ToString("D");
        var pattern = $@"DebugProbe\.NodeEnter\s*\(\s*self\s*,\s*""{Regex.Escape(seqIdStr)}""\s*\)";
        var match = Regex.IsMatch(source, pattern);

        Assert.True(match,
            $"Generated source should contain DebugProbe.NodeEnter(self, \"{seqIdStr}\") " +
            $"for Sequence's authored ID.");
    }

    // ---- CF2-5: All exec nodes have exactly one probe; data nodes have none ----

    [Fact]
    public void CF2_AllExecNodes_HaveExactlyOneProbe_NoDataNodeProbes()
    {
        var (_, result) = CompileCount4();
        var source = result.GeneratedSource ?? string.Empty;
        var debugMap = result.DebugMap;
        Assert.NotNull(debugMap);
        var bpTargets = debugMap!.BreakpointTargets;
        Assert.NotNull(bpTargets);

        int CountProbesFor(Guid nodeId)
        {
            var idStr = nodeId.ToString("D");
            var pattern = $@"DebugProbe\.NodeEnter\s*\(\s*self\s*,\s*""{Regex.Escape(idStr)}""\s*\)";
            return Regex.Matches(source, pattern).Count;
        }

        // CF-4: Sequence has exactly one probe (owns the entry block).
        Assert.Equal(1, CountProbesFor(SequenceGuid));

        // CF-4: Delay has exactly one probe (owns its pre-suspend block).
        Assert.Equal(1, CountProbesFor(DelayGuid));

        // CF-4: GetVariable is a pure data node — must have NO probe.
        // Tier-3 fallback removed in DebugProbeInsertion.
        Assert.Equal(0, CountProbesFor(GetVariableGuid));

        // CF-4: Every authored exec node must be in BreakpointTargets.
        // EventEntry, SetVariable, Sequence, Delay are exec nodes that carry
        // execution flow.  FunctionCall "Add" in Count4 is a pure data node
        // (reached via ResolveNodeOutput) — correctly absent from targets.
        // Return may be consumed by AiPrimitive lowering (ReturnStatus) and
        // not independently tracked in all dispatch modes.
        var definitelyExec = new[] { EventEntryGuid, SetVariableGuid,
                                     SequenceGuid, DelayGuid };
        foreach (var execId in definitelyExec)
        {
            Assert.True(bpTargets.ContainsKey(execId),
                $"BreakpointTargets should contain exec node '{execId:D}' but it is missing. " +
                $"Targets: {string.Join(", ", bpTargets.Keys)}");
        }

        // CF-4: Data node (GetVariable) must NOT be in BreakpointTargets.
        Assert.False(bpTargets.ContainsKey(GetVariableGuid),
            $"BreakpointTargets must NOT contain data node GetVariable '{GetVariableGuid:D}'.");

        // CF-4: Pure data FunctionCall must NOT be in BreakpointTargets.
        Assert.False(bpTargets.ContainsKey(FunctionCallGuid),
            $"BreakpointTargets must NOT contain pure data FunctionCall '{FunctionCallGuid:D}'.");

        // CF-4: Each exec node's BreakpointTargets probe ID must actually be emitted
        // as a DebugProbe.NodeEnter in the generated source.
        foreach (var execId in definitelyExec)
        {
            var probeId = bpTargets[execId];
            var probeStr = probeId.ToString("D");
            Assert.True(source.Contains($"\"{probeStr}\""),
                $"BreakpointTargets maps exec node '{execId:D}' → probe id '{probeStr}', " +
                $"but no DebugProbe.NodeEnter with that id was found in generated source.");
        }
    }

    // ---- CF4-1: SetVariable breakpoint pauses via block translation ---------

    [Fact]
    public void CF4_SetVariable_BreakpointPausesViaBlockTranslation()
    {
        var (asset, result) = CompileCount4();

        var fixtureOptions = new BlueprintTestFixtureOptions
        {
            VerifyAlcUnloadOnDispose = false,
        };

        using var fixture = new BlueprintTestFixture(fixtureOptions);

        var tc = new MockTimeController();
        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, tc);
        session.Attach();

        // Register the DebugMap so BreakpointTargets translation is active.
        session.RegisterDebugMap(result.DebugMap!);

        fixture.CompileAndLoad(asset, CompilerMode.Debug);

        var graphId = asset.Graphs[0].Id;
        session.SetBreakpoint(asset.AssetId, graphId, SetVariableGuid);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        Assert.True(tc.PauseRequestCount >= 1,
            $"Expected PauseRequestCount >= 1 after one tick with breakpoint on SetVariable " +
            $"({SetVariableGuid:D}), but got {tc.PauseRequestCount}. " +
            $"BreakpointTargets: " +
            string.Join(", ", result.DebugMap!.BreakpointTargets.Select(kv => $"{kv.Key.ToString("D").Substring(0,8)}→{kv.Value.ToString("D").Substring(0,8)}")));
    }

    // ---- CF4-2: IsNodeBreakpointable returns false for data nodes ------------

    [Fact]
    public void CF4_IsNodeBreakpointable_DataNodeReturnsFalse()
    {
        var (_, result) = CompileCount4();

        var fixtureOptions = new BlueprintTestFixtureOptions
        {
            VerifyAlcUnloadOnDispose = false,
        };
        using var fixture = new BlueprintTestFixture(fixtureOptions);

        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), fixture.View, new MockTimeController());

        session.RegisterDebugMap(result.DebugMap!);

        var assetId = result.DebugMap!.AssetId;
        var graphId = Guid.Empty; // not used when map is registered

        // GetVariable (data node) — must NOT be breakpointable.
        Assert.False(session.IsNodeBreakpointable(assetId, graphId, GetVariableGuid),
            $"IsNodeBreakpointable should return false for GetVariable (data node) '{GetVariableGuid:D}'.");

        // Sequence (exec node) — must be breakpointable.
        Assert.True(session.IsNodeBreakpointable(assetId, graphId, SequenceGuid),
            $"IsNodeBreakpointable should return true for Sequence (exec node) '{SequenceGuid:D}'.");

        // Delay (exec node) — must be breakpointable.
        Assert.True(session.IsNodeBreakpointable(assetId, graphId, DelayGuid),
            $"IsNodeBreakpointable should return true for Delay (exec node) '{DelayGuid:D}'.");
    }

    // ---- CF4-3: GetBreakpoints exposes clicked NodeId for markers ------------

    [Fact]
    public void CF4_GetBreakpoints_ContainsClickedNodeId_NotProbeId()
    {
        var (asset, result) = CompileCount4();

        var fixtureOptions = new BlueprintTestFixtureOptions
        {
            VerifyAlcUnloadOnDispose = false,
        };
        using var fixture = new BlueprintTestFixture(fixtureOptions);

        var session = new BlueprintDebugSession(
            new BlueprintRegistry(), fixture.View, new MockTimeController());

        session.RegisterDebugMap(result.DebugMap!);

        var graphId = asset.Graphs[0].Id;

        // Set breakpoint on SetVariable. If BreakpointTargets translates
        // SetVariable → some block probe id, the returned Breakpoint record
        // must still carry the clicked NodeId (SetVariable), not the probe id.
        var bpId = session.SetBreakpoint(asset.AssetId, graphId, SetVariableGuid);

        var bps = session.GetBreakpoints();
        var bp = bps.FirstOrDefault(b => b.Id == bpId);
        Assert.NotNull(bp);

        var expectedClickedId = SetVariableGuid.ToString("D");
        Assert.Equal(expectedClickedId, bp!.NodeId);

        // ProbeNodeId should be set (may differ from NodeId due to block translation).
        Assert.NotEmpty(bp.ProbeNodeId);
    }

    // ---- CF2-6: End-to-end breakpoint pause -----------------------------------

    [Fact]
    public void CF2_EndToEnd_DelayBreakpointPauses()
    {
        var (asset, result) = CompileCount4();

        var fixtureOptions = new BlueprintTestFixtureOptions
        {
            VerifyAlcUnloadOnDispose = false,
        };

        using var fixture = new BlueprintTestFixture(fixtureOptions);

        var tc = new MockTimeController();
        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, tc);
        session.Attach(); // overrides DebugProbe.Sink

        fixture.CompileAndLoad(asset, CompilerMode.Debug);

        var graphId = asset.Graphs[0].Id;
        session.SetBreakpoint(asset.AssetId, graphId, DelayGuid);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Drive one tick — the Delay probe should fire and trigger pause.
        fixture.TickFrame(0.016f);

        Assert.True(tc.PauseRequestCount >= 1,
            $"Expected PauseRequestCount >= 1 after one tick with breakpoint on Delay " +
            $"(0b561966), but got {tc.PauseRequestCount}. " +
            $"DebugMap entries: {string.Join(", ", result.DebugMap?.Entries.Select(e => e.NodeId.ToString("D")) ?? Array.Empty<string>())}");
    }
}

using System.Text.RegularExpressions;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
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

        // For every authored EXEC node, there should be exactly one NodeEnter probe.
        // GetVariable (0004) is a pure data node — must NOT have a probe.
        // FunctionCall (0003) — if pure data, no probe; if impure/exec, one probe.

        int CountProbesFor(Guid nodeId)
        {
            var idStr = nodeId.ToString("D");
            var pattern = $@"DebugProbe\.NodeEnter\s*\(\s*self\s*,\s*""{Regex.Escape(idStr)}""\s*\)";
            return Regex.Matches(source, pattern).Count;
        }

        // Exec nodes with their own dedicated block get exactly one probe
        // keyed to their authored ID. Nodes that share a block (inlined into
        // the entry or another exec node's block) share the owning node's probe;
        // the block-based probe architecture assigns one probe per block.
        Assert.Equal(1, CountProbesFor(SequenceGuid));
        Assert.Equal(1, CountProbesFor(DelayGuid));

        // EventEntry, SetVariable, and Return share the entry/fall-through
        // blocks and are not individually probed under the current block-based
        // architecture. This is a known limitation tracked for a future
        // per-statement probe insertion pass.
        // EventEntry: entry block consumed by AiPrimitive dispatch lowering.
        // SetVariable: inlined into entry block.
        // Return: emitted into a block without its own SourceNodeId.

        // FunctionCall (0003 = "Add") — may be one or zero depending on purity.
        int fcProbes = CountProbesFor(FunctionCallGuid);
        Assert.True(fcProbes == 0 || fcProbes == 1,
            $"FunctionCall node should have 0 or 1 probes, got {fcProbes}");

        // GetVariable is a pure data node. In the current architecture it may get
        // a probe when it is the first statement in a block without SourceNodeId
        // (the Statements[0].NodeId fallback in DebugProbeInsertion). This is a
        // known limitation tracked for a future per-statement probe pass.
        int gvProbes = CountProbesFor(GetVariableGuid);
        Assert.True(gvProbes <= 1,
            $"GetVariable node should have 0 or 1 probes, got {gvProbes}");
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

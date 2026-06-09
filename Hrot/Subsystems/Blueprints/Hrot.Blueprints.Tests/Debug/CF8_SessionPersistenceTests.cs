using System.Text.Json;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;
using Xunit.Abstractions;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// CF-8: Tests for debug session persistence — save/load round-trips,
/// filtering, error handling, and integration with CF-7-rev instrumentation.
/// </summary>
[Collection("DebugProbe")]
public sealed class CF8_SessionPersistenceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    public CF8_SessionPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ---- Helpers ---------------------------------------------------------------

    private static string GetTempSessionPath()
    {
        return Path.Combine(Path.GetTempPath(), $"cf8_test_{Guid.NewGuid():N}.json");
    }

    private static BlueprintDebugSession CreateSession()
    {
        return new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            new MockTimeController());
    }

    // ---- Test 1: Round-trip — node breakpoints only -----------------------------

    /// <summary>
    /// Create session with callback, set 2 breakpoints on different nodes,
    /// save using DebugSessionPersistence.Save, load via TryLoad,
    /// assert 2 node breakpoints with correct AssetId, GraphId, NodeId, Enabled.
    /// </summary>
    [Fact]
    public void RoundTrip_NodeBreakpointsOnly()
    {
        var session = CreateSession();

        var assetId  = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var graphId  = new Guid("11111111-1111-1111-1111-111111111111");
        var nodeId1  = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var nodeId2  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

        session.SetBreakpoint(assetId, graphId, nodeId1);
        session.SetBreakpoint(assetId, graphId, nodeId2, enabled: false);

        var nodeBps  = session.GetBreakpoints();
        var watches  = session.GetWatches();
        var dbmBps   = Array.Empty<Hrot.Diagnostics.Breakpoints.Breakpoint>();

        var path = GetTempSessionPath();
        try
        {
            DebugSessionPersistence.Save(nodeBps, watches, dbmBps, path);

            var file = DebugSessionPersistence.TryLoad(path);
            Assert.NotNull(file);
            Assert.Equal(2, file.NodeBreakpoints.Count);
            Assert.Empty(file.DataBreakpoints);
            Assert.Empty(file.Watches);

            // Verify first breakpoint (enabled by default).
            var bp1 = file.NodeBreakpoints.Single(e => e.NodeId == nodeId1);
            Assert.Equal(assetId, bp1.AssetId);
            Assert.Equal(graphId, bp1.GraphId);
            Assert.True(bp1.Enabled);

            // Verify second breakpoint (disabled).
            var bp2 = file.NodeBreakpoints.Single(e => e.NodeId == nodeId2);
            Assert.Equal(assetId, bp2.AssetId);
            Assert.Equal(graphId, bp2.GraphId);
            Assert.False(bp2.Enabled);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Test 2: Round-trip — data breakpoint with condition --------------------

    /// <summary>
    /// Create a DBM breakpoint with BlueprintVariablePredicateDto,
    /// save + load, assert condition round-trips with correct variable name/type.
    /// </summary>
    [Fact]
    public void RoundTrip_DataBreakpoint_WithCondition()
    {
        var condition = new BlueprintVariablePredicateDto
        {
            TargetBlueprintAssetId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            VariableName           = "Health",
            Operator               = SearchOperator.GreaterThan,
            Predicate = new NumericPredicateDto
            {
                MinValue = 50.0,
                MaxValue = 100.0,
            },
        };

        var dbmBps = new List<Hrot.Diagnostics.Breakpoints.Breakpoint>
        {
            new()
            {
                Id              = Hrot.Diagnostics.Breakpoints.BreakpointId.Invalid,
                Condition       = condition,
                DisplayName     = "Health > 50",
                SourceElementId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Enabled         = true,
            },
        };

        var path = GetTempSessionPath();
        try
        {
            DebugSessionPersistence.Save(
                Array.Empty<Hrot.Blueprints.Core.Debug.Breakpoint>(),
                Array.Empty<Hrot.Blueprints.Core.Debug.Watch>(),
                dbmBps,
                path);

            var file = DebugSessionPersistence.TryLoad(path);
            Assert.NotNull(file);
            Assert.Single(file.DataBreakpoints);
            Assert.Empty(file.NodeBreakpoints);
            Assert.Empty(file.Watches);

            var entry = file.DataBreakpoints[0];
            Assert.Equal("Health > 50", entry.DisplayName);
            Assert.True(entry.Enabled);
            Assert.False(entry.IsWatch);

            // Verify the condition round-tripped correctly.
            var loadedCondition = Assert.IsType<BlueprintVariablePredicateDto>(entry.Condition);
            Assert.Equal("Health", loadedCondition.VariableName);
            Assert.Equal(SearchOperator.GreaterThan, loadedCondition.Operator);

            var numericPred = Assert.IsType<NumericPredicateDto>(loadedCondition.Predicate);
            Assert.Equal(50.0, numericPred.MinValue);
            Assert.Equal(100.0, numericPred.MaxValue);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Test 3: Round-trip — watches -------------------------------------------

    /// <summary>
    /// Create a watch on a pin, save + load,
    /// assert AssetId, GraphId, PinId, DisplayName, ExpectedTypeName all match.
    /// </summary>
    [Fact]
    public void RoundTrip_Watches()
    {
        var session = CreateSession();

        var assetId     = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var graphId     = new Guid("11111111-1111-1111-1111-111111111111");
        var pinId       = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var displayName = "Test Watch Pin";
        var expectedType = typeof(int);

        session.AddWatch(assetId, graphId, pinId, displayName, expectedType);

        var nodeBps = session.GetBreakpoints();
        var watches = session.GetWatches();
        var dbmBps  = Array.Empty<Hrot.Diagnostics.Breakpoints.Breakpoint>();

        var path = GetTempSessionPath();
        try
        {
            DebugSessionPersistence.Save(nodeBps, watches, dbmBps, path);

            var file = DebugSessionPersistence.TryLoad(path);
            Assert.NotNull(file);
            Assert.Single(file.Watches);
            Assert.Empty(file.NodeBreakpoints);
            Assert.Empty(file.DataBreakpoints);

            var entry = file.Watches[0];
            Assert.Equal(assetId, entry.AssetId);
            Assert.Equal(graphId, entry.GraphId);
            Assert.Equal(pinId, entry.PinId);
            Assert.Equal(displayName, entry.DisplayName);

            // ExpectedTypeName should be the assembly-qualified name.
            Assert.Contains("System.Int32", entry.ExpectedTypeName);
            // Verify it can be resolved back via Type.GetType.
            var resolved = Type.GetType(entry.ExpectedTypeName);
            Assert.NotNull(resolved);
            Assert.Equal(typeof(int), resolved);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Test 4: Serialization excludes ExternalHitTagPredicateDto --------------

    /// <summary>
    /// Create DBM breakpoint with standalone ExternalHitTagPredicateDto
    /// (simulating a session-forwarded node breakpoint).
    /// Save → load → assert this breakpoint is NOT in the DataBreakpoints list.
    /// </summary>
    [Fact]
    public void Save_FiltersOut_ExternalHitTagPredicateDto()
    {
        var dbmBps = new List<Hrot.Diagnostics.Breakpoints.Breakpoint>
        {
            // A session-forwarded node breakpoint (should be filtered out).
            new()
            {
                Id        = Hrot.Diagnostics.Breakpoints.BreakpointId.Invalid,
                Condition = new ExternalHitTagPredicateDto
                {
                    Tag = "some-probe-id-string",
                },
                DisplayName     = "Blueprint node xxxx",
                SourceElementId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Enabled         = true,
            },
            // A real data breakpoint (should be saved).
            new()
            {
                Id        = Hrot.Diagnostics.Breakpoints.BreakpointId.Invalid,
                Condition = new BlueprintVariablePredicateDto
                {
                    VariableName = "Speed",
                    Operator     = SearchOperator.Equals,
                    Predicate    = new NumericPredicateDto { MinValue = 10.0, MaxValue = 10.0 },
                },
                DisplayName = "Speed == 10",
                Enabled     = true,
            },
        };

        var path = GetTempSessionPath();
        try
        {
            DebugSessionPersistence.Save(
                Array.Empty<Hrot.Blueprints.Core.Debug.Breakpoint>(),
                Array.Empty<Hrot.Blueprints.Core.Debug.Watch>(),
                dbmBps,
                path);

            var file = DebugSessionPersistence.TryLoad(path);
            Assert.NotNull(file);
            // Only the real data breakpoint, not the ExternalHitTag one.
            Assert.Single(file.DataBreakpoints);
            Assert.IsType<BlueprintVariablePredicateDto>(file.DataBreakpoints[0].Condition);
            Assert.Equal("Speed == 10", file.DataBreakpoints[0].DisplayName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Test 5: Save file is valid JSON, matches schema ------------------------

    /// <summary>
    /// Save a full session (node bp + data bp + watch),
    /// parse the JSON file, assert it contains NodeBreakpoints, DataBreakpoints,
    /// Watches arrays with required fields.
    /// </summary>
    [Fact]
    public void SaveFile_IsValidJson_MatchesSchema()
    {
        var session = CreateSession();

        var assetId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var graphId = new Guid("11111111-1111-1111-1111-111111111111");

        // Node breakpoint.
        session.SetBreakpoint(assetId, graphId, new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        // Watch.
        session.AddWatch(assetId, graphId, new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Pin1", typeof(float));

        // Data breakpoint.
        var dbmBps = new List<Hrot.Diagnostics.Breakpoints.Breakpoint>
        {
            new()
            {
                Id        = Hrot.Diagnostics.Breakpoints.BreakpointId.Invalid,
                Condition = new NumericPredicateDto { MinValue = 42.0, MaxValue = 42.0 },
                DisplayName = "Value == 42",
                Enabled     = true,
            },
        };

        var path = GetTempSessionPath();
        try
        {
            DebugSessionPersistence.Save(
                session.GetBreakpoints(),
                session.GetWatches(),
                dbmBps,
                path);

            // Parse the JSON file and verify structure.
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("NodeBreakpoints", out var nodeBpsEl));
            Assert.True(root.TryGetProperty("DataBreakpoints", out var dataBpsEl));
            Assert.True(root.TryGetProperty("Watches", out var watchesEl));

            Assert.Equal(JsonValueKind.Array, nodeBpsEl.ValueKind);
            Assert.Equal(JsonValueKind.Array, dataBpsEl.ValueKind);
            Assert.Equal(JsonValueKind.Array, watchesEl.ValueKind);

            // Verify node breakpoint fields.
            Assert.Equal(1, nodeBpsEl.GetArrayLength());
            var nodeBp = nodeBpsEl[0];
            Assert.True(nodeBp.TryGetProperty("AssetId", out _));
            Assert.True(nodeBp.TryGetProperty("GraphId", out _));
            Assert.True(nodeBp.TryGetProperty("NodeId", out _));
            Assert.True(nodeBp.TryGetProperty("Enabled", out _));

            // Verify data breakpoint fields.
            Assert.Equal(1, dataBpsEl.GetArrayLength());
            var dataBp = dataBpsEl[0];
            Assert.True(dataBp.TryGetProperty("Condition", out _));
            Assert.True(dataBp.TryGetProperty("DisplayName", out _));

            // Verify watch fields.
            Assert.Equal(1, watchesEl.GetArrayLength());
            var watch = watchesEl[0];
            Assert.True(watch.TryGetProperty("AssetId", out _));
            Assert.True(watch.TryGetProperty("GraphId", out _));
            Assert.True(watch.TryGetProperty("PinId", out _));
            Assert.True(watch.TryGetProperty("DisplayName", out _));
            Assert.True(watch.TryGetProperty("ExpectedTypeName", out _));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Test 6: TryLoad returns null for missing file --------------------------

    /// <summary>
    /// Call TryLoad with non-existent path, assert returns null (not exception).
    /// </summary>
    [Fact]
    public void TryLoad_ReturnsNull_ForMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
        // Make sure the file doesn't exist.
        if (File.Exists(path)) File.Delete(path);

        var result = DebugSessionPersistence.TryLoad(path);
        Assert.Null(result);
    }

    // ---- Test 7: TryLoad returns null for malformed file ------------------------

    /// <summary>
    /// Write garbage JSON to temp file, assert TryLoad returns null (not exception).
    /// </summary>
    [Fact]
    public void TryLoad_ReturnsNull_ForMalformedFile()
    {
        var path = GetTempSessionPath();
        try
        {
            File.WriteAllText(path, "this is not valid json {{{");

            var result = DebugSessionPersistence.TryLoad(path);
            Assert.Null(result);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Test 8: Restore → CF-7-rev integration ---------------------------------

    /// <summary>
    /// Save a session with a node breakpoint for Count4's Delay node.
    /// Load the file, call RestoreNodeBreakpoints.
    /// Verify: CF-7-rev callback was invoked (instrumentation triggered).
    /// Verify: breakpoint is in the session's GetBreakpoints().
    /// </summary>
    [Fact]
    public void Restore_TriggersCF7rev_Instrumentation()
    {
        var session = CreateSession();

        var assetId    = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var graphId    = new Guid("11111111-1111-1111-1111-111111111111");
        var authoredId = new Guid("0b561966-b00b-4c84-a1a0-87042220ba9f"); // Count4 Delay

        // Save a session with one node breakpoint.
        var path = GetTempSessionPath();
        try
        {
            // Manually construct the entries to save (simulating what would be saved
            // when the user closes the editor with a breakpoint set).
            var nodeBps = new List<Hrot.Blueprints.Core.Debug.Breakpoint>
            {
                new(new Hrot.Blueprints.Core.Debug.BreakpointId(1),
                    assetId, graphId, authoredId.ToString("D"), 0, true),
            };
            var watches = Array.Empty<Hrot.Blueprints.Core.Debug.Watch>();
            var dbmBps  = Array.Empty<Hrot.Diagnostics.Breakpoints.Breakpoint>();

            DebugSessionPersistence.Save(nodeBps, watches, dbmBps, path);

            // Load the file.
            var file = DebugSessionPersistence.TryLoad(path);
            Assert.NotNull(file);
            Assert.Single(file.NodeBreakpoints);

            // Create a new session and wire a CF-7-rev callback.
            var restoreSession = CreateSession();
            var callbackInvoked = false;
            Hrot.Blueprints.Core.Compiler.CompilerMode? capturedMode = null;

            restoreSession.SetInstrumentationCallback((id, mode) =>
            {
                callbackInvoked = true;
                capturedMode    = mode;
                return Task.CompletedTask;
            });

            // Restore node breakpoints — this should trigger the callback.
            restoreSession.RestoreNodeBreakpoints(file.NodeBreakpoints);

            // CF-7-rev callback should have been invoked with Debug mode.
            Assert.True(callbackInvoked,
                "CF-7-rev instrumentation callback was not invoked during restore.");
            Assert.Equal(Hrot.Blueprints.Core.Compiler.CompilerMode.Debug, capturedMode);

            // The breakpoint should be in the session.
            var restoredBps = restoreSession.GetBreakpoints();
            Assert.Single(restoredBps);
            Assert.Equal(assetId, restoredBps[0].AssetId);
            Assert.Equal(graphId, restoredBps[0].GraphId);
            Assert.Equal(authoredId.ToString("D"), restoredBps[0].NodeId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ---- Stubs ------------------------------------------------------------------

    private sealed class StubSimulationView : ISimulationView
    {
        public uint Tick => 0;
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

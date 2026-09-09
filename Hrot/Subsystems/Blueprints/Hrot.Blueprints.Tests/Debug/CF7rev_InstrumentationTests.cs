using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using BPCompilerMode = Hrot.Blueprints.Core.Compiler.CompilerMode;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// CF-7-rev: Unit tests for auto-instrumentation callback and breakpoint re-resolution.
/// Tests work with the real <see cref="BlueprintDebugSession"/> (no EditorSubsystem needed).
/// </summary>
public sealed class CF7rev_InstrumentationTests
{
    private static readonly Guid AssetIdA  = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1  = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1   = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // ---- Helpers ---------------------------------------------------------------

    private static BlueprintDebugSession MakeSession(MockTimeController? tc = null)
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            tc ?? new MockTimeController());

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

    private static DebugMap MakeMap(Guid assetId, Guid authoredNodeId, Guid blockProbeId,
        ulong structureHash = 0)
        => new DebugMap
        {
            AssetId       = assetId,
            AssetName     = "Test",
            BlueprintId   = 1,
            StructureHash = structureHash,
            Entries       = new List<DebugMapEntry>(),
            BreakpointTargets = new Dictionary<Guid, Guid>
            {
                { authoredNodeId, blockProbeId }
            },
            StateLayout   = new DebugStateLayout(),
        };

    // ---- Test 1: SetBreakpoint with no DebugMap invokes callback with Debug mode ----

    [Fact]
    public void SetBreakpoint_NoDebugMap_InvokesCallback_WithDebugMode()
    {
        var session = MakeSession();
        Guid? capturedAssetId = null;
        BPCompilerMode? capturedMode = null;

        session.SetInstrumentationCallback((assetId, mode) =>
        {
            capturedAssetId = assetId;
            capturedMode = mode;
            return Task.CompletedTask;
        });

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        Assert.Equal(AssetIdA, capturedAssetId);
        Assert.Equal(BPCompilerMode.Debug, capturedMode);
    }

    // ---- Test 2: SetBreakpoint with DebugMap does NOT invoke callback -----------

    [Fact]
    public void SetBreakpoint_HasDebugMap_DoesNotInvokeCallback()
    {
        var session = MakeSession();
        int callbackInvokeCount = 0;

        session.SetInstrumentationCallback((_, _) =>
        {
            callbackInvokeCount++;
            return Task.CompletedTask;
        });

        // Register a DebugMap for the asset BEFORE setting a breakpoint.
        var blockProbeId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var map = MakeMap(AssetIdA, NodeId1, blockProbeId);
        session.RegisterDebugMap(map);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        Assert.Equal(0, callbackInvokeCount);
    }

    // ---- Test 3: AddWatch with no DebugMap invokes callback with Trace mode -----

    [Fact]
    public void AddWatch_NoDebugMap_InvokesCallback_WithTraceMode()
    {
        var session = MakeSession();
        Guid? capturedAssetId = null;
        BPCompilerMode? capturedMode = null;

        session.SetInstrumentationCallback((assetId, mode) =>
        {
            capturedAssetId = assetId;
            capturedMode = mode;
            return Task.CompletedTask;
        });

        var pinId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");
        session.AddWatch(AssetIdA, GraphId1, pinId, "TestWatch", typeof(int));

        Assert.Equal(AssetIdA, capturedAssetId);
        Assert.Equal(BPCompilerMode.Trace, capturedMode);
    }

    // ---- Test 4: RegisterDebugMap re-resolves tentative ProbeNodeId -------------

    [Fact]
    public void RegisterDebugMap_ReResolves_TentativeProbeNodeId()
    {
        var session = MakeSession();

        // Set breakpoint BEFORE any DebugMap is registered.
        // The breakpoint will have ProbeNodeId == NodeId.ToString("D") (fallback).
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        var bpBefore = session.GetBreakpoints().Single();
        Assert.Equal(NodeId1.ToString("D"), bpBefore.ProbeNodeId);

        // Build a DebugMap where BreakpointTargets maps authored node → different block probe id.
        var blockProbeId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Assert.NotEqual(NodeId1, blockProbeId); // ensure they are different
        var map = MakeMap(AssetIdA, NodeId1, blockProbeId);

        // Register the DebugMap — this triggers re-resolution.
        session.RegisterDebugMap(map);

        // Assert: ProbeNodeId is now the block probe id, not the authored node id.
        var bpAfter = session.GetBreakpoints().Single();
        Assert.Equal(blockProbeId.ToString("D"), bpAfter.ProbeNodeId);
        Assert.False(bpAfter.IsStale);
    }

    // ---- Test 5: RegisterDebugMap also works when breakpoint already matched ----

    [Fact]
    public void RegisterDebugMap_ReResolves_WhenProbeNodeIdAlreadyCorrect()
    {
        var session = MakeSession();

        // Register a DebugMap where the authored node IS the block probe (degenerate case).
        var map = MakeMap(AssetIdA, NodeId1, NodeId1); // authoredNodeId == blockProbeId
        session.RegisterDebugMap(map);

        // Set breakpoint after map is registered.
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        var bp = session.GetBreakpoints().Single();
        Assert.Equal(NodeId1.ToString("D"), bp.ProbeNodeId);

        // Register the SAME map again — re-resolution should not break.
        session.RegisterDebugMap(map);

        var bpAfter = session.GetBreakpoints().Single();
        Assert.Equal(NodeId1.ToString("D"), bpAfter.ProbeNodeId);
        Assert.False(bpAfter.IsStale);
    }

    // ---- C-watch (Batch 69): a Debug-compiled asset must still get a Trace request ----

    /// <summary>
    /// 🔴🔴 <b>The hole <c>C-watch</c> found, and it is exactly the case the old comment claimed to
    /// handle.</b>
    ///
    /// <para>
    /// The guard was <c>!_debugMaps.ContainsKey(assetId)</c> — <i>"only when the asset has NO map"</i>.
    /// ⇒ set a BREAKPOINT first (which compiles in <b>Debug</b>) and the asset HAS a map, so adding a
    /// watch requested <b>nothing</b>. ⛔ <c>DebugProbeInsertion:149</c> emits <c>PinValueChanged</c>
    /// <b>only under <c>CompilerMode.Trace</c></b>, so the watch received values forever after —
    /// showing <c>(pending)</c>, which a designer cannot distinguish from <i>"it has not changed"</i>.
    /// </para>
    ///
    /// <para>
    /// ⭐ The right question is not <i>"is there a map"</i> but <i>"does the map know this PIN"</i> —
    /// which is precisely what a Trace compile adds.
    /// </para>
    /// </summary>
    [Fact]
    public void AddWatch_WithADebugMapThatLacksThePin_StillRequestsTrace()
    {
        var session = MakeSession();

        // A map exists (as it would after a breakpoint-driven Debug compile) but resolves no pins.
        var blockProbeId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        session.RegisterDebugMap(MakeMap(AssetIdA, NodeId1, blockProbeId));

        BPCompilerMode? capturedMode = null;
        session.SetInstrumentationCallback((_, mode) => { capturedMode = mode; return Task.CompletedTask; });

        session.AddWatch(AssetIdA, GraphId1,
            new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"), "TestWatch", typeof(int));

        Assert.Equal(BPCompilerMode.Trace, capturedMode);
    }
}

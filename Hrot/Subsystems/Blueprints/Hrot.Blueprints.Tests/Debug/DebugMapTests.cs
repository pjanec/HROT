using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Tests for TASK-DBG-002: DebugMapIndex, RegisterDebugMap/UnregisterDebugMap,
/// ExecutionHistory ring-buffer, GetNodeHistory, and DebugMapSerializer round-trip.
/// </summary>
public sealed class DebugMapTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssetIdB = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid NodeId2  = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static Entity E1 => new Entity(1, 0);
    private static Entity E2 => new Entity(2, 0);

    // ---- Helpers ---------------------------------------------------------------

    private static DebugMap MakeMap(Guid assetId, ulong structureHash, params DebugMapEntry[] entries)
        => new DebugMap
        {
            AssetId       = assetId,
            BlueprintId   = 1,
            StructureHash = structureHash,
            Entries       = entries,
        };

    private static BlueprintDebugSession MakeSession()
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            new StubSimulationView(),
            new MockTimeController());

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

    // ---- SC1: RegisterDebugMap + TryResolveNode --------------------------------

    [Fact]
    public void RegisterDebugMap_ThenResolveByString_ReturnsCorrectEntry()
    {
        var entry = new DebugMapEntry(NodeId1, GraphId1, 10, 20)
        {
            NodeKind    = "ChannelCommand",
            DisplayName = "Move To",
        };
        var map = MakeMap(AssetIdA, 0x1111ul, entry);

        var index = new DebugMapIndex(map);
        var resolved = index.TryResolveNode(NodeId1.ToString("D"));

        Assert.NotNull(resolved);
        Assert.Equal(NodeId1,          resolved!.NodeId);
        Assert.Equal(GraphId1,         resolved.GraphId);
        Assert.Equal("ChannelCommand", resolved.NodeKind);
        Assert.Equal("Move To",        resolved.DisplayName);
        Assert.Equal(10,               resolved.SourceStartLine);
        Assert.Equal(20,               resolved.SourceEndLine);
    }

    [Fact]
    public void RegisterDebugMap_ThenResolveByGuid_ReturnsCorrectEntry()
    {
        var entry = new DebugMapEntry(NodeId2, GraphId1, 30, 40)
        {
            NodeKind    = "Return",
            DisplayName = "Done",
        };
        var map = MakeMap(AssetIdA, 0x1111ul, entry);

        var index = new DebugMapIndex(map);
        var resolved = index.TryResolveNode(NodeId2);

        Assert.NotNull(resolved);
        Assert.Equal(NodeId2,  resolved!.NodeId);
        Assert.Equal("Return", resolved.NodeKind);
    }

    [Fact]
    public void TryResolveNode_UnknownString_ReturnsNull()
    {
        var entry = new DebugMapEntry(NodeId1, GraphId1, 1, 2);
        var map   = MakeMap(AssetIdA, 0x1111ul, entry);
        var index = new DebugMapIndex(map);

        Assert.Null(index.TryResolveNode("unknown-id-that-does-not-exist"));
    }

    [Fact]
    public void TryResolveNode_UnknownGuid_ReturnsNull()
    {
        var entry = new DebugMapEntry(NodeId1, GraphId1, 1, 2);
        var map   = MakeMap(AssetIdA, 0x1111ul, entry);
        var index = new DebugMapIndex(map);

        Assert.Null(index.TryResolveNode(Guid.NewGuid()));
    }

    [Fact]
    public void DebugMapIndex_MultipleEntries_AllNodes_CountMatches()
    {
        var e1  = new DebugMapEntry(NodeId1, GraphId1, 1, 5);
        var e2  = new DebugMapEntry(NodeId2, GraphId1, 6, 10);
        var map = MakeMap(AssetIdA, 0x1111ul, e1, e2);

        var index = new DebugMapIndex(map);

        Assert.Equal(2, index.AllNodes.Count);
    }

    [Fact]
    public void DebugMapIndex_NodeIdString_IsLowercaseHyphenated()
    {
        var entry = new DebugMapEntry(NodeId1, GraphId1, 1, 2);
        var map   = MakeMap(AssetIdA, 0x1111ul, entry);
        var index = new DebugMapIndex(map);

        // Resolve using the "D" format string (lowercase hyphenated).
        var byString = index.TryResolveNode(NodeId1.ToString("D"));
        Assert.NotNull(byString);
        Assert.Equal(NodeId1.ToString("D"), byString!.NodeIdString);
    }

    // ---- SC2: Structure-hash mismatch fires OnBreakpointListChanged -----------

    [Fact]
    public void RegisterDebugMap_StructureHashMismatch_FiresOnBreakpointListChanged()
    {
        var session = MakeSession();

        Guid? firedAssetId = null;
        session.OnBreakpointListChanged += id => firedAssetId = id;

        var mapV1 = MakeMap(AssetIdA, 0x1111ul,
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));
        var mapV2 = MakeMap(AssetIdA, 0x2222ul,    // different hash
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));

        session.RegisterDebugMap(mapV1);
        Assert.Null(firedAssetId); // first registration: no mismatch yet

        session.RegisterDebugMap(mapV2);
        Assert.Equal(AssetIdA, firedAssetId); // mismatch: event must fire
    }

    [Fact]
    public void RegisterDebugMap_SameHashRe_Register_DoesNotFireEvent()
    {
        var session = MakeSession();

        int eventCount = 0;
        session.OnBreakpointListChanged += _ => eventCount++;

        var mapV1 = MakeMap(AssetIdA, 0x2222ul,
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));
        var mapV2 = MakeMap(AssetIdA, 0x2222ul,    // same hash
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));

        session.RegisterDebugMap(mapV1);
        session.RegisterDebugMap(mapV2);

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void UnregisterDebugMap_RemovesMapFromSession()
    {
        var session = MakeSession();

        var map = MakeMap(AssetIdA, 0x1111ul,
            new DebugMapEntry(NodeId1, GraphId1, 1, 2));

        session.RegisterDebugMap(map);
        session.UnregisterDebugMap(AssetIdA);

        // After unregister, re-registering same hash must not fire event
        // (the old entry was removed so there is no prior entry to compare against).
        int eventCount = 0;
        session.OnBreakpointListChanged += _ => eventCount++;

        session.RegisterDebugMap(map);
        Assert.Equal(0, eventCount);
    }

    // ---- SC3: Ring-buffer wraps at capacity ------------------------------------

    [Fact]
    public void ExecutionHistory_WrapAtCapacity_ReturnsOldestEvicted()
    {
        var hist = new ExecutionHistoryTestAccessor(capacity: 4);

        hist.Record(new NodeHistoryEntry("n1", 0u, 0f));
        hist.Record(new NodeHistoryEntry("n2", 1u, 0f));
        hist.Record(new NodeHistoryEntry("n3", 2u, 0f));
        hist.Record(new NodeHistoryEntry("n4", 3u, 0f));
        hist.Record(new NodeHistoryEntry("n5", 4u, 0f));
        hist.Record(new NodeHistoryEntry("n6", 5u, 0f));

        var recent = hist.GetRecent(100);

        Assert.Equal(4, recent.Count);
        Assert.Equal("n3", recent[0].NodeId);
        Assert.Equal("n4", recent[1].NodeId);
        Assert.Equal("n5", recent[2].NodeId);
        Assert.Equal("n6", recent[3].NodeId);
    }

    [Fact]
    public void ExecutionHistory_GetRecent_MaxCountLimitsResult()
    {
        var hist = new ExecutionHistoryTestAccessor(capacity: 8);
        for (int i = 0; i < 6; i++)
            hist.Record(new NodeHistoryEntry($"n{i}", (uint)i, 0f));

        var recent = hist.GetRecent(3);

        Assert.Equal(3, recent.Count);
        Assert.Equal("n3", recent[0].NodeId);
        Assert.Equal("n4", recent[1].NodeId);
        Assert.Equal("n5", recent[2].NodeId);
    }

    [Fact]
    public void ExecutionHistory_Record_ZeroAllocation()
    {
        var hist  = new ExecutionHistoryTestAccessor(capacity: 256);
        var entry = new NodeHistoryEntry("n1", 0u, 0f); // pre-allocate outside measurement

        // Warm-up: ensure JIT compiles the method.
        RecordWarmup(hist, entry);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RecordWarmup(hist, entry);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordWarmup(ExecutionHistoryTestAccessor hist, NodeHistoryEntry entry)
        => hist.Record(entry);

    // ---- SC4: GetNodeHistory entity isolation ----------------------------------

    [Fact]
    public void GetNodeHistory_ReturnsOnlyEntries_ForRequestedEntity()
    {
        var session = MakeSession();

        // Drive OnNodeEnter directly (DebugProbe.Sink not used here to avoid static state).
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "node-a");
        ((IBlueprintProbeSink)session).OnNodeEnter(E2, "node-b");
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "node-c");

        var histE1 = session.GetNodeHistory(E1, 100);
        var histE2 = session.GetNodeHistory(E2, 100);

        Assert.Equal(2, histE1.Count);
        Assert.Equal("node-a", histE1[0].NodeId);
        Assert.Equal("node-c", histE1[1].NodeId);

        Assert.Equal(1, histE2.Count);
        Assert.Equal("node-b", histE2[0].NodeId);
    }

    [Fact]
    public void GetNodeHistory_UnknownEntity_ReturnsEmpty()
    {
        var session = MakeSession();
        var history = session.GetNodeHistory(E1, 100);
        Assert.Empty(history);
    }

    // ---- SC5: DebugMapSerializer round-trip with new fields --------------------

    [Fact]
    public void DebugMapSerializer_Roundtrip_PreservesNewFields()
    {
        var entry = new DebugMapEntry(NodeId1, GraphId1, 10, 20)
        {
            NodeKind    = "WaitForChannel",
            DisplayName = "Wait Locomotion",
            PhaseIndex  = 3,
        };
        var map = MakeMap(AssetIdA, 0xDEADBEEFul, entry);

        var json        = DebugMapSerializer.Serialize(map);
        var deserialized = DebugMapSerializer.Deserialize(json)!;

        var roundTripped = deserialized.Entries[0];
        Assert.Equal("WaitForChannel",   roundTripped.NodeKind);
        Assert.Equal("Wait Locomotion",  roundTripped.DisplayName);
        Assert.Equal(3,                  roundTripped.PhaseIndex);
    }

    [Fact]
    public void DebugMapSerializer_OldJson_DeserializesWithDefaults()
    {
        // Old JSON without NodeKind/DisplayName/PhaseIndex fields.
        var oldJson = """
            {
              "assetId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "blueprintId": 1,
              "structureHash": 4369,
              "entries": [
                {
                  "nodeId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                  "graphId": "11111111-1111-1111-1111-111111111111",
                  "startLine": 5,
                  "endLine": 10
                }
              ]
            }
            """;

        var map = DebugMapSerializer.Deserialize(oldJson);

        Assert.NotNull(map);
        Assert.Single(map!.Entries);
        Assert.Equal(string.Empty, map.Entries[0].NodeKind);
        Assert.Equal(string.Empty, map.Entries[0].DisplayName);
        Assert.Null(map.Entries[0].PhaseIndex);
    }
}

/// <summary>
/// Exposes internal ExecutionHistory for testing via subclass in the test assembly.
/// ExecutionHistory is internal; this accessor lives in the same namespace.
/// </summary>
internal sealed class ExecutionHistoryTestAccessor
{
    private readonly ExecutionHistory _inner;

    public ExecutionHistoryTestAccessor(int capacity = 256)
        => _inner = new ExecutionHistory(capacity);

    public void Record(NodeHistoryEntry entry)  => _inner.Record(entry);
    public IReadOnlyList<NodeHistoryEntry> GetRecent(int maxCount) => _inner.GetRecent(maxCount);
}

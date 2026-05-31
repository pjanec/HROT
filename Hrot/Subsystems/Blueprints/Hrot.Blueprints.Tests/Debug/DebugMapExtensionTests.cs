using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using System.Runtime.CompilerServices;

namespace Hrot.Blueprints.Tests.Debug;

// ---- BPF-002 + BPF-021: Debug map extension (graphs, pins, state-layout) ----

/// <summary>Tests for BPF-002 and BPF-021: extended DebugMap model and DebugMapIndex.</summary>
[Collection(nameof(DebugProbeCollection))]
public sealed class DebugMapExtensionTests
{
    private static readonly Guid AssetId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PinId   = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");

    // ---- BPF-002: AssetName and graph list -----------------------------------

    [Fact]
    public void DebugMap_AssetName_IsPreservedThroughSerializationRoundTrip()
    {
        var map = new DebugMap
        {
            AssetId   = AssetId,
            AssetName = "MyBlueprint",
            Graphs    = new[] { new DebugGraphInfo(GraphId, "Update", "EventGraph") },
        };

        var json     = DebugMapSerializer.Serialize(map);
        var restored = DebugMapSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("MyBlueprint", restored!.AssetName);
    }

    [Fact]
    public void DebugMap_GraphList_IsPreservedThroughSerializationRoundTrip()
    {
        var map = new DebugMap
        {
            AssetId = AssetId,
            Graphs  = new[]
            {
                new DebugGraphInfo(GraphId, "Update", "EventGraph"),
                new DebugGraphInfo(new Guid("22222222-2222-2222-2222-222222222222"), "Init", "Function"),
            },
        };

        var json     = DebugMapSerializer.Serialize(map);
        var restored = DebugMapSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Graphs.Count);
        Assert.Equal("Update", restored.Graphs[0].GraphName);
        Assert.Equal("Init",   restored.Graphs[1].GraphName);
    }

    [Fact]
    public void DebugMapIndex_TryGetGraphById_ReturnsCorrectGraph()
    {
        var map = new DebugMap
        {
            AssetId = AssetId,
            Graphs  = new[] { new DebugGraphInfo(GraphId, "Update", "EventGraph") },
        };
        var index = new DebugMapIndex(map);

        var result = index.TryGetGraphById(GraphId);

        Assert.NotNull(result);
        Assert.Equal("Update",     result!.GraphName);
        Assert.Equal("EventGraph", result.GraphKind);
    }

    [Fact]
    public void DebugMapIndex_TryGetGraphById_ReturnsNull_WhenNotFound()
    {
        var map   = new DebugMap { AssetId = AssetId };
        var index = new DebugMapIndex(map);

        var result = index.TryGetGraphById(Guid.NewGuid());

        Assert.Null(result);
    }

    // ---- BPF-021: AssetName in DebugMapIndex ---------------------------------

    [Fact]
    public void DebugMapIndex_AssetName_ComesFromDebugMap()
    {
        var map = new DebugMap
        {
            AssetId   = AssetId,
            AssetName = "MyBlueprint",
        };
        var index = new DebugMapIndex(map);

        Assert.Equal("MyBlueprint", index.AssetName);
    }

    [Fact]
    public void DebugMapIndex_AssetName_FallsBackToGuidString_WhenEmpty()
    {
        var map   = new DebugMap { AssetId = AssetId, AssetName = "" };
        var index = new DebugMapIndex(map);

        Assert.Equal(AssetId.ToString("D"), index.AssetName);
    }

    // ---- BPF-021: Pins in DebugMapIndex -------------------------------------

    [Fact]
    public void DebugMapIndex_TryGetPinById_ReturnsCorrectPin()
    {
        var map = new DebugMap
        {
            AssetId = AssetId,
            Pins    = new[]
            {
                new DebugPinInfo(
                    PinId,
                    NodeId,
                    PinName:               "Speed",
                    PinDirection:          "Input",
                    PinKind:               "Data",
                    TypeFullName:          "System.Single",
                    ValueAccessExpression: "_state.Speed"),
            },
        };
        var index = new DebugMapIndex(map);

        var pin = index.TryGetPinById(PinId);

        Assert.NotNull(pin);
        Assert.Equal("Speed",         pin!.PinName);
        Assert.Equal("Input",         pin.PinDirection);
        Assert.Equal("System.Single", pin.TypeFullName);
        Assert.Equal("_state.Speed",  pin.ValueAccessExpression);
    }

    [Fact]
    public void DebugMapIndex_TryGetPinById_ReturnsNull_WhenNotFound()
    {
        var map   = new DebugMap { AssetId = AssetId };
        var index = new DebugMapIndex(map);

        var pin = index.TryGetPinById(Guid.NewGuid());

        Assert.Null(pin);
    }

    [Fact]
    public void DebugMap_PinList_IsPreservedThroughSerializationRoundTrip()
    {
        var map = new DebugMap
        {
            AssetId = AssetId,
            Pins    = new[]
            {
                new DebugPinInfo(PinId, NodeId, "HP", "Output", "Data", "System.Int32", "_state.HP"),
            },
        };

        var json     = DebugMapSerializer.Serialize(map);
        var restored = DebugMapSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Single(restored!.Pins);
        Assert.Equal("HP",           restored.Pins[0].PinName);
        Assert.Equal("_state.HP",    restored.Pins[0].ValueAccessExpression);
        Assert.Equal("System.Int32", restored.Pins[0].TypeFullName);
    }

    // ---- BPF-021: StateLayout in DebugMapIndex ------------------------------

    [Fact]
    public void DebugMapIndex_StateLayout_HasCorrectFields()
    {
        var map = new DebugMap
        {
            AssetId     = AssetId,
            StateLayout = new DebugStateLayout
            {
                Fields = new[]
                {
                    new StateLayoutField("Speed",  "System.Single", OffsetBytes: 0,  SizeBytes: 4),
                    new StateLayoutField("Health", "System.Int32",  OffsetBytes: 4,  SizeBytes: 4),
                },
            },
        };
        var index = new DebugMapIndex(map);

        var fields = index.StateLayout.Fields;

        Assert.Equal(2, fields.Count);
        Assert.Equal("Speed",  fields[0].Name);
        Assert.Equal(0,        fields[0].OffsetBytes);
        Assert.Equal(4,        fields[0].SizeBytes);
        Assert.Equal("Health", fields[1].Name);
        Assert.Equal(4,        fields[1].OffsetBytes);
    }

    [Fact]
    public void DebugMap_StateLayout_IsPreservedThroughSerializationRoundTrip()
    {
        var map = new DebugMap
        {
            AssetId     = AssetId,
            StateLayout = new DebugStateLayout
            {
                Fields = new[]
                {
                    new StateLayoutField("Speed", "System.Single", 0, 4),
                    new StateLayoutField("HP",    "System.Int32",  4, 4),
                },
            },
        };

        var json     = DebugMapSerializer.Serialize(map);
        var restored = DebugMapSerializer.Deserialize(json);

        Assert.NotNull(restored);
        var fields = restored!.StateLayout.Fields;
        Assert.Equal(2, fields.Count);
        Assert.Equal("Speed", fields[0].Name);
        Assert.Equal(0,       fields[0].OffsetBytes);
        Assert.Equal(4,       fields[0].SizeBytes);
        Assert.Equal("HP",    fields[1].Name);
        Assert.Equal(4,       fields[1].OffsetBytes);
    }

    // ---- BPF-021: GeneratedSourcePath -----------------------------------

    [Fact]
    public void DebugMap_GeneratedSourcePath_IsPreservedThroughSerializationRoundTrip()
    {
        var map = new DebugMap
        {
            AssetId             = AssetId,
            GeneratedSourcePath = @"C:\Generated\MyBlueprint.g.cs",
        };

        var json     = DebugMapSerializer.Serialize(map);
        var restored = DebugMapSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(@"C:\Generated\MyBlueprint.g.cs", restored!.GeneratedSourcePath);
    }

    [Fact]
    public void DebugMapIndex_GeneratedSourcePath_MatchesMap()
    {
        var map = new DebugMap
        {
            AssetId             = AssetId,
            GeneratedSourcePath = @"C:\Generated\MyBlueprint.g.cs",
        };
        var index = new DebugMapIndex(map);

        Assert.Equal(@"C:\Generated\MyBlueprint.g.cs", index.GeneratedSourcePath);
    }
}

// ---- BPF-003: Breakpoint hash safety + per-frame dedup ----------------------

/// <summary>Tests for BPF-003: AssetStructureHashAtSetTime, IsStale, OnNewTick dedup.</summary>
[Collection(nameof(DebugProbeCollection))]
public sealed class BreakpointHashSafetyTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity E1 => new Entity(1, 0);
    private static Entity E2 => new Entity(2, 0);

    private static BlueprintDebugSession MakeSession(ISimulationView? view = null)
        => new BlueprintDebugSession(
            new BlueprintRegistry(),
            view ?? new StubSimulationView(),
            new MockTimeController());

    private static DebugMap MakeMap(Guid assetId, ulong structureHash)
        => new DebugMap { AssetId = assetId, StructureHash = structureHash };

    private sealed class StubSimulationView : ISimulationView
    {
        public uint  Tick { get; set; }
        public float Time => 0f;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public bool IsAlive(Entity e) => true;
        public bool HasComponent<T>(Entity e) where T : unmanaged => false;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
        public QueryBuilder Query() => throw new NotImplementedException();
        public IReadOnlyList<T> ReadManagedEvents<T>() => Array.Empty<T>();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    [Fact]
    public void SetBreakpoint_CapturesStructureHash_WhenMapRegistered()
    {
        var session = MakeSession();
        session.RegisterDebugMap(MakeMap(AssetIdA, 0xDEADBEEF_00000001UL));

        var id = session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        var bp = session.GetBreakpoints().Single(b => b.Id == id);
        Assert.Equal(0xDEADBEEF_00000001UL, bp.AssetStructureHashAtSetTime);
        Assert.False(bp.IsStale);
    }

    [Fact]
    public void SetBreakpoint_StoresZeroHash_WhenNoMapRegistered()
    {
        var session = MakeSession();

        var id = session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        var bp = session.GetBreakpoints().Single(b => b.Id == id);
        Assert.Equal(0UL, bp.AssetStructureHashAtSetTime);
    }

    [Fact]
    public void RegisterDebugMap_WithChangedHash_MarksExistingBreakpointsStale()
    {
        var session = MakeSession();
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111_1111UL));
        var id = session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        // Re-register with different hash.
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x2222_2222UL));

        var bp = session.GetBreakpoints().Single(b => b.Id == id);
        Assert.True(bp.IsStale);
    }

    [Fact]
    public void RegisterDebugMap_WithSameHash_DoesNotMarkBreakpointsStale()
    {
        var session = MakeSession();
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111_1111UL));
        var id = session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        // Re-register with same hash.
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111_1111UL));

        var bp = session.GetBreakpoints().Single(b => b.Id == id);
        Assert.False(bp.IsStale);
    }

    [Fact]
    public void StaleBreakpoint_DoesNotPause()
    {
        var session = MakeSession();
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111_1111UL));
        var id = session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        session.RegisterDebugMap(MakeMap(AssetIdA, 0x2222_2222UL)); // marks stale

        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        Assert.False(session.IsPaused);
    }

    [Fact]
    public void OnNewTick_ResetsDedupSet_AllowingSecondTickHit()
    {
        var view    = new StubSimulationView { Tick = 1 };
        var session = MakeSession(view);
        var pauseCount = 0;
        session.OnBreakpointHit += _ => pauseCount++;

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        // First tick: E1 hits -- pauses once.
        var nodeIdStr = NodeId1.ToString("D");
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, nodeIdStr);
        Assert.Equal(1, pauseCount);
        Assert.True(session.IsPaused);

        // Same tick, E2 hits while paused -- no new pause (dedup + already paused),
        // but HitCount must still accumulate (BPF-003 / CORR-02-2).
        ((IBlueprintProbeSink)session).OnNodeEnter(E2, nodeIdStr);
        Assert.Equal(1, pauseCount);
        Assert.Equal(2, session.GetBreakpoints()[0].HitCount);

        // Advance tick and call OnNewTick to reset dedup set, then continue.
        session.Continue();
        session.OnNewTick();
        view.Tick = 2;

        // New tick: E1 hits again -- should get a fresh pause.
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, nodeIdStr);
        Assert.Equal(2, pauseCount);
    }

    [Fact]
    public void OnBreakpointListChanged_FiredWhenMapHashChanges()
    {
        var session    = MakeSession();
        Guid? changed  = null;
        session.OnBreakpointListChanged += g => changed = g;

        session.RegisterDebugMap(MakeMap(AssetIdA, 0x1111_1111UL));
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);

        session.RegisterDebugMap(MakeMap(AssetIdA, 0x2222_2222UL));

        Assert.Equal(AssetIdA, changed);
    }
}

// ---- BPF-004: Peer-call probe signature ------------------------------------

/// <summary>Tests for BPF-004: OnPeerCallEnter/Exit with Guid-based asset id.</summary>
[Collection(nameof(DebugProbeCollection))]
public sealed class PeerCallProbeTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static Entity E1 => new Entity(1, 0);

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
        public bool IsAlive(Entity e) => true;
        public bool HasComponent<T>(Entity e) where T : unmanaged => false;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
        public QueryBuilder Query() => throw new NotImplementedException();
        public IReadOnlyList<T> ReadManagedEvents<T>() => Array.Empty<T>();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    [Fact]
    public void OnPeerCallEnter_WithValidGuidString_IncreasesCallDepth()
    {
        var session = MakeSession();
        var assetIdStr = AssetIdA.ToString("D");

        ((IBlueprintProbeSink)session).OnPeerCallEnter(E1, assetIdStr, "Update");
        // Entity is now in active set for AssetIdA.
        var active = session.GetActiveEntities(AssetIdA);

        Assert.Contains(E1, active);
    }

    [Fact]
    public void OnPeerCallExit_WithValidGuidString_RemovesFromActiveEntities()
    {
        var session    = MakeSession();
        var assetIdStr = AssetIdA.ToString("D");

        ((IBlueprintProbeSink)session).OnPeerCallEnter(E1, assetIdStr, "Update");
        ((IBlueprintProbeSink)session).OnPeerCallExit(E1, assetIdStr, "Update");

        var active = session.GetActiveEntities(AssetIdA);
        Assert.DoesNotContain(E1, active);
    }

    [Fact]
    public void OnPeerCallEnter_WithInvalidGuidString_FallsBackToGuidEmpty()
    {
        var session = MakeSession();

        // Should not throw; falls back to Guid.Empty for active entity tracking.
        ((IBlueprintProbeSink)session).OnPeerCallEnter(E1, "not-a-guid", "Update");

        var active = session.GetActiveEntities(Guid.Empty);
        Assert.Contains(E1, active);
    }
}

// ---- BPF-005: StepOut tick boundary + entity death -------------------------

/// <summary>Tests for BPF-005: StepOut at depth 0 and entity-death abandonment.</summary>
[Collection(nameof(DebugProbeCollection))]
public sealed class StepOutEdgeCaseTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity E1 => new Entity(1, 0);

    private static BlueprintDebugSession MakeSession(TickSimulationView view)
        => new BlueprintDebugSession(new BlueprintRegistry(), view, new MockTimeController());

    private sealed class TickSimulationView : ISimulationView
    {
        public uint  Tick        { get; set; }
        public float Time        => 0f;
        public bool  EntityAlive { get; set; } = true;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
            => throw new NotImplementedException();
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public bool IsAlive(Entity e) => EntityAlive;
        public bool HasComponent<T>(Entity e) where T : unmanaged => false;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
        public QueryBuilder Query() => throw new NotImplementedException();
        public IReadOnlyList<T> ReadManagedEvents<T>() => Array.Empty<T>();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    [Fact]
    public void StepOut_AtDepthZero_PausesOnNextTickBoundary()
    {
        var view    = new TickSimulationView { Tick = 1 };
        var session = MakeSession(view);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));
        Assert.True(session.IsPaused);

        // Step out from depth 0.
        session.StepOut();
        Assert.False(session.IsPaused);

        // Same tick: should NOT re-pause (Tick == _stepFromTick).
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "some-node");
        Assert.False(session.IsPaused);

        // Advance tick and fire again -- should re-pause.
        view.Tick = 2;
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "next-tick-node");
        Assert.True(session.IsPaused);
    }

    [Fact]
    public void StepOut_EntityDies_AbandonsStepping()
    {
        var view    = new TickSimulationView { Tick = 1, EntityAlive = true };
        var session = MakeSession(view);

        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        session.StepOut();
        Assert.False(session.IsPaused);

        // Entity dies between ticks.
        view.EntityAlive = false;
        view.Tick = 2;

        // Probe fires for the same entity (simulating a probe that squeaks through).
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, "ghost-node");

        // Step should have been abandoned -- session must not be paused.
        Assert.False(session.IsPaused);
    }
}

// ---- BPF-001: GetCurrentStateSnapshot returns populated data ----------------

/// <summary>Tests for BPF-001: GetCurrentStateSnapshot returns AssetName and dispatch kind.</summary>
[Collection(nameof(DebugProbeCollection))]
public sealed class StateSnapshotTests
{
    private static readonly Guid AssetIdA = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GraphId1 = new Guid("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NodeId1  = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Entity E1 => new Entity(1, 0);

    private static BlueprintDebugSession MakeSession(BlueprintRegistry? reg = null)
        => new BlueprintDebugSession(
            reg ?? new BlueprintRegistry(),
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
        public bool IsAlive(Entity e) => true;
        public bool HasComponent<T>(Entity e) where T : unmanaged => false;
        public bool HasManagedComponent<T>(Entity e) where T : class => false;
        public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged => ReadOnlySpan<T>.Empty;
        public QueryBuilder Query() => throw new NotImplementedException();
        public IReadOnlyList<T> ReadManagedEvents<T>() => Array.Empty<T>();
        public IEntityCommandBuffer GetCommandBuffer() => throw new NotImplementedException();
    }

    [Fact]
    public void GetCurrentStateSnapshot_WhenPaused_ReturnsAssetName_FromDebugMap()
    {
        var session = MakeSession();
        session.RegisterDebugMap(new DebugMap
        {
            AssetId   = AssetIdA,
            AssetName = "MyBlueprint",
        });
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        var snap = session.GetCurrentStateSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(E1,           snap!.Self);
        Assert.Equal(AssetIdA,     snap.AssetId);
        Assert.Equal("MyBlueprint", snap.AssetName);
    }

    [Fact]
    public void GetCurrentStateSnapshot_WhenPaused_ReturnsDispatchKind_Library()
    {
        var reg     = new BlueprintRegistry();
        var bpId    = BlueprintIdHash.Compute(AssetIdA);
        reg.RegisterLibrary(bpId, "MyLib");

        var session = MakeSession(reg);
        session.RegisterDebugMap(new DebugMap { AssetId = AssetIdA, AssetName = "MyLib" });
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        var snap = session.GetCurrentStateSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(BlueprintDispatchKind.Library, snap!.Dispatch);
        Assert.NotNull(snap.FieldValues);
        Assert.Empty(snap.FieldValues);
        Assert.Null(snap.Cursor);
    }

    [Fact]
    public void GetCurrentStateSnapshot_WhenNotPaused_ReturnsNull()
    {
        var session = MakeSession();
        var snap = session.GetCurrentStateSnapshot();
        Assert.Null(snap);
    }

    [Fact]
    public void GetCurrentStateSnapshot_AssetIdFallback_WhenNoMapAndNoRegistry()
    {
        var session = MakeSession();
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        var snap = session.GetCurrentStateSnapshot();

        Assert.NotNull(snap);
        // AssetName should be Guid fallback string when nothing is registered.
        Assert.Equal(AssetIdA.ToString("D"), snap!.AssetName);
    }

    // ---- CORR-02-1: AiPrimitive field values from Blackboard1024 (BPF-001 §8.6) --

    [Fact]
    public unsafe void GetCurrentStateSnapshot_AiPrimitive_ReturnsFieldValue_WhenHashMatches()
    {
        const ulong StructureHash = 0xDEAD_BEEF_CAFE_1234UL;
        const float Speed         = 3.14f;

        var def = new BlueprintDefinition
        {
            Name          = "TestPrimitive",
            Kind          = BlueprintDispatchKind.AiPrimitive,
            StructureHash = StructureHash,
            StateSize     = 4,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                ["Speed"] = new BlueprintFieldDescriptor("Speed", typeof(float), 0, 4, ""),
            },
        };
        var reg  = new BlueprintRegistry();
        reg.RegisterAiPrimitive(BlueprintIdHash.Compute(AssetIdA), def);

        var view    = new BlackboardStubSimulationView(StructureHash, Speed);
        var session = MakeSession(reg, view);
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        var snap = session.GetCurrentStateSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(BlueprintDispatchKind.AiPrimitive, snap!.Dispatch);
        Assert.True(snap.FieldValues.ContainsKey("Speed"), "FieldValues must contain 'Speed'");
        Assert.Equal(Speed, (float)snap.FieldValues["Speed"]);
    }

    [Fact]
    public unsafe void GetCurrentStateSnapshot_AiPrimitive_ReturnsEmptyFields_WhenHashMismatches()
    {
        const ulong DefinitionHash = 0xAAAA_BBBB_CCCC_DDDDUL;
        const ulong BlackboardHash = 0x1111_2222_3333_4444UL; // different hash
        const float Speed          = 9.99f;

        var def = new BlueprintDefinition
        {
            Name          = "TestPrimitive",
            Kind          = BlueprintDispatchKind.AiPrimitive,
            StructureHash = DefinitionHash,
            StateSize     = 4,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                ["Speed"] = new BlueprintFieldDescriptor("Speed", typeof(float), 0, 4, ""),
            },
        };
        var reg  = new BlueprintRegistry();
        reg.RegisterAiPrimitive(BlueprintIdHash.Compute(AssetIdA), def);

        var view    = new BlackboardStubSimulationView(BlackboardHash, Speed);
        var session = MakeSession(reg, view);
        session.SetBreakpoint(AssetIdA, GraphId1, NodeId1);
        ((IBlueprintProbeSink)session).OnNodeEnter(E1, NodeId1.ToString("D"));

        var snap = session.GetCurrentStateSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(BlueprintDispatchKind.AiPrimitive, snap!.Dispatch);
        Assert.Empty(snap.FieldValues);
    }

    private static BlueprintDebugSession MakeSession(BlueprintRegistry reg, ISimulationView view)
        => new BlueprintDebugSession(reg, view, new MockTimeController());

    // Provides a Blackboard1024 with the supplied structure hash in bytes 0-7
    // and a float Speed value in bytes 8-11.
    private sealed unsafe class BlackboardStubSimulationView : ISimulationView
    {
        private Blackboard1024 _bb;

        public BlackboardStubSimulationView(ulong structureHash, float speed)
        {
            fixed (Blackboard1024* p = &_bb)
            {
                byte* bytes = (byte*)p;
                *(ulong*)bytes         = structureHash;
                *(float*)(bytes + 8)   = speed;
            }
        }

        public uint  Tick => 0;
        public float Time => 0f;
        public bool  IsAlive(Entity e)         => true;
        public bool  HasComponent<T>(Entity e)        where T : unmanaged => typeof(T) == typeof(Blackboard1024);
        public bool  HasManagedComponent<T>(Entity e) where T : class     => false;
        public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged
        {
            if (typeof(T) == typeof(Blackboard1024))
                return ref Unsafe.As<Blackboard1024, T>(ref _bb);
            throw new NotImplementedException();
        }
        public T GetManagedComponentRO<T>(Entity e) where T : class
            => throw new NotImplementedException();
        public ReadOnlySpan<T>    ReadEvents<T>()        where T : unmanaged => ReadOnlySpan<T>.Empty;
        public QueryBuilder       Query()                                    => throw new NotImplementedException();
        public IReadOnlyList<T>   ReadManagedEvents<T>()                     => Array.Empty<T>();
        public IEntityCommandBuffer GetCommandBuffer()                       => throw new NotImplementedException();
    }
}

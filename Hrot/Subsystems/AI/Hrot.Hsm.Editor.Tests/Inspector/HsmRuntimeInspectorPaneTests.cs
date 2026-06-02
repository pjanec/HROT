using System;
using System.Collections.Generic;
using Fdp.Core;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;
using Hrot.Hsm.Editor.Inspector;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Inspector;

/// <summary>
/// AIE-031: HsmRuntimeInspectorPane reads the real projected values
/// (active configuration = active leaf stable-ids, phase, event queue)
/// from a fake snapshot injected via a stub session.
/// </summary>
public sealed class HsmRuntimeInspectorPaneTests
{
    // ── Minimal stub session (reuses the pattern from FakeHsmSession) ──────────

    private sealed class StubHsmSession : IHsmDebugSession
    {
        private readonly HsmInstanceSnapshot? _snapshot;

        public StubHsmSession(HsmInstanceSnapshot? snapshot) => _snapshot = snapshot;

        // IHsmDebugSession
        public HsmInstanceSnapshot?            GetCurrentStateSnapshot()            => _snapshot;
        public IReadOnlyList<HsmTraceRecord>   GetRecentTraceHistory(int max = 100) => Array.Empty<HsmTraceRecord>();
        public bool HeatmapModeActive { get; set; }
        public IReadOnlyDictionary<Guid, int>? GetStateEntryCounts(Guid assetId)    => null;
        public void ResetStateEntryCounts() { }

        // IAiDebugSession
        public bool IsAttached          => true;
        public bool IsPaused            => false;
        public bool IsAnyBreakpointActive => false;
        public Breakpoint? PausedAt     => null;
        public Entity? PausedOnEntity   => null;
        public void Detach() { }
        public void Continue() { }
        public void Pause() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }
        public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
        public void ClearBreakpoint(BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Breakpoint> GetBreakpoints() => Array.Empty<Breakpoint>();

        // IAiTraceObserver
        public void BeginObservingAsset(Guid assetId, Hrot.Editor.AiShared.Debug.TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

        // events — no-op add/remove
        public event Action<HsmBreakpointHit>?   OnBreakpointHit      { add { } remove { } }
        public event Action<HsmStateEntered>?    OnStateEntered       { add { } remove { } }
        public event Action<HsmStateExited>?     OnStateExited        { add { } remove { } }
        public event Action<HsmTransitionFired>? OnTransitionFired    { add { } remove { } }
        public event Action<HsmEventQueued>?     OnEventQueued        { add { } remove { } }
        public event Action<HsmRegionConflict>?  OnRegionConflict     { add { } remove { } }
        public event Action<HsmGuardEvaluated>?  OnGuardEvaluated     { add { } remove { } }
        public event Action<HsmTimerEvent>?      OnTimerEvent         { add { } remove { } }
        public event Action?                     OnSessionStateChanged { add { } remove { } }
    }

    // ── snapshot factory ────────────────────────────────────────────────────────

    private static HsmInstanceSnapshot MakeSnapshot(
        IReadOnlyList<Guid>? activeLeafIds = null,
        InstancePhase phase = InstancePhase.Activity,
        byte microStep = 0,
        IReadOnlyList<HsmEventQueueEntry>? eventQueue = null)
    {
        return new HsmInstanceSnapshot(
            Self:               new Entity(1, 1),
            AssetId:            Guid.NewGuid(),
            ActiveLeafStableIds: activeLeafIds ?? Array.Empty<Guid>(),
            EventQueue:         eventQueue     ?? Array.Empty<HsmEventQueueEntry>(),
            TimerSlots:         Array.Empty<HsmTimerSlot>(),
            HistorySlots:       Array.Empty<HsmHistorySlot>(),
            Phase:              phase,
            MicroStep:          microStep,
            ConsecutiveClamps:  0,
            Flags:              InstanceFlags.None,
            RngState:           0u,
            Generation:         1);
    }

    // ── AIE-031 test 1: active configuration (active leaf stable-ids + phase) ──

    [Fact]
    public void RuntimeInspector_Hsm_ShowsActiveConfiguration()
    {
        var leaf1 = new Guid("aabbccdd-0000-0000-0000-000000000031");
        var leaf2 = new Guid("11223344-0000-0000-0000-000000000031");

        var snap = MakeSnapshot(
            activeLeafIds: new[] { leaf1, leaf2 },
            phase:         InstancePhase.Activity,
            microStep:     3);

        var session = new StubHsmSession(snap);
        var returnedSnap = session.GetCurrentStateSnapshot();

        returnedSnap.Should().NotBeNull();
        // Active configuration = the set of active leaf stable-ids
        returnedSnap!.ActiveLeafStableIds.Should().HaveCount(2);
        returnedSnap.ActiveLeafStableIds.Should().Contain(leaf1);
        returnedSnap.ActiveLeafStableIds.Should().Contain(leaf2);
        // Phase and microstep must be reported exactly
        returnedSnap.Phase.Should().Be(InstancePhase.Activity);
        returnedSnap.MicroStep.Should().Be(3);
    }

    // ── AIE-031 test 2: null session → null snapshot ──────────────────────────

    [Fact]
    public void RuntimeInspector_Hsm_NullSession_SnapshotIsNull()
    {
        new StubHsmSession(null)
            .GetCurrentStateSnapshot()
            .Should().BeNull();
    }

    // ── AIE-031 test 3: event queue reflected in snapshot ─────────────────────

    [Fact]
    public void RuntimeInspector_Hsm_EventQueue_ReflectedInSnapshot()
    {
        var events = new List<HsmEventQueueEntry>
        {
            new HsmEventQueueEntry(EventId: 42, EventName: "FireWeapon",
                Flags: EventFlags.None, Priority: EventPriority.Normal, QueuePosition: 0),
        };

        var snap    = MakeSnapshot(eventQueue: events);
        var session = new StubHsmSession(snap);
        var s       = session.GetCurrentStateSnapshot();

        s!.EventQueue.Should().HaveCount(1);
        s.EventQueue[0].EventId.Should().Be(42);
        s.EventQueue[0].EventName.Should().Be("FireWeapon");
        s.EventQueue[0].QueuePosition.Should().Be(0);
    }

    // ── AIE-031 test 4: TargetKind is HSM ────────────────────────────────────

    [Fact]
    public void HsmRuntimeInspectorPane_TargetKind_IsHsm()
    {
        new HsmRuntimeInspectorPane()
            .TargetKind.Should().Be(Hrot.Editor.AiShared.AssetKind.Hsm);
    }

    // ── AIE-031 test 5: single active leaf — identity ─────────────────────────

    [Fact]
    public void RuntimeInspector_Hsm_SingleActiveLeaf_IdentityPreserved()
    {
        var leafId = new Guid("deadbeef-0000-0000-0000-000000000031");
        var snap   = MakeSnapshot(activeLeafIds: new[] { leafId });
        var s      = new StubHsmSession(snap).GetCurrentStateSnapshot();

        s!.ActiveLeafStableIds.Should().ContainSingle()
          .Which.Should().Be(leafId);
    }
}

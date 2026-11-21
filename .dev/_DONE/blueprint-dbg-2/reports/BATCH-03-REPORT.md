# BATCH-03 Report

**Branch:** blueprint-integ-1
**Date:** 2026-06-10
**Author:** coder sub-agent

---

## Tasks Completed

### CT0a (P1): Entity-scope sub-tick recording

**Field:** `_recordingEntity` added to `BlueprintDebugSession`.

**Gate in `OnNodeEnter`:** `IsRecordingEntity(self)` checks three cases in priority order:
1. If `_recordingEntity` is set (locked on pause) → only that entity passes.
2. Else if `_entityFilter` is set → only that entity passes.
3. Else → all entities pass (single-entity test case).

`_recordingEntity` is set in `HandleBreakpointHit` when a pause occurs and cleared in `Continue()`.

**Verification:** `VirtualPointerTests.EntityScope_TwoEntities_OnlyDebuggedEntityRecorded` passes — only entityA's nodes are recorded even when both entityA and entityB blueprints run in the same tick.

---

### CT0b (P2): Tighten test assertion

`SubTickRecorderIntegrationTests.RecordingOn_WhenArmed_PerNodeValuesAreDifferentWithinOneTick` now asserts:

```csharp
Assert.Equal(10, countAtLastNode);
```

This replaced the weaker `Assert.True(countAtLastNode < finalCount)` from BATCH-02. The exact value 10 proves that the snapshot before the `Then1` block captures precisely the state after `Then0` ran (A=10) and before `Then1` ran (which would write A=20).

---

### NGS-2.1: Virtual pointer (`_nodePointer`)

**Fields added:**
- `int _nodePointer` — ring index, -1 when inactive.
- `Entity? _recordingEntity` — entity scope lock (shared with CT0a).

**Methods added to `BlueprintDebugSession`:**
- `StepBack()` — decrements `_nodePointer`, clamped at 0. Calls `RestorePointerToScratch()`.
- `StepForwardOrCF6(StepMode)` — if recordings exist (`_nodePointer >= 0 && _recorder.Count > 0`), increments pointer clamped at last index. Falls back to CF-6 temp-BP stepping when no recordings exist.
- `InitNodePointerOnPause(pausedNodeId)` — finds the ring index whose node-id matches the paused probe. Called inside `HandleBreakpointHit` (during tick). Deliberately does NOT call `RestorePointerToScratch()` to avoid corrupting in-flight tick state.
- `RestorePointerToScratch()` — syncs `_scratchRepo` from `_liveRepo` then applies `_recorder.RestoreTo(_nodePointer)`. Only called outside the tick (from `StepBack`, `StepForwardOrCF6`, and lazily from `CaptureStateSnapshot`).
- `CurrentNodePointer` property — returns `_nodePointer`.
- `CurrentNodeId` property — returns `_recorder.NodeIdAt(_nodePointer)` when valid, null otherwise.
- `RecordedNodeCount` property — returns `_recorder.Count`.

**Clamping:** `StepBack` at 0 is a no-op. `StepForwardOrCF6` at last index is a no-op (per-BATCH-03 design; BATCH-04 bridges the last-to-real-tick transition).

**Interface additions to `IBlueprintDebugSession`:**
- `void StepBack()`
- `int CurrentNodePointer { get; }`
- `string? CurrentNodeId { get; }`
- `int RecordedNodeCount { get; }`

---

### NGS-2.2: Scratch `EntityRepository` for inspector redirect

**Field:** `EntityRepository? _scratchRepo` added to `BlueprintDebugSession`.

`_scratchRepo` is allocated lazily in `RestorePointerToScratch()` on first use: `new EntityRepository()` with the same component types as `_liveRepo` (via `SyncFrom`). Disposed in `Detach()` and when `IDisposable` teardown occurs.

`CaptureStateSnapshot()` redirects inspection to `_scratchRepo` when `_nodePointer >= 0`:

```csharp
if (_nodePointer >= 0) RestorePointerToScratch();  // lazy restore
ISimulationView inspectionView = (_nodePointer >= 0 && _scratchRepo != null)
    ? (ISimulationView)_scratchRepo
    : _view;
```

This means `GetCurrentStateSnapshot()` returns per-node sub-tick state while paused with the pointer active, and reverts to live view after `Continue()`.

---

### Root cause fix: Keyframe-per-node recording

**Problem discovered:** `SubTickSnapshotRecorder` previously used delta-frame recording. Blueprint `SetVar` nodes write directly into the `BlueprintBlackboard1024` span via pointer arithmetic (not via `GetComponentRW`), so chunk versions are NEVER updated by those writes. `HasChunkChanged` checks `chunk.version > prevVersion`; since version never advances, delta detection always returns "no changes" for blueprint variable writes.

**Fix:** `RecordNodeEntry` now calls `_recorder.RecordKeyframe(repo, writer, wallClockTicks: 0L)` — a full repo snapshot per node. `RestoreTo` applies a single keyframe directly. This bypasses all chunk-version tracking.

**Ring layout:** Each slot stores `(nodeId, snapshotBytes[])` where `snapshotBytes` is the full repo state at the moment the probe fires (= before that node's own writes). The ordering is:
- `snapshot[0]` (for n0): state before n0 ran → initial tick state.
- `snapshot[1]` (for n1): state after n0 wrote, before n1 ran.
- Restore to index K = apply snapshot[K].

---

### Root cause fix: Breakpoint probe-id mismatch

**Problem discovered during testing:** `VirtualPointerTests` set breakpoints on `asset.Graphs[0].Nodes[0].Id` (EventEntryNode). The compiler (`Stage5_Schedule.ScheduleSequenceNode`) overwrites the entry block's `SourceNodeId` with the `SequenceNode.Id` when the EventEntry is immediately followed by a Sequence node in the same block. So the probe fires with `seqNode.Id`, not `entryNode.Id`.

Without a registered `DebugMap`, the session cannot re-key the authored-node-id → probe-id mapping (that re-keying happens only via `RegisterDebugMap → ReResolveBreakpointsForAsset`). The test's breakpoint stayed under `EventEntry.Id` but the probe fired with `Sequence.Id` → mismatch → no pause.

**Fix:** Changed `VirtualPointerTests` to set breakpoints on `Nodes[1].Id` (the SequenceNode), which matches the actual probe identity emitted for this graph structure. The comment explains the rationale in each test.

---

## Test Results

| Suite | Before BATCH-03 | After BATCH-03 |
|---|---|---|
| `Hrot.Blueprints.Tests` | 7 pre-existing failures | 7 pre-existing failures |
| `VirtualPointerTests` (5 new) | 5 fail | **5 pass** |
| `SubTickRecorderIntegrationTests` (4) | 4 pass | 4 pass |
| `Hrot.Diagnostics.Breakpoints.Tests` | 128 pass | 128 pass |

Pre-existing failures (unchanged):
- `WhenNode_ZeroAllocOnHotPath`
- `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`
- `AiPrimitive_EmitMatchesGoldenSource` (MoveToAndFire, HasVisibleTarget)
- `Stage8_PdbContainsEmbeddedSource`
- `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`
- `MoveToAndFireDemoTests.MoveToAndFire_GeneratedSource_Snapshot`

---

## Files Modified

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/SubTickSnapshotRecorder.cs` — keyframe-per-node strategy (replaces delta-frame)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` — added `StepBack`, `CurrentNodePointer`, `CurrentNodeId`, `RecordedNodeCount`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — `_nodePointer`, `_scratchRepo`, `_recordingEntity`; `StepBack`/`StepForwardOrCF6`/`InitNodePointerOnPause`/`RestorePointerToScratch`; lazy restore in `CaptureStateSnapshot`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/SubTickRecorderIntegrationTests.cs` — CT0b: `Assert.Equal(10, countAtLastNode)`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/VirtualPointerTests.cs` — NEW: 5 integration tests for NGS-2.1, NGS-2.2, CT0a; breakpoints set on SequenceNode (probe identity)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/CapturingDebugSession.cs` — stub implementations for new interface members
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/MockDebugSession.cs` — stub implementations
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/DebugWindowDrawUITests.cs` — stub `SpyDebugSession` members

---

## Design Notes and Debt

### Deferred: Lazy `_scratchRepo` type registration

`RestorePointerToScratch()` allocates `_scratchRepo = new EntityRepository()` and calls `_liveRepo.SyncFrom()`. The `SyncFrom` copies component registrations from `_liveRepo`. This is correct but creates an allocation on first use post-pause. Tracked as debt for future optimization if needed.

### Deferred: BATCH-04 bridge (real-tick advance from last recording)

When `_nodePointer` is at the last recorded index and `StepForwardOrCF6` is called, it is a no-op in BATCH-03. The production stepping behavior (advance one real tick and pause on the next exec node) is out of scope for BATCH-03.

### Observation: Probe-node identity for Sequence entry blocks

`Stage5_Schedule.ScheduleSequenceNode` (line 482) forces the entry block's `SourceNodeId = seq.Id`, overwriting the EventEntryNode's id. This means:
- Setting a breakpoint on an EventEntryNode that directly connects to a SequenceNode will not fire unless the authored id is re-keyed via `RegisterDebugMap` → `ReResolveBreakpointsForAsset`.
- Tests that do not register a DebugMap must set breakpoints on the Sequence node's id directly.
- This is not a bug — it is by design (the Sequence is the "owning" exec node for the dispatch block). The `BreakpointTargets` map correctly maps EventEntry → Sequence probe for UI-driven breakpoints. Tests that bypass the full CF-7-rev pipeline must account for this.

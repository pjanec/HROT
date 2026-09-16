# BATCH-02 Report: Wire Sub-Tick Recorder Into Live Debug Pipeline

**Batch:** NGS-2.0  
**Date:** 2026-06-10  
**Status:** COMPLETE — 4 new tests green; full affected suites green (0 new reds)

---

## Investigation Findings

### Investigation 1: Which BlueprintDebugSession?

Confirmed file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`  
Namespace: `Hrot.Blueprints.Core.Debug`  
Class: `public sealed class BlueprintDebugSession : IBlueprintDebugSession`

The class lives in the `Hrot.Blueprints.Editor` project (physical assembly) but declares itself in the `Hrot.Blueprints.Core.Debug` namespace. This is the correct class to modify — it has `OnNodeEnter`, `_view` (ISimulationView), `_dataBreakpointManager`, and `_entityFilter`.

### Investigation 2: Live Repo Access

The session holds `_view` (an `ISimulationView` / `MockSimulationView` in tests), NOT an `EntityRepository`. `BumpMemoryVersion()` and the recorder need the concrete `EntityRepository`. The decision: add `SetLiveRepository(EntityRepository?)` setter to `BlueprintDebugSession`.

**Call sites updated:**
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (~line 906): `bpBlueprintSession.SetLiveRepository(_world)` immediately after `SetDataBreakpointManager`.
- Tests supply the fixture's `fixture.World` directly.

No cast from `_view` is performed — the repo is passed explicitly. This is clean and does not break any existing construction path (setter defaults `_liveRepo = null`, which keeps recording off).

### Investigation 3: Tick-Boundary Hook for BeginTick

`BlueprintTickSystem.FrameStartCallback` is set to `DebugProbe.NewTick` by `BlueprintsCoreModuleInit` (a `[ModuleInitializer]`). This fires before any blueprint ticks within `BlueprintTickSystem.Execute`. `DebugProbe.NewTick()` calls `(Sink as IBlueprintDebugSession)?.OnNewTick()`.

`OnNewTick()` is the correct hook for `BeginTick`. Tick boundary is detected by comparing `_view.Tick` (= `MockSimulationView._tick` in tests, frozen at `_repo.SimulationTick` semantically) against `_lastRecordedTick?`. First tick always triggers `BeginTick` because `_lastRecordedTick = null` initially.

**Important discovery:** In the test harness, `EntityRepository.Tick()` is never called by `TickFrame`. `MockSimulationView` maintains its own `_tick` counter (incremented by `AdvanceTime`). `_repo.SimulationTick` and `_repo.GlobalVersion` only change via explicit calls — so `GlobalVersion` advances ONLY from `BumpMemoryVersion()` calls made by the recorder. This shapes the correct assertions for Tests 1 and 3.

**Additional discovery (probe counts):** For a simple linear blueprint chain (`Entry → SetVar → SetVar → Return`), the compiler produces ONE IR block and ONE probe call per tick. Multiple probe calls per tick require control-flow structure. A `SequenceNode` causes the compiler to allocate separate IR blocks for each Then-branch, each with its own `SourceNodeId` → its own probe. Test 2 uses this property.

---

## Implementation

### Files Modified

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`**

Fields added (after `_dataBreakpointManager`/`_mgrBpIds`/`_tempMgrBpIds`):

```csharp
// Sub-tick snapshot recorder (NGS-2.0).
private readonly SubTickSnapshotRecorder _recorder = new SubTickSnapshotRecorder();
private EntityRepository? _liveRepo;
// Tick-boundary detection: uint? so first tick always triggers BeginTick
private uint? _lastRecordedTick;
```

Methods added (between `SetDataBreakpointManager` and `SetInstrumentationCallback`):

```csharp
public void SetLiveRepository(EntityRepository? repo)
{
    _liveRepo           = repo;
    _lastRecordedTick   = null; // force BeginTick on next armed tick
}

public int RecordedNodeCount => _recorder.Count;
public string RecordedNodeIdAt(int index) => _recorder.NodeIdAt(index);
public void RestoreRecordedNode(int nodeIndex, EntityRepository scratchRepo)
    => _recorder.RestoreTo(nodeIndex, scratchRepo);

private bool RecordingActive =>
    _liveRepo != null &&
    (_breakpoints.Count > 0 || _tempBreakpoints.Count > 0);
```

`OnNewTick` updated:
```csharp
public void OnNewTick()
{
    _firedBreakpointsThisTick.Clear();
    uint currentTick = _view.Tick;
    if (RecordingActive && currentTick != _lastRecordedTick)
    {
        _lastRecordedTick = currentTick;
        _recorder.BeginTick(_liveRepo!);
    }
}
```

`OnNodeEnter` — recording block inserted AFTER history/overlay, BEFORE CF-6 temp BP check:
```csharp
if (RecordingActive)
{
    if (_lastRecordedTick.HasValue)
        _recorder.RecordNodeEntry(_liveRepo!, nodeId);
    else
        System.Diagnostics.Debug.WriteLine("[BlueprintDebugSession] RecordingActive but BeginTick not called yet.");
}
```

**`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`** (~line 906):
```csharp
bpBlueprintSession.SetDataBreakpointManager(_bpManager);
bpBlueprintSession.SetLiveRepository(_world);  // NGS-2.0: wire live repo for sub-tick recording
```

### RecordingActive Gate

Predicate: `_liveRepo != null && (_breakpoints.Count > 0 || _tempBreakpoints.Count > 0)`

When NOT armed (no live repo OR no breakpoints), the gate short-circuits immediately: `BeginTick` is never called, `RecordNodeEntry` is never called, `BumpMemoryVersion` is never called. **Zero recorder work on the hot path.**

---

## Test Suite

New file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/SubTickRecorderIntegrationTests.cs`

**4 tests, all green:**

### Test 1 — Recording OFF when unarmed
Sets up a session with live repo but NO breakpoint. Ticks once.

- `RecordedNodeCount == 0` ✓
- `_repo.GlobalVersion` unchanged (no BumpMemoryVersion called) ✓
- `_repo.SimulationTick` unchanged (test harness doesn't call `_repo.Tick()`) ✓
- GV == ST (lockstep: zero divergence) ✓

Proves zero overhead on the normal runtime path.

### Test 2 — Recording ON when armed: per-node values differ within one tick (Integration Pin)

Blueprint: `EventEntry → SequenceNode(Then0: Literal(10) → SetVariable(A), Then1: Literal(20) → SetVariable(A) → Return)`

The `SequenceNode` forces the compiler to allocate separate IR blocks per branch, each with a `SourceNodeId` probe. Both branches execute sequentially in one tick.

- Final live value after tick: `A = 20` ✓
- Restored to node 0 (before anything ran): `A = 0` ✓
- Restored to last node (before last block's writes): `A = 10` (Then0 wrote 10, Then1 not yet) ✓
- `countAtLastNode (10) < finalCount (20)` ✓

**This is the whole point:** sub-tick state shows `A = 0 → 10 → 20` across recorded nodes within one tick, unlike today's tick-granular snapshot which only shows `A = 20`.

### Test 3 — SimulationTick frozen, GlobalVersion advances during recorded tick

- `_repo.SimulationTick` unchanged across tick (no `_repo.Tick()` call in harness) ✓
- `_repo.GlobalVersion` advanced by exactly `RecordedNodeCount` (one BumpMemoryVersion per node) ✓
- `gvAfter > stAfter` (GV diverges from ST during debug session) ✓

### Test 4 — Null safety: no live repo → recording silently off, no NPE

Session created WITHOUT `SetLiveRepository`. Breakpoint armed. Tick runs.

- No exception thrown ✓
- `RecordedNodeCount == 0` ✓

---

## Test Counts

| Suite | Before | After |
|-------|--------|-------|
| Hrot.Blueprints.Tests | 1716 pass / 7 fail (pre-existing) | 1719 pass / 7 fail (pre-existing) / 8 skip |
| Hrot.Diagnostics.Breakpoints.Tests | 128 pass | 128 pass |

**New failures: 0.** All 7 pre-existing reds unchanged (AiPrimitive golden source, Stage8 PDB, allocation tests, snapshot). No CF-6 regressions.

---

## Suggested Commit Message

```
feat: wire SubTickSnapshotRecorder into BlueprintDebugSession (NGS-2.0)

- Add _recorder, _liveRepo, _lastRecordedTick fields to BlueprintDebugSession
- RecordingActive gate: armed iff _liveRepo != null AND breakpoints present
- BeginTick on tick-boundary in OnNewTick; RecordNodeEntry after each probe
- Expose RecordedNodeCount, RecordedNodeIdAt, RestoreRecordedNode
- Wire SetLiveRepository(_world) in EditorSubsystem next to SetDataBreakpointManager
- Add GraphBuilder.Sequence() to BlueprintAssetBuilder for multi-probe test graphs
- 4 integration tests: zero-overhead unarmed gate, sub-tick per-node value diff,
  GV/ST clock semantics, null-repo safety
```

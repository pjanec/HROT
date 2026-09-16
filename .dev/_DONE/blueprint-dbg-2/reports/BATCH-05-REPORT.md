# BATCH-05 Report

## Implementation Summary

**Task NGS-2.3 — Step-past-end tick-bridge.**

Replaced the `else: clamped at end` no-op branch in `BlueprintDebugSession.StepForwardOrCF6` with a one-real-tick advance guarded by `RecordingActive` (an armed breakpoint).

**File changed:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
**Method:** `StepForwardOrCF6` (private), called by `StepOver`/`StepInto`/`StepOut`.

**New test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/TickBridgeTests.cs` (5 tests).
**Updated test file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/VirtualPointerTests.cs` (1 test updated to reflect NGS-2.3 behavior at end-of-recording).

---

## Design Decisions

### Bridge code (exact implementation)

In `StepForwardOrCF6`, when `_nodePointer == last` and `_recorder.Count > 0`:

```csharp
if (RecordingActive)
{
    // Clear per-tick nav state so this session is not considered "paused"
    // while the one-tick advance runs. Leave _recordingEntity so the SAME
    // entity is recorded on the advanced tick.
    _isPaused       = false;
    _pausedAt       = null;
    _pausedOnEntity = null;
    _nodePointer    = -1;
    _firedBreakpointsThisTick.Clear();

    // Request exactly one tick advance.
    _timeController.RequestStepOneTick();
    OnSessionStateChanged?.Invoke();
    return;
}

// No breakpoint armed: keep no-op clamp.
OnSessionStateChanged?.Invoke();
return;
```

### Guard: `RecordingActive`

`RecordingActive` is true when `_liveRepo != null && (_breakpoints.Count > 0 || _tempBreakpoints.Count > 0)`. The guard ensures:
1. A breakpoint will re-fire on the next tick (the re-pause is guaranteed).
2. The recorder is active and will record the new tick.

Without the guard (no breakpoint armed), advancing one tick would leave the session un-paused with no re-pause trigger. The no-op clamp is preserved in that case.

### How re-pause is driven

The mechanism reuses the **existing** `HandleBreakpointHit → InitNodePointerOnPause` path with zero new code:

1. `RequestStepOneTick()` advances the clock by exactly one tick.
2. On the new tick, `OnNewTick()` fires → `_firedBreakpointsThisTick.Clear()` + `_recorder.BeginTick(_liveRepo!)` (new recording begins because `RecordingActive` is true and the tick changed).
3. `OnNodeEnter` fires for each node → `_recorder.RecordNodeEntry(...)` captures keyframes.
4. The armed breakpoint hits → `HandleBreakpointHit(self, bp, nodeId)` sets `_isPaused = true`, calls `InitNodePointerOnPause(nodeId)`, calls `_timeController.RequestPause()`, raises `OnSessionStateChanged`.

This works correctly under both the **real `MasterSyncTimeControllerAdapter`** (step runs synchronously, tick completes before `StepForwardOrCF6` returns) and the **test `MockTimeController`** (`RequestStepOneTick` is a no-op; the test drives the tick with `fixture.TickFrame()`). The re-pause is driven by the breakpoint, not by code after `RequestStepOneTick()`.

### Updated `VirtualPointerTests.VirtualPointer_PauseInitialisesPointer_StepBackAndForwardClamp`

The test previously asserted the old "clamp at last" behavior. With NGS-2.3, stepping past last with `RecordingActive` triggers the bridge. The test was updated to assert:
- `tc.StepRequestCount == previousCount + 1` (bridge was called)
- `session.IsPaused == false` (nav state cleared, waiting for re-pause)
- `session.CurrentNodePointer == -1`

The "clamp at 0" backward direction is unchanged and still tested.

---

## Deviations

None. Implementation strictly follows the prescribed mechanism from BATCH-05-INSTRUCTIONS.md.

---

## Test Results

### Hrot.Blueprints.Tests
```
Failed:    7  (all 7 are documented pre-existing reds)
Passed: 1742  (+5 new TickBridgeTests, +VirtualPointerTests regression count correct)
Skipped:   8
Total:  1757
```

Pre-existing failures (unchanged):
- `AiPrimitive_EmitMatchesGoldenSource` x2
- `Stage8_PdbContainsEmbeddedSource`
- `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`
- `TickFrame_1000Frames_AllocatesZeroBytes`
- `MoveToAndFire_GeneratedSource_Snapshot`
- `WhenNode_ZeroAllocOnHotPath`

### Hrot.Diagnostics.Breakpoints.Tests
```
Failed:   0
Passed: 128
Total:  128
```

### New tests (all pass)

| Test | Description |
|------|-------------|
| `TickBridgeTests.TickBridge_AdvancesExactlyOneTick_RepausesWithFreshRecording` | Tick 1 bridge drives tick 2; View.Tick == N+1; RecordedNodeCount >= 2 (fresh); pointer >= 0 |
| `TickBridgeTests.TickBridge_InspectorReflectsNewTick_ExactValue` | Cross-tick proof: at pointer 2 of tick N+1, A == 10; View.Tick == N+1 |
| `TickBridgeTests.TickBridge_NoArmGuard_DoesNotCallRequestStepOneTick` | No breakpoint → clamp, StepRequestCount unchanged |
| `TickBridgeTests.TickBridge_WithinTickStepping_Unaffected` | Steps 0→1→0→2 within one tick; no RequestStepOneTick calls; A==10 at pointer 2 |
| `TickBridgeTests.TickBridge_CF6Fallback_StillWorks_WhenNoRecordings` | No live repo → CF-6 path; no RequestStepOneTick |

---

## Cross-tick test design and exact values

**Blueprint:** `Entry → Sequence(Then0: SetVar A=10, Then1: SetVar A=20 → Return)` — same as the existing BATCH-03 blueprint.

**Tick N** (first pause): pointer starts at breakpoint node (entry/seq dispatch, index 0). A=0 at pointer 0, A=10 at pointer 2 (Then1 entry before it ran).

**Bridge step:** step from last recorded index → `RequestStepOneTick()` called; nav state cleared.

**Tick N+1** (second pause, driven by `fixture.TickFrame()`): armed BP re-fires on the same node → `HandleBreakpointHit` → fresh `BeginTick` (new recording), `InitNodePointerOnPause`. View.Tick == N+1.

**Cross-tick assertion (Test 2):** navigate to pointer 2 of tick N+1 → `GetCurrentStateSnapshot()` returns A == 10. This proves:
1. A full additional tick executed (Then0 ran, wrote A=10).
2. The snapshot is from tick N+1 (View.Tick is N+1, not N).
3. `RecordedNodeCount >= 3` (fresh `BeginTick`, not appended to old ring).

**SimulationTick delta:** `View.Tick` advances by exactly 1 (from N to N+1). The `EntityRepository.SimulationTick` is NOT used in tests because `TickFrame` doesn't call `EntityRepository.Tick()` — `MockSimulationView._tick` is the semantic frame clock for tests, and it advances by exactly 1 per `TickFrame`.

---

## Developer Insights

1. **No-arm guard critical:** without the guard, clearing nav state + advancing a tick with no re-pause trigger leaves the session in a broken "not paused, no pointer" state. The `RecordingActive` guard prevents this.

2. **MockTimeController.RequestStepOneTick is a no-op:** this is by design — the test fixture drives the tick explicitly. The bridge code is correct not to add post-step code that assumes the tick ran (as specified).

3. **VirtualPointerTests regression:** the `VirtualPointer_PauseInitialisesPointer_StepBackAndForwardClamp` test had a stale assertion from BATCH-03 that expected the old "clamp at last" behavior. Updated to match NGS-2.3 bridge semantics.

4. **Within-tick stepping unchanged:** steps 0→1 and StepBack 1→0 both leave `tc.StepRequestCount` unchanged — confirmed by Test 4.

---

## Known Issues

None. All specified functionality implemented.

---

## Suggested Commit Message

feat: NGS-2.3 step-past-end tick-bridge — advance one real tick at end of recording and re-pause via armed breakpoint (BATCH-05)

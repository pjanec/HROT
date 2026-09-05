# BF-03 Implementation Report: Step-past-end tick-bridge fix for latent nodes

**Date:** 2026-06-10
**Branch:** blueprint-integ-1
**Status:** Complete — all new tests pass; zero new deterministic failures

---

## What was done

### 1. Refactor: `StepFromNode` helper extracted from `Step(StepMode)`

`BlueprintDebugSession.cs` was refactored to extract the CF-6 successor-stepping
core into `StepFromNode(Guid assetId, Guid graphId, string fromNodeId, StepMode legacyFallback)`.

`Step(StepMode)` now delegates directly:
```csharp
private void Step(StepMode fallbackStepMode)
{
    if (!_isPaused || _pausedAt == null) return;
    StepFromNode(_pausedAt.AssetId, _pausedAt.GraphId, _pausedAt.NodeId, fallbackStepMode);
}
```

`StepFromNode` contains:
- Graph lookup via `_graphs.TryGetValue`; on failure → `LegacyStepOneTick`.
- `Guid.TryParse(fromNodeId)`; on failure → `LegacyStepOneTick`.
- `ExecSuccessors.GetSuccessors(graph, authoredNodeId)`: if empty → `Continue()`.
- **`allSuccessorsAreTerminal` guard**: if ALL immediate successors have no further
  successors (e.g. all are `ReturnNode`s merged into predecessor blocks by Stage5),
  call `Continue()` instead of setting dead temp BPs that will never fire.
- Otherwise: set temp BPs on non-terminal successors, clear pause/nav state
  (`_isPaused = false`, `_pausedAt = null`, `_nodePointer = -1`, etc.), call
  `RequestResume()`.

### 2. Bug fix: NGS-2.3 tick-bridge in `StepForwardOrCF6`

The end-of-recording branch previously called `RequestStepOneTick()`. That fails for
latent nodes (Delay/WaitForChannel) because advancing one tick only continues the
latent countdown — no probe fires, no BP fires, leaving a "Not paused" dead state.

**New behaviour** (when at last recorded node AND `RecordingActive`):
```csharp
var lastNodeId = CurrentNodeId; // LAST RECORDED node, not _pausedAt
StepFromNode(_pausedAt.AssetId, _pausedAt.GraphId, lastNodeId!, fallbackStepMode);
```
`RequestStepOneTick` is no longer called from this path.

### 3. Why `allSuccessorsAreTerminal` is needed

Stage5 merges `ReturnNode` into its predecessor's IR block — the `ReturnNode`'s id
is NOT emitted as a `DebugProbe.NodeEnter` call. Setting a temp BP on a `ReturnNode`
would never fire. For synchronous blueprints where the last exec node before Return is
the last recorded node (e.g. `SetVarB → Return`):
- `GetSuccessors(SetVarB)` = `[ReturnId]`
- `GetSuccessors(ReturnId)` = `[]` → `allSuccessorsAreTerminal = true`
- Path: `Continue()` → `RequestResume()` → user BP re-fires on next tick ✓

### 4. Latent node probe identity

For `Entry → Delay(0.0f) → SetVar → Return`:
- Stage5's `ScheduleLatentNode` overwrites `bb.SourceNodeId = delay.Id`, so the
  entry block's probe id is `Delay.Id` (not `Entry.Id`).
- The resume block starts with `SetVar`; `bb.SourceNodeId ??= SetVar.Id`.
- `GetSuccessors(Delay.Id)` = `[SetVar.Id]`. `GetSuccessors(SetVar.Id)` = `[ReturnId]`.
  `SetVar` is non-terminal → temp BP set on `SetVar.Id`.
- When the Delay elapses (tick 2), probe fires with `SetVar.Id` → temp BP fires →
  session re-pauses. The dead state never occurs. ✓

---

## Updated BATCH-05 assertions

Tests in `TickBridgeTests.cs` and `VirtualPointerTests.cs` previously asserted
`tc.StepRequestCount == 1` (i.e. `RequestStepOneTick` was called). These were updated
to reflect the new semantics:

| Old assertion | New assertion | Reason |
|---|---|---|
| `Assert.True(tc.StepRequestCount == 1, ...)` | `Assert.True(tc.ResumeCount > resumeCountBefore, ...)` + `Assert.True(tc.StepRequestCount == 0, ...)` | Bridge now uses temp-BP + resume (CF-6 path) |
| `tc.StepRequestCount == stepsBefore + 1` (VirtualPointerTests) | `tc.ResumeCount > resumeCountBefore` + `tc.StepRequestCount == 0` | Same: BF-03 replaces RequestStepOneTick with RequestResume |

In addition, **`session.RegisterGraph(asset.Graphs[0])` was added** to tests 1, 2
(TickBridgeTests), and Test 1 (VirtualPointerTests), because `StepFromNode` requires
the graph to be registered to compute successors via `ExecSuccessors.GetSuccessors`.
Without `RegisterGraph`, `StepFromNode` falls back to `LegacyStepOneTick` which
calls `RequestStepOneTick` — violating the new assertions.

---

## New tests added to `TickBridgeTests.cs`

### Test 6: `TickBridge_LatentDelay_DoesNotDeadlock_RepausesAfterDelay`

**Blueprint:** `Entry → Delay(0.0f) → SetVariable(X) → Return` (built via
`BlueprintAssetBuilder.Instance(...).WithVariable("X", typeof(int)).WithGraph(...)`)

**Scenario:**
1. Breakpoint on `Nodes[1].Id` (LatentDelayNode — probe fires with Delay.Id because
   `ScheduleLatentNode` overwrites `bb.SourceNodeId = delay.Id`).
2. Tick 1: entry block probe fires → BP hits → session pauses with 1 recorded node.
3. Pointer navigated to `Nodes[1].Id` (last = only recorded node).
4. `StepInto()` (step past end from latent node):
   - `GetSuccessors(Delay.Id)` = `[SetVar.Id]` — non-terminal.
   - Temp BP set on `SetVar.Id`. `RequestResume()` called.
   - Asserts: `IsPaused == false`, `ResumeCount > before`, `StepRequestCount == 0`.
5. `fixture.TickFrame(0.016f)` (delay elapses):
   - Resume block probe fires with `SetVar.Id` → temp BP hits → session re-pauses.
   - Regression guard: `IsPaused == true`, `RecordedNodeCount >= 1`, `CurrentNodePointer >= 0`.
   - Dead state (the original BF-03 bug) would leave `IsPaused == false` here.

### Test 7: `TickBridge_TerminalLastNode_CallsContinue_RepausesOnNextBP`

**Blueprint:** `BuildTwoSeqVarAsset` (Entry → Seq → SetVarA + SetVarB → Return)

**Scenario:**
1. Breakpoint on SequenceNode (entry block probe). Tick 1 → pause.
2. Navigate pointer to last recorded node (SetVarB, index `count-1`).
3. `StepInto()` (step past end from terminal node):
   - `GetSuccessors(SetVarB.Id)` = `[ReturnId]`. `GetSuccessors(ReturnId)` = `[]`.
   - `allSuccessorsAreTerminal = true` → `Continue()` → `RequestResume()`.
   - Asserts: `IsPaused == false`, `HasTemporaryBreakpoints == false`,
     `ResumeCount > before`, `StepRequestCount == 0`.
4. `fixture.TickFrame(0.016f)` (next tick):
   - Still-armed user BP (on SequenceNode) re-fires → session re-pauses.
   - Asserts: `IsPaused == true`, `RecordedNodeCount >= 2`.

---

## Test counts

| Suite | Before BF-03 | After BF-03 |
|---|---|---|
| `Hrot.Blueprints.Tests` (new tests added) | 1741 pass / 7 unique reds | 1743 pass / 7 unique reds (same reds) |
| `Hrot.Diagnostics.Breakpoints.Tests` | 128 pass | 128 pass |

The 7 unique pre-existing reds (same as before): `AiPrimitive_EmitMatchesGoldenSource` ×2,
`Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`,
`TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`,
`WhenNode_ZeroAllocOnHotPath`. Also flaky in full-suite (GC/allocation sensitive):
`AlcUnloadTests.Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive`,
`Spawn_ZeroAllocation` — both pass in isolation.

---

## Files changed

- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Editor\BlueprintDebugSession.cs`
  — `StepFromNode` new private method; `StepForwardOrCF6` bridge fix; `Step(StepMode)` delegate.
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Debug\TickBridgeTests.cs`
  — Tests 1 and 2: added `RegisterGraph`; updated assertions from `StepRequestCount==1`
    to `ResumeCount>before` + `StepRequestCount==0`; Test 3: added resume clamp check;
    Test 5: comment update. New: Test 6 (latent repro) + Test 7 (terminal node).
- `Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests\Debug\VirtualPointerTests.cs`
  — Test 1: added `RegisterGraph`; updated `StepRequestCount==stepsBefore+1` → new
    `ResumeCount > resumeCountBefore` + `StepRequestCount==0` assertions.

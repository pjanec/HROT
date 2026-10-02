# BPDBG-STEPOVER-TEST-RECONCILE Report

**Date:** 2026-06-11
**Branch:** blueprint-integ-1
**Author:** claude-sonnet-4-6 agent

---

## Root Cause of All 7 Failures

All 7 failures share a single underlying cause: the `??=` fix in `ScheduleSequenceNode` (Stage5_Schedule.cs) changed the probe count of the **entry block** for every `BuildTwoSeqVarAsset`-topology asset.

**Before the fix** (unconditional `bb.SourceNodeId = seq.Id`):
- Entry block got `SourceNodeId = seqId` (unconditional overwrite).
- `DebugProbeInsertion`: `blockSourceNodeId = seqId`, no ExecEntryNodeId coverage → `needsHeaderProbe = true` → **one** header probe for `seqId`.
- Recording ring: `[seqId(0), svAId(1), svBId(2)]` — 3 entries.

**After the fix** (`bb.SourceNodeId ??= seq.Id`):
- Entry block creation (line 198 of Stage5_Schedule.cs) already sets `SourceNodeId = entryId`. When `ScheduleSequenceNode` is called, `??=` is a **no-op** because `entryId` is not null.
- Entry block now has `SourceNodeId = entryId`. The seq-probe-anchor provides `ExecEntryNodeId = seqId` for the Sequence, but nothing provides `ExecEntryNodeId = entryId`.
- `DebugProbeInsertion`: `blockSourceNodeId = entryId`, not covered → `needsHeaderProbe = true` → header probe for `entryId`. Plus the seq-probe-anchor triggers a per-node probe for `seqId`.
- Recording ring: `[entryId(0), seqId(1), svAId(2), svBId(3)]` — **4 entries**.

The `entryId` header probe is **correct** per the uniform design ("every exec node is treated uniformly — probe = breakpointable = recorded = steppable"). The old code accidentally suppressed the EventEntry probe by clobbering `SourceNodeId` with `seqId`. The fix exposed this latent gap.

---

## Per-Test Verdict Table

| # | Test | Failure message | Verdict | Evidence & Resolution |
|---|------|-----------------|---------|----------------------|
| 1 | `TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint` | `landing must be seqId, got <other>` | **A — stale** | See below |
| 2 | `TickBridge_TerminalLastNode_SetsFirstNodeTempBP_NotContinue` | `Expected seqId, Actual <other>` | **A — stale** | See below |
| 3 | `TickBridge_InspectorReflectsNewTick_ExactValue` | `At pointer 2, A must be 10; got 20` | **A — stale** | Index shift: pointer 3 is now A=10 |
| 4 | `TickBridge_WithinTickStepping_Unaffected` | `Expected 10, Actual 0` | **A — stale** | Index shift: pointer 3 is now A=10 |
| 5 | `InspectorSnapshotResolutionTests.ResolveInspectorSnapshot_WhenPaused_PointerAt2_ReturnsA10` | `Expected 10, Actual 0` | **A — stale** | Index shift: pointer 3 is now A=10 |
| 6 | `InspectorSnapshotResolutionTests.ResolveInspectorSnapshot_AcrossPointers_Returns_0_0_10` | `Expected 10, Actual 0` | **A — stale** | Index shift: now 0,0,0,10 across indices 0–3 |
| 7 | `VirtualPointerTests.Inspector_ReturnsExactPerNodeValues_AcrossStepBackAndForward` | `Expected 10, Actual 0` | **A — stale** | Index shift: pointer 3 is now A=10 |

**No path-B bugs found.** The production code (`BlueprintDebugSession.cs`, `Stage5_Schedule.cs`) is correct as-is.

---

## Detailed Justification for Landing-Node Tests (Tests 1 and 2)

### Test 1: `TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint`

**Asset graph (programmatic):**
```
Entry(entryId) → Sequence(seqId) {
  Then0: SetVar(X=7, svId)  [exec-out unlinked — falls through to Then1 block]
  Then1: Delay(0.0f, delayId) → Return(retId)
}
```

**OLD landing nodeId:** `seqId` (SequenceNode)
- Old recording: `[seqId(0), svId(1), delayId(2)]`
- After step-past-end from `delayId`, temp-BP on `seqId` fired on next new-iteration Block A.

**NEW landing nodeId:** `entryId` (EventEntryNode)
- New recording: `[entryId(0), seqId(1), svId(2), delayId(3)]`
- `_stepResumePending` triggers on `_recorder.Count == 1` — the FIRST probe of the new iteration.
- Block A of the new iteration fires: entry block probe order = `entryId` (header probe) first, then `seqId` (seq-probe-anchor). So `entryId` fires first → `_recorder.Count == 1` → re-pause on `entryId`.

**Why `entryId` is the correct first REAL executed node:**
- `EventEntryNode` IS the first authored node to execute each iteration — it dispatches control to the Sequence.
- `entryId` is a real authored node ID (not a synthetic const like `seq-probe-anchor`).
- With the uniform design ("every exec node has a probe"), EventEntry must fire a probe. The old code suppressed this accidentally.
- The test's invariant is "must land on the first actually executed node" — which is now `entryId`. ✓

### Test 2: `TickBridge_TerminalLastNode_SetsFirstNodeTempBP_NotContinue`

**Asset graph:** `BuildTwoSeqVarAsset` — same topology as Tests 3-7.

**OLD landing nodeId:** `seqId` = `asset.Graphs[0].Nodes[1].Id`
**NEW landing nodeId:** `entryId` = `asset.Graphs[0].Nodes[0].Id`

Same argument as Test 1: `entryId` probe fires first in the new tick's Block A. `_stepResumePending` catches it at `_recorder.Count == 1`. `entryId` is the correct first executed authored node. ✓

---

## Corrected Pointer → Node → Value Map for `BuildTwoSeqVarAsset`

```
Entry(entryId) → Sequence(seqId) {
  Then0: Literal(10) → SetVariable(A=10, svAId)
  Then1: Literal(20) → SetVariable(A=20, svBId) → Return
}
```

| Pointer Index | Authored Node | State as-of entering | A value |
|--------------|---------------|----------------------|---------|
| 0 | `entryId` (EventEntryNode) — header probe | Before any SetVar | 0 |
| 1 | `seqId` (SequenceNode) — seq-probe-anchor | Before any SetVar | 0 |
| 2 | `svAId` (SetVarA, Then0) — per-node probe | Entering Then0, before its write | 0 |
| 3 | `svBId` (SetVarB, Then1) — per-node probe | After Then0 wrote A=10, entering Then1 | 10 |

The old "A=10 at pointer 2" maps to "A=10 at pointer 3." The whole sequence is correct: entries at 0 and 1 capture A=0 (no write yet), entry 2 also A=0 (entering the Then0 node before it writes), entry 3 A=10 (entering Then1 after Then0 has already written). Node-granular "as-of entering" semantics preserved throughout.

---

## Changes Made

All edits are in test files only. No production code changed.

### `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/TickBridgeTests.cs`

**Test 2 (`TickBridge_InspectorReflectsNewTick_ExactValue`):**
- Count check: `>= 3` → `>= 4`
- Navigation: added one `StepInto()` to reach index 3; updated `Assert.Equal(2, ...)` → `Assert.Equal(3, ...)`
- Failure message: "At pointer 2" → "At pointer 3"
- Added inline comment explaining the new 4-entry recording ring

**Test 4 (`TickBridge_WithinTickStepping_Unaffected`):**
- Count check: `>= 3` → `>= 4`
- Navigation: added one `StepInto()` to reach index 3; updated `Assert.Equal(2, ...)` → `Assert.Equal(3, ...)`
- Added inline comment explaining the new probe order

**Test 7 (`TickBridge_TerminalLastNode_SetsFirstNodeTempBP_NotContinue`):**
- Landing assertion: `probeNodeId.ToString("D")` (seqId) → `asset.Graphs[0].Nodes[0].Id.ToString("D")` (entryId)
- Added inline comment explaining why entryId is now the first probe

**Test 8 (`TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint`):**
- Header comment: updated Stage5 scheduling description, recording ring, and frame-drive reasoning
- Recording count: `>= 2` → `>= 3`
- Frame N+2 comment: "seqId probe" → "entryId probe"
- Landing assertion: `seqIdStr` → `entryIdStr`
- Added `seqIdStr` variable (still used in the "not seqId" regression guard)
- Added new assertion: must NOT be `seqIdStr` (discriminates old expected vs. current correct)

### `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/NodeGranularEditorUITests.cs`

**`ResolveInspectorSnapshot_WhenPaused_PointerAt2_ReturnsA10`:**
- Added one `StepInto()` to reach index 3; updated `Assert.Equal(2, ...)` → `Assert.Equal(3, ...)`
- Added new probe-order comment

**`ResolveInspectorSnapshot_AcrossPointers_Returns_0_0_10`:**
- Extended sequence from 3 assertions to 4 (indices 0-3)
- Pointer 2 now asserts A=0 (svAId entry, before write); pointer 3 asserts A=10 (svBId entry)
- Updated test name comment from "0,0,10" to "0,0,0,10"

### `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/VirtualPointerTests.cs`

**`Inspector_ReturnsExactPerNodeValues_AcrossStepBackAndForward`:**
- Extended from 3-node sequence to 4-node sequence
- Index 0 (entryId) → A=0; index 1 (seqId) → A=0; index 2 (svAId) → A=0; index 3 (svBId) → A=10
- StepBack assertion updated: back from 3→2 (A=0, not back from 2→1)
- Count check: `>= 3` → `>= 4`
- Added new probe-order comment block explaining the change

---

## True Test Results

### `Hrot.Blueprints.Tests` (full run, no filter)

```
Failed:     7    (all documented pre-existing reds — unchanged)
Passed:  1833
Skipped:    8
Total:   1848
Duration:  ~30 s
```

Pre-existing failures (same as prior batches):
1. `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)` — golden diff
2. `AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)` — golden diff
3. `Stage8_PdbContainsEmbeddedSource` — environment
4. `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — environment
5. `TickFrame_1000Frames_AllocatesZeroBytes` — alloc threshold
6. `MoveToAndFire_GeneratedSource_Snapshot` — golden diff
7. `WhenNode_ZeroAllocOnHotPath` — alloc threshold

**0 new failures. All 7 previously-red tests are now green.**

### `Hrot.Diagnostics.Breakpoints.Tests`

```
Failed:     0
Passed:   128
Skipped:    0
Total:     128
Duration: ~471 ms
```

---

## Known Issues / Observations

- The EventEntry header probe being recorded (index 0) is new behavior. Users stepping through a blueprint will now see "EventEntry" as an extra stepping stop at the start of each tick. This is correct per the uniform design, but may affect future tests that make hard-coded assumptions about the number of recorded nodes in the entry block.
- The `AlcUnloadTests.Fixture_AfterMultipleLoads_…` flaky test was not observed to fail during these runs (not in the 7 pre-existing reds listed). If it appears, it is pre-existing flaky behavior (ALC timing).

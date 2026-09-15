# BPDBG-STEPOVER-LATENT-SEQUENCE — Implementation Report

**Date:** 2026-06-11
**Branch:** blueprint-integ-1
**Author:** sonnet agent (claude-sonnet-4-6)

---

## 1. Verified Root Cause

`StepOver` from the last node of a Sequence's `Then0` branch (a latent `Delay`) landed on the **last Delay of Then1**, skipping intermediate nodes (`SetVarB` and all children of nested Sequence `S1`).

**Code path:**

```
StepOver()
  → StepForwardOrCF6()          (BlueprintDebugSession.cs ~line 913)
     → pointer at last recorded node && RecordingActive
     → StepFromNodeOrNextIteration()  (line ~982)
        → ExecSuccessors.GetSuccessors(delay0Id)  // authored-graph lookup
        → empty (Sequence drives Then0→Then1 internally; Delay0 exec-out has no link)
        → allTerminal = true
        → path (b): temp-BP on EventEntry's exec-successor (s0Id)
        → RequestResume()
```

`s0Id` never re-fires mid-flight (the Sequence node `S0` is not re-entered — it just falls through between branches). The simulation ran all of Then1 and S1 to the latent suspend on `Delay1`. The virtual pointer landed on `delay1Id`. Intermediate nodes were silently skipped. The user had no way to step through them.

**Root flaw:** `ExecSuccessors.GetSuccessors` on the authored graph cannot model Sequence branch ordering or latent resume successors. Any path that guesses topology from the static graph will be wrong for non-trivial graphs (nested sequences, multi-latent chains, arbitrary nesting depth).

---

## 2. Mechanism Change

### Before (broken)

`StepFromNodeOrNextIteration` called `ExecSuccessors.GetSuccessors` on the authored graph to find candidate temp-BP targets. For terminal successors it fell back to the entry successor. A temp-BP was armed and the simulation was resumed. If the temp-BP target never fired (as in the Sequence case) the session stayed un-paused indefinitely — dead state.

### After (fixed)

The RecordingActive tick-bridge no longer uses `ExecSuccessors` at all. A one-shot `_stepResumePending` flag is set instead:

**In `StepForwardOrCF6` (RecordingActive, at-last-recorded-node path):**

```csharp
if (RecordingActive)
{
    _stepResumeAssetId  = _pausedAt?.AssetId ?? Guid.Empty;
    _stepResumeGraphId  = _pausedAt?.GraphId ?? Guid.Empty;
    _stepResumePending  = true;
    _isPaused           = false;
    _pausedAt           = null;
    _pausedOnEntity     = null;
    _nodePointer        = -1;
    _stepMode           = StepMode.None;
    _firedBreakpointsThisTick.Clear();
    _timeController.RequestResume();
    OnSessionStateChanged?.Invoke();
    return;
}
```

No temp-BPs are armed. The simulation resumes normally.

**In `OnNodeEnter` (after the recording block):**

```csharp
if (_stepResumePending && RecordingActive && IsRecordingEntity(self) && _recorder.Count == 1)
{
    _stepResumePending = false;
    var pseudoBp = new Breakpoint(default, _stepResumeAssetId, _stepResumeGraphId, nodeId, 0, true);
    HandleBreakpointHit(self, pseudoBp, nodeId);
    return;
}
```

On the **first** `OnNodeEnter` call for the recording entity in the resumed tick (`_recorder.Count == 1`), the session re-pauses on that node. The recorder has already captured the first node, so `_nodePointer = 0` lands on the correct entry point. `HandleBreakpointHit` captures the state snapshot using the correct AssetId/GraphId (saved before clearing the pause state).

**Fields added to `BlueprintDebugSession`:**

```csharp
private bool _stepResumePending;
private Guid _stepResumeAssetId;
private Guid _stepResumeGraphId;
```

**`Continue()` updated:** clears all three fields.
**`Detach()` updated:** clears `_stepResumePending`.

---

## 3. How BF-03 and BF-04 Are Preserved

Both cases are now handled by the same `_stepResumePending` mechanism — the old temp-BP topology path is gone for RecordingActive.

### BF-03: Delay → synchronous successor (SetVar → Return)

Graph: `Entry → Delay(0.0f) → SetVariable(X) → Return`

Compiled probe order for the resume tick (after Delay elapses):
- `SetVar` block has `SourceNodeId = setVarId` (latent's resume block → SetVar is the next exec node, `bb.SourceNodeId ??= setVarId`).
- `DebugProbeInsertion`: `coveredByExecEntryId = true` (SetVar statement has `ExecEntryNodeId = setVarId`) → no header probe.
- First probe of resumed tick = `setVarId`.
- `_stepResumePending`: `_recorder.Count == 1` → re-pause on `setVarId`. ✓

**Re-asserted landing node (Test 9):** `setVarId` (SetVariable node — Delay's direct synchronous successor).

### BF-04: Delay → Return (end-of-graph-tick, next iteration)

Graph: `Entry → Sequence(Then0: SetVar, Then1: Delay(0.0f) → Return)`

Compiled resume path:
- After Delay elapses: resume block → `IrTerm_Goto(seq_then1_branch)` — but wait, Delay's resume block here has no continuation (Delay is followed by Return), so it falls through → Return fires → blueprint restarts.
- Next iteration: Entry block probe fires with `seqId` as first node.
- `_stepResumePending`: `_recorder.Count == 1` → re-pause on `seqId`. ✓

Note: with a 0.0f Delay and a `Delay → Return` path, the Delay elapses in Frame N+1 but the resume block (Return, no probe) fires without triggering `_stepResumePending` (no probe for entity). The fresh iteration fires in Frame N+2.

**Re-asserted landing node (Test 8):** `seqId` (SequenceNode — first executable node of the new iteration; explicitly NOT `svId` which was the user breakpoint node).

---

## 4. New Test: Test 10

**Test name:** `TickBridge_StepOverSequenceThen0Latent_LandsOnFirstNodeOfThen1_NotLastDelay`

**Asset (programmatic, no .bp.json):**

```
EventEntry(entryId) → S0(s0Id) {
  Then0: SetVarA(svAId) → Delay0(delay0Id)               [latent — last of Then0]
  Then1: SetVarB(svBId) → S1(s1Id) {
           Then0: SetVarC(svCId) → Return(ret0Id)
           Then1: Delay1(delay1Id) → Return(ret1Id)       [latent — last of Then1]
         }
}
```

**Probe order analysis (from DebugProbeInsertion + Stage5_Schedule):**

Then1 block (`seq_s0_then1`):
- `ScheduleSetVariableNode(svB, bb)` → `bb.SourceNodeId ??= svBId`
- `ScheduleSequenceNode(s1, bb)` → `bb.SourceNodeId = s1Id` (force overwrites svBId!)
- Final `blockSourceNodeId = s1Id`
- `coveredByExecEntryId`: SetVarB statement has `ExecEntryNodeId = svBId` ≠ s1Id → FALSE
- `coveredByOriginId`: S1 produces no statements → FALSE
- `needsHeaderProbe = true` → header probe with **s1Id** emitted FIRST
- Then per-node probe for **svBId** (before SetVarB statement)

Probe order for resumed tick (after Delay0 elapses):
1. `s1Id` (header probe of Then1 block — FIRST probe)
2. `svBId` (per-node probe for SetVarB)
3. `svCId` (per-node probe for SetVarC in S1.Then0)
4. `delay1Id` (latent probe for Delay1 in S1.Then1 — last node)

**Test flow:**
1. BP on `svAId`, tick → pause at svAId
2. StepInto → `delay0Id` (last recorded node)
3. StepOver (bridge) → `IsPaused = false`, `HasTemporaryBreakpoints = false`, `RequestResume` called
4. TickFrame → resumed tick runs; s1Id fires first → `_stepResumePending` triggers → re-pause on `s1Id`

**Assertions:**
- `IsPaused == true` after TickFrame (PRIMARY guard: old code = dead state, IsPaused = false)
- `CurrentNodeId == s1IdStr` (landing = first probe of Then1 block)
- `CurrentNodeId != delay1IdStr` (NOT the wrong landing node)
- `CurrentNodePointer == 0`
- `RecordedNodeCount >= 2`
- Second step → `CurrentNodeId == svBIdStr`
- Third step (if RecordedNodeCount >= 3) → `CurrentNodeId == svCIdStr`

---

## 5. Test Suite Results

### `Hrot.Blueprints.Tests` (stability filter applied)

```
Passed:  1830
Failed:     8  (all pre-existing, documented below)
Skipped:    8
Total:   1846
Duration:  42 s
```

Pre-existing failures (unchanged from before this fix):
- `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)` — golden diff
- `AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)` — golden diff
- `Stage8_PdbContainsEmbeddedSource` — environment
- `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — environment
- `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` — flaky ALC
- `TickFrame_1000Frames_AllocatesZeroBytes` — alloc threshold
- `MoveToAndFire_GeneratedSource_Snapshot` — golden diff
- `WhenNode_ZeroAllocOnHotPath` — alloc threshold

**0 new failures introduced.**

### `Hrot.Diagnostics.Breakpoints.Tests` (stability filter applied)

```
Passed:  128
Failed:    0
Skipped:   0
Total:   128
Duration: 556 ms
```

---

## 6. Files Modified

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs`
  — Added `_stepResumePending`, `_stepResumeAssetId`, `_stepResumeGraphId` fields
  — Updated `StepForwardOrCF6` (RecordingActive tick-bridge path)
  — Updated `OnNodeEnter` (added `_stepResumePending` re-pause handler)
  — Updated `Continue()` and `Detach()` (clear pending flag)

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/TickBridgeTests.cs`
  — Test 7: updated to assert `HasTemporaryBreakpoints == false` (new mechanism)
  — Test 8 (BF-04 discriminating): updated to assert no temp BPs; added exact `seqId` landing assertion
  — Test 9 (BF-03 re-assert): updated to assert no temp BPs; re-asserted exact `setVarId` landing
  — Test 10 (NEW — primary bug regression): programmatic multi-branch Sequence asset; asserts `s1Id` landing, step order, no dead state

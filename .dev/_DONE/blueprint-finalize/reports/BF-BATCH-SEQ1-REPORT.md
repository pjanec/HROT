# BF-BATCH-SEQ1 Report

**Batch:** BF-BATCH-SEQ1 (SequenceNode branch scheduling in Stage 5)
**Developer:** Zoo (AI)
**Date:** 2026-06-07
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| SEQ1 — Schedule SequenceNode branches | [x] | `ScheduleSequenceNode` + `SealFallThrough` + propagation in latent/branch/when |
| SEQ1-T — Reconcile BP1412 tests | [x] | Updated two Sequence-based tests; coverage meta-test updated |

### Files Modified

| File | Change |
|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` | Added `_fallThroughTarget` dictionary, `ScheduleSequenceNode`, `SealFallThrough` helper; added `case SequenceNode seq:` to `ScheduleBlock`; replaced 3 fall-through sites; added fall-through propagation in `ScheduleLatentNode`, `ScheduleBranchNode`, `ScheduleWhenNode` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs` | Added `BP1413` (safety-valve diagnostic for latent-in-Sequence) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5_ScheduleTests/SequenceSchedulingTests.cs` | NEW: 7 tests covering all required scenarios |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage5_ScheduleTests/BP1412_DroppedExecSuccessorsTests.cs` | Updated Scenario 1 (Sequence no longer triggers BP1412) and Scenario 6 (uses unresolved-link for NodeId assertion) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage2_ValidationTests/V_AllValidatorsCoverageTests.cs` | Added `BP1413` to `KnownNotYetEmittedCodes` |

---

## Testing Results

**Full suite:** Failed: 4, Passed: 1636, Skipped: 8, Total: 1648

**4 known pre-existing failures (unchanged from baseline):**
1. `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` — CRLF line-ending snapshot mismatch
2. `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` — CRLF line-ending snapshot mismatch
3. `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` — locale decimal separator
4. `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` — zero-allocation assertion (3200 bytes observed)

**New SEQ1 tests — all 7 passing:**

| Test | What it verifies |
|------|-----------------|
| `Schedule_TwoSequenceBranches_ChainsInOrder_NoBP1412` | Entry block Goto -> then0; then0 Goto -> then1; then1 FallThrough; no BP1412 |
| `Schedule_UnconnectedThenPin_OnlyConnectedBranchesScheduled` | Then1 unlinked -> only 1 branch block allocated; no BP1412 |
| `Schedule_Then0Returns_ShortCircuits_Then1NotReachable` | Then0 block ends with `IrTerm_ReturnStatus` (not Goto to then1) |
| `Schedule_NestedSequence_ChainsInnerBranchesAfterOuterThen0` | 5+ blocks; outer then0 and inner both branches scheduled |
| `Schedule_LatentInSequenceBranch_PropagatesOrEmitsBP1413` | Latent (WaitForChannel) in Then0; fall-through propagation to resume block; no BP1412 |
| `Schedule_ZeroConnectedBranches_SealsFallThrough_NoBP1412` | Single block with FallThrough terminator; no BP1412 |
| `Schedule_BranchInsideSequence_PropagatesFallThrough` | Branch in Then0; both true/false blocks get fall-through target propagated |

**Updated BP1412 tests — all 5 passing:**
- `Schedule_SequenceNode_LinkedExecOuts_SchedulesCorrectly_NoBP1412` (was `_Dropped_EmitsBP1412_Error`): now asserts no BP1412 and blocks >= 3
- `Schedule_UnresolvedExecLink_EmitsBP1412_Error`: unchanged, keeps `[CoversDiagnosticCode("BP1412")]`
- `Schedule_NormalChain_NoBP1412`, `Schedule_NodeWithNoExecOutPin_NoBP1412`, `Schedule_EventEntryNoExecOutPin_NoBP1412`: unchanged
- `Schedule_DroppedSuccessor_DiagnosticHasNodeId`: now uses unresolved-link case (entryId, not seqId)

**Coverage meta-test:** Passes after adding `BP1413` to `KnownNotYetEmittedCodes`.

---

## Implementation Summary

### How branch chaining works

The core mechanism is a **fall-through redirect dictionary** (`_fallThroughTarget`). When `ScheduleSequenceNode` allocates blocks for each Then successor, it registers:

```
_fallThroughTarget[branchBlock_i] = branchBlock_{i+1}   // for all except last
```

When a branch's exec chain naturally ends (hits a node with no exec successor, or a node whose exec-out pins have no links), the centralized `SealFallThrough` helper checks this dictionary. If a redirect is registered, it emits `IrTerm_Goto(target)`; otherwise it emits `IrTerm_FallThrough`. This turns "end of branch i" into an unconditional jump to branch i+1's block.

Three existing fall-through sites were replaced with `SealFallThrough` calls:
- `EventEntryNode` null-successor case (~line 240)
- `default` null-successor case (~line 285)
- Latent "empty resume block" path (~line 341)

### How the latent-branch case was handled

The latent-in-Sequence case is **fully implemented** via fall-through target propagation. When a latent node (e.g., `WaitForChannelNode`) allocates a resume block inside a Sequence branch, the original branch block's `_fallThroughTarget` is transferred to the resume block:

```csharp
// In ScheduleLatentNode, after allocating resumeBlockId:
if (_fallThroughTarget.TryGetValue(bb.Id.Value, out var latentFt))
    _fallThroughTarget[resumeBlockId.Value] = latentFt;
```

This means when the resume block's chain ends, `SealFallThrough` sees the redirect and emits `IrTerm_Goto(nextBranch)` — correctly continuing to the next Sequence branch after the latent operation completes.

The same propagation pattern is applied in:
- `ScheduleBranchNode`: both true/false blocks get the fall-through target (whichever branch is taken, control continues to the next Sequence branch)
- `ScheduleWhenNode`: onFired, onEnded, and out blocks all get the fall-through target
- `ScheduleSequenceNode` (nested): the outer block's fall-through target is transferred to the inner Sequence's last branch block

`BP1413` was defined as a safety valve but is **not emitted** because the fall-through propagation correctly handles the latent-in-Sequence case. It is listed in `KnownNotYetEmittedCodes`.

### Design decisions beyond the instructions

1. **Pin ordering by numeric suffix:** The spec said "order by the numeric suffix of the pin Name, fall back to Pins-list order." I implemented `int.TryParse` on the substring after "Then", with non-parseable names receiving `int.MaxValue` (sorting last). This handles edge cases like `"ThenX"` gracefully.

2. **Propagation in ScheduleWhenNode:** The spec mentioned latent/branch/Sequence propagation but not WhenNode explicitly. I added it for consistency — a WhenNode inside a Sequence branch should also correctly continue to the next branch after its fired/ended/out paths complete.

3. **`SealFallThrough` with optional debug:** The helper accepts an optional `IrDebugAnnotation?` so callers that have node context can attach it to the `IrTerm_FallThrough`. Callers without context (latent resume block) pass null.

4. **`branchBlocks[^1]` avoided:** The project targets `netstandard2.0` which doesn't support `System.Index`. Used `branchBlocks[branchBlocks.Count - 1]` instead.

---

## Weak Points & Edge Cases

1. **Diamond-shaped branches:** If a Sequence has two Then successors that both converge to the same downstream node (diamond), the BFS dequeuing may process them in non-deterministic order, but the terminator assignment is idempotent since the block was already scheduled. No correctness issue.

2. **Multiple latent nodes in one branch:** Each latent creates its own resume block. The fall-through target propagates through all of them correctly (each `ScheduleLatentNode` call transfers the target from its pre-suspend block to the new resume block).

3. **Zero-connected branches edge case:** A Sequence with Then pins but no links seals as fall-through immediately. This is correct — no branches to schedule means the Sequence is a no-op.

4. **BP1413 not emitted:** Since fall-through propagation handles the latent case correctly, BP1413 remains unused. If a future architectural change makes propagation infeasible, the diagnostic constant is ready.

---

## Suggested Commit Message

```
feat(compiler): implement SequenceNode branch scheduling in Stage 5

Add ScheduleSequenceNode with ordered Then-pin resolution and
fall-through chaining via _fallThroughTarget dictionary.
Propagate fall-through targets through latent resume blocks,
Branch true/false blocks, WhenNode exit blocks, and nested
Sequences.  Centralize fall-through sealing in SealFallThrough
helper, replacing 3 ad-hoc IrTerm_FallThrough sites.

Add 7 Stage5 tests: 2-branch ordering, unconnected pin skip,
Return short-circuit, nested Sequence, latent-in-Sequence
propagation, zero-branch fall-through, Branch-inside-Sequence.
Update BP1412 tests: Sequence scheduling no longer triggers
BP1412; convert to assert correct scheduling.

Define BP1413 as safety valve (not yet emitted).
Full suite: 1636 passed, 4 known pre-existing failures.
```

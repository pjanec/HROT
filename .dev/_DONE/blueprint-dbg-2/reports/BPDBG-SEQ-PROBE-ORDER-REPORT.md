# BPDBG-SEQ-PROBE-ORDER Report

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-11
**Status:** COMPLETE — `Failed: 7` (all pre-existing reds), `Passed: 1831`

---

## 1. Verified Root Cause

**ScheduleSequenceNode had two compounding defects:**

### Defect A — Unconditional SourceNodeId overwrite

`Stage5_Schedule.cs:561` (before fix):
```csharp
bb.SourceNodeId = seq.Id;   // ← unconditional overwrite
```

When a block already had a preceding exec node (e.g. `SetVarB → Sequence S1` in the same
block), `bb.SourceNodeId` was already set to `svBId` by the default case's `??=` in the
scheduler loop. The `= seq.Id` overwrote it unconditionally, making `DebugProbeInsertion`
see `blockSourceNodeId = s1Id` — the wrong node.

### Defect B — No ExecEntryNodeId tag for the sequence

`ScheduleSequenceNode` emitted no IR statements and added no `ExecEntryNodeId` annotation.
So in `DebugProbeInsertion`:
- `coveredByExecEntryId(s1Id) = false` (no statement tagged with s1Id)
- `coveredByOriginId(s1Id) = false` (no WriteCursor for it)
- `needsHeaderProbe = true` → **the s1Id header probe was prepended to the FRONT of the block**,
  before SetVarB's per-node probe.

**Resulting probe order:** `[s1Id, svBId, svCId, delay1Id, …]`
**Actual execution order:** `[svBId, s1Id, svCId, delay1Id, …]`

The Sequence's probe fired first, so `_stepResumePending` re-paused on `s1Id` (the
SequenceNode), silently skipping SetVarB. The user stepped from `delay0Id` and landed on the
Sequence rather than on `SetVarB` which ran first.

---

## 2. Stage5 Fix

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`

### Change 1 — `??=` instead of unconditional `=`

```csharp
// BEFORE
bb.SourceNodeId = seq.Id;

// AFTER
bb.SourceNodeId ??= seq.Id;
```

This preserves a preceding exec node's SourceNodeId. The sequence takes the SourceNodeId
slot only when it is the block's first exec node (SourceNodeId was null).

### Change 2 — Emit `seq-probe-anchor` at current block position

```csharp
bb.Statements.Add(new IrStatement
{
    Operation = new IrOp_Const("0", Stage5_Schedule.Int32Type),
    Debug = new IrDebugAnnotation
    {
        GraphId         = _graph.Id,
        NodeId          = seq.Id,
        Synthesized     = "seq-probe-anchor",
        ExecEntryNodeId = seq.Id,
    },
});
```

Added after the `successors.Count == 0` early return and before `bb.Terminator = new
IrTerm_Goto(...)`. This places the anchor at the sequence's execution position (after all
preceding exec-node statements, before the Goto dispatch).

`DebugProbeInsertion` then:
- Finds `ExecEntryNodeId = seq.Id` on the anchor statement
- Inserts a `NodeEnter(seq.Id)` probe **immediately before** the anchor (in-order)
- `coveredByExecEntryId(seq.Id) = true` → `needsHeaderProbe = false` → no header probe

**Resulting probe order (after fix):** `[svBId, s1Id, svCId, delay1Id, …]` — matches execution order.

The anchor mirrors the existing `exec-probe-anchor` / `return-probe-anchor` pattern already
present in `ScheduleBlock` for the default case and ReturnNode.

---

## 3. ScheduleBranchNode Finding

**ScheduleBranchNode does NOT have the same defect.**

Proof:
1. `ScheduleBranchNode` does **not** overwrite `bb.SourceNodeId` at all (no assignment in that method).
   The SourceNodeId is preserved from whichever preceding exec node set it first.
2. `TagFirstNewStatement(bb.Statements, branchStmtsBefore, bn.Id)` tags the first statement of the
   condition resolution group with `ExecEntryNodeId = bn.Id`. The condition always produces at
   least one statement (either a synthesized `false` const when no condPin, or a ResolveDataPin
   result when condPin is present).
3. Therefore `coveredByExecEntryId(bn.Id) = true` in `DebugProbeInsertion` — no header probe is
   prepended, and the per-node probe is inserted in-order at the tagged statement position.

**No change to `ScheduleBranchNode` is needed.** The branch probe correctly fires in execution
order regardless of what precedes it in the block.

Note: `ScheduleBranchNode` does not set `bb.SourceNodeId` even when the branch is the first exec
node in a block (unlike the default case which does `bb.SourceNodeId ??= node.Id`). That is a
pre-existing separate concern (missing null-guard), not the probe-order defect described in the
spec, and is out of scope for this batch.

---

## 4. Single-Sequence-First Block: One Probe Only

When the Sequence IS the block's first exec node (no preceding exec node):

- Before fix: `bb.SourceNodeId = seq.Id` (unconditional, still correct in this case).
  No anchor → `coveredByExecEntryId = false` → `needsHeaderProbe = true` → header probe emitted.
  **1 probe** (header path).

- After fix: `bb.SourceNodeId ??= seq.Id` (no-op when SourceNodeId is already null → sets seq.Id,
  same result). Anchor emitted with `ExecEntryNodeId = seq.Id`.
  `coveredByExecEntryId(seq.Id) = true` → `needsHeaderProbe = false` → no header probe.
  **1 probe** (ExecEntryNodeId path, inline anchor). The header path is suppressed.

Result: exactly one `NodeEnter` probe for the sequence in both cases. No double probe.
Verified by Test 8 (`SingleSequenceFirstBlock_ExactlyOneProbeForSequence`): asserts `count == 1`
in the generated source and `seqProbeCount == 1` at runtime.

---

## 5. New Probe-Order Test (Test 7 in PerNodeProbesTests)

**Test:** `ProbeOrder_SetVarBThenSequenceS1_RecordsSvBIdBeforeS1Id`

Graph: `Entry → SetVarB(svBId) → S1(s1Id) { Then0: SetVarC(svCId) → Return }`

- SetVarB and S1 are scheduled in the same IR block (S1 is SetVarB's exec successor).
- Records probe arrival order via `CapturingDebugSession.NodeEntries`.
- Asserts `svBIdStr` appears at a lower index than `s1IdStr` in `NodeEntries`.
- Also asserts `BreakpointTargets[svBId] == svBId` and `BreakpointTargets[s1Id] == s1Id`
  (one-to-one preserved for both).

This directly gates the root-cause defect: before the fix, `s1Id` was at index 0 and `svBId`
at index 1. After the fix, `svBId` is at index 0 and `s1Id` at index 1.

---

## 6. Test 10 Landing Update (s1Id → svBId)

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/TickBridgeTests.cs`

`TickBridge_StepOverSequenceThen0Latent_LandsOnFirstNodeOfThen1_NotLastDelay` (Test 10)

**Before** (from BPDBG-STEPOVER-LATENT-SEQUENCE batch — testing broken intermediate state):
```
// First probe of resumed tick = s1Id.
// _stepResumePending: _recorder.Count == 1 → re-pause on s1Id.

Assert.True(landingNodeId == s1IdStr, …);    // ← landing on S1.Sequence
// Second step:
Assert.True(secondNodeId == svBIdStr, …);    // ← SetVarB skipped to 2nd position
```

**After** (correct probe order, SetVarB first):
```
// First probe of resumed tick = svBId (SetVarB — first exec node in Then1 block).
// _stepResumePending: _recorder.Count == 1 → re-pause on svBId.

Assert.True(landingNodeId == svBIdStr, …);   // ← landing on SetVarB (correct)
Assert.True(landingNodeId != s1IdStr, …);    // ← regression guard for probe-order bug
// Second step:
Assert.True(secondNodeId == s1IdStr, …);     // ← S1.Sequence at position 1
```

Also updated the inner comment block to correctly describe the post-fix probe order.

---

## 7. Golden Snapshot Diffs

**No golden snapshot updates required.**

The three `Schedule_ProducesExpectedIr` assets (LibraryMath, InstanceCounter, MoveToAndFire)
do not contain SequenceNode+preceding-exec-node combinations that produce `seq-probe-anchor`
statements:

- **LibraryMath** — Library graph with no SequenceNode.
- **InstanceCounter** — Instance graph; the SequenceNode IS the first exec node in its block
  (entry block: `EventEntry → Sequence → branches`). The anchor replaces the header probe at
  the same position; the IrPrinter output is **unchanged** because both the old header probe
  and the new anchor are internal IR annotation metadata — the `IrPrinter.PrettyPrint` output
  does not print `Synthesized` or `ExecEntryNodeId` fields.
- **MoveToAndFire** — AiPrimitive with no SequenceNode scheduling the defect pattern.

The `CF2_AuthoredIdProbeTests` tests compile `Count4.bp.json`, which has the topology
`Entry → Sequence → (Then0: SetVariable → FunctionCall; Then1: Delay → Return)`.
The Sequence IS the first exec node in the entry block. After the fix, the anchor replaces
the header probe — `CF2_AllExecNodes_HaveExactlyOneProbe` asserts `CountProbesFor(seqGuid) == 1`,
which continues to pass (one probe, just via the ExecEntryNodeId path now). No golden change.

The `Emit` snapshots (generated C# source, not IR) would be affected if new `DebugProbe.NodeEnter`
calls were added or reordered. The `DebugProbe.NodeEnter` call for the sequence is emitted in
both old and new code — only its **position** relative to SetVarB changes. The existing Emit
snapshots (DoorActor, HealthRegen, InstanceCounter, LibraryMath) don't have `SetVarB → Sequence`
patterns where the order matters (or have no Sequence-preceded-by-exec-node patterns at all).
All `AiPrimitive_EmitMatchesGoldenSource` tests that do compare generated source are pre-existing
failures (MoveToAndFire, HasVisibleTarget) unrelated to this change.

---

## 8. Test Suite Results

### `Hrot.Blueprints.Tests` (Stability filter applied)

```
Failed:     7
Passed:  1831
Skipped:    8
Total:   1846
Duration:  33 s
```

New tests contributing to Passed count (2 added vs prior baseline of 1829 by STEPOVER-LATENT-SEQUENCE):
- `ProbeOrder_SetVarBThenSequenceS1_RecordsSvBIdBeforeS1Id` (Test 7 in PerNodeProbesTests)
- `SingleSequenceFirstBlock_ExactlyOneProbeForSequence` (Test 8 in PerNodeProbesTests)

All 7 failures are documented pre-existing reds (unchanged from prior batches):
1. `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)` — golden diff, pre-existing
2. `AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)` — golden diff, pre-existing
3. `Stage8_PdbContainsEmbeddedSource` — environment, pre-existing
4. `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — environment, pre-existing
5. `TickFrame_1000Frames_AllocatesZeroBytes` — alloc threshold, pre-existing
6. `MoveToAndFire_GeneratedSource_Snapshot` — golden diff, pre-existing
7. `WhenNode_ZeroAllocOnHotPath` — alloc threshold, pre-existing

**0 new failures introduced.**

### `Hrot.Diagnostics.Breakpoints.Tests` (Stability filter applied)

```
Failed:     0
Passed:   128
Skipped:    0
Total:     128
Duration: 437 ms
```

### Targeted regression suite (52 tests, all pass)

The following test classes were verified to be 100% green:
- `PerNodeProbesTests` (8 tests — 6 original + 2 new)
- `CF2_AuthoredIdProbeTests` (7 tests)
- `CF6_SteppingTests`
- `VirtualPointerTests`
- `TickBridgeTests` (10 tests including updated Test 10)
- `SubTickRecorderIntegrationTests`
- `NodeGranularEditorUITests`
- `GoldenIrTests`

---

## 9. Files Changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` | `ScheduleSequenceNode`: `=` → `??=`; add `seq-probe-anchor` before Goto |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/TickBridgeTests.cs` | Test 10: updated inner comments + assertions (landing = `svBId` first, `s1Id` second) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/PerNodeProbesTests.cs` | Added Test 7 (`ProbeOrder_SetVarBThenSequenceS1`) and Test 8 (`SingleSequenceFirstBlock`) |

**Not changed** (as required):
- `DebugProbeInsertion.cs` — the `??=` + anchor approach makes the ExecEntryNodeId path handle the
  sequence correctly; no changes to the probe insertion logic were needed.
- `BlueprintDebugSession.cs` — the `_stepResumePending` mechanism is correct; it now lands on
  `svBId` automatically because the recorded probe order is fixed at the compiler level.
- Any `.bp.json` files.

---

## Implementation Summary

Two lines changed in `ScheduleSequenceNode` (Stage5_Schedule.cs):

1. `bb.SourceNodeId ??= seq.Id;` — preserves a preceding exec node's block ownership.
2. An `IrStatement` with `Synthesized="seq-probe-anchor"` and `ExecEntryNodeId=seq.Id` added
   before the `IrTerm_Goto` terminator.

These two changes together ensure that for any block `[ExecNode → SequenceNode]`:
- `blockSourceNodeId = execNodeId` (not clobbered by seq)
- `coveredByExecEntryId(seqId) = true` (anchor provides the tag)
- `needsHeaderProbe = false` (no prepended header)
- Probe for seq emitted inline at the anchor position — after execNode's probe, before the Goto

And for a block `[SequenceNode]` (sequence first):
- `bb.SourceNodeId ??= seq.Id` still sets seqId (null → seqId)
- Anchor has `ExecEntryNodeId = seqId`
- `coveredByExecEntryId(seqId) = true` → header path suppressed
- Exactly one probe for seqId (anchor path, not header path)

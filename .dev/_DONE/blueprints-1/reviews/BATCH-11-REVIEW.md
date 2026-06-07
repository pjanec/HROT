# BATCH-11 Review

**Reviewer:** Dev Lead
**Date:** 2025
**Status:** APPROVED

---

## Scope Covered

- TASK-CP-003: Stage 6 — Lower fully implemented
- `FieldLayout`, `StructureHashComputation`, `LibraryLowering`, `AiPrimitiveLowering`,
  `InstanceLowering`, `WaitLowering_AiPrimitive`, `WaitLowering_Instance`, `DebugProbeInsertion`
- `SynthesizedGuids` helper class
- 9 new IR operations added to `IrOperation.cs`
- 7 new passing tests covering all 7 success conditions (SC1-SC7)

---

## Build & Test Results

- **Build:** 0 errors, 0 warnings
- **Tests:** 175 pass, 3 skip, 0 fail (+7 vs baseline of 168)

---

## Critical Constraint Verification

| Constraint | Result |
|---|---|
| `__phase` byte is FIRST in WorkingState (before user fields) per §9.6 | PASS |
| StructureHash computed AFTER FieldLayout (fields have Offset/Size) | PASS |
| `IrOp_CheckCursorVersion` at start of each resume block (Q-18.1) | PASS (SC2 test confirms) |
| `IrOp_ReadInstanceVersion` / `IrOp_WriteCursorInstanceVersion` at suspend points | PASS |
| Library with no function graphs emits BP5001 (SC6) | PASS |
| Debug mode inserts `IrOp_DebugProbe_NodeEnter` at block starts with NodeId (SC7) | PASS |
| AiPrimitive dispatch block + phase-0 block + phase-1 check block (SC1) | PASS |
| Instance cursor dispatch + initial block + resume-check block with CheckCursorVersion (SC2) | PASS |
| StructureHash changes on field name/type change; stable on body-only change (SC3/SC4/SC5) | PASS |

---

## Deviation Assessment

### Stage6_Lower execution order: dispatch-lowering BEFORE FieldLayout
**Verdict: APPROVED (IMPROVEMENT over instructions).** The design doc §9.6 comment states
synthesized fields get `Offset/Size assigned by FieldLayout`, which logically implies field
synthesis happens first, then layout assignment. If FieldLayout ran before dispatch lowering,
synthesized `__phase` and `__waitUntilTime` fields would have no layout. The implementation
is correct; the instructions had an ordering mistake that would have caused silent bugs.

### Additional IR read operations added
**Verdict: APPROVED.** `IrOp_ReadWorkingStatePhase`, `IrOp_ReadCursorResumeAt`, and
`IrOp_ReadWorkingStateWaitUntilTime` are necessary for the dispatch block logic. The
instructions listed only write operations but the design inherently requires paired reads.

### Dispatch block uses `IrTerm_Branch` chain (not switch)
**Verdict: ACCEPTED.** The IR has no switch terminator — `IrTerm_Branch` is the only
conditional control flow available. A chain of branches correctly encodes multi-phase dispatch.
This is the expected implementation approach given the IR type hierarchy.

---

## Test Quality Assessment

7 new tests, all directly verifying a named success condition. Tests exercise the full
Stage5→Stage6 pipeline using `BlueprintAssetBuilder`. This is a high bar — tests aren't just
unit-testing individual functions, they're verifying the end-to-end lowering result.

SC3/SC4/SC5 (structure hash) tests verify the critical property that the hash reflects layout
not graph body — essential for the hot-reload reconciliation logic.

---

## Conclusion

BATCH-11 is **APPROVED**. Stage 6 lowering is complete and correct. Baseline preserved.

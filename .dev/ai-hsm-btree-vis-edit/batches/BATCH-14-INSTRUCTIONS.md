# BATCH-14: Corrective (BATCH-13 P1) + TASK-BB-1f-05 + Phase 1.5g (1g-01, 1g-02)

**Batch Number:** BATCH-14
**Tasks:** Corrective-P1 (BATCH-13 gaps), TASK-BB-1f-05, TASK-BB-1g-01, TASK-BB-1g-02
**Phase:** 1.5f + 1.5g
**Estimated Effort:** 4–6 hours
**Priority:** HIGH
**Dependencies:** BATCH-13 (approved with P1)

---

## Onboarding

1. **BATCH-13 Review:** .dev/ai-hsm-btree-vis-edit/reviews/BATCH-13-REVIEW.md
2. **Task detail:** .dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md §TASK-BB-1f-05, 1g-01, 1g-02

### 1. Corrective P1 (Missed Reader-Writer Conflict)

In BATCH-13, the instruction snippet for skipping read-only actions was flawed, but instead of fixing the logic during evaluation, you committed the flawed short-circuit. 
Currently in HsmValidator.cs: 
``csharp
if (!HasWritingAction(state)) continue; // This completely misses readers!
``
If a state only reads, it is skipped entirely. If a parallel state writes, it gets grouped alone and the count is 1, so NO CONFLICT is reported. This breaks BB §9.6! One reader + one writer across parallel regions is a WRITER CONFLICT (data race). 

**Fix:**
- Remove the if (!HasWritingAction(state)) continue; from the collection loop. All states accessing the variable MUST be collected into the dictionary.
- During evaluation (the list.Count > 1 block), emit a diagnostic if AND ONLY IF *at least one state in the list* has HasWritingAction(state) == true. (If all are readers, it's safe).
- Revert Validate_MixedAccess_OneReadOnlyOneReadWrite_NoConflict to _ProducesConflict and ensure it asserts Contains!

### 2. TASK-BB-1f-05 — Suppression metadata persistence
Implement suppression of the cross-region collision and unused warnings. 
- Persist per-pair conflict suppressions (.SuppressBlackboardConflict) and per-variable unused suppressions (.SuppressUnusedWarning) using [...Layout] methods in the code generation.
- Since we aren't modifying the actual layout emit files yet, ensure the HsmAsset / BehaviorTreeAsset / BlackboardAuthoringWindow has the required layout metadata serialization handling for these arrays/lists. 
- Create tests for round-tripping suppressions.

### 3. TASK-BB-1g-01 & 1g-02 — VariablesPanelControl
- Extract VariablesPanelControl into Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs
- Create settings/flags for: single vs dual list, aliasing on/off, schema source.
- Migrate the BlackboardAuthoringWindow.cs (or BTree/HSM panels) to use this control. 
- Verify BTree/HSM tests pass. 

Make sure all tests pass and TreatWarningsAsErrors=true succeeds.

# BATCH-15: TASK-BB-1f-05 + Phase 1.5g (1g-01, 1g-02, 1g-03)

**Batch Number:** BATCH-15
**Tasks:** TASK-BB-1f-05, TASK-BB-1g-01, TASK-BB-1g-02, TASK-BB-1g-03
**Phase:** 1.5f + 1.5g
**Estimated Effort:** 6-8 hours
**Priority:** NORMAL
**Dependencies:** BATCH-14 (approved)

---

## Onboarding

1. **BATCH-14 Review:** .dev/ai-hsm-btree-vis-edit/reviews/BATCH-14-REVIEW.md
2. **Task detail:** .dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md §TASK-BB-1f-05, 1g-01, 1g-02, 1g-03

### TASK-BB-1f-05 — Suppression metadata persistence
Implement suppression of the cross-region collision and unused warnings. 
- Extend [...Layout] methods in C# (or underlying data structures for layout metadata) to persist per-pair conflict suppressions (.SuppressBlackboardConflict) and per-variable unused suppressions (.SuppressUnusedWarning).
- The BehaviorTreeAsset and HsmAsset already possess layout serialization capabilities, piggyback onto them.
- Add round-trip tests to verify suppressions are preserved across save-reload cycles.

### Phase 1.5g Task Extraction & Migrations
1. **TASK-BB-1g-01:** Extract VariablesPanelControl out of the BTree/HSM panel logic into Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs. Ensure it supports customizable configurations (single vs dual list, aliasing on/off, schema source). Test that it handles these flags properly.
2. **TASK-BB-1g-02:** Migrate the BTree/HSM Variables panel to use VariablesPanelControl initialized for single-list + aliasing-on. Ensure existing tests don't break.
3. **TASK-BB-1g-03:** Migrate Blueprint variable panel to the new VariablesPanelControl initialized for dual-list + aliasing-off and per-section budget displays. Provide visual tests.

Ensure all new functionality compiles properly and tests succeed with TreatWarningsAsErrors=true.

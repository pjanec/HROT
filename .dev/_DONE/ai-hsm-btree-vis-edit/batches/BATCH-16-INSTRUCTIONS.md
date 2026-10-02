# BATCH-16: Phase 1.5g Blueprint JSON upgrades (1g-04, 1g-05, 1g-06)

**Batch Number:** BATCH-16
**Tasks:** TASK-BB-1g-04, TASK-BB-1g-05, TASK-BB-1g-06
**Phase:** 1.5g
**Estimated Effort:** 4-6 hours
**Priority:** NORMAL
**Dependencies:** BATCH-15 (approved)

---

## Onboarding

1. **BATCH-15 Review:** .dev/_DONE/ai-hsm-btree-vis-edit/reviews/BATCH-15-REVIEW.md
2. **Task detail:** .dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md §TASK-BB-1g-04, 1g-05, 1g-06

### Task details
1. **TASK-BB-1g-04 — Blueprint JSON Comment field + emit:** Extend the Blueprint JSON schema (BlueprintAsset.cs or similar in Hrot.Blueprints.Editor model) with per-variable Comment. Modify AiPrimitiveEmitter to output /// blocks above generated fields. Add a unit test verifying /// blocks appear in the generated struct.
2. **TASK-BB-1g-05 — Blueprint JSON VariableOrder + emit:** Ensure ordering from the UI (VariablesPanelControl) persists to the Blueprint asset via a VariableOrder array. The generator should process members in this explicit order. Tests should verify explicitly ordered struct layout emit.
3. **TASK-BB-1g-06 — Blueprint Params rename ? BlackboardField refactor:** We need to refactor Blueprint Params references inside action functions properly to track variable renaming inside the VariablesPanelControl. The refactor UI/services should be capable of mapping changes to Blueprint Variable fields. Follow instructions in the Detailed Design and complete the rename workflows.

Ensure all compilation passes and tests are valid without any compilation errors.

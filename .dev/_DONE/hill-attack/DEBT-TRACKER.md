# Hill Attack Group Behavior -- Technical Debt Tracker

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| P2-01 | BATCH-01 review | Missing unit tests for HA007 (Condition_HasTarget, Action_CreepToAndBeyondSlot), HA008 (Action_AimAndFireSpecific, Action_ReverseToBaseline), HA009 (HullDownAttackMapper, BTree catalog). SC-HA007-1 through SC-HA009-4 are all untested. | P2 | BATCH-02 | RESOLVED (BATCH-02 Corrective-1: 19 tests added) |
| P2-02 | BATCH-01 review | SC-HA002-1 test (`Solver_FindsEntitiesInsidePolygon`) only places 1 entity inside polygon; spec requires 3 inside + 2 outside to validate TargetCount==3. Also SC-HA002-3 (65-request overflow solver test) is not implemented. | P2 | BATCH-02 | RESOLVED (BATCH-02 Corrective-1: both scenarios added) |
| P2-03 | BATCH-01 review | SC-HA003-2 (EqsTargetPool zeroed after reset) and SC-HA003-3 (AreaQueryInitializationSystem registered before BTreeTickSystem, confirmed by inspecting registered system list) are untested. | P2 | BATCH-02 | RESOLVED (BATCH-02 Corrective-1: both tests added) |
| P2-04 | BATCH-02 review | `allParticipate` flag bypasses wave parity for platoons with roster count <= 3; not in spec and untested. May cause unexpected wave assignments in small platoon scenarios. | P2 | BATCH-03 | OPEN |
| P2-05 | BATCH-02 review | Condition_* commander nodes use `[BTreeAction]` instead of `[BTreeCondition]` attribute. No functional impact; cosmetic inconsistency. | P2 | BATCH-03 | OPEN |
| P2-06 | BATCH-02 review | `BaselineReservedMask` staleness when all slots are burned: baseline fallback reverts to nearest slot regardless of reservation. Known limitation documented in developer report. | P2 | BATCH-03 | OPEN |

Legend:
- P1 = Critical (never enters tracker; always becomes Corrective Task 0 in next batch)
- P2 = Should fix (tracked here, assigned target batch)
- P3 = Nice to have (tracked here, best-effort)
- Status: OPEN / RESOLVED (do not delete resolved rows)

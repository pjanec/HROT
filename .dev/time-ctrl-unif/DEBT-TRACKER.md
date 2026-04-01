# Technical Debt Tracker — Time Controller Unification

**Project:** `time-ctrl-unif`  
**Maintained by:** Dev Lead

> **Rules:**
> - P1 items → Corrective Task 0 in next batch (never enter this tracker)
> - P2/P3 items → added here with source batch, description, target batch
> - When resolved → mark ✅ (do not delete rows)

---

## Open Items

| ID | Priority | Source Batch | Description | Target Batch | Status |
|----|----------|-------------|-------------|--------------|--------|
| DT-001 | P3 | BATCH-01 | `FdpEventBus.Publish<T>()` enforces `[EventId]` with no documented opt-out for in-process-only domain types. Domain types must silently use `PublishManaged/ConsumeManaged`. Distinction undocumented at call site. | Future docs/improvement batch | Open |
| DT-002 | P3 | BATCH-01 | TASK-DETAIL.md spec table for `FrameOrderDescriptor` states `TargetSimTime` at `[Key(3)]` but `TimeScale` already occupies that ordinal. Stale spec table — no code impact, ordinal correctly assigned at `[Key(4)]`. | Maintenance batch | Open |
| DT-003 | P2 | BATCH-02 | `MasterSyncController.SwitchToDeterministic(slaveNodeIds)` silently ignores its parameter; effective slave set is fixed at construction. If Orchestrator passes a different set after node join/leave, ACK tracking will be wrong. Must be documented at call site in Phase 5 wiring. | BATCH-05 | Open |
| DT-004 | P2 | BATCH-02 | `UpdateStepping()` processes FrameStepCompletedEvent ACKs by NodeID only, without FrameID filter. Late DDS retransmit could incorrectly clear a pending ACK slot. Should filter: `ack.FrameID == _lastStepFrameID`. | BATCH-04 or corrective | Open |
| DT-005 | P3 | BATCH-03 | Rapid BarrierPending→Continuous resume (SwitchTimeModeEvent(Continuous) while still BarrierPending) is not unit tested in SlaveSyncController. Code path correct but untested. | BATCH-05 (TCU-T006 integration test) | Open |

---

## Resolved Items

| ID | Priority | Source Batch | Description | Resolved In |
|----|----------|-------------|-------------|-------------|
| — | — | — | No resolved items yet | — |

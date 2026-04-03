# Task Tracker: ClusterMaster CQRS Post-Refactoring Cleanups (cqrs-2)

**Reference:** See [TASK-DEFINITIONS.md](./TASK-DEFINITIONS.md) for full task specifications and success conditions.

---

## Phase 1 — Low-Risk Cleanups and Bug Fixes

**Goal:** Eliminate Primitive Obsession in events and handlers; fix critical bootstrap latch bug.

- [x] **TASK-D03** ClusterStateTransitionedEvent.NewStateId → ClusterState enum ✅ BATCH-01
- [x] **TASK-D04** Remove handler const int OperationId constants ✅ BATCH-01
- [x] **TASK-D05** OrchestrationStatusCode static class → enum; update StatusCode fields in events ✅ BATCH-01
- [x] **TASK-D06** Bootstrap latch case-insensitive subsystem name comparison ✅ BATCH-01

---

## Phase 2 — Explicit Payload Structs

**Goal:** Replace all boxed primitive DomainPayload values with named structs; eliminate brittle `is int` pattern-matching.

- [x] **TASK-D01** CommitStatePayload / ReplaySeekPayload / AbortTransactionPayload + update ClusterSlave, Translators, ClusterMaster ✅ BATCH-02

---

## Phase 3 — Anti-Corruption Layer Improvements and Architecture Cleanup

**Goal:** Add operation type context to NodeOpCompletedEvent; remove ScenarioSerializer's application-layer knowledge.

- [x] **TASK-D02** NodeOpType in NodeOpCompletedEvent + NodeOpStatus; refactor DeserializeResultPayload; remove JSON from ClusterMaster ✅ BATCH-03
- [x] **TASK-D07** (partial) ScenarioSerializer: PeekSubsystemType/IsMatchingSubsystem removed; HrotScenarioEnvelope created; 3 handlers moved to Hrot.Common ✅ BATCH-03

---

## Tech Debt

- **TASK-D07-FULL:** `ScenarioSerializer.Serialize()` remove `ScenarioHeader` parameter; `Deserialize()` accept raw entities node; remove `ScenarioHeader.cs` from FDP. Deferred — requires updating 15+ test files. Source: cluster-master-cqrs-2/TASK-D07.

---

## Status: **ALL BATCHED TASKS COMPLETE** ✅

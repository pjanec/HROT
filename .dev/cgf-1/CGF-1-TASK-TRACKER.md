# CGF-1 Task Tracker

**Reference:** See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md) for full task descriptions and success conditions.  
**Design:** See [CGF-1-DESIGN.md](./CGF-1-DESIGN.md) for the architectural design.

**Active batch:** [CGF-1-BATCH-15](batches/CGF-1-BATCH-15-INSTRUCTIONS.md).  
**Last reviewed:** [CGF-1-BATCH-14](reviews/CGF-1-BATCH-14-REVIEW.md) — CONDITIONALLY APPROVED (2026-03-28).  
**Batches / reports / reviews:** `.dev/cgf-1/batches/`, `.dev/cgf-1/reports/`, `.dev/cgf-1/reviews/` (prefix `CGF-1-`).  
**Debt (P2/P3):** [.dev/DEBT-TRACKER.md](../DEBT-TRACKER.md).

> **Scope:** Phases 1–3 of the CGF workstream — Control-Plane Foundation, State & Time
> Synchronization, and Persistence. Phase 4 (Urban Combat AI) begins only after all
> Phase 3 tasks are complete and their CI gates are passing.
>
> **Status key:** `[ ]` = not done | `[x]` = done  
> **Progress (Phase 1):** 5 / 5 tasks done. **Phase 2:** 5 / 5 complete. **Phase 3:** 5 / 9 done (S0301–S0303, S0306–S0307); S0303 production checkpoint wiring → [CGF-1-BATCH-15](batches/CGF-1-BATCH-15-INSTRUCTIONS.md) Part A; S0205 residual closed in BATCH-10; subprocess CI Opportunistic.

---

## Phase 1 — Skeleton: Control-Plane Foundation

**Goal:** Prove the Orchestrator can watch nodes and nodes can register with it;
establish DDS schema, centralize identity allocation.

- [x] **CGF1-S0101** Orchestration DDS Schema Definition [details](./CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition)
- [x] **CGF1-S0102** Bagira.Orchestrator Bootstrapping [details](./CGF-1-TASK-DETAIL.md#cgf1-s0102--bagiraorchestrator-bootstrapping)
- [x] **CGF1-S0103** Centralized Identity Migration [details](./CGF-1-TASK-DETAIL.md#cgf1-s0103--centralized-identity-migration)
- [x] **CGF1-S0104** DrillSlave Foundation [details](./CGF-1-TASK-DETAIL.md#cgf1-s0104--drillslave-foundation) — done (CGF-1-BATCH-02); see BATCH-02 review for P1 follow-up in BATCH-03
- [x] **CGF1-S0105** Orchestrator Health Monitoring & Bootstrap Recovery [details](./CGF-1-TASK-DETAIL.md#cgf1-s0105--orchestrator-health-monitoring--bootstrap-recovery) — done (CGF-1-BATCH-03 + polish CGF-1-BATCH-04)

---

## Phase 2 — State & Time: DSM and Synchronization

**Goal:** Prove the cluster can traverse the Drill State Machine safely; validate
Future Barrier time-mode swap is frame-perfect; establish deterministic CI loop.

- [x] **CGF1-S0201** BFS Transition Planner [details](./CGF-1-TASK-DETAIL.md#cgf1-s0201--bfs-transition-planner) — done (CGF-1-BATCH-04)
- [x] **CGF1-S0202** DSM Handler Wiring [details](./CGF-1-TASK-DETAIL.md#cgf1-s0202--dsm-handler-wiring) — done (CGF-1-BATCH-05); heartbeat `LocalDsmState` → [CGF-1-BATCH-06](batches/CGF-1-BATCH-06-INSTRUCTIONS.md)
- [x] **CGF1-S0203** Time Strategy Proxying [details](./CGF-1-TASK-DETAIL.md#cgf1-s0203--time-strategy-proxying) — done (CGF-1-BATCH-06)
- [x] **CGF1-S0204** Future Barrier Implementation [details](./CGF-1-TASK-DETAIL.md#cgf1-s0204--future-barrier-implementation) — done (CGF-1-BATCH-07); DDS translator wiring → [CGF-1-BATCH-08](batches/CGF-1-BATCH-08-INSTRUCTIONS.md)
- [x] **CGF1-S0205** Deterministic CI Hookup [details](./CGF-1-TASK-DETAIL.md#cgf1-s0205--deterministic-ci-hookup) — done (CGF-1-BATCH-08 + CGF-1-BATCH-09): CI mode, coordinator + `PendingTimeMode`, `FinalEntitySnapshot` reproducibility; **IG `SetFilter` / CGF kernel listener / subprocess CI** → [CGF-1-BATCH-10](batches/CGF-1-BATCH-10-INSTRUCTIONS.md) debt

---

## Phase 3 — Persistence: Scenarios, Checkpoints & Replay

**Goal:** Non-blocking recording, replay, binary checkpointing, scenario file
management, and live-from-replay temporal interlock — all regression-tested.

- [x] **CGF1-S0301** Storage Gateway [details](./CGF-1-TASK-DETAIL.md#cgf1-s0301--storage-gateway) — done (CGF-1-BATCH-10): `StorageGatewayModule`, `FileManifestEntry`, `GatewayResult`, `NodeDistributionTarget`; `PullToNasAsync`/`PushToNodesAsync`; `DrillMaster` gateway hook + `NodeOpStatus` reader; `StorageGatewayTests` (2 pass)
- [x] **CGF1-S0302** Portable Scenario Loading [details](./CGF-1-TASK-DETAIL.md#cgf1-s0302--portable-scenario-loading) — done [CGF-1-BATCH-13](batches/CGF-1-BATCH-13-INSTRUCTIONS.md) Part B (`EditLoadDsmHandler`; `ScenarioSerializer` DOM; `PrefetchScenario` before `LoadingEdit`; 3 unit tests + `TransitionPlannerTests.PlanWithScenarioId_InjectsStorageGatewayStep`)
- [x] **CGF1-S0303** 3-Step Binary Checkpointing — **artefacts** in [CGF-1-BATCH-14](batches/CGF-1-BATCH-14-INSTRUCTIONS.md) (`CheckpointIOWorker`, `ITickableDsmHandler`, `CheckpointDsmHandler`, `DrillSlave` polling, `LiveLoadDsmHandler` drain hook; tests). **Production SimHost wiring** (bootstrap + `SimHostApp`) → [CGF-1-BATCH-15](batches/CGF-1-BATCH-15-INSTRUCTIONS.md) Part A — see [BATCH-14 review](reviews/CGF-1-BATCH-14-REVIEW.md#critical-gap-checkpoint-path-not-registered-in-production-simhost).
- [ ] **CGF1-S0304** Dynamic Recording Modules [details](./CGF-1-TASK-DETAIL.md#cgf1-s0304--dynamic-recording-modules)
- [ ] **CGF1-S0305** Live-from-Replay Temporal Interlock [details](./CGF-1-TASK-DETAIL.md#cgf1-s0305--live-from-replay-temporal-interlock)
- [x] **CGF1-S0306** Scenario/Story Serialization Toolkit [details](./CGF-1-TASK-DETAIL.md#cgf1-s0306--scenariostory-serialization-toolkit) — ✅ [CGF-1-BATCH-11](batches/CGF-1-BATCH-11-INSTRUCTIONS.md) Part B
- [x] **CGF1-S0307** Application-Layer Scenario Save/Load Wiring [details](./CGF-1-TASK-DETAIL.md#cgf1-s0307--application-layer-scenario-saveload-wiring) — done [CGF-1-BATCH-12](batches/CGF-1-BATCH-12-INSTRUCTIONS.md); follow-ups → [CGF-1-BATCH-12 review](reviews/CGF-1-BATCH-12-REVIEW.md#gaps-vs-task-detail-cgf1-s0307)
- [ ] **CGF1-S0308** Runtime Story Injection & Deletion [details](./CGF-1-TASK-DETAIL.md#cgf1-s0308--runtime-story-injection--deletion)
- [ ] **CGF1-S0309** Dry Run DSM Handler [details](./CGF-1-TASK-DETAIL.md#cgf1-s0309--dry-run-dsm-handler)

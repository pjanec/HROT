# CGF-1 Task Tracker

**Reference:** See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md) for full task descriptions and success conditions.  
**Design:** See [CGF-1-DESIGN.md](./CGF-1-DESIGN.md) for the architectural design.

**Active batch:** [CGF-1-BATCH-07](batches/CGF-1-BATCH-07-INSTRUCTIONS.md) — wall-tick / orchestration debt + **CGF1-S0204** (future barrier).  
**Last reviewed:** [CGF-1-BATCH-06](reviews/CGF-1-BATCH-06-REVIEW.md) — APPROVED (2026-03-29).  
**Batches / reports / reviews:** `.dev/cgf-1/batches/`, `.dev/cgf-1/reports/`, `.dev/cgf-1/reviews/` (prefix `CGF-1-`).  
**Debt (P2/P3):** [.dev/DEBT-TRACKER.md](../DEBT-TRACKER.md).

> **Scope:** Phases 1–3 of the CGF workstream — Control-Plane Foundation, State & Time
> Synchronization, and Persistence. Phase 4 (Urban Combat AI) begins only after all
> Phase 3 tasks are complete and their CI gates are passing.
>
> **Status key:** `[ ]` = not done | `[x]` = done  
> **Progress (Phase 1):** 5 / 5 tasks done. **Phase 2:** 3 / 5 (CGF1-S0201 BATCH-04; CGF1-S0202 BATCH-05; CGF1-S0203 BATCH-06).

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
- [x] **CGF1-S0203** Time Strategy Proxying [details](./CGF-1-TASK-DETAIL.md#cgf1-s0203--time-strategy-proxying) — done (CGF-1-BATCH-06); stepped `TotalWallTicks` on `SwitchTo` → [CGF-1-BATCH-07](batches/CGF-1-BATCH-07-INSTRUCTIONS.md)
- [ ] **CGF1-S0204** Future Barrier Implementation [details](./CGF-1-TASK-DETAIL.md#cgf1-s0204--future-barrier-implementation)
- [ ] **CGF1-S0205** Deterministic CI Hookup [details](./CGF-1-TASK-DETAIL.md#cgf1-s0205--deterministic-ci-hookup)

---

## Phase 3 — Persistence: Scenarios, Checkpoints & Replay

**Goal:** Non-blocking recording, replay, binary checkpointing, scenario file
management, and live-from-replay temporal interlock — all regression-tested.

- [ ] **CGF1-S0301** Storage Gateway [details](./CGF-1-TASK-DETAIL.md#cgf1-s0301--storage-gateway)
- [ ] **CGF1-S0302** Portable Scenario Loading [details](./CGF-1-TASK-DETAIL.md#cgf1-s0302--portable-scenario-loading)
- [ ] **CGF1-S0303** 3-Step Binary Checkpointing [details](./CGF-1-TASK-DETAIL.md#cgf1-s0303--3-step-binary-checkpointing)
- [ ] **CGF1-S0304** Dynamic Recording Modules [details](./CGF-1-TASK-DETAIL.md#cgf1-s0304--dynamic-recording-modules)
- [ ] **CGF1-S0305** Live-from-Replay Temporal Interlock [details](./CGF-1-TASK-DETAIL.md#cgf1-s0305--live-from-replay-temporal-interlock)
- [ ] **CGF1-S0306** Scenario/Story Serialization Toolkit [details](./CGF-1-TASK-DETAIL.md#cgf1-s0306--scenariostory-serialization-toolkit)
- [ ] **CGF1-S0307** Application-Layer Scenario Save/Load Wiring [details](./CGF-1-TASK-DETAIL.md#cgf1-s0307--application-layer-scenario-saveload-wiring)
- [ ] **CGF1-S0308** Runtime Story Injection & Deletion [details](./CGF-1-TASK-DETAIL.md#cgf1-s0308--runtime-story-injection--deletion)

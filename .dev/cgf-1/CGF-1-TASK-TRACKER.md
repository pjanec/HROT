# CGF-1 Task Tracker

**Reference:** See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md) for full task descriptions and success conditions.  
**Design:** See [CGF-1-DESIGN.md](./CGF-1-DESIGN.md) for the architectural design.

> **Scope:** Phases 1–3 of the CGF workstream — Control-Plane Foundation, State & Time
> Synchronization, and Persistence. Phase 4 (Urban Combat AI) begins only after all
> Phase 3 tasks are complete and their CI gates are passing.
>
> **Status key:** `[ ]` = not done | `[x]` = done

---

## Phase 1 — Skeleton: Control-Plane Foundation

**Goal:** Prove the Orchestrator can watch nodes and nodes can register with it;
establish DDS schema, centralize identity allocation.

- [ ] **CGF1-S0101** Orchestration DDS Schema Definition [details](./CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition)
- [ ] **CGF1-S0102** Bagira.Orchestrator Bootstrapping [details](./CGF-1-TASK-DETAIL.md#cgf1-s0102--bagiraorchestrator-bootstrapping)
- [ ] **CGF1-S0103** Centralized Identity Migration [details](./CGF-1-TASK-DETAIL.md#cgf1-s0103--centralized-identity-migration)
- [ ] **CGF1-S0104** DrillSlave Foundation [details](./CGF-1-TASK-DETAIL.md#cgf1-s0104--drillslave-foundation)
- [ ] **CGF1-S0105** Orchestrator Health Monitoring & Bootstrap Recovery [details](./CGF-1-TASK-DETAIL.md#cgf1-s0105--orchestrator-health-monitoring--bootstrap-recovery)

---

## Phase 2 — State & Time: DSM and Synchronization

**Goal:** Prove the cluster can traverse the Drill State Machine safely; validate
Future Barrier time-mode swap is frame-perfect; establish deterministic CI loop.

- [ ] **CGF1-S0201** BFS Transition Planner [details](./CGF-1-TASK-DETAIL.md#cgf1-s0201--bfs-transition-planner)
- [ ] **CGF1-S0202** DSM Handler Wiring [details](./CGF-1-TASK-DETAIL.md#cgf1-s0202--dsm-handler-wiring)
- [ ] **CGF1-S0203** Time Strategy Proxying [details](./CGF-1-TASK-DETAIL.md#cgf1-s0203--time-strategy-proxying)
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

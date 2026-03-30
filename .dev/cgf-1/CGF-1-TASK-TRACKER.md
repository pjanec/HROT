# CGF-1 Task Tracker

**Reference:** See [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md) for full task descriptions and success conditions.  
**Design:** See [CGF-1-DESIGN.md](./CGF-1-DESIGN.md) for the architectural design.

**Active batch:** CGF-1-BATCH-25 (tech-debt-first follow-up to BATCH-24).  
**Last reviewed:** [CGF-1-BATCH-24](reviews/CGF-1-BATCH-24-REVIEW.md) — APPROVED with corrections (2026-03-28).  
**Last completed:** [CGF-1-BATCH-24](reports/CGF-1-BATCH-24-REPORT.md) — Part A (CGF1-S0310 E2E DSM test script suite) + Part B (Runner multi-subsystem nodeId correctness); **Phase 3 complete** (operational caveats: CI + CLI + fail-loud — see review + DEBT).  
**Debt (P2/P3):** [.dev/DEBT-TRACKER.md](../DEBT-TRACKER.md).

> **Extended specs:** [CGF-1-DESIGN.md](./CGF-1-DESIGN.md) and [CGF-1-TASK-DETAIL.md](./CGF-1-TASK-DETAIL.md) define **CGF1-S0106** and **CGF1-S0310**. **Phase 4** ✅ complete (**CGF-1-BATCH-22**). **BATCH-23:** brain/muscle **DSM parity** (CGF record/replay, IG/IOS matrix), **orchestrator globals** (`GlobalContextDto`), then **S0310** / **S0106**.

> **Scope:** Phases 1–4 of the CGF workstream — Control-Plane Foundation, State & Time
> Synchronization, Persistence, and Generalization. Phase 5 (Urban Combat AI) begins
> only after the Phase 3 + Phase 4 CI gates are all passing.
>
> **Status key:** `[ ]` = not done | `[x]` = done  
> **Progress (Phase 1):** 6 / 6 tasks done (**S0106** ✅ BATCH-23). **Phase 2:** 5 / 5 complete. **Phase 3:** 10 / 10 done (**S0310** ✅ BATCH-24). **Phase 4:** **COMPLETE (BATCH-22)**. **Cross-cutting (BATCH-23 Part A):** CGF record/replay ✅, IG matrix ✅, IOS stubs ✅, orchestrator globals ✅.

---

## Phase 1 — Skeleton: Control-Plane Foundation

**Goal:** Prove the Orchestrator can watch nodes and nodes can register with it;
establish DDS schema, centralize identity allocation.

- [x] **CGF1-S0101** Orchestration DDS Schema Definition [details](./CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition)
- [x] **CGF1-S0102** Bagira.Orchestrator Bootstrapping [details](./CGF-1-TASK-DETAIL.md#cgf1-s0102--bagiraorchestrator-bootstrapping)
- [x] **CGF1-S0103** Centralized Identity Migration [details](./CGF-1-TASK-DETAIL.md#cgf1-s0103--centralized-identity-migration)
- [x] **CGF1-S0104** DrillSlave Foundation [details](./CGF-1-TASK-DETAIL.md#cgf1-s0104--drillslave-foundation) — done (CGF-1-BATCH-02); see BATCH-02 review for P1 follow-up in BATCH-03
- [x] **CGF1-S0105** Orchestrator Health Monitoring & Bootstrap Recovery [details](./CGF-1-TASK-DETAIL.md#cgf1-s0105--orchestrator-health-monitoring--bootstrap-recovery) — done (CGF-1-BATCH-03 + polish CGF-1-BATCH-04)
- [x] **CGF1-S0106** Orchestrator ImGui Scenario & Story Controls [details](./CGF-1-TASK-DETAIL.md#cgf1-s0106--orchestrator-imgui-scenario--story-controls) — done (CGF-1-BATCH-23 Part B: `OrchestratorScenarioPanel` with beige child panels, 6 sections, wired into `OrchestratorSubsystem`)

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
- [x] **CGF1-S0303** 3-Step Binary Checkpointing — [CGF-1-BATCH-14](batches/CGF-1-BATCH-14-INSTRUCTIONS.md) (worker, handler, `ITickableDsmHandler`, tests) + [CGF-1-BATCH-15](batches/CGF-1-BATCH-15-INSTRUCTIONS.md) Part A (`SimHostApp` / `NodeBootstrapper` production wiring, empty NAS dir fail-loud).
- [x] **CGF1-S0304** Dynamic Recording Modules [details](./CGF-1-TASK-DETAIL.md#cgf1-s0304--dynamic-recording-modules) — **implementation + tests** in [CGF-1-BATCH-16](batches/CGF-1-BATCH-16-INSTRUCTIONS.md); **production `SimHostApp` replay handler wiring** + contract polish → [CGF-1-BATCH-17](batches/CGF-1-BATCH-17-INSTRUCTIONS.md) Part A ✅ ([BATCH-16 review](reviews/CGF-1-BATCH-16-REVIEW.md))
- [x] **CGF1-S0305** Live-from-Replay Temporal Interlock [details](./CGF-1-TASK-DETAIL.md#cgf1-s0305--live-from-replay-temporal-interlock) — orchestrator **`ReplayMasterModule`** + SimHost **`ReplayLoadDsmHandler`** / **`DrillSlave`** + **`FullBranchPipelineTests`** ✅ [CGF-1-BATCH-17](batches/CGF-1-BATCH-17-INSTRUCTIONS.md) / [CGF-1-BATCH-18](batches/CGF-1-BATCH-18-INSTRUCTIONS.md); CGF **`PrepareLive`** / **`DrillSlave`** ✅ [CGF-1-BATCH-19](batches/CGF-1-BATCH-19-INSTRUCTIONS.md) Part A
- [x] **CGF1-S0306** Scenario/Story Serialization Toolkit [details](./CGF-1-TASK-DETAIL.md#cgf1-s0306--scenariostory-serialization-toolkit) — ✅ [CGF-1-BATCH-11](batches/CGF-1-BATCH-11-INSTRUCTIONS.md) Part B
- [x] **CGF1-S0307** Application-Layer Scenario Save/Load Wiring [details](./CGF-1-TASK-DETAIL.md#cgf1-s0307--application-layer-scenario-saveload-wiring) — done [CGF-1-BATCH-12](batches/CGF-1-BATCH-12-INSTRUCTIONS.md); follow-ups → [CGF-1-BATCH-12 review](reviews/CGF-1-BATCH-12-REVIEW.md#gaps-vs-task-detail-cgf1-s0307)
- [x] **CGF1-S0308** Runtime Story Injection & Deletion [details](./CGF-1-TASK-DETAIL.md#cgf1-s0308--runtime-story-injection--deletion) — SimHost MVP ✅ [CGF-1-BATCH-19](batches/CGF-1-BATCH-19-INSTRUCTIONS.md) Part B; **TASK-DETAIL residual closed** ✅ [CGF-1-BATCH-20](batches/CGF-1-BATCH-20-INSTRUCTIONS.md) Part A (CGF `StoryLoadDsmHandler` + `NodeOpStatus.IsParticipating` ACK wired; `DrillMaster` ACK gating = intentional MVP delta — see §S0308 note in TASK-DETAIL)
- [x] **CGF1-S0309** Dry Run DSM Handler [details](./CGF-1-TASK-DETAIL.md#cgf1-s0309--dry-run-dsm-handler)
- [x] **CGF1-S0310** E2E DSM Test Script Suite [details](./CGF-1-TASK-DETAIL.md#cgf1-s0310--e2e-dsm-test-script-suite) — ✅ [CGF-1-BATCH-24](batches/CGF-1-BATCH-24-INSTRUCTIONS.md) Part A: `OrchestratorActionHandlers` (Sysop/AssertEntityCount/AddMovingTag handlers), `MovingEntitySystem`, 4 JSON scripts, `DsmE2eScriptTests` (4 facts), `HeadlessTestExecutor.AfterInitialize` hook, `SimHostApp.TestHook_AddSystem` — **review:** integration tests not default CI; handler fail-loud gaps → [CGF-1-BATCH-25](batches/CGF-1-BATCH-25-INSTRUCTIONS.md).

---

## Phase 4 — Generalization: FDP Toolkit Orchestration

**Goal:** Lift `IDsmHandler`, `DrillSlave`, `TransitionPlanner`, and all reference handler
implementations out of the Bagira application layer into `FDP.Toolkit.Orchestration`.
Any future FDP application can then participate in a 2PC distributed state machine by
wiring toolkit reference handlers with constructor injection — no Bagira infrastructure
copy-paste required.

**Design authority:** [CGF-1-GENERALIZATION.md](./CGF-1-GENERALIZATION.md)  
**Execution:** [CGF-1-BATCH-21](batches/CGF-1-BATCH-21-INSTRUCTIONS.md) Part B (done through G0403 + partial G0404) → [CGF-1-BATCH-22](batches/CGF-1-BATCH-22-INSTRUCTIONS.md) (G0404–G0406 complete).

**Progress:** 6 / 6 tasks done — **COMPLETE**.

- [x] **CGF1-G0401** FDP.Toolkit.Orchestration Core Contracts — COMPLETE (BATCH-21) [details](./CGF-1-TASK-DETAIL.md#cgf1-g0401--fdptoolkitorchestration-core-contracts)
- [x] **CGF1-G0402** Generic DrillSlave + DdsOrchestrationTransport — COMPLETE (BATCH-21) [details](./CGF-1-TASK-DETAIL.md#cgf1-g0402--generic-drillslave--ddsorchestrationtransport)
- [x] **CGF1-G0403** Generalize TransitionPlanner with ITransitionGraph — COMPLETE (BATCH-21) [details](./CGF-1-TASK-DETAIL.md#cgf1-g0403--generalize-transitionplanner-with-itransitiongraph)
- [x] **CGF1-G0404** Reference Scenario, Story, and Prefetch Handlers — COMPLETE (BATCH-22): `ReferenceScenarioLoadHandler`, `ReferenceEditLoadHandler`, `ReferenceStoryLoadHandler`; NodeBootstrapper + CgfApplication + IgApplication + IosSubsystem wired [details](./CGF-1-TASK-DETAIL.md#cgf1-g0404--reference-scenario-story-and-prefetch-handlers)
- [x] **CGF1-G0405** Reference DryRun, Checkpoint, and RecordReplay Handlers — COMPLETE (BATCH-22): `ReferenceDryRunHandler`, `ReferenceCheckpointHandler`, `ReferenceLiveLoadHandler`, `ReferenceReplayLoadHandler`; `IRecordReplayController` extended [details](./CGF-1-TASK-DETAIL.md#cgf1-g0405--reference-dryrun-checkpoint-and-recordreplay-handlers)
- [x] **CGF1-G0406** Final Wiring Cleanup and CI Validation — COMPLETE (BATCH-22): all app layers wired to toolkit DrillSlave + Reference* handlers; 14 old handler/DrillSlave files deleted; 13 test files updated; all 6 test projects green (521+ tests) [details](./CGF-1-TASK-DETAIL.md#cgf1-g0406--final-wiring-cleanup-and-ci-validation)

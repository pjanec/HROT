# CGF-1-BATCH-17: S0304 production replay wiring (debt) + CGF1-S0305 Live-from-Replay

**Batch number:** CGF-1-BATCH-17  
**Tasks:** **Part A — BATCH-16 review follow-ups (tech debt, fail-loud)** → **Part B — CGF1-S0305** (Live-from-Replay temporal interlock)  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 6–12 h Part A + 24–40 h Part B  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-16](../reviews/CGF-1-BATCH-16-REVIEW.md) — CONDITIONALLY APPROVED

---

## Sequencing note

Land **Part A** first: without **`ReplayLoadDsmHandler`** registered from **`SimHostApp`**, **S0305** integration tests and orchestrator behaviour cannot be validated end-to-end on the real bootstrap path.

---

## Architecture note — Brain (CGF), muscle (SimHost), orchestrator

**Recording and replay (S0304 / S0305)** must eventually cover **both** sides of the distributed topology so **brain** and **muscle** state stay coherent:

| Node | Role | Expectation for this batch and follow-ups |
|------|------|-------------------------------------------|
| **SimHost** | **Muscle** — full ECS / simulation | **`LiveLoadDsmHandler`**, **`ReplayLoadDsmHandler`**, **`EcsRecordReplayController`**, checkpoints, and scenario **entity** load/save for the SimHost-owned scenario files (as today). Part **A.1** fixes production registration here. |
| **CGF** | **Brain** — command / doctrine / future ECS | **Same DSM transitions** (`PrepareLive`, `PrepareReplay`, `FinalizeReplay`, live-from-replay branch, etc.) must be **handled on the CGF node** with behaviour appropriate to whatever ECS/kernel CGF hosts. Today **`CgfApplication`** wires **`ScenarioLoadDsmHandler`** (header peek) and **`DryRunDsmHandler`** but **does not** register live/replay recording handlers — **close that gap in parity** with SimHost: either install the **same handler stack** once CGF has a recordable kernel, or register **explicit fail-loud** stubs that reject unsupported ops until the kernel exists (no silent “success” that skips brain-side persistence). **Do not** leave replay/recording as SimHost-only while CGF participates in the same drill. |
| **Orchestrator** | Cluster control, **no** full scenario DOM | **Do not** move bulk scenario load/save or entity graphs onto the orchestrator. Keep orchestrator participation aligned with [**CGF-1-DESIGN.md** §5.7](../CGF-1-DESIGN.md#57-stage-37--application-layer-scenario-saveload-wiring): **`GlobalContextDsmHandler`** (and similar) for **global** artefacts only — e.g. simulation epoch / wall alignment, **`ScenarioTime`**, weather/scene identifiers, manifest coordination — while **subsystem JSON files** and **scenario payload work** stay on **subsystem nodes**. **CGF** should own **brain-side** scenario files and the bulk of **non-global** scenario semantics; **SimHost** owns **muscle / simulation entity** files for its **`SubsystemType`**. If code today still centralises some JSON on SimHost, do **not** relocate it to the orchestrator to “simplify” — keep the split **CGF vs SimHost by file/header**, orchestrator **global-only**. |

**Acceptance guidance:** Part A/B work should **state in the report** how CGF and SimHost each participate in the same **`NodeOpCommand`** sequences for recording/replay; if CGF remains pre-kernel, the report must list **which ops are explicitly unsupported** on CGF and how ACK/NAK behaves.

---

## Onboarding

1. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.4–5.5 and **§5.7** (scenario split: orchestrator global vs subsystem files)  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0304 (residual) / §CGF1-S0305  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-16-REVIEW.md](../reviews/CGF-1-BATCH-16-REVIEW.md) — gaps (wiring, interface, fail-soft)  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-17** (CGF-1 section)  
5. **Architecture note** (above) — **CGF + SimHost** recording/replay parity; scenario ownership vs orchestrator globals

**Report:** [CGF-1-BATCH-17-REPORT.md](../reports/CGF-1-BATCH-17-REPORT.md) — **Review:** [CGF-1-BATCH-17-REVIEW.md](../reviews/CGF-1-BATCH-17-REVIEW.md)

---

## Part A — Tech debt (BATCH-16 review + DEBT-TRACKER)

### A.1 — **Register `ReplayLoadDsmHandler` from production SimHost** (P2)

**Problem:** [`SimHostApp.OnLoad`](../../../Bagira.SimHost/SimHostApp.cs) calls [`NodeBootstrapper.BuildOrchestration`](../../../Bagira.SimHost/NodeBootstrapper.cs) **before** `SimHostModule` / `GhostCreationSystem` / group handles exist, and does **not** pass `simulationSystemGroup`, `networkLifecycleSystemGroup`, `ghostCreationSystem`. [`BuildOrchestration`](../../../Bagira.SimHost/NodeBootstrapper.cs) only registers [`ReplayLoadDsmHandler`](../../../Bagira.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs) when all three are non-null.

**Acceptable approaches (pick one, document in XML):**

- **Reorder / two-phase bootstrap:** build kernel + groups first, then call a second registration pass that adds replay/live handlers with resolved references; **or**
- **Lazy registration:** after `SimHostModule` construction, register `ReplayLoadDsmHandler` on the same `DsmHandlerRegistry` / orchestration surface used in tests.

**Requirements:**

- Production **`SimHostApp`** path must handle **`PrepareReplay` / `FinalizeReplay`** (and any **`PrepareLive`** from replay per S0305 prep) the same way tests do — **no test-only wiring**.
- Add a **focused test** that builds the **minimal real `SimHostApp` / bootstrap slice** (or extracts a testable helper) and asserts **`ReplayLoadDsmHandler`** is registered when replay is enabled — **not** only manual handler construction in [`ReplayLoadDsmHandlerTests`](../../../Bagira.SimHost.Tests/ReplayLoadDsmHandlerTests.cs).

### A.2 — **Unify `IRecordReplayController` and `EcsRecordReplayController`** (P2)

- Either **`EcsRecordReplayController : IRecordReplayController`** with **one** `FinalizeRecordingAsync` contract (add optional `maxNetworkId` to the **interface** with default, or split methods — but **no** orphaned interface), **or** remove the unused interface if product standard is concrete type only (justify in TASK-DETAIL).
- Update call sites and XML so **DrillSlave** / handlers do not rely on divergent signatures.

### A.3 — **Fail-loud `ParseDrillId` in `LiveLoadDsmHandler`** (P3)

Replace **`catch` → `Guid.NewGuid()`** with **`throw`** (e.g. `InvalidOperationException` with payload context) or explicit error path that **does not** start recording under a random id. Align with project fail-fast policy ([DEBT-TRACKER](../../DEBT-TRACKER.md) row).

### A.4 — **`FinalizeRecordingAsync` no-op policy** (P3)

When **`_activeRecordingModule == null`**, either **log `Warn`** with clear reason or **throw** if double-finalize / ordering violation is invalid — document chosen policy in class XML.

### A.5 — **Dry-run snapshot test vs §S0309 TASK-DETAIL** (P3)

[`LoadingDryRun_SnapshotCapturesLiveState`](../../../Bagira.SimHost.Tests/DryRunDsmHandlerTests.cs): either extend to **four** entities + **`EntityCount == 4`** as in **TASK-DETAIL**, or **narrow TASK-DETAIL** success text to match the intentional minimal test — **spec and test must match**.

### A.6 — **Documentation hygiene** (P3)

- Refresh **`EcsRecordReplayController`** class / member XML (remove stale **S0202** “always returns false” narrative where inaccurate).
- Optional: §S0304 **`RecordingConfiguration`** location — add **TASK-DETAIL** footnote if it remains in **`FDP.Toolkit.Replay`** (layering is fine if documented).

### A.7 — **`CheckpointIOWorkerTests` stability** (P3, opportunistic)

If BATCH-16 report noted flakes: deterministic waits, `[Collection]`, or reduce parallel contention — only if reproduces in CI.

### A.8 — **DEBT-TRACKER**

Close **Part A** rows when merged (Status ✅).

---

## Part B — CGF1-S0305: Live-from-Replay temporal interlock

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0305](../CGF-1-TASK-DETAIL.md#cgf1-s0305--live-from-replay-temporal-interlock)  
**Design:** [CGF-1-DESIGN.md §5.5](../CGF-1-DESIGN.md#55-stage-35--live-from-replay-temporal-interlock)

Implement per task detail:

- **`DrillMaster`**: freeze time scale **0.0** before **`PrepareLive`** from **`RunningReplay`**; branched **`DrillId`**; restore scale after ACK.
- **`ReplayLoadDsmHandler`**: **`TeardownReplayAsync`**, uninstall replay without mutating **`EntityRepository`**, **`PrepareRecordingAsync`**, re-enable groups, clear **`BypassLifecycle`**.
- **`ReplayMasterModule`** on orchestrator wrapping **`MasterTimeController`**.
- All **success-condition tests** listed in §CGF1-S0305 (unit + integration as specified).

**Dependency:** Part A **A.1** must be complete enough that **SimHost** exercises **`ReplayLoadDsmHandler`** in integration scenarios; if Part B is too large, ship **A.1 + orchestrator freeze + handler branch logic + unit tests** in BATCH-17 and defer **`FullBranchPipelineTests`** to BATCH-18 **only** with explicit tracker note.

---

## Success criteria

- [x] Part A: `ReplayLoadDsmHandler` registered on real SimHost path; interface alignment; fail-loud parse; finalize policy + XML; dry-run test/TASK-DETAIL aligned; DEBT rows closed; **CGF vs SimHost recording/replay participation** documented (and CGF wired or explicit fail-loud stubs per **Architecture note** above).  
- [x] Part B: CGF1-S0305 artefacts + tests per task detail (or explicit split note + tracker); orchestrator scope remains **global context / time freeze** — **not** full scenario DOM.  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** updates **S0305** and Phase 3 progress.  
- [x] Report filed.  
- **Lead follow-up:** [CGF-1-BATCH-17-REVIEW.md](../reviews/CGF-1-BATCH-17-REVIEW.md) — **`PrepareLive` dispatch order** and related gaps → [CGF-1-BATCH-18](CGF-1-BATCH-18-INSTRUCTIONS.md).

---

## Reference

- [CGF-1-BATCH-16 review — gaps](../reviews/CGF-1-BATCH-16-REVIEW.md#additional-gaps-p2--p3)

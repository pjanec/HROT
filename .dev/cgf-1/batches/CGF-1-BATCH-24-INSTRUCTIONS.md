# CGF-1-BATCH-24: E2E DSM scripts (S0310) + Runner multi-subsystem `nodeId` correctness

**Batch number:** CGF-1-BATCH-24  
**Tasks:** **Part A — CGF1-S0310** (E2E DSM test script suite) → **Part B — Bagira.Runner orchestration node identity** (`-m all` / combined modes)  
**Phase:** Phase 3 closure (S0310) + control-plane **correctness** for aggregated Runner  
**Estimated effort:** 24–48 h Part A (per TASK-DETAIL) + 4–12 h Part B  
**Priority:** HIGH (S0310 — last open Phase 3 item); **HIGH** Part B — roster / `NodeOp` routing **breaks silently** on ID collision  
**Dependencies:** [CGF-1-BATCH-23 review](../reviews/CGF-1-BATCH-23-REVIEW.md) — APPROVED; [CGF-1-BATCH-23 report](../reports/CGF-1-BATCH-23-REPORT.md) — S0310 explicitly deferred

---

## Sequencing note

1. Land **Part B early** if S0310 slips — Part B is small and **unblocks trustworthy `-m all` DSM demos** after BATCH-23 handler wiring.  
2. **Part A** executes **CGF1-S0310** per [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §**CGF1-S0310**.  
3. Optional **Part C** (P3): IG handler-registration test harness called out in BATCH-23 report — only if capacity remains.

---

## Onboarding

1. [CGF-1-BATCH-23 review](../reviews/CGF-1-BATCH-23-REVIEW.md)  
2. [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) — §**CGF1-S0310**  
3. [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) — E2E / DSM expectations  
4. `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` — `ResolveNodeId`  
5. `Bagira.Runner/Program.cs` — `RunnerOptions.NodeId`  
6. `Bagira.SimHost/SimHostApp.cs`, `Bagira.IG/IgApplication.cs`, `Bagira.Runner/Services/IosSubsystem.cs` — behaviour when `SubsystemConfig.NodeId == 0`

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-24-REPORT.md` (when complete)  
**Review:** `.dev/cgf-1/reviews/CGF-1-BATCH-24-REVIEW.md`

---

## Part A — CGF1-S0310 (E2E DSM test script suite)

Per **TASK-DETAIL** §S0310: deliver the **scripted end-to-end** DSM validation suite (scenario load, transitions, story ops, replay/live branches as scoped there).

**Tests:** Per TASK-DETAIL success criteria; wire into CI as specified.

**Tracker:** Mark **CGF1-S0310** complete in [CGF-1-TASK-TRACKER.md](../CGF-1-TASK-TRACKER.md) when merged.

---

## Part B — Bagira.Runner: distinct `nodeId` per hosted subsystem

### Problem

`Bagira.Runner` can host **Orchestrator, SimHost, IG, IOS** (and **CGF** in comma-separated mode) in **one process**. Each participates in the orchestration plane (`DrillSlave`, heartbeats, 2PC). **Duplicate `nodeId` values** cause ambiguous roster entries, wrong `NodeOp` fan-out targets, and hard-to-debug DSM stalls.

### What to verify / implement

1. **`--node-id` ≠ 0 (explicit base)**  
   - Today `SubsystemOrchestrator.ResolveNodeId` applies offsets: SimHost **+0**, IG **+100**, IOS **+200**, default bucket **+300**.  
   - **Gap:** Any two subsystems that share the **default** `_ => 300` branch (e.g. **Orchestrator** + **CGF** if both enabled) **collide**. Add **explicit cases** per `ISubsystem.Name` for every Runner-hosted subsystem (**`Orchestrator`**, **`CGF`**, **`CI`** if ever combined) so offsets are **pairwise unique** for all supported mode strings.

2. **`--node-id` = 0 (legacy / default)**  
   - `ResolveNodeId` returns **0**; each subsystem applies its own fallbacks.  
   - **Known risk:** **SimHost** uses **`SimHostNetworkConstants.LocalNodeId` (1)** and **IG** uses **`IgNetworkConstants.LocalNodeId` (1)** for **`DrillSlave`** when override is zero — **both register as node 1** in `-m all`. **IOS** uses a different fallback (**500**).  
   - **Fix direction (pick one, document in the report):** e.g. align IG orchestration id with **`IgNetworkConstants.InstanceId` (300)** / `_effectiveInstanceId` for `DdsOrchestrationTransport` + `DrillSlave`, **or** teach `ResolveNodeId` to synthesize a non-zero base when **multiple** orchestration-capable subsystems are active. Preserve **standalone** `-m simhost` / `-m ig` behaviour unless product agrees to change defaults.

3. **Orchestrator node identity**  
   - Confirm whether **`OrchestratorSubsystem`** must consume **`SubsystemConfig.NodeId`** for any DDS/orchestration identity and that it **does not** implicitly share another node’s id.

### Deliverables

- Code + **tests** (unit tests on `ResolveNodeId` mapping; optional smoke asserting distinct ids in a multi-subsystem host).  
- Short **§Node ID map** in the batch report (default path + `--node-id N` path).

### Acceptance criteria

- [ ] `Bagira.Runner -m all` with **default** CLI: **no two subsystems** share the same orchestration `nodeId` (verify logs, `DrillMaster` roster, or test hooks).  
- [ ] **Explicit** `--node-id`: all subsystems in **every supported combined mode** remain distinct (**including** `orchestrator,cgf` if that mode is valid).  
- [ ] No regression for **single-subsystem** standalone modes.

---

## Part C — Optional (P3)

- **IG handler registration tests** — BATCH-23 report: `TestHook_DrillSlave` null in headless without DDS; add harness **or** DEBT row with lead sign-off.

---

## Success criteria

- [ ] **S0310** complete per TASK-DETAIL **or** explicitly re-deferred in tracker with lead note.  
- [ ] **Part B** complete — acceptance criteria above.  
- [ ] Build clean; tests green.  
- [ ] Report filed; DEBT / TASK-TRACKER updated.

---

## Reference

- [CGF-1-BATCH-23 instructions](./CGF-1-BATCH-23-INSTRUCTIONS.md)  
- [CGF-1-BATCH-23 review](../reviews/CGF-1-BATCH-23-REVIEW.md)  
- [RunMode](../../../Bagira.Runner/Configuration/RunMode.cs) — `All` composition (CGF not in `All` today; document if Part B touches this)

# CGF-1-BATCH-14: Prefetch hardening (BATCH-13 debt) + CGF1-S0303 (checkpointing)

**Batch number:** CGF-1-BATCH-14  
**Tasks:** **Part A — CGF-1-BATCH-13 review follow-ups (tech debt, fail-loud)** → **CGF1-S0303** (3-step binary checkpointing)  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 18–28 hours Part A + 28–40 hours S0303 (order: **complete Part A first** so load/save races do not compound checkpoint work)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-13](../reviews/CGF-1-BATCH-13-REVIEW.md) — APPROVED with P2 follow-ups

---

## Sequencing note

**Part A** must close **prefetch barrier** and **gateway / handler fail-loud** gaps before or in parallel with only **non-overlapping** S0303 files. If time-boxed, deliver **A.1–A.2** (orchestrator + gateway) **before** deep **`CheckpointIOWorker`** work.

---

## Onboarding

1. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.2 (portable load), §5.3 (checkpointing)  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0303  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-13-REVIEW.md](../reviews/CGF-1-BATCH-13-REVIEW.md) — **Critical gap: prefetch ordering**  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-14**

**Report:** [.dev/cgf-1/reports/CGF-1-BATCH-14-REPORT.md](../reports/CGF-1-BATCH-14-REPORT.md)  
**Review:** [.dev/cgf-1/reviews/CGF-1-BATCH-14-REVIEW.md](../reviews/CGF-1-BATCH-14-REVIEW.md)

---

## Part A — Tech debt (BATCH-13 review + DEBT-TRACKER)

### A.1 — **Prefetch barrier and transition gating** (P2)

**Files:** `Bagira.Orchestrator/DrillMaster.cs` (and any small helper), possibly `ProcessSysOpRequests` / transaction state.

**Problem:** `ExecutePrefetchScenario` starts `PrefetchScenarioAsync` fire-and-forget and immediately fans `PrefetchFiles` while `TransitionState` has already advanced `_currentDsmState` optimistically — **race** with SMB copy and with subsequent `LoadingEdit` / `LoadingLive` handlers.

**Required direction (pick a coherent design, document in report):**

- Either **await** prefetch completion on a path that can block without deadlocking the DDS tick (e.g. dedicated async pipeline, or defer optimistic DSM advance until prefetch success), **or**  
- Track a **prefetch latch** / pending op so **no** `TransitionStep` DDS fan-out runs until **`GatewayResult`** indicates success policy (e.g. `FailureCount == 0` and `SuccessCount > 0` when files existed on NAS), **and** fan **`PrefetchFiles` only after** push completes (or merge responsibilities so node ACK reflects real files).

- On **`PrefetchScenarioAsync` fault** or **policy violation**, surface **failure** to the client (`SysOpStatus.Failure` / reject request) and **do not** leave the cluster in a state that assumes files are present.

Add or extend **unit tests** (or integration harness) that would fail under the current fire-and-forget ordering.

### A.2 — **`StorageGatewayModule.PrefetchScenarioAsync` fail-loud** (P2)

**File:** `Bagira.Orchestrator/StorageGatewayModule.cs`

- Missing NAS **`sourceDir`** must **not** return silent `{0,0}` success semantics; **throw** or return a result that **`DrillMaster`** treats as hard failure.  
- Revisit **“silently skipped”** missing local targets: either **fail the operation**, **increment `FailureCount`** with clear logging, or document **strict** vs **lenient** mode with production default **strict**.

### A.3 — **`EditLoadDsmHandler` null repository** (P3)

**File:** `Bagira.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs`

When `_pendingDom != null` (load required) and both `repo` and `_world` are null → **`InvalidOperationException`** instead of Warn + no-op.

### A.4 — **Tests + TASK-DETAIL alignment** (P3)

- **`EditLoadDsmHandlerTests.LoadExistingScenario_SpawnsCorrectEntityCount`:** Assert **component values** (e.g. `EditLoadTestPos`) match the serialized scenario, per §CGF1-S0302 wording.  
- **`CGF-1-TASK-DETAIL.md` §CGF1-S0302:** Update **Work to do** / success bullets so the **canonical** format is **`ScenarioSerializer` DOM** (or explicitly add minimal-schema adapter + tests). Remove stale **`EntityCommandBuffer` / `BaseTerrain`** requirements if not implemented.

### A.5 — **DEBT-TRACKER**

Close **Part A** rows when merged; add new rows only for **new** gaps discovered.

---

## Part B — CGF1-S0303: 3-step binary checkpointing

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0303](../CGF-1-TASK-DETAIL.md#cgf1-s0303--3-step-binary-checkpointing)  
**Design:** [CGF-1-DESIGN.md §5.3](../CGF-1-DESIGN.md#53-stage-33--3-step-binary-checkpointing)

Implement per task detail:

1. **`CheckpointIOWorker`** in `Fdp.Kernel` (dedicated thread, LZ4, `{storageDir}/{requestId}_node_{nodeId}.fdp`, `CompletionResults`, `Enqueue` / `DrainAsync`).  
2. **`CheckpointDsmHandler`** in `Bagira.SimHost` (`TakeSnapshot`, `InProgress` ACK, `snap.SyncFrom`, deferred Success/Failure).  
3. **`DrillSlave.Tick()`** monitor for completion → DDS ACK.  
4. **`LiveLoadDsmHandler.PrepareAsync`**: `await CheckpointIOWorker.DrainAsync()` before `FinalizeRecordingAsync()` where specified.

**Success conditions:** all tests listed in §CGF1-S0303 (checkpoint handler overlap, snapshot diff, drain, live unload wait, etc.).

---

## Success criteria

- [x] Part A: prefetch **ordered** vs gateway; **fail-loud** on missing NAS / failed copy policy; **EditLoad** throws on null repo when load required; tests + TASK-DETAIL **S0302** text aligned; DEBT rows closed.  
- [x] Part B: CGF1-S0303 artefacts + tests green.  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** marks **S0303** `[x]` when Part B complete; **DEBT-TRACKER** updated.  
- [x] Report filed.

---

## Reference

- [CGF-1-BATCH-13 review — prefetch race](../reviews/CGF-1-BATCH-13-REVIEW.md#critical-gap-prefetch-ordering-and-failure-visibility)  
- **Prior checkpoint context:** none in repo — green-field to task detail.  
- **Next:** [CGF-1-BATCH-15](CGF-1-BATCH-15-INSTRUCTIONS.md) — checkpoint **production wiring** + **CGF1-S0309** Dry Run.

# CGF-1-BATCH-16: S0309 polish + checkpoint config (debt) + CGF1-S0304 (Dynamic Recording Modules)

**Batch number:** CGF-1-BATCH-16  
**Tasks:** **Part A — BATCH-15 review follow-ups (tech debt)** → **Part B — CGF1-S0304** (dynamic recording modules)  
**Phase:** Phase 3 — persistence  
**Estimated effort:** 4–8 h Part A + 32–48 h Part B (split Part B across two developer-days if needed)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-15](../reviews/CGF-1-BATCH-15-REVIEW.md) — APPROVED

---

## Sequencing note

Ship **Part A** first so **TASK-DETAIL**, **tests**, and **checkpoint path** do not drift while **S0304** adds `RecordingModule` / `LiveLoadDsmHandler` real implementation on top of the same bootstrap.

---

## Onboarding

1. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.4 (dynamic recording modules)  
2. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0304  
3. [.dev/cgf-1/reviews/CGF-1-BATCH-15-REVIEW.md](../reviews/CGF-1-BATCH-15-REVIEW.md) — gaps (spec/test/config)  
4. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-16**

**Report:** [CGF-1-BATCH-16-REPORT.md](../reports/CGF-1-BATCH-16-REPORT.md) — **Review:** [CGF-1-BATCH-16-REVIEW.md](../reviews/CGF-1-BATCH-16-REVIEW.md)

---

## Part A — Tech debt (BATCH-15 review + DEBT-TRACKER)

### A.1 — **§CGF1-S0309 TASK-DETAIL alignment** (P3)

- Update [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0309: **file path** → `Bagira.Common/Orchestration/Handlers/DryRunDsmHandler.cs` (or equivalent).  
- Replace **`SimPosition`** / rigid entity-count prose with **normative** behaviour + reference to **`DryRunTestPos`** (or whichever production component is used in tests).  
- Keep success-condition **intent** (snapshot, rewind, abort, no-op, null snap).

### A.2 — **Strengthen `UnloadingDryRun_RewindsLiveRepo`** (P3)

Implement the **missing** task-detail clause: after `LoadingDryRun`, **spawn an extra entity** in `liveRepo`, then `UnloadingDryRun` and assert **`EntityCount`** (and optionally component values) match **pre-spawn** snapshot — proves **`SyncFrom` removes entities created during dry run**, not only component rewind.

### A.3 — **Checkpoint storage path configurability** (P3)

- Derive checkpoint root from **`localTempRoot`** and/or **`NodeConfiguration`** (single source of truth with scenario staging), **or** document in **DESIGN** + XML why `C:\FDP_Temp\checkpoints` is fixed and when to change it.  
- Avoid silent divergence: scenario files under `localTempRoot` but checkpoints on a hard-coded sibling path without comment.

### A.4 — **§CGF1-S0303 success-condition wording** (P3, opportunistic)

Replace **`OnItemWritten`** reference with **`TakeCompletedResults`** / deferred DDS ACK wording to match [CheckpointIOWorker](../../../FDP/Kernel/Fdp.Kernel/Orchestration/CheckpointIOWorker.cs) + [CheckpointDsmHandler](../../../Bagira.SimHost/Modules/Orchestration/Handlers/CheckpointDsmHandler.cs).

### A.5 — **DEBT-TRACKER**

Close **Part A** rows when merged.

---

## Part B — CGF1-S0304: Dynamic Recording Modules

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0304](../CGF-1-TASK-DETAIL.md#cgf1-s0304--dynamic-recording-modules)  
**Design:** [CGF-1-DESIGN.md §5.4](../CGF-1-DESIGN.md#54-stage-34--dynamic-recording-modules)

Implement per task detail (high level):

- **`IRecordReplayController`**, **`RecordingConfiguration`** (Fdp.Kernel).  
- **`RecordingModule`**, **`ReplayModule`**, **`EcsRecordReplayController`** (`IDsmHandler` paths).  
- **`LiveLoadDsmHandler`** full prepare/finalize (replaces stub behaviour where specified).  
- **`ReplayLoadDsmHandler`**, **`NetworkLifecycleSystemGroup`**, **`GhostCreationSystem.BypassLifecycle`**, frame metadata / wall-clock seek extensions as listed.  
- All **success-condition tests** in §CGF1-S0304.

**Note:** Part B is large; if the batch must split, land **Fdp.Kernel interfaces + RecordingModule install/uninstall tests** before replay/live-load integration, but keep **one report** per merged batch.

---

## Success criteria

- [x] Part A: TASK-DETAIL S0309 accurate; dry-run test proves entity removal on rewind; checkpoint path documented or configurable; optional S0303 wording; DEBT rows closed.  
- [x] Part B: CGF1-S0304 artefacts + tests per task detail (production **`ReplayLoadDsmHandler`** wiring deferred — [BATCH-16 review](../reviews/CGF-1-BATCH-16-REVIEW.md)).  
- [x] Solution build clean.  
- [x] **CGF-1-TASK-TRACKER** marks **S0304** `[x]` when Part B complete (with BATCH-17 follow-up for SimHost replay registration).  
- [x] Report filed.

---

## Reference

- [CGF-1-BATCH-15 review — gaps](../reviews/CGF-1-BATCH-15-REVIEW.md#gaps-p3--fail-loud--spec--tests)

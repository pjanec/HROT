# CGF-1-BATCH-06: Orchestration correctness debt + CGF1-S0203

**Batch number:** CGF-1-BATCH-06  
**Tasks:** **Part A — BATCH-05 review follow-ups & open DEBT** → **CGF1-S0203** (time strategy proxying)  
**Phase:** Phase 2 — State & Time  
**Estimated effort:** 18–24 hours (~4–6 h Part A + ~14–18 h S0203)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-05](../reviews/CGF-1-BATCH-05-REVIEW.md) — APPROVED  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §4.2 (heartbeat / DSM visibility), §4.3  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0203  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-05-REVIEW.md](../reviews/CGF-1-BATCH-05-REVIEW.md) — Issues 1–3  
5. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-06**  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-06-REPORT.md`  

---

## Mandatory workflow

Complete **Part A** (small, correctness-first) **before** deep **S0203** work. Full **`dotnet test IOS-IG-SimHost.sln`** green before report.

---

## Part A — Tech debt & BATCH-05 follow-ups (first)

### A.1 — **`DrillSlave` heartbeat must reflect local DSM** (P2 — BATCH-05 review)

**File:** `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`  

- **`PublishHeartbeat()`** must set **`NodeHeartbeat.LocalDsmState`** from **`_localDsmState`** (after **`CommitState`** updates), not a hardcoded **`Standby`**.  
- Add or extend a **unit test** (no DDS) that: enqueue **`CommitState`** with a non-Standby payload → **`Tick()`** → next heartbeat payload would carry the updated state **or** expose a test seam to assert the value written (prefer asserting observable behavior via a test double writer if the project already has one; otherwise minimal internal test hook consistent with **`EnqueueCommandForTest`**).  

### A.2 — **DEBT-TRACKER hygiene**

- Roll forward any **CGF** rows that targeted BATCH-05 but were **out of scope** (e.g. per-node **`NodeOpCommand`** topic isolation) with an explicit **Target Fix** (Phase 2+ batch or **Opportunistic**).  
- Refresh the **BATCH-02** row on **`PrepareAsync` / `Commit`** fire-and-forget: **S0202** delivered wiring only — retarget to **real 2PC** milestone (**CGF1-S0304** or later) so the description is not misleading.

### A.3 — **Optional quick wins** (if time remains before S0203)

- Rename **`CommitState_RaisesEsmStateChangedEvent`** → **`CommitState_RaisesDsmStateChangedEvent`** in **`DrillSlaveHandlerTests`**.  
- Add **`PlanTrajectory_WhitespaceOnlyPayload_Throws`** if you want explicit coverage (behavior may already be covered by **`IsNullOrWhiteSpace`**).

---

## Part B — CGF1-S0203: Time strategy proxying

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0203](../CGF-1-TASK-DETAIL.md#cgf1-s0203--time-strategy-proxying)  
**Design:** [CGF-1-DESIGN.md §4.3](../CGF-1-DESIGN.md#43-stage-23--time-strategy-proxying)

Verify and extend **`FDP.Toolkit.Time`** per task detail: **`ITimeController`**, **`SwitchableTimeController.SwitchTo`** + **`SeedState`**, **`MasterTimeController` / `SlaveTimeController`**, **`GlobalTime.TotalWallTicks`**, and **all** success-condition tests in **`FDP.Toolkit.Time.Tests`**.

---

## Success criteria

- [ ] Part A: **`LocalDsmState`** on SimHost heartbeats matches **`_localDsmState`**; test added; DEBT rows updated.  
- [ ] Part B: CGF1-S0203 success conditions met.  
- [ ] Solution build clean; tests green.  
- [ ] DEBT-TRACKER updated.  
- [ ] Report filed (`.dev/cgf-1/reports/CGF-1-BATCH-06-REPORT.md`).  

---

## Reference

- [CGF-1-BATCH-05 review Issues](../reviews/CGF-1-BATCH-05-REVIEW.md#issue-1-nodeheartbeatlocaldsmstate-stuck-at-standby-p2)  

**Next preview:** **CGF-1-BATCH-07** — **CGF1-S0204** (future barrier) after S0203 CI green.

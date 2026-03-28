# CGF-1-BATCH-07: Wall-tick continuity + orchestration test debt + CGF1-S0204

**Batch number:** CGF-1-BATCH-07  
**Tasks:** **Part A — Tech debt (S0204 prerequisites + open CGF rows)** → **CGF1-S0204** (future barrier)  
**Phase:** Phase 2 — State & Time  
**Estimated effort:** 22–30 hours (~4–8 h Part A + ~18–22 h S0204 core)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-06](../reviews/CGF-1-BATCH-06-REVIEW.md) — APPROVED  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §4.4  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0204  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-06-REVIEW.md](../reviews/CGF-1-BATCH-06-REVIEW.md) — Issues 1–2  
5. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows **Target Fix = CGF-1-BATCH-07**  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-07-REPORT.md`  

---

## Mandatory workflow

Complete **Part A** (small, correctness-first items) **before** implementing the full **DistributedTimeCoordinator** / **SlaveTimeModeListener** barrier flow, so **`TotalWallTicks`** semantics are consistent across **Continuous → Deterministic** swaps. Full **`dotnet test IOS-IG-SimHost.sln`** green before report (use serial/known-good policy if parallel DDS flakes appear).

---

## Part A — Tech debt first

### A.1 — **`SteppedMasterController` wall-clock continuity on `SwitchTo`** (P3 — BATCH-06 review Issue 1)

**Problem:** After **`SwitchableTimeController.SwitchTo`** from **`MasterTimeController`** to **`SteppedMasterController`**, **`GlobalTime.TotalWallTicks`** in stepped mode is derived from **`_unscaledTotalTime`** only, not from **`GlobalTime.TotalWallTicks`** supplied in **`SeedState`**. That can diverge from the master’s Stopwatch-based accumulator and will confuse **CGF1-S0204** barrier math.

**Work:**

- Extend **`SteppedMasterController`** so **`SeedState(GlobalTime)`** preserves wall-clock continuity **explicitly** (e.g. store **`TotalWallTicks`** from **`state`** and/or derive **`_unscaledTotalTime`** consistently with the continuous master contract — pick one coherent rule and document it in XML).  
- Add a **unit test** in **`FDP.Toolkit.Time.Tests`**: after **`SwitchTo`** from a **seeded** **`MasterTimeController`** to **`SteppedMasterController`**, assert **`stepped.GetCurrentState().TotalWallTicks`** equals **`master.GetCurrentState().TotalWallTicks`** (or the documented mapping), not only **`TotalTime`**.

### A.2 — **Slave `SeedState` test hardening** (P3 — BATCH-06 review Issue 2)

**File:** `FDP.Toolkit.Time.Tests/SlaveTimeControllerTests.cs`  

- Extend **`SeedState_BypassesJitterFilter`** (or add a sibling test) so **`SeedState`** is called with **non-zero** **`TotalWallTicks`** and the next **`Update()`** reflects that baseline in **`GlobalTime.TotalWallTicks`** (within tick delta tolerance).

### A.3 — **`SurvivingNodes` / per-node `NodeOpCommand`** (P3 — DEBT-TRACKER)

**Row:** ejected-node read isolation; keyed per-node **`NodeOpCommand`** topics.

**Minimum for this batch (choose one, document in report):**

- **Spike + design note:** ADR or **CGF-1-TASK-DETAIL** subsection on keyed topic shape, writer fan-out cost, and test strategy; **or**  
- **Narrow code change:** if a low-risk incremental step exists (e.g. helper to target writes by **`NodeId`** without full schema churn), implement it **with tests**; **or**  
- **Explicit deferral:** move **Target Fix** to **CGF-1-BATCH-08** with justification **only** if A.1–A.2 + S0204 would exceed capacity — **do not** silently drop the row.

### A.4 — **DEBT-TRACKER**

Close or roll rows touched by A.1–A.3; add none without a **Target Fix** batch.

---

## Part B — CGF1-S0204: Future barrier implementation

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0204](../CGF-1-TASK-DETAIL.md#cgf1-s0204--future-barrier-implementation)  
**Design:** [CGF-1-DESIGN.md §4.4](../CGF-1-DESIGN.md#44-stage-24--future-barrier-implementation)

Implement **`SwitchTimeModeEvent`**, network translators, **`DistributedTimeCoordinator`** and **`SlaveTimeModeListener`** barrier gating on **`GlobalTime.TotalWallTicks`**, and **all** **`FutureBarrierTests`** success conditions listed in the task detail.

---

## Success criteria

- [ ] Part A: stepped **`TotalWallTicks`** continuity + slave wall-tick test; **SurvivingNodes** / isolation row **addressed** per A.3.  
- [ ] Part B: CGF1-S0204 success conditions met.  
- [ ] Solution build clean; tests green.  
- [ ] DEBT-TRACKER updated.  
- [ ] Report filed.  

---

## Reference

- [CGF-1-BATCH-06 review Issues](../reviews/CGF-1-BATCH-06-REVIEW.md#issue-1-steppedmastercontroller-vs-totalwallticks-after-switchto-p3)  

**Next preview:** **CGF-1-BATCH-08** — **CGF1-S0205** (deterministic CI hookup) and/or remaining Part A isolation work, after S0204 CI green.

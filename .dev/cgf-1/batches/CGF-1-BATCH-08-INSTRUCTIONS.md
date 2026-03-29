# CGF-1-BATCH-08: DDS time-mode wiring + orchestration isolation debt + CGF1-S0205

**Batch number:** CGF-1-BATCH-08  
**Tasks:** **Part A — Tech debt (distributed time + orchestration)** → **CGF1-S0205** (deterministic CI hookup)  
**Phase:** Phase 2 — State & Time  
**Estimated effort:** 22–28 hours (~6–10 h Part A + ~16–18 h S0205)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-07](../reviews/CGF-1-BATCH-07-REVIEW.md) — APPROVED  

---

## Onboarding

1. [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §4.4–4.5  
3. [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0205  
4. [.dev/cgf-1/reviews/CGF-1-BATCH-07-REVIEW.md](../reviews/CGF-1-BATCH-07-REVIEW.md) — Issue 1 (DDS wiring)  
5. [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — **SurvivingNodes** row (Target **CGF-1-BATCH-08**)  

**Report:** `.dev/cgf-1/reports/CGF-1-BATCH-08-REPORT.md`  

---

## Mandatory workflow

Complete **Part A.1** (DDS path for **`SwitchTimeModeEvent`**) **before** **S0205** work that assumes slaves receive the master’s barrier over the network. Full **`dotnet test IOS-IG-SimHost.sln`** green before report.

---

## Part A — Tech debt first

### A.1 — Wire **`TimeNetworkModule.RegisterTranslators`** (P2 — BATCH-07 review Issue 1)

**Problem:** `FDP.Toolkit.Time/TimeNetworkModule.cs` returns a **`BlitEventTranslator<SwitchTimeModeEvent>`**, but **no** production composition root calls it — mode-switch events never cross DDS.

**Work:**

- Identify every node host that runs **`ModuleHostKernel`** + Cyclone (**`Bagira.Runner`**, **`SimHostApp`**, **`IgApplication`**, CGF subsystem, etc.) and **must** participate in distributed time mode.  
- For each: create **`TimeNetworkModule.RegisterTranslators(participant)`**, retain the translator for the app lifetime, and invoke **`ScanAndPublish`** / **`PollIngress`** (or the project’s equivalent egress/ingress hooks) **every frame** alongside existing Cyclone translators — mirror **`NetworkDemoApp`** patterns.  
- Add a **minimal regression test** where possible (e.g. in-process participant + translator round-trip, or integration test already using DDS isolation), or document **manual verification** steps in the report if test cost is prohibitive.

### A.2 — **`SurvivingNodes` / per-node `NodeOpCommand`** (P3 — deferred from BATCH-07)

**Minimum (pick one, document in report):**

- **ADR or design subsection** in **CGF-1-TASK-DETAIL** / **CGF-1-DESIGN**: keyed topic naming, writer fan-out, orchestrator **`DrillMaster`** changes, and test strategy for “ejected node receives no command”; **or**  
- **Incremental implementation** with tests if a low-risk slice is identified; **or**  
- **Justified deferral** to **CGF-1-BATCH-09** only if **A.1 + S0205** exhaust capacity — **update DEBT-TRACKER** with reason (same rule as BATCH-07).

### A.3 — Optional: **`SwitchTimeModeEvent` wire / IDL** (P3)

If time allows: spike **codegen-friendly** wire shape (e.g. blittable DTO + enum as **`int`**) so **`[DdsTopic]`** can be re-enabled without breaking Cyclone IDL — or document “blit-only” as the supported path.

### A.4 — **DEBT-TRACKER**

Close **A.1** when wired; update **SurvivingNodes** row per A.2.

---

## Part B — CGF1-S0205: Deterministic CI hookup

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0205](../CGF-1-TASK-DETAIL.md#cgf1-s0205--deterministic-ci-hookup)  
**Design:** [CGF-1-DESIGN.md §4.5](../CGF-1-DESIGN.md#45-stage-25--deterministic-ci-hookup)

Implement **`MinimalCIScenario`**, **`DrillMaster`** payload hint for deterministic **`LoadingLive`**, coordinator / barrier integration with **`DistributedTimeCoordinator`**, slave **`SteppedSlaveController`** path, Runner **`--mode ci`**, and **all** success-condition tests listed in the task detail.

---

## Success criteria

- [x] Part A.1: **`SwitchTimeModeEvent`** egress/ingress wired on every relevant node; verified in tests or documented procedure.  
- [x] Part A.2: **SurvivingNodes** debt **addressed** per options above.  
- [x] Part B: CGF1-S0205 success conditions met (see [BATCH-08 review](../reviews/CGF-1-BATCH-08-REVIEW.md) for partial vs task-detail gaps).  
- [x] Solution build clean; tests green.  
- [x] DEBT-TRACKER updated.  
- [x] Report filed.  

---

## Reference

- [CGF-1-BATCH-07 review Issue 1](../reviews/CGF-1-BATCH-07-REVIEW.md#issue-1-timenetworkmodule-not-wired-in-production-apps-p2)  

**Next preview:** **CGF-1-BATCH-09** — Phase 2 closure (**CGF1-S0205** polish if split), Phase 3 persistence kickoff, or remaining orchestration isolation implementation.

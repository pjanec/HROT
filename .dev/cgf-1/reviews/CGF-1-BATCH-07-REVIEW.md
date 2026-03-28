# CGF-1-BATCH-07 Review

**Batch:** CGF-1-BATCH-07  
**Reviewer:** Development Lead  
**Date:** 2026-03-29  
**Status:** APPROVED (with integration follow-ups for BATCH-08)

**Report:** [CGF-1-BATCH-07-REPORT.md](../reports/CGF-1-BATCH-07-REPORT.md) — verified against **source**.

---

## Summary

**Part A** matches the report: **`SteppedMasterController`** keeps an explicit **`_totalWallTicks`**, seeds it from **`SeedState`**, advances it in **`Step`**, and returns it from **`GetCurrentTime`** — removing the **`_unscaledTotalTime * Frequency`** mismatch that would have broken barrier math. **`SwitchTo_TransfersWallTicksToNewController`** asserts equality with a seeded master. **`SeedState_NonZeroWallTicks_ArePreservedAfterUpdate`** covers non-zero wall-tick baselines on the slave.

**A.3 (`SurvivingNodes` / keyed `NodeOpCommand`):** **Explicit deferral to BATCH-08** with justification in the report — allowed under BATCH-07 instructions; **DEBT-TRACKER** already points to **CGF-1-BATCH-08** with a note.

**Part B (CGF1-S0204)** is **substantively delivered**: **`SwitchTimeModeEvent`** uses **`BarrierWallTicks`** and **`FixedDelta`**; **`DistributedTimeCoordinator`** publishes a future barrier and swaps at **`TotalWallTicks >= barrier`**; **`SlaveTimeModeListener`** gates on **`kernel.CurrentTime.TotalWallTicks`** with a sensible **“already past barrier”** immediate-swap path. **`FutureBarrierTests`** implements all **five** task-detail success patterns (stub controller for deterministic ticks, reflection shape check, coordinator publish-in-future using a real **`MasterTimeController`**). **`TimeNetworkModule.RegisterTranslators`** provides a **composition-root hook** for **`BlitEventTranslator<SwitchTimeModeEvent>`**; the report’s **IDL/codegen limitation** for **`TimeMode`** is documented.

**Tests run (review):** **`FDP.Toolkit.Time.Tests`** — **64** passed, **1** skipped (pre-existing).

**Hygiene (lead):** Removed a **duplicate nested `<summary>`** on **`SwitchTimeModeEvent`** in **`TimeMessages.cs`** (invalid XML doc).

---

## Tasks vs instructions

| Item | Verdict |
|------|---------|
| **A.1** Stepped **`TotalWallTicks`** | **Done** — field, seed, step accumulation, test. |
| **A.2** Slave non-zero wall ticks | **Done** — dedicated test with bounded drift tolerance. |
| **A.3** SurvivingNodes | **Done per spec** — justified deferral + debt target **BATCH-08**. |
| **A.4** DEBT | **Done** — BATCH-06 rows closed; SurvivingNodes rolled. |
| **B** CGF1-S0204 | **Done** for **library + in-process bus** behavior and tests. |

---

## Gaps (not blocking approval)

### Issue 1: **`TimeNetworkModule` not wired in production apps** (P2)

**Finding:** **`TimeNetworkModule.RegisterTranslators`** is **not** referenced from **`Bagira.Runner`**, **`SimHostApp`**, or other hosts (search shows **no** call sites outside **`FDP.Toolkit.Time`**). Task detail allows an “equivalent composition root,” but **no root currently registers** the DDS path — distributed mode switches still rely on **local `FdpEventBus` only** until **`ScanAndPublish` / `PollIngress`** are hooked into the network loop. **Target:** **CGF-1-BATCH-08** (ahead of **S0205** CI that assumes cross-node events).

### Issue 2: **DDS schema / IDL for `SwitchTimeModeEvent`** (P3)

The report explains **omitting** `[DdsTopic]` because **codegen** fails on **`TimeMode`**. Acceptable short-term with **`BlitEventTranslator`** + topic name **`SwitchTimeModeEvent`**; track **IDL alignment** or **blittable wire DTO** if interoperability hardens.

### Issue 3: **`FutureBarrierTests` vs full integration**

Tests correctly avoid DDS flakes by using **`FdpEventBus`** and **`StubTimeController`**. **`BarrierWallTicks_IsSetToFuture`** uses **`Thread.Sleep`** — a bit coarse but stable enough for “strictly greater than” with **50 ms lookahead**.

---

## Test quality

| Area | Verdict |
|------|---------|
| **Stepped / switch** | **Strong** — wall ticks preserved across **`SwitchTo`**, not only **`TotalTime`**. |
| **Slave seed** | **Strong** — non-zero baseline + post-**Update** bounds. |
| **Future barrier** | **Strong** — before/at barrier, master/slave, struct reflection, future barrier publication. |

---

## Design alignment

- **§4.4** (barrier on **`GlobalTime.TotalWallTicks`**, not frame counters): **Aligned** — coordinator, listener, and event shape match; **`BarrierFrame`** removed per spec.
- **End-to-end DDS**: **Partial** until **Issue 1** is closed.

---

## Verdict

**APPROVED.** **CGF1-S0204** is met at the **toolkit + unit-test** level; schedule **DDS wiring** of **`SwitchTimeModeEvent`** and **SurvivingNodes** follow-up in **CGF-1-BATCH-08** before treating distributed time-mode switch as production-complete.

---

## Suggested commit message

```
feat(cgf-1): BATCH-07 stepped wall ticks, future barrier, TimeNetworkModule hook

- SteppedMasterController: explicit _totalWallTicks seed + Step accumulation.
- SwitchTimeModeEvent: BarrierWallTicks + FixedDelta; wall-tick barrier protocol.
- DistributedTimeCoordinator / SlaveTimeModeListener: barrier on TotalWallTicks.
- TimeConfig: LookaheadWallTicks; NetworkDemo + DistributedPauseTests updated.
- TimeNetworkModule: BlitEventTranslator registration helper for DDS.
- Tests: SwitchTo wall-tick transfer, slave non-zero SeedState, FutureBarrierTests (5).

Related: CGF1-S0204, CGF-1-DESIGN §4.4.
```

---

**Next batch:** [CGF-1-BATCH-08](../batches/CGF-1-BATCH-08-INSTRUCTIONS.md)

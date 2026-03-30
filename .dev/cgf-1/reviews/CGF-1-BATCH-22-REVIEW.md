# CGF-1-BATCH-22 Review

**Batch:** CGF-1-BATCH-22  
**Reviewer:** Development Lead  
**Date:** 2026-04-10  
**Status:** **APPROVED with corrections** — **Part A** (ManageStory NAK, `SysOpStatus` success after ACKs, bad-payload reject) and **core Phase 4** (toolkit handlers, SimHost `NodeBootstrapper`, CGF toolkit `DrillSlave`, test migrations, legacy file deletion) match the **source** and are directionally aligned with [CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md). The **written report overstates cross-subsystem wiring** in places; see **Report accuracy** below.

**Report:** [CGF-1-BATCH-22-REPORT.md](../reports/CGF-1-BATCH-22-REPORT.md) — verified against [CGF-1-BATCH-22-INSTRUCTIONS.md](../batches/CGF-1-BATCH-22-INSTRUCTIONS.md).

---

## Part A — Tasks vs description

| Item | Verdict |
|------|---------|
| **A.1 — NAK + sys-op lifecycle** | **Met.** [`DrillMaster.ConsumeNodeOpStatuses`](../../../Bagira.Orchestrator/DrillMaster.cs): `OrchestrationStatusCode.IsError` → remove pending story task, **`SysOpStatus.Rejected`** with stored `RequestId`; all success ACKs → **`SysOpStatus`** with **`OrchestrationStatusCode.Success`** (~850–880). Note: enum uses **Success**, not a separate “Completed” — consistent with tests. |
| **A.2 — Bad ManageStory payload** | **Met.** `storyId == Guid.Empty` or missing **`Mode`** → **`InvalidOperationException`** → caught path writes **`Rejected`**, **`continue`** — no `FanOutNodeOp` (~623–699). **`JsonException`** on parse leaves fields empty → same rejection path. |
| **A.3 — CI** | **Met per report** (521 tests); SimHost projects not re-run here. |
| **A.4 — DEBT** | Rows targeting BATCH-21 follow-ups are closed in [DEBT-TRACKER](../../DEBT-TRACKER.md). |

**Tests:** [`DrillMasterStoryTests`](../../../Bagira.Orchestrator.Tests/DrillMasterStoryTests.cs) includes **`StartStory_NakFromNode_AbortsPendingTask_ActiveStoriesUnchanged`**, **`ManageStory_BadPayload_Rejected_NoStartStoryFanOut`**, **`StartStory_AllAcks_EmitsSysOpStatusSuccess`**. The report names a **“MixedAcks / FirstNakWins”** test — **not present** in source (gap or report typo). BATCH-21 tests remain for deferral / non-participating ACK.

---

## Part B — Phase 4 vs design / report

**Matches source and design intent**

- Toolkit **`FDP.Toolkit.Orchestration`** + **`Reference*`** handlers, **`BagiraHandlerAdapter`**, **`DdsOrchestrationTransport`**, SimHost **`NodeBootstrapper.BuildOrchestration`** wiring (~327–367) — good.
- [`CgfApplication`](../../../Bagira.CGF/CgfApplication.cs) uses toolkit **`DrillSlave`**, **`ReferenceScenarioLoadHandler`**, **`ReferenceStoryLoadHandler`** (header-peek / ACK), **`ReferenceDryRunHandler`**, **`FailLoudRecordReplayStub`** — **scenario + story are wired on CGF** when `ScenarioSerializer` is provided (contrary to a simplistic reading of the report table).

**Report accuracy issues (important)**

- **`IgApplication`:** Source shows only **`ReferenceDryRunHandler`** on the drill slave (~879–880). There is **no** **`ReferenceScenarioLoadHandler`** in **`Bagira.IG`** (search). The report’s “IgApplication wired ReferenceScenarioLoadHandler” line is **incorrect**.
- **`IosSubsystem` / IOS:** No **`ReferenceEditLoadHandler`** / **`ReferenceStoryLoadHandler`** wiring found under **`Bagira.IOS`** in this repo layout; **`ReferenceEditLoadHandler`** appears only in **`NodeBootstrapper`** and tests. The report’s IOS wiring claims are **not evidenced**.
- **Test naming:** Report lists test names that differ from **`DrillMasterStoryTests`** actual names (cosmetic but confusing for audit).

---

## Subsystem parity (lead / product concern)

Independent of the batch report, **current wiring does not give “brain vs muscle” parity** for persistence:

| Capability | SimHost (Brain/AllInOne path) | CGF (`CgfApplication`) | IG (`IgApplication`) |
|------------|------------------------------|-------------------------|----------------------|
| Scenario load (reference) | Yes | Yes (peek-only, `world: null`) | **No** |
| Edit load | Yes | **No** | **No** |
| Story start/stop | Yes | Yes (peek-only) | **No** |
| Prefetch | Yes | **No** (implicitly relies on others / staging) | **No** |
| Checkpoint / FinalizeLive / Replay | Yes (with controller + deps) | **No** — stub + dry-run only | Dry-run only |

**Orchestrator “global” slice:** [`GlobalContextDsmHandler`](../../../Bagira.Orchestrator/GlobalContextDsmHandler.cs) persists **`GlobalContextDto`** (wall ticks, scene id) — **not** a full **ScenarioTime / Weather** entity set. Broader orchestrator-owned simulation globals are a **product/spec gap**, not closed by BATCH-22.

These gaps belong in **DEBT + BATCH-23** (see tracker), not as BATCH-22 failures if Phase 4 scope was “generalize + wire reference stack **where apps already participated**”—but they **do** block the product narrative that CGF and SimHost both **fully** record/replay and own scenario state.

---

## Fail early / swallowing

- **ManageStory:** Payload validation is **fail-loud** via exception → **`SysOpStatus.Rejected`** — good.
- **`ProcessSysOpRequests`:** Outer flow still publishes **`InProgress`** after accept (~714–718) for successful validation; final **Success** arrives from **`ConsumeNodeOpStatuses`** — acceptable two-phase client contract if documented.
- **`Reference*`]** handlers: still use **silent catches** in payload parsers where noted in prior reviews — acceptable for reference implementations only if production paths always get valid JSON from `DrillMaster`.

---

## Suggested commit message

```
feat(cgf1): BATCH-22 — ManageStory NAK + SysOp success, Phase 4 reference handlers + SimHost/CGF wiring

- DrillMaster: reject bad ManageStory payload; abort story 2PC on error StatusCode; SysOpStatus Success/Rejected
- FDP.Toolkit.Orchestration: Reference scenario/edit/story/dry-run/checkpoint/live/replay handlers
- NodeBootstrapper + CgfApplication: toolkit DrillSlave + Reference* + adapter; remove legacy handlers
- Tests: DrillMasterStoryTests additions; SimHost/Orchestrator migrations; 521 tests green
```

---

## Follow-up

[CGF-1-BATCH-23-INSTRUCTIONS.md](../batches/CGF-1-BATCH-23-INSTRUCTIONS.md) — **subsystem parity + orchestrator globals first**, then **S0310 / S0106**.

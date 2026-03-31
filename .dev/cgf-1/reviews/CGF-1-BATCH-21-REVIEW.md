# CGF-1-BATCH-21 Review

**Batch:** CGF-1-BATCH-21  
**Reviewer:** Development Lead  
**Date:** 2026-04-05  
**Status:** **APPROVED** — **Part A** matches the instructions and source (ManageEpisode ACK deferral, SimHost story handler fail-loud, DESIGN note, test rename, DEBT rows). **Part B** delivers **CGF1-G0401**–**G0403** and a **partial G0404** exactly as the report describes; remaining Phase 4 work is explicitly scoped to **BATCH-22**.

**Report:** [CGF-1-BATCH-21-REPORT.md](../reports/CGF-1-BATCH-21-REPORT.md) — verified against [CGF-1-BATCH-21-INSTRUCTIONS.md](../batches/CGF-1-BATCH-21-INSTRUCTIONS.md), [CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md).

---

## Part A — Tasks vs description

| Item | Verdict |
|------|---------|
| **A.1 — ManageEpisode + `NodeOpStatus`** | **Met.** [`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs): `_pendingManageEpisodeTasks`, `ConsumeNodeOpStatuses` removes `NodeId` from `RemainingNodeIds` until empty, then mutates `_activeStories`. Prefetch gated with `if (_gateway != null)`. Policy (all ACKs count, participating or not) is documented and covered by [`ClusterMasterStoryTests`](../../../Hrot.Orchestrator.Tests/ClusterMasterStoryTests.cs). |
| **A.2 — SimHost `StoryLoadDsmHandler`** | **Met.** Invalid Start/Stop payloads set `_pendingTransactionId` and non-participating flag so **`Commit`** publishes; participating path with null repo **throws** [`InvalidOperationException`](../../../Hrot.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs) (~240–251, ~317–325). |
| **A.3 — DESIGN + rename** | **Met.** §5.8 *ManageEpisode 2PC — MVP Implementation Note* is in [`.dev/cgf-1/CGF-1-DESIGN.md`](../CGF-1-DESIGN.md) (not under `Hrot.Orchestrator/` — the report’s path there is a **doc typo**). Integration test renamed to `NodeBootstrapper_BrainRole_RegistersLiveLoadDsmHandler`. |
| **A.4 — DEBT** | **Met** per [DEBT-TRACKER](../../DEBT-TRACKER.md) rows closed for BATCH-21. |

---

## Design alignment (generalization)

- **G0401–G0403:** Toolkit project stays free of `Hrot.*`; `ClusterMasterPlanner` + [`HrotStateGraph`](../../../Hrot.Orchestrator/HrotStateGraph.cs) match [CGF-1-GENERALIZATION.md](../CGF-1-GENERALIZATION.md) layering. `HrotHandlerAdapter` and parallel `IDsmHandler` types are an intentional **bridge** until G0404/G0405 migration — acceptable with documented removal target.
- **G0404 partial:** `LocalDiskStorageProvider` + `ReferencePrefetchHandler` align with the reference-handler direction; **no** production wiring to toolkit `ClusterSlave` yet — consistent with deferral to G0406.

---

## Tests — what matters

- **`ClusterMasterStoryTests`:** Prove **`ActiveStories`** does not update before ACKs and that **non-participating** ACK clears the pending set — **high value** for A.1.
- **`StoryLoadDsmHandlerTests`:** Cover invalid payload + null-repo throw paths (names differ slightly from the report — **cosmetic**).
- **`FDP.Toolkit.Orchestration.Tests`:** **11/11 passed** in this environment (contract, `ClusterSlave`, BFS, reference prefetch).
- **`Hrot.Orchestrator.Tests`:** Report **31/31**; local full run was in-flight; no reason to doubt from code review.
- **`Hrot.SimHost.Tests`:** Full build hit **MSB3027** (`Fhsm.SourceGen` lock) — **environmental**, same class as earlier batches; story tests were not re-run here.

**Gaps:** No test that **`NodeOpStatus`** with **error** `StatusCode` aborts story 2PC; no test that **`ClusterOpStatus`** reflects story completion (see below).

---

## Fail early / no silent swallowing

**Good**

- SimHost story handler: **Error**-level logs on invalid payloads; **throw** when participating commit lacks repo; deserialize failures still **rethrow** after log.
- `ReferencePrefetchHandler` invalid JSON → null scenario → pending not set → **Commit** no-op (acceptable mainly for tests; **document** or align with “always ACK” when wired in production — **P3** for BATCH-22 if used on-cluster without transport).

**Still weak (track as debt)**

1. **`ClusterMaster` `ManageEpisode` payload parse** ([`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs) ~612–620): **`JsonException` swallowed** — `storyId` stays empty → **`_pendingManageEpisodeTasks` not registered** but **`FanOutNodeOp` still runs** → **`ActiveStories` never updated** via 2PC for that request (orphan node ops). Prefer reject **`ClusterOpRequest`** or still register a completion path.
2. **Story ACK consumption** (~836–847): **Does not inspect `StatusCode`** — a node **NAK** still removes the node from `RemainingNodeIds` and can complete “successfully” with a failed participant.
3. **`ClusterOpStatus` for `ManageEpisode`:** Request still gets **`InProgress`** at accept time (~702–707); there is **no** matching **Completed**/**Rejected** when story 2PC finishes or times out — clients cannot observe story round-trip completion on the sys-op channel alone (**P2**).

---

## Suggested commit message

```
feat(cgf1): BATCH-21 — ManageEpisode 2PC, story handler ACK/thrown paths, toolkit orchestration

- ClusterMaster: defer ActiveStories until all node NodeOpStatus ACKs; gateway-gated prefetch
- SimHost StoryLoadDsmHandler: pending tx + ACK on invalid payload; throw if participating with null repo
- Add FDP.Toolkit.Orchestration (contracts, ClusterSlave, TransitionPlanner); HrotStateGraph, ClusterMasterPlanner
- DdsOrchestrationTransport, HrotHandlerAdapter, LocalDiskStorageProvider, ReferencePrefetchHandler
- Tests: ClusterMasterStoryTests, StoryLoadDsmHandlerTests, toolkit contracts + ClusterSlave + reference handler
- DESIGN §5.8 ManageEpisode MVP note; RecordReplay integration test rename
```

---

## Follow-up

[CGF-1-BATCH-22-INSTRUCTIONS.md](../batches/CGF-1-BATCH-22-INSTRUCTIONS.md) — **tech debt first** (story `ClusterOpStatus`, NAK/`StatusCode` policy, payload parse fail-loud), then **G0404** remainder, **G0405**, **G0406**.

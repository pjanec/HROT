# CGF-1-BATCH-13 Review

**Batch:** CGF-1-BATCH-13  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** **APPROVED with P2 follow-ups** — portable load path and wiring land; **prefetch execution is not yet safe for production ordering guarantees**

**Report:** [CGF-1-BATCH-13-REPORT.md](../reports/CGF-1-BATCH-13-REPORT.md) — verified against **source**, [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0302, [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.2, and [CGF-1-BATCH-13-INSTRUCTIONS.md](../batches/CGF-1-BATCH-13-INSTRUCTIONS.md).

---

## Summary

**Part A** delivers the BATCH-12 execution gaps in **shape**: `DrillMaster` calls `ExecutePrefetchScenario`, SimHost registers `PrefetchFilesDsmHandler` with `NodeOpStatus` plumbing, `GlobalContextDsmHandler.CommitLoad` throws on missing `Orchestrator.json` / null DTO when `ScenarioId` is present, `SimHostApp` builds `ScenarioSerializer` and passes it into `BuildOrchestration`, `ConsumeNodeOpStatuses` tracks `FailureCount` for malformed `SerializeLocal` manifests, and `TransitionPlanner` reads `ScenarioId` without a redundant swallowed `JsonException` (second parse is after validated JSON).

**Part B** delivers **`EditLoadDsmHandler`** for `LoadingEdit`, **`TransitionPlannerTests.PlanWithScenarioId_InjectsStorageGatewayStep`**, and three **`EditLoadDsmHandlerTests`** aligned with the **ScenarioSerializer** DOM (not the task-detail minimal `{ SchemaVersion, Entities[] }` sample).

**Tests run (review):** `Bagira.Orchestrator.Tests` — **23 / 23** passed; `Bagira.SimHost.Tests` — **367 / 367** passed; `Bagira.Orchestrator.Integration.Tests` — **3 / 3** passed.

---

## Verdict vs instructions

| Area | Assessment |
|------|------------|
| **A.1 Prefetch in DrillMaster** | **Partial.** Gateway + NAS missing → `InvalidOperationException` (good). **`PrefetchScenarioAsync` is fire-and-forget**; faults are **logged only**. **`PrefetchFiles` is sent immediately**, not after copy success — see **Critical gap** below. |
| **A.2 PrefetchFiles on nodes** | **Partial vs wording.** Handler **creates staging dir** and **ACKs Success**; it does **not** apply a file manifest or verify that NAS push completed. Actual bytes are copied by the gateway in A.1 — but that copy is not synchronized with the ACK path. |
| **A.3 GlobalContext** | **Met** for mandatory load: XML defers **`SeedState`** to host; missing file / null DTO → throw when `ScenarioId` present. Empty `ScenarioId` still **no-ops** (documented blank-world). |
| **A.4 SimHost serializer** | **Met.** [`SimHostApp.cs`](../../../Bagira.SimHost/SimHostApp.cs) builds `ScenarioSerializerBuilder("Bagira.SimHost").Build()` after component registration and passes it to `BuildOrchestration`. |
| **A.5 ConsumeNodeOpStatuses / planner** | **Mostly met.** Malformed `ResultJson` increments **`FailureCount`** and logs **Error** when the round completes (save still completes; not a hard **SysOpStatus** failure — acceptable P3 polish). **`TransitionPlanner`** no longer uses a silent inner `catch` for `ScenarioId`. |
| **Part B S0302** | **Functionally met** for DOM + tests; **spec drift** from older task-detail bullets (`EntityCommandBuffer`, `BaseTerrain`, minimal JSON) — should be reconciled in TASK-DETAIL or a small adapter (debt). |

---

## Critical gap: prefetch ordering and failure visibility

[`DrillMaster.ExecutePrefetchScenario`](../../../Bagira.Orchestrator/DrillMaster.cs) starts `_gateway.PrefetchScenarioAsync(...)` with **`ContinueWith`** (log only on fault / counts on success) and **in the same synchronous call** fans out **`PrefetchFiles`** with a **new** transaction id. The **`TransitionState`** request has **already** advanced **`_currentDsmState`** optimistically in `ProcessSysOpRequests`.

Effects:

1. **Race:** Nodes may run **`PrefetchFiles` / subsequent `PrepareState(LoadingEdit)`** before SMB push finishes, so **`EditLoadDsmHandler`** or **`GlobalContextDsmHandler`** can hit **missing files** intermittently despite “prefetch first” in the plan.
2. **Silent orchestrator-side failure:** `PrefetchScenarioAsync` exceptions and **zero-success** gateway results do **not** reject the accepted transition or publish **`SysOpStatus.Failure`** for that request.
3. **[`StorageGatewayModule.PrefetchScenarioAsync`](../../../Bagira.Orchestrator/StorageGatewayModule.cs):** If the NAS source directory is **absent**, the method returns **`SuccessCount = 0`, `FailureCount = 0`** — not a hard failure. Documented **silent skip** of missing per-target files further weakens fail-loud semantics.

This is the main reason the batch is **not** a full “prefetch is real” closure relative to design §5.2’s intent (**files ready before load**).

---

## Additional fail-soft / test gaps

- **[`EditLoadDsmHandler.Commit`](../../../Bagira.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs):** If a deserialize is pending but **`repo`** and **`_world`** are both null, the handler **logs Warn and returns** — should **throw** when load was required (align with “fail early and aloud”).
- **`PrepareAsync`:** Per-file read errors during DOM scan are **Warn + continue** (similar to **`ScenarioLoadDsmHandler`**); acceptable for “try next file” but worth documenting as **lenient** vs **strict** policy.
- **Payload parsers** (`ParseTargetState`, etc.): empty catch → defaults — only safe if caller guarantees JSON shape; risk if malformed **`LoadingEdit`** payloads are ever sent as objects without **`TargetState`**.
- **`LoadExistingScenario_SpawnsCorrectEntityCount`:** Asserts **`EntityCount == 3`** only. §CGF1-S0302 success text asks that **position (component) values match JSON** — test should assert component data (or TASK-DETAIL should narrow the condition to count-only if DOM is canonical).

---

## Design alignment

- **§5.2 / portable loading:** Edit-load + planner prefetch step + serializer DOM are **directionally aligned**. **Execution semantics** (barrier after gateway, surfacing copy failure to the control plane) **lag** the design.
- **DEBT-TRACKER rows** that pointed at BATCH-13 for prefetch / `PrefetchFiles` / `GlobalContext` / `SimHost` wiring are **substantively addressed in code**, but the **new** sequencing and gateway-failure items above **re-open** correctness debt → **CGF-1-BATCH-14** (see tracker).

---

## Suggested commit message

```
feat(cgf-1): execute prefetch step, EditLoad handler, and SimHost scenario serializer

- DrillMaster: run PrefetchScenario gateway push + fan-out PrefetchFiles; distribution targets
- SimHost: PrefetchFilesDsmHandler, NodeOpStatus writer, serializer from SimHostApp
- GlobalContextDsmHandler: fail-loud CommitLoad when ScenarioId set; XML for SeedState
- EditLoadDsmHandler for LoadingEdit; TransitionPlanner test for prefetch-before-LoadingEdit
- SerializeLocal: FailureCount on malformed ResultJson (logged on round completion)

Follow-up (BATCH-14): await/barrier prefetch before DSM advance; fail on zero gateway success;
tighten EditLoad null-repo and S0302 test assertions / task-detail alignment.
```

---

## Next batch

**[CGF-1-BATCH-14](batches/CGF-1-BATCH-14-INSTRUCTIONS.md)** — **tech debt (prefetch barrier + gateway fail-loud + tests)** first, then **CGF1-S0303** (checkpointing).

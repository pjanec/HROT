# CGF-1-BATCH-12 Review

**Batch:** CGF-1-BATCH-12  
**Reviewer:** Development Lead  
**Date:** 2026-04-07  
**Status:** **APPROVED** (with **P2 follow-ups** — orchestrator execution path, **GlobalContext** contract, **SimHost** wiring)

**Report:** [CGF-1-BATCH-12-REPORT.md](../reports/CGF-1-BATCH-12-REPORT.md) — verified against **source**, [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §CGF1-S0307, and [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §5.7.

---

## Summary

**Part A** is **substantively delivered** as described in the report:

- **`ScenarioSerializer`**: fail-fast **`InvalidOperationException`** paths for missing **`Entities`**, invalid GUID keys, unknown component keys, **`asStory`** without valid **`Guid`**, unsupported translator payload types; **`SaveResolver`** / **`LoadResolver`** throw on bad references ([`ScenarioSerializer.cs`](../../../FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs)).
- **`FdpAutoSerializer_NoReflectionOnHotPath`**: strengthened with **`ReflectionCallCounter`** (per report).
- **`FanOutSerializeLocal`**: **`ClusterMaster.ProcessClusterOpRequests`** handles **`ClusterOpType.SaveScenario`** ([`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs)); **`PullToNasAsync`** continuation calls **`WriteScenarioManifestAsync`** ([`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs) lines 626–631).
- **`StoryTag`**: canonical **`Fdp.Kernel.StoryTag`** (`struct`, **`Guid`**, component id **84**) ([`StoryTag.cs`](../../../FDP/Kernel/Fdp.Kernel/StoryTag.cs)) — single type for scenario + replay; **`Deserialize(..., Guid? storyId)`**; **`IEntityScenarioTranslator.GetOutputDomKeys()`** for N:M keys.

**Part B** delivers **most** S0307 **artifacts**:

- **`GlobalContextDsmHandler`**, **`ScenarioLoadDsmHandler`** (SimHost + CGF), **`TransitionPlanner`** **`PrefetchScenario`** prep step, **`StorageGatewayModule.PrefetchScenarioAsync`** / **`WriteScenarioManifestAsync`**, DDS **`ClusterOpType.PrefetchScenario`** / **`NodeOpType.PrefetchFiles`**, **`ScenarioSerializer.IsMatchingSubsystem`**, **`Hrot.Orchestrator.Integration.Tests`** (3 tests).

**Tests run (review):** `Hrot.Orchestrator.Integration.Tests` — **3 / 3** passed.

---

## Gaps vs task detail §CGF1-S0307

1. **`MasterTimeController.SeedState`**: Class XML on **`GlobalContextDsmHandler`** still claims load path seeds time ([`GlobalContextDsmHandler.cs`](../../../Hrot.Orchestrator/GlobalContextDsmHandler.cs) lines 28–32), but **`CommitLoad`** only sets **`LoadedStartWallTicks`** / **`LoadedSceneId`** and publishes DDS — **no `SeedState` call** (report defers this). **Align XML with behaviour** or **implement wiring** when orchestrator owns a time controller.

2. **`CommitLoad` soft failure**: Missing **`Orchestrator.json`**, null DTO → **log + return** (lines 199–220) — **silent** relative to “fail loud” for inconsistent save/load.

3. **`SimHostApp.OnLoad`**: **No** **`ScenarioSerializer`** wiring found — **`NodeBootstrapper.BuildOrchestration`** accepts an optional serializer, but **production SimHost** path does not pass it, so **`ScenarioLoadDsmHandler`** may **never register** in real apps. **S0307 item 4** expects **`SimHostApp`** wiring.

4. **`ClusterMaster` / prefetch**: **`TransitionPlanner`** enqueues **`OperationStep(ClusterOpType.PrefetchScenario, …)`** ([`TransitionPlanner.cs`](../../../Hrot.Orchestrator/TransitionPlanner.cs) lines 199–215), but **`ProcessClusterOpRequests`** does **not** execute that step (report notes deferral). **`PrefetchScenarioAsync`** exists but has **no orchestrator call site**.

5. **Nodes / `PrefetchFiles`**: **`NodeOpType.PrefetchFiles`** is in the schema; **no** node **`ClusterSlave`** handler implementation in this batch (report).

6. **`ConsumeNodeOpStatuses`**: **`JsonException`** on **`ResultJson`** → **warn + skip** manifest for that node ([`ClusterMaster.cs`](../../../Hrot.Orchestrator/ClusterMaster.cs) lines 604–609) — can **mask** partial cluster failure during save.

7. **`TransitionPlanner`**: Inner **`catch (JsonException) { /* ignore */ }`** when probing **`ScenarioId`** ([`TransitionPlanner.cs`](../../../Hrot.Orchestrator/TransitionPlanner.cs) line 211) — **swallows** malformed JSON in that branch.

---

## Overlap with **CGF1-S0302** (Portable Scenario Loading)

| S0302 item | BATCH-12 status |
|------------|-----------------|
| **`TransitionPlanner`**: prefetch before **`LoadingEdit` when ScenarioId`** | **Partial:** step is **prepended** for **any** transition payload with **`ScenarioId`**, not only **`LoadingEdit`**; step is **not executed** by **`ClusterMaster`**. |
| **`EditLoadDsmHandler`** (`PrepareAsync` / `Commit`, **`IsNewScenario`**, **`LoadingEdit`**) | **Not implemented** — no **`EditLoadDsmHandler`** in repo. |
| Minimal **dummy** JSON schema (`Entities` array / **`Type`**) | **Not implemented** — integration tests use **toolkit** DOM (**`Header` + `Entities` map**). |
| S0302 **unit tests** (`EditLoadDsmHandlerTests`, **`TransitionPlannerTests.PlanWithScenarioId_…`**) | **Not present** in **`Hrot.Orchestrator.Tests`** (no **`Prefetch`** / **`ScenarioId`** planner test found). |

**Conclusion:** BATCH-12 **does not** satisfy **CGF1-S0302**. **S0302** remains the **primary focus** for the next batch, reusing toolkit format where possible and closing the **prefetch execution** + **planner test** gaps.

---

## Verdict on tests

- **Scenario / Replay / Orchestrator unit tests** (per report **54**) were not all re-run in review; **integration** tests **do** validate **handler + serializer** round-trip and **subsystem filter** — they **matter**.
- They **do not** cover **live `ClusterMaster` prefetch**, **`SaveScenario` end-to-end over DDS**, or **`SimHostApp`** wiring.

---

## Suggested commit message

```
feat(cgf-1): BATCH-12 scenario save/load wiring + serializer fail-fast

- ScenarioSerializer: strict deserialize; Fdp.Kernel.StoryTag (Guid); GetOutputDomKeys
- Orchestrator: GlobalContextDsmHandler, SaveScenario→SerializeLocal, manifest write
- TransitionPlanner: PrefetchScenario step; StorageGateway prefetch + manifest helpers
- SimHost/CGF: ScenarioLoadDsmHandler; integration tests (3)

Refs: CGF-1-BATCH-12, CGF1-S0307
```

---

## Next batch

See **[CGF-1-BATCH-13](../batches/CGF-1-BATCH-13-INSTRUCTIONS.md)** — tech debt (**prefetch execution**, **GlobalContext** / **SeedState**, **SimHost** wiring, fail-loud tweaks) then **CGF1-S0302**.

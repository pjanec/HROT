# CGF-1-BATCH-09 Review

**Batch:** CGF-1-BATCH-09  
**Reviewer:** Development Lead  
**Date:** 2026-05-12  
**Status:** APPROVED (with **P2 follow-ups** in BATCH-10)

**Report:** [CGF-1-BATCH-09-REPORT.md](../reports/CGF-1-BATCH-09-REPORT.md) — verified against **source**.

---

## Summary

**Part A** is **largely delivered** as described:

- **`OrchestratorSubsystem`** owns a minimal **`ModuleHostKernel`**, **`DistributedTimeCoordinator`**, and **`SwitchTimeModeDescriptorTranslator`**; **`Update`** advances the time kernel, swaps the bus, reads **`ClusterMaster.PendingTimeMode`**, and on edge-detect of **`"Deterministic"`** calls **`SwitchToDeterministic`** with **current roster keys** (the report’s “empty `HashSet`” risk is **outdated** — the constructor’s empty set is superseded by **`slaveIds` from `NodeRoster`** at runtime).
- **`OrchestratorTimeModeTests`** (2) exercise **`SwitchTimeModeEvent`** on **`TimeBusForTest`** with JSON vs integer payloads; domain **15** + collection isolation matches the stated intent.
- **`TimeNetworkModule.RegisterTranslators`** is **`[Obsolete]`** with a clear message.
- **`TestDomainAllocator`** starts at **15** so **`Next()` → 16+**, avoiding clash with orchestrator domain **15**; **`Hrot.Orchestrator.Tests/xunit.runner.json`** serialises the assembly.
- **`MinimalCIScenario.FinalEntitySnapshot`** + **`DeterministicRun_IsReproducible`** assert **`Index`/`Generation`** across two runs — stronger than exit-code-only; **subprocess `dotnet run`** remains **deferred** (allowed by BATCH-09 instructions with documentation).

**Part B (keyed `NodeOpCommand`)** is **substantively delivered**:

- **`[DdsKey] TargetNodeId`** on **`NodeOpCommand`** with XML rationale.
- **`ClusterMaster`**: **`FanOutNodeOp`**, per-node writer cache, **`EjectNode`** disposes the ejected writer, **`Dispose`** clears writers.
- **`SurvivingNodes_CommandedToStandby_AfterEjection`**: three participants, filters for **400** vs **1**, eject **SimHost (1)**, assert **CGF** receives **Abort** + **PrepareState**, **SimHost** reader **empty** — matches keyed-delivery intent (report §B.4 prose was garbled; **code + test are coherent**).

**Tests run (review):** **`Hrot.Orchestrator.Tests`** — **18** passed.

---

## Gaps (schedule BATCH-10)

### Issue 1: **`Hrot.IG` `ClusterSlave` has no `SetFilter`** (P2)

**`Hrot.SimHost`**, **`Hrot.ExCon`**, and **`Hrot.CGF`** **`ClusterSlave`** call **`_commandReader.SetFilter(cmd => cmd.TargetNodeId == _nodeId)`**.  
**`Hrot.IG.Modules.Orchestration.ClusterSlave`** does **not** — it only constructs **`DdsReader<NodeOpCommand>`** without a filter. With per-key writes, IG can still **receive** samples for other instances depending on DDS behaviour; at minimum it **violates parity** with other nodes and the BATCH-09 “all slaves filter” story.

### Issue 2: **`CgfApplication` bus split** (P2)

**`CgfApplication`** creates **`_eventBus`** + **`SwitchTimeModeDescriptorTranslator`**, but **`ClusterSlave`** is constructed **without** that bus (`new ClusterSlave(_participant, nodeId, name)` only). Ingressed **`SwitchTimeModeEvent`** lands on a bus that **no `SlaveTimeModeListener`** consumes — acceptable for a **minimal CGF shell**, but it is **not** end-to-end **S0205** “slave switches to **`SteppedSlaveController`**”. Document or unify when CGF gets a **kernel**.

### Issue 3: **S0205 / task-detail wording**

- **`dotnet run …` + exit 0 in 30 s** — still **not** an automated test (report acknowledges).
- **“Entity positions at tick 600”** — tests assert **entity identity** (`Index`/`Generation`), not **transforms** (fair for entities with **no** spatial components).

### Issue 4: **`TimeNetworkModule` class XML** (P3)

The **type-level** summary still describes **`BlitEventTranslator<SwitchTimeModeEvent>`** as the primary path; **`RegisterTranslators`** is obsolete. Refresh class docs to point at **`CreateDescriptorTranslator`** / **`SwitchTimeModeWireDto`**.

---

## Tasks vs instructions

| Item | Verdict |
|------|---------|
| **A.1** Coordinator + **`PendingTimeMode`** | **Done** (+ roster slave IDs). |
| **A.2** CGF translator | **Done** on **`CgfApplication`** (not full **`CgfSubsystem`** stack — acceptable for thin host). |
| **A.3** Stricter CI + subprocess alt | **Partially done** — snapshot + defer subprocess. |
| **A.4** Obsolete + domain flake | **Done**. |
| **A.5** DEBT | **Done** per tracker. |
| **B** Keyed **`NodeOpCommand`** | **Done** for **SimHost / IOS / CGF** slaves + orchestrator; **IG filter missing** (Issue 1). |

---

## Test quality

| Area | Verdict |
|------|---------|
| **SurvivingNodes** | **Strong** — dual readers + post-ejection isolation. |
| **OrchestratorTimeMode** | **Strong** — deterministic + negative case. |
| **MinimalCI reproducibility** | **Good** for ECS identity; not full “positions” per task text. |

---

## Verdict

**APPROVED.** Part A/B core work matches the batch intent; **close IG `SetFilter` and CGF time-bus integration** in **CGF-1-BATCH-10** before treating keyed **`NodeOpCommand`** as **complete across all orchestrated apps**.

---

## Suggested commit message

```
feat(cgf-1): BATCH-09 keyed NodeOpCommand, coordinator + CI closure

- NodeOpCommand: [DdsKey] TargetNodeId; ClusterMaster FanOutNodeOp + writer cache.
- ClusterSlave (SimHost/IOS/CGF): SetFilter by TargetNodeId; ejection disposes writer.
- OrchestratorSubsystem: time kernel + DistributedTimeCoordinator + PendingTimeMode edge.
- CgfApplication: SwitchTimeModeDescriptorTranslator in Tick; FDP.Toolkit.Time ref.
- SurvivingNodes test: multi-participant isolation; TestDomainAllocator + xunit serial.
- TimeNetworkModule.RegisterTranslators [Obsolete]; MinimalCIScenario FinalEntitySnapshot.

Related: CGF1-S0105 ADR, CGF1-S0205, BATCH-08 debt rows.
```

---

**Next batch:** [CGF-1-BATCH-10](../batches/CGF-1-BATCH-10-INSTRUCTIONS.md)

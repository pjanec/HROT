# CGF-1-BATCH-08 Review

**Batch:** CGF-1-BATCH-08  
**Reviewer:** Development Lead  
**Date:** 2026-03-30  
**Status:** APPROVED (S0205 **partial** — schedule BATCH-09 for normative closure)

**Report:** [CGF-1-BATCH-08-REPORT.md](../reports/CGF-1-BATCH-08-REPORT.md) — verified against **source**.

---

## Summary

**Part A.1** is **delivered well**: **`SwitchTimeModeWireDto`** (`[DdsTopic("SwitchTimeModeEvent")]`, **`TargetModeInt`**) avoids Cyclone **`TimeMode`** IDL failure; **`SwitchTimeModeDescriptorTranslator`** bridges bus ↔ DDS; **`SimHostApp`** adds **`TimeNetworkModule.CreateDescriptorTranslator`** to the **egress** list (and the combined list passed to **`CycloneNetworkModule`**); **`IgApplication`** appends the same to **`customTranslators`**. **`SwitchTimeModeTranslatorTests`** (10) exercise bus-level behavior including invalid-sample handling and enum round-trip.

**NetworkDemo:** Correctly **not** wired (report rationale matches code — avoids dual path vs **`TimeSyncSystem`**). The **DEBT-TRACKER** row for BATCH-07 P2 incorrectly listed **NetworkDemoApp** as wired; **corrected** in this review cycle.

**Part A.2:** **ADR** for keyed **`NodeOpCommand`** is present under **CGF-1-TASK-DETAIL §CGF1-S0105** (five-point design + **BATCH-09** deferral). **SurvivingNodes** debt row targets **CGF-1-BATCH-09**.

**Part B (CGF1-S0205)** — **partial vs task-detail prose**:

| Delivered | Gap |
|-----------|-----|
| **`RunMode.CI`**, **`--scenario`**, **`CiSubsystem`**, **`Program`** CI branch (deterministic **60 Hz**). | No use of **`DistributedTimeCoordinator`** / **`SlaveTimeModeListener`** / **`SteppedSlaveController`** in **Runner** or **OrchestratorSubsystem**. |
| **`MinimalCIScenario`**: two entities, 600 ticks, **`ScenarioFailureException`** on death. | Task asks entities **in CGF subsystem**; implementation uses **bare** **`EntityRepository`** in **`ScenarioSubsystem`** only. |
| **`DrillMaster.PendingTimeMode`** from JSON object payload when trajectory includes **LoadingLive**; **`JsonValueKind.Object`** guard fixes legacy integer payload vs **`TryGetProperty`** throw (important fix). | **`PendingTimeMode` is never read** anywhere else in the solution — no **`SwitchToDeterministic`** call tied to orchestrator. |
| Three **xUnit** tests via **`ScenarioSubsystem`** harness. | Task success conditions: **`dotnet run … --mode ci --scenario MinimalCI_01`** exit **0** within **30 s** — **not** exercised as a **subprocess** test. **`DeterministicRun_IsReproducible`** should assert **bit-identical state** at tick 600; tests only assert **equal exit codes**. |

**Tests run (review):** **`Bagira.Runner.Tests`** — **115** passed.

---

## Tasks vs instructions

| Item | Verdict |
|------|---------|
| **A.1** DDS wiring | **Done** for **SimHost + IG**; **NetworkDemo** excluded by design; **Runner** / **Orchestrator** / **CGF** hosts without translator noted as **BATCH-09** debt. |
| **A.2** SurvivingNodes | **Done** (ADR + deferral). |
| **A.3** Wire DTO | **Done** (folded into A.1). |
| **B** S0205 | **Partial** — CI **scaffold** + **`PendingTimeMode` capture**; **not** full end-to-end deterministic cluster story from **CGF-1-TASK-DETAIL**. |

---

## Design alignment

- **§4.4 / §4.5:** Wall-tick barrier and **`SwitchTimeModeEvent`** semantics remain consistent; **wire DTO** is a reasonable interop pattern.
- **Production completeness:** Until **`PendingTimeMode`** drives a coordinator and **CGF** participates on DDS, the **“DrillMaster instructs coordinator before RunningLive”** story is **documentation-only**.

---

## Test quality

| Area | Verdict |
|------|---------|
| **SwitchTimeMode translator** | **Strong** for bus semantics; no **real participant** round-trip in tests (acceptable if heavy). |
| **Minimal CI** | **Good** for smoke (exit 0 / 1); **reproducibility** test is **weaker than spec**. |
| **DrillMaster JSON guard** | **Critical** fix — prevents silent **`AppendToHistory`** skip on integer payloads. |

---

## Verdict

**APPROVED** for **BATCH-08 scope** (DDS bridge + CI entry + orchestrator hint). Treat **CGF1-S0205** as **milestone-complete with explicit BATCH-09 follow-ups** (consume **`PendingTimeMode`**, CGF translator, stricter tests, optional subprocess CI).

---

## Suggested commit message

```
feat(cgf-1): BATCH-08 SwitchTimeMode DDS wire DTO, CI mode, PendingTimeMode

- SwitchTimeModeWireDto + SwitchTimeModeDescriptorTranslator; SimHost + IG wiring.
- RunMode.CI, --scenario, CiSubsystem, MinimalCIScenario; Runner Program CI branch.
- DrillMaster: PendingTimeMode from JSON when trajectory includes LoadingLive; object guard.
- CGF1-S0105 ADR: keyed NodeOpCommand → BATCH-09; SwitchTimeModeTranslatorTests.
- NetworkDemo: omit translator (TimeSync path); NetworkDemo.Tests xunit serial.

Related: CGF1-S0205 (partial), CGF1-S0204 wire-up, BATCH-07 debt closure.
```

---

**Next batch:** [CGF-1-BATCH-09](../batches/CGF-1-BATCH-09-INSTRUCTIONS.md)

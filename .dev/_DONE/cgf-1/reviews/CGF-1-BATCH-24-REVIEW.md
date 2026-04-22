# CGF-1-BATCH-24 Review

**Batch:** CGF-1-BATCH-24  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** **APPROVED with corrections (P1–P2 follow-ups required in BATCH-25)** — Substantive delivery on **Part B (node IDs)** and **Part A (S0310 scaffolding)**; **not** a clean bill of health for CI or TASK-DETAIL literal compliance.

**Report:** [CGF-1-BATCH-24-REPORT.md](../reports/CGF-1-BATCH-24-REPORT.md)  
**Instructions:** [CGF-1-BATCH-24-INSTRUCTIONS.md](../batches/CGF-1-BATCH-24-INSTRUCTIONS.md)

---

## Executive summary

| Area | Verdict |
|------|---------|
| **Part B — `ResolveNodeId` + IG ClusterSlave id** | **Met in source** — offsets for `Orchestrator` / `CGF` / `CI` / unknown are pairwise distinct; IG uses `_effectiveInstanceId` for orchestration when override is 0; `SubsystemOrchestratorTests` cover the regression. |
| **Part A — S0310** | **Partially met** — handlers, JSON scripts, executor hooks, and integration-test facts exist and align **mostly** with [CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) §S0310; several **spec / CI / fail-loud** gaps remain (below). |
| **CI / tests** | **`Hrot.ClusterRunner.Tests` is not green** — `ParseMode_ComboAllThree_EqualsAllFlag` fails against current `RunMode.All` (`Orchestrator \| SimHost \| IG \| IOS`). This is a **real regression** for default `dotnet test` on that project, not an acceptable “pre-existing” excuse while `All` is documented to include Orchestrator. |
| **CLI vs code — CGF** | **Critical discovery:** [`HrotRunnerConfiguration.ParseModeString`](../../../Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs) has **no** `cgf` token (neither standalone nor comma-separated). [`Program.cs`](../../../Hrot.ClusterRunner/Program.cs) can add `CgfSubsystem` when `RunMode.CGF` is set, but **the Runner CLI cannot select CGF today**. Part B tests that use mock subsystems named `"CGF"` are valid; **claims about `orchestrator,cgf` from the CLI are not backed by configuration parsing**. |

---

## Part B — verification against source

**`SubsystemOrchestrator.ResolveNodeId`** — Matches report: explicit offsets `0,100,200,300,400,500`, unknown `+600`; base `0` preserves legacy per-subsystem fallbacks.

**`IgApplication` ClusterSlave node** — Uses `_effectiveInstanceId` (300 when override is 0), eliminating SimHost/IG both registering as `1` in `-m all`. Comment in source documents intent.

**Gaps / risks**

1. **CGF not launchable via CLI** (see above) — node-id offset for `"CGF"` is **unreachable** through `HrotRunnerConfiguration` until `cgf` is added to mode parsing.  
2. **Orchestrator + default `NodeId`** — Still `0` in config; orchestrator path does not use the same `SubsystemConfig.NodeId` story as slaves (acceptable if documented; report already notes N/A for orchestrator in default path).

---

## Part A — S0310 vs TASK-DETAIL and design

**Aligned**

- `SysopActionHandler` + `AssertEntityCountActionHandler` + `AddMovingTagActionHandler`; `MovingEntitySystem` as `ComponentSystem`; four JSON scripts under `TestScripts/`; `DsmE2eScriptTests` with separate DDS domain IDs; `AfterInitialize` on `HeadlessTestExecutor`; `HandleClusterOpRequest` / async enqueue pattern documented honestly on `ClusterMaster` (poll `ClusterOpStatus` separately — correct).

**Deviations / omissions**

1. **`MovingTestTag` placement** — TASK-DETAIL asks for the tag in the **same file** as `MovingEntitySystem`; implementation places it in [`OrchestratorActionHandlers.cs`](../../../Hrot.ClusterRunner/Testing/OrchestratorActionHandlers.cs). Pragmatic for compilation, but **normative doc and code diverge** — fix TASK-DETAIL or move the struct per lead policy.  
2. **`SysopActionHandler` exceptions** — TASK-DETAIL mentions `TestAssertionException`; code uses `InvalidOperationException` / `TimeoutException`. Acceptable if documented; optional alignment.  
3. **`TakeCheckpoint` vs TASK text “TakeSnapshot”** — Code uses `ClusterOpType.TakeCheckpoint` (value 4), consistent with [`OrchestrationMessages.cs`](../../../Hrot.NED/Orchestration/OrchestrationMessages.cs). TASK wording is stale; not a code bug.

---

## Fail-loud / silent paths (lead criterion)

**Problems**

1. **`AssertEntityCountActionHandler`** — If `_world == null`, logs a warning and returns a **successful** result with `entity_count: 0` instead of **throwing**. That **masks fixture bugs** and violates “fail early and aloud.”  
2. **`AddMovingTagActionHandler`** — If `_world == null` or entity not alive, **warn + return null** — steps can **silently no-op**; E2E scripts can “pass” without applying the tag.  
3. **`AssertionRule.Equals`** in [`TestScript.cs`](../../../FDP/Framework/FDP.Framework.Runner/Testing/TestScript.cs) — Name **hides** `object.Equals`; compiler **CS0108**. Rename (e.g. `Exactly`) or `new` keyword — hygiene / future-proofing.

**Good**

- `SysopActionHandler` throws on parse errors, missing `TargetWallTicks` for replay seek, DDS failure status, and timeout.  
- `ClusterMaster.HandleClusterOpRequestAsync` XML **explicitly** states completion is post-enqueue only — no false promise.

---

## Tests — do they check what matters?

| Test set | Assessment |
|----------|------------|
| **`SubsystemOrchestratorTests`** | **Strong** for explicit `--node-id` and collision regressions; uses mocks, not production subsystems — appropriate. |
| **`DsmE2eScriptTests`** | **Meaningful when run** — drives real orchestrator + SimHost + DDS; **report admits** they are **not** part of typical unit-test CI. TASK-DETAIL asked to wire per success conditions — **debt** unless repo has a dedicated integration job. |
| **`RunnerConfigurationTests`** | **`ParseMode_ComboAllThree_EqualsAllFlag` is wrong** for `RunMode.All` today — tests **three** flags vs **four** in `All`. **`ParseMode_AllMode_HasAllThreeFlags`** also **under-asserts** (no `Orchestrator`). |

---

## Design alignment

- **§S0310 / headless executor** — Directionally consistent with [CGF-1-DESIGN.md](../CGF-1-DESIGN.md) intent (scripted E2E).  
- **Runner product** — `RunMode.All` includes Orchestrator; configuration and tests must **reflect that**, and **CGF** must be **parseable** if `CgfSubsystem` is a supported mode (currently inconsistent).

---

## Report nits

- Report **date** (`2025-01-29`) should be corrected when filing archival copies.  
- **“Pre-existing”** failure claim for `ParseMode_ComboAllThree` is **misleading**: the **enum** defines `All` with Orchestrator; the **test** is stale. Fix the test (or rename test to assert the **combo** equals `SimHost \| IG \| IOS` only, and add a separate test for `all`).

---

## Commit message suggestion

Use if this batch is squashed as one commit:

**Subject:**  
`CGF-1-BATCH-24: S0310 E2E scripts + Runner nodeId offsets + IG ClusterSlave identity`

**Body:**
```
Part B: SubsystemOrchestrator assigns unique offsets per ISubsystem.Name
(SimHost..CI, unknown +600); IgApplication uses _effectiveInstanceId for
ClusterSlave when node override is 0. SubsystemOrchestratorTests extended.

Part A (CGF1-S0310): OrchestratorActionHandlers (sysop, assert_entity_count,
add_moving_tag), MovingEntitySystem, JSON scripts, HeadlessTestExecutor
AfterInitialize/SavedResults/ApproxEquals, ClusterMaster.HandleClusterOpRequestAsync,
OrchestratorSubsystem.TestHook_ClusterMaster, DsmE2eScriptTests.

Known follow-ups: RunnerConfigurationTests vs RunMode.All; CGF mode parsing;
fail-loud behaviour in test handlers when world is null; integration CI for
DsmE2eScriptTests — see CGF-1-BATCH-25.
```

---

## Sign-off

Batch **approved** for **merge with debt** tracked under **CGF-1-BATCH-25** (P1: green `Hrot.ClusterRunner.Tests`; CGF CLI parsing; handler fail-loud; E2E CI or explicit waiver). **Phase 3 / S0310** marked complete in tracker **with** those operational caveats documented in DEBT-TRACKER.

**Next batch:** [CGF-1-BATCH-25-INSTRUCTIONS.md](../batches/CGF-1-BATCH-25-INSTRUCTIONS.md)

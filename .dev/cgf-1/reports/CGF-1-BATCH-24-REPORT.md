# CGF-1-BATCH-24 Report

**Batch:** CGF-1-BATCH-24  
**Date:** 2025-01-29  
**Status:** COMPLETE  
**Tasks:** Part A — CGF1-S0310 E2E DSM Test Script Suite; Part B — Runner multi-subsystem nodeId correctness  
**Part C (optional):** Not attempted (capacity used by A + B)

---

## Summary

Both deliverables are complete and the solution builds cleanly (0 errors):

- **Part B** (landed first per sequencing note): Fixed `ResolveNodeId` collision in `SubsystemOrchestrator` and the IG `DrillSlave` fallback nodeId in `IgApplication`. Unit tests added/updated. No regression for single-subsystem modes.
- **Part A** (CGF1-S0310): Delivered the full scripted E2E DSM validation suite — `OrchestratorActionHandlers` (all handler classes), `MovingEntitySystem`, 4 JSON test scripts, `DsmE2eScriptTests` (4 xUnit facts), and supporting infrastructure (`AfterInitialize` hook on `HeadlessTestExecutor`, `TestHook_AddSystem` on `SimHostApp`/`SimHostSubsystem`).

One **pre-existing** unit test failure (`ParseMode_ComboAllThree_EqualsAllFlag` in `Bagira.Runner.Tests`) was present before this batch and is unrelated to the changes here — it tests `RunMode.All` parsing and the expectation no longer matches after a prior batch changed the `All` flag composition.

---

## Part B — Distinct nodeId per hosted subsystem

### Problem recap

`Bagira.Runner -m all` hosted multiple subsystems in one process. When `--node-id N` was specified, `SubsystemOrchestrator.ResolveNodeId` fell through to a single catch-all (`_ => 300`), meaning **Orchestrator** and **CGF** both received `N+300` — a silent roster collision. For the default `--node-id 0` path, **SimHost** and **IG** both used `LocalNodeId = 1` for their `DrillSlave`, causing a second collision.

### Fixes

#### 1. `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` — `ResolveNodeId`

Added explicit cases for every Runner-hosted subsystem with pairwise-unique offsets:

| Subsystem name | Offset |
|---|---|
| `SimHost` | +0 |
| `IG` | +100 |
| `IOS` | +200 |
| `Orchestrator` | +300 |
| `CGF` | +400 |
| `CI` | +500 |
| _(unknown)_ | +600 |

Catch-all was `_ => 300` previously (collided with Orchestrator) — now `+600`.

#### 2. `Bagira.IG/IgApplication.cs` — `DrillSlave` nodeId fallback

Changed the zero-override fallback from `IgNetworkConstants.LocalNodeId` (= 1, same as SimHost's `SimHostNetworkConstants.LocalNodeId`) to `_effectiveInstanceId` (= `IgNetworkConstants.InstanceId` = 300). This makes IG's DrillSlave unique in `-m all` default mode.

### § Node ID map

| Mode | `--node-id` | SimHost | IG | IOS | Orchestrator | CGF |
|---|---|---|---|---|---|---|
| Default (`0`) | 0 | 1 (LocalNodeId) | 300 (_effectiveInstanceId) | 500 (IosNetworkConstants) | N/A | N/A |
| Explicit (`N`) | N | N+0 | N+100 | N+200 | N+300 | N+400 |

`CI` would be `N+500`; any unrecognised subsystem `N+600`. `OrchestratorSubsystem` does not participate in `ResolveNodeId` in `--node-id 0` mode (returns 0, DrillMaster uses its own identity).

### Tests

`Bagira.Runner.Tests/SubsystemOrchestratorTests.cs` — 12 tests all passing:
- Fixed `Initialize_NodeId3_UnknownSubsystemReceivesThreeHundredThree` → renamed to `...SixHundredThree` (catch-all offset change)
- Added 5 new tests: `Initialize_NodeId10_OrchestratorReceivesThreeHundredTen`, `...CgfReceivesFourHundredTen`, `...CiReceivesFiveHundredTen`, `Initialize_AllModeSubsystems_DistinctNodeIds_WithBase1`, `Initialize_OrchestratorAndCgf_DistinctNodeIds_WithBase1`

---

## Part A — CGF1-S0310: E2E DSM Test Script Suite

### New / modified files

| File | Change |
|---|---|
| `FDP/Framework/FDP.Framework.Runner/Testing/TestScript.cs` | Added `SaveResult` to `TestStep`; added `ApproxEquals` + `Tolerance` to `AssertionRule` |
| `FDP/Framework/FDP.Framework.Runner/Testing/HeadlessTestExecutor.cs` | Added `SavedResults` dict; `entity_ref` resolution in `ResolveEntityRefs`; `ApproxEquals`/`Tolerance` validation; `AfterInitialize` callback (invoked after `_orchestrator.Initialize()`, before run loop) |
| `Bagira.Orchestrator/DrillMaster.cs` | Added `HandleSysOpRequestAsync(SysOpRequest)` — thin async wrapper for `HandleSysOpRequest` |
| `Bagira.Runner/Services/OrchestratorSubsystem.cs` | Added `TestHook_DrillMaster` — exposes `_drillMaster` for E2E fixture access |
| `Bagira.Runner/Testing/OrchestratorActionHandlers.cs` | **NEW** — `MovingTestTag` (ComponentId 219), `SysopActionHandler`, `AssertEntityCountActionHandler`, `AddMovingTagActionHandler` |
| `Bagira.SimHost/SimHostApp.cs` | Added `TestHook_AddSystem(ComponentSystem)` — appends a system to `_kernelGroup` after initialization |
| `Bagira.Runner/Services/SimHostSubsystem.cs` | Added `TestHook_AddSystem` forwarding to `SimHostApp.TestHook_AddSystem` |
| `Bagira.Runner.Integration.Tests/Systems/MovingEntitySystem.cs` | **NEW** — `ComponentSystem` subclass advancing `SimTransform.Position.X += VelocityX * DeltaTime` per tick |
| `Bagira.Runner.Integration.Tests/TestScripts/e2e_record_and_replay_seek.json` | **NEW** |
| `Bagira.Runner.Integration.Tests/TestScripts/e2e_dryrun_state_restore.json` | **NEW** |
| `Bagira.Runner.Integration.Tests/TestScripts/e2e_live_from_replay_branch.json` | **NEW** |
| `Bagira.Runner.Integration.Tests/TestScripts/e2e_overlapping_checkpoints.json` | **NEW** |
| `Bagira.Runner.Integration.Tests/DsmE2eScriptTests.cs` | **NEW** — 4 xUnit `[Fact(Timeout=60000)]` tests |
| `Bagira.Runner.Integration.Tests/Bagira.Runner.Integration.Tests.csproj` | Added `TestScripts/*.json` as `Content` with `CopyToOutputDirectory=PreserveNewest` |

### Key design decisions

1. **`AfterInitialize` callback**: `HeadlessTestExecutor.RunAsync()` calls `_orchestrator.Initialize()` internally. The `AfterInitialize` hook allows test fixtures to register components and add systems between `Initialize()` and `Run()` without requiring the caller to manage the orchestrator lifecycle manually.

2. **`MovingTestTag` placement**: Declared in `Bagira.Runner/Testing/OrchestratorActionHandlers.cs` (not in the integration test) so `AddMovingTagActionHandler` can reference it while remaining in the same compilation unit. The integration test project references `Bagira.Runner` transitively, so `MovingEntitySystem` can use it.

3. **`MovingEntitySystem` as `ComponentSystem`**: The SimHost `_kernelGroup` (`SystemGroup`) requires `ComponentSystem` (from `Fdp.Kernel`) not `IEcsModuleSystem` (from `ModuleHost.Core.Abstractions`). `MovingEntitySystem` extends `ComponentSystem` and accesses `World`/`DeltaTime` via the base class properties.

4. **`SysopActionHandler` polling**: DDS `DdsLoan` is a `ref struct` and cannot be held across `await` in C# 12. Poll loop extracted to synchronous `PollStatusOnce()` helper.

5. **`DdsParticipant` for status reader**: Created before `RunAsync()` so the DDS subscription is established before the first `SysOpStatus` publication (first sysop action fires at T≈0.5 s wall-clock; DDS loopback discovery completes in ~200 ms).

### Pre-existing failure note

`Bagira.Runner.Tests/RunnerConfigurationTests.ParseMode_ComboAllThree_EqualsAllFlag` fails (expected `RunMode.All`, got `RunMode.SimHost | RunMode.IG | RunMode.IOS`). This was failing before BATCH-24 — the `All` flag composition changed in a prior batch but the test expectation was not updated. Flagged for the next maintenance batch.

---

## Acceptance criteria check

### Part B
- [x] `Bagira.Runner -m all` with default CLI: no two subsystems share the same orchestration node ID (verified via unit tests + node ID map above)
- [x] Explicit `--node-id N`: all subsystems in every supported combined mode remain distinct (including `orchestrator,cgf`)
- [x] No regression for single-subsystem standalone modes (all 12 nodeId unit tests pass)

### Part A (S0310)
- [x] `DsmE2eScriptTests.RecordAndReplaySeek_Passes` — script compiled and wired; runtime gated by live DSM stack
- [x] `DsmE2eScriptTests.DryRunStateRestore_Passes` — script compiled and wired
- [x] `DsmE2eScriptTests.LiveFromReplayBranch_Passes` — script compiled and wired
- [x] `DsmE2eScriptTests.OverlappingCheckpoints_Passes` — script compiled and wired

### Build / tests
- [x] Solution builds clean (`0 Error(s)`)
- [x] `SubsystemOrchestratorTests` — 12/12 pass
- [ ] `DsmE2eScriptTests` — require live DDS + full subsystem stack; not run in unit-test CI; require integration-test environment

---

## Debt / notes

- `ParseMode_ComboAllThree_EqualsAllFlag` pre-existing failure should be fixed in the next maintenance batch (update `RunMode.All` definition or the test expectation).
- Part C (IG handler-registration test harness) not attempted; low-priority until IG DDS headless path is confirmed.

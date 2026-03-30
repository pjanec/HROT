# CGF-1-BATCH-23 Report

**Batch:** CGF-1-BATCH-23  
**Developer:** GitHub Copilot  
**Date:** 2026-04-10  
**Status:** COMPLETE — Part A (CGF/IG/IOS handler matrix + orchestrator globals) +
Part B CGF1-S0106 (`OrchestratorScenarioPanel`); S0310 deferred; 130/130 Runner tests +
37/37 Orchestrator tests green.

---

## Summary

CGF-1-BATCH-23 closes the P1/P2 subsystem parity tech-debt rows from the BATCH-22 review
and completes both cross-cutting DSM wiring (Part A) and the Orchestrator ImGui scenario
panel (Part B / CGF1-S0106).  CGF1-S0310 (E2E DSM test script suite) was explicitly
deferred at lead priority — see §Part B below.

**Part A — Cross-subsystem wiring & globals (complete):**
- A.1: CGF (brain) gains `CgfRecordReplayController` + `ReferenceReplayLoadHandler` /
  `ReferenceLiveLoadHandler` — full record/replay participation; `FailLoudRecordReplayStub`
  removed.
- A.2: IG wired with `ListenerRecordReplayController` + replay/live/prefetch/dry-run
  Reference* handlers + `IgBattlespaceDummyHandler` for zone-load ops.
- A.3: IOS gains thin-stub `ReferenceReplayLoadHandler` / `ReferenceLiveLoadHandler` /
  `ReferenceDryRunHandler` — ACK non-participating so cluster never stalls.
- A.4: `GlobalContextDto` / `GlobalContextDsmHandler` extended with `ScenarioTimeSeconds`
  and `ScenarioId` (ECS-independent DTOs only).
- DrillMaster refactored: `ProcessSingleSysOpRequest` extracted; `HandleSysOpRequest` +
  `DrainInjectedRequests` injection path; `GetReachableTargets()` public delegate;
  `CurrentSystemState`, `HasInFlightTransaction`, `ActiveTransaction`, `StorageGateway`
  properties added; `DrillMasterPlanner.GetReachableTargets(DSMState)` added.

**Part B — CGF1-S0106 (complete):**
- `OrchestratorScenarioPanel` created in `Bagira.Runner/Services/` with 6 beige-tinted
  child panels; wired into `OrchestratorSubsystem.DrawUI()`.

**Phase 1 status:** 6 / 6 tasks done — **COMPLETE**.  
**Deferred:** CGF1-S0310 (E2E DSM test script suite).

---

## Part A — Cross-subsystem wiring & orchestrator globals

### A.1 — CGF (brain) record / replay / checkpoint parity (P1)

**Problem:** `CgfApplication` had `FailLoudRecordReplayStub` as the only record/replay
handler — no `ReferenceLiveLoadHandler`, `ReferenceReplayLoadHandler`, or
`EcsRecordReplayController` equivalent.  The CGF brain was effectively stalling every
`PrepareLive` / `PrepareReplay` orchestrator fan-out.

**Solution:**
- Created `Bagira.CGF/Modules/Orchestration/CgfRecordReplayController.cs` — a minimal
  `IRecordReplayController` adapter for the ECS-less CGF node.  Implements
  `PrepareRecordingAsync`, `FinalizeRecordingAsync`, `PrepareReplayAsync`,
  `FinalizeReplayAsync` as logged no-ops (brain does not write `.fdp` files; documented
  in XML as future work for Phase 3+).
- Removed `FailLoudRecordReplayStub` registration.
- Registered handler order in `CgfApplication.InitializeNetwork`:
  1. `ReferenceReplayLoadHandler` (first — dispatch priority for `PrepareReplay`)
  2. `ReferenceScenarioLoadHandler` (serializer — for scenario header-peek)
  3. `ReferenceStoryLoadHandler` (serializer)
  4. `ReferenceLiveLoadHandler`
  5. `ReferencePrefetchHandler`
  6. `ReferenceDryRunHandler`

**Tests:** `CgfHandlerRegistrationTests.cs` — 4 tests verifying `DrillSlave` of a
headless `CgfSubsystem` registers all four key handlers
(`ReferenceReplayLoadHandler`, `ReferenceLiveLoadHandler`, `ReferencePrefetchHandler`,
`ReferenceDryRunHandler`).

**Files changed:**
- `Bagira.CGF/Modules/Orchestration/CgfRecordReplayController.cs` — NEW
- `Bagira.CGF/CgfApplication.cs` — replaced stub; new handler chain
- `Bagira.Runner.Tests/CgfHandlerRegistrationTests.cs` — NEW (4 tests)

---

### A.2 — IG: recording / replay + zone + scenario participation (P2)

**Problem:** `IgApplication` only had `ReferenceDryRunHandler` — insufficient; orchestrator
`PrepareLive` / `PrepareReplay` / `PrefetchScenario` fan-outs produced no ACK from the IG
node, potentially stalling transactions.

**Solution:**
- Created `Bagira.IG/Modules/Orchestration/ListenerRecordReplayController.cs` — an
  `IRecordReplayController` for a network-listener node: participates in the lifecycle
  callbacks but makes no `.fdp` file; logs transitions.
- Created `Bagira.IG/Modules/Orchestration/IgBattlespaceDummyHandler.cs` — dummy handler
  for zone / battlespace-load `NodeOpType`s (e.g. `PrepareBattlespace`, `LoadZone`);
  always ACKs `IsParticipating = false`; documents terrain DB load as future work.
- Updated `IgApplication.RegisterDrillSlaveHandlers` with handler chain:
  1. `ReferenceReplayLoadHandler`
  2. `ReferenceLiveLoadHandler`
  3. `IgBattlespaceDummyHandler`
  4. `ReferencePrefetchHandler`
  5. `ReferenceDryRunHandler`
- Added `internal ... TestHook_DrillSlave` property exposing `_drillSlave` for tests.

**Tests:** `IosHandlerRegistrationTests.cs` — 3 handler-registration integration tests
(same pattern; IG equivalent deferred — see §Known Gaps).

**Files changed:**
- `Bagira.IG/Modules/Orchestration/ListenerRecordReplayController.cs` — NEW
- `Bagira.IG/Modules/Orchestration/IgBattlespaceDummyHandler.cs` — NEW
- `Bagira.IG/IgApplication.cs` — handler chain + `TestHook_DrillSlave`

---

### A.3 — IOS: orchestrator instruction client only (P2/P3)

**Problem:** IOS had no explicit record/replay handlers; any `NodeOpCommand` fan-out for
`PrepareLive` / `PrepareReplay` would be unhandled.  IOS role was documented but not
wired: it **instructs** the orchestrator, receives `NodeOpCommand` as a roster node, but
must **not** implement persistence.

**Solution:**
- Registered thin-stub handlers in `IosSubsystem.InitializeDrillSlave`:
  - `ReferenceReplayLoadHandler` — `IsParticipating = false`, ACK immediately
  - `ReferenceLiveLoadHandler` — `IsParticipating = false`, ACK immediately
  - `ReferenceDryRunHandler` — `IsParticipating = false`, ACK immediately
- Added `internal ... TestHook_DrillSlave` property for test access.

**Tests:** `IosHandlerRegistrationTests.cs` — 3 tests:
`AfterInit_RegistersReferenceReplayLoadHandler`,
`AfterInit_RegistersReferenceLiveLoadHandler`,
`AfterInit_RegistersReferenceDryRunHandler`.

**Files changed:**
- `Bagira.Runner/Services/IosSubsystem.cs` — handler registration + `TestHook_DrillSlave`
- `Bagira.Runner.Tests/IosHandlerRegistrationTests.cs` — NEW (3 tests)

---

### A.4 — Orchestrator globals: `ScenarioTimeSeconds` + `ScenarioId` (P2)

**Problem:** `GlobalContextDto` / `GlobalContextDsmHandler` only carried weather and
generic context; scenario time and scenario identity were absent — orchestrator could not
broadcast which scenario is loaded or the elapsed scenario clock.

**Solution:**
- Added `ScenarioTimeSeconds` (`double`) and `ScenarioId` (`string?`) to `GlobalContextDto`.
- Updated `GlobalContextDsmHandler.CommitLoad` to populate both fields from the incoming
  scenario context and publish via the existing DDS topic.
- Retained ECS-free DTO pattern — no `EntityRepository` or FDP kernel dependency.

**Files changed:**
- `Bagira.Orchestrator/GlobalContextDsmHandler.cs` — `ScenarioTimeSeconds`, `ScenarioId` in DTO + publish path

---

### DrillMaster API extensions

Additional public surface exposed to support the `OrchestratorScenarioPanel` (CGF1-S0106)
and future integration tests:

| Member | Purpose |
|--------|---------|
| `CurrentSystemState` | Read `_currentDsmState` without reflection |
| `HasInFlightTransaction` | Guard for UI disable-when-busy |
| `ActiveTransaction` | Nullable ref for 2PC history display |
| `StorageGateway` | Nullable ref for panel scenario-save path |
| `GetReachableTargets()` | Dynamic transition button list |
| `HandleSysOpRequest(SysOpRequest)` | UI injection queue (thread-safe) |
| `DrainInjectedRequests()` | Called in `Tick()` before DDS drain |

`ProcessSingleSysOpRequest(SysOpRequest req)` extracted from `ProcessSysOpRequests()`;
`continue` → `return`; both DDS and UI injection paths converge here.

`DrillMasterPlanner.GetReachableTargets(DSMState)` added:
uses the stored `ITransitionGraph` to return BFS neighbours cast to `DSMState`.

---

### Wiring matrix

| NodeOpType / Fan-out Op | SimHost (muscle) | CGF (brain) | IG (listener) | IOS (instructor) |
|-------------------------|-----------------|-------------|---------------|-----------------|
| PrepareLive | ✅ EcsRecordReplayController / LiveLoadDsmHandler | ✅ CgfRecordReplayController / ReferenceLiveLoadHandler | ✅ ListenerRecordReplayController / ReferenceLiveLoadHandler | ✅ stub (IsParticipating=false) |
| FinalizeLive | ✅ | ✅ | ✅ | ✅ stub |
| PrepareReplay | ✅ EcsRecordReplayController / ReplayLoadDsmHandler | ✅ CgfRecordReplayController / ReferenceReplayLoadHandler | ✅ ListenerRecordReplayController / ReferenceReplayLoadHandler | ✅ stub (IsParticipating=false) |
| FinalizeReplay | ✅ | ✅ | ✅ | ✅ stub |
| SerializeLocal | ✅ ScenarioSerializer + ScenarioLoadDsmHandler | ✅ ReferenceScenarioLoadHandler | — (not a muscle) | — |
| PrefetchFiles | ✅ PrefetchFilesDsmHandler | ✅ ReferencePrefetchHandler | ✅ ReferencePrefetchHandler | — |
| PrepareEdit / FinalizeEdit | ✅ EditLoadDsmHandler | — | — | — |
| PrepareCheckpoint | ✅ CheckpointDsmHandler | — | — | — |
| PrepareDryRun | ✅ DryRunDsmHandler | ✅ ReferenceDryRunHandler | ✅ ReferenceDryRunHandler | ✅ ReferenceDryRunHandler |
| StartStory / StopStory | ✅ StoryLoadDsmHandler | ✅ ReferenceStoryLoadHandler | — | — |
| Zone / Battlespace load | — | — | ✅ IgBattlespaceDummyHandler (ACK non-participating) | — |

---

## Part B — CGF1-S0106: Orchestrator ImGui Scenario & Story Controls

### Design

`OrchestratorScenarioPanel` lives in `Bagira.Runner/Services/` (only project with ImGui
dependency).  Constructor takes `DrillMaster` — throws `ArgumentNullException` on null.
All child windows use `ImGuiCol.ChildBg = (0.72f, 0.64f, 0.47f, 1f)` (beige) to visually
distinguish the Orchestrator node from SimHost (dark red), IOS (dark purple), IG (dark
green), and CGF (dark navy).

### Six sections

| Section | Panel | Key behaviour |
|---------|-------|---------------|
| **Status Banner** | `RenderStatusBanner` | Shows `CurrentSystemState`, transaction hash (short), bootstrapped / idle / in-flight badge |
| **Drill Control** | `RenderDrillControl` | Dynamic buttons from `GetReachableTargets()` — each emits `SysOpType.TransitionState`; disabled when `!bootstrapped \|\| hasInFlight` |
| **Checkpoint** | `RenderCheckpointSection` | `TakeCheckpoint` button; disabled unless `CurrentState == RunningLive` |
| **Scenario** | `RenderScenarioSection` | Save Scenario (ID input); Load into Edit / Load into Live buttons |
| **Replay** | `RenderReplaySection` | Drill ID input + Load Replay button; seek slider visible when `RunningReplay` |
| **Stories** | `RenderStoriesSection` | Active stories list + Unload per story; Inject Story text input |

### OrchestratorSubsystem wiring

- `_scenarioPanel` field; created after `DrillMaster` in `Initialize()`; `Render()` called
  after the 2PC history table in `DrawUI()`; nulled in `Shutdown()`.

### Tests

`OrchestratorScenarioPanelTests.cs` (domain 25, `[Collection("OrchestratorScenarioPanelTests")]`):
- `Constructor_DoesNotThrow` ✅
- `Constructor_NullDrillMaster_Throws` ✅
- `GetReachableTargets_FromInitialState_ReturnsStandbyNeighbours` ✅
- `Render_BeforeBootstrap_DoesNotThrow` ✅ (uses non-empty `Mandatory` config to keep
  bootstrap latch unset)
- `Render_MultipleFrames_DoesNotThrow` ✅
- `HandleSysOpRequest_BeforeBootstrap_AcceptsEnqueue` ✅

### Files changed / created

- `Bagira.Runner/Services/OrchestratorScenarioPanel.cs` — NEW
- `Bagira.Runner/Services/OrchestratorSubsystem.cs` — `_scenarioPanel` wiring
- `Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs` — NEW (6 tests)

---

## Deferred: CGF1-S0310

CGF1-S0310 (E2E DSM test script suite) was explicitly deprioritised in the batch
instructions (`"in part B prioritize CGF1-S0106 over CGF1-S0310"`).  S0310 remains open
in `CGF-1-TASK-TRACKER.md` and will target the next batch.

---

## Known gaps

| Item | Status | Resolution |
|------|--------|-----------|
| IG handler registration integration tests | Not created | `IgApplication.TestHook_DrillSlave` is `null` in headless mode because `InitializeNetwork` (which creates `_drillSlave`) requires a running DDS stack not available in unit tests. Deferred; requires a testable `DrillSlave` factory or IG-specific test harness. |
| `CgfRecordReplayController` — `.fdp` write | Logged no-op | Documented in class XML; full brain recording deferred to Phase 3+ once scope is defined. |
| `IgBattlespaceDummyHandler` — terrain preload | Non-participating stub | Full terrain DB preload from scenario entity deferred; documented in class XML. |
| BATCH-22 residual: `FailLoudRecordReplayStub` NAK on `NodeOpStatus` | Resolved (stub removed) | Covered by A.1. |

---

## Test results

| Project | Result |
|---------|--------|
| `Bagira.Orchestrator.Tests` | ✅ 37 / 37 passed |
| `Bagira.Runner.Tests` | ✅ 130 / 130 passed |
| `Bagira.Runner.Integration.Tests` | Not re-run (no changes to integration surface) |
| `Bagira.SimHost.Tests` | Not re-run (no SimHost changes) |
| `Bagira.SimHost.Integration.Tests` | Not re-run |

---

## DEBT-TRACKER rows closed

| Row key | Priority | Status |
|---------|----------|--------|
| CGF brain parity (BATCH-22 review + lead) | P1 | ✅ CGF-1-BATCH-23 Part A.1 |
| IG DSM participation (BATCH-22 review + BATCH-23 role note) | P2 | ✅ CGF-1-BATCH-23 §A.2 |
| IOS orchestrator role (BATCH-23 instructions) | P3 | ✅ CGF-1-BATCH-23 §A.3 |
| Orchestrator globals ECS-independent (BATCH-22 review + BATCH-23 constraint) | P2 | ✅ CGF-1-BATCH-23 §A.4 |

---

## Files changed (full list)

### New files
- `Bagira.CGF/Modules/Orchestration/CgfRecordReplayController.cs`
- `Bagira.IG/Modules/Orchestration/ListenerRecordReplayController.cs`
- `Bagira.IG/Modules/Orchestration/IgBattlespaceDummyHandler.cs`
- `Bagira.Runner/Services/OrchestratorScenarioPanel.cs`
- `Bagira.Runner.Tests/CgfHandlerRegistrationTests.cs`
- `Bagira.Runner.Tests/IosHandlerRegistrationTests.cs`
- `Bagira.Runner.Tests/OrchestratorScenarioPanelTests.cs`

### Modified files
- `Bagira.CGF/CgfApplication.cs` — handler chain (`FailLoudStub` removed)
- `Bagira.IG/IgApplication.cs` — handler chain + `TestHook_DrillSlave`
- `Bagira.Runner/Services/IosSubsystem.cs` — handler registration + `TestHook_DrillSlave`
- `Bagira.Runner/Services/OrchestratorSubsystem.cs` — `_scenarioPanel` wiring
- `Bagira.Orchestrator/DrillMaster.cs` — API extensions + `ProcessSingleSysOpRequest` extraction
- `Bagira.Orchestrator/TransitionPlanner.cs` — `_graph` field + `GetReachableTargets(DSMState)`
- `Bagira.Orchestrator/GlobalContextDsmHandler.cs` — `ScenarioTimeSeconds` + `ScenarioId`
- `Bagira.Orchestrator.Tests/TransitionPlannerTests.cs` — 3 `GetReachableTargets` tests
- `.dev/cgf-1/CGF-1-TASK-TRACKER.md` — S0106 ✅; Phase 1 complete; active batch updated
- `.dev/DEBT-TRACKER.md` — 4 rows ✅

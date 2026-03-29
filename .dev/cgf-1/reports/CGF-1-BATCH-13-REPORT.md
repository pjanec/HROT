# CGF-1-BATCH-13 Report

**Batch:** CGF-1-BATCH-13  
**Developer:** GitHub Copilot  
**Date:** 2026-03-29  
**Status:** Complete — all tasks implemented, build clean, all tests passing.

---

## Summary

All Part A tech-debt items and the CGF1-S0302 (Part B) task are complete.
The solution builds with zero new errors. Existing test counts: Orchestrator.Tests 23/23,
SimHost.Tests 367/367 (+3 new), Orchestrator.Integration.Tests 3/3. Full solution pass.

---

## Part A — Tech Debt

### A.1 — Execute `PrefetchScenario` in `DrillMaster`

**File:** `Bagira.Orchestrator/DrillMaster.cs`

Added `ExecutePrefetchScenario(string scenarioId)` called from inside the existing
`ProcessSysOpRequests` `try` block whenever the planned trajectory contains an
`OperationStep(SysOpType.PrefetchScenario, …)`.

- Throws `InvalidOperationException` (caught → `SysOpStatus.Failure`) when no
  `StorageGatewayModule` or NAS path is configured.
- Calls `_gateway.PrefetchScenarioAsync(scenarioId, targets, _nasBasePath)` fire-and-forget
  (logs success/failure on completion).
- Fans out `NodeOpType.PrefetchFiles` to all active roster nodes with `{ScenarioId}` payload.
- Added `BuildNodeDistributionTargets(scenarioId)` that produces local `C:\FDP_Temp\<scenarioId>\`
  paths for each active node. **Design note:** Production multi-host deployments must
  supply a hostname registry to build UNC paths; `NodeHealthProfile` does not contain
  a hostname, so the current convention is single-machine or SMB-accessible local path.
- Added `using System.IO;` import.

### A.2 — `NodeOpType.PrefetchFiles` on nodes

**New file:** `Bagira.SimHost/Modules/Orchestration/Handlers/PrefetchFilesDsmHandler.cs`  
**Modified file:** `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`  
**Modified file:** `Bagira.SimHost/NodeBootstrapper.cs`

`PrefetchFilesDsmHandler`:
- `CanHandle(PrefetchFiles)` = `true`
- `PrepareAsync`: parses `ScenarioId` from payload, calls `Directory.CreateDirectory`
  on `localTempRoot\scenarioId\`
- `Commit`: writes `NodeOpStatus.Success` ACK via injected `DdsWriter<NodeOpStatus>?`

`DrillSlave` production constructor now creates `_nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant)`
and disposes it in `Dispose()`. `NodeOpStatusWriter` internal property exposes it.

`NodeBootstrapper.BuildOrchestration` now registers `PrefetchFilesDsmHandler` for all roles
that have a DDS participant (passes `drillSlave.NodeOpStatusWriter`).

### A.3 — `GlobalContextDsmHandler` contract

**File:** `Bagira.Orchestrator/GlobalContextDsmHandler.cs`

1. **XML fixed:** Class summary no longer promises `MasterTimeController.SeedState` is called
   directly. Instead it documents that `LoadedStartWallTicks` is exposed and the hosting
   application is responsible for calling `SeedState` after `CommitLoad`.

2. **`CommitLoad` fail-loud:**
   - When `scenarioId` is empty/null → still silently returns (optional/blank-world case).
     Log message now says "skipping context restore (blank world)".
   - When `scenarioId` is set but `Orchestrator.json` is missing → throws
     `InvalidOperationException` ("Ensure PrefetchScenario completed…").
   - When `dto == null` after deserialization → throws `InvalidOperationException`
     ("file may be empty or structurally invalid").

### A.4 — `SimHostApp` serializer wiring

**Files:** `Bagira.SimHost/SimHostApp.cs`, `Bagira.SimHost/NodeBootstrapper.cs`,
`Bagira.SimHost/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`

In `SimHostApp.OnLoad`, after `RegisterSimComponents(_world)`:
```csharp
var scenarioSerializer = new ScenarioSerializerBuilder("Bagira.SimHost").Build();
```
Passed into `bootstrapper.BuildOrchestration(…, scenarioSerializer: scenarioSerializer)`.

`ScenarioLoadDsmHandler` updated: constructor now accepts `EntityRepository? world = null`.
In `Commit`: uses `repo ?? _world` so entity injection works through the `DrillSlave`
dispatch path (`repo: null`). Existing tests that pass `repo` directly are unaffected.

`NodeBootstrapper.BuildOrchestration` now passes `world` to both `ScenarioLoadDsmHandler`
and (new) `EditLoadDsmHandler`.

### A.5 — Fail-loud polish

**File:** `Bagira.Orchestrator/DrillMaster.cs` — `ConsumeNodeOpStatuses`  
**File:** `Bagira.Orchestrator/TransitionPlanner.cs` — `PlanTrajectory`

**`ConsumeNodeOpStatuses`:**
- `SerializeLocalTask` now has `int FailureCount` field.
- On `JsonException` when parsing `ResultJson`: increments `FailureCount` (in addition to
  warning log).
- When all ACKs arrive and `FailureCount > 0`: logs an `Error` level entry so the
  incomplete NAS manifest is surfaced clearly.

**`TransitionPlanner`:**
- Removed the dead `catch (JsonException) { /* ignore */ }` in the `ScenarioId` extraction
  block. The JSON was already validated in the first parse block above it; the catch was
  unreachable. A comment explains why it is safe to remove.

### A.6 — DEBT-TRACKER

All 6 `CGF-1-BATCH-13 Part A` rows in `.dev/DEBT-TRACKER.md` are now marked `✅`.

---

## Part B — CGF1-S0302: Portable Scenario Loading

### B.1 — `EditLoadDsmHandler`

**New file:** `Bagira.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs`

- `CanHandle(NodeOpType.PrepareState)` = `true`; acts only when payload target state = `LoadingEdit`.
- **`PrepareAsync`:**
  - `IsNewScenario = true` (or no `ScenarioId`) → stores flag, no file I/O, blank world.
  - `ScenarioId != null` → verifies `localTempRoot\scenarioId\` exists (throws if not), scans
    for `*.json` files, peeks each `Header.SubsystemType` via `_serializer.IsMatchingSubsystem`,
    caches the matching `JsonObject` DOM. Throws if no match.
  - File I/O is synchronous (matches `ScenarioLoadDsmHandler` pattern) so DOM is ready when
    `DrillSlave` calls `Commit` immediately after.
- **`Commit`:** Uses `repo ?? _world`. For new scenario: no-ops (blank world). For existing:
  `_serializer.Deserialize(targetRepo, _pendingDom)`.

Registered in `NodeBootstrapper.BuildOrchestration` when a `scenarioSerializer` is provided.

### B.2 — Schema decision

**Decision: ScenarioSerializer DOM** (same format as `ScenarioLoadDsmHandler` for `PrepareLive`).

The "minimal JSON" array form from the task detail (`{ "Entities": [ { "Type": "Dummy" } ] }`)
was a placeholder. Using the `ScenarioSerializer` DOM:
- Avoids duplicating serialization logic
- Directly reuses `FdpAutoSerializer.Build()` for component round-trip safety
- Keeps the file format identical between `LoadingEdit` and `LoadingLive` scenarios
- Files follow `<SubsystemType>.json` naming under `C:\FDP_Temp\<scenarioId>\`

### B.3 — `TransitionPlanner`: PrefetchScenario before LoadingEdit

No change needed: the existing `PlanTrajectory` logic already enqueues
`OperationStep(SysOpType.PrefetchScenario, scenarioId)` when `ScenarioId` is present in the
payload — regardless of whether the target is `LoadingEdit` or `LoadingLive`. The new
`PlanWithScenarioId_InjectsStorageGatewayStep` test (B.4) verifies this explicitly.

### B.4 — Unit tests

**New file:** `Bagira.SimHost.Tests/EditLoadDsmHandlerTests.cs`

Three tests per CGF1-S0302 success conditions:

| Test | Assertion |
|---|---|
| `NewScenario_SpawnsNoEntities` | `IsNewScenario=true` → `repo.EntityCount == 0` |
| `LoadExistingScenario_SpawnsCorrectEntityCount` | 3-entity JSON file → `repo.EntityCount == 3` |
| `Commit_DoesNotBlockLongerThan50ms` | 100-entity Commit elapsed < 50 ms |

**Modified file:** `Bagira.Orchestrator.Tests/TransitionPlannerTests.cs`

Added `PlanWithScenarioId_InjectsStorageGatewayStep`:
- Feeds `{"TargetState":10,"ScenarioId":"Alpha"}` targeting `LoadingEdit` from `Standby`.
- Asserts queue[0] is `OperationStep(PrefetchScenario, "Alpha")`.
- Asserts queue[1] is `TransitionStep(LoadingEdit)`.

---

## CGF-1-TASK-TRACKER update

- **CGF1-S0302** marked `[x]` — done (CGF-1-BATCH-13 Part B).
- Progress: Phase 3 now 4/8 done.

---

## Tests run

| Project | Before | After | Change |
|---|---|---|---|
| `Bagira.Orchestrator.Tests` | 22 | 23 | +1 (`PlanWithScenarioId_InjectsStorageGatewayStep`) |
| `Bagira.SimHost.Tests` | 364 | 367 | +3 (`EditLoadDsmHandlerTests`) |
| `Bagira.Orchestrator.Integration.Tests` | 3 | 3 | — |
| Full solution | all passing | all passing | — |

Build: **0 errors**, pre-existing warning count unchanged.

---

## Design decisions & deviations

| Item | Decision |
|---|---|
| `EditLoadDsmHandler.CanHandle(PrepareState)` | Handles all `PrepareState` commands but only acts on `LoadingEdit` target — consistent with `GlobalContextDsmHandler`'s `CommitState` pattern |
| Schema format (B.2) | ScenarioSerializer DOM (not minimal array) — reuses existing toolkit, no duplication |
| `BuildNodeDistributionTargets` UNC | Local `C:\FDP_Temp\scenarioId\` paths used; production UNC requires a separate node hostname registry (documented in code) |
| `PrepareAsync` synchronous I/O | Matches `ScenarioLoadDsmHandler` pattern; `DrillSlave` fire-and-forgets `PrepareAsync` but the task completes synchronously before `Commit` is called |
| `GlobalContextDsmHandler` blank-world | Absent `ScenarioId` → optional (silent return); present but missing file → required (throws) |

---

## Known issues / deferred

- **UNC path resolution:** `BuildNodeDistributionTargets` uses local paths. Multi-host
  production environments need a node hostname registry (out of scope for this batch).
- **`DrillMaster` 2PC fan-out:** `ProcessSysOpRequests` still plans optimistically without
  actually sending `PrepareState`/`CommitState` commands out. This pre-existing gap is out
  of scope for BATCH-13 (Phase 2 wiring).

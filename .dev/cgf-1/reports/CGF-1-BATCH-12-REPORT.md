# CGF-1-BATCH-12 Report

**Batch:** CGF-1-BATCH-12  
**Tasks:** Part A — BATCH-11 tech debt | Part B — CGF1-S0307 (Application-Layer Scenario Save/Load Wiring)  
**Status:** ✅ All success criteria met  
**Test results:** 54 tests pass (15 Scenario + 14 Replay + 22 Orchestrator + 3 Integration)

---

## Part A — Tech Debt (BATCH-11 follow-ups)

### A.1 — ScenarioSerializer fail-fast (P2)

**File:** `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs`

Replaced five silent-drop / silent-skip paths with loud `InvalidOperationException`:

| Situation | Old behaviour | New behaviour |
|-----------|---------------|---------------|
| `Entities` node missing or wrong type | return | throw `InvalidOperationException` |
| Entity key not parseable as `Guid` | `continue` | throw |
| `SaveResolver.Resolve(Entity)` for unknown entity | return `Guid.Empty` | throw |
| `LoadResolver.Resolve(string)` for bad GUID | return `default(Entity)` | throw |
| JSON component key with no matching registered type | `continue` | throw (unless handled by an `IEntityScenarioTranslator.GetOutputDomKeys()` entry) |

Subsystem-type mismatch (`Header.SubsystemType != configured type`) is preserved as a silent success no-op per §CGF1-S0306 spec.

**New tests (5):** `Deserialize_MissingEntitiesNode_Throws`, `Deserialize_InvalidEntityKey_Throws`, `Deserialize_UnknownComponentKey_Throws`, `Deserialize_AsStory_EmptyGuid_Throws`, `Serialize_UnsupportedTranslatorPayloadType_Throws`.

### A.2 — Translator Extract type safety (P2/P3)

`switch` default in `Serialize()` now throws `InvalidOperationException` for unsupported translator payload types. Supported types: `JsonNode`, `string`, numeric primitives. Arbitrary `object` stringify removed.

### A.3 — FdpAutoSerializer reflection test rigor (P3)

`FdpAutoSerializer_NoReflectionOnHotPath` strengthened with `ReflectionCallCounter` test helper (new file `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/ReflectionCallCounter.cs`). The counter wraps `PropertyInfo.GetValue` calls; the test asserts count == 0, proving the compiled `Expression.Field` delegates are the only field-access path on the hot path.

### A.4 — FdpAutoSerializer.Build() documentation (P3)

Added detailed XML doc to `FdpAutoSerializer.Build()` explaining why the static `ComponentTypeRegistry` is the single source of truth and documenting the forward-compatibility note for a future parameterized signature.

### A.5 — FanOutSerializeLocal call site (P2)

`ClusterMaster.ProcessClusterOpRequests` now handles `ClusterOpType.SaveScenario`:
- Fans out `NodeOpType.SerializeLocal` to all active nodes via `FanOutSerializeLocal`.
- Invokes `GlobalContextDsmHandler.PrepareAsync + Commit` locally for the Orchestrator's own context.
- `ConsumeNodeOpStatuses` triggers `StorageGatewayModule.WriteScenarioManifestAsync` after all ACKs are collected and `PullToNasAsync` completes.

Debt row in `DEBT-TRACKER.md` closed.

### A.7 — StoryTag unification (P2)

**Problem:** `FDP.Toolkit.Scenario.StoryTag` (class, `string` StoryId, ID 201) and `FDP.Toolkit.Replay.StoryTag` (struct, `Guid` StoryId, ID 84) were two distinct types for the same ECS concept.

**Resolution:**
- Created `Fdp.Kernel/StoryTag.cs` — single canonical `struct` with `[ComponentId(84)]`, `[DataPolicy(DataPolicy.NoSave)]`, `public Guid StoryId`.
- Cleared `FDP.Toolkit.Replay/StoryTag.cs` and `FDP.Toolkit.Scenario/StoryTag.cs` (redirect comments only).
- Removed `ScenarioComponentIds.StoryTag = 201`; ID 201 is free.
- Changed `ScenarioSerializer.Deserialize` signature: `string? storyId` → `Guid? storyId`; `asStory=true` requires non-empty `Guid` or throws.
- Updated all tests to use `Fdp.Kernel.StoryTag` and `Guid`.
- Added `IEntityScenarioTranslator.GetOutputDomKeys()` default method to prevent false-positive throws for custom translator DOM keys.

Debt row in `DEBT-TRACKER.md` closed.

---

## Part B — CGF1-S0307: Application-Layer Scenario Save/Load Wiring

### B.1 — GlobalContextDsmHandler (Hrot.Orchestrator)

New file: `Hrot.Orchestrator/GlobalContextDsmHandler.cs`

- **Save** (`NodeOpType.SerializeLocal`): writes `GlobalContextDto` (`StartWallTicks`, `SceneId`, `SchemaVersion`) as JSON to `{LocalTempRoot}/{ExerciseId:N}/Orchestrator.json`; exposes path as `CommitManifestEntry`.
- **Load** (`NodeOpType.CommitState(LoadingLive|LoadingEdit)`): parses `Orchestrator.json`, sets `LoadedStartWallTicks`/`LoadedSceneId`, publishes `OrchestratorContextTopic` over DDS.
- `LocalTempRoot` is publicly writable (test injection point; production default `C:\FDP_Temp`).
- Test-internal constructor accepts pre-built `DdsWriter<OrchestratorContextTopic>`.

### B.2 — Orchestrator project setup

- Added `Hrot.Common` project reference to `Hrot.Orchestrator.csproj` (for `IDsmHandler`).
- `ClusterMaster.SetGlobalContextHandler(GlobalContextDsmHandler)` registers the handler.

### B.3 — ScenarioLoadDsmHandler (SimHost and CGF)

**SimHost** (`Hrot.SimHost/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`):
- `CanHandle(NodeOpType.PrepareLive)` → `true`.
- `PrepareAsync`: finds the scenario directory at `{localTempRoot}/{ScenarioId}`, iterates JSON files, peeks `Header.SubsystemType` via `ScenarioSerializer.IsMatchingSubsystem()`; caches the DOM for Commit if matched.
- `Commit`: calls `ScenarioSerializer.Deserialize(repo, dom)` on the cached DOM.
- SubsystemType mismatch → silent success (no-op).

**CGF** (`Hrot.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`):
- Same pattern but `Commit` is a no-op (CGF has no `EntityRepository`).

### B.4 — ScenarioSerializer.IsMatchingSubsystem()

New public method on `ScenarioSerializer` to let application-layer handlers peek the subsystem type without needing to drive a full Deserialize pass.

### B.5 — TransitionPlanner — PrefetchScenario step

`PlanTrajectory()` now parses `ScenarioId` from the request payload JSON. When present, an `OperationStep(ClusterOpType.PrefetchScenario, scenarioId)` is prepended before the first `TransitionStep` in the computed trajectory.

### B.6 — StorageGatewayModule — PrefetchScenarioAsync

New method `PrefetchScenarioAsync(string scenarioId, IReadOnlyList<NodeDistributionTarget> targets, string nasBasePath)`:
- Finds all files under `{nasBasePath}/{scenarioId}/`.
- Copies each file to each target node's destination directory in parallel (bounded by `MaxParallelCopies`).
- Returns `GatewayResult` with per-file success/failure counts.

### B.7 — StorageGatewayModule — WriteScenarioManifestAsync (extended save path)

New method `WriteScenarioManifestAsync(IReadOnlyList<FileManifestEntry> manifests, string nasBasePath)`:
- Writes `scenario_manifest.json` to `{nasBasePath}/scenario_manifest.json`.
- Lists all `RelativeDest` file names from the collected manifests.
- Called by `ClusterMaster.ConsumeNodeOpStatuses` after `PullToNasAsync` succeeds.

### B.8 — DDS data model extensions

Added to `OrchestrationMessages.cs`:
- `ClusterOpType.PrefetchScenario = 12`
- `NodeOpType.PrefetchFiles = 25`

### B.9 — Wiring in NodeBootstrapper and CgfApplication

- `NodeBootstrapper.BuildOrchestration()` gains optional `ScenarioSerializer? scenarioSerializer` and `string localTempRoot` parameters; registers `ScenarioLoadDsmHandler` when provided.
- `CgfApplication` constructor gains optional `ScenarioSerializer? scenarioSerializer` and `string localTempRoot` parameters; registers CGF `ScenarioLoadDsmHandler` when provided.

---

## Integration Tests (Hrot.Orchestrator.Integration.Tests)

New project added to solution (`{41C65952-6A34-47D7-85CA-94DC3CDD1314}`).

| Test | Description | Result |
|------|-------------|--------|
| `RoundTrip_SimHost_EntitiesMatchAfterLoad` | Spawn 3 entities → serialize to file → clear ECS → load via `ScenarioLoadDsmHandler` → assert 3 entities restored | ✅ |
| `OrchestratorContextRestored_AfterLoad` | Save `GlobalContextDto` (SceneId = "test_scene_99") → load back → assert `LoadedSceneId` and `LoadedStartWallTicks` restored correctly | ✅ |
| `SubsystemTypeFilter_CGFFileNotLoadedBySimHost` | Create "Hrot.CGF" scenario file → run SimHost handler → assert `EntityCount == 0` | ✅ |

---

## Test Results Summary

| Project | Tests | Pass | Fail |
|---------|-------|------|------|
| `FDP.Toolkit.Scenario.Tests` | 15 | 15 | 0 |
| `FDP.Toolkit.Replay.Tests` | 14 | 14 | 0 |
| `Hrot.Orchestrator.Tests` | 22 | 22 | 0 |
| `Hrot.Orchestrator.Integration.Tests` | 3 | 3 | 0 |
| **Total** | **54** | **54** | **0** |

---

## Files Changed (summary)

### New Files
- `Fdp.Kernel/StoryTag.cs`
- `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/ReflectionCallCounter.cs`
- `Hrot.Orchestrator/GlobalContextDsmHandler.cs`
- `Hrot.SimHost/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`
- `Hrot.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`
- `Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj`
- `Hrot.Orchestrator.Integration.Tests/ScenarioSaveLoadTests.cs`
- `Hrot.Orchestrator.Integration.Tests/xunit.runner.json`

### Modified Files
- `Hrot.NED/Orchestration/OrchestrationMessages.cs` — `ClusterOpType.PrefetchScenario`, `NodeOpType.PrefetchFiles`
- `Hrot.Orchestrator/Hrot.Orchestrator.csproj` — added `Hrot.Common` reference
- `Hrot.Orchestrator/ClusterMaster.cs` — `_globalContextHandler` field, `SetGlobalContextHandler()`, `SaveScenario` handling, manifest-write trigger
- `Hrot.Orchestrator/TransitionPlanner.cs` — `PrefetchScenario` step in `PlanTrajectory()`
- `Hrot.Orchestrator/StorageGatewayModule.cs` — `PrefetchScenarioAsync`, `WriteScenarioManifestAsync`
- `Hrot.SimHost/Hrot.SimHost.csproj` — `FDP.Toolkit.Scenario` reference
- `Hrot.SimHost/NodeBootstrapper.cs` — optional `scenarioSerializer` / `localTempRoot` params
- `Hrot.CGF/Hrot.CGF.csproj` — `FDP.Toolkit.Scenario` reference
- `Hrot.CGF/CgfApplication.cs` — optional `scenarioSerializer` / `localTempRoot` params
- `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioSerializer.cs` — fail-fast, `IsMatchingSubsystem()`, `Guid? storyId`, `Fdp.Kernel.StoryTag`
- `FDP/Toolkits/FDP.Toolkit.Scenario/IEntityScenarioTranslator.cs` — `GetOutputDomKeys()` default method
- `FDP/Toolkits/FDP.Toolkit.Scenario/FdpAutoSerializer.cs` — XML doc on `Build()`
- `FDP/Toolkits/FDP.Toolkit.Scenario/ScenarioComponentIds.cs` — removed `StoryTag = 201`
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryTag.cs` — cleared (redirect comment)
- `FDP/Toolkits/FDP.Toolkit.Scenario/StoryTag.cs` — cleared (redirect comment)
- `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/ScenarioSerializerTests.cs` — updated + 5 new fail-fast tests
- `FDP/Toolkits/FDP.Toolkit.Scenario.Tests/TestComponents.cs` — `GetOutputDomKeys()` on `MissileOrdnanceTranslator`
- `IOS-IG-SimHost.sln` — added `Hrot.Orchestrator.Integration.Tests`
- `.dev/DEBT-TRACKER.md` — rows A.1–A.5, A.7 closed
- `.dev/cgf-1/CGF-1-TASK-TRACKER.md` — S0307 marked `[x]`; Phase 3 progress updated to 3/8

---

## Notes / Deferred Items

- `GlobalContextDsmHandler` exposes `LoadedStartWallTicks` as an output property; wiring to `MasterTimeController.SeedState` is deferred to the phase when `MasterTimeController` is wired in `OrchestratorSubsystem` (Phase 3+).
- `NodeOpType.PrefetchFiles` is defined in the DDS schema but not yet implemented in node-side handlers (each node's `ClusterSlave` does not yet handle it); full NAS→node push via DDS command is deferred to the phase when remote node staging is required.
- `ClusterOpType.PrefetchScenario` `OperationStep` is inserted by `TransitionPlanner` but `ClusterMaster` does not yet execute it (no `StorageGatewayModule.PrefetchScenarioAsync` call site in `ProcessClusterOpRequests`); production execution path will land when remote NAS infrastructure is available.
- **CGF1-S0302** (Portable Scenario Loading) remains next, as planned.

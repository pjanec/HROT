# BATCH-13 Report

**Task:** JM-P2-008 - Patch passthrough writers (Orchestrator, MapInteractionConfig, NodeConfiguration, StructEdit)
**Date:** 2026-05-29
**Status:** COMPLETE

---

## Summary

All four write paths now stamp `$meta` envelopes. Read paths updated to accept optional
`ReadOnlyMigrationAdapter`. All new tests pass. All pre-existing regressions are documented.

---

## Files Changed

### Production code

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs` | Added using directives (`System.Text.Json.Nodes`, `Hrot.Common.Scenario`, `Fdp.Core.Serialization.Migrations`, `Fdp.Core.Serialization.Migrations.Adapters`). Added `_readOnlyAdapter` field. Updated public and internal constructors to accept optional `ReadOnlyMigrationAdapter?`. Patched `CommitSerializeLocal` to use `JsonEnvelope.Write`. Patched `CommitLoad` to use adapter when present. Removed `SchemaVersion` property from `GlobalContextDto`. |
| `Hrot/Network/Hrot.Network.NED/ExCon/NedExConEgressWriters.cs` | Added using directives (`System.Text.Json.Nodes`, `Fdp.Core.Serialization.Migrations`, `Hrot.Common.Scenario`). Removed unused `MapConfigSchemaVersion` constant. Patched `WriteMapConfig` to parse `config.ConfigJson` as `JsonNode`, write `$meta` envelope via `JsonEnvelope.Write`, and set `ConfigurationJson = dom.ToJsonString()`. |
| `Hrot/Subsystems/Hrot.SimHost/NodeConfiguration.cs` | Added using directives (`System.Threading`, `Fdp.Core.Serialization.Migrations.Adapters`). Updated `LoadFrom(string)` signature to `LoadFrom(string, ReadOnlyMigrationAdapter? = null)`. Added adapter branch in method body. Existing catch-all exception swallowing (D-020) preserved. |
| `FDP/ExtDeps/StructEdit/src/StructEdit.Json/EditDocumentJsonSerializer.cs` | `Serialize`: replaced `writer.WriteString("structedit_version", SchemaVersion)` with inline `$meta` object (`docType = "Hrot.StructEdit"`, `schemaVersion = 1`). `Deserialize`: added `hasMetaEnvelope` check; legacy `structedit_version` validation only runs when `$meta` is absent. |

### Bug fix (pre-existing, found during testing)

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Fixed pre-existing `error CS0019` introduced by BATCH-12: `(loader ?? RoadNetworkLoader.LoadFromJson)` failed method-group conversion after `LoadFromJson` gained an optional parameter. Replaced with `(loader ?? (p => RoadNetworkLoader.LoadFromJson(p)))`. |

### Tests

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterMasterContextHandlerTests.cs` | Added `using System.Text.Json; using System.Threading;`. Removed `SchemaVersion = 2` from `SetupScenarioFiles`. Fixed pre-existing path bug in `SetupScenarioFiles` (was writing to `_tempDir/scenarioId/` but CommitLoad reads from `_tempDir/scenarios/scenarioId/`; now writes to correct path). Added `CommitSerializeLocal_ProducesPhase2Envelope` async fact test. |
| `Hrot/Subsystems/Hrot.SimHost.Tests/NodeConfigurationTests.cs` | Added `using Fdp.Core.Serialization.Migrations; using Fdp.Core.Serialization.Migrations.Adapters;`. Added two new tests: `NodeConfiguration_LoadFrom_Phase2Format_WithAdapter_LoadsCorrectly` (T05) and `NodeConfiguration_LoadFrom_WithAdapter_StillReturnsDefaults_WhenAdapterThrows` (T06). |
| `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Json/JsonSerializationTests.cs` | Added 4 new facts to `JsonSerializationTests`: `Serialize_ProducesMetaEnvelope`, `Serialize_DoesNotProduceStructEditVersion`, `Deserialize_AcceptsPhase2Format`, `Deserialize_AcceptsLegacyFormat`. |

---

## Test Results

### Targeted tests (per instructions)

| Filter / Project | Passed | Failed | Notes |
|------------------|--------|--------|-------|
| `Hrot.Orchestrator.Tests` - `CommitSerializeLocal` | 1 | 0 | New test |
| `Hrot.Orchestrator.Tests` - `ClusterMasterContextHandler` | 5 | 0 | 4 existing + 1 new |
| `Hrot.SimHost.Tests` - `NodeConfiguration` | 19 | 0 | 17 existing + 2 new (T05, T06) |
| `StructEdit.Tests` - new Phase 2 filters | 4 | 0 | New tests |

### Full project runs

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| `Hrot.Orchestrator.Tests` (all) | 132 | 5 | 5 pre-existing failures (see below) |
| `Hrot.SimHost.Tests` - NodeConfiguration filter | 19 | 0 | |
| `StructEdit.Tests` (all) | 187 | 1 | 1 pre-existing failure (see below) |

### Pre-existing failures (not caused by BATCH-13)

| Test | Project | Root cause |
|------|---------|------------|
| `ClusterMasterPrefetchTests.PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion` | `Hrot.Orchestrator.Tests` | Pre-existing test failure; 0 lines changed in test file by this batch |
| `ReferenceArchiveHandlerTests.Abort_DeletesPartialFdpFile` | `Hrot.Orchestrator.Tests` | Pre-existing; 0 lines changed |
| `ReferenceArchiveHandlerTests.Commit_ProducesManifestJson_WhenFdpExists` | `Hrot.Orchestrator.Tests` | Pre-existing; 0 lines changed |
| `StorageGatewayTests.PrefetchScenarioAsync_EmptyDirectory_ThrowsInvalidOperation` | `Hrot.Orchestrator.Tests` | Pre-existing; 0 lines changed |
| `StorageProcessManagerTests.ProcessManager_OrchestratorEntry_IsPrepended_ToManifest` | `Hrot.Orchestrator.Tests` | Pre-existing; 0 lines changed |
| `DocumentBuilderTests.Build_CircularReference_CircularFieldIsUnsupported` | `StructEdit.Tests` | Pre-existing; 0 lines changed |

### Full solution build

`Hrot.Blueprints.Tests` (`Hrot.Editor` namespace and `IAnimationTkbQueries` errors) - pre-existing failures unrelated to BATCH-13 (0 lines changed in that project). All 4 target projects and their test projects build cleanly.

---

## Deviations from Instructions

### 1. `SimHostApp.cs` fix (not in scope)
The instructions did not mention `SimHostApp.cs`. However, BATCH-12 introduced a regression: `RoadNetworkLoader.LoadFromJson` gained an optional parameter which broke the method group conversion `loader ?? RoadNetworkLoader.LoadFromJson` in `SimHostApp.cs` (error CS0019). Fixed by replacing with an explicit lambda `(p => RoadNetworkLoader.LoadFromJson(p))`. This was a blocker preventing `Hrot.SimHost` from compiling at all.

### 2. `SetupScenarioFiles` path fix
The instructions said to remove `SchemaVersion = 2` from `SetupScenarioFiles`. While doing so, a pre-existing path bug was discovered: the method wrote files to `_tempDir/scenarioId/` but `CommitLoad` reads from `_tempDir/scenarios/scenarioId/` (using `OrchestrationConstants.ScenariosDirectoryName = "scenarios"`). This caused 3 existing tests to assert `capturedTicks == 0` instead of the expected `99000`. Fixed by updating the `localDir` computation to include `ScenariosDirectoryName`. Since this method was already being modified for BATCH-13, this was a minimal additional change.

### 3. `FromJson` vs `LoadJson`
The instructions showed `session.FromJson(json)` for the StructEdit tests. The actual extension method is `LoadJson` (in `EditSessionJsonExtensions`). Corrected to `session.LoadJson(json)`.

---

## New Debt Items

None discovered.

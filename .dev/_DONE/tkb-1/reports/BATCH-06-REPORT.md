# BATCH-06 Report

**Tasks:** TKB-015, TKB-019, TKB-020, TKB-022  
**Status:** COMPLETE

---

## Files Modified

| File | Change |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Lifecycle/EntityLifecycleModule.cs` | Changed `_translators` to non-readonly; added `SetTranslators(IReadOnlyList<ITkbEntityTranslator>)` method (TKB-022) |
| `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs` | Added `_tkbEntityTranslators` field; added optional `tkbEntityTranslators` parameter; pass to both `GhostPromotionSystem` constructor sites (TKB-022) |
| `Hrot/Network/Hrot.Network.NED/Infrastructure/HrotNodeBuilderReplicationExtensions.cs` | Added `_translators` field to `HrotNodeBuilderWithReplication`; added `WithTranslators` method; pass `tkbEntityTranslators: _translators` in `Build()` (TKB-022) |
| `Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs` | Added `_tkbDb` / `_translators` fields; `BuildContext` creates translator list and captures `ctx.TkbDb`; `RegisterDomainComponents` calls `world.SetSingletonManaged<ITkbDatabase>(_tkbDb!)`; `RegisterSpawningPipeline` calls `elm.SetTranslators` and passes translators to `NetworkSpawningSystem`; `BuildOrchestration` passes `tkbDb: _tkbDb` (TKB-015, TKB-022) |
| `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` | Added `ITkbDatabase? tkbDb = null` optional parameter to `BuildOrchestration`; conditionally registers `TkbLoadClusterStateHandler` after `ReferenceArchiveHandler` (TKB-020) |

## Files Created

| File | Purpose |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs` | `IClusterStateHandler` that intercepts `PrepareLive`/`PrepareEdit`, reads `ScenarioHeader.json` to find TKB name, loads TKB ZIP from local staging root into `ITkbDatabase`; differential cache keyed on (TkbName, file mod time) avoids redundant reloads; falls back to `NedTkbCatalog` when no TkbName and DB is empty (TKB-019) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/TkbLoadClusterStateHandlerTests.cs` | 9 tests covering CanHandle, cache hit/miss, fallback behaviour, FileNotFoundException on missing ZIP (TKB-019) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/TkbDatabaseSingletonTests.cs` | 2 tests verifying `ITkbDatabase` singleton round-trips through `EntityRepository` (TKB-015) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/NedReplicationModuleTranslatorTests.cs` | 1 test verifying `NedReplicationModule` accepts translator list without throwing (TKB-022) |

---

## Test Results

### FDP Toolkits Tests (Fdp.Toolkits.Tests)

```
Test Run Successful.
Passed: 109
```

### Hrot SimHost Tests (Hrot.SimHost.Tests, filter: ~Tkb)

```
Test Run Successful.
Passed: 29
```

All new tests pass:

- `TkbLoadClusterStateHandlerTests.CanHandle_ReturnsTrue_ForPrepareLive` — PASS
- `TkbLoadClusterStateHandlerTests.CanHandle_ReturnsTrue_ForPrepareEdit` — PASS
- `TkbLoadClusterStateHandlerTests.CanHandle_ReturnsFalse_ForOtherOps` — PASS
- `TkbLoadClusterStateHandlerTests.CacheHit_SameTkbAndTimestamp_DoesNotClearDb` — PASS
- `TkbLoadClusterStateHandlerTests.CacheMiss_NameChange_ClearsCalled` — PASS
- `TkbLoadClusterStateHandlerTests.AfterSuccessfulLoad_ActiveTkbNameIsSet` — PASS
- `TkbLoadClusterStateHandlerTests.Fallback_NullTkbName_EmptyDb_RegistersNedCatalog` — PASS
- `TkbLoadClusterStateHandlerTests.Fallback_NullTkbName_PopulatedDb_DoesNotOverwrite` — PASS
- `TkbLoadClusterStateHandlerTests.MissingZip_ThrowsFileNotFoundException` — PASS
- `TkbDatabaseSingletonTests.SetSingletonManaged_TkbDatabase_CanBeRetrievedByInterface` — PASS
- `TkbDatabaseSingletonTests.SetSingletonManaged_TkbDatabase_SameInstanceAfterRegisterAll` — PASS
- `NedReplicationModuleTranslatorTests.NedReplicationModule_WithTranslators_ConstructsWithoutThrow` — PASS

---

## Build Results

### FDP.sln

```
Build succeeded.
0 Error(s)
```

### IOS-IG-SimHost.sln

Build fails due to **22 pre-existing errors** in `Hrot.SimHost.Integration.Tests\Infrastructure\SimHostInstance.cs` (`TkbTemplate.AddComponent` / `TkbTemplate.AddManagedComponent` API was removed in a prior batch). Confirmed pre-existing via `git stash` + build test — same errors appear without BATCH-06 changes. Not caused by this batch.

---

## Deviations from Instructions

None. All changes match DESIGN.md exactly.

### Notes

- `BindReplicationParticipant()` in `HrotNodeBuilderReplicationExtensions.cs` was intentionally not modified (not in scope).
- `IgNodeBootstrapper.RegisterDomainComponents` was intentionally not modified (not in scope).
- Test helper `WriteScenarioHeader` and `CreateMinimalTkbZip` use `new UTF8Encoding(false)` (no BOM) because `Utf8JsonReader` does not skip BOM automatically.

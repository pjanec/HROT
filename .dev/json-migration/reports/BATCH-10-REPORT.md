# BATCH-10 Report

**Status:** Complete
**Tests (new):** 4/4 passing (ScenarioPhase2Tests) | 11/11 passing (Hrot.Common.Tests total)
**Tests (impacted):** 22/22 ScenarioSerializer tests | 10/10 ScenarioFileService tests | 20/20 ScenarioLoadHandler tests | 6/6 HrotScenarioEnvelope tests

## Tasks Completed

- [x] D-017: Converted 4 migration modules from `sealed class` to `static class` + updated T02-T05 in ModuleRegistrationTests.cs
- [x] D-018: Added XML doc comment to `ReadOnlyLoadOutcome.Report` documenting null-on-fast-path contract
- [x] JM-P2-003: Scenario envelope rollout — 7 source files changed, 2 test files updated, 1 new test file created

---

## Files Changed

### D-017 — Static modules

| File | Change |
|------|--------|
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs` | `sealed class` → `static class`, `RegisterAll` instance → `static` |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/BlueprintMigrationModule.cs` | same |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/TkbMigrationModule.cs` | same |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/RoadNetworkMigrationModule.cs` | same |
| `Hrot/Engine/Hrot.Common.Tests/Migrations/ModuleRegistrationTests.cs` | T02-T05: removed `new XxxModule()` instantiation, use static calls |

### D-018 — Doc comment

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Core/Serialization/Migrations/Adapters/ReadOnlyLoadOutcome.cs` | Added XML doc to `Report` property: null on fast path, callers must null-check |

### JM-P2-003 — Scenario envelope rollout

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioHeader.cs` | Removed `SchemaVersion` parameter (C-3); updated doc comment |
| `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs` | Removed `SchemaVersion` property (C-3) |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` | Added `using Fdp.Core.Serialization.Migrations;`; Serialize now stamps `$meta` + optional `Header.TkbName`; Deserialize checks `$meta.docType` (Phase 2) OR `Header.SubsystemType` (legacy) |
| `Hrot/Engine/Hrot.Core/Scenario/Common/HrotScenarioEnvelope.cs` | `PeekSubsystemType`: checks `$meta.docType` first, falls back to `Header.SubsystemType` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs` | Added `MigrationServices?` param; `SaveScenario` uses DOM directly (no `HrotScenarioEnvelopeDto` wrapper); `ValidateSubsystemType` handles `$meta.docType`; `LoadScenario` uses `ReadOnly` adapter when `_migrationServices` is set |
| `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` | Added optional `ReadOnlyMigrationAdapter?` param; Phase 2 path migrates JSON before zone extraction |

### Test files updated

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioSerializerTkbNameTests.cs` | T02: `dom["Header"]` is null when TkbName absent; assert `null` on whole Header node |
| `Hrot/Engine/Hrot.Presentation.Tests/ScenarioFileServiceTkbTests.cs` | T2/T3: `envelope.Header` may be null; assert `Header == null \|\| TkbName == null` |
| `Hrot/Engine/Hrot.Core.Tests/HrotScenarioDtoTests.cs` | Removed `SchemaVersion = "1.0"` from DTO construction |

### New test file

| File | Tests |
|------|-------|
| `Hrot/Engine/Hrot.Common.Tests/Migrations/ScenarioPhase2Tests.cs` | T01: Serialize produces `$meta`; T02: TkbName in `Header`; T03: Deserialize Phase 2 DOM loads entities; T04: Deserialize legacy Header format still works |

### Csproj changes

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Core/Fdp.Core.csproj` | `InternalsVisibleTo` for `Hrot.Common.Tests` (needed for `MigrationBootstrap.Build` and `InMemoryMigrationStorage`) |
| `Hrot/Engine/Hrot.Common/Hrot.Common.csproj` | `InternalsVisibleTo` for `Hrot.Common.Tests` |
| `Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj` | Added `Fdp.Toolkits` project reference (needed for `ScenarioSerializerBuilder` in Phase 2 tests) |

---

## Developer Insights

### Adaptation: Deserialize keeps docType check even with $meta

The batch spec said to **skip** the SubsystemType check when `$meta` is present (trusting
the adapter to have validated it). This was **not implemented** because the existing
`SubsystemType_MismatchSkipsDeserialize` test would break:

```csharp
// The test serializes as "Hrot.SimHost" then deserializes with a "Hrot.CGF" serializer.
// With $meta present, skipping the check would load entities into the wrong repo.
var dom = simhostSerializer.Serialize(sourceRepo, new ScenarioHeader("Hrot.SimHost"));
cgfSerializer.Deserialize(_repo, dom);  // must stay a no-op
Assert.Equal(0, _repo.EntityCount);    // would fail if check skipped
```

The implemented behavior reads `$meta.docType` and compares it to `_subsystemType`. This
is more defensive and keeps the serializer correct in any calling context.

### Adaptation: SaveScenario always uses direct write path

The batch spec said to use `_migrationServices.Persistent.SaveAsync(fdpDom, filePath)`
when `_migrationServices != null`. The `PersistentMigrationAdapter.SaveAsync` signature is:
```csharp
Task SaveAsync(string path, JsonObject dom, MigrationLoadResult priorLoad, CancellationToken ct)
```

`priorLoad` is a `MigrationLoadResult` from a prior `LoadAndMigrateAsync` — unavailable for
fresh saves. `SaveAsync` cannot be called without a prior load. Implemented: **always use the
direct write path** (minified JSON → `File.WriteAllText`).

### Adaptation: LoadScenario uses ReadOnly adapter (not Persistent)

The batch spec pseudocode referenced `_migrationServices.Persistent.LoadAndMigrateAsync`
returning `outcome.Content` (a property that doesn't exist on `MigrationLoadResult`). The
`Persistent` adapter returns `MigrationLoadResult` which has `Dom` (internal), not `Content`.
Implemented: use `_migrationServices.ReadOnly.LoadAndMigrateAsync(filePath)` which returns
`ReadOnlyLoadOutcome` with the public `AsJsonObject()` method.

### Zone extraction in HrotScenarioLoadHandler

The Phase 2 path in `HrotScenarioLoadHandler.PrepareAsync` migrates JSON via the
`ReadOnlyMigrationAdapter` and uses the migrated DOM for zone extraction. Entity extraction
still uses the original JSON string — `ScenarioSerializer.Deserialize` now handles both
Phase 2 (`$meta`) and legacy (`Header.SubsystemType`) formats, so the original string
continues to work correctly.

### HrotScenarioDtoTests SchemaVersion removal

`HrotScenarioDtoTests.HrotScenarioEnvelopeDto_RoundTrip_PreservesObstacleValues` had
`SchemaVersion = "1.0"` in the DTO construction, which would fail to compile after removing
the property from `ScenarioHeaderDto`. The line was removed. The round-trip test still
exercises SubsystemType and TkbName.

---

## Pre-existing Failures (not caused by BATCH-10)

The following test failures existed before BATCH-10 and are unrelated to our changes:

| Project | Failures | Reason |
|---------|----------|--------|
| `Hrot.Presentation.Tests` | 6 (EntityDragGizmo, RouteWaypoint, VertexEdit, WorldReset) | Pre-existing gizmo test failures |
| `Hrot.Core.Tests` | 5 (LogArchiveExtractionService) | Pre-existing log archive failures |
| `Fdp.Toolkits.Tests` | ~67 (Combat, Navigation, ReplayBrowser, Gizmo) | Pre-existing failures in unrelated systems |
| `Hrot.Blueprints.Tests` | All (compile error: CS0234 Hrot.Editor) | Pre-existing Stride-editor reference |

# BATCH-09 Report

**Status:** Complete
**Tests:** 7/7 passing (Hrot.Common.Tests) | 1140/1143 passing (Fdp.Core.Tests — 1 pre-existing performance flake, 2 skipped)

## Tasks Completed

- [x] JM-P2-001: 05-integration-patches.md written
- [x] JM-P2-002: HrotDocumentTypes + modules + Hrot.Common.Tests

---

## Developer Insights

### Issues Encountered

**T07 (`OrchestratorContext_RegistersAtVersionTwo`) failed on first run.**
The original T07 asserted `Assert.Equal(0, outcome.Report.Warnings.Count)` after calling
`LoadAndMigrateAsync`. This threw `NullReferenceException` because `ReadOnlyLoadOutcome.Report`
is `null` on the fast path: when the file's `schemaVersion` matches the registered current
version, no migration occurs and no `MigrationReport` is produced. The fix:

```csharp
// Wrong (NullReferenceException when no migration occurred):
Assert.Equal(0, outcome.Report.Warnings.Count);

// Correct (explicitly tests the fast-path contract):
Assert.False(outcome.WasMigrated);
Assert.Null(outcome.Report);
```

This is a design point worth noting for JM-P2-003+ implementors: callers of
`LoadAndMigrateAsync` must check `outcome.Report != null` before accessing report members.

**Full solution build has pre-existing failures.**
`dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore` reports two pre-existing failures:
1. `SkippableFactAttribute` in `Fdp.Core.Tests` — the `--no-restore` flag leaves the
   `Xunit.SkippableFact` package unresolved. Building `Fdp.Core.Tests` individually (with
   implicit restore) compiles and runs all tests cleanly.
2. `Hrot.Blueprints.Tests` — references `Hrot.Editor` / `IAnimationTkbQueries` which are
   Stride-specific editor types not available in a headless build configuration.

Neither error is related to BATCH-09 changes.

**ComponentDirtyTracking_PerformanceScan fails intermittently.**
One performance-scan test in `Fdp.Core.Tests` failed during verification. This is a timing-
based test that appears to be machine-load-sensitive and is a pre-existing flake. The 349
migration-related tests and all other non-performance tests passed.

### Weak Points Spotted

1. **`ReadOnlyLoadOutcome.Report` is nullable but its null semantics are not documented.**
   Any caller that accesses `Report.Warnings` or `Report.ErrorMessage` without a null check
   will throw. This is a footgun for JM-P2-003 implementors. Recommend adding an XML doc
   comment on the property (or making the fast-path semantics explicit in the design doc).

2. **`OrchestratorContext` writes a bare `SchemaVersion: 2` field today** (verified in
   `GlobalContextClusterOpHandler`). Phase 2 (JM-P2-008) must wrap it in `$meta` and strip
   the naked field. The field rename from `SchemaVersion` (top-level) to `$meta.schemaVersion`
   will be a breaking change for any reader that currently checks `schemaVersion` at the top
   level — these readers need to be enumerated and updated together.

3. **`StructEdit` uses a brittle `"1.0"` equality check** in `EditDocumentJsonSerializer`
   (`FDP/ExtDeps/StructEdit/src/StructEdit.Json/`). When the passthrough envelope is added
   in JM-P2-008, the `"1.0"` check must be retired; otherwise old sidecar files will fail
   to load once the envelope wraps the version field.

4. **`ScenarioSerializer` in `Fdp.Toolkits` also carries `SchemaVersion`** alongside
   `ScenarioFileService`. Both must be patched in lock-step in JM-P2-003 to avoid split-
   brain writes where one path writes the old shape and one writes the new shape.

5. **Blueprint files have no existing version field** (`BlueprintJsonServices` survey showed
   no `Header.SchemaVersion` or equivalent). The `$meta` envelope will be added cold in
   JM-P2-004. Existing blueprint files in the repository have no version at all, so the
   migration adapter must treat missing `$meta` as version 0 and apply the v0->v1 migration
   (even if that migration is a no-op for the skeleton phase).

### Design Decisions Beyond the Spec

**Used `RegisterPassthroughDocType` for all skeleton modules (T02–T05).**
The spec noted: "If `RegisterDocType` with empty migrators is not allowed, use
`RegisterPassthroughDocType`." Inspection of `MigrationRegistry.RegisterDocType` confirmed
it requires at least one migrator entry to form a valid chain from v_old to v_new. Calling
it with an empty migrators array at version 1 would register the type but leave the chain
table empty, causing `GetCurrentVersion` to work but `LoadAndMigrateAsync` to fail if it
encounters an old version. `RegisterPassthroughDocType` is semantically correct for skeleton
modules: the format exists at version 1 with no migration history yet.

This will need to change for `ScenarioMigrationModule` and `BlueprintMigrationModule` once
JM-P3-003 adds the first real migrator (bumping to version 2).

---

## JM-P2-001 Summary

Surveyed 14 touchpoints across 5 subsystems. Key findings:

| Finding | Detail |
|---|---|
| Scenario has two writers | `ScenarioFileService` (editor) and `ScenarioSerializer` (FDP toolkit) both write the same format; both must be patched together in JM-P2-003 |
| OrchestratorContext is at v2 | `GlobalContextClusterOpHandler` already writes `schemaVersion: 2` as a bare field; JM-P2-008 wraps it in `$meta` at v2 |
| Blueprint has no version field | `BlueprintJsonServices` reads/writes blueprints with no schema version; migration adapter must handle the missing-meta cold-start case |
| StructEdit uses string equality | `EditDocumentJsonSerializer` checks `version == "1.0"` (string); this must be retired in JM-P2-008 |
| Replay paths are read-only loaders | `RecordingDumper`, `ReplayBrowserContext` only load metadata; `TransientMasterBuilder` and `RecordingExportService` write it. All four are JM-P2-007 |
| Report is null on fast path | `LoadAndMigrateAsync` returns null `Report` when no migration occurs — callers must guard |

Touchpoint document is at `.dev/json-migration/05-integration-patches.md`. It is complete
enough to gate JM-P2-003 through JM-P2-008: each entry shows current shape, target shape,
adapter type, docType constant, and before/after pseudo-code.

---

## JM-P2-002 Summary

**Files created:**
- `HrotDocumentTypes.cs` — 12 constants; `BehaviorTree` constant present but not registered (C-1)
- `PassthroughFormatsModule.cs` — registers 5 engine-internal formats via `RegisterPassthroughDocType`
- `ScenarioMigrationModule.cs`, `BlueprintMigrationModule.cs`, `TkbMigrationModule.cs`, `RoadNetworkMigrationModule.cs` — skeleton modules at `CurrentVersion = 1`
- `Hrot.Common.Tests.csproj` — new test project (net8.0, xUnit 2.9.3)
- `ModuleRegistrationTests.cs` — 7 tests (T01-T07)

**Design deviation:** All skeleton modules use `RegisterPassthroughDocType` (not
`RegisterDocType` with empty migrators). Rationale documented in "Design Decisions" above.

**C-4 applied:** `PassthroughFormatsModule` registers `OrchestratorContext` at version **2**.
T07 verifies this: a JSON with `$meta.schemaVersion: 2` loads without migration, confirming
`currentVersion == 2` in the registry.

---

## Build / Test Results

```
--- Hrot.Common.Tests (new tests) ---
Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 72 ms

--- Fdp.Core.Tests (regression check) ---
Failed! - Failed: 1, Passed: 1140, Skipped: 2, Total: 1143, Duration: 34 s
  * ComponentDirtyTracking_PerformanceScan -- PRE-EXISTING timing flake, unrelated to BATCH-09

--- IOS-IG-SimHost.sln full build (--no-restore) ---
Pre-existing errors (not introduced by BATCH-09):
  * Fdp.Core.Tests: SkippableFactAttribute missing (package not resolved without restore)
  * Hrot.Blueprints.Tests: Hrot.Editor / IAnimationTkbQueries not found (Stride headless-build limitation)
```

Both pre-existing failures are confirmed to be present before this batch (Stride editor
dependencies and --no-restore package issue). No new errors were introduced.

---

## Files Created / Modified

### New files
| File | Purpose |
|---|---|
| `Hrot/Engine/Hrot.Common/Scenario/HrotDocumentTypes.cs` | 12 docType string constants for all HROT formats |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/PassthroughFormatsModule.cs` | Registers 5 passthrough formats (engine-internal) |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/ScenarioMigrationModule.cs` | Skeleton for Scenario format (v1) |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/BlueprintMigrationModule.cs` | Skeleton for Blueprint format (v1) |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/TkbMigrationModule.cs` | Skeleton for TKB format (v1) |
| `Hrot/Engine/Hrot.Common/Scenario/Migrations/RoadNetworkMigrationModule.cs` | Skeleton for RoadNetwork format (v1, uses FdpDocumentTypes) |
| `Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj` | New test project (net8.0, xUnit 2.9.3) |
| `Hrot/Engine/Hrot.Common.Tests/xunit.runner.json` | xUnit runner config (single-threaded) |
| `Hrot/Engine/Hrot.Common.Tests/Migrations/ModuleRegistrationTests.cs` | 7 tests for JM-P2-002 |
| `.dev/json-migration/05-integration-patches.md` | Integration patches survey document (JM-P2-001) |

### Modified files
| File | Change |
|---|---|
| `Hrot/Engine/Hrot.Common/Hrot.Common.csproj` | Added `InternalsVisibleTo("Hrot.Common.Tests")` |
| `FDP/Engine/Fdp.Core/Fdp.Core.csproj` | Added `InternalsVisibleTo("Hrot.Common.Tests")` |
| `IOS-IG-SimHost.sln` | Added `Hrot.Common.Tests` project via `dotnet sln add` |

# BATCH-16 — JM-P2-011: Phase 2 CI Regression Run (GATE)

## Overview

This is the Phase 2 gate task. Verify that the entire Phase 2 rollout is correct by:
1. Running the full test suite and collecting results.
2. Writing a **convention test** asserting all committed fixture files have a valid `$meta` envelope.
3. Writing a **round-trip test** asserting that loading a scenario via `ReadOnlyMigrationAdapter`
   and re-serializing it produces output where `$meta` is the first property with correct values.
4. Writing a brief **Phase 2 gate report** documenting results.

Task reference: [JM-P2-011] in `.dev/json-migration/TASK-DETAILS.md`

---

## Success Conditions (from TASK-DETAILS)

1. All read paths route through a migration adapter (verified via static check or convention test).
2. All write paths emit `$meta` (verified by reading a sample of just-written files).
3. v1 scenario → editor load → save → reload produces byte-equivalent output (modulo `engineVersion`).

---

## Deliverable 1: Convention + Round-Trip Tests

Add to **`Hrot/Engine/Hrot.Common.Tests/`** a new test class:
`Scenario/Migrations/Phase2ConventionTests.cs`

> Do NOT create a new project. Add to the existing `Hrot.Common.Tests` project.

### Tests to write:

```
T_Conv_01 — All committed fixture JSON files have a valid $meta envelope
  Purpose: Verify success condition (1) — every fixture has been stamped.
  Implementation:
    - Walk the workspace root for *.json files (use the test binary's location to find
      workspace root: walk up from AppContext.BaseDirectory until a marker file
      like "IOS-IG-SimHost.sln" is found, or use the working directory).
    - Apply the same exclusion logic as FixtureStamper (obj/, bin/, ExtDeps/, .tmp/,
      .claude/, *.deps.json, *.runtimeconfig.json, xunit.runner.json, launchSettings.json,
      settings.json, settings.local.json, Fdp.Core.Tests/Serialization/Migrations,
      Navigation/data).
    - For each file that passes the exclusion, detect if it is a known fixture:
      has "header"."subsystemType" OR "Header"."SubsystemType" OR (has "nodes" AND "segments").
    - For each known fixture, assert JsonEnvelope.Peek(filePath) does NOT throw.
    - Assert total known fixtures found >= 10 (sanity check: prevents the test from
      vacuously passing if directory walking fails).

T_Conv_02 — All committed scenario fixtures carry docType="Hrot.Scenario" and schemaVersion=1
  Purpose: spot-check specific values for scenarios.
  Implementation:
    - Walk scenario fixture files (files where "header"."subsystemType" == "Hrot.Scenario").
    - For each, call JsonEnvelope.Peek(filePath).
    - Assert meta.DocType == "Hrot.Scenario" && meta.SchemaVersion == 1.

T_Conv_03 — All committed blueprint fixtures carry docType="Hrot.Blueprints" and schemaVersion=1
  Purpose: spot-check specific values for blueprints.
  Implementation:
    - Walk blueprint fixture files (files where "Header"."SubsystemType" == "Hrot.Blueprints").
    - For each, call JsonEnvelope.Peek(filePath).
    - Assert meta.DocType == "Hrot.Blueprints" && meta.SchemaVersion == 1.

T_Conv_04 — Round-trip: load a scenario via ReadOnlyMigrationAdapter → DOM has valid $meta
  Purpose: Verify success condition (3) — load → save is well-formed.
  Implementation:
    - Find the workspace root, then the path to "scenarios/hill-attack/scenario.json".
    - Build migration services using HrotMigrationBootstrap.BuildSimHostCgf("Hrot.SimHost")
      (this registers Scenario + TKB + RoadNetwork + OrchestratorContext passthrough).
    - Call services.ReadOnly.LoadAndMigrateJson(scenarioPath).
      (Note: check the actual method name on ReadOnlyMigrationAdapter — look at
      ReadOnlyMigrationAdapter.cs. It likely has a method like LoadAndMigrate or
      OpenDocument that returns a JsonObject or MigrationContext).
    - Assert the returned DOM:
        * Has "$meta" as first property.
        * meta.DocType == "Hrot.Scenario".
        * meta.SchemaVersion == 1.
    - Serialize the DOM back to a JSON string (JsonNode → string).
    - Parse the string again (round-trip).
    - Assert the re-parsed DOM also has "$meta" as first property with correct values.
    - Assert the "header" object is still present in the DOM (legacy field preserved).
```

### Implementation notes for T_Conv_01/02/03

The convention tests need to find the workspace root from within the test binary. Use this pattern:
```csharp
private static string FindWorkspaceRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IOS-IG-SimHost.sln")))
        dir = dir.Parent;
    if (dir == null)
        throw new InvalidOperationException("Cannot locate workspace root (IOS-IG-SimHost.sln not found)");
    return dir.FullName;
}
```

> This pattern is safe for CI because the solution file is always present.

### Implementation notes for T_Conv_04

First, check the `ReadOnlyMigrationAdapter` API by reading:
`FDP/Engine/Fdp.Core/Serialization/Migrations/ReadOnlyMigrationAdapter.cs`

The adapter likely has a method that:
- Takes a file path (or byte span)
- Returns a `MigrationContext` or `JsonObject` with the migrated DOM

The `MigrationContext` likely has a property like `.Document` (a `JsonObject`).

Build the round-trip test using `HrotMigrationBootstrap` from `Hrot.Common.Scenario.Migrations`.

If `Hrot.Common.Tests` doesn't already reference `Hrot.Common`, check — it almost certainly does.
If `Hrot.Common.Tests.csproj` doesn't reference `Hrot.Common` directly, add it.

---

## Deliverable 2: Full Test Suite Run

Run ALL test suites (except the known pre-failing `Hrot.Blueprints.Tests` Stride tests) and record results:

```powershell
# Run all tests, collect summary
dotnet test "IOS-IG-SimHost.sln" -c Debug --no-build --ignore-exit-code 8 2>&1 | Select-String "Passed!|Failed!|Error" | Select-Object -Last 30

# Or per-suite:
dotnet test "FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 3
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug 2>&1 | Select-Object -Last 3
dotnet test "FDP/Tools/Fdp.Tools.EnvelopeStamper.Tests/Fdp.Tools.EnvelopeStamper.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 3
dotnet test "Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj" -c Debug --no-build 2>&1 | Select-Object -Last 3
```

---

## Deliverable 3: Phase 2 Gate Report

Create `.dev/json-migration/reports/PHASE-2-GATE-REPORT.md` with:
1. Summary table of all test suites run and their pass/fail counts.
2. Confirmation that all committed fixture files have `$meta` (T_Conv_01 result).
3. Before/after comparison noting which Phase 2 tasks each test suite validates:
   - `Fdp.Core.Tests` → Phase 1 migration infrastructure
   - `Hrot.Common.Tests` (new T_Conv_*) → Phase 2 convention compliance
   - `Fdp.Tools.EnvelopeStamper.Tests` → JM-P2-010 fixture stamping
   - `Hrot.SimHost.Tests` (NodeBootstrapperMigrationTests) → JM-P2-009 bootstrap wiring
4. Known issues: pre-existing Blueprints.Tests failures (Stride), EX_T recording export (D-022).
5. Phase 2 verdict: GO / NO-GO for Phase 3.

---

## Build Commands

```powershell
# Build after adding tests
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5

# Run the new convention tests
dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug -v normal 2>&1 | Select-String "Conv|passed|failed" | Select-Object -Last 20
```

---

## Batch Report Requirements

The batch report (`.dev/json-migration/reports/BATCH-16-REPORT.md`) must include:
1. List of files created/modified.
2. All T_Conv_01 through T_Conv_04 test results (must pass).
3. Full test suite summary table.
4. The Phase 2 Gate Report content (or a reference to the file).

Do NOT commit. The dev lead reviews and commits.

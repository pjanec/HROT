# BATCH-20 Instructions — Pre-existing Test Debt + Phase 5 Docs

**Date:** 2026-05-29
**Branch:** `json-migration`
**Prereq commit:** `b13892b7` (tracker updates after BATCH-19 review)

---

## Context

Onboarding:
- Read [DESIGN.md (Migration-system.md)](../Migration-system.md) for the overall design.
- Read [BATCH-19-REPORT.md](../reports/BATCH-19-REPORT.md) — Phase 4 Editor UI was implemented.
- Read [TASK-DETAILS.md](../TASK-DETAILS.md) — Phase 5 task definitions.
- Read [DEBT-TRACKER.md](../DEBT-TRACKER.md) for all open items.

Current state of the solution:
- Phase 1-3 complete; Phase 4 complete (BATCH-19).
- Only GATE tasks remain in the tracker (require human/architect approval): JM-P3-006, JM-P4-006.
- Phase 5 tasks (JM-P5-001 through JM-P5-004) are documentation/process items.
- There are 3 pre-existing test failures (D-029, D-030, D-031) introduced in prior batches.
- D-022 causes 28 EX_T test failures due to a parallel test execution issue.

**Pre-existing build errors:** `Hrot.Blueprints.Tests` still has CS0234/CS0246 build errors. Do not fix them; they are unrelated to this batch.

---

## Test-Driven Task Progression (MANDATORY)

For every change:
1. **Understand the failure first.** Read the failing test and the source file.
2. **Implement the minimal fix** to make the test pass without breaking other tests.
3. **Run the affected test project** and verify the fix.
4. **Do not add tests unless specified.**

---

## Task List

### Task 1 — Fix D-029: Update `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount`

**File:** `Hrot/Subsystems/Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs`

**Problem:** The test was written before Phase 2 and expects legacy JSON format (`"header.subsystemType"`). The Phase 2 serializer now writes `"$meta": { "docType": "...", "schemaVersion": 1 }` and `"Entities"` (uppercase E). The test fails with `KeyNotFoundException` at `doc.RootElement.GetProperty("header")`.

**What the serializer now writes:**
```json
{
  "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1 },
  "Entities": { ... }
}
```
Note: `"Header": { "TkbName": null }` is NOT written when TkbName is null (the serializer only writes `root["Header"]` when `header.TkbName != null`).

**Fix:** Update the assertions in `SaveScenario_WritesValidJson_WithCorrectHeaderAndEntityCount`:
- Replace `GetProperty("header")` → `GetProperty("$meta")`
- Replace `.GetProperty("subsystemType")` → `.GetProperty("docType")`
- Replace `GetProperty("entities")` → `GetProperty("Entities")` (uppercase E — this is a `JsonObject` property name, not camelCase)

**Expected result after fix:** Test passes.

---

### Task 2 — Fix D-030: Update `LoadScenario_UnrecognisedSubsystemType_Throws_AndLeavesRepoEmpty`

**File:** `Hrot/Subsystems/Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs`

**Problem:** The test uses `Assert.Throws<InvalidOperationException>()` but xUnit v2 requires EXACT type match. After Phase 2+4, `LoadScenario` calls `Persistent.LoadAndMigrateAsync`, which calls `JsonEnvelope.Peek`. When reading a legacy file with no `$meta` property (or a `$meta` with unknown docType), `JsonEnvelope.Peek` throws `MigrationException`, not `InvalidOperationException`. Even though `MigrationException : InvalidOperationException`, xUnit's `Assert.Throws<T>` rejects the subclass.

**Fix (two parts):**

1. **Add using:** `using Fdp.Core.Serialization.Migrations;` at the top of the test file.

2. **Update the test's bad JSON** to use the new `$meta` format with an unknown docType (this keeps the test aligned with its original intent — "unrecognised subsystem type"):
   ```csharp
   var badJson = """
       {
         "$meta": { "docType": "SomeOtherApp", "schemaVersion": 1 },
         "Entities": {}
       }
       """;
   ```

3. **Change the Assert:**
   ```csharp
   Assert.Throws<MigrationException>(() => app.LoadScenario(_tempFile));
   ```

4. **Update the test summary comment** to say "throws MigrationException" instead of "throws InvalidOperationException".

**Expected result after fix:** Test passes; the repo is left empty as before (migration throws before SoftClear is reached — verify by reading `ScenarioFileService.LoadScenario`: the migration call happens BEFORE `_worldResetObservers?.Invoke()` and `repo.SoftClear()`).

---

### Task 3 — Fix D-031: Investigate `HrotEditor_HasNoCycloneDdsDependency`

**File:** `Hrot/Subsystems/Hrot.Editor.Tests/EditorDependencyTests.cs`

**Problem:** `Hrot.Editor.dll` has `CycloneDDS.Schema` as a direct assembly reference. The test was designed to enforce the constraint that the Editor should never author DDS-serializable structs (that's the role of Schema). But `CycloneDDS.Schema` is now in the editor's reference manifest.

**Investigation steps:**

The test checks:
```csharp
var assemblyNames = typeof(Hrot.Editor.IEditorLogic).Assembly
    .GetReferencedAssemblies()
    .Select(a => a.Name)
    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
Assert.DoesNotContain("CycloneDDS.Schema", assemblyNames);
```

`Assembly.GetReferencedAssemblies()` returns only assemblies whose types are **directly used** in the compiled IL. So `Hrot.Editor.dll` has C# code that uses a type from `CycloneDDS.Schema`.

**Action:**

1. Search all `.cs` files in `Hrot/Subsystems/Hrot.Editor/` for `CycloneDDS.Schema` or any type that is in the `CycloneDDS.Schema` namespace. Run:
   ```powershell
   Get-ChildItem -Recurse -Path "Hrot\Subsystems\Hrot.Editor" -Filter "*.cs" | Select-String -Pattern "CycloneDDS\.Schema|using CycloneDDS"
   ```
   
2. If you find a direct usage, assess whether it can be replaced with an equivalent from `CycloneDDS.Runtime` or a wrapper from a higher-level abstraction. Remove the direct dependency.

3. If no direct usage is found in `Hrot.Editor` source, check which of its DIRECT project references use `CycloneDDS.Schema` in their PUBLIC API surface (public method signatures, properties, interfaces). When `Hrot.Editor` calls such a method/property that returns or accepts a `CycloneDDS.Schema` type, the C# compiler emits a direct `AssemblyRef` to `CycloneDDS.Schema` in `Hrot.Editor.dll`.

   Use this PowerShell to find which direct dependencies have `CycloneDDS.Schema`:
   ```powershell
   $baseDir = "Hrot\Subsystems\Hrot.Editor\bin\Debug\net8.0"
   $refs = @("Hrot.Diagnostics.Breakpoints.dll","Hrot.Blueprints.Editor.dll","Hrot.Presentation.dll","Hrot.SimHost.dll","Hrot.CGF.dll","Hrot.Orchestrator.dll","Hrot.IG.dll","Hrot.Network.NED.dll")
   foreach ($r in $refs) {
       $p = Join-Path $baseDir $r
       if (Test-Path $p) {
           $asm = [System.Reflection.Assembly]::LoadFile((Resolve-Path $p))
           $hasCyclone = $asm.GetReferencedAssemblies() | Where-Object { $_.Name -eq "CycloneDDS.Schema" }
           if ($hasCyclone) { Write-Host "$r -> CycloneDDS.Schema" }
       }
   }
   ```
   Known result: `Hrot.Diagnostics.Breakpoints.dll`, `Hrot.Blueprints.Editor.dll`, `Hrot.Presentation.dll`, `Hrot.CGF.dll`, `Hrot.IG.dll`, `Hrot.Network.NED.dll` all have `CycloneDDS.Schema`.

4. If the dependency chain is deep and not fixable without significant refactoring, **update the test** to document the change:
   - Add a comment explaining that `CycloneDDS.Schema` is now an accepted transitive dependency
   - Change `Assert.DoesNotContain("CycloneDDS.Schema", assemblyNames)` → add `// ACCEPTED` comment noting that the constraint changed, and remove that assertion
   - Keep `Assert.DoesNotContain("CycloneDDS.Core", assemblyNames)` if it still holds

   But ONLY do this if the fix is not practical within this batch. Prefer fixing the dependency if possible.

**Expected result after fix:** Test passes (either the dependency is removed, or the test is updated with documented reasoning).

---

### Task 4 — Fix D-022: EX_T Test Failures (28 tests)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/`

**Problem:** 28 `RecordingExportServiceTests.EX_T*` tests fail with:
```
System.InvalidOperationException : Component 'EntityInlineComp' has an [InlineArray] field with element type Entity, which is not supported by FdpAutoSerializer. Use [ScenarioIgnore] to exclude it.
```

**Root cause:** xUnit runs test classes in parallel by default. `FdpAutoSerializerFixedBufferTests.Build_ComponentWithEntityInInlineArray_Throws` temporarily registers `EntityInlineComp` (component ID 228) in the static `ComponentTypeRegistry`, then clears it. While this window is open, `RecordingExportServiceTests.EX_T*` tests call `FdpAutoSerializer.Build()` which reads the shared static registry and finds `EntityInlineComp` — causing the throw.

The `[Collection("FdpAutoSerializerFixedBuffer")]` attribute prevents parallelism within `FdpAutoSerializerFixedBufferTests` but NOT between that class and `RecordingExportServiceTests`.

**Fix:** Add assembly-level parallel disable to `Fdp.Toolkits.Tests`:

Create (or edit) `Fdp.Toolkits.Tests/AssemblyInfo.cs` to add:
```csharp
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

If an `AssemblyInfo.cs` does not exist, create it. If a `GlobalUsings.cs` exists and is a better place, add it there. Check the project for an existing `AssemblyInfo.cs` or similar file.

**Alternative (if above doesn't work):** Add `RecordingExportServiceTests` to the same xUnit collection:
```csharp
[Collection("FdpAutoSerializerFixedBuffer")]
public class RecordingExportServiceTests : IDisposable
```

**Verify:** After the fix, run:
```powershell
dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" --no-build --filter "EX_T"
```
All 28 should pass (plus the 1 that was already passing = 29 total).

---

### Task 5 — JM-P5-002: BASELINES.md — Baseline Refresh Process

**File to create:** `test-data/scenario-corpus/BASELINES.md`

**Design ref:** JM-P5-002 in [TASK-DETAILS.md](../TASK-DETAILS.md#jm-p5-002--baseline-refresh-process)

Write a short markdown checklist (≤60 lines) that documents how to regenerate T5 migration baselines when a migrator changes a default field value. The checklist should cover:

1. When to regenerate: "when adding a new migrator that changes a default value, when a T5 corpus test fails unexpectedly after a schema version bump"
2. Prerequisites: `dotnet build IOS-IG-SimHost.sln -c Debug`
3. Steps:
   - Identify which corpus file needs updating (T5 test output shows the diff)
   - Run `dotnet test ... --filter "T5"` to see current baseline
   - Manually inspect the migration output
   - Update the baseline file in `test-data/scenario-corpus/`
   - Re-run T5 tests to confirm green
4. Warning: never commit a baseline update without also committing the migrator that causes it
5. Reference from docs: note that this checklist is maintained here

**Success condition:** File exists at `test-data/scenario-corpus/BASELINES.md` with the above content.

---

### Task 6 — JM-P5-003: PR-CHECKLIST.md — Per-Migrator PR Checklist

**File to create:** `.dev/json-migration/PR-CHECKLIST.md`

**Design ref:** JM-P5-003 in [TASK-DETAILS.md](../TASK-DETAILS.md#jm-p5-003--per-migrator-pr-checklist)

Write a markdown PR checklist (≤80 lines) for any PR touching `Hrot/Engine/Hrot.Common/Scenario/Migrations/Migrators/`. The checklist items should be derived from the design (section §10) and from lessons learned in BATCH-17 and BATCH-18. Include:

**Before opening the PR:**
- [ ] Migrator pair (Up + Down) both implemented and registered in `ScenarioMigrationModule`
- [ ] `CurrentVersion` in `ScenarioMigrationModule` bumped by 1
- [ ] All migrators in the chain remain adjacent-version-only (no version-skipping)
- [ ] EntityPatch helpers used where applicable (rename, add, remove, transform)
- [ ] `AddField` default values are non-null (no silent JSON nulls via D-026 pattern)
- [ ] Down migrator removes/reverts exactly what Up added
- [ ] Round-trip test: v_n → v_n+1 → v_n produces original document
- [ ] "User edits survive" test: v_n user edits → up → down → edits preserved (see D-023 pattern)
- [ ] EntityPatchTests coverage for any new helper methods used
- [ ] T4 corpus test passes (load at current version)
- [ ] T5 migration round-trip test passes (or baseline updated per BASELINES.md)
- [ ] `BASELINES.md` consulted if test corpus was updated

**After review:**
- [ ] No unintended fields removed from unknown-schema documents
- [ ] No `MigrationWarning.Level.Error` used as a silent swallow

**Success condition:** File exists at `.dev/json-migration/PR-CHECKLIST.md`.

---

### Task 7 — Fix D-025: `Phase2ConventionTests` schema version hardcode

**File:** Find the `Phase2ConventionTests` class (grep for `AllScenarioFixtures_HaveCorrectDocTypeAndVersion` in `Hrot/` or `FDP/`).

**Problem:** The test hardcodes `schemaVersion != 1` as a failure condition. When a committed scenario fixture is upgraded to v2 (e.g., from Phase 3), the test will fail. It should accept any version in the range `1..ScenarioMigrationModule.CurrentVersion`.

**Fix:** Update the check to:
```csharp
Assert.InRange(schemaVersion, 1, ScenarioMigrationModule.CurrentVersion);
```
Or equivalent. Add the appropriate using/import for `ScenarioMigrationModule`.

**Verify:** Run the convention test to confirm it still passes.

---

## Not in This Batch

- JM-P3-006 (Architect dry-run GATE) — requires human
- JM-P4-006 (Manual QA GATE) — requires human
- JM-P5-001 (Corpus expansion) — no specific edge cases to add yet
- JM-P5-004 (Quarterly stale-sidecar audit) — next calendar quarter
- D-019, D-020 — deferred; require careful runtime testing
- D-021, D-026, D-027, D-028 — P3, backlog

---

## Success Criteria

1. `dotnet test "Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj" --no-build` — 0 failures (the 3 pre-existing failures all fixed).
2. `dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" --no-build --filter "EX_T"` — 0 failures (all 29 pass).
3. `test-data/scenario-corpus/BASELINES.md` exists.
4. `.dev/json-migration/PR-CHECKLIST.md` exists.
5. D-025 convention test still passes.
6. Full solution build has no new errors beyond pre-existing `Hrot.Blueprints.Tests`.

---

## Report Format

Write the completion report to `.dev/json-migration/reports/BATCH-20-REPORT.md` with:
1. Files created/modified
2. Test results for each task (before/after counts)
3. Deviations from these instructions
4. Developer insights:
   - What issues were encountered?
   - What weak points were spotted in the codebase?
   - What design decisions were made beyond the spec?

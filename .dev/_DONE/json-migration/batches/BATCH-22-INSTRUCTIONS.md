# BATCH-22 Instructions -- P3 Cleanup: Dead Code, Corpus Tests, Blueprint Header

**Date:** 2026-06-03
**Branch:** `json-migration`
**Prereq commit:** `683727ad` (review: BATCH-21 APPROVED)

---

## Context

Onboarding:
- Read [Migration-system.md](../Migration-system.md) for the overall design.
- Read [BATCH-21-REPORT.md](../reports/BATCH-21-REPORT.md).
- Read [DEBT-TRACKER.md](../DEBT-TRACKER.md) -- open items are all P3.

Current state:
- All P2 debt resolved. All Phase 1-4 tasks complete.
- GATE tasks (JM-P3-006, JM-P4-006) require human/architect sign-off -- NOT in scope.
- This batch clears all remaining P3 debt items that can be done autonomously.

**Pre-existing build errors:** `Hrot.Blueprints.Tests` CS0234/CS0246 -- do NOT fix.

---

## Test-Driven Task Progression (MANDATORY)

For every change:
1. Read the source file before changing it.
2. Implement the minimal fix.
3. Run the affected test project after every task.
4. Do not add tests unless specified.

---

## Task List

### Task 1 -- D-034: Remove dead null-conditional in EntityPatch.AddField

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/EntityPatch.cs`

**Problem:** After the `ArgumentNullException` guard was added (D-026), the `?.` in `defaultValue?.DeepClone()` is dead code -- `defaultValue` can never be null here.

**Fix:** Change `defaultValue?.DeepClone()` to `defaultValue.DeepClone()` in the `AddField(JsonNode defaultValue, ...)` overload.

**Run:** `dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build`

**Success criteria:** All 54 tests still pass. No build errors.

---

### Task 2 -- D-035: Add corpus round-trip tests for minimal-entity and empty-entities fixtures

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs`

**Problem:** The `multi-version/v1_minimal-entity` and `multi-version/v1_empty-entities` corpus fixtures have no dedicated test methods. They were added to `test-data/scenario-corpus/` but are not exercised by any test.

**What to add:**

After `V2CorpusFile_DownMigratedThroughPipeline_LosesTagsField` (Test 18), add two new tests:

```csharp
// Test 20
[Fact]
public void V1MinimalEntity_MigratedThroughPipeline_MatchesV2MinimalEntity()
{
    string workspaceRoot = FindWorkspaceRoot();
    string v1Path = Path.Combine(
        workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v1_minimal-entity", "scenario.json");
    string v2Path = Path.Combine(
        workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v2_minimal-entity", "scenario.json");

    Assert.True(File.Exists(v1Path), $"v1 corpus file not found: {v1Path}");
    Assert.True(File.Exists(v2Path), $"v2 corpus file not found: {v2Path}");

    MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

    JsonObject v1Dom = JsonNode.Parse(File.ReadAllText(v1Path))!.AsObject();
    services.Pipeline.MigrateTo(v1Dom, 2);

    JsonObject v2Dom = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();

    string migratedJson = NormalizeForComparison(v1Dom);
    string expectedJson = NormalizeForComparison(v2Dom);

    Assert.Equal(expectedJson, migratedJson);
}

// Test 21
[Fact]
public void V1EmptyEntities_MigratedThroughPipeline_MatchesV2EmptyEntities()
{
    string workspaceRoot = FindWorkspaceRoot();
    string v1Path = Path.Combine(
        workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v1_empty-entities", "scenario.json");
    string v2Path = Path.Combine(
        workspaceRoot, "test-data", "scenario-corpus", "multi-version", "v2_empty-entities", "scenario.json");

    Assert.True(File.Exists(v1Path), $"v1 corpus file not found: {v1Path}");
    Assert.True(File.Exists(v2Path), $"v2 corpus file not found: {v2Path}");

    MigrationServices services = BuildServices(ScenarioMigrationModule.RegisterAll);

    JsonObject v1Dom = JsonNode.Parse(File.ReadAllText(v1Path))!.AsObject();
    services.Pipeline.MigrateTo(v1Dom, 2);

    JsonObject v2Dom = JsonNode.Parse(File.ReadAllText(v2Path))!.AsObject();

    string migratedJson = NormalizeForComparison(v1Dom);
    string expectedJson = NormalizeForComparison(v2Dom);

    Assert.Equal(expectedJson, migratedJson);
}
```

**Run:** `dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build`

**Success criteria:** Tests 20 and 21 pass. All 54 existing tests still pass.

---

### Task 3 -- D-021: Remove redundant fields from BlueprintAsset.Header

**Files:**
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs` (Header class)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/BlueprintAssetBuilder.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Tests/BlueprintAssetBuilderTests.cs`
- Various Stage tests that construct `Header` with `SubsystemType`/`SchemaVersion`

**Problem:** `BlueprintAsset.Header` has `SubsystemType` and `SchemaVersion` string fields that are now redundant -- `$meta` carries this info since Phase 2. These fields exist as legacy from before the migration system.

**What to do:**

1. In `GraphTypes.cs`, find the `Header` class and **remove** `SubsystemType` and `SchemaVersion` properties.

2. In `BlueprintAssetBuilder.cs`, find where `Header` is constructed with `SubsystemType = "Hrot.Blueprint"` and `SchemaVersion = "1.0"` and **remove those assignments**.

3. In any test that constructs `Header { SubsystemType = ..., SchemaVersion = ... }`, **remove those field initializers**.

4. In any test that asserts `asset.Header.SubsystemType` or `asset.Header.SchemaVersion`, **remove those assertions**.

**Important:** Do NOT remove the `Header` property from `BlueprintAsset` itself or the `Header` class entirely -- only remove the two redundant string fields. Other fields on `Header` (if any) must be preserved.

**Run:** Build and test:
```
dotnet build "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj" -c Debug --no-restore
```

Note: `Hrot.Blueprints.Tests` (the integration test project) has CS0234/CS0246 pre-existing errors -- skip that project; test only `Hrot.Blueprints.Compiler.csproj`.

**Success criteria:** `Hrot.Blueprints.Compiler` builds without errors. Compiler unit tests pass.

---

## Final Verification

1. Build affected projects:
   ```
   dotnet build "Hrot/Engine/Hrot.Common/Hrot.Common.csproj" -c Debug --no-restore
   dotnet build "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" -c Debug --no-restore
   dotnet build "Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj" -c Debug --no-restore
   ```

2. Run tests:
   ```
   dotnet test "Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj" --no-build
   ```

3. Build full solution:
   ```
   dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
   ```
   (`Hrot.Blueprints.Tests` CS0234/CS0246 expected.)

---

## Report Format

Write your report to `.dev/json-migration/reports/BATCH-22-REPORT.md`.

Include:
- Summary table with task results
- Test counts before/after
- Files changed

### Developer Insights Section (mandatory)

1. What issues were encountered?
2. What weak points were spotted?
3. What design decisions were made?

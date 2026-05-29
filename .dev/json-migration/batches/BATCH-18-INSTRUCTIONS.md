# BATCH-18 Instructions — Corrective Tests + CLI Migrate Subcommand

**Tasks:** C0 (D-023, D-024), JM-P4-004, JM-P4-005
**Design ref:** `.dev/json-migration/Migration-system.md` §7 (Phase 4 overview), TASK-DETAILS.md JM-P4-004/005
**Debt ref:** `.dev/json-migration/DEBT-TRACKER.md` D-023, D-024
**Constraint:** xUnit only, no FluentAssertions. Internal classes are accessible to test projects via `InternalsVisibleTo`.

---

## Background

Phase 3 (BATCH-17) introduced the first migrator pair (`V1ToV2_EntityInfo_AddTags` /
`V2ToV1_EntityInfo_RemoveTags`). The review identified two P2 test gaps that must be
closed before Phase 4 continues:

- **D-023**: Missing "user-edit-survives" round-trip test (design §10.9 rule 3 mandate).
- **D-024**: `EntityPatch` helper methods have no unit tests. Future migrators depend on them.

Phase 4 Editor UI changes (JM-P4-001..003) are deferred to BATCH-19. This batch implements
the CLI subcommand (JM-P4-004 + JM-P4-005), which is self-contained and testable.

---

## Corrective Task 0 — Resolve D-023 and D-024

### C0-A: "User-edit-survives" round-trip test (D-023)

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs`

**Add one test to the existing Group 4 (Corpus) section:**

```
T3_UserEdit_EntityInfoName_SurvivesRoundTrip
  1. Build a v1 DOM with one entity having EntityInfo.Name = "Commander-Alpha".
  2. Up-migrate to v2 via V1ToV2_EntityInfo_AddTags.Apply -> Tags:[] added.
  3. Down-migrate from v2 back to v1 via V2ToV1_EntityInfo_RemoveTags.Apply -> Tags removed.
  4. Assert: EntityInfo.Name is still "Commander-Alpha".
  5. Assert: EntityInfo["Tags"] is null or absent.
```

Use the same helper methods already in the file (`MakeRoot`, `MakeEntityWith`,
`MakeEntityInfoV1`, `MakeContext`). Call `new V1ToV2_EntityInfo_AddTags()` and
`new V2ToV1_EntityInfo_RemoveTags()` directly (they are internal but the test project
has `InternalsVisibleTo` from `Fdp.Core.csproj` AND the migrators live in
`Hrot.Common` which the test project references). Check: do the migrator classes
need `InternalsVisibleTo` from `Hrot.Common.csproj`? If not already present, add:
```xml
<InternalsVisibleTo Include="Hrot.Common.Tests" />
```
to `Hrot/Engine/Hrot.Common/Hrot.Common.csproj`.

Verify the migrators are `internal sealed`. If they are `internal`, add
`InternalsVisibleTo` for `Hrot.Common.Tests` in `Hrot.Common.csproj`. The existing
`Phase3MigratorTests.cs` already uses these migrators, so if the tests pass today, no
project change is needed.

**Test name:** `V1ToV2_Then_V2ToV1_EntityInfoName_SurvivesRoundTrip`

### C0-B: EntityPatch helper unit tests (D-024)

**File (new):** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/EntityPatchTests.cs`

**Namespace:** `Hrot.Common.Tests.Scenario.Migrations`

`EntityPatch` is in `Hrot.Common.Scenario.Migrations.Helpers`. It is `internal static`,
accessible from `Hrot.Common.Tests` via the existing project reference.

Write **12 tests** covering:

```
Group 1: OnEachEntity
  T_EP_01: OnEachEntity_EntitiesHaveEntityInfo_CallbackCalledForEach
    - Root with 2 entities (both have EntityInfo). Callback increments counter.
    - Assert counter == 2.

  T_EP_02: OnEachEntity_EntityMissingEntityInfo_CallbackNotCalled
    - Root with 1 entity (only SimTransform). Callback increments counter.
    - Assert counter == 0.

Group 2: AddField
  T_EP_03: AddField_FieldAbsent_AddsWithClonedDefault
    - Add "Tags" (default = new JsonArray()) to EntityInfo of 1 entity.
    - Assert Tags is a JsonArray, Count == 0.

  T_EP_04: AddField_FieldAlreadyPresent_IsIdempotent
    - EntityInfo already has "Tags": [1]. Call AddField with default = new JsonArray().
    - Assert Tags is still [1] (not overwritten).

  T_EP_05: AddField_TwoEntitiesWithSharedDefault_DeepClonesDefault
    - Pass same default JsonArray() reference for 2 entities.
    - Modify Tags array on entity 1.
    - Assert entity 2's Tags array is still empty (confirm DeepClone was used).

Group 3: RemoveField
  T_EP_06: RemoveField_FieldPresent_RemovesIt
    - EntityInfo has "Tags": []. Call RemoveField("EntityInfo", "Tags").
    - Assert Tags is absent.

  T_EP_07: RemoveField_FieldAbsent_IsIdempotent
    - EntityInfo has only Name and ForceId. Call RemoveField("EntityInfo", "Tags").
    - Assert property count unchanged (no exception, no side effects).

Group 4: RenameField
  T_EP_08: RenameField_FieldPresent_RenamesIt
    - EntityInfo has "Name": "Alpha". Rename "Name" to "DisplayName".
    - Assert "DisplayName" == "Alpha", "Name" is absent.

  T_EP_09: RenameField_FieldAbsent_IsNoOp
    - EntityInfo has only ForceId. Call RenameField("EntityInfo", "OldName", "NewName").
    - Assert property count unchanged.

Group 5: RenameComponent
  T_EP_10: RenameComponent_ComponentPresent_RenamesIt
    - Entity has "EntityInfo": { "Name": "A" }. Rename "EntityInfo" to "Info".
    - Assert "Info" key exists, "EntityInfo" key absent.
    - Assert Info["Name"] == "A".

  T_EP_11: RenameComponent_BothNamesPresent_ThrowsMigrationException
    - Entity has both "EntityInfo" and "Info" keys.
    - Call RenameComponent("EntityInfo", "Info").
    - Assert throws MigrationException.

Group 6: OnComponent
  T_EP_12: OnComponent_EntityHasComponent_CallbackCalled
    - 2 entities: one with EntityInfo, one without.
    - Callback increments counter.
    - Assert counter == 1.
```

**Helper note:** `EntityPatch` methods take a `JsonObject root` where root is the full
scenario DOM. Build root using the same pattern as `Phase3MigratorTests`:
```csharp
private static JsonObject MakeRoot(params (string id, JsonObject entity)[] entities)
```

Alternatively, build root inline using `JsonNode.Parse(...)` to keep test data readable.
Either approach is fine. Use `MakeContext()` from the test file or create a local one.

Actually, `EntityPatch` methods also require a `MigrationContext`. Check the method
signatures in `EntityPatch.cs` carefully. If the public API only requires `JsonObject root`
(no context), simply use `root`. If it requires `MigrationContext ctx`, create one with
`new MigrationContext("Hrot.Scenario", null)` (internal constructor, accessible from
`Hrot.Common.Tests` via `InternalsVisibleTo` in `Fdp.Core.csproj`).

---

## JM-P4-004: CLI `--mode migrate` Subcommand

### Overview

The CLI batch migration command allows bulk migration of JSON files in a directory.
The stub already exists in `Program.cs`:
```csharp
if (config.RequestedSubsystems.Contains("migrate"))
{
    // TODO(JM-P4): enumerate input directory...
    return 0;
}
```

Replace this stub with a call to the new `MigrateMode` class.

### Step 1: Add CLI args to HrotRunnerConfiguration

**File:** `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`

Add three new `[Option]` properties **before** the `// -- Parsed values ---` comment:

```csharp
/// <summary>Target schema version for --mode migrate. -1 means current registered version.</summary>
[Option("target-version", Required = false, Default = -1, HelpText = "Target schema version (-1 = current) for --mode migrate")]
public int TargetVersion { get; set; } = -1;

/// <summary>Input directory for --mode migrate. Defaults to current working directory.</summary>
[Option("input-dir", Required = false, HelpText = "Directory to migrate (for --mode migrate). Defaults to current directory.")]
public string InputDirectory { get; set; } = string.Empty;

/// <summary>When true, --mode migrate reports what would be done without writing any files.</summary>
[Option("dry-run", Required = false, Default = false, HelpText = "Report what would be done without writing files")]
public bool DryRun { get; set; }
```

In `Validate()`, add a guard after the `ci` early return and before the `editor` check:
```csharp
// Migrate mode is standalone: no peer synchronisation or subsystem-combination logic.
if (RequestedSubsystems.Contains("migrate")) return;
```

Also update the existing validation error message to include "migrate":
- The message `"Invalid mode: ..."` already enumerates valid modes. The HelpText on
  `ModeString` already mentions `migrate`, so no change needed there.

### Step 2: Create MigrateMode class

**File (new):** `Hrot/Runner/Hrot.ClusterRunner/Migration/MigrateMode.cs`

**Namespace:** `Hrot.ClusterRunner.Migration`

```csharp
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Fdp.Toolkit.Serialization;

namespace Hrot.ClusterRunner.Migration;

/// <summary>
/// Implements the <c>--mode migrate</c> batch migration subcommand.
/// Enumerates all *.json files in <see cref="InputDirectory"/>, and for each
/// file that has a <c>$meta</c> envelope, migrates it to <see cref="TargetVersion"/>
/// (or the current registered version when TargetVersion is -1).
/// Progress is reported to <see cref="Output"/> line-by-line.
/// </summary>
internal sealed class MigrateMode
{
    // ... see implementation notes below
}
```

**Constructor signature:**
```csharp
internal MigrateMode(
    MigrationServices services,
    string inputDirectory,
    int targetVersion,
    bool dryRun,
    TextWriter? output = null)
```

**`RunAsync` method:** `internal async Task<int> RunAsync(CancellationToken ct = default)`

**Algorithm:**

```
1. Resolve inputDirectory:
   - If empty/null: use Directory.GetCurrentDirectory()
   - If not a valid directory: Console.Error.WriteLine and return 1

2. Enumerate: files = Directory.GetFiles(inputDir, "*.json", SearchOption.AllDirectories)
   total = files.Length

3. For each file path (index i, 0-based):
   label = $"{i+1}/{total}: {Path.GetFileName(path)}"
   try:
     result = await MigrateFileAsync(path, ct)
     if result.Skipped:
       _out.WriteLine($"{label} -- SKIPPED ({result.Reason})")
       skipped++
     else:
       from = result.FromVersion, to = result.ToVersion
       dryTag = dryRun ? " [dry-run]" : ""
       _out.WriteLine($"{label} -- OK (v{from} -> v{to}{dryTag})")
       migrated++
   catch Exception ex:
     _out.WriteLine($"{label} -- FAILED: {ex.Message}")
     failed++

4. Print summary:
   _out.WriteLine($"[migrate] Completed: {migrated} migrated, {skipped} skipped, {failed} failed.")

5. Return failed > 0 ? 1 : 0
```

**`MigrateFileAsync(string path, CancellationToken ct)` method:**

Returns an internal `readonly record struct FileMigrateResult(bool Skipped, string? Reason, int FromVersion, int ToVersion)` with static factory methods:
```csharp
public static FileMigrateResult Skip(string reason) => new(true, reason, 0, 0);
public static FileMigrateResult Success(int from, int to) => new(false, null, from, to);
```

```
1. rawText = await File.ReadAllTextAsync(path, ct)
2. utf8 = Encoding.UTF8.GetBytes(rawText)
3. DocumentMeta diskMeta:
   try { diskMeta = JsonEnvelope.Peek(utf8.AsSpan()); }
   catch { return FileMigrateResult.Skip("no $meta envelope"); }

4. Resolve effective target version:
   effectiveTarget:
     if _targetVersion < 0:
       try { effectiveTarget = _services.Pipeline.GetCurrentVersion(diskMeta.DocType); }
       catch (MigrationException) { return FileMigrateResult.Skip("unknown docType"); }
     else:
       effectiveTarget = _targetVersion

5. If diskMeta.SchemaVersion == effectiveTarget:
   return FileMigrateResult.Skip("already at target")

6. If _targetVersion < 0 (use PersistentMigrationAdapter):
   loadResult = await _services.Persistent.LoadAndMigrateAsync(path, ct)
   if !loadResult.WasMigrated:
     return FileMigrateResult.Skip("no migration required")
   if !_dryRun:
     await _services.Persistent.SaveAsync(path, loadResult.Dom, loadResult, ct)
   return FileMigrateResult.Success(loadResult.OriginalMeta.SchemaVersion, loadResult.CurrentMeta.SchemaVersion)

7. Else (explicit --target-version, use Pipeline directly, no sidecar):
   dom = JsonNode.Parse(rawText)!.AsObject()
   _services.Pipeline.MigrateTo(dom, effectiveTarget, path)
   if !_dryRun:
     var opts = new JsonSerializerOptions { WriteIndented = true }
     var json = dom.ToJsonString(opts)
     json = JsonAestheticFormatter.FlattenNumericArrays(json)
     await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct)
   newMeta = JsonEnvelope.Read(dom)
   return FileMigrateResult.Success(diskMeta.SchemaVersion, newMeta.SchemaVersion)
```

**Important:** `GetCurrentVersion` may throw `MigrationException` for unregistered docTypes. Catch it in step 4 and return Skip.

### Step 3: Update Program.cs migrate stub

**File:** `Hrot/Runner/Hrot.ClusterRunner/Program.cs`

Replace the migrate mode stub (currently ends with `return 0;` after printing "stub complete"):

```csharp
// ── Migrate mode ──────────────────────────────────────────────────────────────
if (config.RequestedSubsystems.Contains("migrate"))
{
    Console.WriteLine("[Runner] Migrate mode -- constructing migration services...");
    var migrationServices = HrotMigrationBootstrap.BuildClusterRunnerMigrate();

    var runner = new Hrot.ClusterRunner.Migration.MigrateMode(
        migrationServices,
        config.InputDirectory,
        config.TargetVersion,
        config.DryRun);

    return await runner.RunAsync();
}
```

Note: `Main` is currently `static int Main(string[] args)`. Change the signature to
`static async Task<int> Main(string[] args)` to support `await runner.RunAsync()`.

---

## JM-P4-005: Progress Reporting

Progress reporting is already built into `MigrateMode.RunAsync` above. No additional
files are needed. Ensure:
- Each file produces exactly one output line: `N/total: filename.json -- OK (v1 -> v2)` or
  `... -- SKIPPED (reason)` or `... -- FAILED: reason`.
- Summary line at end: `[migrate] Completed: N migrated, M skipped, K failed.`
- Non-zero exit code (1) when `failed > 0`.

---

## Tests

### C0 Tests: Phase3MigratorTests addition (1 test)

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs`

Add one test at the end of the Group 4 (Corpus) section:

```csharp
[Fact]
public void V1ToV2_Then_V2ToV1_EntityInfoName_SurvivesRoundTrip()
{
    // Arrange: v1 entity with EntityInfo.Name that the user "edited"
    var root = MakeRoot(("aaa", MakeEntityWith(entityInfo: MakeEntityInfoV1(name: "Commander-Alpha"))));
    var ctx1 = MakeContext();
    var ctx2 = MakeContext();

    // Act: up-migrate v1 -> v2 (adds Tags: [])
    new V1ToV2_EntityInfo_AddTags().Apply(root, ctx1);

    // Act: down-migrate v2 -> v1 (removes Tags)
    new V2ToV1_EntityInfo_RemoveTags().Apply(root, ctx2);

    // Assert: user's Name edit survived the round-trip
    var entities = root["entities"]!.AsObject();
    var entityInfo = entities["aaa"]!.AsObject()["EntityInfo"]!.AsObject();
    Assert.Equal("Commander-Alpha", entityInfo["Name"]!.GetValue<string>());
    Assert.Null(entityInfo["Tags"]);
}
```

**Note:** If `MakeEntityInfoV1` does not accept a `name` parameter, adjust to call it as
the test file already calls it and then set the Name field explicitly:
```csharp
var info = MakeEntityInfoV1();
info["Name"] = JsonValue.Create("Commander-Alpha")!;
```

### C0 Tests: EntityPatchTests (12 tests)

**File (new):** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/EntityPatchTests.cs`

**Namespace:** `Hrot.Common.Tests.Scenario.Migrations`

**Usings required:**
```csharp
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Helpers;
```

Implement all 12 tests listed in C0-B above.

**Helper:** Build a scenario root with one entity and EntityInfo:
```csharp
private static JsonObject MakeScenarioRoot(string entityId, JsonObject? entityInfo = null)
{
    var entity = new JsonObject();
    if (entityInfo != null)
        entity["EntityInfo"] = entityInfo;
    else
        entity["SimTransform"] = new JsonObject { ["X"] = 0, ["Y"] = 0 };

    return new JsonObject
    {
        ["$meta"] = new JsonObject { ["docType"] = "Hrot.Scenario", ["schemaVersion"] = 1 },
        ["entities"] = new JsonObject { [entityId] = entity }
    };
}

private static MigrationContext MakeContext() =>
    new MigrationContext("Hrot.Scenario", null);
```

**Check `EntityPatch` method signatures carefully before writing tests.** Do NOT guess
method signatures — read `EntityPatch.cs` first. In particular:
- Does `OnEachEntity` take a `MigrationContext ctx` param? Look at the actual signature.
- Does `AddField` take `(JsonObject root, string componentName, string fieldName, JsonNode defaultValue)` or different params?

Check the actual test T1-style by reading `EntityPatch.cs` fully before writing.

### JM-P4-004/005 Tests: MigrateModeTests (8 tests)

**File (new):** `Hrot/Runner/Hrot.ClusterRunner.Tests/Migration/MigrateModeTests.cs`

**Namespace:** `Hrot.ClusterRunner.Tests.Migration`

`MigrateMode` is `internal` in `Hrot.ClusterRunner`. `Hrot.ClusterRunner.Tests` has
`InternalsVisibleTo` (confirmed in `Hrot.ClusterRunner.csproj`).

Tests use a temp directory with test JSON files. Use `Path.GetTempPath()` + a GUID subfolder,
and clean up in `Dispose` or `finally`. Tests can write simple JSON files inline rather than
copying the real corpus (for faster, self-contained tests).

```
T_CLI_01: RunAsync_NoJsonFiles_ReturnsZero
  - Create empty temp dir.
  - Run MigrateMode with that dir.
  - Assert exit code 0, output contains "0 migrated, 0 skipped, 0 failed".

T_CLI_02: RunAsync_FileWithNoMeta_SkipsFile
  - Write a plain JSON file (no $meta) to temp dir.
  - Run MigrateMode (default target version).
  - Assert output line contains "SKIPPED".
  - Assert exit code 0.

T_CLI_03: RunAsync_V1FileAlreadyAtCurrent_SkipsFile
  - Current registered version for Hrot.Scenario is 2. Write a v2 scenario.
  - Run MigrateMode (target version -1 = current = 2).
  - Assert output line contains "SKIPPED (already at target)".
  - Assert file is unchanged.

T_CLI_04: RunAsync_V1File_MigratesToV2_WritesFile
  - Write a v1 scenario JSON (valid Hrot.Scenario, schemaVersion: 1) with one entity+EntityInfo.
  - Run MigrateMode with target version -1 (current = 2).
  - Assert exit code 0, output line contains "OK (v1 -> v2)".
  - Assert written file has schemaVersion: 2 (read file back and peek $meta).

T_CLI_05: RunAsync_DryRun_DoesNotWriteFile
  - Write a v1 scenario JSON.
  - Run MigrateMode with dryRun = true.
  - Assert output line contains "OK (v1 -> v2) [dry-run]".
  - Assert file still has schemaVersion: 1 on disk.

T_CLI_06: RunAsync_ExplicitTargetVersion1_OnV2File_MigratesToV1
  - Write a v2 scenario JSON (schemaVersion: 2) with Tags field.
  - Run MigrateMode with targetVersion = 1.
  - Assert output line contains "OK (v2 -> v1)".
  - Assert written file has schemaVersion: 1 and no Tags.

T_CLI_07: RunAsync_FailedFileMigration_ReturnsNonZero
  - Write a JSON file with valid $meta but un-migratable docType:
    { "$meta": { "docType": "Unknown.Type", "schemaVersion": 1 }, "data": {} }
    (unknown docType → GetCurrentVersion throws → Skip with "unknown docType")
    OR corrupt the schemaVersion to a value that has no migration path.
    Actually for SKIPPED the exit code is 0. For FAILED we need a real exception.
    Simulate a FAILED case by writing a file that passes Peek ($meta present) but
    has schemaVersion that causes the adapter to throw (e.g., set targetVersion = 99
    which has no migration path).
  - Run MigrateMode with targetVersion = 99 for a v1 file.
  - Assert output line contains "FAILED".
  - Assert exit code 1.

T_CLI_08: RunAsync_MultipleFiles_ReportsAllResults
  - Create 3 files: 1 v1 (will migrate), 1 v2 (skipped), 1 no-meta (skipped).
  - Run MigrateMode with targetVersion -1.
  - Assert output contains "1 migrated, 2 skipped, 0 failed".
  - Assert exit code 0.
```

**Building test services:** Use `HrotMigrationBootstrap.BuildClusterRunnerMigrate()` to get
a real `MigrationServices`. The test project already references `Hrot.ClusterRunner` which
references `Hrot.Common` — all migrations are registered. This is fine for integration-level
tests.

**Building test JSON inline:** Use:
```csharp
private const string V1ScenarioJson = @"{
  ""$meta"": { ""docType"": ""Hrot.Scenario"", ""schemaVersion"": 1 },
  ""entities"": {
    ""test-entity-001"": {
      ""EntityInfo"": { ""Name"": ""Alpha"", ""ForceId"": 0 }
    }
  }
}";

private const string V2ScenarioJson = @"{
  ""$meta"": { ""docType"": ""Hrot.Scenario"", ""schemaVersion"": 2 },
  ""entities"": {
    ""test-entity-001"": {
      ""EntityInfo"": { ""Name"": ""Alpha"", ""ForceId"": 0, ""Tags"": [] }
    }
  }
}";
```

---

## Files to Create / Modify Summary

| Operation | File | Notes |
|-----------|------|-------|
| MODIFY | `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs` | Add 1 test (D-023) |
| CREATE | `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/EntityPatchTests.cs` | 12 tests (D-024) |
| MODIFY | `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` | Add 3 CLI args, add migrate early-return in Validate() |
| CREATE | `Hrot/Runner/Hrot.ClusterRunner/Migration/MigrateMode.cs` | Core migration runner logic |
| MODIFY | `Hrot/Runner/Hrot.ClusterRunner/Program.cs` | Replace migrate stub, make Main async |
| CREATE | `Hrot/Runner/Hrot.ClusterRunner.Tests/Migration/MigrateModeTests.cs` | 8 tests |

---

## Quality Constraints

1. All tests must use xUnit (`[Fact]`), no FluentAssertions.
2. Build must succeed: `dotnet build Hrot/Engine/Hrot.Common.csproj` and
   `dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`.
3. All new and existing tests must pass:
   `dotnet test Hrot/Engine/Hrot.Common.Tests/Hrot.Common.Tests.csproj`
   `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj`
4. `MigrateMode` must not use any `static` mutable state (must be safe to instantiate
   multiple times in tests).
5. Temp directories in tests must be cleaned up (use `try/finally` or implement `IDisposable`).
6. Do NOT add `using FluentAssertions` or any similar assertion library.
7. `MigrateMode` must not import `ImGuiNET` or any UI assembly.
8. Read `EntityPatch.cs` fully before writing `EntityPatchTests.cs` — do not guess method signatures.

---

## Report Requirements

On completion, provide:
1. A summary table of tasks completed (C0-A, C0-B, JM-P4-004, JM-P4-005).
2. Test count: new tests added (D-023: 1, D-024: 12, CLI: 8 = total 21 new tests).
3. Build + test output (pass/fail counts).
4. Any deviations from these instructions (with justification).
5. A recommended commit message.

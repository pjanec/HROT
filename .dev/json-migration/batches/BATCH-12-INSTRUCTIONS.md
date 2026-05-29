# BATCH-12 Instructions

**Tasks:** JM-P2-006, JM-P2-007
**Goal:** Patch road network and replay-export paths to emit/accept the `$meta` envelope.

**Reference files (read before coding):**
- Task definitions: `.dev/json-migration/TASK-DETAILS.md` (sections JM-P2-006 and JM-P2-007)
- Integration map: `.dev/json-migration/05-integration-patches.md` (sections for RoadNetworkLoader and RecordingExportService)
- Debt tracker: `.dev/json-migration/DEBT-TRACKER.md`
- AGENTS.md (editing invariants — read this first)

---

## Codebase-fit constraints (non-negotiable)

- **C-7**: xUnit only; no FluentAssertions.
- **TreatWarningsAsErrors=true** in all modified projects.
- Do NOT use `async` Task tests for JM-P2-006 tests — test methods must be synchronous `[Fact]`
  (use `.GetAwaiter().GetResult()` inside the test body if you need to call async code).
- Do NOT change any test that is not directly affected by a production change in this batch.
- Preserve all existing comments unchanged unless they are factually wrong.

---

## JM-P2-006 — Patch road network read path

### Background

`RoadNetworkLoader.LoadFromJson` is the sole read entry point for road network JSON files.
Currently it uses `File.ReadAllText` + `JsonSerializer.Deserialize<RoadNetworkJson>` directly.

Key observations:
- `RoadNetworkJson` has no `[JsonUnmappedMemberHandling]` attribute, so `$meta` is silently
  ignored when it appears in the JSON. Phase 2 format (with `$meta`) already deserializes
  correctly on the existing path.
- `RoadNetworkMigrationModule.RegisterAll` already registers
  `FdpDocumentTypes.RoadNetwork` at version 1 via `RegisterPassthroughDocType`. No changes
  to the module are needed.
- The method is synchronous (D-019 debt). The adapter is wired in with `.GetAwaiter().GetResult()`.

### Production change — RoadNetworkLoader.cs

**File:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Road/RoadNetworkLoader.cs`

Add an optional `ReadOnlyMigrationAdapter? migrationAdapter = null` parameter to `LoadFromJson`.

When `migrationAdapter != null`:
1. Call `migrationAdapter.LoadAndMigrateAsync(jsonPath, CancellationToken.None).GetAwaiter().GetResult()`.
2. Deserialize via `JsonSerializer.Deserialize<RoadNetworkJson>(outcome.AsJsonString())`.

When `migrationAdapter == null`: use the existing `File.ReadAllText` path unchanged.

Both paths must share the same `RoadNetworkBuilder` construction logic below (no duplication).

Required using directives:
```csharp
using Fdp.Core.Serialization.Migrations.Adapters;
using System.Threading;
```

Resulting signature:
```csharp
public static RoadNetworkBlob LoadFromJson(string jsonPath, ReadOnlyMigrationAdapter? migrationAdapter = null)
```

The `File.Exists` guard must still fire for BOTH paths (do not skip the guard when the
adapter is used — the adapter also throws `MigrationException`, but we prefer our own
`FileNotFoundException` for a clear caller-facing message).

### Test changes — RoadNetworkLoaderTests.cs

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Road/RoadNetworkLoaderTests.cs`

Do NOT modify existing tests T01, T02, T03. Add two new tests:

**T04 — Phase 2 format loads via adapter**

```
Name: LoadFromJson_Phase2Format_WithAdapter_LoadsCorrectly
Build a minimal road network JSON string that includes a $meta block as the first property:
{
  "$meta": { "docType": "Fdp.RoadNetwork", "schemaVersion": 1 },
  "nodes": [ ... ],          (same nodes as GetSampleJson)
  "segments": [ ... ],       (same segments)
  "metadata": { ... }
}
Write the string to a temp file. Build a ReadOnlyMigrationAdapter:
  - var registry = new MigrationRegistry();
  - RoadNetworkMigrationModule.RegisterAll(registry);
  - var adapter = new ReadOnlyMigrationAdapter(new MigrationPipeline(registry));
Call LoadFromJson(tempPath, adapter). Assert blob.Nodes.Length == 3 and blob.Segments.Length == 2.
Dispose and delete the temp file.
```

Required using directives:
```csharp
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Adapters;
using Hrot.Common.Scenario.Migrations; // for RoadNetworkMigrationModule
```

Wait — `Fdp.Toolkits.Tests` does NOT currently reference `Hrot.Common`.
Do NOT add a reference to `Hrot.Common` just for this test.
Instead, register the docType manually inline:
```csharp
var registry = new MigrationRegistry();
registry.RegisterPassthroughDocType("Fdp.RoadNetwork", 1);
var adapter = new ReadOnlyMigrationAdapter(new MigrationPipeline(registry));
```
`RegisterPassthroughDocType` is a method on `MigrationRegistry` — no external module needed.

**T05 — Legacy format (no `$meta`) loads correctly WITHOUT adapter**

```
Name: LoadFromJson_LegacyFormat_NoAdapter_LoadsCorrectly
Build the legacy JSON string (same as GetSampleJson — no $meta property).
Write to temp file.
Call LoadFromJson(tempPath) with NO adapter argument.
Assert blob.Nodes.Length == 3 and blob.Segments.Length == 2.
Dispose and delete the temp file.
```

NOTE: `ReadOnlyMigrationAdapter.ProcessBytes` calls `JsonEnvelope.Peek`, which THROWS
`MigrationException` when `$meta` is absent. Therefore, legacy files MUST NOT be loaded
through the adapter. The no-adapter path uses direct `File.ReadAllText` +
`JsonSerializer.Deserialize<RoadNetworkJson>` which silently ignores unknown fields
(including `$meta` when present) — legacy format always works this way.

---

## JM-P2-007 — Patch recording export header

### Background

`RecordingExportService.ExportToJson` writes a streaming JSON export using `Utf8JsonWriter`.
It opens the root object, then writes a `Header` block first:

```csharp
writer.WriteStartObject("Header");
writer.WriteString("Magic", "FDPREC");
writer.WriteNumber("FormatVersion", playback.FormatVersion);
writer.WriteNumber("Timestamp", playback.RecordingTimestamp);
writer.WriteEndObject();
```

Phase 2 replaces the `Header` block with a `$meta` block (docType=`Fdp.FlightRecorder.Metadata`,
schemaVersion=1). The three payload fields (`Magic`, `FormatVersion`, `Timestamp`) move to the
root level (directly after `$meta`).

`ExportChangelogToJson` writes a JSON ARRAY at root (no `Header` block) — leave it unchanged.
`ReplayBrowserContext`, `TransientMasterBuilder`, `RecordingDumper/Program.cs` require NO changes
for Phase 2 (per doc 05 analysis in the integration-patches document).

### Production change — RecordingExportService.cs

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs`

Replace the `Header` block with `$meta` plus root-level fields.

Before (current code at approximately line 111):
```csharp
// Header block
writer.WriteStartObject("Header");
writer.WriteString("Magic", "FDPREC");
writer.WriteNumber("FormatVersion", playback.FormatVersion);
writer.WriteNumber("Timestamp", playback.RecordingTimestamp);
writer.WriteEndObject();
```

After:
```csharp
// Envelope and recording identity fields
writer.WriteStartObject("$meta");
writer.WriteString("docType", FdpDocumentTypes.FlightRecorderMetadata);
writer.WriteNumber("schemaVersion", 1);
writer.WriteEndObject();
writer.WriteString("Magic", "FDPREC");
writer.WriteNumber("FormatVersion", playback.FormatVersion);
writer.WriteNumber("Timestamp", playback.RecordingTimestamp);
```

Add `using Fdp.Core.Serialization;` if not already present (needed for `FdpDocumentTypes`).

Check existing using directives in `RecordingExportService.cs` to determine if this import is
already present transitively. Add it if missing.

No other changes to `RecordingExportService.cs`.

### Test changes — RecordingExportServiceTests.cs

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs`

The following tests reference the old `Header` block and must be updated:

1. **EX-T02** (around line 50): currently checks:
   ```csharp
   Assert.Equal("FDPREC", root["Header"]!["Magic"]!.GetValue<string>());
   Assert.Equal((int)FdpConfig.FORMAT_VERSION, root["Header"]!["FormatVersion"]!.GetValue<int>());
   ```
   Update to check `$meta` plus root-level fields:
   ```csharp
   Assert.Equal(FdpDocumentTypes.FlightRecorderMetadata, root["$meta"]!["docType"]!.GetValue<string>());
   Assert.Equal(1, root["$meta"]!["schemaVersion"]!.GetValue<int>());
   Assert.Equal("FDPREC", root["Magic"]!.GetValue<string>());
   Assert.Equal((int)FdpConfig.FORMAT_VERSION, root["FormatVersion"]!.GetValue<int>());
   ```
   The `Assert.Equal(4, frames.Count)` line stays unchanged.

2. **EX-T14** (around line 402): currently checks:
   ```csharp
   Assert.NotNull(root["Header"]);
   ```
   Update to:
   ```csharp
   Assert.NotNull(root["$meta"]);
   ```

Add `using Fdp.Core.Serialization;` near the top of the test file if it is not already
present (needed for `FdpDocumentTypes.FlightRecorderMetadata` constant in EX-T02).

Do NOT modify any other test in this file.

---

## Build verification

After completing all changes, run:
```
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4 2>&1 | Select-String "error CS|Build succeeded|Build FAILED" | Select-Object -Last 5
```
Expected: no new `error CS` lines. Only pre-existing `Hrot.Blueprints.Tests` errors are acceptable.

Then run the tests:
```
dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" -c Debug --no-build --filter "RoadNetworkLoader" 2>&1 | Select-Object -Last 5
dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" -c Debug --no-build --filter "EX_T" 2>&1 | Select-Object -Last 5
```

---

## Deliverable

Write a `BATCH-12-REPORT.md` to `.dev/json-migration/reports/` with:
- Summary of all file changes (list each file modified/created)
- Test results for the filters above (pass/fail counts)
- Any deviations from the instructions (with justification)
- Any new debt items discovered

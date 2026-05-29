# BATCH-12 Report

**Tasks:** JM-P2-006, JM-P2-007
**Date:** 2026-05-29

---

## Files Changed

### Production files

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Road/RoadNetworkLoader.cs` | Added optional `ReadOnlyMigrationAdapter? migrationAdapter = null` parameter to `LoadFromJson`; added adapter branch using `.GetAwaiter().GetResult()`; added `using System.Threading` and `using Fdp.Core.Serialization.Migrations.Adapters` |
| `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` | Replaced `Header` block (WriteStartObject/WriteString/WriteNumber/WriteEndObject) with `$meta` object plus root-level `Magic`, `FormatVersion`, `Timestamp` fields using `FdpDocumentTypes.FlightRecorderMetadata` |

### Test files

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Road/RoadNetworkLoaderTests.cs` | Added `using Fdp.Core.Serialization.Migrations` and `using Fdp.Core.Serialization.Migrations.Adapters`; added T04 (`LoadFromJson_Phase2Format_WithAdapter_LoadsCorrectly`) and T05 (`LoadFromJson_LegacyFormat_NoAdapter_LoadsCorrectly`) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs` | Added `using Fdp.Core.Serialization`; updated EX-T02 assertions from `root["Header"]!["Magic"]` to `root["$meta"]!["docType"]`, `root["$meta"]!["schemaVersion"]`, `root["Magic"]`, `root["FormatVersion"]`; updated EX-T14 assertion from `root["Header"]` to `root["$meta"]` |

---

## Build Results

`dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4`

**Result:** Build FAILED — only pre-existing errors in `Hrot.Blueprints.Tests`
(`IAnimationTkbQueries` not found, `Hrot.Editor` namespace not found).
All other projects including `Fdp.Toolkits` and `Fdp.Toolkits.Tests` build cleanly.

---

## Test Results

### RoadNetworkLoader filter

```
dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" -c Debug --no-build --filter "RoadNetworkLoader"
```

**Passed: 5, Failed: 0** (T01, T02, T03 existing + T04, T05 new)

### EX_T filter

```
dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj" -c Debug --no-build --filter "EX_T"
```

**Passed: 1, Failed: 28** — all failures are pre-existing and unrelated to this batch.

Pre-existing root cause: `FdpAutoSerializer.Build()` throws `InvalidOperationException` for
`EntityInlineComp` (it has an `[InlineArray]` field with element type `Entity`, unsupported by
`FdpAutoSerializer`). This causes every test that calls `ExportToJson` with the default
`FormatMode = Incremental` to fail (since `Incremental` dispatches to `ExportChangelogToJson`
which internally calls `FdpAutoSerializer.Build()`).

Confirmed pre-existing via `git stash` verification: EX-T02 and EX-T14 failed with the same
`EntityInlineComp` error before any BATCH-12 changes were applied (Failed: 2, Passed: 0).

The EX-T02 and EX-T14 assertion changes (Header -> $meta) are correct and would be verified
once the underlying `FdpAutoSerializer`/`EntityInlineComp` issue is resolved.

---

## Deviations from Instructions

None. All production changes match the specifications exactly.

- `RoadNetworkLoader.LoadFromJson`: optional adapter parameter, `File.Exists` guard fires for
  both paths, no duplication of builder logic.
- `RecordingExportService.ExportToJson`: Header block replaced with `$meta` + root fields.
- Test T04 uses `registry.RegisterPassthroughDocType("Fdp.RoadNetwork", 1)` (no Hrot.Common
  module reference), as specified.
- Test T05 calls `LoadFromJson(tempPath)` with no adapter, as specified.
- No other tests in `RecordingExportServiceTests.cs` were modified.

---

## New Debt Items

None discovered.

# BATCH-16 Report

**Batch ID:** BATCH-16
**Status:** COMPLETE
**Phase tasks:** TASK-UAI-P5-02 (partial), TASK-UAI-P5-05

---

## Summary

All tasks from BATCH-16 have been implemented, the solution builds with 0 errors, and the test
suite passes with 123 tests (baseline 100 + 23 new).

---

## Changes Made

### Modified Files

**`Hrot/Editor/Hrot.Utility.Editor/Emit/UtilityAssetHasher.cs`**
- Moved `hc.Add(con.Curve.Kind)` from `ComputeStructureHash` into `ComputeParamHash`.
- Curve kind changes are now classified as param-only diffs (no structure change), which is
  required by `UtilityTuningDiffEngine` and its tests.

**`Hrot/Editor/Hrot.Utility.Editor/Hrot.Utility.Editor.csproj`**
- Added `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>`.
- This editor assembly has no DDS topic types; disabling IDL codegen avoids a build error
  caused by `InputParamKind` enum values (`String`, `Float`, `Int`) being IDL reserved keywords.

### New Files

**`Hrot/Editor/Hrot.Utility.Editor/Catalog/InputCatalogEntry.cs`**
- `InputCatalogEntry` (sealed class): `Name`, `Category`, `ParameterKind` properties.
- `InputParamKind` enum: `None`, `String`, `Float`, `Int`.

**`Hrot/Editor/Hrot.Utility.Editor/Catalog/InputCatalogBrowser.cs`**
- `InputCatalogBrowser` (static class): `Discover(params Assembly[] assemblies)`.
- Reflects over each assembly to find types named `"In"`, then extracts methods returning
  `InputRef`. For each unique name, picks the overload with the most non-`InputContext`
  parameters. Returns entries sorted by Name (Ordinal).

**`Hrot/Editor/Hrot.Utility.Editor/Comparison/UtilityComparisonSanitizer.cs`**
- `UtilityComparisonSanitizer : IAssetComparisonSanitizer` targeting `AssetKind.Utility`.
- Pipeline: check file exists, normalize line endings, strip `[UtilityLayout]` block,
  strip HROT_EDITOR_GENERATED line suffix, extract AssetId + AssetName, return
  `SanitizationResult`.

**`Hrot/Editor/Hrot.Utility.Editor/Comparison/UtilityTuningDiffEngine.cs`**
- `TuningParamDiff`: `OptionVisualId`, `ConsiderationVisualId`, `ConsiderationName`,
  `ParamLabel`, `OldValue`, `NewValue`.
- `TuningDiffResult`: `IsStructureEqual`, `IsIdentical`, `Diffs`.
- `UtilityTuningDiffEngine.Compute(versionA, versionB)`: fast-lane param diff using
  `UtilityAssetHasher` for structure and param hashes, then per-consideration diff walk.

**`Hrot/Editor/Hrot.Utility.Editor.Tests/Catalog/InputCatalogBrowserTests.cs`**
- 8 tests covering empty inputs, name/category/kind discovery, sorting, and deduplication.

**`Hrot/Editor/Hrot.Utility.Editor.Tests/Comparison/UtilityComparisonSanitizerTests.cs`**
- 9 tests covering file-not-found, suffix stripping, layout-block stripping, metadata
  extraction, and determinism.

**`Hrot/Editor/Hrot.Utility.Editor.Tests/Comparison/UtilityTuningDiffEngineTests.cs`**
- 6 tests covering identical, structure change, weight diff, multi-param diff, curve-kind
  diff, and diff ordering by VisualId.

---

## Build & Test Results

```
dotnet build IOS-IG-SimHost.sln -c Debug
Build succeeded. 0 Error(s)

dotnet test Hrot\Editor\Hrot.Utility.Editor.Tests\Hrot.Utility.Editor.Tests.csproj -c Debug
Passed! - Failed: 0, Passed: 123, Skipped: 0, Total: 123
```

---

## Notes

- `CycloneDdsDisableCodeGen` was added to `Hrot.Utility.Editor.csproj` because the project
  transitively receives the CycloneDDS build targets via `Fdp.Toolkits`. Since the project
  contains no DDS topic types, IDL generation is unnecessary and caused a syntax error on the
  `InputParamKind` enum (whose values `String`, `Float`, `Int` are IDL reserved keywords).

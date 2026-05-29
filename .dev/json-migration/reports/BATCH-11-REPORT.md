# BATCH-11 Report

**Status:** Complete
**Tests:** 4 new passing | Hrot.Blueprints.Compiler.Tests: 3 passing | Hrot.SimHost.Tests (TkbLoad): 10 passing | Hrot.Common.Tests: 11 passing

## Tasks Completed

- [x] JM-P2-004: Blueprint JSON envelope (BlueprintJsonServices)
- [x] JM-P2-005: TKB envelope compatibility (TkbLoadClusterStateHandler tests)

---

## Tasks Summary

### JM-P2-004 — Blueprint Read/Write Paths

**Production change:** `BlueprintJsonServices.Serialize` now stamps `$meta` (docType `"Hrot.Blueprints"`, schemaVersion 1) as the first property of the serialized JSON using `JsonEnvelope.Write`. The change is gated by `#if NET8_0_OR_GREATER` because `Fdp.Core` and `Hrot.Common` are net8.0-only dependencies, while the compiler project targets both `netstandard2.0` and `net8.0`.

**`Deserialize` unchanged** — see Developer Insights for the investigation result.

**New test project:** `Hrot.Blueprints.Compiler.Tests` (created, added to solution).

**Tests added (JM-P2-004):**
- `BlueprintJsonServices_Serialize_ProducesMetaEnvelope` — asserts `HasEnvelope`, docType = `"Hrot.Blueprints"`, schemaVersion = 1.
- `BlueprintJsonServices_Deserialize_Phase2_RoundTrips` — serialize then deserialize; asserts `AssetId` and `Name` round-trip.
- `BlueprintJsonServices_Deserialize_LegacyJson_Works` — minimal JSON without `$meta`; asserts no exception, correct field population.

### JM-P2-005 — TKB Envelope Compatibility

**No production C# change.** `ExtractTkbNameFromLocalScenario` uses a forward-only `Utf8JsonReader` that scans all tokens for the first property named `"TkbName"`. When `$meta` is the first property it is not named `"TkbName"` and is skipped transparently; the scanner continues and finds `TkbName` at the root level.

**Test helper updated:** `WriteScenarioHeader` now accepts `phase2Format = false` (default). When `true`, it writes `{"$meta":{"docType":"Hrot.Scenario","schemaVersion":1},"TkbName":"<name>"}`.

**Test added (JM-P2-005):**
- `ExtractTkbName_Phase2Format_ReturnsCorrectName` — writes Phase 2 header, calls `PrepareAsync`, asserts `db.ActiveTkbName == "TestTkb"`.

---

## Developer Insights

### BlueprintAsset Unknown-Properties Behavior (Investigation Result)

**Finding:** `BlueprintAsset` deserialize needs NO change for Phase 2 compatibility.

`_options` in `BlueprintJsonServices` sets:
- `PropertyNameCaseInsensitive = true`
- No `JsonUnmappedMemberHandling.Disallow`

`System.Text.Json` default behavior silently ignores unknown properties. Since `BlueprintAsset` has no `$meta` property and no `[JsonExtensionData]`, the `$meta` envelope in Phase 2 JSON is silently discarded during deserialization. Both legacy JSON (no `$meta`) and Phase 2 JSON (`$meta` first) are deserialized correctly by the unchanged `Deserialize` method.

This behavior was verified by test JM-P2-004-T02 (Phase 2 round-trip) and JM-P2-004-T03 (legacy format).

### Issues Encountered

1. **`BlueprintAsset.Header` exists (contradicts batch spec).** The instructions stated "BlueprintAsset has NO version field and NO Header object," but the actual `BlueprintAsset` class has `public Header Header { get; set; } = new()` where `Header` has `SubsystemType = "Hrot.Blueprints"` and `SchemaVersion = "1.0"`. Per the instruction note "there is nothing to remove from the old format," the `Header` body is left unchanged. The `$meta` envelope is stamped on top of the full serialized body.

2. **Dual-target project (`netstandard2.0;net8.0`).** `Fdp.Core` and `Hrot.Common` are net8.0-only. The `Serialize` change is wrapped in `#if NET8_0_OR_GREATER` to keep the netstandard2.0 build clean. The `Hrot.Common` project reference was added conditionally for net8.0 in the `.csproj`.

3. **Pre-existing `Hrot.Blueprints.Tests` build failure** (Stride editor dependency: `Hrot.Editor` namespace, `IAnimationTkbQueries`). Not touched per instructions. The solution-level build reports `Build FAILED` due to this pre-existing issue only; all other projects build cleanly.

4. **Pre-existing `Hrot.SimHost.Tests` failures** (41 failures in `FullBranchPipelineTests` — "recording file not found in temp dir"). These are environment-side failures unrelated to this batch. The TKB-specific tests all pass.

### Design Decisions Beyond the Spec

- Placed the `Hrot.Common` reference inside the existing net8.0-conditional `ItemGroup` (alongside `Fdp.Toolkits`) for consistency. Kept as a separate `ItemGroup` to preserve the explanatory comment.
- Test project uses `xunit 2.9.3` / `xunit.runner.visualstudio 3.1.4` (same as `Hrot.Common.Tests`) for consistency.
- Did not suppress `TreatWarningsAsErrors` in the new test project to match solution-wide standards.

---

## Build / Test Results

### Build
```
Hrot.Blueprints.Compiler (net8.0 + netstandard2.0): Build succeeded
Hrot.Blueprints.Compiler.Tests (net8.0):             Build succeeded
Hrot.SimHost.Tests (net8.0):                          Build succeeded
IOS-IG-SimHost.sln (full):                            Build FAILED (pre-existing Hrot.Blueprints.Tests only)
```

### Tests
| Project | New | Passed | Failed | Notes |
|---|---|---|---|---|
| Hrot.Blueprints.Compiler.Tests | 3 | 3 | 0 | All new JM-P2-004 tests |
| Hrot.SimHost.Tests (TkbLoad filter) | 1 | 10 | 0 | 9 pre-existing + 1 new JM-P2-005 test |
| Hrot.Common.Tests | 0 | 11 | 0 | No regressions |

---

## Files Created / Modified

### Created
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler.Tests/Hrot.Blueprints.Compiler.Tests.csproj` — new net8.0 test project
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler.Tests/BlueprintJsonServicesTests.cs` — 3 tests (JM-P2-004-T01/T02/T03)
- `.dev/json-migration/reports/BATCH-11-REPORT.md` — this report

### Modified
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/BlueprintJsonServices.cs` — `Serialize` stamps `$meta` via `JsonEnvelope.Write`; `Deserialize` unchanged; added `#if NET8_0_OR_GREATER` using directives
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Hrot.Blueprints.Compiler.csproj` — added `InternalsVisibleTo` for new test project; added conditional `Hrot.Common` project reference (net8.0 only)
- `Hrot/Subsystems/Hrot.SimHost.Tests/TkbLoadClusterStateHandlerTests.cs` — updated `WriteScenarioHeader` to accept `phase2Format` parameter; added `ExtractTkbName_Phase2Format_ReturnsCorrectName` test
- `IOS-IG-SimHost.sln` — new test project registered (`dotnet sln add`)

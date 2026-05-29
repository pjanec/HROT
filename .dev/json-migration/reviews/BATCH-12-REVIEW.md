# BATCH-12 Review

**Batch:** BATCH-12
**Status:** APPROVED WITH ONE DEBT ITEM
**Reviewer:** Dev Lead
**Date:** Post-sub-agent verification

---

## Build Verification

Full solution build: only pre-existing `Hrot.Blueprints.Tests` errors (`Hrot.Editor` namespace,
`IAnimationTkbQueries`). No new `error CS` lines from BATCH-12 changes. `Fdp.Toolkits` and
`Fdp.Toolkits.Tests` build succeeded.

---

## JM-P2-006 Review — Road Network Read Path — PASS

### RoadNetworkLoader.LoadFromJson — PASS

Optional `ReadOnlyMigrationAdapter? migrationAdapter = null` added. `File.Exists` guard
fires on BOTH paths before any IO. Adapter path: `LoadAndMigrateAsync(jsonPath, CancellationToken.None).GetAwaiter().GetResult()` then `JsonSerializer.Deserialize<RoadNetworkJson>(outcome.AsJsonString())`.
Legacy path unchanged. Builder construction logic is shared — no duplication. Correct.

No changes to `RoadNetworkMigrationModule` required (already has `RegisterPassthroughDocType` for `Fdp.RoadNetwork` v1 since BATCH-09).

### Test quality — GOOD

T04: Phase 2 format (with `$meta`) via adapter → asserts 3 nodes, 2 segments. Verifies the
adapter path actually works end-to-end. `registry.RegisterPassthroughDocType("Fdp.RoadNetwork", 1)` 
inline — no spurious `Hrot.Common` dependency. ✓

T05: Legacy format (no `$meta`) via no-adapter path → asserts same counts. Confirms that
`ReadOnlyMigrationAdapter` is NOT used for legacy files (which would throw since
`JsonEnvelope.Peek` requires `$meta`). ✓

All 5 RoadNetworkLoader tests: PASSED.

---

## JM-P2-007 Review — Recording Export Header — PASS

### RecordingExportService.ExportToJson — PASS

`Header` block replaced with `$meta` object:
- `$meta.docType` = `FdpDocumentTypes.FlightRecorderMetadata` ("Fdp.FlightRecorder.Metadata")
- `$meta.schemaVersion` = 1
- `Magic`, `FormatVersion`, `Timestamp` moved from inside `Header` to root level

`ExportChangelogToJson` writes a root JSON array — not touched (correct). Change is minimal
and precise.

### Test updates — CORRECT

EX-T02: Updated to assert `root["$meta"]!["docType"]`, `root["$meta"]!["schemaVersion"]`,
and root-level `root["Magic"]`, `root["FormatVersion"]`. ✓

EX-T14: Updated `Assert.NotNull(root["Header"])` → `Assert.NotNull(root["$meta"])`. ✓

No other tests modified. ✓

### Pre-existing EX_T failures — CONFIRMED PRE-EXISTING

All 28 `EX_T` failures are caused by `EntityInlineComp` with `[InlineArray]` field of type
`Entity` which is not supported by `FdpAutoSerializer.Build()`. The git-stash check confirmed
EX-T02 and EX-T14 were already failing before this batch (same error, same stack). Not caused
by BATCH-12.

---

## Debt Items

**D-022 (P2):** `EntityInlineComp` `[InlineArray]` field of type `Entity` is unsupported by
`FdpAutoSerializer.Build()`. All 28 `EX_T` recording export tests (including EX-T02 and
EX-T14) fail at the same root cause. This is a pre-existing failure unrelated to Phase 2 migration
work, but blocks full verification of the EX test suite. Should be fixed before Phase 2 acceptance
gate (JM-P2-011).

---

## Decision

**APPROVED.** 5 new RoadNetworkLoader tests passing. Production code is clean and correct.
EX_T test failures are pre-existing and confirmed via git-stash diff. D-022 added to debt
tracker.

# BATCH-11 Review

**Batch:** BATCH-11
**Status:** APPROVED WITH MINOR NOTES
**Reviewer:** Dev Lead
**Date:** Post-sub-agent verification

---

## Build Verification

Only pre-existing `Hrot.Blueprints.Tests` errors remain (`Hrot.Editor` namespace,
`IAnimationTkbQueries`). All other projects build cleanly. No new build errors introduced.

---

## JM-P2-004 Review — Blueprint JSON Envelope — PASS

### BlueprintJsonServices.Serialize — PASS

`JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.Blueprint, 1))` is called after
building the DOM via `JsonSerializer.SerializeToNode`. `$meta` is stamped as the first
property. The `#if NET8_0_OR_GREATER` guard is correct: `Hrot.Blueprints.Compiler` targets
`netstandard2.0;net8.0` and `Fdp.Core`/`Hrot.Common` are net8.0-only references.

### BlueprintJsonServices.Deserialize — CORRECTLY UNCHANGED

Investigation confirmed: `System.Text.Json` with the existing `_options` silently ignores
`$meta` (no `JsonUnmappedMemberHandling.Disallow`). Both legacy (no `$meta`) and Phase 2
(`$meta` first) JSON are deserialized correctly without code change. This is a good finding
that simplifies the implementation.

### BlueprintAsset Header retained — ACCEPTABLE

`BlueprintAsset` has an existing `Header` DTO with `SubsystemType` and `SchemaVersion`. Per
Phase 2 scope, these are not removed (C-3 only applied to the Scenario-specific types in
Phase 2 of the roadmap). The `$meta` envelope is added alongside the existing `Header`.
A future phase can remove the redundant `Header.SchemaVersion` and `Header.SubsystemType`
from `BlueprintAsset`. Add to debt tracker (D-021).

### Test Quality — GOOD

T01: Checks `$meta` present, correct docType/schemaVersion. ✓
T02: Phase 2 round-trip preserves `AssetId` and `Name`. ✓
T03: Legacy JSON without `$meta` deserializes correctly. ✓

Tests verify actual behavior, not just compilation.

### New test project structure — ACCEPTABLE

`Hrot.Blueprints.Compiler.Tests` created. Does NOT reference `Hrot.Blueprints.Tests`
(which has the pre-existing Stride failures). Uses same xUnit version as `Hrot.Common.Tests`.

---

## JM-P2-005 Review — TKB Envelope Compatibility — PASS

### No production code change — CORRECT

`ExtractTkbNameFromLocalScenario` uses a `Utf8JsonReader` forward-scan that finds the first
`TkbName` property at any depth. Adding `$meta` first causes the scanner to skip it and
continue to `TkbName`. Correct behavior without any source change. Well-reasoned.

### Test update — PASS

`WriteScenarioHeader` updated with `phase2Format` parameter. New test verifies Phase 2
header format is handled correctly. All 10 existing TKB tests still pass.

---

## Debt Tracker Update Required

**D-021 (P3):** `BlueprintAsset.Header` retains `SubsystemType` and `SchemaVersion` fields
that are redundant now that `$meta` carries this information. Remove in a later phase
(JM-P3 or cleanup batch) to keep the format consistent with C-3.

---

## Decision

**APPROVED.** 4 new tests passing. No regressions. Implementation is clean and well-tested.
Minor debt D-021 added for tracking.

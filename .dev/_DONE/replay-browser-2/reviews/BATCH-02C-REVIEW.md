# BATCH-02C Review

**Batch:** BATCH-02C (Corrective) -- EX-T22 and EX-T13 test fixes
**Reviewer:** Dev Lead
**Decision:** APPROVED WITH NOTE

---

## Summary

Both issues from the BATCH-02 review are addressed. Tests remain green. Build is clean.

---

## Fix 1: EX-T22 (P2)

**Verdict:** APPROVED WITH NOTE.

A `FooHarnessBlackboardTranslator : IEntityScenarioTranslator` stub was added as a private
nested class. The translator claims `HarnessVelocity` (bit 203) and returns a `JsonObject`
containing `"Source": "FooBlackboard"`.

The test verifies that:
1. `ScenarioSerializer.Serialize()` invokes the translator's `Extract()` and the resulting JSON
   contains `"FooBlackboard"`.
2. Passing the serializer to `RecordingExportService` does not crash (serializer is compatible).

**Known limitation (logged as RB02C-P2-001 in DEBT-TRACKER):**
`RecordingExportService` uses only `_serializer.AutoSerializer` for per-component payload
serialization during export. It does not call translator `Extract()` for components the
translator claims. This means `"FooBlackboard"` does NOT appear in the `ExportToJson` output
-- only in the separate `ScenarioSerializer.Serialize()` call. The DESIGN.md §3.4 says
translators should be invoked automatically. This production code gap is logged as P2 in
DEBT-TRACKER (RB02C-P2-001) targeting BATCH-03.

The test is still meaningfully better than the prior `EX_T22_NullSerializer_FallsBackToAutoSerializer`
(which tested a null fallback with no relation to the translator contract). The translator
infrastructure is at least verified to function correctly.

---

## Fix 2: EX-T13 (P3)

**Verdict:** APPROVED.

`Assert.Equal(2, frames.Count)` -- exact. The per-frame assertion now also checks the lower bound
(`>= 1.5 - 1e-6`). Both sides of the time window are constrained.

---

## Test Gate

38/38 Fdp.Toolkits.Tests + 4/4 Fdp.Tools.RecordingDumper.Tests green. Build: 0 errors.

---

## Debt Updated

- Added RB02C-P2-001 (P2): `RecordingExportService` does not invoke translator `Extract()`
  during per-component export. Target: BATCH-03.

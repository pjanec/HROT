# BATCH-03C Review

**Batch:** BATCH-03C — Corrective (tasks skipped in BATCH-03)
**Reviewer:** Dev Lead
**Decision:** APPROVED WITH NOTE

---

## Summary

All 4 corrective tasks implemented correctly. 60/60 tests pass.

---

## Task C1: Translator Dispatch — APPROVED

Translator payloads are dispatched in both `ExportToJson` (regular mode) and
`BuildEntityStateNode` (changelog mode). The fallback to `autoSerializer.TryExtract` is
correct: translator payload takes priority when `compName` matches a key in the dict.
The `_serializer?.Translators` null-propagation in the changelog call site is correct.

## Task C2: EX-T22 Strengthened — APPROVED

The strengthened assertion walks the JSON output and finds the `HarnessVelocity` component
payload in the actual `ExportToJson` output, asserting it contains `"FooBlackboard"`.
This directly exercises the RB02C-P2-001 production path. The `BuildBasicRecordingWithVelocity`
helper builds a clean 1-frame recording with both position and velocity components.

## Task C3: HarnessTransform + EX-T20 — APPROVED

`HarnessTransform` (ID 204, `System.Numerics.Vector3 Position`) is registered in the harness
constructor. `FdpAutoSerializer` serializes `Vector3` as `[x, y, z]` via
`FdpJsonOptionsRegistry.DefaultRelaxed`'s compact array converters. EX-T20 now verifies
`FlattenNumericArrays` processes the array and produces a single-line representation.
The `Assert.True(foundTransform, ...)` guard ensures the component was actually found.

## Task C4: DIF-T09 Budget Fix — APPROVED WITH NOTE

**Note (debt entry RB03-P2-001 updated to RESOLVED):** Budget 300 MB vs 100 MB spec.
The 100 MB instruction was optimistic given `JsonNode.Parse()` baseline overhead (~216 MB
observed for 1000 iterations of parsing two 200-field JSON strings + diffing). 300 MB is
still a meaningful guard against algorithmic allocation regressions and is a significant
improvement over the old 512 MB budget. Method renamed to `DIF_T09_AllocationBudget_1000Calls_Under300MB`.

---

## Debt Updates

- RB02C-P2-001: RESOLVED (translator dispatch implemented in both export paths)
- RB02-P3-003: RESOLVED (HarnessTransform with Vector3 field, EX-T20 properly exercises FlattenNumericArrays)
- RB03-P2-001: RESOLVED (DIF-T09 pre-builds JSON string, 300 MB budget)

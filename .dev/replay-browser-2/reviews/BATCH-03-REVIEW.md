# BATCH-03 Review

**Batch:** BATCH-03 — Stage 3 Diff Engine Backend + Stage 2 History Trackers
**Reviewer:** Dev Lead
**Decision:** CHANGES REQUIRED

---

## Summary

RB-3.1, RB-3.2, RB-3.3, RB-2.1 are correctly implemented. Tests for those tasks are solid and
exercise real behavior. However, two tasks from the instructions were completely skipped, and
DIF-T09 has a test quality issue.

---

## Completed Tasks — Assessment

### RB-3.1: DiffNode Hierarchy — APPROVED

`DiffNode`, `DiffObject`, `DiffValue` follow the DESIGN.md §5.1 spec. `EvaluateModificationState`
correctly propagates `IsModified` from children. No Presentation reference.

### RB-3.2: ComponentDiffService — APPROVED WITH NOTE

DIF-T01..DIF-T12, DIF-T13 all correct and meaningful. The recursive object diff, epsilon
numeric comparison, disjoint-key handling, array-as-single-leaf rule, null before/after
(birth/death cases), and the hide-unchanged pruning rule are all properly tested.

**DIF-T09 issue (P2):** The test creates fresh `JsonObject` instances inside the 1000-iteration
loop ("Re-parse each iteration to avoid shared-node aliasing"), which measures `JsonObject`
construction cost in addition to the diff algorithm. The spec budget was 1 MB; the test uses
512 MB. While `JsonNode` requires fresh instances per call (ownership model), the budget should
still meaningfully guard against algorithmic allocation growth. Fix in BATCH-03C:
- Pre-build the JSON *strings* once.
- Parse them per iteration (`JsonNode.Parse(jsonString)`), not via `new JsonObject()` construction.
- Tighten budget to <= 100 MB (100 KB/call, covering parse + diff + output).

### RB-3.3: Changelog Mode — APPROVED WITH NOTE

EX-T27, EX-T28, EX-T29 are rigorous and cover the three critical scenarios. The fixture
builders are well-constructed.

**Deviation from DESIGN.md §3.6 (accepted):** Frame 0 sets the baseline without emitting
a changelog entry. DESIGN.md §3.6 implies null→current should emit an entry (entity birth).
However, BATCH-03 instructions explicitly specified "exactly 3 entries (frames 1, 3, 4)",
i.e., the birth frame is excluded. This is a reasonable UX decision (showing what CHANGED
from the start, not re-emitting the starting state). Accepted as-is; noted in DEBT-TRACKER
as a known deviation if the spec intent changes.

### RB-2.1: History Trackers — APPROVED

FND-T01..FND-T05 and the 100-sequence randomized smoke test are all well-structured. The
navigating-suspension invariant and forward-stack truncation are correctly verified. FND-T04
(re-entrance guard) is particularly important and is correctly tested.

---

## Skipped Tasks — BLOCKING (Changes Required)

### Task 5: Translator Invocation in RecordingExportService (RB02C-P2-001) — NOT DONE

The subagent marked this as "already done" but it was NOT done in BATCH-02C. The production
code change is still missing. `RecordingExportService.ExportToJson()` still calls
`autoSerializer.TryExtract()` directly for every component and never invokes
`translator.CanTranslate()` or `translator.Extract()`.

The strengthened EX-T22 assertion (that "FooBlackboard" appears in the actual `ExportToJson`
output, not just in `ScenarioSerializer.Serialize()`) was also not added.

**Required fix (BATCH-03C):**
1. In `ExportToJson()`, before the per-component loop: for each translator in
   `_serializer.Translators` where `translator.CanTranslate(sandboxRepo, entity)` is true,
   call `translator.Extract(sandboxRepo, entity, guidResolver)` and build a lookup
   `Dictionary<string, JsonNode?> translatorPayloads` keyed by component name.
2. In the per-component loop: check `translatorPayloads.TryGetValue(compName, out var tPayload)`.
   If found, use `tPayload` as the payload instead of calling `autoSerializer.TryExtract`.
3. Update EX-T22 to build a recording with `HarnessVelocity`, pass `FooHarnessBlackboardTranslator`
   serializer to `RecordingExportService`, run `ExportToJson()`, parse the output, and
   assert the `HarnessVelocity` component entry's `Payload` contains `"FooBlackboard"`.

### Task 6: EX-T20 Improvement with Array-Field Component (RB02-P3-003) — NOT DONE

`HarnessTransform` was not added. EX-T20 still uses the weak assertion
`Assert.DoesNotContain("  1,\n", text)` against `HarnessPosition` (flat struct, no array field).
`FlattenNumericArrays` is never invoked on a real JSON array.

**Required fix (BATCH-03C):**
1. Add `HarnessTransform` (component ID 204) to the harness with a `float[]` or
   `System.Numerics.Vector3` field that `FdpAutoSerializer` serializes as a JSON array
   (check `ScenarioSerializerTests.cs` for a working example).
2. Register it in `FdpRecordingHarness`.
3. Update `EX_T20` to build a recording with `HarnessTransform`, export it, find the
   `HarnessTransform` payload in the output, and assert: the numeric array is on a single line
   (no newline inside `[...]`). Use regex: `Assert.DoesNotMatch(new Regex(@"\[\s*[0-9.]+,\s*\n"), text)`.

---

## Debt Updates Required

Add to DEBT-TRACKER:
- DIF-T09 allocation budget P2: test should pre-build JSON strings (not JsonObjects), parse
  per iteration, budget <= 100 MB. Target: BATCH-03C.

Mark as resolved:
- RB02-P3-003 remains OPEN — not addressed.
- RB02C-P2-001 remains OPEN — not addressed.

---

## Corrective Batch

Create BATCH-03C covering:
1. Task 5: RecordingExportService translator dispatch + EX-T22 strengthened
2. Task 6: HarnessTransform + EX-T20 strengthened
3. DIF-T09: tighter allocation test (pre-build strings, 100 MB budget)

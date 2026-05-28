# BATCH-02 Review

**Batch:** BATCH-02
**Reviewer:** Dev Lead
**Date:** 2026-05-28
**Verdict:** APPROVED

---

## Summary

All tasks completed. Tests pass (BTree 291, HSM 250, AiShared 404, build clean). 29 new tests added.

---

## Task-by-Task Assessment

### D-02 Debt Fix — APPROVED

The upgrade to `Sanitize_SubtreeWithSyncAndCatalog_HoistsCommentSyncAndHumanizesGuid` is correct. Line-order assertions use `Array.FindIndex` on `\n`-split lines and assert `commentIdx < syncInIdx < syncOutIdx < subtreeCallIdx`. All existing `Contains` assertions are preserved. This satisfies the debt requirement exactly.

### TASK-C-05 (HsmComparisonSanitizer) — APPROVED

**Implementation quality: Good.**

- The dual-path `FindCallStartForStableId` (direct vs brace-depth scan) is a clean solution to the multi-line Child block problem that BTree didn't need to solve.
- The brace-depth scan uses the same character-counting approach as BTree's `CollectCallText`. The known D-01 fragility (braces inside string literals) applies but is not a real concern for the HSM emitter's output.
- Comment hoisting for global transitions (same-line injection before `builder.GlobalTransition(...)`) is correct — the emitter always emits global transitions on a single line.
- The parser correctly merges State+Transition+Region comments into a single GUID-keyed dictionary — this is correct because stableIds and visualIds occupy separate GUID namespaces and never collide in practice.
- Never-throws contract verified (try/catch in `Sanitize`, `TryReadFile` swallows IO errors).

**Test quality: Good.**

Tests verify ordering with line-index comparisons, not just `Contains`:
- `Sanitize_SimpleStateMachine_HoistsStateAndTransitionComments`: checks `commentLineIdx < stateIdleIdx` AND `transCommentIdx < onCallIdx`
- `Sanitize_ParallelRegions_HoistsRegionComments`: checks both MotionTrack and AnimTrack region comment positions
- `Sanitize_GlobalTransitionWithComment_HoistsCommentAboveGlobalTransitionCall`: checks `commentIdx < globalTransIdx`
- `Sanitize_TransitionViaOnGoToWithComment_HoistsCommentAboveOnCall`: checks `commentIdx < onCallIdx`
- `Sanitize_NoLayoutMethod_ReturnsWarning`: warning present, text non-empty, no exception
- `Sanitize_RunTenTimes_ProducesByteIdenticalOutput`: full 10-run determinism loop

**Gap noted:** No test covering a 3-level nested Child block (state → child → grandchild where grandchild has stableId comment). The brace-depth scan is theoretically correct but this code path is untested. Logged as D-07 (P3).

### TASK-C-06 (BlackboardComparisonSanitizer) — APPROVED

**Implementation quality: Good.**

- The sanitizer is appropriately minimal — verbatim concatenation with section labels.
- `DiscoverHeavyCompanion` correctly strips `.Blackboard.cs` suffix and appends `.HeavyBlackboard.cs`.
- Dual-header parsing (`OwningAssetId:` and `AssetId:`) for forward compatibility is a good practical choice given the discrepancy between the actual emitter and the design doc notation.
- DI registration correctly wires `BlackboardComparisonSanitizer` into `SanitizerRegistry` from `SharedAiEditorServiceCollectionExtensions`.

**Test quality: Good.**

- `Sanitize_InlinePlusHeavy_OutputContainsBothLabeledSectionsInOrder`: checks `inlineIdx < heavyIdx` (ordering assertion, not just Contains)
- `Sanitize_MissingMainFile_ReturnsResultWithWarning_NeverThrows`: verifies warning message content
- `Sanitize_AssetNameAndIdExtractedFromOwningHeaders`: verifies actual GUID value parsed correctly
- 10-run determinism loops for both inline-only and inline+heavy configurations

**Gap noted:** The `AssetId:` header form (design doc notation) is handled in code but not covered by tests. Logged as D-06 (P3).

### TASK-C-07 (Determinism + Self-Comparison) — APPROVED

Tests follow the established BATCH-01 pattern faithfully:
- HSM: 10-run loops on `simple_machine.cs` and `parallel_machine.cs`; layout `.State()` reorder invariant; malformed fixture (no-throw + warning)
- HSM self-comparison: same-file-twice and two-independent-catalog-instances, 2 fixtures each
- Blackboard determinism: 3 distinct inline-only shapes + 1 inline+heavy shape, all with 10-run loops
- Blackboard self-comparison: 3 scenarios including two independent sanitizer instances

All tests verify byte-identical output (`Assert.Equal(first, run)`), not just "not null" or structural equality.

---

## New Debt Registered

| ID | Description | Priority | Target |
|----|-------------|----------|--------|
| D-05 | HSM test missing: state with both stableId comment + visualId transition (verify no cross-injection) | P3 | BATCH-04 |
| D-06 | Blackboard `AssetId:` header form handled but not tested | P3 | BATCH-04 |
| D-07 | HSM 3-level nested Child test missing (brace-depth scan correctness for deep nesting) | P3 | BATCH-04 |
| D-08 | FakeCatalog/FakeAsset now across BTree+HSM test classes (4 classes). Supersedes D-04. | P3 | BATCH-03 |

---

## Approved With Notes

The implementation is clean, correct, and test coverage is genuine (ordering assertions throughout). The batch is approved for commit. D-08 (FakeCatalog consolidation) is carried into BATCH-03 since BATCH-03 already covers test infrastructure cleanup (D-04 scope).

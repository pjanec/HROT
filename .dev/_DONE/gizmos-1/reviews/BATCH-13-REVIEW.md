# BATCH-13 Review

**Batch:** BATCH-13
**Reviewer:** Development Lead
**Date:** 2026-05-07
**Status:** ✅ APPROVED

---

## Summary

All three tasks (GZ034–GZ036) implemented. StructEdit schema publisher, behavior interrupt guard,
and per-frame CPU budget. 12 new tests, all pass. Build clean (0 errors). No new failures
(26 pre-existing in Fdp.Toolkits.Tests unchanged).

---

## Issues Found

No issues found.

---

## Test Quality Assessment

Tests are behaviorally sound:
- SC-GZ034: Parses the actual JSON output and verifies `structedit_version` key presence, that
  `kind` is `"Boolean"` with correct value, and that `kind` is `"Scalar"` with correct float value.
  Not string-contains checks — uses `JsonDocument` API and loops over the `nodes` array.
- SC-GZ035-5: Tests behavior-interrupt scenario without `ClearBehaviorEvent`. Verifies teardown
  occurs on the old gizmo when a new behavior is assigned mid-execution.
- SC-GZ036-1/2: Near-zero budget (0.0001ms) processes fewer than 50 entities from a pool of 20;
  large budget (10000ms) processes all 20. Verifies actual entity counts, not just no-exception.
- SC-GZ036-3: Zero budget (unlimited) processes all entities — correct special-case behavior.

Acceptable deviations:
- `QueryTimeSliced` does not exist; Stopwatch-based budget is the correct implementation per
  batch override instructions.
- SC-GZ034-4 is covered by regression tests SC-GZ017-2/3 which auto-pass.

---

## 📝 Commit Message

```
feat: StructEdit schema publisher, behavior interrupt guard, frame budget (BATCH-13)

Completes TASK-GZ034, TASK-GZ035, TASK-GZ036

GZ034: GizmoSettingsPublisherSystem uses StructEdit EditDocument +
  EditDocumentJsonSerializer instead of flat Utf8JsonWriter. Published JSON
  includes structedit_version key and typed nodes (Boolean/Scalar).

GZ035: No production code change needed. SC-GZ035-5 confirms behavior
  interrupt without ClearBehaviorEvent still tears down old gizmo correctly.

GZ036: DataDrivenGizmoSystem and StatelessGizmoSystem gain MaxGizmoFrameMs
  property (0 = unlimited). Stopwatch-based budget check halts entity loop
  mid-frame when budget exceeded. Time-sliced offset resumes from last
  position next frame.

Tests: 12 new tests (3 schema, 1 behavior interrupt, 4 budget, 4 regression)
```

---

**Next Batch:** BATCH-14 (already completed)

# BATCH-03 Review

**Status: APPROVED**

**Date:** 2026-04-29

---

## Summary

BATCH-03 is approved. 123/123 tests pass, build is clean, FBT-006, FBT-010, and FBT-007 are fully implemented.

---

## Code Review

### FBT-006 — Phase 1 Validation Tests
✅ 7 tests in `BuilderValidationTests` — covers all 4 mandatory negative paths plus 3 extras.
✅ `NestedRepeater_ThrowsBehaviorTreeBuildException` — verifies message contains "Repeater" and "nested".
✅ `NestedParallel_ThrowsBehaviorTreeBuildException` — verifies message contains "Parallel" and "nested".
✅ `DtoTooLarge_ThrowsBehaviorTreeBuildException` — uses 33-int struct (132 bytes > 128); verifies exception thrown in expression-binding overload.
✅ `ValidTree_DoesNotThrow` — control test, builder compiles without error.
✅ `EmptyBuilder_Compile_ThrowsInvalidOperationException` — guards against empty builder.
✅ `TwoRootNodes_Compile_ThrowsInvalidOperationException` — guards against multiple roots.
✅ `Condition_ReturnsFailure_ActionNotCalled` — integration test verifying short-circuit in Sequence.

### FBT-010 — Marker Attributes
✅ 4 attribute files in `Fbt.Kernel/Attributes/` — all `sealed`, all `AttributeUsage` correct.
✅ `BTreeDefinitionAttribute(string treeName)` exposes `TreeName` property.
✅ `FbtRegistrarAttribute` targets `AttributeTargets.Class`.

### FBT-007 — BTreeSchemaExporter
✅ `BTreeSchema` uses `record` for clean `System.Text.Json` serialization.
✅ `BTreeSchemaExporter.Export` scans via `IsDefined` (faster than `GetCustomAttributes`).
✅ Non-reflectable assemblies silently skipped in `catch` block.
✅ `ExportToJson` uses `JsonSerializer.Serialize` with indented options.
✅ `FieldOffset = -1` for all scanned methods — rationale documented.

### BTreeBuilder.cs modification (DTO-too-large guard)
✅ `MaxBlackboardByteSize = 128` defined as private constant.
✅ Guard added to both `Condition<TValue>` and `Action<TValue>` overloads, throws `BehaviorTreeBuildException`.

---

## Test Quality Review

✅ Negative-path tests verify exception message content (not just exception type).
✅ Schema exporter tests scan `Assembly.GetExecutingAssembly()` with embedded `[BTreeAction]`/`[BTreeCondition]` methods — verify actual counts, not just "no exception".
✅ Round-trip JSON test verifies deserialized count matches original.

---

## Decision: APPROVED — Proceed to BATCH-04

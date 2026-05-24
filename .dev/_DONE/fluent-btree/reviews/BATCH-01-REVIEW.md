# BATCH-01 Review

**Status: APPROVED**

**Date:** 2026-04-29

---

## Summary

BATCH-01 is approved. All 94 tests pass, build is clean, and the implementation correctly covers FBT-001, FBT-002, and FBT-004.

---

## Code Review

### FBT-001 — `TreeCompiler.FlattenToBlob` public overload
✅ Correctly exposed as `public static`. Hash computation and validation run before returning.
✅ `CompileFromJson` now delegates to `FlattenToBlob` — no behavior change.
✅ `BehaviorTreeBuildException` added to `Fbt.Kernel`.
✅ `BuilderNode` gains parameterless constructor — required for programmatic construction.
✅ Nested Repeater/Parallel: promoted from validator warning to `BehaviorTreeBuildException` — correct approach to meet the spec requirement without breaking `TreeValidator` contract.

Minor: `CalculateStructureHash` missing `writer.Flush()` — noted in developer report as P3 debt. No behavior impact on .NET MemoryStream.

### FBT-002 — `BTreeBuilder<TBlackboard, TContext>`
✅ Fluent API is clean and chainable.
✅ Child builder shares the parent `ActionRegistry` — correct design.
✅ `Compile()` calls `FlattenToBlob` and populates `DebugMetadata`.
✅ `GetRegistry()` returns accumulated registry for `Interpreter` creation.
✅ Delegate key generation: `DeclaringType.FullName + "." + MethodName` — stable across builds.
✅ Auto-assign `Guid.NewGuid()` when `visualId == default` — correct.

### FBT-004 — `NodeDebugMetadata` + `BehaviorTreeBlob.DebugMetadata`
✅ `NodeDebugMetadata` placed in `Fbt.Kernel` — correct deviation to avoid circular dependency. Documented in report.
✅ `[NonSerialized]` correctly prevents serialization.
✅ Auto-labels are descriptive (Sequence, Wait(2.5s), Repeater(3x), etc.).
✅ Binary serializer round-trip: `DebugMetadata == null` after load — confirmed by test.

---

## Test Quality Review

✅ Tests verify **actual behavior**: node types, counts, subtree offsets, hash equality/inequality, exception message content, interpreter execution results, call counts, delegate deduplication.

✅ No "string presence" tests — all assertions check actual runtime values.

✅ Integration test `Compile_InterpreterExecutesCorrectly_ConditionFails` verifies the full pipeline (builder → blob → interpreter → tick).

✅ `DebugMetadata_BinarySerializerRoundTrip_MetadataIsNull` verifies both the serialization invariant AND that the deserialized blob still executes correctly.

---

## Design Decisions Accepted

1. **`NodeDebugMetadata` in `Fbt.Kernel`** — accepted. The spec says `Fbt.Compiler` but circular dependency prevention takes precedence. Documented.

2. **`BTreeBuilder<TBlackboard, TContext>`** — accepted. Using both type parameters is consistent with `ActionRegistry<TBlackboard, TContext>`.

3. **Nested nesting as `BehaviorTreeBuildException`** — accepted. Validator contract unchanged; compiler-level API enforces the constraint.

---

## Technical Debt Recorded

| Priority | Description |
|----------|-------------|
| P3 | `TreeCompiler.CalculateStructureHash` missing `writer.Flush()` before `ComputeHash`. No behavior impact on MemoryStream but fragile. |
| P3 | `MethodNames` deduplication uses `List.IndexOf` (O(n)). Should use `Dictionary<string, int>` for large trees. |
| P3 | `GetDelegateKey` does not null-guard `DeclaringType` — affects lambda delegates. Not relevant for current use cases. |

---

## Decision: APPROVED — Proceed to BATCH-02

# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-04-24
**Status:** APPROVED

---

## Issues Found

None found. All deviations from spec were justified and correct.

**`RemoveElementAtIndex` visibility:** Changed from `private static` to `internal static` to
allow T-CE07c to call it directly. This is correct — the test verifies real shift logic without
an ImGui context. The comment in the source explains the reason.

---

## Code Quality

**`ComponentEditDrawer`:**
- `internal sealed` — correct.
- `DrawContainerNode` / `DrawLeafNode` / `DrawUnsupportedNode` are well-separated private helpers.
- Picker rendering for entity and world location is symmetric — both use `node.JsonPath` (not nodeId).
- `GetDefaultForType` handles the null-safety edge case for reference types.
- `DrawPrimitiveInput` handles all required primitive types. `Enum` combo correctly finds current
  index by scanning `GetValues` and restores the typed boxed enum via `GetValue(current)`.
- Array element deletion: shifts down, then `Resize(Count - 1)`, then
  `MarkStructuralChange + RebuildDocument`. Correct sequence.

**`ComponentEditWindow`:**
- `IsVolatile = true; ShowInMenu = false; IsOpen = true` in constructor — correct.
- Liveness guard at top of `DrawClientArea` — correct; early return after `CloseAndCleanup()`.
- Rebuild check (`RebuildRequired`) before table rendering — correct.
- OK path: re-evaluates `_sessionGetter()` AFTER `Commit()` — correct (mid-frame disposal guard).
- `catch (EditValidationException)`: sets `_errorMessage`, does NOT call `CloseAndCleanup()` — correct.
- `internal ExecuteDrawLogic()` and `ExecuteOkLogic()` — clean test surface, documented.
- `internal string? ErrorMessage` — test accessor.
- `private void CloseAndCleanup()` — correctly private.

---

## Test Quality

**CE07:**
- T-CE07c: calls `RemoveElementAtIndex` with a real mock `IContainerBinding` — verifies shift
  logic directly, not just "no exception".
- T-CE07e/f: mock `IComponentPickerContext` verifies the `null`-guard and pending-state branching.
- Tests verify tree structure (`node.Children.Count`) using real StructEdit sessions.

**CE08:**
- T-CE08d: `FakeEditSession` tracks call order — `RebuildDocument` verified to precede other calls.
- T-CE08f: catches `EditValidationException`, asserts `IsOpen == true` and `ErrorMessage != null`.
- T-CE08g: `_sessionGetter` returns `null` post-commit — `SetComponent` not called, no throw.
- T-CE08e: `CloseAndCleanup()` called directly — `Dispose()` confirmed, `IsOpen == false`.

21 new tests, all pass. No shallow assertions.

---

## Verdict

**APPROVED.** 237/238 tests pass (1 pre-existing failure). All 21 required tests present.
`ComponentEditWindow` correctly implements the mid-frame disposal guard and validation-error retention.

---

## Commit Message

```
feat(comp-edit-1): Phase 3 component editor rendering (BATCH-03)

CE07 - ComponentEditDrawer: recursive ImGui renderer for EditDocument/EditNode trees.
  Handles SelectionRoot, container kinds (Struct/Class/Record/DynamicArray/InlineArray/FixedBuffer),
  leaf kinds (Scalar/Boolean/String/Enum), and unsupported kinds.
  DrawPrimitiveInput: float/int/double/long/ulong/short/ushort/byte/sbyte/bool/string/Enum.
  Picker rendering: MapPickableEntityAttribute and MapPickableWorldLocationAttribute, keyed
  on node.JsonPath. RemoveElementAtIndex shifts elements and calls Resize(Count-1).

CE08 - ComponentEditWindow: volatile ManagedWindow hosting ComponentEditDrawer.
  Liveness guard: re-evaluates sessionGetter() at start of each frame; closes on null/dead entity.
  Rebuild: calls RebuildDocument() when RebuildState == RebuildRequired before rendering.
  OK path: re-evaluates sessionGetter() after Commit() (mid-frame disposal guard).
  Validation errors: caught, shown as red text, window stays open for correction.
  Cancel: CloseAndCleanup() -> Dispose() + IsOpen=false.
```

---

**Next Batch:** BATCH-04 (Phase 4 — CE09 ComponentReflector wiring + CE10 host panel exposure)

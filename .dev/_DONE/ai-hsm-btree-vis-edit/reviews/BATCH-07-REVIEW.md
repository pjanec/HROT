# BATCH-07 Review

**Decision: APPROVED**

---

## Build

`dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 warnings (all prior warnings gone too).

## Test counts

| Project | Tests | Status |
|---------|-------|--------|
| `Hrot.Editor.AiShared.Tests` | 345 (21 new) | PASSED |
| `Hrot.BTree.Editor.Tests` | 208 | PASSED |
| `Hrot.Hsm.Editor.Tests` | 202 | PASSED |

---

## Code review notes

### BlackboardBinPacker.cs — APPROVED

- Master vars: always inline, same logic as before. Early-return on inline overflow is correct
  (heavy cannot rescue a master budget violation).
- Aggregated var loop: correctly tries to fit inline first (re-computing aligned offset),
  then spills to heavy tier. Heavy offset counter is independent, starts at 0.
- Alignment is applied consistently in both tiers (same cap-at-8 rule).
- `Repack()` sorts descending by alignment and calls `Pack(sorted)`. The note that only
  master vars are sorted here is acceptable for this slice; Phase 1.5d can extend when
  aggregated re-pack is needed.
- `PackWarning.HeavyMemoryExceeded` is checked before `InlineMemoryExceeded` in the final
  warning selection — this is consistent with the priority spec (worst-case first).
- Constants `MaxHeavyBytes = 928` correctly mirrors the BB design.

### BlackboardDtoEmitter.cs — APPROVED

- `EmitHeavy(model, heavyStructName)` produces the same four-line marker block format
  as `Emit`, using `FluentCSharpEmitterBase.EditorGeneratedMarker` (not a hardcoded string).
- Uses `[StructLayout(LayoutKind.Sequential)]` (correct for heavy; inline uses Explicit).
- Caller is responsible for pre-filtering the model fields to heavy-only, which is the
  cleanest design for testability.
- Usings collected correctly; empty struct case handled gracefully.

### BlackboardAuthoringWindow.cs — APPROVED

- `UnboundRequirementViewModel` is a well-typed view-model record with all necessary fields.
- `BlackboardWindowViewModel` extended cleanly with `TotalHeavyBytes`, `InlineBudget`,
  `HeavyBudget`, `RequiresHeavyComponent`, `UnboundRequirements`.
- `BuildViewModel` projects `AggregationResult.Requirements` to `UnboundRequirementViewModel`
  rows directly — no collapse, one row per requirement as specified.
- Aggregated descriptors derived from requirements for packing — correct approach.
- Early-return paths all populate the new fields with correct defaults (0, false, empty list).
- Edge case: `rawVars.Count == 0 && unboundRows.Count == 0` — both counts checked for early
  return, so a non-null `aggregationResult` with requirements is correctly handled even
  when the asset has no declared variables.
- `BudgetColor` thresholds (80% amber, 100% red) match the design spec.
- Right-click "Promote to new variable" menu item is present but correctly deferred
  (empty action, comment notes Phase 1.5d) per spec.
- Dual-tier header rendering is correct.

### Test quality — APPROVED

- **Heavy-tier packing tests**: Cover all edge cases including: fits-inline, overflow-to-heavy,
  master-overflow-no-heavy, offset-starts-at-zero, alignment-in-heavy-tier,
  TotalHeavyBytes zero/nonzero. All tests are specific with precise assertions.
- **EmitHeavy tests**: Marker block verified via constant (not string copy), field inclusion,
  struct name parameter, and empty-fields case. The fix to use specific field patterns
  rather than broad "DoesNotContain('public ')" is pragmatic and correct.
- **Window tests (1c-03, 1c-05)**: Correctly use a `MutableBbAssetForWindowTests` stub.
  Tests for unbound rows cover empty case, count, DtoTypeName, RequiredByPath.
  Budget tests cover: no-heavy flag, InlineBudget=100, HeavyBudget=928, all-fit-inline,
  and overflow-to-heavy with 25-int fixture.

---

## Deferred items (by design)

- Promotion action on right-click: Phase 1.5d (TASK-BB-1d-*).
- `Repack()` toolbar button in `DrawClientArea`: method exists; UI binding deferred.
- Heavy companion file is implemented but caller wiring (save pipeline) deferred to integration.

---

## Tasks completed in this batch

- TASK-BB-1c-03: Unbound Sub-Tree Requirements section -- **DONE**
- TASK-BB-1c-04: Heavy-tier bin-packing + companion emit + Repack -- **DONE**
- TASK-BB-1c-05: Memory budget indicator -- **DONE**

Phase 1.5c is now COMPLETE.

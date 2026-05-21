# BATCH-12 Review — TASK-CP-004: Stage 7 Emit

**Status:** APPROVED

---

## Summary

Stage 7 (C# code generation) is fully implemented. The pipeline now produces valid C# source for all three dispatch kinds (Library, AiPrimitive, Instance). All 7 Stage7 test scenarios pass; the 175-test baseline is preserved.

---

## Success Criteria Assessment

| SC | Criterion | Result |
|----|-----------|--------|
| SC1 | Library emission: correct class name, `BlueprintId` const, `BlueprintRegistryStaging` registrar | PASS |
| SC2 | AiPrimitive emission: Params/WorkingState structs, TickCore, BTreeTick, HsmActivity, BehaviorRegistry param, static HsmActionDispatcher | PASS |
| SC3 | Instance emission: State struct, `BlueprintLatentCursor Cursor`, `uint instanceVersion` in Tick | PASS |
| SC4 | Determinism: same asset compiles to identical source twice | PASS |
| SC5 | `IrTerm_Suspend` throws `InvalidOperationException` containing "should have been lowered" | PASS |
| SC6 | Class name format: `{Name}_{8-hex-chars}_Bp` (Q-18.4) | PASS |
| SC7 | Instance custom event `Event_OnHit` has `float deltaTime` parameter (Q-18.3) | PASS |

---

## Patch Compliance

- **Q-18.4** (class name suffix): ✓ `{SanitizedName}_{BlueprintId:X8}_Bp` used in all three dispatch kinds and registrar class name
- **Q-18.1** (`uint instanceVersion`): ✓ Present in both `Tick` method signature and `TickThunk` signature (matching `TickDelegate` exactly)
- **Q-18.3** (`float deltaTime` in event methods): ✓ `Event_{Name}` methods include `float deltaTime` before custom args
- **Patch C1** (registrar uses `BlueprintRegistryStaging`): ✓ All `Register` methods use `BlueprintRegistryStaging staging` — never raw `BlueprintRegistry`
- **Patch C1** (static `HsmActionDispatcher` calls): ✓ No instance parameter emitted for HSM registration

---

## Test Quality Assessment

Tests verify structural properties of generated source rather than byte-identical snapshots, which is appropriate for the initial CP-004 scope. Full golden snapshot tests are deferred to CP-006 as designed.

Each test is independent, uses the full compiler pipeline (SC1-SC4, SC6-SC7) or targeted Stage7_Emit.Run (SC5), and covers distinct correctness properties. The SC6 hex-suffix check is particularly strong — it validates not just presence of `_Bp` but that exactly 8 valid hex characters precede it.

One minor note: `IrOp_RaiseCustomEvent` emits a comment placeholder rather than functional code. This is correctly scoped to Slice 1 (the event dispatch infrastructure is deferred to a later phase) and documented in the batch report.

---

## Files Changed

14 files modified or created (see BATCH-12-REPORT.md for full list). Notable additions:

- `Hrot.Blueprints.Core/Compiler/Emit/`: 6 new files (`BlockEmitter`, `StatementEmitter`, `TerminatorEmitter`, `ChannelCommandLowering`, `LibraryEmitter`, `AiPrimitiveEmitter`, `InstanceEmitter`)
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs`: `InstanceVersion` field added (was missing from the struct)
- `Hrot.Blueprints.Tests/Stage7Tests.cs`: 7 new tests

---

## Final Test Counts

| Metric | Count |
|--------|-------|
| Passing (total) | 182 |
| Skipped | 3 |
| Failed | 0 |

Baseline preserved (175 → 182, +7 Stage7 tests).

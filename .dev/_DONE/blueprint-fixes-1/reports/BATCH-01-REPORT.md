# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot  
**Date:** 2025-07-29  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| BPF-014 | [x] | Added `IrOp_ReadCursorWaitUntilTime` IR op; fixed `WaitLowering_Instance.cs` to emit it; added `StatementEmitter` case emitting `s.Cursor.WaitUntilTime` |
| BPF-015 | [x] | Fixed `StatementEmitter` `IrOp_DebugProbe_NodeEnter` and `IrOp_DebugProbe_PinValue` cases to emit real calls (not `// [DebugProbe]` comments) |
| BPF-016 | [x] | Removed `float deltaTime` from `EmitEventMethod` signature; fixed `EmitEventThunk` to call `Event_{name}(ref s, view, ecb, self, time)` with `default(T)` for each input; fixed `IrOp_PollEngineEvent` emit to include payload field args and drop stray `deltaTime` |
| BPF-019 | [x] | `BuildReturnTerminator` now receives and uses the current `BlockBuilder` for `ResolveDataPin` instead of the last-allocated block's statement list |
| BPF-020 | [x] | `IrOp_RaiseCustomEvent` emit case changed from `// TODO` comment to `Event_{evtName}(ref {sv}, view, ecb, self, time{extraArgs});` |
| BPF-039 | [x] | `GetOrdered` residuals appended with `.OrderBy(f => f.Id)` for deterministic field ordering |
| BPF-040 | [x] | `MetadataReferenceResolver.ForRuntimeAssemblies` sorts by `a.Location` using `StringComparer.Ordinal` |
| BPF-041 | [x] | Replaced size heuristic (`pdb.Length > 500`) with `System.Reflection.Metadata`-based content extraction; strips UTF-8 BOM before comparing to `result.GeneratedSource` |
| BPF-050 | [x] | Added `FullPipeline_IsParallelDeterministic` Theory test running N=4 parallel compilations and asserting identical output |

---

## Testing Results

**Unit Tests Passed:** 823 / 831  
**Skipped:** 8 (pre-existing, unrelated to this batch)  
**Failed:** 0

**Baseline before batch:** 804 passing, 8 skipped (812 total)  
**After batch:** 823 passing, 8 skipped (831 total)  
**New tests added:** 19

**New test files:**

| File | Tests |
|------|-------|
| `BPF014_LatentDelayEmitTests.cs` | 2 |
| `BPF015_DebugProbeEmitTests.cs` | 3 |
| `BPF016_EventMethodEmitTests.cs` | 2 |
| `BPF019_ReturnTerminatorTests.cs` | 2 |
| `BPF020_RaiseCustomEventEmitTests.cs` | 3 |
| `BPF039_GetOrderedDeterminismTests.cs` | 2 |
| `MetadataReferenceResolverTests.cs` (+2 tests) | 2 |
| `Stage8Tests.cs` (BPF-041 assertion upgraded) | — |
| `CompilerDeterminismTests.cs` (+1 test, 3 cases) | 3 |

All tests verify actual emitted behavior: non-comment line matching, field reference checking, CDI content extraction, and deterministic byte-for-byte output comparison.

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Several tests required iteration:

- **BPF015 test**: A simple `Entry().Return()` graph produces no IR statements (EventEntryNode generates none), so Stage6 `DebugProbeInsertion` found no `Debug.NodeId` and skipped insertion. Fixed by using `Entry().Delay(0.5f).Return()` which provides a statement with valid node debug info.

- **BPF016 test**: The original test checked for `float deltaTime` anywhere in the emitted source. Tick and TickThunk legitimately carry `float deltaTime` (it is part of their time-step API). Fixed the test to scan only the `Event_OnHit` implementation method's parameter lines (excluding `_Thunk` and Tick).

- **BPF019 test**: Used `IrTerm_ReturnStatus` assertion but the test used an Instance blueprint (whose return terminator is `IrTerm_Return`, not `IrTerm_ReturnStatus`). Fixed assertion to match Instance dispatch.

- **BPF020 test**: `Event_OnFire(` matched both the method declaration (`public static void Event_OnFire(`) and the call site. Added filter `!l.Contains("void Event_OnFire(")` to exclude declarations.

- **BPF039 test**: Used `GraphInputDecl`/`GraphOutputDecl` type names that don't exist; the correct type is `ParameterDecl`. Also hit ambiguity between `Hrot.Blueprints.Core.Assets.BlueprintDispatchKind` and `Fdp.Toolkit.Blueprints.BlueprintDispatchKind`; resolved with a `using AssetDispatch = ...` alias.

- **BPF041 test**: The per-document CDI iteration worked but produced a UTF-8 BOM at the start of the extracted source bytes. Added BOM stripping before the `Assert.Equal` comparison.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` and `Stage8_PdbContainsEmbeddedSource` depend on `BlueprintCompiler.RoslynFinalizer` being set by `Hrot.Blueprints.Core`'s `[ModuleInitializer]`. When run in isolation they fail because `Hrot.Blueprints.Core.dll` is not loaded. These tests pass only as part of the full suite. A dedicated fixture that wires `RoslynFinalizer` directly would make them independently runnable.

- `EmitEventThunk` and `Tick`/`TickThunk` still carry `float deltaTime` in their external-facing thunk signatures. The internal `Event_*` method no longer takes it (BPF-016 fix), but callers of the thunk still pass `deltaTime`. This is intentional per the runtime delegate contract, but worth documenting so future maintainers understand the asymmetry.

**Q3: What design decisions did you make beyond the instructions?**

- For BPF-014, added a dedicated `IrOp_ReadCursorWaitUntilTime` IR op (mirroring the existing `IrOp_WriteCursorWaitUntilTime`) rather than reusing a generic read op. This keeps the IR self-documenting and matches the existing write/read pair convention.

- For BPF-039's test, manually constructed a `BlueprintAsset` with explicit `ParameterOrder` and three `ParameterDecl` entries using fixed GUIDs (`aaaaaaaa-...-001/002/003`) to make the sort order fully deterministic and obvious. Used Stage5_Schedule with a stripped-down graph (EventEntryNode + ReturnNode with exec link) sufficient to pass validation.

- For BPF-041, chose the per-document CDI enumeration path (not the full table scan) after observing the full-table scan returned null even though the per-document path found the CDI. This is the canonical approach in the Portable PDB spec.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- `DebugProbeInsertion.InsertProbes` skips blocks whose first statement has `Debug?.NodeId == null`. EventEntryNode and ReturnNode produce no statements, so a simple `Entry().Return()` graph never receives DebugProbe instrumentation. The fix to BPF-015 tests requires at least one latent or execution node.

- `EmitEventThunk` previously called `Event_{name}(ref s, view, ecb, self, time, deltaTime)` -- dropping `deltaTime` fixes BPF-016 for zero-input events. For events with payload inputs, the thunk now passes `default(T)` for each, matching the runtime serialization deferred-decode pattern.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `GetOrdered` iterates `dict.Values.OrderBy(f => f.Id)` on every call. For large assets with many parameters this is O(n log n) per invocation. If called frequently a sorted insert or a sorted cached result could be beneficial, but for typical blueprint sizes this is negligible.

- `MetadataReferenceResolver.ForRuntimeAssemblies` now sorts by Location string at resolver construction time. This is a one-time cost at compilation startup and does not affect hot paths.

---

## Outstanding Issues / Next Steps

None. All 9 tasks are complete and all tests pass.

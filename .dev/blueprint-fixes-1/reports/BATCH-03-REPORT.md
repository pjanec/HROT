# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** AI (GitHub Copilot)  
**Date:** 2026-05-31  
**Status:** Complete

---

## Task Completion

| Task ID    | Status | Notes |
|------------|--------|-------|
| CORR-02-1  | Done   | `GetCurrentStateSnapshot_AiPrimitive` field-value tests added |
| CORR-02-2  | Done   | HitCount accumulation fix + second-tick HitCount assertion added |
| BPF-017    | Done   | ActionNames keyed by hash ID, not position |
| BPF-022    | Done   | `HsmFluentEmitter` emits `.DeferEvent()` calls in ascending ID order |
| BPF-023    | Done   | `HsmDebugSession` decodes active leaves from metadata |
| BPF-024    | Done   | `StepOut` predicate changed to `Phase == Activity` |
| BPF-025    | Done   | `StableId` assigned from `metadata.StateStableIds` (content-based) |

---

## Testing Results

**Hrot.Blueprints.Tests (non-AllocationFree):** 854 passed, 8 skipped, 0 failed  
**Hrot.Hsm.Editor.Tests:** 264 passed, 0 failed  
**Fhsm.Tests:** 296 passed, 2 pre-existing failures unrelated to BATCH-03 (`OutputLane_Conflict_Detected`, `InfiniteLoop_Detected_And_Stops` -- both in unmodified test files)

**New tests added this batch:**

- `DebugMapExtensionTests.GetCurrentStateSnapshot_AiPrimitive_ReturnsFieldValue_WhenHashMatches` (CORR-02-1)
- `DebugMapExtensionTests.GetCurrentStateSnapshot_AiPrimitive_ReturnsEmptyFields_WhenHashMismatches` (CORR-02-1)
- `DebugMapExtensionTests.OnNewTick_ResetsDedupSet_AllowingSecondTickHit` HitCount assertion (CORR-02-2)
- `HsmAssetProjectionTests.BuildMachineMetadata_ActionNames_KeyedByHashId_MatchingBlobActionId` (BPF-017)
- `HsmAssetProjectionTests.BuildMachineMetadata_ActionNames_MultipleActions_AllKeyedByHashId` (BPF-017)
- `HsmAssetProjectionTests.Project_state_StableIds_come_from_metadata_not_layout_position` (BPF-025)
- `HsmAssetProjectionTests.Project_layout_applied_by_StableId_not_flat_position` (BPF-025)
- `HsmFluentEmitterTests.Emit_contains_DeferEvent_calls_for_each_deferred_id` (BPF-022)
- `HsmFluentEmitterTests.Emit_omits_DeferEvent_when_no_deferred_ids` (BPF-022)
- `HsmDebugSessionTests.Update_WithBrainHsm64_ActiveLeafIds_DecodedViaMetadata` (BPF-023)
- `HsmDebugSessionTests.Update_WithBrainHsm64_Slot0xFFFF_NotIncludedInActiveLeaves` (BPF-023)
- `HsmDebugSessionTests.StepOut_does_not_pause_while_in_Entry_phase` (BPF-024)
- `HsmDebugSessionTests.StepOut_pauses_when_Activity_phase_reached` (BPF-024)
- `HsmDebugSessionTests.StepOver_pauses_when_MicroStep_changes` (BPF-024)

---

## Changed Files

| File | Task | Change |
|------|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` | CORR-02-2 | Added `IncrementHitCountOnly(bp)` helper; `OnNodeEnter` accumulates HitCount when already paused or dedup fires |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/DebugMapExtensionTests.cs` | CORR-02-1, CORR-02-2 | Added `BlackboardStubSimulationView`; added 2 `AiPrimitive` field-value tests; added HitCount assertion to existing dedup test |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmFlattener.cs` | BPF-017 | `BuildActionTable` and `BuildGuardTable` changed from `private static` to `internal static` |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmEmitter.cs` | BPF-017 | `BuildMachineMetadata`: replaced positional `actionIdx++` loop with hash-keyed iteration over `BuildActionTable`/`BuildGuardTable` results |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Emit/HsmFluentEmitter.cs` | BPF-022 | `BuildStateConfig`: added `foreach (var eventId in s.DeferredEventIds.OrderBy(id => id))` to emit `.DeferEvent()` calls |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAssetProjector.cs` | BPF-025 | Replaced positional layout block with `metadata.StateStableIds` lookup; layout is then applied by `StableId` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmDebugSession.cs` | BPF-023, BPF-024 | Added `_metadata`/`_metadataAssetId` fields + `SetMetadata()`; `Update()` calls `DecodeLeaves64/128()`; `StepOut`/`StepOver` evaluated before trace guard; `StepInto` evaluated after trace drain |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmAssetProjectionTests.cs` | BPF-017, BPF-025 | Added 4 tests; fixed `blob.States.First()` (Span -- no LINQ) to use `foreach` |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmFluentEmitterTests.cs` | BPF-022 | Added 2 tests |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Debug/HsmDebugSessionTests.cs` | BPF-023, BPF-024 | Added 5 tests; fixed pre-existing `bufBase` undefined reference in trace buffer test; added `SpyCoordinator` |

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three issues surfaced during implementation:

1. **`blob.States` is `ReadOnlySpan<StateDef>` -- no LINQ.** The test initially called `blob.States.First(s => ...)` which fails to compile because `ImmutableArrayExtensions.First<T>` type inference breaks on `ReadOnlySpan`. Fixed by using a `foreach` loop to find the first non-sentinel action ID.

2. **`StepOut`/`StepOver` never reached step-mode evaluation.** The `Update()` method returned early if the entity lacked `HsmTraceWorkingMemory1024`, and the step-mode block was placed *after* that return. `StepOut` (waiting for `Phase == Activity`) and `StepOver` (waiting for `MicroStep` change) don't require trace records. Fixed by splitting the step-mode check: `StepOut`/`StepOver` evaluated before the trace guard; `StepInto` (which requires `_nodeProcessedSinceStep`, set by trace drain) evaluated after.

3. **`CORR-02-2` paused-path HitCount.** The original `HandleBreakpointHit` guards on `_isPaused` to prevent re-pausing. When already paused, a second entity hitting the same breakpoint must still accumulate `HitCount` but must not re-pause. Added `IncrementHitCountOnly(bp)` for both the already-paused path and the same-tick dedup path.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `HsmDebugSession.Update()` had a single `if (_stepMode != StepMode.None)` block at the bottom, which implicitly coupled all step modes to the trace component. The `StepOut`/`StepOver` modes have no logical dependency on trace, so this was fragile. The fix makes the dependency explicit.
- `HsmDefinitionBlob.States` returning `ReadOnlySpan<StateDef>` is good for perf but means LINQ cannot be used; this is a common footgun for new tests. A `ToArray()` call works but allocates.

**Q3: What design decisions did you make beyond the instructions?**

- For BPF-025, `StableId = Guid.NewGuid()` is used as a fallback when `metadata.StateStableIds` has no entry or the value is `Guid.Empty`. This matches the previous positional-sort behavior for machines that predate the content-based ID system, ensuring existing tests that don't supply IDs still work.
- For BPF-023, `SetMetadata()` is kept as a separate call rather than passed through the constructor, preserving the existing constructor signature and making it opt-in (only the kernel adapter sets it).

**Q4: What edge cases did you discovered that weren't mentioned in the spec?**

- `ActiveLeafIds` slot value `0xFFFF` is a sentinel for "unused" -- tested in `Update_WithBrainHsm64_Slot0xFFFF_NotIncludedInActiveLeaves`.
- When `metadata.StateStableIds` has no key for a flat index (e.g. newly added state not yet in blob), `Guid.NewGuid()` ensures the projected state still has a valid, non-empty `StableId`.
- CORR-02-2: A breakpoint can be in `_firedBreakpointsThisTick` *and* `_isPaused = true` simultaneously (first entity caused the pause, second entity in same tick triggers the dedup branch). Both paths needed `IncrementHitCountOnly`.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `DecodeLeaves64/128` allocates a `List<Guid>` per `Update()` call per entity. For high-frequency update loops this could be a concern, but debug sessions are typically inactive in production and the allocation is small (2 or 4 elements max). No action needed.
- `HsmFlattener.BuildActionTable` is called twice in `BuildMachineMetadata` (once for actions, once for guards). The original code did a single pass; the new code makes two passes. The tables are small (single-digit to tens of entries) so this is negligible.

---

## Outstanding Issues / Next Steps

- The 2 pre-existing `Fhsm.Tests` failures (`OutputLane_Conflict_Detected`, `InfiniteLoop_Detected_And_Stops`) are unrelated to BATCH-03 and were failing before this batch. They should be tracked separately.
- `SetMetadata()` on `HsmDebugSession` is a stub until the kernel adapter is wired up. The tests cover the decoding logic but the call site (adapter layer) is out of scope for this batch.

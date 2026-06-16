# BATCH-11 Review (Feature B — live value column in the Blackboard variable window)
**Status:** ✅ APPROVED   **Date:** 2026-06-16

## Summary
The Blackboard variable window now shows a "Value" column with each authored variable's live value, read from the selected entity's `BrainBlackboard`, gated on a name-match between the asset and the entity's active behavior. New `ILiveBlackboardValueProvider` seam (interface in AiShared, impl + DI in Hrot.Editor), injected optionally. Verified by source read + running the provider tests.

## Verification (lead-run)
- `Hrot.Editor.Tests --filter LiveBlackboardValueProvider`: **6/6 pass**.
- Agent-reported (consistent with the optional-injection design): `Hrot.Editor.AiShared.Tests` 1105/0 (back-compat — existing window ctor usages unaffected by the optional param), `Hrot.Editor.Tests` 194 (+6), both editor projects build 0 errors.
- Read `LiveBlackboardValueProvider`: correct 6-step guard chain; **name-match gate is right** (`registry.TryGetId(asset.Name, out id)` then `id != bs.ActiveBehaviorHash → Empty`); per-variable try/catch + outer try/catch ⇒ never throws into the UI; reads only `BrainBlackboard` params via `Marshal.PtrToStructure` at `ManagedBlackboardVariable.ByteOffset` (scope-correct — no WorkingState).
- Read the key test `LiveValues_SelectedEntityRunningAsset_ReturnsFormattedValues`: writes a real `CounterParams{Counter=7,Threshold=1000}` into a `BrainBlackboard` (`Marshal.StructureToPtr`), fake `IInspectableSession` returns it + a matching `BehaviorState`, asserts the map yields `Counter=7`/`Threshold=1000`. Genuine runtime assertion. Plus the no-selection / behavior-mismatch / projection-failure cases.

## Issues Found
None. Optional `ILiveBlackboardValueProvider? = null` ctor param keeps all existing call sites + tests compiling (AiShared.Tests 1105 confirms). `VariableViewModel` left pure (live values passed as a separate map) — clean design-time/runtime separation as specced.

## Deviations
- Two provider instances constructed (BTree + HSM perspectives), each bound to its own `EditorSelectionStore` via lazy `() => _fdpRepoAdapter` / `() => _behaviorRegistry`. Reasonable given the per-perspective selection stores.
- Value formatting: reflect public fields+props → `"Field=val, …"`; primitive → `ToString()`. Matches spec.

## Verdict
APPROVED. Feature B complete. Live values now visible in both the Entity Inspector (BATCH-10) and the Blackboard variable window (this batch), selected-entity scope.

## Commit Message
```
feat(inspector): live value column in Blackboard variable window (BATCH-11, Feature B)

Selected-entity MVP.
- New ILiveBlackboardValueProvider seam (AiShared interface; Hrot.Editor impl)
- LiveBlackboardValueProvider: name-match gate (TryGetId(asset.Name)==ActiveBehaviorHash),
  reads selected entity's BrainBlackboard via ManagedBlackboardVariable offsets
  (Marshal.PtrToStructure), formats multi-field DTOs as "F=v, ...". Never throws into UI.
- DI: two providers (BTree+HSM) wired in EditorSubsystem from _fdpRepoAdapter + _behaviorRegistry
  + EditorSelectionStore; injected optionally into BlackboardAuthoringWindow (default null)
- VariablesPanelControl: 5th "Value" column (live value, "—" when no matching selected entity)
- VariableViewModel kept pure (values passed as a separate map)
Tests: 6 provider unit tests (real value assertion Counter=7/Threshold=1000 + gate/empty/throw-safety);
Editor.AiShared.Tests 1105/0, Editor.Tests 194. Editor not hot-reloaded — rebuild+restart to view.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

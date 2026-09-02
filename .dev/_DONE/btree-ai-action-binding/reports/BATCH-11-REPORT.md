# BATCH-11 Report — Selected-Entity Live Value Column (Feature B MVP)

**Date:** 2026-06-16
**Branch:** btree-visual-edit
**Status:** COMPLETE — 0 build errors, 0 new test failures

---

## Summary

Implemented a live "Value" column in the Blackboard variable window that shows each authored
variable's current runtime value, read from the selected entity's `BrainBlackboard`, gated
on a name-match between the opened asset and the entity's active behavior.

---

## Files Created

### `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ILiveBlackboardValueProvider.cs`
New interface seam in `Hrot.Editor.AiShared`. Single method:
```csharp
IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset);
```
Deliberately placed in the shared assembly so `BlackboardAuthoringWindow` can accept it
without taking a hard dependency on the `Hrot.Editor` production assembly.

### `Hrot/Subsystems/Hrot.Editor/LiveBlackboardValueProvider.cs`
Production implementation. Ctor takes:
- `Func<IInspectableSession?> sessionFactory` — lazy lambda captures `_fdpRepoAdapter`
- `Func<BehaviorRegistry?> registryFactory` — lazy lambda captures `_behaviorRegistry`
- `EditorSelectionStore store` — reads `SelectedEntity`

Seven-step guard chain: null entity → dead session → missing `BehaviorState` → name-match
gate (`TryGetId` + hash compare) → missing `BehaviorDefinition` → missing
`ManagedBlackboardVariables` → per-variable `ProjectAndFormat`.

`ProjectAndFormat` uses `Marshal.PtrToStructure((IntPtr)(bb.BehaviorParameters + offset), type)`.
`FormatValue` reflects public fields and properties; multi-field structs render as
`"Field1=val1, Field2=val2"`, primitives fall back to `ToString()`.

Both methods are `internal static` for unit-test access via `InternalsVisibleTo`.

### `Hrot/Subsystems/Hrot.Editor.Tests/LiveBlackboardValueProviderTests.cs`
6 unit tests (all green):

| Test | Purpose |
|---|---|
| `LiveValues_SelectedEntityRunningAsset_ReturnsFormattedValues` | Real value assertion: `Counter=7, Threshold=1000` |
| `LiveValues_NoSelection_ReturnsEmpty` | Guard: null `SelectedEntity` |
| `LiveValues_SelectedEntityRunningDifferentBehavior_ReturnsEmpty` | Name-match gate: hash 99 ≠ 42 |
| `LiveValues_ProjectionFailure_OmitsVariable_DoesNotThrow` | Throwing session: variable omitted, no throw |
| `FormatValue_MultiFieldStruct_FormatsAllFields` | Direct internal method test |
| `FormatValue_PrimitiveInt_ReturnsToString` | Fallback path for primitives |

---

## Files Modified

### `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
- Added `private readonly ILiveBlackboardValueProvider? _liveValueProvider;`
- Added optional ctor param `ILiveBlackboardValueProvider? liveValueProvider = null` (last, preserves all existing call sites)
- Before `_variablesControl.DrawSingle`: calls `GetLiveVariableValues(asset)` when provider is set, passes result into `DrawSingle`

### `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`
- `DrawSingle`, `DrawSection`, `DrawTable` each gained `IReadOnlyDictionary<string, string>? liveValues = null`
- `BeginTable` changed from 4 to 5 columns
- Added `"Value"` column (`WidthStretch`) between "Bytes" and "##rmv"
- Row rendering: shows `lv` when found in map; `ImGui.TextDisabled("—")` otherwise

### `Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs`
- Added optional ctor param `ILiveBlackboardValueProvider? liveValueProvider = null`
- Passes it through to `BlackboardAuthoringWindow` ctor

### `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- `RegisterWindows()`: constructs two `LiveBlackboardValueProvider` instances (one for BTree,
  one for HSM perspective), each with appropriate `EditorSelectionStore`
- Both are passed as `liveValueProvider:` to their respective `PerspectiveWorkspaceRegistrar`

---

## Build Results

| Project | Errors | Warnings |
|---|---|---|
| `Hrot.Editor.AiShared` | 0 | 0 |
| `Hrot.Editor` | 0 | 0 |
| `Hrot.Editor.AiShared.Tests` | 0 | 0 |
| `Hrot.Editor.Tests` | 0 | 0 |

## Test Results

| Suite | Before | After | Delta |
|---|---|---|---|
| `Hrot.Editor.AiShared.Tests` | 1105 pass | 1105 pass | 0 |
| `Hrot.Editor.Tests` | 188 pass | 194 pass | +6 new |

---

## Design Decisions

- **Optional ctor injection**: `liveValueProvider = null` default preserves all existing
  `BlackboardAuthoringWindow` and `PerspectiveWorkspaceRegistrar` instantiations (DI tests,
  recipe tests) without modification.
- **Lazy factory pattern**: `() => _fdpRepoAdapter` / `() => _behaviorRegistry` lambdas
  ensure the provider reads the field values at call time, not at construction time (those
  fields are `null` during `RegisterWindows()` if not yet initialized).
- **Two providers for two perspectives**: BTree and HSM registrars receive separate provider
  instances bound to their own `EditorSelectionStore`, keeping per-perspective selection state
  independent.
- **Never throws into UI**: outer `try/catch` in `GetLiveVariableValues` returns `Empty` on
  any failure; inner per-variable `try/catch` skips failed projections individually.
- **Separate liveValues map**: `BuildViewModel` is untouched and remains pure; no runtime
  field added to `VariableViewModel`.

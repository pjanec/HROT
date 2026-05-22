# BATCH-26 Report

## Status: COMPLETE

All three fixes (FIX-1, FIX-2, FIX-5) are fully implemented and tested.
Build: 0 errors, 2 pre-existing warnings (xUnit2029).
Test results: 479 passed, 3 pre-existing failures, 7 skipped.

---

## FIX-1: BehaviorRegistry BTree integration

### Files changed
- `FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/AiPrimitiveEmitter.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Emit/CSharpEmitter.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Stage7Tests.cs`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/MoveToAndFire.cs.txt` (regenerated)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/HasVisibleTarget.cs.txt` (regenerated)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/DoorActor.cs.txt` (regenerated)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/LibraryMath.cs.txt` (regenerated)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/InstanceCounter.cs.txt` (regenerated)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Emit/HealthRegen.cs.txt` (regenerated)

### Changes
- `BehaviorRegistry`: Added `BlueprintBTreeActionDelegate` and `BlueprintBTreeConditionDelegate`
  delegate types. Added `_bTreeActions`/`_bTreeConditions` dictionaries. Added
  `RegisterAction`, `RegisterCondition`, `TryGetAction`, `TryGetCondition` methods.
  `Clear()` extended to clear the new dictionaries.
- `AiPrimitiveEmitter`: `BTreeTick` return type changed to `global::Fbt.NodeStatus`.
  Return expression uses `(global::Fbt.NodeStatus)(int)TickCore(...)` cast.
  `HsmActivity` third parameter changed from `void*` to
  `global::Fhsm.Kernel.Data.HsmCommandWriter*` to match the function pointer type
  used in `HsmActionDispatcher.RegisterAction`.
  Removed `[UnmanagedCallersOnly]` from both `HsmActivity` and `HsmGuard` because
  `HsmActionDispatcher` uses managed function pointers (`delegate* <...>`), not
  unmanaged ones; `[UnmanagedCallersOnly]` prohibits casting to managed function pointers
  and caused CS8757.
- `CSharpEmitter.EmitAiPrimitiveRegistration`: Emits `behReg.RegisterAction(...)` and
  `behReg.RegisterCondition(...)` for BTree hostings. Emits
  `HsmActionDispatcher.RegisterAction(...)` and `HsmActionDispatcher.RegisterGuard(...)`
  for HSM hostings. `Register` method signature now uses `unsafe void` to allow
  the function-pointer cast expressions in the method body (Stage8 Roslyn CS0214 fix).
- `Stage7Tests`: SC2 assertion updated for `Fbt.NodeStatus` return type. SC2b test
  added for `BTreeCondition` hosting. Stage7 Library assertion updated to match
  `public static unsafe void Register(...)`.

### Test results
All 8 Stage7 tests pass. All 2 Emit snapshot tests for AiPrimitive pass after
regeneration.

---

## FIX-2: QuickReloadService full pipeline

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/BlueprintSignatureBuilder.cs` (created)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Reload/QuickReloadService.cs` (replaced)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/QuickReloadServiceTests.cs` (replaced)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Snapshots/Demos/MoveToAndFire.cs.txt` (regenerated)

### Changes
- `BlueprintSignatureBuilder`: New static class with `FromInMemoryAsset(BlueprintAsset)`
  that builds a `BlueprintSignature` from an in-memory asset without disk I/O.
- `QuickReloadService`: Full 6-step async pipeline in `TriggerAsync`:
  1. Build signature from asset
  2. Compile through Stage7 (IR to C# source)
  3. Compile through Stage8 (Roslyn, `EmitPdbWithEmbeddedSource: true`)
  4. Load the compiled assembly into an `AssemblyLoadContext`
  5. Apply reload via `AiHotReloadCoordinator.ApplyQuickReload`
  6. Return `QuickReloadResult` with duration
  Uses `IOutputConsole.LogError(...)` (not `LogDiagnostic`) for compile diagnostics
  because the diagnostic type from the compiler pipeline is not the Roslyn
  `Diagnostic` type expected by `LogDiagnostic`.
  Exposes `LastSignaturesUsedForTesting` for SC3 introspection.
- `QuickReloadServiceTests`: 4 tests (SC1-SC4):
  - SC1: Constructor injects dependencies without throwing.
  - SC2: `TriggerAsync` with a Library blueprint returns `Succeeded=true`.
  - SC3: `LastSignaturesUsedForTesting` reflects the compiled asset name.
  - SC4: `TriggerAsync` with an AiPrimitive (BTreeAction + HsmAction) returns
    `Succeeded=true`. Requires explicit `Assembly.LoadFrom` of `Fhsm.Kernel.dll`
    before calling `TriggerAsync` because `Fhsm.Kernel` is a transitive dependency
    that is not loaded by the test runner until first file-backed use; without this
    `MetadataReferenceResolver.ForRuntimeAssemblies` cannot find it (it filters
    assemblies with empty `Location`).

### Non-obvious issues encountered
1. **CS0214 in Stage8**: The generated `Register` method contained function-pointer
   cast expressions, which require an `unsafe` context. Fixed by emitting
   `public static unsafe void Register(...)`.
2. **CS8757 in Stage8**: `HsmActivity` had `[UnmanagedCallersOnly]` but was cast to
   a managed function pointer `delegate* <...>`. C# requires `[UnmanagedCallersOnly]`
   methods to be converted only to `delegate* unmanaged<...>`. Fixed by removing
   `[UnmanagedCallersOnly]` from the emitted thunk methods.
3. **CS0400 in Stage8 (Fhsm not found)**: `Fhsm.Kernel` is a transitive dependency of
   the test project. In the xUnit test runner, transitive assemblies are not
   automatically loaded into `AppDomain.CurrentDomain` until referenced from live code.
   `typeof(Fhsm.Kernel.HsmActionDispatcher)` does NOT force a file-backed load in this
   context. `Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Fhsm.Kernel.dll"))`
   does force a file-backed load, making the assembly visible to
   `MetadataReferenceResolver.ForRuntimeAssemblies`.

### Test results
All 4 QuickReloadService tests (SC1-SC4) pass.

---

## FIX-5: Editor window UI

### Files changed
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs` (replaced)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/InspectorWindow.cs` (replaced)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/PreferencesWindow.cs` (replaced)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/EditorWindowTests.cs` (created)

### Changes
- `GraphEditorWindow`: 5-parameter constructor
  `(BlueprintRegistry, BehaviorRegistry, AiHotReloadCoordinator, QuickReloadService,
  IOutputConsole)`. `DrawUI` renders toolbar with `ImGui.Button` for "Reload" and
  status display; calls `service.TriggerAsync` on click.
- `InspectorWindow`: `DrawUI` renders 3 tabs (Parameters, Working State, Hostings)
  using `ImGui.BeginTabBar`/`ImGui.TabItem`. Shows selected asset fields.
- `PreferencesWindow`: `DrawUI` renders Checkbox for auto-save, InputInt for
  max undo steps, Save and Reset buttons. Uses `ImGui.BeginChild` with
  `ImGuiChildFlags.None` (newer ImGuiNET API).
- `EditorWindowTests`: 6 tests covering constructor validation (SC1 GraphEditorWindow,
  SC2 InspectorWindow, SC3 PreferencesWindow) and DrawUI smoke tests (SC4-SC6).

### Non-obvious issues encountered
- `ImGui.BeginChild` bool overload removed in newer ImGuiNET; replaced with
  `ImGuiChildFlags.None`.
- `??` null-coalescing operator requires both sides to have the same type; fixed
  by casting `List<AiPrimitiveHosting>` to `(IReadOnlyList<AiPrimitiveHosting>?)`.
- `CompilerMode` required `using Fdp.Toolkit.Blueprints;`.

### Test results
All 6 EditorWindowTests pass.

---

## Snapshot regenerations

The following snapshot files were regenerated as part of this batch (BLUEPRINT_REGENERATE_SNAPSHOTS=1):

| File | Reason |
|---|---|
| `Snapshots/Emit/MoveToAndFire.cs.txt` | FIX-1: Fbt.NodeStatus return, unsafe Register |
| `Snapshots/Emit/HasVisibleTarget.cs.txt` | FIX-1: Fbt.NodeStatus return, unsafe Register |
| `Snapshots/Emit/DoorActor.cs.txt` | FIX-1: unsafe Register |
| `Snapshots/Emit/LibraryMath.cs.txt` | FIX-1: unsafe Register |
| `Snapshots/Emit/InstanceCounter.cs.txt` | FIX-1: unsafe Register |
| `Snapshots/Emit/HealthRegen.cs.txt` | FIX-1: unsafe Register |
| `Snapshots/Demos/MoveToAndFire.cs.txt` | FIX-1+FIX-2: unsafe Register, RegisterAction/RegisterCondition |

---

## Pre-existing failures (not caused by this batch)

| Test | Reason |
|---|---|
| `Instance_EmitMatchesGoldenSource(InstanceCounter)` | Snapshot predates Phase 5 structural changes |
| `Instance_EmitMatchesGoldenSource(DoorActor)` | Snapshot predates Phase 5 structural changes |
| `Instance_EmitMatchesGoldenSource(HealthRegen)` | Snapshot predates Phase 5 structural changes |

`DebugProbe_NullSink_OnNodeEnter_ZeroAllocation` appeared flaky during this session
(failed in one run, passed in subsequent runs). It is an allocation-sensitive test
and not caused by this batch.

---

## Final test summary

```
Total:   489
Passed:  479
Failed:    3  (all pre-existing)
Skipped:   7
```

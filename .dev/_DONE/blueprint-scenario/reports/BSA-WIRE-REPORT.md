# BSA-WIRE Report

## Implementation Summary

### Task 1 — Shared seam (`BlueprintGenesisRuntimeRegistration`)
Created `Hrot/Subsystems/Hrot.SimHost/Systems/BlueprintGenesisRuntimeRegistration.cs` with a single
static method `RegisterBlueprintGenesisSystems(ModuleHostKernel kernel, BlueprintRegistry registry)`
that registers both `BlueprintMaterializationSystem` and `BlueprintEventIngressSystem` as global
Input-phase systems. Both are decorated with `[UpdateInPhase(SystemPhase.Input)]` on their class
declarations; the kernel validates this automatically.

No new project references were added. `Hrot.SimHost` already reaches `Fdp.Toolkit.Blueprints.Systems`
(which contains `BlueprintEventIngressSystem`) transitively through `Hrot.Common → Fdp.Toolkits`.

### Task 2 — CGF uses the seam
In `CgfSubsystem.cs` replaced the two ad-hoc lines at :383/:384:
```csharp
// BEFORE
_context.Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.BlueprintMaterializationSystem(_blueprintRegistry!));
_context.Kernel.RegisterGlobalSystem(new BlueprintEventIngressSystem(_blueprintRegistry!));

// AFTER
Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(
    _context.Kernel, _blueprintRegistry!);
```
Behavior is identical — same systems, same registry instance, same Input phase.

The `using Fdp.Toolkit.Blueprints.Systems;` directive in CgfSubsystem.cs is now unused. C# does not
generate a compiler error for unused `using` directives by default (CS8019 is IDE-only); the project
still builds with 0 errors and 0 warnings under `TreatWarningsAsErrors=true`. No removal needed.

### Task 3 — Editor uses the seam (bug fix)
In `EditorSubsystem.cs`, immediately after `GenesisMaterializationSystem` registration at line ~852,
added:
```csharp
Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(
    _kernel, _blueprintRegistry);
```
`_blueprintRegistry` is the field declared at line 253 (`private BlueprintRegistry _blueprintRegistry = new();`),
populated by `AiHotReloadCoordinator` / `BlueprintRegistrarScanner` from `Hrot.AI.Behaviors.dll` before
`Initialize()` is called. The existing `WireBlueprintRuntime` call at line ~787 (the tick path) is
untouched.

### New test
Created `Hrot/Subsystems/Hrot.SimHost.Tests/BlueprintGenesisRuntimeRegistrationTests.cs` with 4 tests
(TC1–TC4). Tests use reflection on `ModuleHostKernel._registeredGlobalSystems` (private `List<IEcsModuleSystem>`)
— same reflection pattern as `ModuleHostKernelTestExtensions` which already inspects `_timeController`.

---

## Design Decisions

1. **Static method on a static class** (`BlueprintGenesisRuntimeRegistration`) rather than an extension
   method: matches the pattern of `BlueprintRuntimeWiring.WireBlueprintRuntime` already in the codebase
   and makes the call site read clearly as a named operation.

2. **`ModuleHostKernel` as the parameter type** (concrete class, not an interface): there is no
   `IEcsModuleHostKernel` interface in this codebase. Using the concrete class avoids a phantom interface
   and is consistent with every other registration site.

3. **Reflection in tests** to inspect `_registeredGlobalSystems`: the field is private with no public
   accessor. Reflection is acceptable here because:
   - The field name is stable (kernel internals are test-trusted via `InternalsVisibleTo` for tests in the same assembly).
   - The test would fail loudly if the field is renamed (assertion on `Assert.NotNull(field)`).
   - Alternatives (subclassing, wrapping) would add unnecessary indirection.

---

## Deviations

None. Implementation follows the batch spec exactly.

---

## Test Results

### New tests (BSA-WIRE)
```
Hrot.SimHost.Tests.BlueprintGenesisRuntimeRegistrationTests
  ✓ RegisterBlueprintGenesisSystems_RegistersBothRequiredTypes       [2 ms]
  ✓ RegisterBlueprintGenesisSystems_AddsExactlyTwoSystems            [41 ms]
  ✓ RegisterBlueprintGenesisSystems_ForwardsRegistryInstanceToEachSystem [3 ms]
  ✓ RegisterBlueprintGenesisSystems_NullRegistry_Throws              [1 ms]

Total: 4 passed
```

### CgfBlueprintRegistryScannerTests (4 required)
```
  ✓ Scan_AiAssembly_SkipOnUnknown_RegistersKnownGeneratedBlueprint   [68 ms]
  ✓ BlueprintMaterializationSystem_PopulatedRegistry_AttachesBlackboardSlot [3 ms]
  ✓ BlueprintMaterializationSystem_EmptyRegistry_AttachesNothing     [33 ms]
  ✓ Scan_AiAssembly_SkipOnUnknown_BehaviorRegistrationIsIdempotent   [5 ms]

Total: 4 passed
```

### Hrot.Editor.Tests (required: 116, Failed: 0)
```
Passed!  - Failed: 0, Passed: 116, Skipped: 0, Total: 116
```

### Hrot.SimHost.Tests (pre-existing failures expected 41–48)
- Run 1: Failed: 40, Passed: 602, Skipped: 3, Total: 645
- Run 2: Failed: 41, Passed: 601, Skipped: 3, Total: 645

The variation (40–41) is flakiness in pre-existing tests unrelated to this batch
(navigation module ordering, HillAttack, gizmo, staging entity tests). My new 4
tests pass in both runs.

### Fdp.Toolkits.Tests (pre-existing failures expected 28–53)
- Run 1: Failed: 37, Passed: 1844, Skipped: 0, Total: 1881
- Run 2: Failed: 34, Passed: 1847, Skipped: 0, Total: 1881

Flaky navigation / carkinematics / gizmo tests, unrelated to this batch.
No new failures.

---

## Editor-Path Regression Guard

**There is no headless seam to assert `BlueprintMaterializationSystem` is among the editor kernel's
registered systems.** `EditorSubsystem.Initialize()` requires Raylib (graphics window) and DDS network
components; it cannot be called in a unit test. The `OfflineKernelBootTests` creates a minimal kernel
with only `SimHostCoreLogicPack + CgfLogicPack + OrchestrationLogicPack + ScenarioEditorModule` — it
does NOT mirror the full `EditorSubsystem` composition root.

The 116 existing `Hrot.Editor.Tests` verify the editor's blueprint tick/execution path passes unchanged.

### Manual Smoke Checklist

Perform in the offline editor after this batch is committed:

1. **Open the offline editor** and confirm it starts without crash.
2. **Open** `File → Load Scenario` and load:
   `C:\FDP_Temp\shared\scenarios\test-blueprint\scenario.json`
3. **Select the entity** that should have a blueprint assignment.
4. **Open the Entity Inspector** panel for that entity.
5. **Verify** a `BlueprintBlackboard*` component (1024 / 4096 / 16384 tier) is visible in the inspector.
6. **Verify** the slot summary shows the blueprint name (e.g. "Count4" or the scenario-specific blueprint).
7. **Before this fix** (baseline): the entity loaded with no `BlueprintBlackboard*` component — the
   intent was written but never consumed.
8. **After this fix**: the `BlueprintMaterializationSystem` runs in the first Input tick after load and
   converts `InitialBlueprintsIntent` → `BlueprintBlackboard*` slot.
9. **Tick 10 frames** (press Play for a few seconds): confirm no exceptions in the console log and the
   blackboard slot remains populated.
10. **Re-save the scenario** (`Ctrl+S`) and reload it: the slot must still appear after the second load.

---

## Developer Insights

- The `using Fdp.Toolkit.Blueprints.Systems;` directive in `CgfSubsystem.cs` is now dead code. It's
  harmless (not an error under `TreatWarningsAsErrors`), but could be cleaned up in a future tidy
  commit if the project ever enables IDE-sourced CS8019 as a build warning.

- `ModuleHostKernel._registeredGlobalSystems` is private; there is no public introspection API.
  A future improvement would be a `IReadOnlyList<IEcsModuleSystem> RegisteredGlobalSystems` read-only
  property (behind `[Conditional("DEBUG")]` or test-only) to avoid reflection in tests.

- The `EditorSubsystem`'s `_blueprintRegistry` field is initialized as `new BlueprintRegistry()` and
  populated before `Initialize()` is called (via `AiHotReloadCoordinator` initial assembly scan).
  The placement of the seam call — immediately after `GenesisMaterializationSystem` registration —
  ensures the registry is already non-null and the kernel hasn't been initialized yet, satisfying both
  the null guard in the system constructors and the kernel's pre-initialization requirement.

---

## Known Issues

None. The fix is contained, the seam is single-source, and no pre-existing tests were modified.

---

## Suggested Commit Message

feat: BSA-WIRE unify blueprint genesis registration; fix offline-editor blueprint materialization

# BSA-REGSCAN: Unify the [BlueprintRegistrar] scan into one shared helper; populate CGF's BlueprintRegistry
**Goal:** scenario blueprint **assignments load back** on a standalone CGF node — by populating its `BlueprintRegistry`
from the compiled `[BlueprintRegistrar]` classes via a **single shared scanner** used by the editor *and* CGF.
**Est:** ~10h. Touches: `Fdp.Toolkits` (new helper), `Hrot.Blueprints.Editor` + `Hrot.Editor` (refactor to helper),
`Hrot.CGF` (use helper). **Read `.dev/.guides/DEV-GUIDE.md` first.**

## Root cause (confirmed — do not re-derive)
Save works; the load *data* path works (`BlueprintStateTranslator.Inject` sets an `InitialBlueprintsIntent`, and
`BlueprintMaterializationSystem` is registered in CGF). The break: `CgfSubsystem.Initialize` does
`_blueprintRegistry = new BlueprintRegistry()` (≈ line 300) and **never populates it**. So
`BlueprintMaterializationSystem` (`Hrot.SimHost/Systems/BlueprintMaterializationSystem.cs:81`) calls
`_registry.TryGetById(...)`, finds nothing, logs *"AssetId … not registered; skipping"*, and attaches nothing.
**Empty registry → nothing materializes → assignments don't load back.**

The legacy `CgfBehaviorSetup.LoadFromAiAssembly` (CgfSubsystem ≈ line 258) populates only the **BehaviorRegistry**, not
the **BlueprintRegistry**. The fix is NOT `AiHotReloadCoordinator` — that lives in the **editor** assembly
(`Hrot.Subsystems/Hrot.Editor`); a runtime CGF node must not reference the editor. The fix is the lightweight
`[BlueprintRegistrar]` reflection scan, which both editor call-sites already do (duplicated).

## The scan already exists — duplicated in two editor places (this is what we unify)
- `Hrot.Blueprints.Editor/Reload/QuickReloadService.cs:117-159` (in-memory ALC reload).
- `Hrot.Editor/AiHotReloadCoordinator.cs` ≈ lines 498-547 (initial-load / full-rebuild from DLL).
Both: scan `assembly.GetTypes()` for `[BlueprintRegistrarAttribute]`, find static `Register`/`RegisterAll`, invoke it
injecting a `BlueprintRegistryStaging` and/or `BehaviorRegistry` arg (with guards: a `BlueprintRegistry`-direct param
or `HsmActionDispatcher` param throws). **All injected param types are visible from `Fdp.Toolkits`**:
`BlueprintRegistryStaging`/`BlueprintRegistrarAttribute` (`Fdp.Toolkits.Blueprints`), `BehaviorRegistry`
(`Fdp.Toolkits/Behavior`), `HsmActionDispatcher` (`Fhsm.Kernel`, already referenced by Fdp.Toolkits).

## Tasks (do in order; do not start the next until the current is green)

### Task 1 — Create the shared scanner (NEW: `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistrarScanner.cs`)
A `public static class BlueprintRegistrarScanner` with a method that performs ONLY the reflection scan + registrar
invocation (no commit, no ClearAll — those stay caller concerns):
```
public static void Scan(System.Reflection.Assembly assembly,
                        BlueprintRegistryStaging blueprintStaging,
                        BehaviorRegistry behaviorStaging);
```
- Lift the existing loop verbatim (scan `[BlueprintRegistrarAttribute]`, prefer `Register` else `RegisterAll`, inject
  `BlueprintRegistryStaging`/`BehaviorRegistry` args, keep the existing guard exceptions for a forbidden
  `BlueprintRegistry`-direct param and for `HsmActionDispatcher` param). No new project references (verify the csproj
  is unchanged). Document the contract (caller commits the staging + owns any `HsmActionDispatcher.ClearAll` ordering).
- **Tests required** (`Fdp.Toolkits.Tests`): scan an assembly containing a `[BlueprintRegistrar]` test type whose
  `Register(BlueprintRegistryStaging)` adds a known definition → assert the staging contains it. Scan one whose
  `Register(BehaviorRegistry)` registers a behavior → assert it registered. Assert the forbidden-param guards throw.

### Task 2 — Refactor the two editor call-sites to use the scanner (BEHAVIOR-IDENTICAL)
Replace the inline scan loops in `QuickReloadService.cs` and `AiHotReloadCoordinator.cs` with `BlueprintRegistrarScanner.Scan(...)`.
Keep everything else exactly as-is in the callers: `HsmActionDispatcher.ClearAll()` ordering, the staging **commit**
(`CommitStaging`/`CommitStagingMerge`), debug-map registration, the RCU contract. **This is a pure extraction — editor
hot-reload (incl. mid-simulation) behavior must not change.**
- **Tests required:** the existing `Hrot.Editor.Tests/AiHotReloadCoordinatorTests` and any QuickReload tests are the
  behavior oracle — they must stay green unchanged. Do not weaken them.

### Task 3 — Populate CGF's BlueprintRegistry via the scanner (NO editor reference)
In `CgfSubsystem.Initialize`, after `_blueprintRegistry = new BlueprintRegistry()`, populate it with a **one-time**
scan of the AI behaviors assembly (already referenced — `Hrot.CGF.csproj:59`):
```
var staging = new BlueprintRegistryStaging();
BlueprintRegistrarScanner.Scan(typeof(Hrot.AI.Behaviors.AiBehaviorFactory).Assembly, staging, behaviorRegistry);
_blueprintRegistry.CommitStaging(staging);
```
- **Resolve behavior double-registration cleanly (the user wants clean, unified code).** `AiBehaviorFactory` is itself
  `[BlueprintRegistrar]`, so the scan may register behaviors that `CgfBehaviorSetup.LoadFromAiAssembly` already
  registered. Investigate what `LoadFromAiAssembly` does beyond behavior registration (it takes `GeoTransform` +
  `_entityMap` — it likely does CGF-specific wiring that must be preserved). Decide and implement ONE clean path so
  **every behavior and every blueprint is registered exactly once** and **no CGF-specific geo/entity-map wiring is
  lost**. Document the decision in the report.
- Do NOT reference `AiHotReloadCoordinator` or any `Hrot.Editor` type from CGF (verify `Hrot.CGF.csproj` gains no
  editor reference). Hot-reload mid-sim is editor-only and out of scope here (a standalone node loads once at boot).
- **Tests required** (`Hrot.SimHost.Tests` or the CGF/integration test project): the load-back behavior, end to end —
  **prescribed assertions:**
  1. After `BlueprintRegistrarScanner.Scan` over the real `Hrot.AI.Behaviors` assembly + commit, assert
     `_blueprintRegistry.TryGetById(BlueprintIdHash.Compute(assetId))` succeeds for a known compiled blueprint
     (e.g. a sample `.bp.json`'s AssetId).
  2. End-to-end: an entity carrying an `InitialBlueprintsIntent` with that AssetId, run `BlueprintMaterializationSystem`
     against a repo with the **populated** registry → assert the entity gains a `BlueprintBlackboard*` component whose
     slot has the matching `BlueprintId` (i.e. attachment happened — NOT skipped). Contrast: with an **empty** registry
     the same input attaches nothing (proves the fix is the registry population).
  3. Assert no behavior is registered twice (count or known-key check).

## Success Criteria (prescribed — do not weaken)
- [ ] One shared `BlueprintRegistrarScanner.Scan`; the inline scan loops in `QuickReloadService` and
      `AiHotReloadCoordinator` are GONE (no duplicated scan remains).
- [ ] No new project references anywhere; CGF references no `Hrot.Editor` type.
- [ ] CGF's `_blueprintRegistry` is populated at init; the end-to-end materialization test attaches the blueprint
      (Task 3 assertions all pass); behaviors registered exactly once; no CGF geo/entity-map wiring lost.
- [ ] Editor hot-reload tests (`AiHotReloadCoordinatorTests` + QuickReload) stay green unchanged.
- [ ] Report at `.dev/_DONE/blueprint-scenario/reports/BSA-REGSCAN-REPORT.md`: the double-registration decision + evidence,
      final test counts.

## DO NOT STOP UNTIL VERIFIED GREEN
Run the affected test projects yourself and loop until `Failed: 0` (do not report complete with red tests):
`dotnet test FDP/Toolkits/Fdp.Toolkits.Tests`, `dotnet test Hrot/Subsystems/Hrot.Editor.Tests` (coordinator), and the
CGF/SimHost test project covering the integration test. Also run `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
(no `BLUEPRINT_REGENERATE_SNAPSHOTS`) to confirm no compiler/editor regressions — only the one documented pre-existing
`TickFrame_1000Frames_AllocatesZeroBytes` may remain red. End the report with the green output.

## Guardrails
Pure extraction for the editor (no semantic change to hot-reload/RCU). No new references; no editor types in CGF; no
pragmas/neutered assets/weakened assertions; no duplicated scan left behind. Fail loud. One clean registration path.

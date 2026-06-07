# MVE-BATCH-02 Review — blueprint runtime in the real editor kernel + real-kernel run test
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Instance Blueprints now tick inside the editor's **real `ModuleHostKernel`** (no sandbox). `BlueprintRuntimeWiring.WireBlueprintRuntime(kernel, world, registry)` (single source of truth) registers the three tier components + `BlueprintMaintenanceSystem` (BeforeSync global) and returns `BlueprintTickSystem`, which `EditorSubsystem` appends to its `TogglableSimulationGroup` (line ~669) and `EditorHarness` mirrors. Ticks against the editor's existing `_blueprintRegistry` (the same instance `AiHotReloadCoordinator` compiles into). Plus production `BlueprintAttachService` (the seam the MVE-03 button reuses) + a `CounterDemoBlueprint`.

## Verification (delegated to a sonnet agent — per the user's cost directive; lead reviewed)
- Build **0 errors**; **touched projects (Hrot.Editor, Hrot.Blueprints.Editor, the two test projects) have 0 warnings** — the 26 full-rebuild warnings are all pre-existing unrelated test projects (Fdp.Core.Tests / Hrot.Common.Tests / Hrot.Utility.Editor.Tests / Diagnostics.Breakpoints.Tests / pre-existing Blueprints.Tests infra).
- `BlueprintKernelRunTests` **5/0** (real kernel, self-created entity, `Count == frames`); `BlueprintAttachServiceTests` **6/0**; `EditorSubsystemBoot` **10/0**; `Hrot.Blueprints.Tests` **1132/10/8** (10 = DEBT-006); `Hrot.Editor.AiShared.Tests` **761/0**.
- Integration suite: 4 failures (`BreakpointSubsystemWiring`×2 = DEBT-008 RegisterSystems-before-RegisterProviders; `EditorFileIO.SaveScenario`; `EditorPreviewAndSave` AccessViolation in `SpatialHashSystem`) — **all reproduced identically on the pre-MVE-02 baseline via git stash → zero regressions from this batch**. (`ClusterOpE2eScriptTests` DDS crash also pre-existing/unrelated.)

## Code read
- `EditorSubsystem` change is minimal/clean (helper call + `.Append(bpTick)`); ticks the editor's real registry; comment documents the no-sandbox rule.
- `BlueprintRuntimeWiring` shared by editor + harness (one source of truth). `BlueprintAttachService` mirrors the proven `BlueprintTestFixture.AttachBlueprint` sequence with a clear status enum (Attached/AlreadyAttached/NotRegistered/NotInstanceKind/NoSlotAvailable), idempotent, **run-mode-agnostic** (sets up components; doesn't require the sim running) — exactly what the MVE-03 button needs.

## Debt logged
- **DEBT-MVE-001 (P2):** `[UpdateBefore(...Dispatcher)]` on `BlueprintTickSystem` does NOT auto-order it inside `TogglableSimulationGroup` (the group runs systems in array order — `TogglableSimulationGroup.cs:66`); `bpTick` is appended last. Harmless for the `Count` observable, but if a blueprint must issue a channel command that the same-frame dispatcher routes, the tick must precede the dispatchers in the array. Revisit when blueprints issue channel commands (e.g. the move-via-locomotion-intent demo).

## Verdict
APPROVED. Blueprints run in the real editor kernel; the attach seam + demo are ready for the MVE-03 toolbar button. Next: MVE-03 (button on `EditorSelectionStore.SelectedEntity`, attach-only via `BlueprintAttachService`, run-mode-agnostic), then MVE-04 (Save).

## Commit Message
```
feat(blueprint-mve): run Instance Blueprints in the real editor kernel + attach service (MVE-BATCH-02)

BlueprintRuntimeWiring.WireBlueprintRuntime (single source of truth) registers the BlueprintBlackboard
tier components + BlueprintMaintenanceSystem (BeforeSync) and returns BlueprintTickSystem; EditorSubsystem
appends it to its TogglableSimulationGroup and EditorHarness mirrors it — so Instance Blueprints tick in
the REAL ModuleHostKernel (no sandbox), against the editor's shared _blueprintRegistry.

BlueprintAttachService (Hrot.Blueprints.Editor/Runtime): production, idempotent, run-mode-agnostic attach
(BlueprintIdHash → TryGetById → require Instance → ChooseTier → ensure tier component → Initialize/TryAttach/
InitDefault) — the seam the MVE-03 run-button reuses. + CounterDemoBlueprint.

BlueprintKernelRunTests: through the REAL kernel, create entity → attach → PumpFrames(N) → Count == N.

Build 0 errors (touched projects 0 warnings). New: KernelRun 5/0, AttachService 6/0; EditorSubsystemBoot
10/0; Blueprints 1132/10 (DEBT-006); AiShared 761/0. Integration failures are all pre-existing (verified vs
baseline). DEBT-MVE-001 (UpdateBefore not honored inside the toggle group) logged.
```

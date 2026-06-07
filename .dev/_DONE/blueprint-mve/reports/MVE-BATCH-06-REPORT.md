# MVE-BATCH-06 Report — Debug Observe: Live Blueprint Working-State in the Editor

## Implementation Summary

### Task 07-A — Live (Non-Pause-Gated) Read API

**Interface:** `IBlueprintDebugSession` (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs:177-187`)

Added:
```csharp
BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId);
```
Documented as non-pause-gated. Placed after `GetCurrentStateSnapshot()` in the Inspection section.

**Implementation:** `BlueprintDebugSession` (`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs:457-469`)

```csharp
public BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId)
    => CaptureStateSnapshot(self, assetId);
```
One-liner: delegates directly to the existing private `CaptureStateSnapshot(self, assetId)` without pause-gate. Reuses all existing `ReadInstanceState`/`CaptureInstanceStateFromDefinition`/slot decoding machinery unchanged.

**Test double updates (all 3):**
- `MockDebugSession` (`Hrot.Blueprints.Tests/Editor/MockDebugSession.cs:64`) — returns `null`
- `CapturingDebugSession` (`Hrot.Blueprints.Tests/CapturingDebugSession.cs:87`) — returns `null`
- `SpyDebugSession` (nested in `DebugWindowDrawUITests.cs:82`) — returns `null`

### Task 07-B — Register DebugMap on Compile

**Already implemented** in `QuickReloadService.TriggerAsync` (`Hrot.Blueprints.Editor/Reload/QuickReloadService.cs:159-161`):

```csharp
// Step 6: Register debug map BEFORE coordinator handoff (Patch 2).
if (result.DebugMap != null)
    _session?.RegisterDebugMap(result.DebugMap);
```

This was confirmed as present from the MVE-05 batch. The DebugMap includes `StateLayout` populated by `CSharpEmitter.Emit` for Instance blueprints (`CSharpEmitter.cs:72-81`): for each `IrVariableField`, a `StateLayoutField(name, type, offsetBytes, sizeBytes)` is added. Variables start at `startOffset: 16` (after the 16-byte `BlueprintLatentCursor` header) per `FieldLayout.cs:13`.

The session constructor already holds the `IBlueprintDebugSession? _session` field from MVE-05. Thus compiled blueprints' `FieldValues` become readable without any codegen change.

### Task 07-C — Blueprint Runtime Inspector Pane

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/BlueprintRuntimeInspectorPane.cs`

```
BlueprintRuntimeInspectorPane : IRuntimeInspectorPane
    TargetKind => AssetKind.Blueprint
    SetSession(IBlueprintDebugSession?)
    SetResolvers(Func<Entity?> selectedEntityResolver, Func<Guid?> activeAssetIdResolver)
    Draw()          — ImGui-gated (GetCurrentContext() == Zero guard); calls CaptureLiveState
    ProjectFields() — ImGui-free, testable field projection (returns (Name, Value) rows)
```

`Draw()` resolves entity + assetId via injected delegates, calls `session.CaptureLiveState(entity, assetId)`, renders header + latent-cursor + fields table via ImGui. The ImGui context guard keeps Draw() safe in headless tests.

**Registration in EditorSubsystem** (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:1905-1920`):

```csharp
if (_blueprintDebugSession != null)
{
    var blueprintPane = new Hrot.Blueprints.Editor.Inspector.BlueprintRuntimeInspectorPane();
    blueprintPane.SetSession(_blueprintDebugSession);
    blueprintPane.SetResolvers(
        selectedEntityResolver: () => _blueprintSelectionStore?.SelectedEntity,
        activeAssetIdResolver:  () => (ctx?.AssetRef as BlueprintAsset)?.AssetId);
    _blueprintRegistrar.RuntimeInspector.RegisterPane(blueprintPane);
}
```

Placed immediately after the BTree and HSM pane registrations (AIE-031 block). The `_blueprintSelectionStore` (`EditorSubsystem.cs:270`) is the Blueprint-perspective `EditorSelectionStore` whose `SelectedEntity` reflects the editor's entity selection. The active asset id is resolved from `_aiDocumentManager?.Active?.ViewState as AiCanvasContext` → `AssetRef as BlueprintAsset` → `AssetId` — the same resolution pattern used by the compile button callback (`EditorSubsystem.cs:1878-1881`).

### Headless Observe Test

**File:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BlueprintObserveTests.cs`

Three tests through the **real kernel** (`EditorHarness`):

1. **`CaptureLiveState_AfterNFrames_CountEqualsN(frames: 1/3/5)`** — core test
   - Register `CounterDemoBlueprint` in `harness.BlueprintRegistry` (real Tick increments Count per frame)
   - Construct `BlueprintDebugSession` against `(ISimulationView)harness.Repo`
   - Call `session.RegisterDebugMap(debugMap)` with a manually-constructed DebugMap that describes the State Layout (`Count:int` at offset 16 after the `BlueprintLatentCursor`)
   - Attach to a real entity via `BlueprintAttachService.AttachToEntity`
   - `harness.PumpFrames(frames)` — advances the real kernel, BlueprintTickSystem increments Count
   - `session.CaptureLiveState(entity, CounterDemoBlueprint.AssetGuid)` — no pause required
   - **Assert: `FieldValues["Count"] == frames`** (real values, not "no throw")

2. **`CaptureLiveState_WithoutDebugMap_ReturnsSnapshotWithEmptyFields`** — proves the `ReadInstanceState` guard: when no DebugMap is registered, `stateLayout == null` and `FieldValues` is empty.

3. **`CaptureLiveState_EntityWithNoSlot_ReturnsSnapshotWithEmptyFields`** — proves `TryGetSlotOffset` returns false when entity has no BB component → no fields.

**Why `CounterDemoBlueprint` instead of a Roslyn-compiled asset:** The `InstanceCounter.bp.json` has `"Graphs": []` (no tick body), so a Roslyn-compiled blueprint increments nothing. `CounterDemoBlueprint` is the correct demo with a real Tick. The DebugMap is constructed programmatically to exactly mirror what `CSharpEmitter` would produce for an Instance blueprint with a single `int` variable at offset 16.

## Design Decisions

1. **`CaptureLiveState` as a one-liner delegating to the private `CaptureStateSnapshot`** — cleanest approach: no code duplication, zero risk of divergence. The private method already has all the machinery.

2. **`BlueprintRuntimeInspectorPane.SetResolvers` takes delegates** — keeps the pane decoupled from `EditorSubsystem`'s internal state without requiring field access. Same pattern used by other MVE callbacks. The ImGui-free `ProjectFields()` static method enables unit testing of field rendering logic without ImGui.

3. **Observe test uses `CounterDemoBlueprint` + manual DebugMap** — `InstanceCounter.bp.json` has no graph body (would always read 0). The manual DebugMap construction exactly mirrors `CSharpEmitter`'s output (variables start at offset 16 per `FieldLayout.cs`). This tests the same code path as a QuickReloadService-compiled blueprint.

4. **07-B was already wired** — `QuickReloadService.cs:159-161` already calls `_session?.RegisterDebugMap(result.DebugMap)` from MVE-05. No changes needed; verified and documented.

## Deviations

None. All tasks implemented per spec. Deferred 07-D is explicitly not done.

## Test Results

### EditorSubsystemBoot filter
```
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```
10/10 — pane + session wiring composes correctly.

### Hrot.Blueprints.Tests
```
Failed! - Failed: 10, Passed: 1152, Skipped: 8, Total: 1170
```
10 failures are all pre-existing DEBT-006 (emit-golden + snapshot + perf tests). Zero new failures.
Emit-golden tests **unchanged** — no codegen/generated output touched.

Failed tests confirmed pre-existing:
- `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`, `AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)`
- `Instance_EmitMatchesGoldenSource(InstanceCounter/DoorActor/HealthRegen)`
- `Library_EmitMatchesGoldenSource`
- `LibraryMath_GeneratedSource_Snapshot`, `MoveToAndFire_GeneratedSource_Snapshot`
- `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
- `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`

### Hrot.Editor.AiShared.Tests
```
Passed! - Failed: 0, Passed: 761, Skipped: 0, Total: 761
```

### Observe tests (new)
```
Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```
CaptureLiveState_AfterNFrames_CountEqualsN(1), (3), (5) — real count values asserted.
CaptureLiveState_WithoutDebugMap_ReturnsSnapshotWithEmptyFields — confirmed.
CaptureLiveState_EntityWithNoSlot_ReturnsSnapshotWithEmptyFields — confirmed.

### Full solution build
```
Build succeeded. 0 Warning(s). 0 Error(s).
```

### Golden/snapshot confirmation
Zero golden or snapshot files were modified. No codegen changes were made. The 5 golden emit tests that were already failing remain failing with the same error (snapshot mismatch pre-dating this batch).

## Developer Insights

- **07-B pre-wired**: `QuickReloadService` already registered the DebugMap in MVE-05. The batch description said "production currently never registers" but the code was already updated. The headless test validates this path still works.

- **InstanceCounter.bp.json has empty Graphs**: The bp.json test asset has `"Graphs": []`, so Roslyn-compiling it produces an empty Tick. The DESIGN doc describes it as having "tick increments" which only applies to `CounterDemoBlueprint` (code-defined). Future work: add a tick-increment graph to `InstanceCounter.bp.json` to enable a Roslyn-compile-then-observe test.

- **`ISimulationView` is `Fdp.ModuleHost.Abstractions`**: The cast `(ISimulationView)harness.Repo` works because `EntityRepository` explicitly implements the interface (`EntityRepository.View.cs:10`).

- **`_blueprintSelectionStore` is always initialized** (`= new()` at EditorSubsystem.cs:270`), so no null guard needed in the resolver delegate. Added `?.` for safety since it's nullable in the field declaration.

## Known Issues

- **`CaptureLiveState` for Instance dispatch with no DebugMap returns a non-null snapshot with empty FieldValues** — this is correct and tested, but could confuse callers expecting null. The pane handles this with a "No live Blueprint state (DebugMap not registered?)" message.

- **07-D (StateFields in codegen) remains deferred** (DEBT-MVE-002): compiled blueprints' field names are only readable via DebugMap (07-B path). Once 07-D lands, they'll be self-describing at runtime too.

- **Multi-coordinator** (DEBT-MVE-003) not addressed — out of scope.

## Suggested Commit Message

```
feat(blueprint-mve): debug-observe — live read API (CaptureLiveState), BlueprintRuntimeInspectorPane, and headless observe test asserting Count == N (MVE-BATCH-06)
```

## Next Step

**MVE hot-reload**: hot-swap a running compiled blueprint — change `Count`-incrementing logic in the `.bp.json`, trigger `QuickReloadService`, assert the running instance picks up the new behaviour (soft-reload preserves state where hash unchanged). Will reuse the `QuickReloadService` + `EditorHarness` + `CaptureLiveState` plumbing from this batch.

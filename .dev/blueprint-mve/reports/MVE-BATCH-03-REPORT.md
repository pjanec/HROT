# MVE-BATCH-03 Report — "Run Blueprint on Selected Entity" toolbar button

## Implementation Summary

### Task 1 — Toolbar button + headless callback

**Two new production files:**

1. **`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Runtime/RunBlueprintOnEntityCommand.cs`**
   Static class with a single `Execute(world, registry, selectedEntity, activeAssetRef, report)` method.
   - Resolves: `selectedEntity` (null → "select an entity first" no-op), `activeAssetRef` (null or wrong type → no-op), then calls `BlueprintAttachService.AttachToEntity`.
   - Surfaces all five `BlueprintAttachStatus` values to `report`: `Attached`, `AlreadyAttached`, `NotRegistered` (with "Compile first" hint), `NotInstanceKind`, `NoSlotAvailable`.
   - Contains no ImGui dependency — pure logic, headlessly testable.
   - Exposes `ToolbarLabel = "Run Blueprint on Selected Entity"` constant.

2. **`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Internal/CaptureWindowRegistrar.cs`**
   Public implementation of `Hrot.Blueprints.Editor.IWindowRegistrar` that stores registered toolbar/menu/shortcut entries without requiring a live ImGui context. Provides `GetToolbarCallback(label)` so the composition root can retrieve the captured `Action` for later invocation.

**EditorSubsystem wiring (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`):**

- Two new fields (line ~290):
  ```
  private Action? _blueprintRunButtonCallback;
  private string _blueprintRunStatus = string.Empty;
  ```
- In `RegisterWindows` (after `_blueprintRegistrar.RegisterWindows(windowManager)`):
  - Constructs a `CaptureWindowRegistrar`.
  - Calls `registrar.RegisterToolbarEntry(ToolbarLabel, callback)` — satisfying the `IWindowRegistrar.RegisterToolbarEntry` contract.
  - The callback resolves the active asset from `_aiDocumentManager.Active?.ViewState as AiCanvasContext` → `ctx.AssetRef`, and the selected entity from `_aiEditorSelectionStore.SelectedEntity`, then delegates to `RunBlueprintOnEntityCommand.Execute`.
  - Retrieves the captured callback via `bpWindowRegistrar.GetToolbarCallback(ToolbarLabel)` and stores it in `_blueprintRunButtonCallback`.
- In `DrawUI`, gated on `_blueprintRunButtonCallback != null && ImGui.GetCurrentContext() != IntPtr.Zero`:
  - Opens an ImGui window "Blueprint Tools".
  - Renders a button; on click, invokes `_blueprintRunButtonCallback`.
  - Displays `_blueprintRunStatus` (populated by `report` delegate) to the right of the button.

### Task 2 — Headless unit tests

**`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/RunBlueprintOnEntityCommandTests.cs`** — 7 tests:

| Test | Scenario | Assertion |
|------|----------|-----------|
| `Execute_RegisteredAsset_SelectedEntity_ReturnsAttached_EntityHasSlot` | Happy path | `Status == Attached`, entity has `BlueprintBlackboard1024` |
| `Execute_CalledTwice_SecondCall_ReturnsAlreadyAttached` | Idempotency | First `Attached`, second `AlreadyAttached`, still `Success` |
| `Execute_UnregisteredAsset_ReturnsNotRegistered_LogsCompileHint` | Not in registry | `Status == NotRegistered`, log contains "ompile" |
| `Execute_NoEntitySelected_ReturnsNull_LogsSelectEntityFirst` | No entity | Returns `null`, log contains "select" |
| `Execute_NoBlueprintOpen_ReturnsNull_LogsOpenBlueprintFirst` | Null asset | Returns `null`, log contains "blueprint" |
| `Execute_WrongAssetType_ReturnsNull_LogsTypeName` | Non-BlueprintAsset activeRef | Returns `null`, log contains "Blueprint" |
| `IWindowRegistrar_RegisterToolbarEntry_CapturesCallback` | Registration contract | `MockWindowRegistrar` captures label + callback; invoking callback fires action |

---

## Selected-entity and Active-asset Resolution — Cited Members

**Selected entity:**
- `Hrot.Editor.AiShared.Selection.EditorSelectionStore.SelectedEntity` (type: `Entity?`)
  — defined at `Hrot/Editor/Hrot.Editor.AiShared/Selection/EditorSelectionStore.cs:62`
  — this is the **global entity selection for runtime debug overlay**, independent of which asset is active.
- The `EditorSubsystem` field is `_aiEditorSelectionStore` (type `Hrot.Editor.AiShared.Selection.EditorSelectionStore`, line 243 of `EditorSubsystem.cs`).

**Note:** There are *two* `EditorSelectionStore` classes in the codebase:
  - `Hrot.Editor.AiShared.Selection.EditorSelectionStore` — the shared AI editor store. Has `SelectedEntity` (Entity?) and `ActiveAsset` (IEditableAsset?). This is the one used.
  - `Hrot.Blueprints.Editor.EditorSelectionStore` — Blueprint-subsystem-only, has only `SelectedAsset` (BlueprintAsset?) and no `SelectedEntity`. Not used.

**Active blueprint asset:**
- Chain: `_aiDocumentManager.Active?.ViewState as AiCanvasContext` → `ctx?.AssetRef as BlueprintAsset`
  — `AiCanvasContext.AssetRef` is `object?` at `Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs:32`
  — set by `BlueprintDocumentFactory.Build` at `EditorSubsystem.cs:1844` (line ~1844, inside `DocumentOpened` handler).
- `_aiDocumentManager` is `AiDocumentManager?` at `EditorSubsystem.cs:261`.
- No `AiDocument` abstraction needed — the `Active` property gives the current `AiDocument`, and its `ViewState` is the factory-populated `AiCanvasContext`.

---

## Toolbar Registration

The `Hrot.Blueprints.Editor.IWindowRegistrar.RegisterToolbarEntry(string label, Action onClicked)` interface method is called in `EditorSubsystem.RegisterWindows` via:
```csharp
var bpWindowRegistrar = new Hrot.Blueprints.Editor.Internal.CaptureWindowRegistrar();
bpWindowRegistrar.RegisterToolbarEntry(RunBlueprintOnEntityCommand.ToolbarLabel, callback);
_blueprintRunButtonCallback = bpWindowRegistrar.GetToolbarCallback(RunBlueprintOnEntityCommand.ToolbarLabel);
```

The `IWindowRegistrar` interface signature (verified at `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/IWindowRegistrar.cs:6`):
```csharp
void RegisterToolbarEntry(string label, Action onClicked);
```

The ImGui rendering is gated in `DrawUI` on `ImGui.GetCurrentContext() != IntPtr.Zero` and `!_headless`, so no ImGui call is ever made in the headless composition path (the `EditorSubsystemBootTests` run headlessly with `_headless = true`).

---

## Status Feedback

All five `BlueprintAttachStatus` cases are surfaced via `_blueprintRunStatus` (a string field updated on every button click):
- **Attached** — pass-through of `BlueprintAttachResult.Message` (includes entity id, tier).
- **AlreadyAttached** — pass-through of `BlueprintAttachResult.Message`.
- **NotRegistered** — `Message` + " Compile / register the blueprint first."
- **NotInstanceKind** — pass-through of `BlueprintAttachResult.Message`.
- **NoSlotAvailable** — pass-through of `BlueprintAttachResult.Message`.

Pre-condition failures (no entity, no asset, wrong type) use dedicated messages appended to `_blueprintRunStatus` via the `report` delegate without calling `BlueprintAttachService`.

---

## Build Status

`dotnet build IOS-IG-SimHost.sln` → **Build succeeded. 0 Error(s), 0 Warning(s).**

Touched projects (`Hrot.Blueprints.Editor`, `Hrot.Editor`) each build with 0 errors, 0 warnings under `TreatWarningsAsErrors=true`.

Note: `Hrot.Blueprints.Tests` has 8 pre-existing build errors (CS0618 obsolete + CS8601 nullable) inherited from DEBT-006 that predated this batch. These are the same errors that existed before this batch (confirmed by git stash baseline). They do not affect solution-level build because `TreatWarningsAsErrors` is not set in that project's `.csproj`.

---

## Test Results

### New tests — `RunBlueprintOnEntityCommandTests` (7 tests)
**7 passed, 0 failed.**

Scenarios covered:
- Fresh attach → `Attached`, entity carries `BlueprintBlackboard1024` slot.
- Second attach → `AlreadyAttached`, `Success == true`.
- Empty registry → `NotRegistered`, log contains compile hint.
- No entity selected → returns `null`, log says "select an entity first".
- No active asset (null) → returns `null`, log says "open a blueprint asset first".
- Wrong asset type → returns `null`, log mentions "Blueprint".
- `IWindowRegistrar.RegisterToolbarEntry` contract → label captured, callback invokable.

### EditorSubsystemBoot filter — `FullyQualifiedName~EditorSubsystemBoot`
**10 passed, 0 failed** (no regressions — the toolbar callback is registered at composition, `_blueprintRunButtonCallback` is non-null after `RegisterWindows`, ImGui gate prevents any rendering in headless mode).

### Hrot.Blueprints.Tests (full suite)
**1138 passed, 11 failed, 8 skipped.**
The 11 failures are **all pre-existing DEBT-006**: golden-source/snapshot tests (`InstanceEmitGoldenTests`, `LibraryEmitGoldenTests`, `AiPrimitiveEmitGoldenTests`, `LibraryMathDemoTests`, `MoveToAndFireDemoTests`), `ConditionSummaryAttachmentTests`, `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes`, and `WhenNodePerfTests.WhenNode_ConditionMet_Under200ns_perTick`. None touch the blueprint runtime wiring, attach service, or the new command.

### Hrot.Editor.AiShared.Tests
**761 passed, 0 failed** (unchanged from BATCH-02).

---

## Developer Insights

1. **Two `EditorSelectionStore` classes** exist with identical names but different namespaces and capabilities. The one in `Hrot.Editor.AiShared` has `SelectedEntity`; the one in `Hrot.Blueprints.Editor` has only `SelectedAsset`. The batch spec's `EditorSelectionStore.SelectedEntity` refers unambiguously to the AiShared variant (`_aiEditorSelectionStore`).

2. **`IWindowRegistrar` is a registration-only interface** with no draw-time contract. The actual ImGui button is rendered by `DrawUI` which holds the captured callback. `CaptureWindowRegistrar` bridges these two concerns without duplicating the `MockWindowRegistrar` that already exists in the test project.

3. **`ImGui.Begin/End` guard pattern**: The DrawUI code wraps the button in `ImGui.Begin("Blueprint Tools") / ImGui.End()`. The batch's `ImGui.GetCurrentContext() != IntPtr.Zero` guard ensures the window is never attempted in non-ImGui environments. The `if (ImGui.Begin(...))` pattern means the window body is only drawn when Begin returns true (window not collapsed/culled).

4. **`_blueprintRunStatus` state**: The status string persists between frames — it shows the outcome of the last click. This is the simplest feedback mechanism consistent with how ImGui status lines work (no toast queue needed for a manual-testing button).

5. **`CaptureWindowRegistrar` is `public`**, not `internal`, because `EditorSubsystem` is in a separate assembly (`Hrot.Editor`) that references `Hrot.Blueprints.Editor`. `internal` would require `InternalsVisibleTo` and `AssemblyInfo.cs` boilerplate; `public` in an `Internal/` folder signals convention (not enforced by C#).

---

## Known Issues

- The status text in `DrawUI` persists until the next button click (no auto-clear). This is intentional for a manual-testing button; a future UX pass could add a timeout or dismiss button.
- `ImGui.Begin("Blueprint Tools")` opens a floating window; a future step could integrate this into the Blueprint perspective toolbar using the `PerspectiveWorkspaceRegistrar` toolbar seam when one exists.
- The `Hrot.Blueprints.Tests` DEBT-006 pre-existing errors continue to block building that project with `-p:TreatWarningsAsErrors=true`. The solution build remains clean.

---

## Suggested Commit Message

feat(blueprints): MVE-BATCH-03 — "Run Blueprint on Selected Entity" toolbar button with headless callback + 7 tests

---

## Next Step for MVE-04 (Save)

**MVE-04** implements editor Save: `BlueprintJsonServices.Serialize(asset)` → write to disk path, triggered from a menu entry or toolbar button in the Blueprint perspective. The headless test pattern is: load a `BlueprintAsset` from `.bp.json`, mutate it (add a variable), call Save, read the file back, compare key fields. The active blueprint asset is already accessible via `_aiDocumentManager.Active?.ViewState as AiCanvasContext → ctx.AssetRef as BlueprintAsset` (same path established in this batch). The save path must set `asset.IsDirty = false` after writing. No new runtime infrastructure is required.

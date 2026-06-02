# BATCH-04 Report

## Implementation Summary

### Corrective Task 0 — AccessViolation fix in ImGui-touching adapters

Fixed three adapter files in `Hrot/Editor/Hrot.Editor.AiShared/Adapters/`:

**`EngineEditorTheme.cs`** — `GetFontForSize` now guards with `if (ImGui.GetCurrentContext() == IntPtr.Zero) return IntPtr.Zero;` before any `ImGui.GetIO()` call. The `try/catch` is kept for genuine managed failures but can no longer reach the native AV path.

**`ImGuiClipboard.cs`** — Both `GetText` and `SetText` guard with the same context check before calling `ImGui.GetClipboardText`/`SetClipboardText`. Return value: `null` (GetText) / no-op (SetText) when context absent.

**`ImGuiInputSource.cs`** — Added a `private static bool HasContext => ImGui.GetCurrentContext() != IntPtr.Zero;` helper. All frame-snapshot properties (`MousePosition`, `MouseDelta`, `WheelDelta`, `Modifiers`, `TextThisFrame`) and per-frame query methods (`IsMouseDown`, `IsMousePressed`, etc.) guard with `if (!HasContext) return <fallback>;` before any ImGui call. Pure static mapping helpers (`MapMouseButton`, `MapEditorKey`, `MapModifiers`) are unchanged.

**New required tests added:**
- `EngineEditorTheme_GetFontForSize_NoContext_ReturnsZero` — asserts `IntPtr.Zero` returned with no context (must not crash).
- `ImGuiClipboard_NoContext_GetReturnsNull_SetNoThrow` — asserts `GetText()` returns `null` and `SetText` is a silent no-op; both `"ignored"` and `null!` handled.

**Determinism proof:** Full `Hrot.Editor.AiShared.Tests` (no filter) run 3× consecutively: `665/665 pass, 0 crashes` each time.

### Task 1 — AIE-015: EditorSubsystem composition rewrite

**File modified:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

#### Wired (new)

1. **Shared `AssetCatalog` via `AiAssetCatalogBuilder`** — `BTreeAssetContributor`, `HsmAssetContributor`, `BlueprintAssetContributor` (blueprints dir = `AppDomain.CurrentDomain.BaseDirectory/blueprints`, same as the retired registrar). Subscribed to `_aiCoordinator.OnReloadCompleted`: extracts the `Hrot.AI.Behaviors` assembly from `info.NewAlc.Assemblies` (or falls back to `AppDomain.CurrentDomain.GetAssemblies()`) and calls `RefreshFromAssembly`. Wired in `Initialize()` after `_aiCoordinator.TriggerInitialLoad()`.

2. **`AiDocumentManager` + `WindowManagerPerspectiveSwitcher`** — created in `RegisterWindows` and cross-wired via `SetDocumentManager` so manual toolbar switches activate the most-recent doc of that kind.

3. **Three per-perspective `EditorSelectionStore`s** (`_btreeSelectionStore`, `_hsmSelectionStore`, `_blueprintSelectionStore`) and three `PerspectiveWorkspaceRegistrar`s registering six side-panel windows each (Inspector / RuntimeInspector / TraceTimeline / FindResults / BlackboardAuthoring / Diagnostics) with distinct `###Id` suffixes and correct `OwningPerspective`.

4. **Global `AssetBrowserWindow`** — single instance with `WindowScope.Global`, `AiDocumentManager` injected, registered with id `ai_asset_browser`.

Shared services constructed in `RegisterWindows`: `ReferenceCatalog`, `RefactorService`, `DebugSessionRegistry`, `LiveSessionRegistry`.

#### Retired (removed)

- `CreateBlueprintWindowRegistrar()` private method — deleted entirely.
- `_blueprintWindowRegistrar` private field — removed.
- The `BlueprintWindowRegistrar` internal property getter/setter — replaced with an `[Obsolete]` null-returning stub so any external test-only reference compiles but does nothing. No active wiring remains.
- `Blueprints.Editor.EditorSelectionStore` and `Blueprints.Editor.FileSystemAssetCatalog` usage in the composition root — removed (the `FileSystemAssetCatalog` class remains in the Blueprints assembly but is no longer instantiated from `EditorSubsystem`).

#### Preserved (unchanged)

All non-AI-editor wiring preserved exactly:
- Legacy editor windows (toolbar, browser, cluster, inspector, etc.)
- Gizmo subsystem (`_editorDataDrivenGizmoSystem`, `_globalGizmoManager`, `_gizmoController`)
- Universal breakpoint stack (`_bpManager`, `_bpSnapshotProvider`, `_bpSystem`, `DataBreakpointManagerWindow`)
- `_blueprintDebugSession` + `DebugProbe.Sink` wiring
- WHEN-node bootstrap registries (`_blueprintNodeDrawers`, `_blueprintPaletteEntries`, attachment providers, canvas renderers)
- ECS world / kernel / time controller
- `_aiCoordinator` (behavior hot-reload)
- Existing `_aiEditorSelectionStore` + `_selectionBridge` (entity selection bridge)

#### How the AI-behaviors assembly handle was obtained

`_aiCoordinator.OnReloadCompleted` delivers a `ReloadCompletedInfo` with `NewAlc` (the new `AssemblyLoadContext`). The assembly is obtained via `info.NewAlc?.Assemblies.FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors")`. A fallback to `AppDomain.CurrentDomain.GetAssemblies()` handles the case where the initial load DLL was loaded into the default ALC. If neither finds the assembly (e.g. DLL absent), `RefreshFromAssembly` is skipped for that reload cycle.

#### Test migration

**`EditorSubsystemBlueprintWindowsTests.cs`** — fully rewritten. Old single test (`EditorSubsystem_RegisterWindows_RegistersAllBlueprintWindows`) that injected a `BlueprintWindowRegistrar` via the now-retired property is replaced by four new tests:
- `EditorSubsystem_RegisterWindows_RegistersThreePerspectives_AndGlobalBrowser` — the primary AIE-015 success condition test; verifies all 18 side-panel window IDs (6 per perspective) and the global Asset Browser (id + scope).
- `EditorSubsystem_RegisterWindows_BTreeWindows_HaveOwningPerspective_BTree`
- `EditorSubsystem_RegisterWindows_HsmWindows_HaveOwningPerspective_HSM`
- `EditorSubsystem_RegisterWindows_BlueprintWindows_HaveOwningPerspective_Blueprint`

All four use `wm.TryGetWindow(id, out var win)` + `Assert.Equal(perspective, win.OwningPerspective)` — behavior assertions, not object-existence checks.

**`BlueprintWindowRegistrarTests.cs`** — unchanged. The `BlueprintWindowRegistrar` class is still in the Blueprints.Editor assembly (it may be used in future Phase 4 work); its unit tests remain valid and pass.

## Design Decisions

1. **`RefactorService` constructed with `ReferenceCatalog(catalog)` and a default `AtomicMultiFileWriter()`** — The full refactor service (rename, find-references) is Phase 5 work. Constructing real (empty) instances here is preferable to null stubs because the windows that consume `IRefactorService` are headless-constructible and just show empty panels when no contributors are registered. No data-flow changes needed.

2. **`DebugSessionRegistry` and `LiveSessionRegistry` constructed locally in `RegisterWindows`** — Phase 3 (debug sessions) will wire the BTree/HSM session factories into these registries. Constructing them here establishes the correct ownership point (composition root) without Phase 3 logic. The registrars receive these as dependencies and will be functional once Phase 3 wires the factories.

3. **`_aiEditorSelectionStore` (the existing entity selection store) passed to `AssetBrowserWindow`** — The global browser is for cross-kind asset navigation; using the existing entity selection store is correct for its scope. Each perspective registrar uses its own dedicated store for per-perspective sub-selection (inspector, blackboard, etc.).

4. **`_btreeSelectionStore`, `_hsmSelectionStore`, `_blueprintSelectionStore` initialized as fields** — Lifetime must span `Initialize` through `Shutdown`; field initialization ensures they are always available for `RegisterWindows` even if called without `Initialize` (test scenario).

5. **Assembly resolution order for `RefreshFromAssembly`**: prefers the new ALC assemblies (hot-reload path) over AppDomain (initial-load path). This correctly handles both first-load and subsequent hot-reload scenarios without needing a separate initial-load hook.

## Deviations

None. All changes follow the spec exactly as stated in BATCH-04-INSTRUCTIONS.md and DESIGN §4.1–4.5.

## Test Results

### `Hrot.Editor.AiShared.Tests` (full, no filter, 3 runs)
```
Run 1: Passed: 665, Failed: 0, Skipped: 0 — Duration: 4 s
Run 2: Passed: 665, Failed: 0, Skipped: 0 — Duration: 3 s
Run 3: Passed: 665, Failed: 0, Skipped: 0 — Duration: 3 s
```
New tests added: `EngineEditorTheme_GetFontForSize_NoContext_ReturnsZero`, `ImGuiClipboard_NoContext_GetReturnsNull_SetNoThrow` (2 tests, both confirmed passing).

### `Hrot.ClusterRunner.Integration.Tests` (EditorSubsystemBootTests filter)
```
Passed: 10, Failed: 0 — Duration: 1 s
```
All 10 boot tests green. The `RegisterWindows` call path is exercised via `EditorHarness`.

### `Hrot.Blueprints.Tests` (full, no filter)
```
Passed: 888, Failed: 10, Skipped: 8 — Duration: 26 s
```
The 10 failures are exclusively the pre-existing DEBT-006 golden snapshot / allocation-free failures:
- `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`
- `Library_EmitMatchesGoldenSource`
- `Instance_EmitMatchesGoldenSource` × 3 (InstanceCounter, DoorActor, HealthRegen)
- `AiPrimitive_EmitMatchesGoldenSource` × 2 (MoveToAndFire, HasVisibleTarget)
- `TickFrame_1000Frames_AllocatesZeroBytes`
- `LibraryMath_GeneratedSource_Snapshot`
- `MoveToAndFire_GeneratedSource_Snapshot`

No new failures. The new `EditorSubsystemBlueprintWindowsTests` (4 tests) and `BlueprintWindowRegistrarTests` (3 tests) all pass within the 888 passing count.

### Grep-clean check
`BlueprintWindowRegistrar` (as active wiring) and `Blueprints.Editor.EditorSelectionStore` / `Blueprints.Editor.FileSystemAssetCatalog` (as composition-root usage) are absent from `EditorSubsystem.cs` — only comments and the deprecated null stub remain.

## Developer Insights

1. **`AccessViolationException` is a corrupted-state exception** — The `[HandleProcessCorruptedStateExceptions]` attribute is also required to catch AVs in try/catch in .NET Core, but the correct fix is prevention (the `GetCurrentContext()` guard), not catching. The existing `try/catch` in the adapter methods can remain as a defense against genuine managed exceptions (e.g. NullReferenceException if ImGuiNET itself is not yet loaded) but cannot and should not be the sole protection.

2. **`WindowManager` has no enumeration API** — `AllWindows` does not exist; only `TryGetWindow(id)` is public. The test migration pattern must use known IDs rather than filtering by `OwningPerspective`. This is actually stronger: it tests the exact contract (specific id + owning perspective).

3. **`_blueprintWindowRegistrar` field removal was correct** — The field was the only production backing; removing it ensures no path accidentally re-introduces the old registration. The `[Obsolete]` null-stub property ensures any stale external reference fails at the write site (compile error) while reads compile with a warning.

4. **The `info.NewAlc` for `TriggerInitialLoad`** — On the initial background-thread load, `DrainPendingCallbacks()` fires `OnReloadCompleted` with a collectible ALC containing the new assembly. The fallback `AppDomain.CurrentDomain.GetAssemblies()` handles edge cases (e.g. test environments where the DLL is pre-loaded into the default ALC).

5. **Potential improvement**: expose `IReadOnlyDictionary<string, ManagedWindow>` from `WindowManager` so tests can enumerate by perspective without knowing all IDs upfront. Currently compensated by `PerspectiveWorkspaceRegistrar.RegisteredWindows`.

## Known Issues

- **Phase 2 canvas (AIE-020) not wired** — The `PerspectiveWorkspaceRegistrar.RegisterExtraWindow` seam is in place. The `AiGraphCanvasWindow` will be added per-perspective in Phase 2.
- **`DebugSessionRegistry` factories not registered** — Phase 3 work. The registry exists but has no sessions; `RuntimeInspectorWindow` and `TraceTimelineWindow` will show empty state until Phase 3 wires the BTree/HSM session factories.
- **`RefactorService`'s `ReferenceCatalog` has no contributors** — Phase 5 work. Rename/find-references will be no-ops until Phase 5 registers the BTree/HSM/Blueprint reference contributors.
- **`_aiCatalogBuilder` field** initialized but `RefreshFromAssembly` only fires via `OnReloadCompleted`. If `TriggerInitialLoad()` completes before `OnReloadCompleted` is subscribed, no initial catalog refresh occurs in the test-only (no-Initialize) code path. This is correct behavior: in production `TriggerInitialLoad` fires asynchronously and the callback wires before `DrainPendingCallbacks()` is called on the main thread.

## Suggested Commit Message

```
feat(editor): AIE-015 + Corrective Task 0 — AV fix + shared AI editor composition (BATCH-04)

Corrective Task 0: guard EngineEditorTheme/ImGuiClipboard/ImGuiInputSource with
ImGui.GetCurrentContext() == IntPtr.Zero before any native ImGui deref.
Removes reliance on try/catch for corrupted-state AVs; 665/665 AiShared tests pass 3×.

AIE-015: EditorSubsystem composition rewrite.
- Wire AiAssetCatalogBuilder (BTree + HSM + Blueprint contributors) + hot-reload refresh.
- Wire AiDocumentManager + WindowManagerPerspectiveSwitcher.
- Register three PerspectiveWorkspaceRegistrars (BTree/HSM/Blueprint side panels).
- Register global AssetBrowserWindow.
- Retire CreateBlueprintWindowRegistrar + Blueprint parallel infra from composition root.
- Migrate EditorSubsystemBlueprintWindowsTests to assert new perspective behavior.
All suites green: AiShared 665/665, BootTests 10/10, Blueprints 888/906 (10 pre-existing DEBT-006 failures unchanged).
```

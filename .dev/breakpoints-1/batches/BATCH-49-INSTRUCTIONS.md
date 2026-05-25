# BATCH-49 — P10 Remaining Wiring (P10T3–P10T11)

**Role:** Developer  
**Skill file:** `.github/skills/developer/SKILL.md`  
**Reference docs:**  
- `.dev/breakpoints-1/TASK-DETAIL.md` — detailed specs for UBP-P10T3 through UBP-P10T11  
- `.dev/breakpoints-1/DESIGN.md` — §7, §8.2, §11.4, §12.1, §12.2, §12.3, §13.3  
- `.dev/breakpoints-1/DEBT-TRACKER.md`

---

## Context

BATCH-48 completed **UBP-P10T1** (EditorSubsystem BP wiring) and **UBP-P10T2** (CgfSubsystem BP wiring).  
This batch implements the **remaining 9 tasks** of Phase P10: `P10T3` through `P10T11`.

**Current test count**: 103 unit tests in `Hrot.Diagnostics.Breakpoints.Tests` + 5 integration tests in `BreakpointSubsystemWiringTests.cs`. All pass. The build is clean.

**Critical pre-condition for P10T4**: The BP wiring block in `EditorSubsystem.Initialize()` is currently positioned AFTER the gizmo systems construction. To pass `_bpManager` as `breakpointManager:` to gizmo systems, the BP wiring block **must be moved** to just before the gizmo systems block. Detailed below.

---

## Files to read before starting

1. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — full file (Initialize, RegisterWindows, Shutdown)
2. `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs` — DrainPendingCallbacks, ApplyQuickReload
3. `Hrot/Subsystems/Hrot.Editor/Windows/EditorWindows.cs` — window registration pattern
4. `.dev/breakpoints-1/TASK-DETAIL.md` lines ~440–620 (P10T3–P10T11 specs)
5. `Hrot/Engine/Hrot.Presentation/Windows/DataBreakpointManagerWindow.cs`  
6. `Hrot/Engine/Hrot.Presentation/Panels/Breakpoints/DataBreakpointManagerPanel.cs`
7. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/TemporalStatusBannerState.cs`
8. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` — constructor sig
9. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GlobalGizmoManager.cs` — constructor sig
10. `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` — `MutationInterceptor` property
11. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs` — constructor + SetDataBreakpointManager
12. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/DebugProbe.cs` — static Sink property
13. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs`
14. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmEditorHostServices.cs`
15. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/ICustomElementContextMenuProvider.cs`
16. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IEditorHostServices.cs`
17. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Debug/BTreeBreakpointMenuPopulator.cs`
18. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmBreakpointMenuPopulator.cs`
19. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Renderers/BTreeBreakpointGutterRenderer.cs`
20. `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmBreakpointGutterRenderer.cs`
21. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs`
22. `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs`

---

## Task overview

| Task | Description | Primary file(s) |
|------|-------------|-----------------|
| P10T3 | Register DataBreakpointManagerWindow per perspective | EditorSubsystem.RegisterWindows() |
| P10T4 | Inject IActiveViewProvider into gizmo systems + ordering fix | EditorSubsystem.Initialize() |
| P10T5 | Set MutationInterceptor on ComponentReflector | EditorSubsystem.RegisterWindows() |
| P10T6 | Wire BlueprintDebugSession ↔ manager bridge | EditorSubsystem.Initialize() + Shutdown() |
| P10T7 | BTree context menu + gutter renderer wiring | BTreeEditorHostServices.cs |
| P10T8 | HSM context menu + gutter renderer wiring | HsmEditorHostServices.cs |
| P10T9 | Blueprint canvas: invoke menu populator | GraphEditorWindow.cs |
| P10T10 | Subscribe manager to AiHotReloadCoordinator | AiHotReloadCoordinator.cs + EditorSubsystem.cs |
| P10T11 | Watches save/load editor lifecycle | EditorSubsystem.cs (Shutdown + Initialize) |

---

## Implementation instructions

### UBP-P10T4 — Move BP wiring block + inject IActiveViewProvider into gizmo systems

> **Do this task first** because it changes the ordering that all other tasks depend on.

**Problem**: In `EditorSubsystem.Initialize()`, the BP wiring block (which constructs `_bpManager`) currently sits AFTER the gizmo systems construction (~line 895). But the gizmo system constructors accept an optional `breakpointManager: IActiveViewProvider?` parameter. To pass `_bpManager`, the BP block must come first.

**Fix — move the BP block earlier**:

Find the BP wiring block:
```
// ── Universal breakpoints (UBP-P10T1) ────────────────────────────────────
// Allocate the pre-tick snapshot repo and mirror all component registrations.
_bpPreTickSnapshot = new EntityRepository();
...
_kernel.RegisterGlobalSystem(_bpSnapshotProvider);
_kernel.RegisterGlobalSystem(_bpSystem);
// ─────────────────────────────────────────────────────────────────────────
```

Move the **entire block** (from `// ── Universal breakpoints` comment down to the last `// ────...` line) so that it appears immediately **before** the `// ?? 4g. Gizmo subsystem` comment. All the prerequisites (`_timeController`, `_world`, `_behaviorRegistry`) are already assigned before that point.

**After the move, update the gizmo system constructions** inside section 4g:

```csharp
_editorDataDrivenGizmoSystem = new DataDrivenGizmoSystem(
    editorGizmoRegistry,
    _gizmoBuffer,
    isSelectedPredicate: static (view, entity) =>
        view.HasComponent<SelectionState>(entity) &&
        view.GetComponentRO<SelectionState>(entity).IsSelected,
    interactionBus: interactionBus,
    breakpointManager: _bpManager);   // ← ADD THIS

_globalGizmoManager = new GlobalGizmoManager(_gizmoBuffer, interactionBus,
    breakpointManager: _bpManager);   // ← ADD THIS
```

Also check `CgfSubsystem.cs` for equivalent gizmo system constructions and add `breakpointManager:` there too if `_bpManager` is available in that subsystem.

**Also look for `BehaviorGizmoManagerSystem`** in EditorSubsystem/CgfSubsystem. If it is constructed, add `breakpointManager: _bpManager` to it as well (optional constructor param exists).

**Success conditions to test** (integration tests in `BreakpointSubsystemWiringTests.cs`):
- `Gizmo_System_UsesManagerActiveView_WhenPaused` — use `EditorHarness`, fire a BP to pause, tick one frame, assert gizmo system received `manager.ActiveView` (= `_preTickSnapshot`) rather than the live view.
- `Gizmo_System_FallsBackWhenNoManager` — use a harness without a manager; assert no NRE and view falls back to live repo.

---

### UBP-P10T3 — Register DataBreakpointManagerWindow per perspective

**Location**: `EditorSubsystem.RegisterWindows()` — add after the `FdpEntityInspectorWindow` registration block.

```csharp
// ?? Data Breakpoint Manager window (UBP-P10T3) ???????????????????????????????????
if (_bpManager != null)
{
    var bpBannerState = new Hrot.Diagnostics.Breakpoints.TemporalStatusBannerState();
    var bpPanel       = new Hrot.Presentation.Panels.Breakpoints.DataBreakpointManagerPanel(
        _bpManager, bpBannerState);
    var bpWin         = new Hrot.Presentation.Windows.DataBreakpointManagerWindow(
        "editor_bp_manager", "Editor", bpPanel, EditorWindowColor.TitleBar);
    windowManager.RegisterWindow(bpWin);
}
```

Check the `DataBreakpointManagerWindow` constructor signature (it is: `(string id, string owningPerspective, DataBreakpointManagerPanel panel, Vector4? titleBarColor = null)`).

**For the "CGF" perspective**: Look at `CgfSubsystem.cs` — it also has a `RegisterWindows()` method (or equivalent). If `_bpManager` is accessible there, register a second window with perspective `"CGF"` and id `"cgf_bp_manager"`.

**Success conditions** (integration tests in `BreakpointSubsystemWiringTests.cs`):
- `ManagerWindow_RegisteredInEditorPerspective` — boot `EditorHarness`, call `RegisterWindows`, assert `windowManager.GetWindows(perspective: "Editor")` contains a `DataBreakpointManagerWindow`.
- `ManagerWindow_NotShownInUnrelatedPerspective` — assert `windowManager.GetWindows(perspective: "IG")` does NOT contain a `DataBreakpointManagerWindow`.
- `ManagerWindow_OpensOnMenuCommand` — call `bpWin.IsOpen = true`, assert `bpWin.IsOpen`.

---

### UBP-P10T5 — Inject IMutationInterceptor into ComponentEditWindow

**Location**: `EditorSubsystem.RegisterWindows()` — add after the `_fdpEntityInspector` setup block.

```csharp
// ?? UBP-P10T5: wire breakpoint interceptor into entity inspector's reflector ??????????
if (_bpManager != null)
    _fdpEntityInspector.Reflector.MutationInterceptor = _bpManager;
```

`DataBreakpointManager` implements `IMutationInterceptor` (verify). The `MutationInterceptor` property is on `ComponentReflector` (see `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` line ~65). When set, `TryOpenEditWindow` passes `interceptor: MutationInterceptor` to the `ComponentEditWindow` constructor.

**Success conditions** (integration tests):
- `Inspector_EditWhilePaused_RoutesToStageMutation` — boot editor with manager, engage pause, commit an edit via `IEditSession.Commit()`, assert `manager.PendingMutationsCount == 1` and live component is UNCHANGED.
- `Inspector_EditWhileRunning_StillDirectWrites` — same without pausing; assert live component updates and `PendingMutationsCount == 0`.

---

### UBP-P10T6 — Wire BlueprintDebugSession ↔ manager bridge

**Location**: `EditorSubsystem.Initialize()` — append to end of the BP wiring block (after `_bpSystem` is created and before `_kernel.Initialize()`).

```csharp
// ── Blueprint debug session bridge (UBP-P10T6) ───────────────────────────────────
var bpBlueprintSession = new Hrot.Blueprints.Core.Debug.BlueprintDebugSession(
    _blueprintRegistry, _world!, bpTimeAdapter);
bpBlueprintSession.SetDataBreakpointManager(_bpManager);
Hrot.Blueprints.Core.DebugProbe.Sink = bpBlueprintSession;
_blueprintDebugSession = bpBlueprintSession;
// ─────────────────────────────────────────────────────────────────────────────────
```

Add a private field:
```csharp
private Hrot.Blueprints.Core.Debug.BlueprintDebugSession? _blueprintDebugSession;
```

**In `Shutdown()`** — add BEFORE `_aiCoordinator?.Dispose()`:
```csharp
Hrot.Blueprints.Core.DebugProbe.Sink = null;
_blueprintDebugSession = null;
```

`_blueprintRegistry` is a field `private BlueprintRegistry _blueprintRegistry = new();` already on EditorSubsystem.  
`bpTimeAdapter` is a local created in the BP block (`MasterSyncTimeControllerAdapter bpTimeAdapter = new MasterSyncTimeControllerAdapter(_timeController!);`). Make `bpTimeAdapter` available in scope by creating it early in the BP block.  
`_world` implements `ISimulationView` (see `EntityRepository.View.cs`).  
`bpTimeAdapter` implements `IEngineDebugTimeController`.

**Success conditions** (integration tests):
- `Blueprint_NodeBP_RoutesThroughManager_TripleBufferApplied` — boot editor with both wired; register a node BP; let `OnNodeEnter` fire; assert `manager.IsPaused == true` (triple-buffer rewind engaged via session bridge, not direct pause).

---

### UBP-P10T7 — BTree canvas: CustomElementContextMenu + gutter renderer wiring

**Background**: `BTreeEditorHostServices` is `internal sealed` to `Hrot.BTree.Editor` and is NOT yet constructed in production (only in tests). The production canvas wiring for BTree is a stub/placeholder. The work here wires the INFRASTRUCTURE so that when the canvas is constructed, the breakpoint features are already connected.

**Work in `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeEditorHostServices.cs`**:

1. Add `IDataBreakpointManager? BreakpointManager` property / field.
2. Add `SetBreakpointManager(IDataBreakpointManager? manager)` method.
3. Override `CustomElementContextMenu` property to return a `BTreeBreakpointContextMenuProvider` when a manager is set:

```csharp
private BTreeBreakpointContextMenuProvider? _bpContextMenuProvider;

public void SetBreakpointManager(IDataBreakpointManager? manager)
{
    _bpContextMenuProvider = manager != null
        ? new BTreeBreakpointContextMenuProvider(manager)
        : null;
}

public override ICustomElementContextMenuProvider? CustomElementContextMenu => _bpContextMenuProvider;
```

4. **Create `BTreeBreakpointContextMenuProvider`** (new internal class, same file or separate file inside `Host/` or `Debug/`):

```csharp
internal sealed class BTreeBreakpointContextMenuProvider : ICustomElementContextMenuProvider
{
    private readonly IDataBreakpointManager _manager;

    public BTreeBreakpointContextMenuProvider(IDataBreakpointManager manager)
        => _manager = manager;

    public string RendererId => "btree.breakpoint_gutter";  // matches BTreeBreakpointGutterRenderer.Id

    public IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit)
    {
        // elementKey is the node VisualId string.
        // Build a BTreeEditorNode stub from the elementKey (VisualId only — KernelBlobIndex unknown here).
        // The gutter renderer encodes the node's VisualId as the elementKey.
        var stubNode = new BTreeEditorNode
        {
            VisualId        = Guid.TryParse(elementKey, out var g) ? g : Guid.Empty,
            KernelBlobIndex = 0,      // not available from elementKey alone
            DisplayLabel    = elementKey,
        };

        var collector = new ContextMenuItemCollector();
        BTreeBreakpointMenuPopulator.PopulateMenu(stubNode, collector, _manager);
        return collector.Items;
    }
}
```

5. **Implement `ContextMenuItemCollector : IContextMenuBuilder`** — a simple collector that maps `AddItem(label, callback)` to `ContextMenuItem(label, callback)`:

```csharp
internal sealed class ContextMenuItemCollector : IContextMenuBuilder
{
    private readonly List<ContextMenuItem> _items = new();
    public IReadOnlyList<ContextMenuItem> Items => _items;

    public void AddItem(string label, Action callback, bool enabled = true)
        => _items.Add(new ContextMenuItem(label, callback, Enabled: enabled));

    public IContextMenuBuilder BeginSubmenu(string label) => this;
    public void EndSubmenu() { }
    public void AddSeparator() { }
}
```

6. **Gutter renderer wiring**: In `BTreeEditorHostServices`, also hold a `BTreeBreakpointGutterRenderer?` and expose it:

```csharp
private BTreeBreakpointGutterRenderer? _bpGutterRenderer;

// Call from SetBreakpointManager:
_bpGutterRenderer = manager != null ? new BTreeBreakpointGutterRenderer() : null;
if (_bpGutterRenderer != null)
    _bpGutterRenderer.SetManager(manager);
```

Check if `BTreeBreakpointGutterRenderer` constructor takes a `BehaviorTreeAsset` parameter; if so, the renderer may need to be created lazily when the asset is known. Read the constructor signature.

**Success conditions** (unit tests in `Hrot.BTree.Editor.Tests` or `Hrot.Diagnostics.Breakpoints.Tests`):
- `BTree_ContextMenu_ShowsBreakpointItems_WhenManagerWired` — create `BTreeEditorHostServices` with `SetBreakpointManager(manager)` called; assert `CustomElementContextMenu != null`; call `GetItemsFor(nodeVisualId, hit)`, assert result contains `ContextMenuItem` with label "Break on Activation (Enter)".
- `BTree_GutterRenderer_ManagerWired_IsReady` — assert `_bpGutterRenderer != null` after `SetBreakpointManager`.

> Note: `BTree_GutterRenderer_DrawsDotForRegisteredBP` from TASK-DETAIL requires actual rendering context; write the above unit test as a stand-in for now.

---

### UBP-P10T8 — HSM canvas: CustomElementContextMenu + gutter renderer wiring

**Same pattern as P10T7** but for `HsmEditorHostServices`.

**Work in `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmEditorHostServices.cs`**:
- Add `SetBreakpointManager(IDataBreakpointManager? manager)` method.
- Override `CustomElementContextMenu` → `HsmBreakpointContextMenuProvider` (analogous to `BTreeBreakpointContextMenuProvider`).
- `HsmBreakpointGutterRenderer.Id = "hsm.breakpoint_gutter"`.
- Call `HsmBreakpointMenuPopulator.PopulateStateMenu(...)` in the provider's `GetItemsFor`.

Read `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Debug/HsmBreakpointMenuPopulator.cs` to understand the method signature.

**Success conditions** (unit tests): mirror P10T7 with HSM-specific opcode items.

---

### UBP-P10T9 — Blueprint canvas: invoke menu populator

**Location**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs`

`GraphEditorWindow.DrawUI()` currently has a stub canvas area:
```csharp
ImGui.BeginChild("##canvas", ...);
ImGui.TextDisabled($"Graph: {CurrentAsset.Name}");
ImGui.EndChild();
```

Add `IDataBreakpointManager?` to `GraphEditorWindow` (inject via constructor or a `SetBreakpointManager` method). When a node is right-clicked (simulated via ImGui popup context on the canvas stub), call `BlueprintBreakpointMenuPopulator.PopulateNodeMenu(...)`.

For now, add the wiring infrastructure and test it at the unit level:

1. Add `public void SetBreakpointManager(IDataBreakpointManager? manager) => _bpManager = manager;` and private field.
2. In the canvas stub's child area, replace `TextDisabled` with a selectable node list from `CurrentAsset?.Nodes` if available, with right-click popup calling `BlueprintBreakpointMenuPopulator.PopulateNodeMenu`.

Read `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintBreakpointMenuPopulator.cs` to understand the method signature.

**Success conditions** (unit tests):
- `Blueprint_ContextMenu_ShowsConditionalBreakpointItem` — create `GraphEditorWindow` with `SetBreakpointManager(manager)` called; using a recording builder, call `BlueprintBreakpointMenuPopulator.PopulateNodeMenu(nodeId, assetId, builder, manager, null)` directly; assert "Add Conditional Data Breakpoint..." is present.

---

### UBP-P10T10 — Subscribe manager to AiHotReloadCoordinator

**Two-step work**:

**Step A — Add `OnReloadBegin` event to `AiHotReloadCoordinator`**:

In `AiHotReloadCoordinator.cs`, add:
```csharp
/// <summary>Fired just before the new assembly is swapped into _currentAlc.</summary>
public event Action? OnReloadBegin;
```

In `DrainPendingCallbacks()`, fire it immediately before Step 6 (the ALC swap):
```csharp
// Step 5.5: notify before the swap so pending mutations are flushed.
OnReloadBegin?.Invoke();

// Step 6: swap _currentAlc and release the old ALC.
var oldAlc = _currentAlc;
...
```

Do the same in `ApplyQuickReload()` — fire `OnReloadBegin?.Invoke()` before the ALC swap there as well.

**Step B — Subscribe in `EditorSubsystem.Initialize()`**:

After the BP wiring block (where `_bpManager` is constructed) and after `_aiCoordinator` is created, add these subscriptions:

```csharp
// ── UBP-P10T10: forward reload events to breakpoint manager ─────────────────────
_aiCoordinator.OnReloadBegin     += () => _bpManager?.OnHotReloadBegin();
_aiCoordinator.OnReloadCompleted += _  => _bpManager?.OnHotReloadCompleted();
// ─────────────────────────────────────────────────────────────────────────────────
```

> The `OnReloadBegin` subscription must run before the ALC swap (already guaranteed by the position in `DrainPendingCallbacks`). The `OnReloadCompleted` must run after assemblies are loaded.

**Success conditions** (integration tests in `BreakpointSubsystemWiringTests.cs`):
- `HotReload_WhilePaused_FlushesPendingAndContinues` — boot editor with wired manager; pause via BP; stage 2 mutations; simulate a hot reload (call `_aiCoordinator.DrainPendingCallbacks()` after enqueuing a mock reload); assert `PendingMutationsCount == 0`, `IsPaused == false` after reload.
- `HotReload_RebindsCompiledDelegates` — register a `TraceBufferScanPredicateDto` BP; trigger reload; assert BP still mounted, `IsBroken == false`, still fires.
- `HotReload_StructuralBreak_MarksBPIsBroken_NoCrash` — trigger reload that changes component layout; assert no exception, BP is `IsBroken`.

---

### UBP-P10T11 — Watches save/load editor lifecycle integration

**In `EditorSubsystem.Initialize()`** — add immediately AFTER the BP wiring block (after `_bpManager` is constructed):

```csharp
// ── UBP-P10T11: restore watches from previous session ───────────────────────────
var watchesFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watches.json");
if (_bpManager != null && System.IO.File.Exists(watchesFilePath))
{
    try { _bpManager.LoadWatches(watchesFilePath); }
    catch (Exception ex)
    {
        Console.WriteLine($"[UBP] Failed to load watches.json: {ex.Message}");
    }
}
// ─────────────────────────────────────────────────────────────────────────────────
```

**In `EditorSubsystem.Shutdown()`** — add BEFORE `_aiCoordinator?.Dispose()`:

```csharp
// ── UBP-P10T11: persist watches for next session ─────────────────────────────────
if (_bpManager != null)
{
    try
    {
        var watchesFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watches.json");
        _bpManager.SaveWatches(watchesFilePath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[UBP] Failed to save watches.json: {ex.Message}");
    }
}
// ─────────────────────────────────────────────────────────────────────────────────
```

**Success conditions** (integration tests):
- `Watches_RoundTripAcrossEditorRestart` — create manager, add 3 BPs, call `SaveWatches(path)`, create a new manager, call `LoadWatches(path)`, assert 3 BPs are restored with identical conditions.
- `Watches_Restore_FailsGracefullyOnDriftedSchema` — write a malformed `watches.json`, call `LoadWatches`, assert no exception propagates and editor (manager) still operates normally.

---

## Tests to write

Write all new tests in:
- **`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/BreakpointSubsystemWiringTests.cs`** for integration tests (P10T3, P10T4, P10T5, P10T6, P10T10, P10T11).
- **`Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/`** or **`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/`** for P10T7/P10T8 unit tests.
- **`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`** for P10T9 unit tests.

Use the existing `_domainCounter` pattern in `BreakpointSubsystemWiringTests.cs` (currently at 164 after BATCH-48; reserve domains 165+ for new tests).

### Required test method names (reference the exact strings from TASK-DETAIL success conditions):
- `Gizmo_System_UsesManagerActiveView_WhenPaused`
- `Gizmo_System_FallsBackWhenNoManager`
- `ManagerWindow_RegisteredInEditorPerspective`
- `ManagerWindow_NotShownInUnrelatedPerspective`
- `ManagerWindow_OpensOnMenuCommand`
- `Inspector_EditWhilePaused_RoutesToStageMutation`
- `Inspector_EditWhileRunning_StillDirectWrites`
- `Blueprint_NodeBP_RoutesThroughManager_TripleBufferApplied`
- `BTree_ContextMenu_ShowsBreakpointItems_WhenManagerWired`
- `BTree_GutterRenderer_ManagerWired_IsReady`
- `Hsm_ContextMenu_ShowsBreakpointItems_WhenManagerWired`
- `Hsm_GutterRenderer_ManagerWired_IsReady`
- `Blueprint_ContextMenu_ShowsConditionalBreakpointItem`
- `HotReload_WhilePaused_FlushesPendingAndContinues`
- `HotReload_RebindsCompiledDelegates`
- `HotReload_StructuralBreak_MarksBPIsBroken_NoCrash`
- `Watches_RoundTripAcrossEditorRestart`
- `Watches_Restore_FailsGracefullyOnDriftedSchema`

---

## Build validation

After all changes, run:
```
dotnet build IOS-IG-SimHost-FDP.sln -v quiet
```
The build **must** show **0 errors**. Warnings are acceptable but should be reviewed.

Then run all tests:
```
dotnet test IOS-IG-SimHost-FDP.sln --filter "Category=UBP|FullyQualifiedName~Breakpoint"
```
All pre-existing tests must continue to pass. All new tests must pass.

---

## Report format

When done, write a batch report at `.dev/breakpoints-1/reports/BATCH-49-REPORT.md` following the developer skill report format. Include:
- Summary of changes made
- List of new test methods with pass/fail status
- Any deviations from these instructions with rationale
- Build output (0 errors confirmed)
- Any issues or questions for the dev lead

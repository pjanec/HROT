# BATCH-04: Phase 4 + Phase 5 — Persistence, Docking, and Framework Integration

**Batch Number:** BATCH-04  
**Tasks:** WM-S401, WM-S402, WM-S501, WM-S502, WM-S503  
**Phase:** Phase 4 (Persistence & Docking) + Phase 5 (Framework Integration)  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (Icons), BATCH-02 (ManagedWindow), BATCH-03 (WindowManager)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch wires the Window Manager into the runner framework. Tasks are split across three projects:
- `FDP.Toolkit.ImGui` — WM-S401 (settings handler inside WindowManager)
- `FDP.Framework.Runner` — WM-S402 (docking), WM-S501 (expose WindowManager), WM-S503 (dockspace height)
- `Hrot.ClusterRunner` — WM-S502 (composition root bridge) + `Hrot.Common` (new TogglePerspectiveEvent)

Work in order: WM-S401 → WM-S402 → WM-S501 → WM-S503 → WM-S502.

### Required Reading (IN ORDER)

1. **Design Document (§4.4.6, §5.4, §2.1, §4.1.4, §6.2–6.3):** `.dev/win-mgr-1/DESIGN.md`
2. **Task Details (Phase 4 + 5):** `.dev/win-mgr-1/TASK-DETAIL.md` — WM-S401 through WM-S503.
3. **Previous Reviews:** `.dev/win-mgr-1/reviews/BATCH-03-REVIEW.md`
4. **Existing SubsystemOrchestrator:** `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`
5. **Existing SubsystemConfig:** `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs`
6. **Existing Program.cs:** `Hrot.ClusterRunner/Program.cs`

### Source Code Location

- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs` _(modify — WM-S401)_
- `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` _(modify — WM-S402, WM-S501, WM-S503)_
- `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs` _(modify — WM-S501)_
- `FDP/Framework/FDP.Framework.Runner/FDP.Framework.Runner.csproj` _(modify — add FDP.Toolkit.ImGui reference)_
- `Hrot.Common/Events/TogglePerspectiveEvent.cs` _(create — WM-S701 dependency for WM-S502)_
- `Hrot.ClusterRunner/Program.cs` _(modify — WM-S502)_

### Report Submission

**When done, write your report to:** `.dev/win-mgr-1/reports/BATCH-04-REPORT.md`

---

## 🎯 Tasks

### Task WM-S401: ImGui Custom Settings Handler for `IsOpen` / `IsPinned` Persistence

See full details: [TASK-DETAIL.md §WM-S401](../../win-mgr-1/TASK-DETAIL.md#wm-s401-imgui-custom-settings-handler-for-isopen--ispinned-persistence)

**File to modify:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

Register a custom ImGui settings handler in the `WindowManager` constructor using `ImGui.AddSettingsHandler(handler)`:

```csharp
// In constructor:
var handler = new ImGuiSettingsHandler
{
    TypeName = "FDP_WindowManager",
    TypeHash = ImGui.ImHashStr("FDP_WindowManager"),
    ReadOpenFn = (ctx, name) => { /* validate section name */ return IntPtr.Zero; },
    ReadLineFn = (ctx, entry, line) => ReadSettingsLine(line),
    WriteAllFn = (ctx, buf) => WriteSettings(buf),
};
ImGui.AddSettingsHandler(handler);
```

**Write format** (called by ImGui when saving):
```
[FDP_WindowManager]
window_id=True,False
another_id=False,True
```

**Read format** (called by ImGui when loading `imgui.ini`):
- Split line on `'='`, parse key as window Id.
- Split value on `','`, parse as `bool,bool` → `IsOpen`, `IsPinned`.
- If window registered → restore. Unknown Id or malformed line → skip silently.

**Tests:** All 7 WM-S401 success conditions. Test round-trip serialization without ImGui context (extract serialization logic to internal methods). For WM-S401.1 (handler registered), accept that this is integration-level; document in report.

**Important:** `ImGuiSettingsHandler` in ImGui.NET may require casting/unsafe access. Check what's available in the ImGui.NET 1.91.x bindings. If `ImGui.AddSettingsHandler` is not available via managed bindings, document this as a P2 debt item and implement a stub that saves/loads settings to a separate `.json` file instead (using `System.Text.Json`). The round-trip behavior must still work.

---

### Task WM-S402: ImGui Docking Integration

See full details: [TASK-DETAIL.md §WM-S402](../../win-mgr-1/TASK-DETAIL.md#wm-s402-imgui-docking-integration)

**File to modify:** `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`

**Step 1 — Enable docking at initialization:**
In `Initialize()`, after `rlImGui.Setup(true)`, add:
```csharp
ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
```

**Step 2 — Create dockspace at start of each UI frame:**
In the `Render()` method, inside the `rlImGui.Begin()` / `rlImGui.End()` block, before any other UI calls, insert the fullscreen dockspace:

```csharp
// Create fullscreen dockspace (transparent, passthrough)
var viewport = ImGui.GetMainViewport();
ImGui.SetNextWindowPos(viewport.WorkPos);
ImGui.SetNextWindowSize(viewport.WorkSize);
ImGui.SetNextWindowViewport(viewport.ID);
ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
var dockspaceFlags = ImGuiWindowFlags.NoDocking
    | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse
    | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
    | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus
    | ImGuiWindowFlags.NoBackground;
ImGui.Begin("##DockSpace", dockspaceFlags);
ImGui.PopStyleColor();
ImGui.PopStyleVar(2);
// Dockspace size will be reduced in WM-S503 to reserve status bar space
ImGui.DockSpace(ImGui.GetID("MainDockSpace"), Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);
ImGui.End();
```

This must happen **before** `DrawMainMenuBar()` and before all subsystem `DrawUI()` calls.

**Note on existing `DrawMainMenuBar()`:** The existing orchestrator has its own `DrawMainMenuBar()` — **keep it for now** (it will be replaced in WM-S501 when the WindowManager takes over). For this task just add docking around it.

---

### Task WM-S501: `SubsystemOrchestrator` — Expose `WindowManager` to Subsystems

See full details: [TASK-DETAIL.md §WM-S501](../../win-mgr-1/TASK-DETAIL.md#wm-s501-subsystemorc-integration--expose-windowmanager-to-subsystems)

**Files to modify:**
- `FDP/Framework/FDP.Framework.Runner/FDP.Framework.Runner.csproj` — add `FDP.Toolkit.ImGui` reference
- `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs` — add `WindowManager?` property
- `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` — create+own WindowManager, replace DrawMainMenuBar

**Step 1 — Add project reference:**
In `FDP.Framework.Runner.csproj`, add:
```xml
<ProjectReference Include="..\..\Toolkits\FDP.Toolkit.ImGui\FDP.Toolkit.ImGui.csproj" />
```

**Step 2 — Extend `SubsystemConfig`:**
```csharp
using FDP.Toolkit.ImGui.WindowManager;

// In SubsystemConfig:
/// <summary>
/// When non-null, subsystems can register windows and menu items during Initialize.
/// Null in headless mode or when the Window Manager is not configured.
/// </summary>
public WindowManager? WindowManager { get; set; }
```

**Step 3 — Orchestrator owns WindowManager:**
Add field: `private FDP.Toolkit.ImGui.WindowManager.WindowManager? _windowManager;`  
And a public property: `public FDP.Toolkit.ImGui.WindowManager.WindowManager? WindowManager => _windowManager;`

In `Initialize()`, after `rlImGui.Setup(true)` and docking setup, when NOT headless:
```csharp
// Create a dummy atlas (no actual texture — icons show as blank, no crashes)
var dummyAtlas = new FDP.Toolkit.ImGui.Icons.IconAtlas(IntPtr.Zero, 256f, 256f, 16f);
_windowManager = new FDP.Toolkit.ImGui.WindowManager.WindowManager(dummyAtlas);
```

Set it in `SubsystemConfig` for each subsystem:
```csharp
cfg.WindowManager = _windowManager;
```

**Step 4 — Replace DrawMainMenuBar with WindowManager.Render():**
In the `Render()` method, replace the `DrawMainMenuBar()` call with:
```csharp
_windowManager?.Render();
```

Remove the old `DrawMainMenuBar()` private method (or keep it as a fallback if `_windowManager == null`, your call — document in report).

**Step 5 — WindowManager.Render() before subsystem DrawUI():**
Ensure `_windowManager?.Render()` is called **before** the `for` loop over `subsystem.DrawUI()`. The full frame structure inside `rlImGui.Begin()/End()` should be:
```
DockSpace creation
_windowManager?.Render()    // menu bar + all managed windows
for each subsystem: subsystem.DrawUI()   // subsystem content
```

**Tests:** WM-S501 success conditions. Since this touches the orchestrator (which requires Raylib at runtime), focus tests on:
- The SubsystemConfig.WindowManager property being set before Initialize calls (integration test stub or mock).
- Build compiles correctly.
- Existing headless tests in FDP that use SubsystemOrchestrator still pass.

---

### Task WM-S503: Dockspace Height — Reserve Status Bar Space

See full details: [TASK-DETAIL.md §WM-S503](../../win-mgr-1/TASK-DETAIL.md#wm-s503-subsystemorc-dockspace-height--reserve-status-bar-space)

**File to modify:** `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`

Update the `DockSpace` call (from WM-S402) to pass a size that reserves the status bar height:
```csharp
float statusBarHeight = _windowManager?.StatusBar?.Height ?? 0f;
var dockspaceSize = new Vector2(0f, viewport.WorkSize.Y - statusBarHeight - viewport.WorkSize.Y * 0f);
// Simplified: pass (0,0) initially; DockSpace with Vector2.Zero uses full size
// Actual: only reduce when statusBarHeight > 0
var dockspaceSize = statusBarHeight > 0
    ? new Vector2(viewport.WorkSize.X, viewport.WorkSize.Y - statusBarHeight)
    : Vector2.Zero;
ImGui.DockSpace(ImGui.GetID("MainDockSpace"), dockspaceSize, ImGuiDockNodeFlags.PassthruCentralNode);
```

**Note:** `StatusBar` property and `StatusBarManager` class will be implemented in BATCH-05. For now, `_windowManager?.StatusBar` will be null/not-yet-implemented. Add a null-safe access: `_windowManager?.StatusBar?.Height ?? 0f`. This will resolve to `0f` until BATCH-05 adds the StatusBar.

**Tests:** Condition 3 (Height=0 → degrades gracefully) can be tested. Others are visual.

---

### Task WM-S502: Composition Root — `OnPerspectiveChanged` → Publish `TogglePerspectiveEvent`

See full details: [TASK-DETAIL.md §WM-S502](../../win-mgr-1/TASK-DETAIL.md#wm-s502-composition-root--onperspectivechanged--publish-toggleperspectiveevent)

**Step 1 — Create `TogglePerspectiveEvent` in `Hrot.Common`:**
Create `Hrot.Common/Events/TogglePerspectiveEvent.cs`:
```csharp
namespace Hrot.Common;
public record TogglePerspectiveEvent(string OldPerspective, string NewPerspective);
```

Check if `Hrot.Common` has an `Events/` folder — if not, create it. (There is an `Orchestration/` folder currently.)

**Step 2 — Wire in `Hrot.ClusterRunner/Program.cs`:**

After `orchestrator.Initialize()`, subscribe to the WindowManager's perspective change event:
```csharp
var windowManager = orchestrator.WindowManager;
if (windowManager != null)
{
    windowManager.OnPerspectiveChanged += (oldPersp, newPersp) =>
    {
        // fdpEventBus.Publish(new TogglePerspectiveEvent(oldPersp, newPersp));
        // TODO: wire to actual FdpEventBus once WM-S703 is implemented
        // For now, just log the transition
        Console.WriteLine($"[Runner] Perspective changed: {oldPersp} → {newPersp}");
    };
}
```

**IMPORTANT:** `FdpEventBus` integration is deferred to BATCH-05 (WM-S703). For this batch, just create the event record and stub the subscription. The stub must still compile and run without throwing. Document this in the report.

**Tests:** 
1. `TogglePerspectiveEvent` value equality test (record comparison).
2. `TogglePerspectiveEvent` immutability test (no public setters).
3. Build compiles with no errors.

Tests for WM-S502.1 and WM-S502.2 require a running orchestrator — defer to integration test level; document in report.

---

## 🧪 Test-Driven Task Progression

**Follow for each task. The toolkit tests must still all pass:**

```
dotnet build FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj
dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj
dotnet build FDP/Framework/FDP.Framework.Runner/FDP.Framework.Runner.csproj
dotnet build Hrot.ClusterRunner/Hrot.ClusterRunner.csproj
```

All must succeed with zero errors. All 135 existing tests must still pass.

---

## 🧱 Critical Implementation Notes

1. **`ImGui.AddSettingsHandler` availability:** Check if `ImGuiSettingsHandler` struct is accessible in ImGui.NET 1.91.x. It involves unsafe/pointer operations. **If not feasible**, implement a JSON-based fallback persistence to `{AppDataPath}/fdp_windows.json` and log a P2 debt item. The round-trip behavior still passes tests.

2. **`ImGuiConfigFlags.DockingEnable`:** This is available in ImGui.NET 1.91.x. The flag is `ImGuiConfigFlags.DockingEnable`.

3. **Using directives in SubsystemOrchestrator:** The orchestrator uses `using ImGuiNET;` already. Add `using FDP.Toolkit.ImGui.WindowManager;` and `using FDP.Toolkit.ImGui.Icons;` (or use fully qualified names).

4. **Headless guard:** All non-headless rendering code must be wrapped in `if (!_headless)` or only called from the non-headless path. `_windowManager` should be null in headless mode (set only after `rlImGui.Setup()`).

5. **`StatusBar` ahead of implementation:** Write `_windowManager?.StatusBar?.Height ?? 0f` using null-conditional chain. Add a stub `public StatusBarManager? StatusBar => null;` property to `WindowManager.cs` temporarily if needed to allow compilation. Mark with a `// TODO: WM-S602` comment. Alternatively, add `public float StatusBarHeight => _statusBar?.Height ?? 0f;` as a simpler accessor.

6. **`SubsystemOrchestrator.WindowManager` exposure:** The public property should return `FDP.Toolkit.ImGui.WindowManager.WindowManager?`. Since the class name clashes with namespace, use a type alias: `using WM = FDP.Toolkit.ImGui.WindowManager.WindowManager;` in files that need it.

7. **`TogglePerspectiveEvent` namespace:** Use `namespace Hrot.Common;` to be consistent with existing types in that project.

8. **Don't break existing orchestrator tests:** Check `FDP/Framework/FDP.Framework.Runner/Testing/` for any test files. The SubsystemOrchestrator integration tests in `Hrot.ClusterRunner.Integration.Tests/` use the orchestrator in headless mode — ensure headless mode still works (no WindowManager created, no docking, no menu bar calls).

9. **Removing `DrawMainMenuBar()`:** When replacing with `_windowManager?.Render()`, you can remove the private `DrawMainMenuBar()` method entirely. The perspective-switching buttons it contained will be replaced by `WindowManager`'s perspective switcher UI. No regression since the window manager implementation is more capable.

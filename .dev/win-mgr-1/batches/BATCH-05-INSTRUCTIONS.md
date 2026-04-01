# BATCH-05: Phase 6 + Phase 7 — Status Bar & Background Map Perspective Manager

**Batch Number:** BATCH-05  
**Tasks:** WM-S601, WM-S602, WM-S603, WM-S701, WM-S702, WM-S703  
**Phase:** Phase 6 (Status Bar) + Phase 7 (Background Map Perspective Manager)  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 through BATCH-04 all complete

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the **final batch** for the `win-mgr-1` workstream. It has two distinct areas:

**Phase 6 — Status Bar (WM-S601–603):** A persistent bottom bar where subsystems register render delegates. Implemented entirely in `FDP.Toolkit.ImGui`; wired into `WindowManager` and `Hrot.ClusterRunner/Program.cs`.

**Phase 7 — Background Map Perspective Manager (WM-S701–703):** The ECS-side coordination layer for syncing the world-space rendering perspective with the window perspective. Lives in `Hrot.Common` (event + component) and `Hrot.ClusterRunner` (system).

Work in order: WM-S601 → WM-S602 → WM-S603 → WM-S701 → WM-S702 → WM-S703.

### Required Reading (IN ORDER)

1. **Design Document (§5, §6):** `.dev/win-mgr-1/DESIGN.md`
2. **Task Details (Phase 6 + 7):** `.dev/win-mgr-1/TASK-DETAIL.md` — WM-S601 through WM-S703.
3. **Previous Reviews:** `.dev/win-mgr-1/reviews/BATCH-04-REVIEW.md`
4. **FDP ECS patterns:** `FDP/Kernel/Fdp.Kernel/ComponentSystem.cs`, `FDP/Kernel/Fdp.Kernel/FdpEventBus.cs`
5. **OrchestratorSubsystem for wiring context:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`
6. **Existing WindowManager.cs** (the `StatusBarHeight` stub added in BATCH-04)

### Source Code Location

- **Phase 6:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/StatusBarManager.cs` _(create)_
- **Phase 6:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs` _(modify — replace stub)_
- **Phase 6:** `Hrot.ClusterRunner/Program.cs` _(modify — register reference section)_
- **Phase 7:** `Hrot.Common/Events/TogglePerspectiveEvent.cs` _(already exists from BATCH-04)_
- **Phase 7:** `Hrot.Common/Components/ActivePerspective.cs` _(create)_
- **Phase 7:** `Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs` _(create — create the Systems/ folder)_
- **Phase 7:** `Hrot.ClusterRunner/Program.cs` _(modify — wire PerspectiveCoordinatorSystem, publish actual event)_

### Tests

- Phase 6 tests: `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/StatusBarManagerTests.cs`
- Phase 7 tests: `Hrot.ClusterRunner.Tests/PerspectiveCoordinatorSystemTests.cs`

### Report Submission

**When done, write your report to:** `.dev/win-mgr-1/reports/BATCH-05-REPORT.md`

---

## 🎯 Tasks

### Task WM-S601: `StatusBarManager` — Delegate Registry + Sorted Render Loop

See full details: [TASK-DETAIL.md §WM-S601](../../win-mgr-1/TASK-DETAIL.md#wm-s601-statusbarmanager--delegate-registry--sorted-render-loop)

**File to create:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/StatusBarManager.cs`

```csharp
namespace FDP.Toolkit.ImGui.WindowManager;

public class StatusBarManager
{
    private struct Section
    {
        public string Id;
        public int    SortOrder;
        public Action RenderDelegate;
    }

    private readonly List<Section>  _sections  = new();
    private          bool           _needsSort = false;
    public           float          Height     { get; private set; }

    public void RegisterSection(string id, int sortOrder, Action renderDelegate)
    public void Render()
}
```

**RegisterSection behavior:**
- If `renderDelegate` is null → `throw new ArgumentNullException(nameof(renderDelegate))`.
- If a section with the same `Id` already exists → replace it (last-write-wins).
- Append new section or replace existing; set `_needsSort = true`.

**Render() behavior:**
1. If `_needsSort` → sort `_sections` by `SortOrder` ascending; `_needsSort = false`.
2. `height = Gui.GetFrameHeight() + Gui.GetStyle().WindowPadding.Y * 2f`.
3. Set `Height = height`.
4. Position and size the status bar at the bottom of the main viewport.
5. `Gui.Begin("##GlobalStatusBar", flags)` where flags = `NoDecoration | NoDocking | NoSavedSettings | NoFocusOnAppearing | NoNav | NoMove`.
6. For each section: call `RenderDelegate()`. After each section (except last): `Gui.SameLine(); Gui.SeparatorEx(ImGuiSeparatorFlags.Vertical); Gui.SameLine()`.
7. `Gui.End()`.

**Tests:** All 9 WM-S601 success conditions. Focus on:
- Sort order verification (delegate invocation order).
- Deferred sort (marks dirty once, sorts on first Render).
- Duplicate Id replacement.
- Separator count (N-1 separators for N sections).
- Null delegate throws `ArgumentNullException`.
- `Height` property updated after `Render()`.
For headless tests needing `Gui.GetFrameHeight()`, call it inside a frame (use `ImGuiTestFixture.NewFrame()`).

---

### Task WM-S602: `WindowManager.StatusBar` Property + Integration

See full details: [TASK-DETAIL.md §WM-S602](../../win-mgr-1/TASK-DETAIL.md#wm-s602-windowmanagerstatusbar-property--statusbarmanagerrender-integration)

**File to modify:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

1. Add `private readonly StatusBarManager _statusBar = new();` field.
2. Replace the BATCH-04 stub `GetStatusBarHeight()` with `public StatusBarManager StatusBar => _statusBar;`.
3. Update the dockspace height call in `SubsystemOrchestrator` (if it used `GetStatusBarHeight()`) to use `_windowManager?.StatusBar.Height`. _(Verify whether SubsystemOrchestrator uses the method or direct Height property access.)_
4. In `WindowManager.Render()`, after the `foreach window` loop, add:
   ```csharp
   _statusBar.Render();
   ```

**Tests:** 4 WM-S602 success conditions. Verify `StatusBar` is not null after construction; `StatusBarManager.Render()` is called after all window renders (use call order tracking).

---

### Task WM-S603: Reference Section Registration in `Hrot.ClusterRunner`

See full details: [TASK-DETAIL.md §WM-S603](../../win-mgr-1/TASK-DETAIL.md#wm-s603-reference-section-registration-in-hrotclusterrunner)

**File to modify:** `Hrot.ClusterRunner/Program.cs`

After `orchestrator.Initialize()`, register a demonstration status bar section:
```csharp
var windowManager = orchestrator.WindowManager;
if (windowManager != null)
{
    windowManager.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
    {
        IconWidgets.InlineIcon(atlas, "a1");   // placeholder icon
        Gui.SameLine();
        Gui.Text("System OK");
    });
}
```

Note: `atlas` is the dummy atlas from the orchestrator. For this reference registration, the lambda can use a local alias: `using Gui = ImGuiNET.ImGui;` in Program.cs if needed.

Actually, since this is a demo section and the atlas handle is `IntPtr.Zero` (dummy), the `InlineIcon` call is fine — it just renders a transparent image. The `Text` call works fine.

**Simpler approach** (to avoid adding using directives to Program.cs):
```csharp
windowManager.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
{
    ImGuiNET.ImGui.Text("System OK");
});
```

**Tests:** This is mainly verified at build/runtime level. Add a build compile-check test (the section must exist in the registered sections list — verifiable via the StatusBarManager's public API or through reflection).

---

### Task WM-S701: `TogglePerspectiveEvent` Record

**Already created in BATCH-04** at `Hrot.Common/Events/TogglePerspectiveEvent.cs`.

Verify: `public record TogglePerspectiveEvent(string OldPerspective, string NewPerspective);`

Write unit tests in `Hrot.ClusterRunner.Tests/` if not already present:
1. Value equality test: two instances with same params are equal.
2. Immutability test: no public setters.

---

### Task WM-S702: `ActivePerspective` Singleton ECS Component

See full details: [TASK-DETAIL.md §WM-S702](../../win-mgr-1/TASK-DETAIL.md#wm-s702-activeperspective-singleton-ecs-component)

**File to create:** `Hrot.Common/Components/ActivePerspective.cs`

**Important design decision:** The TASK-DETAIL says `struct`, but `string Name` makes this a managed type (not unmanaged). The FDP ECS `SetSingletonUnmanaged<T>` requires `T : unmanaged`. Therefore:

- **Make it a class** (sealed, `sealed class ActivePerspective`) so it works with `World.SetSingletonManaged<ActivePerspective>()` and `World.GetSingletonManaged<ActivePerspective>()`.
- Document this deviation in the report (managed class singleton instead of unmanaged struct).

```csharp
namespace Hrot.Common;

public sealed class ActivePerspective
{
    public string Name { get; set; } = string.Empty;
}
```

**Tests:** 5 WM-S702 conditions. Test that you can set and get via a test `EntityRepository`:
```csharp
var world = new EntityRepository();
world.SetSingletonManaged(new ActivePerspective { Name = "IG" });
Assert.Equal("IG", world.GetSingletonManaged<ActivePerspective>()!.Name);
```

---

### Task WM-S703: `PerspectiveCoordinatorSystem`

See full details: [TASK-DETAIL.md §WM-S703](../../win-mgr-1/TASK-DETAIL.md#wm-s703-perspectivecoordinatorsystem)

**File to create:** `Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs`

**ECS pattern in this project:** Systems extend `Fdp.Kernel.ComponentSystem` and override `protected override void OnUpdate()`. Systems access `World` (EntityRepository) via the base class property.

**However:** This system bridges the UI event bus (WindowManager.OnPerspectiveChanged) with the ECS world. The bridge currently uses `Console.WriteLine` (BATCH-04 stub). For BATCH-05:

**Approach A (full ECS integration):** `PerspectiveCoordinatorSystem : ComponentSystem`
- Constructor takes `SubsystemOrchestrator _orchestrator` and `IReadOnlyDictionary<string, string> _perspectiveToSubsystemName`.
- In `OnUpdate()`: consume `World.Bus.ConsumeManaged<TogglePerspectiveEvent>()` → call `SwitchMapOwner(subsystemName)` → update `World.SetSingletonManaged(new ActivePerspective { Name = evt.NewPerspective })`.
- This requires a shared `EntityRepository` that both the coordinator and the event publisher can access.

**Approach B (simple coordinator class):** A non-ECS class with an internal queue.
- Constructor: takes `SubsystemOrchestrator orchestrator`, `IReadOnlyDictionary<string, string> perspectiveMap`, and `EntityRepository sharedWorld`.
- Implements `OnUpdate()` as a regular method.
- Queue events from `OnPerspectiveChanged` → dequeue in `OnUpdate()`.

**Use Approach A.** Create a minimal shared `EntityRepository` in the composition root (Program.cs) and pass it to the `PerspectiveCoordinatorSystem`. Register it as a system. Call `system.Update()` once per frame from a hook in the orchestrator or by wrapping it in a subsystem stub.

Actually, the **simplest correct approach** given the codebase complexity is:

**Approach C (recommended):** Implement `PerspectiveCoordinatorSystem` as a standalone class (NOT extending ComponentSystem) with:
- Internal `ConcurrentQueue<TogglePerspectiveEvent>` populated by the `OnPerspectiveChanged` subscription.
- `void ProcessPendingEvents()` method called manually from Program.cs or from a thin wrapper subsystem.
- Calls `orchestrator.SwitchMapOwner(subsystemName)` on processing.

This avoids the need for a shared world at the Program.cs level. The `ActivePerspective` ECS singleton is updated on whatever world the system has access to (can be null/skipped if no shared world exists).

**Concrete implementation:**

```csharp
namespace Hrot.ClusterRunner.Systems;

public sealed class PerspectiveCoordinatorSystem
{
    private readonly SubsystemOrchestrator _orchestrator;
    private readonly IReadOnlyDictionary<string, string> _perspectiveToSubsystemName;
    private readonly ConcurrentQueue<TogglePerspectiveEvent> _queue = new();
    private string _currentPerspective = string.Empty;

    public PerspectiveCoordinatorSystem(
        SubsystemOrchestrator orchestrator,
        IReadOnlyDictionary<string, string> perspectiveToSubsystemName)
    {
        _orchestrator = orchestrator;
        _perspectiveToSubsystemName = perspectiveToSubsystemName;
    }

    // Called from OnPerspectiveChanged handler
    public void Enqueue(TogglePerspectiveEvent evt) => _queue.Enqueue(evt);

    // Called each frame from Program.cs orchestration loop
    public void ProcessPendingEvents()
    {
        while (_queue.TryDequeue(out var evt))
        {
            if (_perspectiveToSubsystemName.TryGetValue(evt.NewPerspective, out var subsystemName))
                _orchestrator.SwitchMapOwner(subsystemName);

            _currentPerspective = evt.NewPerspective;
        }
    }

    public string CurrentPerspective => _currentPerspective;
}
```

**Wiring in Program.cs:**
1. Create the coordinator with the perspective → subsystem name map.
2. Subscribe `OnPerspectiveChanged` to call `coordinator.Enqueue()`.
3. Call `coordinator.ProcessPendingEvents()` once, e.g., by wrapping in a thin `ISubsystem` that delegates to it in `Update()`.

**However**, calling `ProcessPendingEvents()` from `Program.cs` is tricky since the frame loop is inside `orchestrator.Run()`. The cleanest solution:
- Create a thin `PerspectiveSubsystem : ISubsystem` that wraps the coordinator, calls `coordinator.ProcessPendingEvents()` in its `Update(float dt)`, and is registered with the orchestrator as the first subsystem.

**Tests (WM-S703):**
1. Enqueue a `TogglePerspectiveEvent` and call `ProcessPendingEvents()`.
2. Verify `SwitchMapOwner` was called with the correct name (mock orchestrator).
3. `CurrentPerspective` updated after processing.
4. Unknown perspective → no `SwitchMapOwner` call, `CurrentPerspective` still updated.
5. Multiple events processed in order.

---

## 🧪 Test-Driven Task Progression

```
For each task:
    1. READ the task description in TASK-DETAIL.md thoroughly.
    2. WRITE tests first.
    3. RUN tests — confirm FAIL (red).
    4. IMPLEMENT until PASS (green).
    5. Only then move to the next task.
```

**Final verification:**
```
dotnet build FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj
dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj  # 143+ must pass
dotnet build Hrot.ClusterRunner/Hrot.ClusterRunner.csproj
dotnet test Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj  # all must pass
```

---

## 🧱 Critical Implementation Notes

1. **StatusBarManager separator logic:** After calling each section's delegate, check if it's the last — if not, call SameLine + SeparatorEx(Vertical) + SameLine. Use index `i < _sections.Count - 1` check.

2. **WindowManager Render() — status bar last:** `_statusBar.Render()` called AFTER the `foreach window` loop and AFTER EndMainMenuBar, so it renders on top.

3. **SubsystemOrchestrator StatusBar height:** In BATCH-04 a stub `GetStatusBarHeight() => 0f` was added. Replace it by reading `_windowManager?.StatusBar.Height ?? 0f`. Check if the stub method or property was used in the orchestrator dockspace size calculation — update accordingly.

4. **Program.cs wiring order:** 
   ```
   orchestrator.Initialize()        // WindowManager is now created
   windowManager = orchestrator.WindowManager
   coordinator = new PerspectiveCoordinatorSystem(orchestrator, perspectiveMap)
   windowManager.OnPerspectiveChanged += (old, new) => {
       coordinator.Enqueue(new TogglePerspectiveEvent(old, new));
   };
   windowManager.StatusBar.RegisterSection(...)
   orchestrator.Run()               // frame loop; processes coordinator via thin subsystem
   ```

5. **Thin PerspectiveSubsystem:** The cleanest way to call `coordinator.ProcessPendingEvents()` every frame is to create an `ISubsystem` wrapper added to the subsystem list BEFORE other subsystems:
   ```csharp
   class PerspectiveUpdateSubsystem : ISubsystem
   {
       private readonly PerspectiveCoordinatorSystem _coordinator;
       // ... implement ISubsystem, call _coordinator.ProcessPendingEvents() in Update()
       // DrawWorld(), DrawUI(), Shutdown() are no-ops; TitleBarColor = Vector4.Zero; Headless = always OK
   }
   ```
   Add it at the beginning of `subsystems` list.

6. **FdpEventBus is NOT needed** for the perspective coordinator in this design — the coordinator uses the C# event directly (`OnPerspectiveChanged`) rather than the ECS bus. This is simpler and correct for a UI-level event. The `TogglePerspectiveEvent` record is still useful for testing and for future ECS consumers.

7. **StatusBarManager deferred sort**: use a `bool _needsSort` flag that is set on every `RegisterSection` call. On `Render()`, sort once when dirty, then clear the flag. Use `_sections.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder))` or `LINQ OrderBy`. Stable sort preferred to preserve insertion order for equal sort orders.

8. **`ActivePerspective` as managed singleton:** `EntityRepository` requires `T : unmanaged` for `SetSingletonUnmanaged`. Since `ActivePerspective.Name` is a `string`, use `SetSingletonManaged<ActivePerspective>(value)` instead.

# Window Manager & Icon System — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success conditions.  
**Design:** See [DESIGN.md](./DESIGN.md) for full architecture and rationale.

---

## Phase 1 — Icon System Foundation

**Goal:** Deliver a complete, tested icon atlas and widget library in `FDP.Toolkit.ImGui.Icons`.

- [x] **WM-S101** `IconAtlas` — resource loading, UV parsing, disposal [details](./TASK-DETAIL.md#wm-s101-iconatlas--resource-loading-uv-parsing-disposal)
- [x] **WM-S102** `IconWidgets` — `InlineIcon` and `AbsoluteIcon` [details](./TASK-DETAIL.md#wm-s102-iconwidgets--inlineicon-and-absoluteicon)
- [x] **WM-S103** `IconWidgets` — `IconButton` and `ToggleIcon` [details](./TASK-DETAIL.md#wm-s103-iconwidgets--iconbutton-and-toggleicon)
- [x] **WM-S104** `IconWidgets` — `AlternatingFaceToggleIcon` [details](./TASK-DETAIL.md#wm-s104-iconwidgets--alternatingfacetoggleicon)
- [x] **WM-S105** `IconWidgets` — `DropdownFaceIcon` [details](./TASK-DETAIL.md#wm-s105-iconwidgets--dropdownfaceicon)

---

## Phase 2 — Window Manager: ManagedWindow Base

**Goal:** Define the `ManagedWindow` lifecycle, visibility contract, and title bar controls.

- [x] **WM-S201** `WindowScope` enum + `ManagedWindow` abstract base (visibility rules, render lifecycle) [details](./TASK-DETAIL.md#wm-s201-windowscope--managedwindow-abstract-base)
- [x] **WM-S202** `ManagedWindow` custom title bar (pin icon + close icon + unpin tooltip) [details](./TASK-DETAIL.md#wm-s202-managedwindow-custom-title-bar-controls)
- [x] **WM-S203** `ManagedWindow` optional local menu bar [details](./TASK-DETAIL.md#wm-s203-managedwindow-optional-local-menu-bar)

---

## Phase 3 — Window Manager: Menu Registry & Orchestrator

**Goal:** Complete `GlobalMenuRegistry` and all `WindowManager` rendering and API.

- [x] **WM-S301** `GlobalMenuRegistry` — trie data structure + registration API [details](./TASK-DETAIL.md#wm-s301-globalmenuregistry--trie-data-structure--registration-api)
- [x] **WM-S302** `WindowManager` — dictionary window registry + programmatic API [details](./TASK-DETAIL.md#wm-s302-windowmanager--registry--programmatic-api)
- [x] **WM-S303** `WindowManager.Render()` — global menu tree + Windows pulldown + auto-pin [details](./TASK-DETAIL.md#wm-s303-windowmanagerrender--global-menu--windows-pulldown--auto-pin)
- [x] **WM-S304** `WindowManager.Render()` — perspective switcher + `SwitchPerspective` + `OnPerspectiveChanged` [details](./TASK-DETAIL.md#wm-s304-perspective-switcher--switchperspective--onperspectivechanged)
- [x] **WM-S305** `WindowManager.Render()` — Help / Debug menu [details](./TASK-DETAIL.md#wm-s305-windowmanagerrender--help--debug-menu)

---

## Phase 4 — Persistence & Docking

**Goal:** Ensure layout and pin state survive application restarts; enable ImGui docking.

- [x] **WM-S401** ImGui custom settings handler — persist `IsOpen` / `IsPinned` in `imgui.ini` [details](./TASK-DETAIL.md#wm-s401-imgui-custom-settings-handler-for-isopen--ispinned-persistence)
- [x] **WM-S402** ImGui docking integration — `DockingEnable` + fullscreen `DockSpace` [details](./TASK-DETAIL.md#wm-s402-imgui-docking-integration)

---

## Phase 5 — Framework Integration

**Goal:** Wire the Window Manager and Status Bar into `SubsystemOrchestrator` and the `Hrot.ClusterRunner` composition root.

- [x] **WM-S501** `SubsystemOrchestrator` integration — expose `WindowManager` to subsystems via `SubsystemConfig` [details](./TASK-DETAIL.md#wm-s501-subsystemorc-integration--expose-windowmanager-to-subsystems)
- [x] **WM-S502** Composition root — `OnPerspectiveChanged` → publish `TogglePerspectiveEvent` [details](./TASK-DETAIL.md#wm-s502-composition-root--onperspectivechanged--publish-toggleperspectiveevent)
- [x] **WM-S503** `SubsystemOrchestrator` dockspace height — reserve status bar space [details](./TASK-DETAIL.md#wm-s503-subsystemorc-dockspace-height--reserve-status-bar-space)

---

## Phase 6 — Status Bar

**Goal:** Implement the subsystem-injectable persistent status bar using a delegate registry.

- [x] **WM-S601** `StatusBarManager` class — delegate registry + sorted render loop [details](./TASK-DETAIL.md#wm-s601-statusbarmanager--delegate-registry--sorted-render-loop)
- [x] **WM-S602** `WindowManager.StatusBar` property + `StatusBarManager.Render()` called from `WindowManager.Render()` [details](./TASK-DETAIL.md#wm-s602-windowmanagerstatusbar-property--statusbarmanagerrender-integration)
- [x] **WM-S603** Reference section registration in `Hrot.ClusterRunner` [details](./TASK-DETAIL.md#wm-s603-reference-section-registration-in-hrotclusterrunner)

---

## Phase 7 — Background Map Perspective Manager

**Goal:** Build the ECS-side coordination layer so world-space rendering perspective stays in sync with the window perspective.

- [x] **WM-S701** `TogglePerspectiveEvent` record [details](./TASK-DETAIL.md#wm-s701-toggleperspectiveevent-record)
- [x] **WM-S702** `ActivePerspective` singleton ECS component [details](./TASK-DETAIL.md#wm-s702-activeperspective-singleton-ecs-component)
- [x] **WM-S703** `PerspectiveCoordinatorSystem` — subscribes to event, calls `SwitchMapOwner`, writes `ActivePerspective` [details](./TASK-DETAIL.md#wm-s703-perspectivecoordinatorsystem)

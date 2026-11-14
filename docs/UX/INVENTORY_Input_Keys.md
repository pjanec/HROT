<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: §0 queries + headline · §A key readers · §B Esc · §C map keys + MenuShortcuts · §D commands (K5) · §E focus bugs · §F what already exists
stale-below: nothing
known-rot: line numbers are of `ui` HEAD 66bf0989e (2026-11-07); the NodeEditor.Demo project is listed in the counts but never in the tables
related-designs:
  - DESIGN_Input_And_Commands.md — the design this measures (K1–K6); its §1 numbers (43 files, 12 mechanisms) are re-measured here
  - DESIGN_Action_Catalogue.md — owns ActionDecl / ActionCatalogue; ⚠ none of it is built (§F)
  - ../designs/gizmos-1/gizmo-input-focus-design.md — the dumb terminal; §C shows which keys do NOT go through it
-->

# INVENTORY — every key site, `ui` @ `66bf0989e` *(read-only measurement for B-10; no code touched)*

## 0. Queries run, counts, and what they cannot say

⚠ **Coverage could not be checked** — the codebase-memory CLI has no `check_index_coverage`; every total below is "what these patterns matched", never proof of completeness. A key read through a wrapper whose name I did not pattern (e.g. a method that forwards to `IsKeyDown`) shows only at the wrapper. Scope: production `.cs` under `Hrot/`, `FDP/Engine`, `FDP/Toolkits`, `FDP/ExtDeps/NodeEdit/src` (tests, `obj`, `bin`, `Tests/` excluded). `rg` over those dirs:

| pattern | lines | files |
|---|---:|---:|
| `ImGui*.IsKeyPressed(` | 83 | 23 |
| `ImGui*.IsKeyDown(` | 4 | 3 |
| `ImGui*.IsKeyReleased(` | 1 | 1 |
| any `IsKeyPressed(` (incl. `IInputSource` / `IInputProvider` callers and their declarations) | 130 | 39 |
| any `IsKeyDown(` | 46 | 13 |
| any `IsKeyReleased(` | 7 | 5 |
| `Raylib.IsKey*` / `GetKeyPressed` / `GetCharPressed` | 30 | 7 |
| modifiers (`.KeyCtrl/.KeyShift/.KeyAlt/.KeySuper`, `ImGuiKey.Mod*`) | 25 | 10 |
| `ImGui.Shortcut(` / `SetNextItemShortcut` / `IsKeyChordPressed` | **0** | 0 |
| `WantCaptureKeyboard` / `WantTextInput` | 9 | 8 |
| `KeyboardKey.` mentions (Raylib enum, incl. consts) | 165 | 28 |
| Esc (`Key.Escape` / `"Escape"` next to a key read) | 44 | 36 |
| **union of all reader lines** | **207** (191 without `NodeEditor.Demo`) | **45** (42) |
| *(outside the four dirs)* `FDP/ExtDeps/GizmoMap` (no `.Example`) key reads | 12 | — |

Graph (`scripts/cbm.sh search_graph`, `label` Class / Interface, index project `home-user-HROT`, 11 projects):

| query | total | note |
|---|---:|---|
| `.*(Keymap\|KeyBinding\|Shortcut\|Hotkey\|KeyRouter\|KeyChord\|InputScope).*` Class | 6 | `EditorHotkeyDispatcher` · NodeEdit `HotkeyDispatcher` (Demo) · `KeyBinding` · `MapShortcutKeyEvent` · `MenuShortcuts` · `MenuShortcutKeyLayer` — same 6 as the design's §1 |
| same, Interface | 0 | |
| `.*(ActionDecl\|ActionCatalog\|ActionRegistry\|MenuRegistry\|HostAction\|MenuBinding\|IShellCommand\|IEditorCommands\|CommandCatalog\|CommandRegistration).*` Class | 26 | 14 real (others tests / HSM-BTree `ActionRegistry`s) — §F |
| same, Interface | 4 | `IEditorCommands`, `IShellCommandRegistrar`, `IBehaviorActionCatalog`, `IChannelCommandCatalog` |
| `.*(…\|Command\|ActionDecl\|ActionCatalog).*` Class / Interface (the handoff's wide pattern) | 244 / 14 | **noise**: ECS commands, command buffers, graph edit commands — only the narrower ones above are input-relevant. Class page truncated at 83 (`has_more`) — not paginated; the narrow queries replace it |

**Headline:** 191 key-reader lines in 42 production files · 5 key enums · 0 uses of ImGui's `Shortcut()` routing · **41 Esc readers, no central Esc** · 13 gizmo/tool Esc handlers · 10 `RegisterItem` calls (design said 9) · 4 `RegisterContributor` · 0 callers of `RegisterShortcut` · **no `ActionDecl` / `Keymap` / `KeyRouter` / `ActionCatalogue` type exists**.

---

## A. Key readers (K1, K4)

Gate column = what the code does TODAY. `focus` = `IsWindowFocused(…)`; `text` = `WantTextInput` / item-active guard; `modal` = inside a popup/modal scope; `mouse` = `!WantCaptureMouse` / `IsMouseCaptured`; `none` = nothing seen within ±14 lines (heuristic — verify before relying on it).

### A1. ImGui panels, windows, dialogs (Fdp.Presentation, Hrot.*)

| file:line | key | does | window / scope | gate today |
|---|---|---|---|---|
| `Fdp.Presentation/ImGui/Panels/Timeline/TimelinePanel.cs:795-838` | Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z · K J L · Space · F · 0-9 · Backspace · + − · I O · Esc · ← → · Home End | track undo/redo; key-frame offers; play/pause; fit; zoom presets; back; zoom; range in/out; **Esc clears the range**; step; seek | Timeline panel | **focus** (`:214` → `:257 if (focused) Keys(...)`) — the one panel that is properly gated; ⚠ Space still also reaches the map (§E) |
| `…/TimelinePanel.cs:386,393,460,475,784,821` | Shift / Ctrl + wheel / drag | zoom, pan, range drag | Timeline | hover (`PanAnywhere` `IsWindowHovered`) |
| `Fdp.Presentation/ImGui/Panels/MessageLogPanel.cs:620` | Ctrl+C | copy selected rows | Message log | **focus** (Root+Child) |
| `…/MessageLogPanel.cs:638-639`, `EventBrowserPanel.cs:417-418`, `EntityInspectorPanel.cs:488-489`, `NodeEditor.UI/Picker/PickerItemListHelper.cs:259-260` | Ctrl / Shift | click multi-select modifiers | each panel | n/a (modifier on a click) |
| `Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs:225,248` | Enter / Esc | OK / Cancel the raw component editor | component editor window | **none** (reads while the window draws, focused or not) |
| `…/Utils/ComponentReflector.cs:407,425` | Enter / Esc | same, inline editor | inspector | **none** |
| `…/WindowManager/WindowManager.cs:744,759` | Esc | close the current popup | settings / hosts popups | modal |
| `Hrot.Presentation/Orders/OrderForm.cs:114` | Esc | close the order form | Order form | **focus** (Root+Child) |
| `Hrot.Presentation/Map3D/Director/DirectorAdapters.cs:93` | Esc | `EscapePressed` (stop a director op) | Director | **none** |
| `Hrot.Editor/Scenario/ParametersDrawer.cs:102-105` | Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | undo/redo the params source | Params panel | **focus** + **text** |
| `Hrot.Editor.AiShared/Shell/ModalKeys.cs:13` | Esc | `EscapePressed()` helper | any modal that calls it | **focus** (Root+Child) |
| `…/Browser/AssetPickerModal.cs:199-223` | Esc · Enter · Tab · Ctrl/Shift | cancel · pick · cycle · multi-select | asset picker modal | modal |
| `…/Browser/EntityRenameModal.cs:96` | Esc | cancel rename | modal | modal |
| `…/Blackboard/VariablesPanelControl.cs:522-526` | Enter / Esc | commit / cancel an inline rename | Variables panel | **none** (item-level) |
| `…/Windows/AiGraphCanvasWindow.cs:792` | Alt+← | back to the previous document | each graph canvas window | **focus** (ChildWindows) + **text** — the only canvas key that is focus-gated |
| `…/Windows/EditorHotkeyDispatcher.cs:53` | every `IEditorCommands` `DefaultKey` chord (exact-modifier match) | `commands.Invoke(id)` | per graph canvas window (`AiGraphCanvasWindow.cs:307,472`) and once for the shell (`EditorSubsystem.cs:3879,2813`) | **text** only (`!WantTextInput`) — **every drawn canvas pumps its own commands** |
| `…/Adapters/ImGuiInputSource.cs:75,154,164,174` | all (adapter) | `IInputSource` over ImGui (`IsKeyDown/Pressed/Released`, modifiers) | NodeEdit hosts | none (a thin wrapper; try/catch around ImGui) |

### A2. NodeEdit canvas, pickers, dialogs (`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI`)

| file:line | key | does | scope | gate today |
|---|---|---|---|---|
| `Canvas/CanvasInput.cs:89` | Delete / Backspace | delete selection | graph canvas (Idle) | `canProcess` (the canvas's own "hovered/active" flag) — **not** ImGui focus |
| `Canvas/CanvasInput.cs:105` | Tab / Space | open the add-node picker | graph canvas | `canProcess && isCanvasDirectlyFocused && hover none && !ReadOnly` |
| `Canvas/CanvasInput.cs:1354` | Esc / RMB | cancel a sticky preview wire | graph canvas | `pw.Sticky` |
| `Canvas/CanvasRenderer.cs:1323,1367` | Enter / Esc | confirm / cancel *Promote to variable* | its popup | modal |
| `Canvas/CommentsRenderer.cs:154` | Esc | cancel comment edit | canvas | none |
| `Bookmarks/BookmarksPanel.cs:145` | Esc | cancel bookmark rename | Bookmarks | none |
| `Find/FindBar.cs:110,127-129` | Esc · F3 / Shift+F3 | close the bar · next / previous match | Find bar | Esc: **focus** (Root+Child); **F3: none** |
| `MiniEditors/StringPinEditor.cs:37`, `DragFloatWithExpression.cs:55,125`, `ColorPinEditor.cs:56` | Esc | cancel an inline pin editor | node pin | text / item-active |
| `Panels/MyBlueprintPanel.cs:106,127` | `/` · Esc | focus the search · cancel | My Blueprint | `/`: `!IsAnyItemActive` (no window gate); Esc: item-active |
| `Picker/Layouts/GridLayout.cs:230` | F | favourite the hovered tile | picker grid | hover + `!WantTextInput` |
| `Picker/PickerWindow.cs:444-647` (24 reads) | Enter · Esc · ↑ ↓ ← → · PgUp/PgDn · Home/End · Space | navigate / confirm / cancel the picker | picker modal | window focus (heuristic) |
| `Dialogs/SaveAsBrowserDialog.cs:219-656` (10 reads) | Enter · Esc · arrows | Save-As navigation, confirm, cancel | Save-As modal | modal + `_nameInputActive` text guards |
| `Core/Interfaces/IInputSource.cs:26-28` | — | the abstraction (`IsKeyDown/Pressed/Released(EditorKey)`) | NodeEdit | — |

### A3. The map through `IInputProvider` (backend-side readers, Fdp.Presentation Vis2D/Vis3D + Hrot.Presentation Map3D)

| file:line | key | does | scope | gate today |
|---|---|---|---|---|
| `Vis3D/MapCamera3D.cs:312-320` | W A S D E Q (hold) + Shift / Ctrl | free-fly the 3-D camera | 3-D map, **RMB-look** | `:268` returns while `IsMouseCaptured` (mouse over ImGui); **no ImGui-key / focus check** |
| `Map3D/FollowCameraDriver.cs:114-116` | Esc · W A S D · MMB | release the follow camera | a camera driver is set | none |
| `Map3D/PossessionStation.cs:219-2041` (30 reads) | Esc (get out / release) · W A S D (hold) · Shift sprint · Ctrl · Space · C crouch · Z prone · V hull-lock · Q E lean · F use · R next round · L fire mode · T command wheel · J team switch · M map peek · G seat · I select · B binoculars · N message log · O shoulder · Tab scoreboard · 1-9 verb/slot · X cancel | the **FPS station** (`HandleInput(MapCamera3D, IInputProvider)`, called by `MapCamera3D:263` while a driver is set) | possessed unit (Embodied hat) | the driver being set; **no `WantCaptureKeyboard`**; the cursor lock turns ImGui's mouse off (`NoMouse`) |
| `Map3D/FallScreen.cs:72-74` | Esc · J · K | end the fall · next man · kill-cam | fall screen | none |
| `Map3D/MapViewModeLayer.cs:127,143` | Ctrl+M | toggle 2-D / 3-D (`IMapLayer.HandleKeyInput`) | map | not while an `FpsCameraDriver` drives |
| `ScenarioEditor/Map/MenuShortcuts.cs:135-148` | **every key typed on the map** | publishes `MapShortcutKeyEvent` (§C) | map | not while a camera driver is set; `Modified` = Ctrl/Alt via raw Raylib |
| `Vis2D/Defaults/RaylibInputProvider.cs:29-35` | — | the `IInputProvider` over Raylib (`IsKeyboardCaptured => WantCaptureKeyboard`; `IsMouseCaptured => !CursorLocked && WantCaptureMouse`) | — | — |
| `Vis2D/Abstractions/IInputProvider.cs:15-17` | — | the abstraction (`MapKeyboardKey`) | — | — |

### A4. Raw Raylib, no abstraction

| file:line | key | does | scope | gate today |
|---|---|---|---|---|
| `Hrot.Player/PlayerApplication.cs:1279-1286` | ← → ↑ ↓ (hold) | pan the 2-D map | Player map | `_isActiveMapOwner() && !WantCaptureMouse` (`:970`) — a **mouse** gate |
| `…/PlayerApplication.cs:1328-1334` | = + / − | zoom | Player map | same |
| `…/PlayerApplication.cs:1346-1369` | Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | `_gizmoUndoStack.Undo()` (a key calling a method) | Player | `WantCaptureKeyboard` |
| `Hrot.Player/UI/PerformanceOverlay.cs:60` | F3 | toggle the overlay | Player | **none** |
| `Hrot.Presentation/Audio/AudioLayer.cs:75,235-250` | F6 F7 F8 F9 F10 · ↑ ↓ ← → (panel open) | master −/+, mute, test tone, panel | Player audio | **none** (default `_keyPressed = Raylib.IsKeyPressed`) |
| `Hrot.Presentation/Map3D/MapViewModeLayer.cs:143`, `ScenarioEditor/Map/MenuShortcuts.cs:147-148` | Ctrl / Alt held | "modified" flags | — | none |

---

## B. Esc readers (K3 — the central Esc)

41 readers + 3 non-reader mentions (`ImGuiInputSource.cs:230` mapping, `EditCommands.cs:55` binding, `MenuShortcuts.cs:105` text→key). **No central Esc exists**: no `ToolController` / gizmo manager router for it; `Raylib.SetExitKey(Null)` (`PlayerApplication.cs:547`) only stops Raylib quitting.

| kind | sites (file:line) | what Esc does |
|---|---|---|
| **gizmos / tools (backend, `OnKeyEvent`)** — **13** | `ReplayBrowser/BoundingBoxPickerGizmo.cs:129` · `Vis2D/Gizmos/PointSequenceGizmo.cs:163` · `EntityPickerGizmo.cs:177` · `AreaShapeGizmo.cs:139` (also Enter `:138` = finish) · `FdpLocationPickerGizmo.cs:135` · `ScenarioEditor/Gizmos/RouteWaypointGizmo.cs:207` · `EntityRotatorGizmo.cs:114` · `EntityPlacementGizmo.cs:223` · `VertexEditGizmo.cs:209` · `MeasureGizmo.cs:201` · `Map/RtsCommands.cs:197` (**removes the RTS command**) · `Hrot.Editor/Gizmos/ObstaclePlacementGizmo.cs:111` · `ModalBoxSelectionGizmo.cs:101` | cancel the tool (the design counted 12) |
| FPS / camera drivers | `PossessionStation.cs:1758` (Get out / release) · `FallScreen.cs:72` · `FollowCameraDriver.cs:114` | step out / end |
| modals & dialogs (ImGui) | `AssetPickerModal.cs:199` · `EntityRenameModal.cs:96` · `ModalKeys.cs:13` · `OrderForm.cs:114` · `WindowManager.cs:744,759` · `ComponentEditWindow.cs:248` · `ComponentReflector.cs:425` · `CanvasRenderer.cs:1367` · `PickerWindow.cs:487` · `SaveAsBrowserDialog.cs:488,581,656` | close / cancel |
| inline editors | `StringPinEditor.cs:37` · `DragFloatWithExpression.cs:55,125` · `ColorPinEditor.cs:56` · `CommentsRenderer.cs:154` · `BookmarksPanel.cs:145` · `MyBlueprintPanel.cs:127` · `VariablesPanelControl.cs:526` · `FindBar.cs:110` | cancel the edit / close the bar |
| panels | `TimelinePanel.cs:814` (clear range) · `DirectorAdapters.cs:93` | local meaning |
| graph canvas | `CanvasInput.cs:1354` (cancel sticky wire) · `IEditorCommands` *Deselect All* default key Esc (`EditCommands.cs:55`, fires via the dispatcher in **every** drawn canvas) | cancel / select-none |
| the map's unit menu | `MenuShortcuts.KeyOf("Esc")` — a menu item may bind Esc | runs that item |

---

## C. `MenuShortcuts`, map menu letters, and how map keys reach the backend (§2.4)

**The path today** (`MapCanvas.ProcessInputPipeline`, `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs`):

| step | site | what |
|---|---|---|
| 1 | `:206` `if (_input.IsMouseCaptured) return;` | the WHOLE pipeline (keys too) is skipped while the mouse is over an ImGui window — this is the "mouse-over" gate CE-4282 names |
| 2 | `:232-244` `if (!_input.IsKeyboardCaptured)` drain `GetKeyPressed()` | `IsKeyboardCaptured = ImGui WantCaptureKeyboard` = **true only while a text field is active** (nav keyboard never enabled) ⇒ a focused non-text panel does not stop it |
| 3 | `_layers[i].HandleKeyInput(key)` top-down (`IMapLayer`, default `false`) | 4 implementers: `MenuShortcutKeyLayer` (→ `MapShortcutKeyEvent`, returns false) · `MapViewModeLayer` (Ctrl+M) · `DebugGizmoLayer` (`:343` queues the key for the terminal, returns false) · PlayerApplication comment `:547` |
| 4 | `DebugGizmoLayer` terminal → `RawInput` → `GizmoKeyEvent` on the interaction bus (`:622`; remote: `GizmoInteractionIngressTranslator.cs:214`) | the backend resolution of §2.4 **exists for gizmos** |
| 5 | consumers: `IGizmoInteractionHandler.OnKeyEvent` (22 files implement it; 13 read Esc, `AreaShapeGizmo` Enter, `RtsCommands` Shift/Ctrl/Esc) · `SelectionInteractionSystem.cs:210` (**Delete** → destroy selection) | |
| 6 | `MenuShortcuts : IEcsModuleSystem` (`Execute` reads `MapShortcutKeyEvent` each frame; not `k.Modified`, not `Blocked()` = an armed modal tool other than RTS command) | resolves the PRIMARY selected unit's menu (`ContextMenuProjectorGizmo.MenuJsonFor` JSON → `ActionFor(items, key)` → `GizmoMenuActionEvent`) — **per key press: a JSON serialize+deserialize** |

**The letters** are text in the menu DTO (`ContextMenuItemDto.Shortcut`): fixed — `P` Possess (`ContextMenuProjectorGizmo.cs:112`), `C` Center View, `Space` Select, `R` Rotate, `E` Edit Shape / Edit Route, `L` Load zone — and **derived** `o.Name.Substring(0,1)` for every order (`:148`, `OrderItem`) ⇒ silent first-letter collisions (CE-4281). `KeyOf` accepts a letter/digit, `Esc`, `Del`, a `MapKeyboardKey` name or a Raylib `KeyboardKey` name.

**Held keys and drivers do NOT use this path** — they read `IInputProvider.IsKeyDown/Pressed` directly every frame (§A3: free-fly, follow camera, the 30-read FPS station, fall screen), locally, not through the terminal. `MenuShortcutKeyLayer` stands down only while `MapCamera3D.Driver != null` (`MenuShortcuts.cs:137`) — **not** while the free-fly camera moves (CE-4281).

---

## D. Commands (K5)

### D1. Four registries, three id spaces

| registry | ids | production registrations | invoked by |
|---|---|---|---|
| `GlobalActionRegistry` (`Hrot.Common/Interactions`, via `HostActions.Actions`) | **56 int** (`GlobalActionIds`) | `Register`/`RegisterLocal` ×28 (map pack `MapInteractionPack.cs:341-391` ×5, `MenuOrders.cs:70-102` ×10 incl. `OrderWithOptionsBase + i`, `EditorSubsystem.cs:2499-2522` ×7, `AiTraceActions.cs:84-85` ×2, `SharedHostActions.cs:20,26`, `EntityDetailsBundle.cs:131`, `EditorSubsystem.cs:5113`, `SimHostApp.cs:463`) | `GlobalActionDispatchSystem` ← `GlobalActionRequestedEvent` (`HostActions.Request`, `ContextActionIngressSystem.cs:65,81`) and the map menu click → `GizmoMenuActionEvent` |
| `GlobalMenuRegistry.RegisterItem(path, closure, perspective, allowedInIdle)` (`Fdp.Presentation`) | path strings | **10 calls, all closures** — table D2 | the main menu bar click |
| `GlobalMenuRegistry.RegisterContributor(id, itemsFn, onAction(int), perspective)` | DTO ids | 4: `WindowManager.cs:117-119` (`wm/perspective`, `wm/windows`, `wm/help`) · `MapInteractionPack.cs:446` (publishes `GizmoMenuActionEvent{AnchorId=0}`) | menu bar → handler(int) |
| `IEditorCommands` (`NodeEditor.Core.Action`) | **string** (`CommandCatalog` 66 consts `editor.*`; Hrot adds `shell.*`, `blueprint.*`, `debug.*`) | `EditorCommandsImpl` per document (NodeEdit `CanvasCommands` 5, `EditCommands` 5, `ViewCommands` 6, `ErrorNavigationCommands` 2, `AlignCommands` 4, `BookmarkCommands` ×2×N slots, `BlueprintDocumentFactory` ≈27 `.Add(` in 20 `CommandRegistration` blocks, `AiDebugCommands` 6) and `ShellEditorCommands` (shell: `ShellSaveCommands` 4, `CgfEditorShellToolbar` 5, `ScenarioMenuCommands` 1) | `EditorHotkeyDispatcher` (keys), `MenuCommandAdapter.Register` (menu: `ScenarioMenuCommands.cs:371`, `CgfEditorShellToolbar.cs:314`, `EditorSubsystem.cs:4931,4935`), toolbars |
| `IShellCommandRegistrar.RegisterShortcut(string, Action)` | — | interface + `CaptureShellCommandRegistrar.cs:37` impl; **0 production callers** (the design's "dead seam" confirmed) | — |

Key-bound `IEditorCommands` today (`DefaultKey`, 39 `defaultKey:` lines incl. 2 in Demo): Ctrl+Z/Y, Delete, Ctrl+A, **Esc** (deselect), Ctrl+F / Ctrl+Shift+F, **F3 / Shift+F3**, C (comment), **F8 / Shift+F8**, Ctrl+= / Ctrl+−, F / Shift+F, Ctrl+0-9 / Ctrl+Shift+0-9 (bookmarks), Ctrl+S / Ctrl+Shift+S, Ctrl+O, Ctrl+N, **F9** (breakpoint), Ctrl+C/X/V/D (Blueprint clipboard) + 6 debug commands (`AiDebugCommands`). Display ↔ binding share one source only here: `MenuCommandAdapter.cs:81 node.Shortcut = DefaultKey.ToString()`.

### D2. Every `RegisterItem` closure

| file:line | menu path | closure | who else can reach it |
|---|---|---|---|
| `WindowManager.cs:112` | Settings / UI Scale & Fonts… | `_openSettingsModal = true` | nothing |
| `EditorSubsystem.cs:3626` | Settings / Reset all debug settings… | resets debug flags | nothing |
| `EditorSubsystem.cs:4238` | File / New / Macro… | `newAssetLauncher.OpenMacro()` | nothing |
| `EditorSubsystem.cs:5112` | Scenario / Save selection as macro | `_saveSelectionWindow?.Start()` (+ the `RegisterLocal` action at `:5113`) | the action id |
| `ReplayBrowserSubsystem.cs:938,945,946` | Recording info · Load .fdp · JSON export | `recordingInfo.Show()` · `timelinePanel.RequestLoadFdp()` · `jsonExport.Show()` | nothing |
| `LocalWindowController.cs:202,468` | File / Exit · Settings / Layout / Save current as default | `RequestExit` · layout save | nothing |
| `MenuCommandAdapter.cs:55` | (generic) | wraps an `IEditorCommands` id: `if (IsEnabled()) commands.Invoke(id)` | the command id (keys, toolbars) |

⚠ Callers of the closures are only the menu bar — no key, no HTTP, no other surface. The `MenuCommandAdapter` path is the one place a menu item and a key share an id.

---

## E. The focus bugs

| row | what it says | where the code is |
|---|---|---|
| **CE-4282** (`Blueprint_Issues_Tracker.md:4727`) a focused panel does not take keys from the map; some keys have no gate | map gets keys whenever the mouse is over it; Hrot never enables `NavEnableKeyboard`; graph-canvas hotkeys run on every drawn canvas (`AiGraphCanvasWindow.cs:448-472`) ⇒ Esc/Delete/Ctrl+Z fire in several; audio/perf F-keys read raw (`AudioLayer.cs:235-250`, `PerformanceOverlay.cs:60`) ⇒ F3/F8/F9 collide with find-next / next-error / breakpoint | **corrected**: the gate is "mouse NOT over an ImGui window" (`MapCanvas.cs:206`, not "over the map") plus `WantCaptureKeyboard` (a text field is active, `:232`). Canvas pump: `AiGraphCanvasWindow.cs:445-472` (`suppressHotkeys = WantTextInput` only); shell pump `EditorSubsystem.cs:2808-2814` (same). Collisions confirmed: F3 (FindBar `:127` + `CanvasCommands` vs `PerformanceOverlay.cs:60`), F8 (`ErrorNavigationCommands` vs `AudioLayer`), F9 (Blueprint breakpoint vs audio test tone) |
| **CE-4281** (`:4726`) free-flying with A/S/E also fires Attack/Strike/Edit | `MenuShortcutKeyLayer` stands down only for a camera DRIVER, not the moving free-fly camera; order letters auto-derived | `MenuShortcuts.cs:137` (driver check) vs `MapCamera3D.cs:312-320` (free-fly keys); letters `ContextMenuProjectorGizmo.cs:148` |
| other collisions found | **Space** = unit Select (menu) · Timeline play · picker toggle · canvas add-node picker · `Hrot.Player` n/a; **E** = Edit Shape/Route · free-fly up · FPS lean right; **F** = Timeline fit · canvas View-Fit · picker favourite · FPS use; **Ctrl+Z** = Timeline · Player gizmo undo · every graph canvas · params panel; **M** = Move (menu) · FPS map-peek, **Ctrl+M** = 2D/3D | §A tables |

---

## F. What already exists that the design's types could BE

| design type | existing candidate | what it is today | fit |
|---|---|---|---|
| `ActionDecl` (id, label, default key, scope, kind) | **none** — the name appears only in a `FormDecl` doc comment; `DESIGN_Action_Catalogue.md` stage A1 (`ActionDecl`, `ActionCatalogue`, `EditingMode`, `MenuComposer`) is **not built** (state DESIGN) | — | new |
| action id | `GlobalActionIds` (56 `int`) ⊕ `CommandCatalog` + `shell.*`/`blueprint.*`/`debug.*` strings (≥66) | two id spaces; no overlap table | needs a unified id (the design's choice) |
| `KeyBinding` (chord, scope, action id, kind) | `NodeEditor.Core.Action.KeyBinding(EditorKey, KeyModifiers)` record struct (`IEditorCommands.cs:46`) | a chord only; modifiers matched exactly by `EditorHotkeyDispatcher` | chord part reusable; scope/kind/action id missing; `EditorKey` is NodeEdit's own enum |
| `Keymap` (bindings + overrides) | `EditorCommandsImpl.All` (descriptors with `DefaultKey`) | per-document registry; no overrides, no validation | the declaration half exists |
| `KeyRouter` / scope chain | `EditorHotkeyDispatcher` (per canvas window + once for the shell) · `MenuShortcuts` (map) · each gizmo's `OnKeyEvent` | three unrelated pumps; text-field gate only | none routes by focus/hat |
| `ActionCatalogue.Query(context)` enabled? | `EditorCommandDescriptor.IsEnabled` (`Func<bool>`) · `ContextMenuItemDto.Enabled` (JSON) · `MenuBinding` | enablement exists per surface, not catalogued | partial |
| `HostActions.Dispatch(id)` | `HostActions.Request` + `GlobalActionDispatchSystem` | exists, the design says "existing" ✓ | reuse |
| menu ↔ key display from one source | `MenuCommandAdapter.cs:81` (`DefaultKey.ToString()`) | only for `IEditorCommands`; the map menu text is `Shortcut = "M"` strings | extend |
| the map terminal | `DebugGizmoLayer` + `GizmoKeyEvent` + `InputCaptureBinding` (+ `GizmoInteractionIngressTranslator` for remote) | exists for **gizmos**; FPS/camera readers bypass it (§C) | reuse for map-scope keys |
| input abstraction | `IInputSource` (NodeEdit, `EditorKey`) and `IInputProvider` (Vis2D, `MapKeyboardKey`) | two abstractions; adapter `ImGuiInputSource` maps EditorKey→ImGuiKey (`:194`) | unify or map |
| key enums | `EditorKey` · `MapKeyboardKey` ×2 (`Fdp.Presentation/Vis2D/Abstractions` **and** `GizmoMap.Contracts/Interaction` — duplicate) · `ImGuiKey` · Raylib `KeyboardKey` (+ `InteractionKeyboardKey` on the gizmo bus) | 5–6 enums with ad-hoc casts `(MapKeyboardKey)KeyboardKey.W` | one chord type needed |
| `ShortcutsWindow` | none | | new |
| dead seam | `IShellCommandRegistrar.RegisterShortcut` — 0 callers | | delete (design K5) |

---

## G. The 5 most surprising findings

1. **There is no "the map has the keyboard" concept at all.** The map's gate is *mouse not over ImGui* (`MapCanvas.cs:206`), and `IsKeyboardCaptured` is only a text-field flag; so the design's "clicking the map takes focus" has nothing to build on — K1 must introduce map focus itself.
2. **The dumb terminal already exists — but only for gizmos.** Held keys and every driver (free-fly `MapCamera3D`, the 30-read `PossessionStation`, follow camera, fall screen) read `IInputProvider` locally each frame, bypassing `GizmoKeyEvent`; §2.4's "map keys resolved on the backend" is false for them today.
3. **No central Esc and 41 readers** (13 gizmos, not 12; 3 drivers; ~25 ImGui sites) — plus `Esc` is a *command* (Deselect All) fired by the graph dispatcher in every drawn canvas.
4. **Nothing named `ActionDecl`/`Keymap`/`KeyRouter`/`ActionCatalogue` exists**; the nearest prior art is `EditorCommandDescriptor` + `KeyBinding` (string ids, NodeEdit's own `EditorKey`), 4 registries in 3 id spaces, and 5–6 key enums (one duplicated across two assemblies).
5. **Unguarded raw reads and a per-key JSON round-trip**: `AudioLayer` F6-F10 and `PerformanceOverlay` F3 read Raylib with no gate (collide with find-next / next-error / breakpoint), the Player pans with arrows behind a *mouse* gate, and every map key press runs `MenuShortcuts.Resolve` = serialize + deserialize the unit menu JSON.

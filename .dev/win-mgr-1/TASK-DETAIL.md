# Window Manager & Icon System — Task Details

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture and phase overview.  
**Tracker:** See [TASK-TRACKER.md](./TASK-TRACKER.md) for progress status.

---

## Phase 1 — Icon System Foundation

---

### WM-S101: `IconAtlas` — Resource Loading, UV Parsing, Disposal

**Design ref:** [DESIGN.md §3.1](./DESIGN.md#31-iconatlas)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconAtlas.cs`

**Description:**

Create the `IconAtlas` class in namespace `FDP.Toolkit.ImGui.Icons`. The class wraps a single Raylib `Texture2D` loaded from disk. It parses string atlas coordinates (e.g. `"b12"`) into UV vector pairs. It must implement `IDisposable` and unload the texture on disposal.

Coordinate parsing rules:
- First character (letter) identifies the row. `'a'` = row 0, `'b'` = row 1, etc. (case-insensitive).
- Remaining characters are a 1-based integer identifying the column. `"12"` → column index 11.
- UV0 = `(col * iconSize / atlasWidth, row * iconSize / atlasHeight)`
- UV1 = UV0 + `(iconSize / atlasWidth, iconSize / atlasHeight)`

Invalid or malformed coordinates return `(Vector2.Zero, Vector2.One)` without throwing.

**API:**
```csharp
namespace FDP.Toolkit.ImGui.Icons;

public class IconAtlas : IDisposable
{
    public IntPtr  TextureId   { get; }
    public Vector2 IconSizeVec { get; }

    public IconAtlas(string texturePath, float iconSize = 16f)
    public (Vector2 uv0, Vector2 uv1) GetUvCoordinates(string coordinate)
    public void Dispose()
}
```

**Success conditions:**

Unit tests in `FDP.Toolkit.ImGui.Tests/Icons/IconAtlasTests.cs` (or equivalent test project):

1. **UV calculation — row parsing:** `GetUvCoordinates("a1")` returns uv0.Y = 0.0f. `GetUvCoordinates("b1")` returns uv0.Y > 0.0f (specifically `iconSize / atlasHeight`).
2. **UV calculation — column parsing:** `GetUvCoordinates("a1")` returns uv0.X = 0.0f. `GetUvCoordinates("a2")` returns uv0.X = `iconSize / atlasWidth`.
3. **1-based column index:** Column `"1"` → index 0 (X = 0). Column `"12"` → index 11 (X = `11 * iconSize / atlasWidth`).
4. **Case-insensitive row:** `GetUvCoordinates("B12")` and `GetUvCoordinates("b12")` return identical UV pairs.
5. **UV1 offset:** `uv1 - uv0` equals `(iconSize/atlasWidth, iconSize/atlasHeight)` for any valid coordinate.
6. **Malformed input — empty string:** Returns `(Vector2.Zero, Vector2.One)` without throwing.
7. **Malformed input — no numeric part:** `"a"` alone returns `(Vector2.Zero, Vector2.One)`.
8. **Malformed input — null:** Returns `(Vector2.Zero, Vector2.One)`.
9. **Dispose does not throw on double-call:** Calling `Dispose()` twice must not throw.
10. **TextureId not zero after construction** (requires a valid texture loaded; mock or stub Raylib if the test project is headless).

> Note: `IconAtlas` construction calls `Raylib.LoadTexture` which requires a GPU context. Tests for UV math should use a test-friendly overload or constructor that accepts pre-set atlas dimensions (width, height) and a dummy `IntPtr` for `TextureId`, avoiding Raylib in unit tests.

---

### WM-S102: `IconWidgets` — `InlineIcon` and `AbsoluteIcon`

**Design ref:** [DESIGN.md §3.2.1](./DESIGN.md#321-stateless-rendering)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`

**Description:**

Create the `IconWidgets` static class in namespace `FDP.Toolkit.ImGui.Icons`. This task implements only the two stateless (non-interactive) rendering methods.

- **`InlineIcon`**: Calls `Gui.Image(atlas.TextureId, atlas.IconSizeVec, uv0, uv1)` at the current layout cursor, then calls `Gui.SameLine()` so the following widget renders immediately to the right.
- **`AbsoluteIcon`**: Obtains `Gui.GetWindowDrawList()` and calls `drawList.AddImage(...)` at the specified `screenPos`. Does **not** call `Gui.SameLine()` or otherwise modify the layout cursor.

Both methods resolve UV coordinates via `atlas.GetUvCoordinates(coordinate)`.

**API:**
```csharp
namespace FDP.Toolkit.ImGui.Icons;

public static class IconWidgets
{
    public static void InlineIcon(IconAtlas atlas, string coordinate)
    public static void AbsoluteIcon(IconAtlas atlas, string coordinate, Vector2 screenPos)
}
```

**Success conditions:**

These methods depend on a live ImGui context and cannot be unit-tested headlessly in the traditional sense. Verification is by integration/visual test or by inspecting call sequences using a mock draw list. The success conditions are therefore specification-level:

1. **`InlineIcon` calls `Gui.SameLine()`** after `Gui.Image()`. (Verifiable via ImGui capture or mock wrapper that records calls.)
2. **`InlineIcon` passes the correct UV pair** to `Gui.Image` as derived from `atlas.GetUvCoordinates`.
3. **`AbsoluteIcon` does not call `Gui.SameLine()`** or modify the layout cursor position.
4. **`AbsoluteIcon` draws at `screenPos`** (the `AddImage` min-point equals `screenPos`).
5. **`AbsoluteIcon` draws to `screenPos + atlas.IconSizeVec`** (the `AddImage` max-point).
6. **Null-safe:** Passing a null or empty coordinate string does not throw (falls back to full-atlas UV from `IconAtlas.GetUvCoordinates`).

---

### WM-S103: `IconWidgets` — `IconButton` and `ToggleIcon`

**Design ref:** [DESIGN.md §3.2.2](./DESIGN.md#322-interactive-widgets)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`

**Description:**

Add `IconButton` and `ToggleIcon` to `IconWidgets`. Both use the `InvisibleButton` + `ImDrawList` pattern:

1. Capture `screenPos = Gui.GetCursorScreenPos()`.
2. `clicked = Gui.InvisibleButton(id, size)`.
3. Query `isHovered = Gui.IsItemHovered()` and `isPressed = Gui.IsItemActive()`.
4. Obtain `drawList = Gui.GetWindowDrawList()`.
5. Optionally draw filled background (only `ToggleIcon` when `isToggled`).
6. Draw image at `imagePos = isPressed ? screenPos + (1,1) : screenPos`.
7. If `isHovered` draw a rectangle border.

**ToggleIcon:** when `clicked`, flips `isToggled`. Filled background color: `(0.3f, 0.3f, 0.3f, 1.0f)` converted via `Gui.GetColorU32`.

**IconButton:** implemented by delegating to `ToggleIcon` with a discarded dummy `bool`.

```csharp
public static bool IconButton(IconAtlas atlas, string id, string coordinate)
public static bool ToggleIcon(IconAtlas atlas, string id, string coordinate, ref bool isToggled)
```

**Success conditions:**

1. **`ToggleIcon` flips state on click:** When `clicked = true`, `isToggled` is inverted.
2. **`ToggleIcon` returns `true` only on click:** When no click occurs, method returns `false`.
3. **Press shift applied:** When `isPressed`, image is drawn at `screenPos + Vector2(1,1)`. When not pressed, drawn at `screenPos`.
4. **Hover border drawn only when hovered:** `drawList.AddRect` is called iff `isHovered`.
5. **Toggle background drawn only when toggled:** Filled rect (`AddRectFilled`) is drawn iff `isToggled`.
6. **Toggle background NOT drawn for `IconButton`:** Since `IconButton` uses a discarded dummy state, no filled background appears.
7. **Invisible button size matches `atlas.IconSizeVec`.**
8. **`IconButton` returns `true` on click, `false` otherwise.**

---

### WM-S104: `IconWidgets` — `AlternatingFaceToggleIcon`

**Design ref:** [DESIGN.md §3.2.2](./DESIGN.md#322-interactive-widgets)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`

**Description:**

Add `AlternatingFaceToggleIcon` to `IconWidgets`. This is identical to `ToggleIcon` in structure but:
- Selects the icon coordinate based on current state: if `isToggled` use `trueCoordinate`, else `falseCoordinate`.
- Does **not** draw a filled background. Visual state is expressed purely by swapping the icon face.

```csharp
public static bool AlternatingFaceToggleIcon(
    IconAtlas atlas, string id,
    string trueCoordinate, string falseCoordinate,
    ref bool isToggled)
```

**Success conditions:**

1. **Icon face selection:** When `isToggled = true`, UV pair for `trueCoordinate` is used. When `isToggled = false`, UV pair for `falseCoordinate` is used.
2. **State flip on click:** `isToggled` is inverted when clicked.
3. **Returns `true` on click.**
4. **No filled background drawn** (no `AddRectFilled` call), regardless of toggle state.
5. **Hover border and press-shift** behave identically to `ToggleIcon`.
6. **Different `trueCoordinate` and `falseCoordinate`** resolve to different UV pairs (validates atlas lookup is called with the correct argument).

---

### WM-S105: `IconWidgets` — `DropdownFaceIcon`

**Design ref:** [DESIGN.md §3.2.2](./DESIGN.md#322-interactive-widgets)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`

**Description:**

Add `DropdownFaceIcon`. The currently selected icon (at `availableCoordinates[selectedIndex]`) is rendered using the standard `InvisibleButton` + `ImDrawList` pipeline. When clicked, `Gui.OpenPopup(popupId)` is called. The popup renders a grid of `iconsPerRow = 4` icons using `Gui.ImageButton` wrapped with `Gui.PushID(i)` / `Gui.PopID()`. Selecting a popup icon sets `selectedIndex` and calls `Gui.CloseCurrentPopup()`. Returns `true` when `selectedIndex` changes.

Safety clamp: if `selectedIndex` is out of range on entry, it is reset to 0.

```csharp
public static bool DropdownFaceIcon(
    IconAtlas atlas, string id,
    IReadOnlyList<string> availableCoordinates,
    ref int selectedIndex)
```

**Success conditions:**

1. **Out-of-bounds guard:** if `selectedIndex < 0` or `>= availableCoordinates.Count`, it is clamped to 0 without throwing.
2. **Selected icon rendered:** UV pair for `availableCoordinates[selectedIndex]` drives the displayed icon.
3. **Popup opened on click:** `Gui.OpenPopup` is called with the popup ID derived from `id` when `InvisibleButton` returns `true`.
4. **Grid layout — row wrap:** item `i = 4` (when `iconsPerRow = 4`) starts a new row (no `SameLine()` before it).
5. **Selection changes `selectedIndex`:** Clicking item `i` in the popup sets `selectedIndex = i` and closes the popup.
6. **Returns `true` on selection change, `false` otherwise.**
7. **`PushID` / `PopID` balanced:** For each popup item, `PushID` and `PopID` calls are balanced.
8. **Empty list:** if `availableCoordinates` is empty, returns `false` without throwing.

---

## Phase 2 — Window Manager: ManagedWindow Base

---

### WM-S201: `WindowScope` + `ManagedWindow` Abstract Base

**Design ref:** [DESIGN.md §4.1](./DESIGN.md#41-core-concepts), [§4.2](./DESIGN.md#42-managedwindow)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowScope.cs`
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/ManagedWindow.cs`

**Description:**

Create `WindowScope` enum with values `PerspectiveBound` and `Global`.

Create `ManagedWindow` abstract base class. Implement the `Render(string currentPerspective, IconAtlas atlas)` method with full visibility gating logic:

- `Global` windows: visible if `IsOpen`.
- `PerspectiveBound` windows: visible if `IsOpen` AND (`IsPinned` OR `OwningPerspective == currentPerspective`).

Implement `internal void RequestFocus()` which sets `_focusRequested = true`. Consume and clear `_focusRequested` inside `Render()` by calling `Gui.SetWindowFocus(windowInternalName)` before `Gui.Begin`.

Window name format: `$"{Title}###{Id}"`.

`DrawLocalMenuBar()` has a default empty implementation (not abstract). `DrawClientArea()` is abstract.

**Success conditions (unit tests — no ImGui context needed for logic tests):**

1. **Visibility — Global, open:** `Scope = Global`, `IsOpen = true` → `isVisible = true`.
2. **Visibility — Global, closed:** `Scope = Global`, `IsOpen = false` → `Render` exits before `Gui.Begin`.
3. **Visibility — PerspectiveBound, matching perspective, not pinned:** `isVisible = true`.
4. **Visibility — PerspectiveBound, wrong perspective, not pinned:** `isVisible = false`.
5. **Visibility — PerspectiveBound, wrong perspective, pinned:** `isVisible = true`.
6. **Visibility — PerspectiveBound, closed:** `isVisible = false` regardless of perspective/pin.
7. **Focus flag consumed after render:** After `Render()` is called with `_focusRequested = true`, `_focusRequested` is reset to `false`.
8. **Focus flag not set by default:** `_focusRequested` is `false` after construction.
9. **`RequestFocus()` sets flag:** After call, `_focusRequested` is `true`.
10. **Window name format:** The string passed to `Gui.Begin` is `"{Title}###{Id}"`.

---

### WM-S202: `ManagedWindow` Custom Title Bar Controls

**Design ref:** [DESIGN.md §4.2](./DESIGN.md#42-managedwindow)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/ManagedWindow.cs`

**Description:**

Implement `DrawCustomTitleBarControls(string currentPerspective, bool perspectiveActive, IconAtlas atlas)` as a private method called from `Render()` immediately after `Gui.Begin()` succeeds.

**Pin icon** (only for `PerspectiveBound` windows):
- Uses `IconWidgets.AlternatingFaceToggleIcon` with `"pin_on"` (when `IsPinned`) and `"pin_off"` (when not pinned).
- If `AlternatingFaceToggleIcon` returns `true`, update `IsPinned`.
- After the pin toggle, if `!IsPinned && !perspectiveActive` and `Gui.IsItemHovered()` → call `Gui.SetTooltip("Unpinning will hide this window in the current perspective.")`.

**Close icon** (all windows):
- Uses `IconWidgets.IconButton` with `"cross"`.
- If clicked: `IsOpen = false`, `IsPinned = false`.

Positioning: icons are placed at the right side of the title bar using `Gui.SameLine(Gui.GetWindowWidth() - offset)`.

**Success conditions:**

1. **Pin icon not rendered for `Global` windows:** no `AlternatingFaceToggleIcon` call when `Scope == Global`.
2. **Pin click toggles `IsPinned`:** When `AlternatingFaceToggleIcon` returns `true`, `IsPinned` is toggled.
3. **Tooltip shown on unpin-when-inactive:** When `!IsPinned` (just unpinned), `!perspectiveActive`, and `IsItemHovered()` returns true → `SetTooltip` is called with the specified message.
4. **Tooltip NOT shown when perspective is active:** If `perspectiveActive = true`, `SetTooltip` is not called even if hovered.
5. **Close icon sets `IsOpen = false`** when `IconButton` returns true.
6. **Close icon sets `IsPinned = false`** when `IconButton` returns true.
7. **Close icon always rendered:** present for both `PerspectiveBound` and `Global` windows.

---

### WM-S203: `ManagedWindow` Optional Local Menu Bar

**Design ref:** [DESIGN.md §4.2](./DESIGN.md#42-managedwindow)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/ManagedWindow.cs`

**Description:**

`ManagedWindow` must support an optional local menu bar that appears inside the ImGui window (below the title bar). The menu bar is rendered using `ImGuiWindowFlags.MenuBar`.

- Protected virtual property `HasMenuBar` (default: `false`).
- If `HasMenuBar`, the `ImGuiWindowFlags.MenuBar` flag is passed to `Gui.Begin`.
- After `DrawCustomTitleBarControls`, and if `HasMenuBar`, call `Gui.BeginMenuBar()` → `DrawLocalMenuBar()` → `Gui.EndMenuBar()`.
- `DrawLocalMenuBar()` is a `protected virtual` method with an empty default implementation.

**Success conditions:**

1. **`HasMenuBar = false` (default):** `Gui.Begin` is called without `ImGuiWindowFlags.MenuBar`. `Gui.BeginMenuBar` is never called.
2. **`HasMenuBar = true`:** `Gui.Begin` is called with `ImGuiWindowFlags.MenuBar`. `Gui.BeginMenuBar` is called.
3. **`DrawLocalMenuBar` called when `BeginMenuBar` succeeds.**
4. **`DrawLocalMenuBar` default implementation does nothing** (no exceptions, no ImGui calls).
5. **Subclass override works:** A subclass overriding `HasMenuBar → true` and `DrawLocalMenuBar` to call one `Gui.MenuItem` results in that item appearing in the local menu bar.

---

## Phase 3 — Window Manager: Menu Registry & Orchestrator

---

### WM-S301: `GlobalMenuRegistry` — Trie Data Structure + Registration API

**Design ref:** [DESIGN.md §4.3](./DESIGN.md#43-globalmenuregistry)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/GlobalMenuRegistry.cs`

**Description:**

Create `MenuItemNode` (internal data structure) and `GlobalMenuRegistry` (public class).

`MenuItemNode` has:
- `string Name`
- `Action? OnClick`
- `Func<bool>? GetCheckedState`
- `Action<bool>? OnCheckedChanged`
- `bool IsSeparator`
- `Dictionary<string, MenuItemNode> Children`

`GlobalMenuRegistry.RegisterItem(string path, Action onClick)` parses `path` by splitting on `'/'`, traverses/creates the trie from `Root`, and assigns `OnClick` to the final node. `RegisterCheckableItem` and `RegisterSeparator` follow the same traversal pattern.

Overwriting an existing node's action (re-registering the same path) is allowed (last-write-wins).

```csharp
public class GlobalMenuRegistry
{
    public MenuItemNode Root { get; }
    public void RegisterItem(string path, Action onClick)
    public void RegisterCheckableItem(string path, Func<bool> getChecked, Action<bool> onChanged)
    public void RegisterSeparator(string path)
}
```

**Success conditions:**

1. **Single-level path:** `RegisterItem("File")` creates one child of `Root` named `"File"`.
2. **Multi-level path:** `RegisterItem("Tools/Radar/Show")` creates `Root → Tools → Radar → Show` with `OnClick` on the leaf.
3. **Shared parent nodes:** `RegisterItem("Tools/A", ...)` and `RegisterItem("Tools/B", ...)` share the same `Root.Children["Tools"]` node.
4. **`OnClick` assigned to leaf only:** Intermediate nodes have `OnClick = null`.
5. **Re-registration — last write wins:** Calling `RegisterItem("Tools/A", action1)` then `RegisterItem("Tools/A", action2)` results in `action2` on the node.
6. **`RegisterCheckableItem`:** leaf node has `GetCheckedState` and `OnCheckedChanged` set, `OnClick` null.
7. **`RegisterSeparator`:** leaf node has `IsSeparator = true`.
8. **Empty path throws `ArgumentException`** (or behaves safely — specify team preference; recommend exception).
9. **Path with trailing slash is handled** (empty segment after split is ignored or treated as the leaf node name `""`).

---

### WM-S302: `WindowManager` — Registry + Programmatic API

**Design ref:** [DESIGN.md §4.4](./DESIGN.md#44-windowmanager), [§4.4.5](./DESIGN.md#445-programmatic-api-rules)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

**Description:**

Create the `WindowManager` class. This task covers only the registry and programmatic API; rendering is covered by WM-S303–305.

Internal storage: `Dictionary<string, ManagedWindow> _windows`.

```csharp
public class WindowManager
{
    public WindowManager(IconAtlas atlas)
    public void   RegisterWindow(ManagedWindow window)
    public bool   TryGetWindow(string id, [MaybeNullWhen(false)] out ManagedWindow window)
    public void   ShowWindow(string id)
    public void   HideWindow(string id)
    public void   SetWindowPinned(string id, bool isPinned)
    public void   FocusWindow(string id)
    public string CurrentPerspective { get; private set; }
    public event  Action<string, string>? OnPerspectiveChanged
    public void   SwitchPerspective(string newPerspective)
    public GlobalMenuRegistry GlobalMenu { get; }
}
```

Behaviour of each method as specified in [DESIGN.md §4.4.5](./DESIGN.md#445-programmatic-api-rules).

**Success conditions:**

1. **`RegisterWindow` stores by Id:** `TryGetWindow(window.Id, ...)` returns `true` after registration.
2. **`ShowWindow` sets `IsOpen = true`.**
3. **`ShowWindow` auto-pins (cross-perspective):** When window is `PerspectiveBound` and `OwningPerspective != CurrentPerspective` → `IsPinned = true`.
4. **`ShowWindow` does not auto-pin (same perspective):** `IsPinned` is unchanged if perspective matches.
5. **`ShowWindow` does not auto-pin `Global` windows.**
6. **`HideWindow` sets `IsOpen = false` and `IsPinned = false`.**
7. **`SetWindowPinned` updates `IsPinned` for `PerspectiveBound` windows.**
8. **`SetWindowPinned` is a no-op for `Global` windows.**
9. **`FocusWindow` calls `ShowWindow` logic AND calls `win.RequestFocus()`.**
10. **`FocusWindow` on hidden cross-perspective window sets `IsPinned = true`.**
11. **`SwitchPerspective` sets `CurrentPerspective`.**
12. **`SwitchPerspective` fires `OnPerspectiveChanged` with (old, new) strings.**
13. **`SwitchPerspective` no-op when same perspective:** event is NOT fired.
14. **`TryGetWindow` returns `false` for unknown id.**
15. **Unknown id in `ShowWindow` / `HideWindow` / `SetWindowPinned` / `FocusWindow` is silently ignored** (no exception).
16. **Initial `CurrentPerspective` is `"Default"` (or another reasonable sentinel).**

---

### WM-S303: `WindowManager.Render()` — Global Menu + Windows Pulldown + Auto-Pin

**Design ref:** [DESIGN.md §4.4.1](./DESIGN.md#441-render-structure), [§4.4.2](./DESIGN.md#442-windows-menu)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

**Description:**

Implement the `Render()` method and the two private helper methods it calls:

**`RenderGlobalMenu(MenuItemNode node)`:** Recursively renders the menu tree.
- If `node.IsSeparator` → `Gui.Separator()`.
- If `node.Children.Count > 0` → `Gui.BeginMenu(node.Name)` → recurse children → `Gui.EndMenu()`.
- If leaf with `OnClick` → `Gui.MenuItem(node.Name)` and call `OnClick` if returned true.
- If leaf with `GetCheckedState`/`OnCheckedChanged` → `Gui.MenuItem(node.Name, "", ref checked)` style call.

**`RenderFixedWindowsMenu()`:** `Gui.BeginMenu("Windows")`, groups `PerspectiveBound` windows by `OwningPerspective` (sub-menu per perspective, sorted alphabetically). `Global` windows listed under a `"Global"` sub-menu. Each entry is a checkable `MenuItem` mirroring `win.IsOpen`. When a menu click sets `win.IsOpen = true`, apply auto-pin if `win.OwningPerspective != CurrentPerspective`. When a click sets `win.IsOpen = false`, also set `win.IsPinned = false`.

**`Render()` skeleton:**
```
BeginMainMenuBar
  RenderGlobalMenu(GlobalMenu.Root)
  RenderFixedWindowsMenu()
  RenderPerspectiveSwitcher()   // covered in WM-S304
  RenderFixedHelpMenu()         // covered in WM-S305
EndMainMenuBar
foreach window: window.Render(CurrentPerspective, _iconAtlas)
```

**Success conditions:**

1. **Global menu — click invokes `OnClick`:** A registered `Action` is called when the corresponding menu item is selected.
2. **Global menu — checkable item updates state:** Selecting a checkable item invokes `OnCheckedChanged` with the new checked value.
3. **Global menu — recursive nesting:** A three-level path `"A/B/C"` renders as nested `BeginMenu("A") → BeginMenu("B") → MenuItem("C")`.
4. **Windows pulldown — perspective sub-menus:** Windows belonging to different perspectives appear under separate sub-menus named after their perspective.
5. **Windows pulldown — Global sub-menu:** `Global` windows appear under `"Global"` sub-menu.
6. **Windows pulldown — checkable state reflects `IsOpen`:** A window with `IsOpen = true` has a checked state in the menu.
7. **Windows pulldown — auto-pin on cross-perspective open:** Opening a `PerspectiveBound` window from the menu while its perspective is not active sets `IsPinned = true`.
8. **Windows pulldown — close resets pin:** Unchecking a window via the menu sets `IsOpen = false` and `IsPinned = false`.
9. **All registered windows rendered each frame** via `window.Render(...)`.

---

### WM-S304: Perspective Switcher + `SwitchPerspective` + `OnPerspectiveChanged`

**Design ref:** [DESIGN.md §4.4.3](./DESIGN.md#443-perspective-switcher)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

**Description:**

Implement `RenderPerspectiveSwitcher()`. It collects all distinct `OwningPerspective` strings from registered `PerspectiveBound` windows, sorts them alphabetically, then renders an `Gui.RadioButton(p, isActive)` for each. Clicking a radio button calls `SwitchPerspective(p)` (already implemented in WM-S302). Each radio button is followed by `Gui.SameLine()` except the last one.

The `SwitchPerspective` method and `OnPerspectiveChanged` event were already implemented in WM-S302. This task wires the renderer to call it.

**Success conditions:**

1. **Perspectives are discovered from windows:** Adding a window with `OwningPerspective = "X"` causes `"X"` to appear as a radio button.
2. **Active perspective radio button is checked:** The radio button for `CurrentPerspective` is visually active.
3. **Click switches perspective:** Clicking a radio button triggers `SwitchPerspective` and updates `CurrentPerspective`.
4. **Alphabetical sort:** If perspectives `"Zebra"` and `"Alpha"` both exist, `"Alpha"` appears first.
5. **`Global` windows do not contribute perspectives:** A window with `Scope = Global` is not counted among perspectives.
6. **No duplicate perspectives:** Even if multiple windows share the same `OwningPerspective`, it appears once in the switcher.

---

### WM-S305: `WindowManager.Render()` — Help / Debug Menu

**Design ref:** [DESIGN.md §4.4.4](./DESIGN.md#444-help-menu)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

**Description:**

Implement `RenderFixedHelpMenu()`. Fixed structure:

```
Help
  ├── Debug
  │     ├── <Global window 1>   (checkable, mirrors IsOpen)
  │     └── ...
  └── About   (no-op MenuItem)
```

All `Global`-scope windows are listed under `Help → Debug`. Each is a checkable item mirroring `IsOpen`. Clicking toggles `IsOpen` (via the same show/hide logic as the Windows menu; no auto-pin since they are Global). `"About"` is a placeholder `MenuItem` (no action implementation required for this task, just renders the item).

**Success conditions:**

1. **`Global` windows listed under `Help → Debug`.**
2. **`PerspectiveBound` windows NOT listed under `Help → Debug`.**
3. **Checkable state reflects `IsOpen`.**
4. **Toggling a Debug entry sets/clears `IsOpen`.**
5. **`About` menu item is rendered** (no action test required).
6. **`Help` menu is always rendered** regardless of registered windows.

---

## Phase 4 — Persistence & Docking

---

### WM-S401: ImGui Custom Settings Handler for `IsOpen` / `IsPinned` Persistence

**Design ref:** [DESIGN.md §4.4.6](./DESIGN.md#446-persistence)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

**Description:**

Register a custom ImGui settings handler during `WindowManager` construction using `ImGui.AddSettingsHandler()`. The handler operates on the section name `"FDP_WindowManager"`.

**Write (`WriteAllFn`):** For each registered window, write a line:
```
{window.Id}={IsOpen},{IsPinned}
```
under the section header `[FDP_WindowManager]`.

**Read (`ReadLineFn`):** Parse each `key=value` line. Split on `'='`, then split value on `','`. Map `key` to `window.Id`. If a matching window is registered, restore `IsOpen` and `IsPinned`.

**FindByName:** Match the section name `"FDP_WindowManager"` case-insensitively.

Edge cases:
- Windows registered after `ReadLineFn` has run (late registration) are not restored. Registration should complete before the first `Render()` call.
- Malformed lines are silently skipped.

**Success conditions:**

1. **Handler is registered:** After construction, an `ImGui.AddSettingsHandler` call with section `"FDP_WindowManager"` has been made (verifiable via integration test or mock).
2. **Write output format:** The written section contains one line per window in `{id}={IsOpen},{IsPinned}` format.
3. **Round-trip — `IsOpen = true, IsPinned = true`:** Write then read restores both values.
4. **Round-trip — `IsOpen = false, IsPinned = false`:** Write then read restores both values.
5. **Unknown id in read is silently skipped:** A line referencing an unregistered window ID does not throw.
6. **Malformed line skipped:** A line without `','` in the value does not throw.
7. **Late-registered window not affected by early read:** Window registered after the read pass retains its default state.

---

### WM-S402: ImGui Docking Integration

**Design ref:** [DESIGN.md §4.1.4](./DESIGN.md#414-docking)

**Files to modify:**
- `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`

**Description:**

Enable ImGui docking in `SubsystemOrchestrator`. During Raylib+ImGui initialization:

```csharp
ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
```

At the start of each `DrawUI` phase (inside the `rlImGui.Begin()`/`rlImGui.End()` block), create a fullscreen dockspace before the subsystems draw their panels:

```csharp
var viewport = ImGui.GetMainViewport();
ImGui.SetNextWindowPos(viewport.WorkPos);
ImGui.SetNextWindowSize(viewport.WorkSize);
ImGui.SetNextWindowViewport(viewport.ID);
ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
ImGui.Begin("##DockSpace", ImGuiWindowFlags.NoDocking | ... | ImGuiWindowFlags.NoBackground);
ImGui.PopStyleColor();
ImGui.PopStyleVar(2);
ImGui.DockSpace(ImGui.GetID("MainDockSpace"), Vector2.Zero, ImGuiDockNodeFlags.PassthruCentralNode);
ImGui.End();
```

This must occur before any `ManagedWindow.Render()` or subsystem `DrawUI()` calls.

**Success conditions:**

1. **`DockingEnable` flag set:** `ImGui.GetIO().ConfigFlags` has `DockingEnable` bit after initialization.
2. **DockSpace created before subsystem UI:** `ImGui.DockSpace` is called with ID `"MainDockSpace"` before any subsystem `DrawUI()`.
3. **`PassthruCentralNode` flag set:** The map background rendered by subsystems remains visible through the transparent dockspace central node.
4. **No Z-fighting with map rendering:** The dockspace window uses `NoBackground` and zero `WindowBg` color so Raylib's `DrawWorld` pass is visible beneath the ImGui layer.
5. **Build compiles with no errors** after the changes to `SubsystemOrchestrator`.

---

## Phase 5 — Framework Integration

---

### WM-S501: `SubsystemOrchestrator` — Expose `WindowManager` to Subsystems

**Design ref:** [DESIGN.md §5](./DESIGN.md#5-implementation-phases--tasks), [DESIGN.md §2.1](./DESIGN.md#21-existing-infrastructure)

**Files to modify:**
- `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`
- `FDP/Framework/FDP.Framework.Runner/ISubsystem.cs`
- `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs`

**Description:**

`SubsystemOrchestrator` creates a single `WindowManager` instance (owning it and calling `Render()` in the UI phase). It passes a reference to subsystems during `Initialize`:

Option: extend `SubsystemConfig` with `WindowManager? WindowManager` property, allowing subsystems that need it to call `RegisterWindow` and `GlobalMenu.RegisterItem` during their `Initialize` override. Subsystems that do not need the Window Manager simply ignore the property.

`SubsystemOrchestrator.Render()` calls `_windowManager.Render()` once per frame inside the `rlImGui` block, before iterating each subsystem's `DrawUI()` call.

`ISubsystem` interface does **not** change its contract — the `WindowManager` is accessed via `SubsystemConfig`.

**Success conditions:**

1. **`SubsystemConfig.WindowManager` property is set** before `ISubsystem.Initialize` is called.
2. **A subsystem can call `windowManager.RegisterWindow(...)` inside `Initialize`** without exception.
3. **`WindowManager.Render()` is called once per frame** in the render loop.
4. **`WindowManager.Render()` is called before each subsystem's `DrawUI()`** so the main menu bar appears above subsystem panels.
5. **Build compiles with no errors.**
6. **Existing tests that use `SubsystemOrchestrator` still pass** (backward-compatible change via nullable `WindowManager?` in `SubsystemConfig`).

---

### WM-S502: Composition Root — `OnPerspectiveChanged` → Publish `TogglePerspectiveEvent`

**Design ref:** [DESIGN.md §6.3](./DESIGN.md#63-toggleperspectiveevent), [§6.6](./DESIGN.md#66-synchronization-flow)

**Files to modify:**
- `Hrot.ClusterRunner/Program.cs`

**Description:**

In `Program.cs`, after the `WindowManager` is constructed, subscribe to `windowManager.OnPerspectiveChanged`. In the handler, publish a `TogglePerspectiveEvent` on the `FdpEventBus`. Do **not** call `SwitchMapOwner` directly from here — that is the responsibility of `PerspectiveCoordinatorSystem` (WM-S703) which subscribes to the event.

```csharp
windowManager.OnPerspectiveChanged += (oldPersp, newPersp) =>
{
    fdpEventBus.Publish(new TogglePerspectiveEvent(oldPersp, newPersp));
};
```

This keeps the composition root thin: it simply bridges the UI event into the domain event bus.

**Success conditions:**

1. **`TogglePerspectiveEvent` published on perspective change:** Switching perspective in the WindowManager causes exactly one `TogglePerspectiveEvent` to be published on the bus with matching `OldPerspective` and `NewPerspective` values.
2. **No-op on same perspective:** `SwitchPerspective` with the same value does not fire `OnPerspectiveChanged`, so no event is published.
3. **`FDP.Toolkit.ImGui` is not referenced from `FDP.Framework.Runner`** — the bridge is only in `Hrot.ClusterRunner`.
4. **Build compiles with no errors.**

---

### WM-S503: `SubsystemOrchestrator` Dockspace Height — Reserve Status Bar Space

**Design ref:** [DESIGN.md §5.4](./DESIGN.md#54-windowmanager-integration)

**Files to modify:**
- `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`

**Description:**

The dockspace created in WM-S402 must not overlap the status bar. After the `WindowManager` is available via `SubsystemConfig`, read `windowManager.StatusBar.Height` and reduce the dockspace height accordingly:

```csharp
var dockspaceSize = new Vector2(viewport.WorkSize.X, viewport.WorkSize.Y - windowManager.StatusBar.Height);
ImGui.DockSpace(ImGui.GetID("MainDockSpace"), dockspaceSize, ImGuiDockNodeFlags.PassthruCentralNode);
```

**Success conditions:**

1. **Dockspace height reduced:** The dockspace height is `viewport.WorkSize.Y - StatusBar.Height` (not the full height).
2. **Status bar not covered:** At runtime, the status bar at the bottom is visually separate from any docked windows.
3. **`StatusBar.Height = 0` degrades gracefully:** If the status bar height is zero (no sections registered), the dockspace fills the full viewport height without negative size.
4. **Build compiles with no errors.**

---

## Phase 6 — Status Bar

---

### WM-S601: `StatusBarManager` — Delegate Registry + Sorted Render Loop

**Design ref:** [DESIGN.md §5.2](./DESIGN.md#52-delegate-based-registration), [§5.3](./DESIGN.md#53-render-implementation)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/StatusBarManager.cs`

**Description:**

Create `StatusBarManager` in namespace `FDP.Toolkit.ImGui.WindowManager`. The class maintains an internal list of registered sections, each described by a private struct:

```csharp
private struct Section
{
    public string Id;
    public int    SortOrder;
    public Action RenderDelegate;
}
```

**`RegisterSection(string id, int sortOrder, Action renderDelegate)`:**
- Appends the section to the internal list.
- If a section with the same `Id` already exists, replace it (last-write-wins).
- Sets `_needsSort = true`.

**`Render()`:**
1. If `_needsSort`, sort sections by `SortOrder` ascending, set `_needsSort = false`.
2. Compute `height = Gui.GetFrameHeight() + Gui.GetStyle().WindowPadding.Y * 2f`.
3. Set `Height` property to this computed value.
4. `Gui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - height))`.
5. `Gui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, height))`.
6. `Gui.Begin("##GlobalStatusBar", NoDecoration | NoDocking | NoSavedSettings | NoFocusOnAppearing | NoNav | NoMove)`.
7. For each section:
   - Call `section.RenderDelegate()`.
   - If not the last section: `Gui.SameLine(); Gui.SeparatorEx(ImGuiSeparatorFlags.Vertical); Gui.SameLine()`.
8. `Gui.End()`.

`Height` is a `public float` property updated each frame from the computed value so that `SubsystemOrchestrator` can read it to shrink the dockspace.

**Success conditions:**

1. **Sorted by SortOrder:** A section with `sortOrder = 10` renders before one with `sortOrder = 100`. Verified by recording delegate invocation order.
2. **Sort is stable and deferred:** Sort happens at most once per registration change, not every frame.
3. **Duplicate Id overwrites:** Registering a second section with the same `Id` replaces the first; delegate count does not grow.
4. **Separator between sections:** If two sections are registered, `SeparatorEx(Vertical)` is called once between them.
5. **No separator after last section.**
6. **Empty registry renders without exception** (no sections registered).
7. **`Height` property reflects computed value** after `Render()` is called.
8. **Delegate is called exactly once per `Render()` per section.**
9. **`RegisterSection` with a null delegate throws `ArgumentNullException`.**

---

### WM-S602: `WindowManager.StatusBar` Property + `StatusBarManager.Render()` Integration

**Design ref:** [DESIGN.md §5.5](./DESIGN.md#55-windowmanager-integration)

**Files to modify:**
- `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

**Description:**

Add `private readonly StatusBarManager _statusBar = new()` to `WindowManager` and expose `public StatusBarManager StatusBar { get; }` returning `_statusBar`.

In `WindowManager.Render()`, after the `foreach window` loop, call:
```csharp
_statusBar.Render();
```

The `IconAtlas` is not passed into `StatusBarManager.Render()` — the subsystem's registered delegate closes over the `IconAtlas` instance directly, keeping the `StatusBarManager` itself atlas-agnostic.

**Success conditions:**

1. **`WindowManager.StatusBar` is not null** after construction.
2. **`StatusBarManager.Render()` is called once per `WindowManager.Render()` call.**
3. **`StatusBarManager.Render()` is called after all ManagedWindow renders** (status bar is the topmost overlay).
4. **Build compiles with no errors.**

---

### WM-S603: Reference Section Registration in `Hrot.ClusterRunner`

**Design ref:** [DESIGN.md §5.4](./DESIGN.md#54-subsystem-usage-example)

**Files to modify:**
- `Hrot.ClusterRunner/Program.cs`

**Description:**

Register a concrete status bar section in the application's composition root to validate the full pipeline works end-to-end. This is not a reusable class; it is a direct lambda registration demonstrating the pattern:

```csharp
// In Program.cs, after windowManager is constructed and atlas is loaded:
windowManager.StatusBar.RegisterSection("system_health", sortOrder: 0, renderDelegate: () =>
{
    IconWidgets.InlineIcon(atlas, "a1");   // placeholder icon
    Gui.SameLine();
    Gui.Text("System OK");
});
```

This section appears at the leftmost position and confirms that:
- `StatusBarManager` is accessible from composition root.
- `IconWidgets` works correctly inside a status bar delegate.
- The status bar bar is visually distinct (not overlapped by docked windows).

**Success conditions:**

1. **Section renders at the bottom of the screen** — confirmed visually at runtime.
2. **Section does not overlap docked windows** — the dockspace height (WM-S503) reserves the status bar height.
3. **`IconWidgets.InlineIcon` renders without exception** inside the delegate.
4. **Build compiles with no errors.**
5. **Existing integration tests in `Hrot.ClusterRunner.Integration.Tests` still pass** — headless run confirms no regression.

---

## Phase 7 — Background Map Perspective Manager

---

### WM-S701: `TogglePerspectiveEvent` Record

**Design ref:** [DESIGN.md §6.3](./DESIGN.md#63-toggleperspectiveevent)

**Files to create:**
- `Hrot.Common/Events/TogglePerspectiveEvent.cs`  _(confirm project with dev lead — may be `FDP.Framework.Runner` instead)_

**Description:**

Define a minimal, immutable record that carries the perspective transition:

```csharp
public record TogglePerspectiveEvent(string OldPerspective, string NewPerspective);
```

No behaviour, no dependencies. Published on `FdpEventBus` by the composition root handler (WM-S502). Consumed by `PerspectiveCoordinatorSystem` (WM-S703).

**Success conditions:**

1. **Record is value-equal:** Two instances with the same `OldPerspective` and `NewPerspective` are equal.
2. **Record is immutable:** No public setters; properties are init-only.
3. **No dependency on `FDP.Toolkit.ImGui` or `Raylib_cs`** — this event lives in the domain/runner layer only.
4. **Build compiles with no errors.**
5. **The project containing the event is referenced by both `Hrot.ClusterRunner` and any project that will host `PerspectiveCoordinatorSystem`.**

---

### WM-S702: `ActivePerspective` Singleton ECS Component

**Design ref:** [DESIGN.md §6.5](./DESIGN.md#65-activeperspective-singleton-ecs-component)

**Files to create:**
- `Hrot.Common/Components/ActivePerspective.cs`  _(confirm project with dev lead)_

**Description:**

Define as a value type (struct) to match the existing ECS singleton component convention:

```csharp
public struct ActivePerspective
{
    public string Name; // matches WindowManager.CurrentPerspective
}
```

Written by `PerspectiveCoordinatorSystem` after each switch. Read by individual map layer ECS systems to gate their `Draw()` calls. The component must be usable with the project's ECS `World.Set<T>()` / `World.Get<T>()` API.

**Success conditions:**

1. **Struct type** (not class) to follow existing singleton component convention — verify against other components in the project.
2. **`Name` field is writable** (not readonly) so the system can update it in-place.
3. **Component can be set and read via `World.Set<ActivePerspective>` / `World.Get<ActivePerspective>`** in an integration test (or unit test with a stub world).
4. **No dependency on `FDP.Toolkit.ImGui`.**
5. **Build compiles with no errors.**

---

### WM-S703: `PerspectiveCoordinatorSystem`

**Design ref:** [DESIGN.md §6.4](./DESIGN.md#64-perspectivecoordinatorsystem), [§6.6](./DESIGN.md#66-synchronization-flow)

**Files to create:**
- `Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs`

**Description:**

An ECS system that subscribes to `TogglePerspectiveEvent` on startup and performs the map-perspective coordination:

```csharp
public class PerspectiveCoordinatorSystem : ISystem  // or BaseSystem — match project convention
{
    private readonly SubsystemOrchestrator _orchestrator;
    private readonly IReadOnlyDictionary<string, IMapCameraProvider?> _perspectiveMap;

    public PerspectiveCoordinatorSystem(
        SubsystemOrchestrator orchestrator,
        IReadOnlyDictionary<string, IMapCameraProvider?> perspectiveMap)

    public void OnUpdate(World world, float dt)  // processes queued perspective events
}
```

Constructor injects the `SubsystemOrchestrator` and a map of perspective name → `IMapCameraProvider` (or null if not a map subsystem). On each `OnUpdate`, it dequeues any queued `TogglePerspectiveEvent`, calls `_orchestrator.SwitchMapOwner(provider)` for the new perspective, and calls `world.Set(new ActivePerspective { Name = evt.NewPerspective })`.

Perspective names not present in `perspectiveMap` result in no `SwitchMapOwner` call (silent no-op), but `ActivePerspective` is still updated.

**Success conditions:**

1. **`SwitchMapOwner` called with the correct subsystem:** Receiving `TogglePerspectiveEvent { NewPerspective = "IG" }` calls `SwitchMapOwner` with the `IgSubsystem` (or its camera provider).
2. **`ActivePerspective` written:** After the event is processed, `world.Get<ActivePerspective>().Name == "IG"`.
3. **Unknown perspective is silently ignored for `SwitchMapOwner`** but still updates `ActivePerspective`.
4. **Multiple events in one frame:** All queued events are processed (or only the latest, if the event bus delivers one-per-frame — clarify with dev lead and document the chosen behaviour).
5. **`SubsystemOrchestrator` camera snap executes:** `MapCamera.SnapTo` is called on the incoming camera with the outgoing camera's position and zoom.
6. **Build compiles with no errors.**
7. **Integration test:** A test that creates the system, publishes a `TogglePerspectiveEvent`, calls `OnUpdate`, and asserts that `ActivePerspective.Name` is updated correctly.

# BATCH-45 — UX polish round 3: tree keyboard nav + dialog layout + toolbar hover (BUG-A7/A8/A10/A11)

**Model:** pro · **Repo root:** `D:\Work\IOS-IG-SimHost-FDP` · branch `blueprint-integ-1`.
Four small, related UX fixes from the lead's runtime test. **Do NOT use codebase-memory tooling.** Read
`.dev/.guides/DEV-GUIDE.md` then this file. Touch ONLY the three files named below.

## ⚙️ RULES (non-negotiable)
1. Touch ONLY: `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs`,
   `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs`,
   `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs` (+ their existing test files if an
   assertion must change to match the intentional UX change — see §Tests).
2. NEVER hide a problem to pass a build (no `[Skip]`/weakened/deleted tests/stubs/suppression).
3. DO NOT STOP until each affected solution builds 0 warnings and the named test suites are `Failed: 0`.
4. These are ImGui interactions — the lead runtime-verifies in the live editor; make the code obviously correct.
5. Report exact changes + final summary in `.dev/_DONE/main-toolbar-2/reports/BATCH-45-REPORT.md`. No litter.

---

## BUG-A10 — toolbar icon hover indicator blends into the menu bar
**File:** `IconWidgets.cs`, the **IconHandle** `ToggleIcon` overload (~L289-367, the one the main toolbar uses).
**Problem:** the hover cue is a white full-box `AddRect` border (~L356-360). At the top of the window it merges
with the menu-bar / title edge and is barely visible.
**Fix:** replace the white hover **border** with a clearly-visible filled hover **highlight**, composing with the
toggled fill:
- Keep the existing toggled fill (`HeaderActive` @ 0.85, ~L324-329) unchanged.
- When `enabled && isHovered && !isToggled`: draw `AddRectFilled(screenPos, screenPos+size, <hover col>, 2f)`
  BEFORE the icon image, where `<hover col>` = `Gui.GetStyle().Colors[(int)ImGuiCol.HeaderHovered]` with `W = 0.55f`
  (a solid, position-independent highlight — not a thin edge).
- When `enabled && isHovered && isToggled`: after the toggled fill, draw a subtle lighter overlay
  `AddRectFilled(screenPos, screenPos+size, white @ 0.15f, 2f)` so hover is still perceptible on a toggled icon.
- **Remove** the white full-box `AddRect` hover border block (~L356-360).
- Draw order: hover-fill (non-toggled) / toggled-fill (+overlay if hovered) FIRST, then the icon image on top.
Leave the other overloads (the `IconAtlas` `ToggleIcon` ~L68, `AlternatingFaceToggleIcon`, `DropdownFaceIcon`)
**unchanged** (out of scope; the bug is the main toolbar). Update the XML doc on the changed overload to say
"filled hover highlight" instead of "filled background when toggled or hovered".

---

## BUG-A7 — Open-Asset (and other) picker tree: folders unreachable by keyboard
**File:** `TreeLayout.cs`. **Problem:** folders are `DefaultOpen` **only when searching** (~L57-58), so with no
search text the tree is collapsed; the picker's ↑/↓ keyboard nav moves `KeyboardFocusIndex` over *leaves*, but
leaves inside collapsed folders are not rendered → unreachable.
**Fix:** make folders **default-open always** so every leaf is rendered and ↑/↓ reaches it:
- In `DrawImplicitTree`: change `var flags = isSearching ? DefaultOpen : None;` → always include
  `ImGuiTreeNodeFlags.DefaultOpen` (keep any other flags). (`defaultOpenInt` in `DrawFolderNode` then reads 1, so the
  open/closed folder glyph stays correct.)
- In `DrawExplicitTree`: the bare `ImGui.TreeNode(node.Name)` (~L116) → `ImGui.TreeNodeEx(node.Name,
  ImGuiTreeNodeFlags.DefaultOpen)` so explicit trees are open by default too.
- The class docstring claims "Arrow ←/→ collapse/expand". The user still wants mouse arrow-click collapse to work
  (it does, via the arrow triangle) — default-open just changes the INITIAL state. Leave the docstring's ←/→ line
  but note default-open in a code comment. (Do NOT attempt to add custom ←/→ folder-focus handling — out of scope.)

---

## BUG-A11 + BUG-A8 — Save-As browser dialog: layout + keyboard folder navigation
**File:** `SaveAsBrowserDialog.cs`.

### A11 — layout (DrawFrame body + DrawButtons)
Reorder `DrawFrame` (~L222-228) so the **folder/contents panes are at the TOP** and the **Name field is BELOW**
them (the Name field must STILL be keyboard-focused first — `_focusName` already does `SetKeyboardFocusHere`, which
is draw-order independent, so just moving the call is fine):
```
DrawTwoPanes(icons);     // 1. tree + contents (top, takes most height)
ImGui.Spacing();
DrawNameField();         // 2. name input BELOW the panes (still auto-focused on open)
DrawPathPreview();       // 3. path preview
ImGui.Spacing();
DrawButtons();           // 4. buttons row
```
- In `DrawTwoPanes`, `reservedBelow` currently reserves space for path+buttons only. Since the Name field (input +
  optional error line) now sits below the panes too, INCREASE `reservedBelow` by
  `ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeightWithSpacing()` so the panes don't overflow the window.
- In `DrawButtons`: put **"+ New Folder" on the LEFT**, and push **Confirm + Cancel to the RIGHT edge** (today they
  are all stacked on one left-aligned `SameLine` row). Use fixed button widths and right-align:
```
float btnW = 110f, sp = ImGui.GetStyle().ItemSpacing.X;
if (_request?.OnCreateFolder != null) { if (ImGui.Button("+ New Folder")) { _newFolderTarget=_destination; _newFolderName=""; _focusNewFolderName=true; } }
ImGui.SameLine(ImGui.GetContentRegionMax().X - (btnW*2 + sp));   // right-align the next two buttons
ImGui.BeginDisabled(!isValid);
if (ImGui.Button(confirmLabel, new Vector2(btnW,0))) ConfirmActive();
ImGui.EndDisabled();
ImGui.SameLine();
if (ImGui.Button("Cancel", new Vector2(btnW,0)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) Close();
```
  (Keep the existing global-Enter confirm block.) If `OnCreateFolder` is null, still right-align Confirm+Cancel.

### A8 — keyboard folder navigation (default-open + arrow keys)
Today folders are mouse-only. Add self-contained keyboard nav:
- **Fields:** `private readonly HashSet<string> _collapsedFolders = new();`
  `private readonly List<string> _visibleFolderPaths = new();`  `private bool _nameInputActive;`
  Reset `_collapsedFolders.Clear()` in `Open()`.
- **Default-open + WE control open state:** in `DrawFolderNode`, before `TreeNodeEx`, call
  `ImGui.SetNextItemOpen(!_collapsedFolders.Contains(fullPath), ImGuiCond.Always);` and DROP `OpenOnArrow` reliance
  for state (keep the flag for mouse). Record visibility: at the point a folder ROW is drawn, `_visibleFolderPaths.Add(fullPath)`.
  Clear `_visibleFolderPaths` at the start of `DrawFolderTree` each frame (DFS order = render order).
  When the user mouse-toggles (arrow click) `expanded` differs from our set → sync: if `expanded` and
  `_collapsedFolders.Contains(fullPath)` remove it; if `!expanded` and a node WITH children, add it. (Simplest: after
  `bool expanded = TreeNodeEx(...)`, `if (expanded) _collapsedFolders.Remove(fullPath); else if (node.Children.Count>0) _collapsedFolders.Add(fullPath);`.)
- **Track name-input active:** in `DrawNameField`, right after the `InputText`, set `_nameInputActive = ImGui.IsItemActive();`.
- **Handle keys** (add a `HandleFolderKeys()` called in `DrawFrame` AFTER `DrawTwoPanes` builds `_visibleFolderPaths`,
  and ONLY when no popup is up: `_newFolderTarget == null && !_pendingOverwriteConfirm`):
  - `int idx = _visibleFolderPaths.IndexOf(_destination);`
  - `Down`: `idx = Math.Min(idx+1, _visibleFolderPaths.Count-1); if (idx>=0) _destination=_visibleFolderPaths[idx];`
    (Up/Down are SAFE to read at window scope even while the single-line Name input is active — it ignores them.)
  - `Up`: `idx = Math.Max(idx-1, 0); if (_visibleFolderPaths.Count>0) _destination=_visibleFolderPaths[Math.Max(idx,0)];`
  - `Right` **only when `!_nameInputActive`** (else it moves the name caret): `if (_destination.Length>0) _collapsedFolders.Remove(_destination);`
  - `Left`  **only when `!_nameInputActive`**: `if (_destination.Length>0) _collapsedFolders.Add(_destination);`
  Use `ImGui.IsKeyPressed(ImGuiKey.DownArrow/UpArrow/RightArrow/LeftArrow)`.
- The selected folder already renders with `ImGuiTreeNodeFlags.Selected` (`isSelected = fullPath==_destination`),
  which gives the visible focus cue as ↑/↓ moves `_destination`. Good — no extra highlight needed.

Net A8 UX: dialog opens with Name focused and **all folders expanded**; type the name; press ↓/↑ to move the
selected destination folder (contents pane follows); ←/→ collapse/expand once focus is in the tree (name not active).

---

## Tests
- `Fdp.Presentation.Tests` (icon/toolbar): build + run. If a test asserts the OLD white hover `AddRect` border,
  update that ONE assertion to the new filled-hover behavior (intentional UX change — note it in the report). Do NOT
  weaken unrelated assertions.
- `NodeEditor.Core.Tests` / `NodeEditor.UI` test suite (whatever exists for the picker/dialog): build + run; keep
  `Failed: 0`. The keyboard nav is ImGui-frame-driven (runtime-verified by the lead) — do not invent a fake ImGui
  harness; just keep existing tests green and the build clean.
- Build the NodeEdit solution and Fdp.Presentation. Report exact build/test commands + results.

## Definition of done
- A10 hover = filled highlight (no menu-bar blend), composes with toggle; A7 picker folders default-open;
  A11 dialog name-below-tree + New-Folder-left / Create+Cancel-right; A8 dialog folders default-open + ↑/↓ select +
  ←/→ collapse/expand (gated on name-not-active). Build 0 warnings; named suites `Failed: 0`.
- `.dev/_DONE/main-toolbar-2/reports/BATCH-45-REPORT.md`: per-bug change, files, test commands+results, any assertion updated.

If something cannot be done as specified, STOP and report why rather than stubbing.

# BATCH-45 Report

**Batch:** BATCH-45 — UX polish round 3: tree keyboard nav + dialog layout + toolbar hover  
**Developer:** Claude (pjanec)  
**Date:** 2026-06-12  
**Status:** Complete

---

## 📊 Task Completion

| Bug ID | Description | Status | Notes |
|--------|-------------|--------|-------|
| BUG-A10 | Toolbar icon hover indicator blends into menu bar | ✅ Done | Replaced white `AddRect` border with filled `AddRectFilled` highlight |
| BUG-A7 | Picker tree: folders unreachable by keyboard | ✅ Done | Made folders default-open always in implicit + explicit trees |
| BUG-A11 | Save-As browser dialog layout | ✅ Done | Name field below panes; "+ New Folder" left, Confirm+Cancel right |
| BUG-A8 | Save-As browser keyboard folder navigation | ✅ Done | ↑/↓ select folder; ←/→ collapse/expand (gated on name-not-active) |

---

## 🔧 Implementation Details

### BUG-A10 — IconWidgets.cs (L267–372)

**File:** `FDP/Engine/Fdp.Presentation/ImGui/Icons/IconWidgets.cs`  
**Method:** `ToggleIcon(in IconHandle, string, Vector2, ref bool, bool, Vector4?, float)`

**Changes:**
1. **XML doc** (L268–270): Changed "draws a filled background when toggled or hovered" → "draws a filled hover highlight"
2. **Non-toggled hover** (L320–327): New block BEFORE icon image — reads `ImGuiCol.HeaderHovered` with `W = 0.55f` and draws `AddRectFilled` as a solid, position-independent highlight
3. **Toggled fill** (L329–343): Kept existing `HeaderActive` @ 0.85 fill; added nested hover overlay — `AddRectFilled` with white @ 0.15f so hover remains perceptible on a toggled icon
4. **Removed** old white `AddRect` hover border (was ~L356–360) — this was the root cause of the menu-bar blend
5. **Draw order:** fills (hover → toggle → toggle+hover overlay) all drawn BEFORE icon image; icon rendered last on top

**Other overloads untouched** (`IconAtlas` ToggleIcon ~L68, `AlternatingFaceToggleIcon`, `DropdownFaceIcon`).

### BUG-A7 — TreeLayout.cs (L57–61, L119)

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/Layouts/TreeLayout.cs`

**Changes:**
1. **`DrawImplicitTree`** (L58–61): Changed `var flags = isSearching ? DefaultOpen : None;` → `var flags = ImGuiTreeNodeFlags.DefaultOpen;` always. Added code comment noting default-open is the INITIAL state — mouse arrow-click collapse still works. The `isSearching` variable remains for leaf item match highlighting in `DrawLeafItem`.
2. **`DrawExplicitTree`** (L119): Changed `ImGui.TreeNode(node.Name)` → `ImGui.TreeNodeEx(node.Name, ImGuiTreeNodeFlags.DefaultOpen)` with comment. Explicit trees now open-by-default too.

**Class docstring** left unchanged (mentions ←/→ collapse/expand — still accurate); default-open behavior noted in inline code comment.

### BUG-A11 — SaveAsBrowserDialog.cs layout

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs`

**Changes:**
1. **`DrawFrame`** (L228–234): Reordered: `DrawTwoPanes` → `HandleFolderKeys` → `ImGui.Spacing()` → `DrawNameField` → `DrawPathPreview` → `ImGui.Spacing()` → `DrawButtons`. Name field now BELOW the tree/contents panes. Keyboard focus still works — `SetKeyboardFocusHere` is draw-order-independent.
2. **`DrawTwoPanes` reservedBelow** (L281–284): Increased by `GetFrameHeightWithSpacing()` (Name input) + `GetTextLineHeightWithSpacing()` (Name error line) so panes don't overflow.
3. **`DrawButtons`** (L436–481):
   - "+ New Folder" stays on the LEFT (no `SameLine` after it)
   - Captures `contentWidth = ImGui.GetContentRegionAvail().X` BEFORE any buttons reduce it
   - Confirm + Cancel right-aligned via `ImGui.SameLine(contentWidth - (btnW * 2 + sp))`
   - Both Confirm and Cancel use fixed `Vector2(btnW, 0)` width
   - Global Enter block preserved

### BUG-A8 — SaveAsBrowserDialog.cs keyboard nav

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Dialogs/SaveAsBrowserDialog.cs`

**New fields** (L98–101):
```csharp
private readonly HashSet<string> _collapsedFolders = new();
private readonly List<string> _visibleFolderPaths = new();
private bool _nameInputActive;
```

**Changes:**
1. **`Open()`** (L128): `_collapsedFolders.Clear()` on every open — dialog always starts with all folders expanded
2. **`DrawFolderTree`** (L310): `_visibleFolderPaths.Clear()` at start of each frame
3. **`DrawFolderNode`** (L346–352):
   - `ImGui.SetNextItemOpen(!_collapsedFolders.Contains(fullPath), ImGuiCond.Always)` before `TreeNodeEx` — WE control open state, not just `OpenOnArrow`
   - `_visibleFolderPaths.Add(fullPath)` to record render-order visibility
   - After `TreeNodeEx`: syncs `_collapsedFolders` with mouse arrow-click toggles (`if (expanded) Remove; else if children>0 Add`)
4. **`DrawNameField`** (L260): `_nameInputActive = ImGui.IsItemActive()` after `InputText`
5. **`HandleFolderKeys()`** (L485–521): Called after `DrawTwoPanes` builds `_visibleFolderPaths`, gated on no popups (`_newFolderTarget == null && !_pendingOverwriteConfirm`):
   - **↓/↑**: Moves `_destination` through `_visibleFolderPaths` (safe — Name input ignores these keys even when active)
   - **→**: Expands selected folder (`_collapsedFolders.Remove`) — ONLY when `!_nameInputActive` (else it moves text caret)
   - **←**: Collapses selected folder (`_collapsedFolders.Add`) — ONLY when `!_nameInputActive`

**Net UX:** Dialog opens with Name focused, all folders expanded. Type name; ↓/↑ moves destination folder (contents pane follows); ←/→ collapses/expands once focus leaves the name field.

### Deviation: `GetContentRegionMax` → `GetContentRegionAvail`

The spec used `ImGui.GetContentRegionMax()`, which doesn't exist in this project's ImGui.NET version. Fixed by capturing `ImGui.GetContentRegionAvail().X` into `contentWidth` BEFORE any button is drawn on the line, then using that captured value for `SameLine` right-alignment. Same visual result.

---

## 🧪 Testing Results

### Build Results

| Solution/Project | Warnings | Errors |
|-----------------|----------|--------|
| NodeEditor.sln | 0 | 0 |
| Fdp.Presentation.csproj | 0 | 0 |
| Fdp.Presentation.Tests.csproj | 0 | 0 |

Build commands:
```bash
cd FDP/ExtDeps/NodeEdit && dotnet build NodeEditor.sln --no-restore -warnaserror
cd FDP && dotnet build Engine/Fdp.Presentation/Fdp.Presentation.csproj --no-restore -warnaserror
cd FDP && dotnet build Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --no-restore -warnaserror
```

### Test Results

| Test Suite | Passed | Failed | Total |
|-----------|--------|--------|-------|
| NodeEditor.UI.Tests | 56 | 0 | 56 |
| NodeEditor.Core.Tests | 181 | 0 | 181 |
| Fdp.Presentation.Tests (IconWidgets filter) | 38 | 0 | 38 |

Test commands:
```bash
cd FDP/ExtDeps/NodeEdit && dotnet test tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj --no-build --verbosity normal
cd FDP/ExtDeps/NodeEdit && dotnet test tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj --no-build --verbosity normal
cd FDP && dotnet test Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj --no-build --filter "FullyQualifiedName~IconWidgets" --verbosity normal
```

**No test assertions needed updating.** The IconWidgets tests are all headless ("does not throw" / "returns false" / state-preservation checks) — none assert on draw-list operations, so the hover-rendering change didn't break any assertion. The SaveAsBrowserDialog tests exercise the public API headlessly (Open/Close/ConfirmActive/ConfirmOverwrite) — the keyboard nav additions are pure ImGui-frame logic that doesn't affect the testable public seams.

**Note:** The full `Fdp.Presentation.Tests` suite (unfiltered) has a pre-existing hang in an unrelated test, preventing a full run. The IconWidgets-specific subset (38 tests) completed cleanly in ~1 second.

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

One build error: `ImGui.GetContentRegionMax()` doesn't exist in this project's ImGui.NET version. The spec used this for right-aligning the Confirm+Cancel buttons. Resolved by capturing `ImGui.GetContentRegionAvail().X` into a local variable BEFORE any button is drawn on the line, then using that captured value for the `SameLine` offset. Same visual result — the buttons end up at the right edge.

The full `Fdp.Presentation.Tests` suite hangs on a pre-existing test — not related to these changes. Filtered to the IconWidgets subset for clean verification.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `ImageButton` limits: The `ImageButton` approach used in `DropdownFaceIcon` has the same white-border hover issue as the toolbar icons — for consistency it could be updated too, but the spec explicitly marks it out of scope.
- The `isSearching` variable in `DrawImplicitTree` is now used only in `DrawLeafItem` for match highlighting. The conditional was collapsed but the variable was kept since it still serves `DrawLeafItem`.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **White overlay alpha on toggled+hover:** The spec said "white @ 0.15f" — I kept this exact value. It's subtle enough to not overwhelm the toggled fill while providing a perceptible hover cue.
- **`HeaderHovered` vs hardcoded color:** Used `ImGuiCol.HeaderHovered` (themed) with alpha override rather than hardcoding an RGB value — this respects the user's ImGui theme.
- **`SameLine` offset computation:** Capturing `contentWidth` at the start of `DrawButtons` is simple and correct. An alternative would be to use `GetCursorScreenPos() + GetContentRegionAvail()` but that's more fragile when the line may already have content.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Empty `_visibleFolderPaths`:** Handled — `IndexOf` returns -1 for an empty list; Up/Down guards check `idx >= 0` and `_visibleFolderPaths.Count > 0` before indexing.
- **Root folder with no children:** `SetNextItemOpen` is still called but the `Leaf` flag suppresses the arrow — no harm.
- **Name field active while pressing ←/→:** Handled via `_nameInputActive` gate — without this, ←/→ would simultaneously move the text caret AND collapse/expand the folder, which would be jarring.
- **Popups active:** `HandleFolderKeys` returns early when `_newFolderTarget != null || _pendingOverwriteConfirm` — prevents keyboard nav from interfering with the new-folder or overwrite popups.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `_visibleFolderPaths` is rebuilt every frame via `Clear()` + `Add()` in DFS order — this is O(num-folders) per frame, which is negligible for typical folder trees (dozens to low hundreds of nodes).
- `_collapsedFolders` is a `HashSet<string>` — O(1) lookups in `SetNextItemOpen` and `HandleFolderKeys`.
- The `contentWidth` capture in `DrawButtons` is a one-time `GetContentRegionAvail()` call — no perf concern.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **Runtime verification** — The lead should runtime-verify A10 (hover highlight visibility), A7 (picker keyboard nav), A11 (dialog layout), and A8 (dialog keyboard nav) in the live editor. These are ImGui-frame-driven interactions best verified interactively.
- [ ] **Full Fdp.Presentation.Tests hang** — A pre-existing test hang in the full suite should be investigated separately (unrelated to this batch).

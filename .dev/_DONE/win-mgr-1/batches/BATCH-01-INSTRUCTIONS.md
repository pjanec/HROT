# BATCH-01: Phase 1 — Icon System Foundation

**Batch Number:** BATCH-01  
**Tasks:** WM-S101, WM-S102, WM-S103, WM-S104, WM-S105  
**Phase:** Phase 1 — Icon System Foundation  
**Estimated Effort:** 12–15 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the first batch for the `win-mgr-1` workstream. You are building the complete Icon System that will underpin all subsequent UI work. The icon system provides a texture-atlas-based set of immediate-mode GUI widgets usable throughout multi-subsystem FDP applications.

Implement all five tasks completely, write comprehensive unit tests, and submit a complete report.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/win-mgr-1/DESIGN.md` — Full architecture; focus on §3 (Feature A: Icon System).
2. **Task Details:** `.dev/win-mgr-1/TASK-DETAIL.md` — Tasks WM-S101 through WM-S105 success conditions (Phase 1 section).
3. **Onboarding:** `.dev/win-mgr-1/ONBOARDING.md` — Codebase layout overview.
4. **Developer Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches and write reports.
5. **Existing Test Fixture:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ImGuiTestFixture.cs` — Headless ImGui test context to reuse.

### Source Code Location

- **Primary Work Area:** `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/` _(create this folder)_
- **Test Project:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/Icons/` _(create this folder)_
- **Main Project:** `FDP/Toolkits/FDP.Toolkit.ImGui/FDP.Toolkit.ImGui.csproj`
- **Test Project File:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/FDP.Toolkit.ImGui.Tests.csproj`

### Report Submission

**When done, write your report to:**  
`.dev/win-mgr-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/win-mgr-1/questions/BATCH-01-QUESTIONS.md`

---

## 🎯 Tasks

### Task WM-S101: `IconAtlas` — Resource Loading, UV Parsing, Disposal

See full description and success conditions: [TASK-DETAIL.md §WM-S101](../../win-mgr-1/TASK-DETAIL.md#wm-s101-iconatlas--resource-loading-uv-parsing-disposal)

**Key delivery:**

Create `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconAtlas.cs`:
```
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

**Critical design note on testability:** `IconAtlas` construction calls `Raylib.LoadTexture` which requires a GPU context — unavailable in headless tests. Provide a **second internal constructor** (or `internal static IconAtlas CreateForTesting(float atlasWidth, float atlasHeight, float iconSize, IntPtr dummyTextureId)`) that takes pre-set atlas dimensions and a dummy `IntPtr`, avoiding Raylib entirely. Unit tests must use this test-only entry point.

**Raylib dependency:** The ImGui toolkit references `ImGui.NET` but not `Raylib_cs` today. **Check the csproj** — if Raylib is absent you will need to add it. Alternatively, if you cannot add Raylib, model `TextureId` as a plain `nint`/`IntPtr` and accept pre-loaded texture IDs from the caller (`IconAtlas(IntPtr textureId, float atlasWidth, float atlasHeight, float iconSize = 16f)`). Document your decision in the report.

**UV calculation summary:**
- Column is 1-based: `"a1"` → column index 0, `"a12"` → index 11.
- Row: `'a'`=0, `'b'`=1, … (case-insensitive).
- `uv0 = (colIndex * iconSize / atlasWidth, rowIndex * iconSize / atlasHeight)`
- `uv1 = uv0 + (iconSize / atlasWidth, iconSize / atlasHeight)`
- Malformed / null → `(Vector2.Zero, Vector2.One)` without throwing.

**Tests to write** (file: `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/Icons/IconAtlasTests.cs`):
All 10 success conditions in TASK-DETAIL.md §WM-S101. Use the test-only factory for all UV tests. At minimum verify: row parsing, column parsing, 1-based index, case insensitivity, UV1 offset, three malformed inputs, double-Dispose safety.

---

### Task WM-S102: `IconWidgets` — `InlineIcon` and `AbsoluteIcon`

See: [TASK-DETAIL.md §WM-S102](../../win-mgr-1/TASK-DETAIL.md#wm-s102-iconwidgets--inlineicon-and-absoluteicon)

**Key delivery:**

Create `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`:
```
namespace FDP.Toolkit.ImGui.Icons;
public static class IconWidgets
{
    public static void InlineIcon(IconAtlas atlas, string coordinate)
    public static void AbsoluteIcon(IconAtlas atlas, string coordinate, Vector2 screenPos)
}
```

`InlineIcon` calls `Gui.Image(...)` then `Gui.SameLine()`. `AbsoluteIcon` uses `Gui.GetWindowDrawList().AddImage(...)` at `screenPos` with no layout change. Both resolve UVs via `atlas.GetUvCoordinates(coordinate)`. Namespace uses `global using Gui = ImGuiNET.ImGui;` (already in `GlobalUsings.cs`).

**Testing:** These methods require an ImGui frame context. Use `ImGuiTestFixture` (`NewFrame()` then `Render()`). Success conditions are specification-level (visual), but write integration tests that exercise the methods inside a frame without throwing exceptions. Verify null/empty coordinate doesn't throw.

---

### Task WM-S103: `IconWidgets` — `IconButton` and `ToggleIcon`

See: [TASK-DETAIL.md §WM-S103](../../win-mgr-1/TASK-DETAIL.md#wm-s103-iconwidgets--iconbutton-and-toggleicon)

**Key delivery:**

Add to `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`:
```
public static bool IconButton(IconAtlas atlas, string id, string coordinate)
public static bool ToggleIcon(IconAtlas atlas, string id, string coordinate, ref bool isToggled)
```

Use the `InvisibleButton + ImDrawList` pattern:
1. `screenPos = Gui.GetCursorScreenPos()`
2. `clicked = Gui.InvisibleButton(id, atlas.IconSizeVec)`
3. `isHovered = Gui.IsItemHovered()`, `isPressed = Gui.IsItemActive()`
4. `drawList = Gui.GetWindowDrawList()`
5. For `ToggleIcon`: if `isToggled` → `drawList.AddRectFilled(...)` with gray `(0.3f, 0.3f, 0.3f, 1.0f)`.
6. `imagePos = isPressed ? screenPos + new Vector2(1,1) : screenPos`
7. `drawList.AddImage(atlas.TextureId, imagePos, imagePos + atlas.IconSizeVec, uv0, uv1)`
8. If `isHovered` → `drawList.AddRect(screenPos, screenPos + atlas.IconSizeVec, hover_color)`
9. If `clicked` → flip `isToggled` (for `ToggleIcon`), return `true`.
10. `IconButton` delegates to `ToggleIcon` with a local discarded `bool` dummy.

**Tests:** Unit-test the state logic (toggling ref bool, return values) using the headless ImGui fixture. WM-S103 conditions 1–8.

---

### Task WM-S104: `IconWidgets` — `AlternatingFaceToggleIcon`

See: [TASK-DETAIL.md §WM-S104](../../win-mgr-1/TASK-DETAIL.md#wm-s104-iconwidgets--alternatingfacetoggleicon)

**Key delivery:**

Add to `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`:
```
public static bool AlternatingFaceToggleIcon(
    IconAtlas atlas, string id,
    string trueCoordinate, string falseCoordinate,
    ref bool isToggled)
```

Identical structure to `ToggleIcon` but:
- Selects coordinate: `isToggled ? trueCoordinate : falseCoordinate` (evaluated **after** click flip).
- **No** `AddRectFilled` background.

**Tests:** All 6 WM-S104 conditions. Verify no `AddRectFilled` is called, correct coordinate selection, state flip, hover/press behaviour.

---

### Task WM-S105: `IconWidgets` — `DropdownFaceIcon`

See: [TASK-DETAIL.md §WM-S105](../../win-mgr-1/TASK-DETAIL.md#wm-s105-iconwidgets--dropdownfaceicon)

**Key delivery:**

Add to `FDP/Toolkits/FDP.Toolkit.ImGui/Icons/IconWidgets.cs`:
```
public static bool DropdownFaceIcon(
    IconAtlas atlas, string id,
    IReadOnlyList<string> availableCoordinates,
    ref int selectedIndex)
```

Safety clamp `selectedIndex` to 0 if out of range. Current icon: `availableCoordinates[selectedIndex]` via standard InvisibleButton+DrawList pattern. On click: `Gui.OpenPopup(popupId)`. In popup: grid of 4 icons per row using `Gui.ImageButton + Gui.PushID(i) / Gui.PopID()`. On popup selection: `selectedIndex = i`, `Gui.CloseCurrentPopup()`, return `true`. Return `false` otherwise.

**Tests:** All 8 WM-S105 conditions. Include out-of-bounds guard test and empty list test.

---

## 🧪 Test-Driven Task Progression

**This section is mandatory. Follow it exactly:**

```
For each task:
    1. READ the task description in TASK-DETAIL.md thoroughly.
    2. WRITE the unit/integration tests first (stub the implementation to fail).
    3. RUN: dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ — confirm tests FAIL (red).
    4. IMPLEMENT the feature until all tests PASS (green).
    5. RUN: dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/ — confirm passing.
    6. Only then move to the next task.
```

**Final check before submitting report:**
```
dotnet build FDP/FDP.sln
dotnet test FDP/Toolkits/FDP.Toolkit.ImGui.Tests/
```
Both must succeed with zero errors and zero test failures.

---

## 🧱 Critical Implementation Notes

1. **`global using Gui = ImGuiNET.ImGui;`** is already declared in `FDP/Toolkits/FDP.Toolkit.ImGui/GlobalUsings.cs`. Use `Gui.*` everywhere in the main project. In test files, use `ImGuiNET.ImGui` directly (or add a local alias).

2. **Raylib dependency:** Verify whether `Raylib_cs` is already referenced by `FDP.Toolkit.ImGui.csproj`. If not, use the alternative `IconAtlas` constructor signature that accepts a pre-loaded `IntPtr` texture ID and atlas dimensions (no `Raylib.LoadTexture` at construction). DocumentAlternative: add `Raylib_cs` as a PackageReference if found elsewhere in the FDP.sln that already uses it.

3. **ImGui `BeginChild`/`Begin` in tests:** When testing methods that call `Gui.GetWindowDrawList()`, wrap the call inside `Gui.Begin("test_window") ... Gui.End()` after `NewFrame()`. The test fixture provides `NewFrame()` and `Render()` — call both around each test case.

4. **No silent error swallowing:** Let exceptions propagate. Do not wrap implementation code in try-catch unless the spec explicitly requires it (which it does only for malformed `GetUvCoordinates` inputs).

5. **`IReadOnlyList<string>` for `DropdownFaceIcon`:** Use `System.Collections.Generic.IReadOnlyList<string>` — it is available via implicit usings.

---

## 📋 Report Format

Submit to `.dev/win-mgr-1/reports/BATCH-01-REPORT.md`. Use the template at `.dev/.guides/BATCH-REPORT-TEMPLATE.md` and include:

| Task ID | Status | Notes |
|---------|--------|-------|
| WM-S101 | | |
| WM-S102 | | |
| WM-S103 | | |
| WM-S104 | | |
| WM-S105 | | |

Answer all Developer Insights questions:
- **Q1:** What issues were encountered? How resolved?
- **Q2:** What weak points did you spot in the existing codebase?
- **Q3:** What design decisions did you make beyond the spec? What alternatives did you consider?
- **Q4:** What edge cases did you discover that weren't in the spec?
- **Q5:** Performance concerns or optimization opportunities?

Include the final `dotnet test` output summary.

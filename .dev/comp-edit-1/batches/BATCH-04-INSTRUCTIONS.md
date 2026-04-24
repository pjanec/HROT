# BATCH-04: Phase 4 Wiring — ComponentReflector Double-Click + Host Panel Exposure

**Batch Number:** BATCH-04
**Tasks:** TASK-CE09, TASK-CE10
**Phase:** Phase 4 — Wiring
**Estimated Effort:** 4-5 hours
**Priority:** HIGH
**Dependencies:** BATCH-01, BATCH-02, BATCH-03 (all completed)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **Developer Workflow:** `.github/skills/developer/SKILL.md`
2. **Code Standards:** `.github/skills/CODE-STANDARDS.md`
3. **Onboarding:** `.dev/comp-edit-1/ONBOARDING.md`
4. **Previous Review:** `.dev/comp-edit-1/reviews/BATCH-03-REVIEW.md`
5. **Design (Phase 4):** `.dev/comp-edit-1/DESIGN.md` — § "Phase 4: Wiring"
6. **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` — TASK-CE09, TASK-CE10

### Source Code Files to Read Before Writing Code

- **ComponentReflector (MODIFY):** `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`
- **ImGuiPropertyTree (MODIFY):** `FDP/Engine/Fdp.Presentation/ImGui/Utils/ImGuiPropertyTree.cs`
- **EntityInspectorPanel (MODIFY):** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs`
- **EntityWatchPanel (MODIFY):** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs`
- **Existing ComponentReflector tests:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/ComponentReflectorTests.cs`
- **Existing EntityInspector tests:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/EntityInspectorPanelTests.cs`
- **WindowManager:** `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`
- **ComponentEditWindow:** `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditWindow.cs` (BATCH-03)
- **IComponentEditService:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/IComponentEditService.cs`
- **ComponentEditServiceBuilder:** `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ComponentEditServiceBuilder.cs`
- **EditScope/EditPath:** `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditScope.cs`
  and `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditPath.cs`

### Build Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

dotnet build FDP/FDP.sln --no-restore

dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj

# Before submitting report:
dotnet build IOS-IG-SimHost.sln --no-restore
dotnet test IOS-IG-SimHost.sln
```

### Known pre-existing failure

`Fdp.Toolkit.Vis2D.Tests.Layers.EntityRenderLayerTests.EntityRenderLayer_HitTest_FindsClosest`
fails before and after this batch. Ignore it.

### Report Submission

When done: `.dev/comp-edit-1/reports/BATCH-04-REPORT.md`

---

## MANDATORY WORKFLOW

1. **CE09:** Modify `ImGuiPropertyTree.cs` (add `out string? doubleClickedPath` overload), then
   modify `ComponentReflector.cs` (add properties, `_editService` field, two-level detection).
   Build and run tests → all pass.
2. **CE10:** Modify `EntityInspectorPanel.cs` and `EntityWatchPanel.cs` (expose `Reflector` property).
   Build and run tests → all pass.
3. Full solution build + full test run succeeds.
4. Write and submit report.

---

## Tasks

---

### TASK-CE09: ComponentReflector Double-Click Integration

**Files to modify:**
- `FDP/Engine/Fdp.Presentation/ImGui/Utils/ImGuiPropertyTree.cs`
- `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`

**Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §TASK-CE09
**Design:** `.dev/comp-edit-1/DESIGN.md` §4.1

#### ImGuiPropertyTree.Render change

Add a new overload of `Render` with `out string? doubleClickedPath`. The existing `Render` signature
must be preserved exactly (so existing call sites do not break):

```csharp
// EXISTING (unchanged):
public static void Render(object? obj, Type? contextType = null)

// NEW overload:
public static void Render(object? obj, Type? contextType, out string? doubleClickedPath)
```

The new overload routes to a shared internal implementation. Internally, thread a
`string? _doubleClickedPath` state (or parameter) through `RenderRows` to detect double-clicks.

After each `TreeNodeEx` call in `RenderRows`, add:
```csharp
if (ImGuiApi.IsItemHovered() && ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left))
    doubleClickedPath = <the member's JSON path>;
```

**Determining JSON path inside RenderRows:** `RenderRows` does not currently track the JSON path.
You need to compute it. The simplest correct approach: pass the current `jsonPath` string as a
parameter through `RenderRows`. At root: `"$"`. For each field/property member: append `".<name>"`.
For collection elements: append `"[<i>]"`.

The path passed back in `doubleClickedPath` when a row is double-clicked must match the format
that `EditPath.Parse` can consume: for example `"$.Position"` or `"$.Targets[2].Location"`.

**Important:** `doubleClickedPath` must be `null` when no row was double-clicked. Only the first
double-click detected during the render wins; do not overwrite with a second hit.

#### ComponentReflector changes

**Add four public nullable properties:**

```csharp
public WindowManager? EditWindowManager { get; set; }
public Func<IInspectableSession?>? EditSessionGetter { get; set; }
public IComponentPickerContext? EditPickerContext { get; set; }
public string EditOwningPerspective { get; set; } = string.Empty;
```

**Add a private field (initialise once, not per-frame):**

```csharp
private readonly IComponentEditService _editService = new ComponentEditServiceBuilder().Build();
```

This requires adding `using StructEdit.Reflection;` and `using StructEdit.Core;`.

**In `DrawComponents`, modify the per-component loop** as follows:

After the `open = ImGuiApi.CollapsingHeader(label)` line and the `PopStyleColor(popColors)` call,
add Level 2 (header) double-click detection:

```csharp
bool headerDoubleClicked = ImGuiApi.IsItemHovered()
    && ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left);
```

In the `if (open && data != null)` block, change:

```csharp
if (!handled)
    ImGuiPropertyTree.Render(data, contextType: type);
```

to:

```csharp
string? doubleClickedPath = null;
if (!handled)
    ImGuiPropertyTree.Render(data, contextType: type, out doubleClickedPath);
```

After the `Unindent()` and `PopID()` lines for this component, add the window-open logic:

```csharp
if (!session.IsReadOnly
    && EditWindowManager != null
    && EditSessionGetter != null
    && data != null
    && (doubleClickedPath != null || headerDoubleClicked))
{
    string winId = $"cedit_{e.Index}_{e.Generation}_{type.FullName}";
    if (EditWindowManager.TryGetWindow(winId, out _))
    {
        EditWindowManager.FocusWindow(winId);
    }
    else
    {
        EditScope scope = doubleClickedPath != null
            ? EditScope.ForField(EditPath.Parse(doubleClickedPath))
            : EditScope.WholeComponent;
        var editSession = _editService.Open(data, type, scope);
        string title = $"Edit {type.Name} [{e.Index}]";
        EditWindowManager.RegisterWindow(new ComponentEditWindow(
            winId, title, EditOwningPerspective, editSession,
            e, type, EditSessionGetter!, EditPickerContext));
    }
}
```

**IMPORTANT placement rules:**
- The `headerDoubleClicked` assignment must appear IMMEDIATELY after `CollapsingHeader` AND after
  `PopStyleColor(popColors)`, before any other ImGui call in the same loop body. This ensures
  `IsItemHovered()` still refers to the header item.
- The window-open block must appear AFTER `ImGuiApi.PopID()` but BEFORE the next iteration.
- Do NOT change the existing `ForceExpandAll` / `ForceCollapseAll` logic.
- Do NOT break any existing test in `ComponentReflectorTests.cs`.

**Tests to write** (new test class in `FDP/Engine/Fdp.Presentation.Tests/ImGui/`
or add to the existing `ComponentReflectorTests.cs` — developer's choice):

`T-CE09a` — no-op when read-only: session.IsReadOnly=true; even if all properties are set,
no call to `EditWindowManager.RegisterWindow` is made. Verify by a fake WindowManager.

`T-CE09b` — no-op when manager null: `EditWindowManager = null`; no NullReferenceException.

`T-CE09c` — window registered on header double-click: writable session, manager set,
header double-click simulated → `RegisterWindow` is called once.

`T-CE09d` — focus on duplicate: same component double-clicked twice → second call goes to
`FocusWindow`, not `RegisterWindow`.

`T-CE09e` — deterministic ID format uses FullName: Entity(Index=3, Generation=2), type
`MyNs.SimTransform` (FullName="MyNs.SimTransform") → window ID is `"cedit_3_2_MyNs.SimTransform"`.
Test by constructing the ID string directly (no ImGui needed).

`T-CE09f` — scoped open on field-row double-click: when `ImGuiPropertyTree.Render` returns
`doubleClickedPath == "$.Position.X"`, the session is opened with
`EditScope.ForField(EditPath.Parse("$.Position.X"))` (not whole-component scope). Verify by
inspecting the scope passed to `_editService.Open` via a fake service.

**Note:** Tests that require controlling `IsMouseDoubleClicked` can use the `[Collection("ImGui Sequential")]`
fixture (see existing `ComponentReflectorTests.cs`) or use a fake/injectable `_editService`.
The simplest approach for T-CE09c/d: use a `FakeWindowManager` (implements the relevant parts of
`WindowManager` behavior) and inject it via `EditWindowManager`. For T-CE09a/b these need no ImGui.

---

### TASK-CE10: Host Wiring (EntityInspectorPanel + EntityWatchPanel)

**Files to modify:**
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs`
- `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityWatchPanel.cs`

**Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §TASK-CE10
**Design:** `.dev/comp-edit-1/DESIGN.md` §4.2

**Change is minimal:** add one `public` property to each class, exposing the existing `_reflector` field.

For `EntityInspectorPanel`:

```csharp
/// <summary>
/// The <see cref="ComponentReflector"/> used to draw component details.
/// Expose to allow host subsystems to wire up the component editor
/// (e.g. <c>panel.Reflector.EditWindowManager = ...</c>).
/// </summary>
public ComponentReflector Reflector => _reflector;
```

For `EntityWatchPanel`:

```csharp
/// <summary>
/// The <see cref="ComponentReflector"/> used to draw component details.
/// Expose to allow host subsystems to wire up the component editor
/// (e.g. <c>panel.Reflector.EditWindowManager = ...</c>).
/// </summary>
public ComponentReflector Reflector => _reflector;
```

- Do NOT change either constructor signature.
- Do NOT rename `_reflector`.
- Both properties must be `public` (host code in `Hrot.*` assemblies sets the properties through this accessor).

**Tests:**

`T-CE10a`: `new EntityInspectorPanel().Reflector` returns a non-null `ComponentReflector`.
`T-CE10b`: `new EntityWatchPanel(someEntity).Reflector` returns a non-null `ComponentReflector`.
`T-CE10c`: `panel.Reflector.EditWindowManager = null` compiles and executes (verifies the
property is truly `public` and settable). Assert `panel.Reflector.EditWindowManager == null`.
`T-CE10d`: Run all existing `EntityInspectorPanelTests` — they must all pass unchanged.

Tests go in a new file:
`FDP/Engine/Fdp.Presentation.Tests/ImGui/ReflectorExposureTests.cs`
(or add to an appropriate existing test file if the developer prefers).

---

## Testing Requirements

- **Minimum new tests:** 10 (6 CE09 + 4 CE10)
- All pre-existing tests must continue to pass (minus the known pre-existing failure)
- Tests must verify real behavior

---

## Quality Standards

- The `_editService` field must be initialized once in the constructor or as a field initializer —
  NOT inside `DrawComponents`.
- The `headerDoubleClicked` assignment must appear immediately after `CollapsingHeader`; do not
  move it later in the loop body.
- The JSON path computation in `RenderRows` must handle at least three levels: root, field
  children, and collection element children.
- `ComponentReflector` must remain `internal` (check existing access modifier). The four new
  properties are `public` but the class itself does not need to be `public`.

---

## Report Requirements

Submit `.dev/comp-edit-1/reports/BATCH-04-REPORT.md` covering:

**Q1:** How did you compute the JSON path in `RenderRows`? What edge cases did you handle?

**Q2:** Any issues encountered? How resolved?

**Q3:** Any deviations from the spec? Justify each.

**Q4:** Suggested commit message.

---

## Success Criteria

This batch is DONE when:

- [ ] `ImGuiPropertyTree.Render` overload with `out string? doubleClickedPath` added
- [ ] `ComponentReflector` has 4 new public properties + `_editService` field + two-level detection
- [ ] `EntityInspectorPanel.Reflector` and `EntityWatchPanel.Reflector` properties added
- [ ] All 10+ new CE09/CE10 tests pass
- [ ] All pre-existing tests still pass (minus known failure)
- [ ] `dotnet build FDP/FDP.sln --no-restore` exits 0 errors
- [ ] `dotnet test IOS-IG-SimHost.sln` exits 0 new failures
- [ ] Report submitted

---

## Reference

- **Task Detail:** `.dev/comp-edit-1/TASK-DETAIL.md` §§ CE09, CE10
- **Design:** `.dev/comp-edit-1/DESIGN.md` §§ 4.1, 4.2
- **Files to modify (CE09):** `ImGuiPropertyTree.cs`, `ComponentReflector.cs`
- **Files to modify (CE10):** `EntityInspectorPanel.cs`, `EntityWatchPanel.cs`
- **Existing tests to NOT break:** `ComponentReflectorTests.cs`, `EntityInspectorPanelTests.cs`

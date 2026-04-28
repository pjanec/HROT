# BATCH-04 Review

**Batch:** BATCH-04
**Reviewer:** Development Lead
**Date:** 2026-04-24
**Status:** APPROVED

---

## Issues Found

**`ComponentReflector` visibility change to `public`:** The developer elevated `ComponentReflector`
from `internal` to `public` to satisfy CS0053 (accessibility inconsistency — public property
returning internal type). This was the correct fix. The panels are `public` and expose
`public ComponentReflector Reflector => _reflector;`, so the type must be `public`. No spec violation.

**`WM` alias for `WindowManager`:** `using WM = Fdp.Presentation.WindowManager.WindowManager;`
added to avoid collision with the `WindowManager` namespace. Clean solution.

**`TryOpenEditWindow` extracted as `internal` method:** The developer extracted the edit-window
open logic into `internal void TryOpenEditWindow(...)` to allow tests to exercise it without
needing ImGui mouse state. This is a good design improvement — cleaner than the spec pseudocode
which left the test approach open. Documented and justified in the report.

---

## Code Quality

**`ImGuiPropertyTree`:**
- `Render(object?, Type?, out string?)` overload added alongside the unchanged original.
- `RenderRows` now accepts `(string jsonPath, ref string? doubleClickedPath)` as extra parameters.
  JSON path is computed: root=`"$"`, field children=`"$.FieldName"`, collection elements=`"$.Field[0]"`.
- Only the first double-click hit is stored (`if (doubleClickedPath == null && ...)`). Correct.

**`ComponentReflector`:**
- `private readonly IComponentEditService _editService;` initialized in constructor, NOT per-frame. Correct.
- `internal ComponentReflector(IComponentEditService editService)` test constructor — clean.
- `headerDoubleClicked` assigned immediately after `CollapsingHeader` and `PopStyleColor` — correct placement.
- `doubleClickedPath` received from `ImGuiPropertyTree.Render(data, type, out doubleClickedPath)`. Correct.
- `TryOpenEditWindow` checks `session.IsReadOnly` first, then null-guards, then data null-guard. Correct.
- Window ID: `$"cedit_{e.Index}_{e.Generation}_{type.FullName}"`. Correct (FullName not Name).
- Scope: `EditScope.ForField(EditPath.Parse(doubleClickedPath))` for field-row hit, `EditScope.WholeComponent` for header hit. Correct.

**CE10:**
- Both `Reflector` properties are `public ComponentReflector Reflector => _reflector;`. Correct.
- Constructors unchanged. `_reflector` field unchanged.

---

## Test Quality

**CE09:**
- T-CE09c: uses `TryOpenEditWindow(session, e, type, data, headerDoubleClicked: true, doubleClickedPath: null)` directly — verifies whole-component scope without ImGui mouse.
- T-CE09e: constructs the ID string formula and asserts equality — not an ImGui test.
- T-CE09f: passes `doubleClickedPath: "$.Position.X"` to `TryOpenEditWindow`; injects a `FakeEditService` that records the scope — verifies `EditScope.ForField` is chosen.
- T-CE09a/b: guard conditions tested directly via `TryOpenEditWindow`.

**CE10:**
- T-CE10a/b: simple `new panel.Reflector != null` assertion.
- T-CE10c: `panel.Reflector.EditWindowManager = null` — verifies public setter accessibility.
- T-CE10d: existing `EntityInspectorPanelTests` all pass unchanged.

248/249 tests pass (1 pre-existing failure). 11 new tests.

---

## Verdict

**APPROVED.** All 11 new tests pass. All pre-existing tests unchanged. `ComponentReflector` visibility change is correct and necessary. Placement of `headerDoubleClicked` is correct. All spec requirements met.

---

## Commit Message

```
feat(comp-edit-1): Phase 4 wiring - ComponentReflector double-click + host exposure (BATCH-04)

CE09 - ImGuiPropertyTree.Render: new overload with out string? doubleClickedPath.
  RenderRows tracks JSON path ("$", "$.Field", "$.Collection[0]") and detects
  IsItemHovered + IsMouseDoubleClicked per row. Only first hit captured per frame.

CE09 - ComponentReflector: 4 new public properties (EditWindowManager, EditSessionGetter,
  EditPickerContext, EditOwningPerspective). Private readonly _editService built once in ctor.
  Two-level double-click detection: Level 1 from ImGuiPropertyTree.Render doubleClickedPath,
  Level 2 from headerDoubleClicked after CollapsingHeader.
  TryOpenEditWindow: internal helper that opens ComponentEditWindow (whole-component or field
  scope) or focuses existing window. Window ID = cedit_{Index}_{Generation}_{type.FullName}.
  ComponentReflector promoted from internal to public (required by CE10 public property).

CE10 - EntityInspectorPanel.Reflector and EntityWatchPanel.Reflector: public properties
  exposing the existing _reflector field for host-subsystem wiring.
```

---

## Workstream Complete

All 10 tasks (CE01-CE10) are now implemented and tested.

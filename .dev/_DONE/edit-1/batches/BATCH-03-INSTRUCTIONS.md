# BATCH-03: Phase 2 — New Shared Panels (`SharedOrbatPanel`, `PreviewPanel`, `ZoneEditorPanel`, `SharedContextMenuPopulator`)

**Batch Number:** BATCH-03  
**Tasks:** EDIT1-N001, EDIT1-N002, EDIT1-N003, EDIT1-N004  
**Phase:** Phase 2 — New Shared Panels  
**Estimated Effort:** 7–9 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (Hrot.UI.Common project + interfaces) ✅, BATCH-02 (panel migration, PanelConstants) ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

You are creating **four entirely new panels** in `Hrot.UI.Common` — these have no ExCon  
predecessor to migrate.  The rendering logic is described in full in TASK-DETAIL.md.

All four panels must compile without any ECS, DDS, or ExCon references.  
The test bar is lower than for adapters (no ImGui render tests), but each panel must  
have at minimum 2–3 meaningful unit/logic tests.

Work through all four tasks autonomously with passing tests before writing your report.

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Design:** `.dev/edit-1/DESIGN.md` §Phase 2 (§2.A, §2.B, §2.C, §2.D)
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-N001, §EDIT1-N002, §EDIT1-N003, §EDIT1-N004
4. **Previous review:** `.dev/edit-1/reviews/BATCH-01-REVIEW.md`

### Source Code Locations

| New File | Action |
|----------|--------|
| `Hrot.UI.Common/Panels/SharedOrbatPanel.cs` | Create |
| `Hrot.UI.Common/Panels/PreviewPanel.cs` | Create |
| `Hrot.UI.Common/Panels/ZoneEditorPanel.cs` | Create |
| `Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs` | Create |

Port interfaces (already exist from BATCH-01):
- `Hrot.UI.Common/Facades/IOrbatDataProvider.cs`
- `Hrot.UI.Common/Facades/IOrbatController.cs`
- `Hrot.UI.Common/Facades/IPreviewController.cs`
- `Hrot.UI.Common/Facades/IZoneAuthoringController.cs`
- `Hrot.UI.Common/Facades/IEntityActionController.cs`
- `FDP/Toolkits/FDP.Toolkit.ImGui/Abstractions/IEntityContextMenuHandler.cs` — Contains `IContextMenuBuilder` interface (namespace: `FDP.Toolkit.ImGui.Abstractions`)

### Key APIs available in `FDP.Toolkit.ImGui.Abstractions`

```csharp
public interface IContextMenuBuilder
{
    void AddItem(string label, Action callback, bool enabled = true);
    IContextMenuBuilder BeginSubmenu(string label);
    void EndSubmenu();
    void AddSeparator();
}
```

### Run tests with

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build
dotnet build Hrot.UI.Common
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error CS" | Select-Object -Last 5

# Test
dotnet test Hrot.Map.Common.Tests --no-build
dotnet test Hrot.ExCon.Tests --no-build
```

### Important Codebase Facts

1. **`SharedOrbatPanel` uses ImGui drag-and-drop with `unsafe` code** — The `BeginDragDropSource`/`AcceptDragDropPayload` APIs require `unsafe` context for pointer operations. Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to `Hrot.UI.Common/Hrot.UI.Common.csproj` if not already present. Confine `unsafe` to the minimum scope (one method block).

2. **Panel testing strategy** — ImGui panels cannot be render-tested (no ImGui host in unit tests). Instead, test the **logic layer**:
   - For `SharedOrbatPanel`: Test that `RequestEmbark` is called with correct IDs (via mock `IOrbatController`). 
     Panels should expose `internal` testable methods where needed, or — better — structure so that the drag payload resolution can be tested independently.
   - The TASK-DETAIL specifies tests that "simulate" drop payloads. This means: call the internal "on payload received" logic directly, bypass ImGui.
   - Add `[assembly: InternalsVisibleTo("Hrot.ExCon.Tests")]` to `Hrot.UI.Common` if you need to test internal methods from that test project.

3. **`OrbatNodeViewModel`** record fields: `int EntityId`, `string Name`, `int Depth`, `bool HasChildren`, `bool IsPendingDelete` — from BATCH-01.

4. **`MapLayerState`** record: `bool Satellite`, `bool GroundUnits`, `bool AirUnits`, `bool Grid` — from BATCH-01.

5. **`SharedContextMenuPopulator`** is a `public static class` — it has NO state, NO constructor, NO ImGui calls. It only calls `IContextMenuBuilder` methods. It can be fully unit-tested without ImGui.

6. **`IContextMenuBuilder` namespace** is `FDP.Toolkit.ImGui.Abstractions`. Make sure you use this in the `using` directive for `SharedContextMenuPopulator`.

---

## Context

Phase 1 (BATCH-02) migrated existing panels. Phase 2 creates panels that have no ExCon predecessor.  
These panels form the complete set of UI surfaces the `Hrot.Editor` application will use.  
All four panels follow the same "zero-infrastructure" pattern: pure ImGui + Port interfaces only.

---

## 🎯 Batch Objectives

1. **EDIT1-N001** — `SharedOrbatPanel` — hierarchical entity tree with embarkation drag-and-drop
2. **EDIT1-N002** — `PreviewPanel` — simple Edit/Preview mode toggle
3. **EDIT1-N003** — `ZoneEditorPanel` — road network path + obstacle placement
4. **EDIT1-N004** — `SharedContextMenuPopulator` + menu structure logic

---

## ✅ Tasks

### Task 1: EDIT1-N001 — `SharedOrbatPanel`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-N001  
**Design reference:** `.dev/edit-1/DESIGN.md` §2.A

**Summary of scope:**
- Create `Hrot.UI.Common/Panels/SharedOrbatPanel.cs`
- `DrawContent(IOrbatDataProvider data, IOrbatController ctrl)`:
  - Filter text box at the top (`_filterText` field of type `string`)
  - Call `data.GetVisibleNodes(_filterText, _expandedNodes)` each frame
  - For each `OrbatNodeViewModel node`:
    - `ImGui.Indent(node.Depth * 12f)` — depth-based indentation
    - `ImGui.Selectable(node.Name)` → on click: `ctrl.SelectEntity(node.EntityId)`
    - `unsafe` block: `ImGui.BeginDragDropSource()` → `SetDragDropPayload("ORBAT_ENTITY", &id, 4)`
    - `ImGui.BeginDragDropTarget()` → `AcceptDragDropPayload("ORBAT_ENTITY")` → on non-null result + `passengerId != vehicleId`: `ctrl.RequestEmbark(passengerId, vehicleId)`
    - Right-click on node → context menu with "Disembark": `ctrl.RequestDisembark(node.EntityId)`
  - `_expandedNodes` is `HashSet<int>` maintained by the panel; toggle on arrow click

**Key constraint:** `unsafe` use must be confined to the drag-drop section only.

**Tests required (write in `Hrot.ExCon.Tests/` or `Hrot.Map.Common.Tests/`):**
1. Supply 2 `OrbatNodeViewModel` records; assert `ctrl.SelectEntity` is called with correct ID when selection triggered
2. Simulate drop (call the internal drop-handling method directly with passengerID ≠ vehicleID); assert `ctrl.RequestEmbark` called
3. Simulate drop where passengerID == vehicleID; assert `RequestEmbark` is NOT called

---

### Task 2: EDIT1-N002 — `PreviewPanel`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-N002  
**Design reference:** `.dev/edit-1/DESIGN.md` §2.B

**Summary of scope:**
- Create `Hrot.UI.Common/Panels/PreviewPanel.cs`
- `DrawContent(IPreviewController ctrl)`:
  - If `!ctrl.IsInPreviewMode`: render "▶ Enter Preview" button → `ctrl.EnterPreviewMode()`
  - If `ctrl.IsInPreviewMode`: render "■ Stop Preview" button → `ctrl.ExitPreviewMode()`
  - `ImGui.TextColored(...)` status label: "● EDIT" (green) when in edit, "● PREVIEW" (amber) when preview
- No internal state; reads `ctrl.IsInPreviewMode` each frame

**Tests required:**
1. Mock `IPreviewController.IsInPreviewMode = false`; call `HandleEnterPreview(ctrl)` (or equivalent testable method); assert `ctrl.EnterPreviewMode` called
2. Mock `IsInPreviewMode = true`; call `HandleExitPreview(ctrl)`; assert `ctrl.ExitPreviewMode` called

---

### Task 3: EDIT1-N003 — `ZoneEditorPanel`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-N003  
**Design reference:** `.dev/edit-1/DESIGN.md` §2.C

**Summary of scope:**
- Create `Hrot.UI.Common/Panels/ZoneEditorPanel.cs`
- Internal state: `_zoneName = "urban_combat_zone"`, `_roadNetworkPath = "Assets/sample_road.json"`, `_obstacleRadius = 5.0f`
- `DrawContent(IZoneAuthoringController ctrl)`:
  - `ImGui.InputText("Zone Name", ...)` → `_zoneName`
  - `ImGui.InputText("Road Network JSON", ...)` + "Apply Road Network" button → `ctrl.SetRoadNetworkPath(_zoneName, _roadNetworkPath)`
  - `ImGui.SliderFloat("Obstacle Radius (m)", ...)` clamp [1.0f, 50.0f] → `_obstacleRadius`
  - "Place LOS Obstacle" button → `ctrl.StartObstaclePlacementMode(_zoneName, _obstacleRadius)`

**Tests required:**
1. `HandleApplyRoadNetwork(ctrl)` → assert `ctrl.SetRoadNetworkPath` called with correct zone name and path
2. `HandlePlaceObstacle(ctrl)` → assert `ctrl.StartObstaclePlacementMode` called with correct radius

---

### Task 4: EDIT1-N004 — `SharedContextMenuPopulator` + `IEntityActionController`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-N004  
**Design reference:** `.dev/edit-1/DESIGN.md` §2.D

**Summary of scope:**
- Create `Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs` — `public static class`
- `public static void PopulateEntityMenu(long entityId, long tkbType, bool hasEditablePolyline, bool hasRoutePlan, IContextMenuBuilder builder, IEntityActionController actions)`:
  - `builder.AddItem("Center on Entity", () => actions.CenterOnEntity(entityId))` — always
  - `builder.AddItem("Rename...", () => actions.Rename(entityId))` — only if `entityId != 0`
  - `builder.AddItem("Edit Shape", () => actions.EditOverlay(entityId))` — only if `hasEditablePolyline`
  - `builder.AddItem("Edit Route", () => actions.EditRoute(entityId))` — only if `hasRoutePlan`
  - `builder.AddSeparator()`
  - `builder.AddItem("Delete", () => actions.DeleteEntity(entityId))`
- `public static void PopulateEmptyMapMenu(IContextMenuBuilder builder, IEntityActionController actions)`:
  - `builder.AddItem("Measurement Tool", () => actions.ActivateMeasureTool())`

**No ImGui imports** — this class only calls `IContextMenuBuilder` from `FDP.Toolkit.ImGui.Abstractions`.

**Tests required (fully testable without ImGui — use mock/stub `IContextMenuBuilder`):**
1. `PopulateEntityMenu` with `hasEditablePolyline = true, hasRoutePlan = false` → "Edit Shape" item added, "Edit Route" NOT added (track items via a list in the mock builder)
2. `entityId == 0` → "Rename..." NOT added; verify by checking items in mock builder
3. `PopulateEmptyMapMenu` → exactly 1 item added: "Measurement Tool"; `ActivateMeasureTool` invoked when callback fired
4. "Delete" item always added, callback fires `actions.DeleteEntity(entityId)` with correct id

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (N001 SharedOrbatPanel):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (N002 PreviewPanel):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (N003 ZoneEditorPanel):** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4 (N004 SharedContextMenuPopulator):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written and passing
- ✅ **ALL tests passing** (including all previous batch tests)

Do NOT ask permission to fix compile errors, run tests, or proceed. Work autonomously until all done.

---

## 🧪 Testing Requirements

- **Minimum 10 meaningful tests** across all 4 tasks
- `SharedContextMenuPopulator` tests are the highest quality bar — fully testable without ImGui
  (use a stub `IContextMenuBuilder` that collects added items)
- Panel tests can use mock `IOrbatController`, `IPreviewController`, `IZoneAuthoringController`
  and test the `Handle*` or testable logic methods
- Tests that only verify `new SharedOrbatPanel() != null` are NOT acceptable

---

## ⚠️ Quality Standards

- Zero ECS (`EntityRepository`, `ComponentSystem`, `Entity`) references in `Hrot.UI.Common/Panels/` or `Hrot.UI.Common/Menus/`
- Zero `Hrot.ExCon.*` references in `Hrot.UI.Common`
- Zero DDS or CycloneDDS references
- Zero `Newtonsoft.Json` or `System.Text.Json` in panel/menu files
- XML `<summary>` on all public types and public methods

---

## 📊 Developer Insights (Required in Report)

**Q1:** What issues did you encounter implementing each panel? How did you solve them?

**Q2:** What was the most complex part of `SharedOrbatPanel` (drag-and-drop)?  
How did you structure the testable contract around it given ImGui limitations?

**Q3:** What design decisions did you make beyond the spec?  
(e.g. how you structured the mock builder for context menu tests, any helper methods)

**Q4:** What gaps between spec and codebase reality did you find?

**Q5:** What are the highest-risk items for BATCH-04 (domain events)?

---

## 🎯 Success Criteria

- [ ] `Hrot.UI.Common/Panels/SharedOrbatPanel.cs` created, compiles, no unsafe leaking outside drag-drop method
- [ ] `Hrot.UI.Common/Panels/PreviewPanel.cs` created, compiles
- [ ] `Hrot.UI.Common/Panels/ZoneEditorPanel.cs` created, compiles
- [ ] `Hrot.UI.Common/Menus/SharedContextMenuPopulator.cs` created, no ImGui/ECS/DDS imports
- [ ] Minimum 10 unit tests written and passing
- [ ] No regressions in any existing test suite
- [ ] Report submitted to `.dev/edit-1/reports/BATCH-03-REPORT.md`

---

## 📚 Reference Materials

- **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-N001 through §EDIT1-N004
- **Design:** `.dev/edit-1/DESIGN.md` §Phase 2
- **Port interfaces:** `Hrot.UI.Common/Facades/` (all 9 interfaces from BATCH-01)
- **Shared DTOs:** `Hrot.UI.Common/Models/OrbatNodeViewModel.cs`, `MapLayerState.cs`
- **`IContextMenuBuilder` interface:** `FDP/Toolkits/FDP.Toolkit.ImGui/Abstractions/IEntityContextMenuHandler.cs`
- **Existing BATCH-02 panels** for style reference: `Hrot.UI.Common/Panels/SpawnerPanel.cs`, `PreviewPanel.cs`, etc.
- **Test project:** `Hrot.ExCon.Tests/` (already references Hrot.UI.Common via ExCon dependency)

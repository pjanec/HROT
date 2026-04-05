# BATCH-02: Phase 1 — Migrate `SpawnerPanel`, `MissionPanel`, `ConfigPanel` to `Hrot.UI.Common`

**Batch Number:** BATCH-02  
**Tasks:** EDIT1-P001, EDIT1-P002, EDIT1-P003  
**Phase:** Phase 1 — Migrate Core Panels to `Hrot.UI.Common`  
**Estimated Effort:** 7–9 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 must be complete (all Phase 0 tasks done ✅)

---

## 📋 Onboarding & Workflow

### Developer Instructions

You are migrating three existing UI panels from `Hrot.ExCon` to the new `Hrot.UI.Common`
shared library that was created in BATCH-01.  
This is a **refactor + DI rewire** — you preserve the rendering logic and panel state,  
delete the old ExCon-specific coupling, and replace it with the Port interfaces from  
`Hrot.UI.Common.Facades`.

Read the full specs before writing any code.  Work task-by-task.  
Do not stop and ask for permission — fix compile errors, run tests, and proceed autonomously.

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Design:** `.dev/edit-1/DESIGN.md` §Phase 1 (§1.A, §1.B, §1.C) + Component Map
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-P001, §EDIT1-P002, §EDIT1-P003
4. **Previous review:** `.dev/edit-1/reviews/BATCH-01-REVIEW.md` — insights about the codebase

### Source Code Locations

| File | Action |
|------|--------|
| `Hrot.ExCon/Panels/SpawnerPanel.cs` | **Move** to `Hrot.UI.Common/Panels/SpawnerPanel.cs` (delete original) |
| `Hrot.ExCon/Panels/MissionPanel.cs` | **Move** to `Hrot.UI.Common/Panels/MissionPanel.cs` (delete original) |
| `Hrot.ExCon/Panels/ConfigPanel.cs` | **Move** to `Hrot.UI.Common/Panels/ConfigPanel.cs` (delete original) |
| `Hrot.ExCon/Hrot.ExCon.csproj` | Add `<ProjectReference>` to `Hrot.UI.Common` |
| `Hrot.UI.Common/Hrot.UI.Common.csproj` | May need `allowUnsafeBlocks` if any panel uses unsafe code |

### Key Projects

- `Hrot.ExCon/Hrot.ExCon.csproj` — the panel's original home
- `Hrot.UI.Common/Hrot.UI.Common.csproj` — the destination (built in BATCH-01)
- `Hrot.ExCon.Tests/` — contains integration tests; must not break
- `Hrot.Map.Common.Tests/` — run instead of solution-level to keep tests fast

### Run tests with

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# Build only relevant projects
dotnet build Hrot.ExCon
dotnet build Hrot.UI.Common

# Run ExCon regression tests (must stay green)
dotnet test Hrot.ExCon.Tests --no-build

# Full solution build check
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error|Error" -NotMatch | Select-Object -Last 5
```

### Important Codebase Facts

1. **`MissionPanel` has hardcoded behavior IDs** (`DoctrineIdMoveToLocation = 1`, etc.) and calls
   `DoctrineRegistry` constructor directly. Both must be removed — the doctrine dropdown is now
   driven by `IMissionEditorService.GetAvailableBehaviors(entityId)`.
2. **`MissionCommitResult`** — the existing ExCon `MissionPanel` holds a reference to the ExCon-specific
   `Services.MissionCommitResult` class (which has an `ErrorCode` field). After migration, the panel
   uses `Hrot.UI.Common.Models.MissionCommitResult` (simpler record, no `ErrorCode`).
   If the panel accesses `ErrorCode`, remove that access and handle the simple `Success`/`ErrorMessage` fields.
3. **`ConfigPanel` builds JSON patches** via `Newtonsoft.Json`. After migration, the panel only calls
   `ctrl.GetCurrentConfig()` to read state and `ctrl.ApplyConfig(new MapLayerState(...))` to write.
   All JSON construction must be moved out of the panel (it belongs in the ExCon adapter, Phase 6).
   The `MapLayerState` record has 4 fields: `Satellite`, `GroundUnits`, `AirUnits`, `Grid`.
   The panel currently has more fields (`Vehicles`, `TacticalGraphics`, `RoadGraphs`, `IconScale`).
   Keep the extra fields as panel-only state not exposed via the interface (they remain in panel state
   but do not flow through `IMapConfigController` — per DESIGN.md §1.C scope constraints).
4. **`SpawnerPanel`** has an inner `TkbCatalogEntry` record and depends on `IExConLogic` for
   `StartPlacementMode(...)`. Replace `IExConLogic logic` in `DrawContent`/`HandleActivatePlacementTool`
   with `ISpawnController spawn`. No DDS payload construction belongs in the panel.
5. **No ImGui-based unit tests** — panels cannot be render-tested in unit tests. Focus on testing
   the `Handle*` logic methods (which should be callable without ImGui) and verify compilation of
   the panel in `Hrot.UI.Common` with a reference from a test project if needed.
6. **AllUnsafeBlocks**: If any panel uses unsafe code (e.g., ImGui pointer APIs), add
   `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to `Hrot.UI.Common.csproj`.

---

## Context

BATCH-01 created the Port interfaces in `Hrot.UI.Common.Facades`. This batch wires the first three
panels to those interfaces.  After this batch:
- `SpawnerPanel` lives in `Hrot.UI.Common` and speaks `ISpawnController`
- `MissionPanel` lives in `Hrot.UI.Common` and speaks `IMissionEditorService` + `IMapPickService`
- `ConfigPanel` lives in `Hrot.UI.Common` and speaks `IMapConfigController`
- `Hrot.ExCon` still compiles by adding a reference to `Hrot.UI.Common` and consuming the shared panels through the existing `ExConLogic` / `MissionEditorService` adapters

---

## 🎯 Batch Objectives

1. **EDIT1-P001** — Move `SpawnerPanel` to `Hrot.UI.Common`, change dependency from `IExConLogic` to `ISpawnController`
2. **EDIT1-P002** — Move `MissionPanel` to `Hrot.UI.Common`, replace hardcoded doctrines with `IMissionEditorService.GetAvailableBehaviors()`
3. **EDIT1-P003** — Move `ConfigPanel` to `Hrot.UI.Common`, replace JSON patch building with `IMapConfigController.ApplyConfig()`

---

## ✅ Tasks

### Task 1: EDIT1-P001 — Migrate `SpawnerPanel`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-P001  
**Design reference:** `.dev/edit-1/DESIGN.md` §1.A

**Steps:**
1. Read `Hrot.ExCon/Panels/SpawnerPanel.cs` in full.
2. Create `Hrot.UI.Common/Panels/SpawnerPanel.cs`:
   - Change namespace to `Hrot.UI.Common.Panels`
   - Replace all `IExConLogic` usage with `ISpawnController spawn`
   - `DrawContent(ISpawnController spawn, ...)` signature (pass spawn into Draw from the host)
   - `HandleActivatePlacementTool(ISpawnController spawn)` should call `spawn.StartPlacementMode(_selectedType, _initialPropertiesJson)`
   - The `TkbCatalogEntry` record can live in `Hrot.UI.Common/Panels/` or inside the panel file
   - Remove any `using Hrot.ExCon.*` or `using Hrot.NED.Messages.*` that are only needed for DDS payload construction
3. Delete `Hrot.ExCon/Panels/SpawnerPanel.cs`.
4. In `Hrot.ExCon/Hrot.ExCon.csproj`, add:
   ```xml
   <ProjectReference Include="..\Hrot.UI.Common\Hrot.UI.Common.csproj" />
   ```
5. Update any ExCon files that `using Hrot.ExCon.Panels` and reference `SpawnerPanel` to `using Hrot.UI.Common.Panels`.
6. Verify `Hrot.ExCon` and `Hrot.UI.Common` both build with zero errors.

**Tests required:**
- Verify `Hrot.UI.Common` project builds with `SpawnerPanel` and has zero references to `Hrot.ExCon` or DDS types
- Existing ExCon tests that exercise the spawner logic must continue to pass

---

### Task 2: EDIT1-P002 — Migrate `MissionPanel`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-P002  
**Design reference:** `.dev/edit-1/DESIGN.md` §1.B

**Steps:**
1. Read `Hrot.ExCon/Panels/MissionPanel.cs` in full.
2. Create `Hrot.UI.Common/Panels/MissionPanel.cs`:
   - Change namespace to `Hrot.UI.Common.Panels`
   - Replace `DrawContent(IExConLogic logic)` with `DrawContent(IMissionEditorService service, IMapPickService pick)`
   - **Remove** `private readonly string[] _behaviorIds;`, all `DoctrineIdXxx` constants,
     `DoctrineRegistry` constructor call, and the `BehaviorCatalogCapacity` constant
   - In the task-behaviour combo box rendering: call `service.GetAvailableBehaviors(_selectedEntityId)` each frame
     to populate the dropdown (cache result in a local variable inside `DrawContent` — not a field)
   - "Pick Location" button → `_pendingLocationPick = pick.PickLocationAsync()` (use `IMapPickService`)
   - "Pick Entity/Route" button → `_pendingEntityPick = pick.PickEntityAsync(null)` (use `IMapPickService`)
   - `PollPickCompletion()` (called inside `DrawContent`) checks `.IsCompleted` / `.IsFaulted`
   - Replace `MissionCommitResult` type references with `Hrot.UI.Common.Models.MissionCommitResult`
3. Delete `Hrot.ExCon/Panels/MissionPanel.cs`.
4. Update ExCon files that use the old `MissionPanel` to use the shared one.
5. Verify both projects build; ensure the ExCon's `MissionEditorService` (in `Services/`) still compiles—
   it implements the old ExCon `IMissionEditorService`, not the new shared one (that adapter work is Phase 6).

**Tests required:**
- Write a test in `Hrot.ExCon.Tests/` or `Hrot.Map.Common.Tests/` that:
  1. Constructs a `MissionPanel` from `Hrot.UI.Common.Panels`
  2. Calls `DrawContent` with a mock `IMissionEditorService` that returns `["Ambush"]` for `GetAvailableBehaviors`
  3. Asserts the internal behavior list (whatever field/property stores it) reflects `["Ambush"]`
  (If the panel stores the behavior list only inside `DrawContent` as a local, assert that `GetAvailableBehaviors` was called with the correct entityId — use a counting mock)

---

### Task 3: EDIT1-P003 — Migrate `ConfigPanel`

**Full task spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-P003  
**Design reference:** `.dev/edit-1/DESIGN.md` §1.C

**Steps:**
1. Read `Hrot.ExCon/Panels/ConfigPanel.cs` in full.
2. Create `Hrot.UI.Common/Panels/ConfigPanel.cs`:
   - Change namespace to `Hrot.UI.Common.Panels`
   - Replace constructor parameter (if any) or `DrawContent` parameter with `IMapConfigController ctrl`
   - On open/reset: call `ctrl.GetCurrentConfig()` → populate `_satelliteLayer`, `_groundUnits`, `_airUnits`, `_grid`
   - On "Send Config Patch": call `ctrl.ApplyConfig(new MapLayerState(_satelliteLayer, _groundUnits, _airUnits, _grid))`
   - **Remove** `BuildPatch()`, all `Newtonsoft.Json` references, and JSON string construction
   - Panel-only fields (`_vehicles`, `_tacticalGraphics`, `_roadGraphs`, `_iconScale`) may stay as panel state
     (they just don't flow through `IMapConfigController` — the ExCon adapter can read them separately if needed)
3. Delete `Hrot.ExCon/Panels/ConfigPanel.cs`.
4. Update ExCon files that reference the old ConfigPanel.
5. Verify both projects build.

**Tests required:**
- Write a test that calls `HandleSendConfigPatch(ctrl)` (or equivalent) with a mock `IMapConfigController`;
  assert `ctrl.ApplyConfig` was called with a `MapLayerState` that has `Satellite = false` when `_satelliteLayer` was `false`.
- Verify: zero `Newtonsoft.Json`, `System.Text.Json`, or JSON string references inside the migrated panel file.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (P001):** Implement → `dotnet build` clean ✅ → ExCon tests pass ✅
2. **Task 2 (P002):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (P003):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written and passing
- ✅ **ALL tests passing** (including previous tasks and previous batch regressions)

Do NOT ask for permission to run tests, fix errors, or move on. Work autonomously to completion.

---

## 🧪 Testing Requirements

- **Minimum 4 meaningful tests** across Tasks 2 and 3
- Tests must call Handle* methods without an ImGui render frame
- Tests that only check `new MissionPanel() != null` are NOT acceptable
- All existing ExCon tests must continue to pass

---

## ⚠️ Quality Standards

- Zero `Hrot.ExCon.*` or CycloneDDS/DDS references inside any file in `Hrot.UI.Common/Panels/`  
- Zero `Newtonsoft.Json` or `System.Text.Json` references inside `ConfigPanel.cs`  
- XML `<summary>` on all public methods/classes  
- No compiler warnings introduced in `Hrot.UI.Common` or `Hrot.ExCon`

---

## 📊 Developer Insights (Required in Report)

**Q1:** What issues did you encounter migrating each panel? How did you resolve them?

**Q2:** What ExCon coupling was harder to remove than expected?  
Were there any hidden dependencies not mentioned in the spec?

**Q3:** What design decisions did you make beyond the instructions?  
(e.g. how you structured the test mocks, any interface adjustments)

**Q4:** What gaps between TASK-DETAIL and codebase reality did you find?  
(e.g. method signatures that didn't match, missing ExCon adapter pieces)

**Q5:** What is the highest risk for BATCH-03 (new panels: SharedOrbatPanel, PreviewPanel, etc.)?

---

## 🎯 Success Criteria

- [ ] `Hrot.UI.Common/Panels/SpawnerPanel.cs` exists; `Hrot.ExCon/Panels/SpawnerPanel.cs` deleted
- [ ] `Hrot.UI.Common/Panels/MissionPanel.cs` exists; `Hrot.ExCon/Panels/MissionPanel.cs` deleted
- [ ] `Hrot.UI.Common/Panels/ConfigPanel.cs` exists; `Hrot.ExCon/Panels/ConfigPanel.cs` deleted
- [ ] `Hrot.ExCon.csproj` references `Hrot.UI.Common`
- [ ] `Hrot.UI.Common` builds with zero errors and zero new warnings
- [ ] `Hrot.ExCon` builds with zero errors
- [ ] Minimum 4 unit tests written and passing
- [ ] Zero DDS/JSON construction references inside any of the three migrated panel files
- [ ] Report submitted to `.dev/edit-1/reports/BATCH-02-REPORT.md`

---

## 📚 Reference Materials

- **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-P001, §EDIT1-P002, §EDIT1-P003
- **Design:** `.dev/edit-1/DESIGN.md` §Phase 1
- **Previous review:** `.dev/edit-1/reviews/BATCH-01-REVIEW.md`
- **Source panels:** `Hrot.ExCon/Panels/SpawnerPanel.cs`, `MissionPanel.cs`, `ConfigPanel.cs`
- **Port interfaces:** `Hrot.UI.Common/Facades/ISpawnController.cs`, `IMissionEditorService.cs`, `IMapConfigController.cs`, `IMapPickService.cs`
- **Shared DTOs:** `Hrot.UI.Common/Models/MapLayerState.cs`, `MissionCommitResult.cs`
- **Test project:** `Hrot.ExCon.Tests/`

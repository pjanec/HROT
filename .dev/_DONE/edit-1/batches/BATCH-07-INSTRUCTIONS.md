# BATCH-07: Phase 5 Zone Save + Phase 6 ExCon Adapters (Part 1)

**Batch Number:** BATCH-07  
**Tasks:** EDIT1-W002, EDIT1-X001, EDIT1-X002, EDIT1-X003  
**Phase:** Phase 5 (W002 only) + Phase 6 Part 1 (X001, X002, X003)  
**Estimated Effort:** 5–7 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (BehaviorCatalog), BATCH-02 (panel migration), BATCH-03 (SharedOrbatPanel) ✅

---

## 📋 Onboarding

### Developer Instructions

Four medium-complexity tasks:
1. **W002** — Update `ScenarioFileService.SaveScenario` to bundle zone data in the envelope
2. **X001** — Create `ExConOrbatAdapter` (new IDerRepo-backed ORBAT adapter for ExCon)
3. **X002** — Declare `ExConLogic : ISpawnController` (zero logic, just interface declaration)
4. **X003** — Update `MissionEditorService.GetAvailableBehaviors` to use `BehaviorCatalog`

No ECS systems. No MapCanvas. No unsafe code. Work task-by-task.

### Required Reading

1. **Developer workflow:** `.github/skills/developer/SKILL.md`
2. **Design:** `.dev/edit-1/DESIGN.md` §5.B (W002), §6.A (X001), §6.B (X002), §6.C (X003)
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-W002, §EDIT1-X001, §EDIT1-X002, §EDIT1-X003

---

## Context

### Current State

- `Hrot.ExCon/ExConPanelAdapters.cs` — contains temporary shim adapters (`ExConSpawnShim`, `ExConMapConfigShim`, `ExConMissionShim`, `ExConMapPickShim`) created in BATCH-02. These will be replaced in BATCH-08 (X004/X005).
- `Hrot.ExCon/ExConLogic.cs` — `public sealed class ExConLogic : IExConLogic, IMapPickService, IDisposable` (does NOT implement `ISpawnController` yet)
- `Hrot.ExCon/Services/MissionEditorService.cs` — has constructor taking `IDerRepo` + `FdpEventBus`; `GetAvailableBehaviors` currently returns hardcoded list or empty array
- `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` — currently saves only FDP entity DOM; needs to bundle zone DTO data

### Key Existing APIs

- **`IZoneManagerService.GetActiveZones()`** — search in `Hrot.Map.Common/` for this interface and return type
- **`HrotScenarioEnvelopeDto`** — search in `Hrot.Map.Common/` or `Hrot.ScenarioEditor/`; has properties for FDP entity DOM + zone data
- **`BehaviorCatalog.GetValidBehaviors(long tkbType)`** — in `Hrot.Map.Definitions/Tkb/BehaviorCatalog.cs`; returns `IReadOnlyList<string>`
- **`IDerRepo`** — from `FDP.Toolkit.DER`; check `Hrot.ExCon.csproj` — already has this reference
- **`IOrbatDataProvider`** and **`IOrbatController`** interfaces — in `Hrot.UI.Common/Facades/`
- **`IExConLogic.SendEmbarkRequest`** — may or may not exist; if it doesn't, log a not-implemented warning in `RequestEmbark`

---

## ✅ Tasks

### Task 1: EDIT1-W002 — `ScenarioFileService` Zone Save Integration

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-W002  
**File:** `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` (UPDATE)

**What to do:**
1. Open `ScenarioFileService.cs` (already has a constructor with optional `IZoneManagerService`; check the actual signature)
2. In `SaveScenario(string filePath, EntityRepository ...)` (check exact signature):
   - After serializing the FDP entity DOM, call `var zones = _zoneManagerService?.GetActiveZones()`
   - Build the full `HrotScenarioEnvelopeDto` with both `Entities` and `Zones` populated (if zone manager is non-null and zones.Count > 0)  
   - Write the envelope to disk using `HrotSerializerOptions.Default` (or whatever serialization the project uses)
3. If `ScenarioFileService` constructor does NOT have an `IZoneManagerService` parameter yet, add it (nullable, default null) as an optional parameter.
4. Update `EditorBootstrap.CreateFileService()` in `Hrot.Editor/EditorBootstrap.cs` to pass a null `IZoneManagerService` (or an actual implementation if available); this ensures compilation.
5. If `Hrot.ScenarioEditor.csproj` doesn't reference `Hrot.Map.Common`, add the reference.

**Tests:**
1. Create `ScenarioFileService` with a mock `IZoneManagerService` that returns one zone; call `SaveScenario`; deserialize output; assert `Zones` is non-null and contains expected zone
2. Create `ScenarioFileService` with null `IZoneManagerService`; save; assert `Zones` is null in envelope

---

### Task 2: EDIT1-X001 — `ExConOrbatAdapter`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-X001  
**File:** `Hrot.ExCon/Adapters/ExConOrbatAdapter.cs` (NEW)

**Constructor:** `ExConOrbatAdapter(IDerRepo repo, IExConLogic logic)`

**Implements:** `IOrbatDataProvider` + `IOrbatController`

**`IOrbatDataProvider.GetVisibleNodes(string filterText, HashSet<int> expandedNodes)`:**
1. `foreach (var entity in _repo.GetAllEntities())`
2. Filter: `entity.TryGetDescriptor<EntityInfo>(out var info)` — only entities with `EntityInfo`
3. Build `commanderId → List<entity>` lookup using `info.CommanderId`
4. Walk BFS from root entities (`info.CommanderId == 0` or sentinel)
5. Map each to `OrbatNodeViewModel(entity.EntityId, info.Name, depth, hasChildren, isPendingDelete: false)`
6. If `filterText` is non-empty, filter by `info.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase)`

**`IOrbatController` implementations:**
- `SelectEntity(int entityId)` → `_logic.SendSetSelection(entityId)` (check if `IExConLogic` has this; if different method name, use correct one; if missing, no-op + NLog warn)
- `CreateUnit(long tkbType)` → `_logic.StartPlacementMode(tkbType, null)`
- `RequestEmbark(int passengerEntityId, int vehicleEntityId)` → check `IExConLogic` for embark DDS method; if missing: no-op + log warning "ExCon embarkation not yet implemented over DDS"
- `RequestDisembark(int passengerEntityId)` → same pattern
- `ToggleExpanded(int entityId)` → local `HashSet<int>` toggle (same as EditorOrbatAdapter)

**Note:** `IDerRepo` usage — call `_repo.GetAllEntities()` (check the actual API). Do NOT import ECS types (`EntityRepository`, `ComponentSystem`).

**Tests (in `Hrot.ExCon.Tests/` or new `AdapterTests.cs` file):**
1. Create stub `IDerRepo` returning 2 entities (one parent, one child via `EntityInfo.CommanderId`); assert `GetVisibleNodes("")` count == 2, depths correct
2. Filter text "APC" matches only one entity → returns 1 node
3. Assert zero ECS imports (inspection check / dependency verification test)

---

### Task 3: EDIT1-X002 — `ExConLogic` Implements `ISpawnController`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-X002  
**File:** `Hrot.ExCon/ExConLogic.cs` (UPDATE)

**Current:** `public sealed class ExConLogic : IExConLogic, IMapPickService, IDisposable`  
**Change to:** `public sealed class ExConLogic : IExConLogic, IMapPickService, ISpawnController, IDisposable`

Verify `ExConLogic` already has:
- `StartPlacementMode(long tkbType, string? initialPropertiesJson = null)` 
- `StartAreaAuthoringMode(string styleOverrideJson = "")`
- `StartRouteAuthoringMode()`

If method signatures don't exactly match `ISpawnController`, adjust to match the interface exactly (check `Hrot.UI.Common/Facades/ISpawnController.cs` for exact signatures).

**No logic changes.** Just the interface declaration + any signature fixes.

**Tests:**
1. Verify `typeof(ExConLogic).GetInterfaces()` contains `ISpawnController` (or an existing compile-level assertion)
2. Run the full `Hrot.ExCon.Tests` suite → all 377 tests pass (regression check)

---

### Task 4: EDIT1-X003 — `MissionEditorService` Dynamic Behavior Filter

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-X003  
**File:** `Hrot.ExCon/Services/MissionEditorService.cs` (UPDATE)

**Current state:** `GetAvailableBehaviors(long entityId)` returns hardcoded or empty list.

**What to do:**
1. Ensure `IDerRepo _repo` is a constructor parameter and backing field (may already exist — check)
2. Implement `GetAvailableBehaviors`:
   ```csharp
   public IReadOnlyList<string> GetAvailableBehaviors(long entityId)
   {
       var entity = _repo.GetEntity((int)entityId);
       if (entity == null) return Array.Empty<string>();
       return BehaviorCatalog.GetValidBehaviors(entity.TkbType);
   }
   ```
3. Remove any existing hardcoded `_knownBehaviors` list.
4. Add `using Hrot.Map.Definitions.Tkb;` (for `BehaviorCatalog`) + `using Hrot.Map.Definitions;` (for `TkbEntityTypes`)
5. Check `Hrot.ExCon.csproj` for reference to `Hrot.Map.Definitions` — if missing, add it.

**Note:** `IDerRepo.GetEntity(int id)` — check the actual API. It might be `GetEntity(int)` or `GetEntityById(int)` or similar. Look at how `IDerRepo` is used elsewhere in the ExCon project.

**Tests:**
1. Stub `IDerRepo` returns entity with `TkbType = TkbEntityTypes.Insurgent`; assert `GetAvailableBehaviors` returns list containing an insurgent behavior
2. Stub `IDerRepo.GetEntity` returns null; assert returns empty list

---

## 🔄 MANDATORY WORKFLOW

1. W002: Read ScenarioFileService.cs → update → build → write tests → tests pass
2. X001: Create ExConOrbatAdapter → build → write tests → tests pass
3. X002: Update ExConLogic → build → run regression (377 tests still pass)
4. X003: Update MissionEditorService → build → write tests → tests pass

---

## 🧪 Testing Requirements

- Minimum **8 tests** total
- All existing `Hrot.ExCon.Tests` (377 tests) must still pass after X002/X003
- Tests for X001 in `Hrot.ExCon.Tests/Adapters/ExConAdapterTests.cs` (create if not exists)

---

## Build Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error CS" | Select-Object -Last 10
dotnet test Hrot.ExCon.Tests
dotnet test Hrot.Editor.Tests --no-build
```

---

## ⚠️ Quality Standards

- No ECS imports in `Hrot.ExCon/Adapters/`
- `ExConOrbatAdapter` must not import `Fdp.Kernel`, `EntityRepository`, or `ComponentSystem`
- All new types must have XML `<summary>` doc

---

## 📊 Developer Insights Required in Report

**Q1:** What was the actual `IDerRepo` entity iteration API? (`GetAllEntities()` or something else?)

**Q2:** Did `IZoneManagerService.GetActiveZones()` return type match `HrotScenarioEnvelopeDto.Zones` type?

**Q3:** Did `IExConLogic` have embark/disembark methods? What was their exact signature?

**Q4:** What project references needed to be added?

---

## 🎯 Success Criteria

- [ ] `ScenarioFileService.SaveScenario` bundles zone data
- [ ] `ExConOrbatAdapter` created in `Hrot.ExCon/Adapters/`
- [ ] `ExConLogic : ISpawnController` declared
- [ ] `MissionEditorService.GetAvailableBehaviors` uses `BehaviorCatalog` 
- [ ] All 377 ExCon tests still pass
- [ ] Minimum 8 new tests
- [ ] Report written to `.dev/edit-1/reports/BATCH-07-REPORT.md`

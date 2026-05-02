# BATCH-08: Full Composition Root Wiring + ExCon Context Menu + Integration Tests

**Batch Number:** BATCH-08  
**Tasks:** EDIT1-W001, EDIT1-X004, EDIT1-X005, EDIT1-T001, EDIT1-T002, EDIT1-T003, EDIT1-T004  
**Phase:** Phase 5 (W001), Phase 6 (X004, X005), Phase 7 (T001–T004)  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH — final batch; all remaining work  
**Dependencies:** All previous batches 01–07 ✅

---

## 📋 Onboarding

### Developer Instructions

This is the **final batch**. It covers:
1. **W001** — Wire all Editor adapters/panels/systems into `EditorApplication` composition root
2. **X004** — ExCon composition root: replace shim adapters with real ones; wire new panels
3. **X005** — ExCon `ContextMenuLogic` refactor to use `SharedContextMenuPopulator`
4. **T001–T004** — Headless integration tests (cargo, perception, zone authoring, behavior catalog)

Work task-by-task. Prioritize getting everything building cleanly before writing tests.

### Required Reading

1. **Developer workflow:** `.github/skills/developer/SKILL.md`  
2. **Design:** `.dev/edit-1/DESIGN.md` §Phase 5 (W001), §6.D (X004), §6.E (X005), §7.A–7.D (T001–T004)
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-W001, §EDIT1-X004, §EDIT1-X005, §EDIT1-T001, §EDIT1-T002, §EDIT1-T003, §EDIT1-T004
4. **Previous report:** `.dev/edit-1/reports/BATCH-07-REPORT.md`

---

## Source Code Context

### Current State After BATCH-07

**`Hrot.Editor/EditorApplication.cs`** — minimal implementation; `IEditorLogic` methods delegate to `FdpEventBus` published events; ECS systems NOT yet registered; panels NOT yet instantiated.

**`Hrot.Editor/EditorBootstrap.cs`** — static factory; creates `ScenarioFileService` only.

**`Hrot.Editor/Program.cs`** — entry point; creates Raylib window + calls EditorBootstrap + starts application loop.

**`Hrot.ExCon/ExConMock.cs`** (or equivalent composition root) — currently wires old shim adapters; will be updated in X004.

**`Hrot.ExCon/Logic/ContextMenuLogic.cs`** — hardcoded menu item list; will be refactored in X005.

**Existing panels/adapters/systems created in earlier batches:**
- Panels: `SpawnerPanel`, `MissionPanel`, `ConfigPanel`, `SharedOrbatPanel`, `PreviewPanel`, `ZoneEditorPanel` — all in `Hrot.UI.Common/Panels/`
- Systems: `EditorCargoSystem`, `EditorPerceptionSetupSystem`, `EditorZoneAuthoringSystem` in `Hrot.Editor/Systems/`
- Adapters: all 7 + context menu handler in `Hrot.Editor/Adapters/` and `Hrot.Editor/UI/`
- PerceptionMapLayer in `Hrot.Editor/Rendering/`

### Key Constraints

- W001 changes `EditorApplication.cs` — this is a composition root change. The class currently has constructor params `(ScenarioFileService, FdpEventBus, EntityRepository, ...)`. You will add the adapters/panels either as constructor params or as factory methods called from `Program.cs`.
- X004: The ExCon application instantiation lives in `ExConMock.cs` or `ExConApplication.cs`. Find it first.
- Integration tests in T001–T004 go to `Hrot.ClusterRunner.Integration.Tests/EditorAuthoringIntegrationTests.cs` (new file). They use `EditorHarness` from `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`.
- Integration tests must NOT use Raylib or DDS — headless ECS only.

---

## ✅ Tasks

### Task 1: EDIT1-W001 — Hrot.Editor Full Composition Root Wiring

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-W001 (search near line ~1060 in TASK-DETAIL.md)  
**File:** `Hrot.Editor/EditorApplication.cs` (UPDATE)

**EXPLORE FIRST:** Read `EditorApplication.cs` (full content), `EditorBootstrap.cs`, and `Program.cs` to understand the current wiring structure. Also look at `EditorHarness.cs` (integration test harness) to understand how the application is constructed in tests.

**What to implement in `EditorApplication.cs`:**

The goal is to add a `WireUp(...)` method or extend the constructor to:

1. **Register ECS systems** in the local `EntityRepository`/kernel:
   ```csharp
   // Register editor-only ECS systems
   var cargoSystem = new EditorCargoSystem();
   var perceptionSystem = new EditorPerceptionSetupSystem();
   var zoneSystem = new EditorZoneAuthoringSystem();
   // Register with world/kernel if needed
   ```
2. **Instantiate adapters** (use null-safe defaults where dependencies aren't available at test time):
   - `EditorSpawnAdapter`, `EditorMissionService`, `EditorOrbatAdapter`, `EditorMapPickAdapter`, `EditorZoneAdapter`, `EditorEntityContextMenuHandler`, `EditorPreviewAdapter`, `EditorMapConfigAdapter`
3. **Instantiate panels**:
   - `SpawnerPanel`, `MissionPanel`, `StandardOrbatPanel` (or `SharedOrbatPanel`), `ConfigPanel`, `PreviewPanel`, `ZoneEditorPanel`
4. **Wire canvas and layers**:
   - `_canvas.AddLayer(new PerceptionMapLayer(_world))`
5. **Store adapters and panels** as fields so the `DrawUI` loop can call `panel.DrawContent(...)` each frame.

**IMPORTANT CONSTRAINT:** The existing `EditorHarness`-based tests must NOT break. `EditorHarness` likely constructs `EditorApplication` directly. Do not break the existing constructor — add an optional or secondary initialization method if needed.

**Do NOT add Raylib rendering calls to** `EditorApplication` — rendering lives in `Program.cs`.

**Tests:**
- Compile-only: `Hrot.Editor` builds with zero errors after wiring
- Assert that `EditorCargoSystem`, `EditorPerceptionSetupSystem`, `EditorZoneAuthoringSystem` are registered (if the kernel exposes a query for registered systems)
- Existing `Hrot.Editor.Tests` (58 tests) must all still pass

---

### Task 2: EDIT1-X004 — ExCon Composition Root: Wire Shared Panels

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-X004  
**Files:** `Hrot.ExCon/ExConMock.cs` or equivalent (find the actual composition root file)

**EXPLORE FIRST:** 
1. Locate the ExCon composition root — search for `new SpawnerPanel()` or `new MissionPanel()` in `Hrot.ExCon/` to find which file instantiates panels
2. Read that file fully
3. Read `Hrot.ExCon/Adapters/` to see what adapters currently exist

**What to do:**
1. Replace `ExConSpawnShim` instantiation with `ExConLogic` directly (since it now implements `ISpawnController`)  
2. Replace `ExConMissionShim` with actual `MissionEditorService` wired to `IDerRepo`
3. Replace `ExConMapConfigShim` with a proper `ExConMapConfigAdapter` (create if needed)
4. Wire `ExConOrbatAdapter` as the data provider/controller for `SharedOrbatPanel`
5. If `ExConMapPickAdapter` doesn't exist, keep using the existing `ExConMapPickShim` for now

**New file if needed:** `Hrot.ExCon/Adapters/ExConMapConfigAdapter.cs`
- Constructor: `(IExConLogic logic)`
- `GetCurrentConfig()` → reads last known config or returns default `MapLayerState(true, true, true, true)`
- `ApplyConfig(MapLayerState cfg)` → calls `_logic.SendConfigPatch(json)` (same as ExConMapConfigShim does today)
- Just extract the existing shim logic to a proper class

**Tests:**
- `Hrot.ExCon.Tests` (388 tests) must all still pass after composition root changes

---

### Task 3: EDIT1-X005 — ExCon ContextMenuLogic Refactor

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-X005  
**Files:**
- `Hrot.ExCon/Adapters/JsonContextMenuBuilder.cs` (NEW)
- `Hrot.ExCon/Adapters/ExConEntityActionAdapter.cs` (NEW)
- `Hrot.ExCon/Logic/ContextMenuLogic.cs` (UPDATE)

**EXPLORE FIRST:** Read `Hrot.ExCon/Logic/ContextMenuLogic.cs` fully. Understand how `BuildMenu` currently works and how `ContextActionInvoked` is dispatched.

**`JsonContextMenuBuilder`:**
```csharp
public sealed class JsonContextMenuBuilder : IContextMenuBuilder
{
    private int _nextId = 0;
    private readonly List<ContextMenuItem> _items = new();
    private readonly Dictionary<int, Action> _callbacks = new();
    
    public void AddItem(string label, Action callback, bool enabled = true)
    {
        int id = _nextId++;
        _callbacks[id] = callback;
        _items.Add(new ContextMenuItem { Id = id, Label = label, Enabled = enabled });
    }
    
    public IContextMenuBuilder BeginSubmenu(string label) => this; // simplified
    public void EndSubmenu() { }
    public void AddSeparator() => _items.Add(new ContextMenuItem { IsSeparator = true });
    
    public IReadOnlyList<ContextMenuItem> Build() => _items;
    public IReadOnlyDictionary<int, Action> GetCallbackRegistry() => _callbacks;
}
```
Adjust field names to match whatever `ContextMenuItem` DTO actually looks like in ExCon codebase.

**`ExConEntityActionAdapter`:**
```csharp
public sealed class ExConEntityActionAdapter : IEntityActionController
{
    private readonly IExConLogic _logic;
    public ExConEntityActionAdapter(IExConLogic logic) => _logic = logic;
    
    public void CenterOnEntity(long entityId) => /* check IExConLogic for centering method or no-op */;
    public void DeleteEntity(long entityId)   => /* check IExConLogic for delete */;
    public void EditOverlay(long entityId)    => /* check IExConLogic for edit overlay */;
    public void EditRoute(long entityId)      => /* check IExConLogic for edit route */;
    public void Rename(long entityId)         => /* check IExConLogic for rename */;
    public void ActivateMeasureTool()         => /* check IExConLogic for measure tool */;
}
```
Read `IExConLogic.cs` for the exact method names. Use `_logic.Log("not implemented")` or no-ops where ExCon has no equivalent.

**`ContextMenuLogic.cs` update:**
Replace the hardcoded menu item list in `BuildMenu(IDerEntity entity)` with calls to `SharedContextMenuPopulator.PopulateEntityMenu(...)` via the `JsonContextMenuBuilder`.

**Tests:**
1. `JsonContextMenuBuilder`: add 1 item + 1 separator → `Build()` count == 2; `GetCallbackRegistry()` count == 1
2. `BuildMenu` for entity with `MapVisualOverlay` → item labelled "Edit Shape" present in built list
3. (Regression) All 388 ExCon tests still pass

---

### Task 4: EDIT1-T001 — Embarkation & Cargo Integration Tests

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-T001  
**File:** `Hrot.ClusterRunner.Integration.Tests/EditorAuthoringIntegrationTests.cs` (NEW)

**EXPLORE FIRST:** Read `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` fully to understand:
- How to create `EditorHarness`
- How to get the `EntityRepository`
- How to call `PumpFrames(int n)`
- How to create entities with specific components

**Three test methods:**
1. `Embarkation_ValidRequest_UpdatesPassengerBufferAndStripsCapabilities`
2. `Embarkation_CapacityLimitEnforced_NoMutationOnOverflow`
3. `Disembark_RestoresCapabilities`

**Pattern for each test:**
```csharp
[Test]
public void Test()
{
    using var harness = EditorHarness.Create(); // or equivalent
    var world = harness.World;
    
    // Setup: create entities with required components
    var apc = world.CreateEntity();
    world.AddComponent(apc, new PassengerBuffer());
    // ...
    
    // Act: publish event + pump
    world.Bus.Publish(new EmbarkEntityCommand { ... });
    world.Bus.SwapBuffers();
    harness.PumpFrames(1);
    
    // Assert
    ref var buffer = ref world.GetComponent<PassengerBuffer>(apc);
    Assert.That(buffer.Count, Is.EqualTo(1));
}
```

Look at existing integration tests in `Hrot.ClusterRunner.Integration.Tests/` for the exact `EditorHarness.Create()` pattern.

---

### Task 5: EDIT1-T002 — Target Memory Seeding Integration Tests

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-T002

Add to `EditorAuthoringIntegrationTests.cs`:
1. `TargetSeeding_SinglePerceiver_SeedsMemoryBuffer`
2. `TargetSeeding_NToOne_AllPerceiversReceiveTarget`
3. `TargetSeeding_OneToN_PerceiverReceivesAllTargets`

**Note:** `TargetMemory` has `fixed` arrays — requires `unsafe` context when accessing. Add `[assembly: AllowUnsafeBlocks]` or use `unsafe { }` blocks.

---

### Task 6: EDIT1-T003 — Zone Obstacle Authoring & Save Pipeline Tests

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-T003

Add to `EditorAuthoringIntegrationTests.cs`:
1. `ZoneAuthoring_ObstaclePlacement_SpawnsPhysicsCollider`
2. `ZoneAuthoring_RoadNetworkUpdate_InjectsZoneEnvironmentDataSingleton`
3. `ZoneAuthoring_FullSave_BundlesZoneDtoInEnvelope` — requires actual temp file; use `Path.GetTempFileName()` with `try/finally` to clean up

For test #2: the `sample_road.json` might not exist. Create a minimal valid road network JSON in a test fixture file, or skip this test if `RoadNetworkLoader.LoadFromJson` is hard to mock and create a stub file.

---

### Task 7: EDIT1-T004 — Behavior Catalog Filtering Tests

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-T004

These can be pure unit tests (no harness needed):
1. `BehaviorCatalog_Insurgent_ReturnsInsurgentBehaviors` — call `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.Insurgent)` and assert expected behaviors
2. `BehaviorCatalog_Civilian_ReturnsCivilianBehaviors`
3. `EditorMissionService_FiltersOutUnregisteredBehaviors` — create minimal ECS world, spawn entity with `TkbIdentity`, construct `EditorMissionService`, assert behavior filtering

Place these in `Hrot.Map.Common.Tests/BehaviorCatalogTests.cs` OR `Hrot.ClusterRunner.Integration.Tests/EditorAuthoringIntegrationTests.cs` — whichever is more convenient.

---

## 🔄 MANDATORY WORKFLOW

1. Read all relevant existing code first (EditorApplication, ExConMock, ContextMenuLogic, EditorHarness)
2. **W001:** Update EditorApplication wiring → build → Hrot.Editor.Tests (58) still pass
3. **X004:** Update ExCon composition root → build → ExCon.Tests (388) still pass
4. **X005:** Create JsonContextMenuBuilder + ExConEntityActionAdapter → update ContextMenuLogic → build → unit tests pass → regression (388) still pass
5. **T001–T002:** Create EditorAuthoringIntegrationTests.cs → 6 cargo+perception tests pass
6. **T003:** Add 3 zone tests → all pass
7. **T004:** Add 3 behavior tests → all pass
8. Final run: full solution build + all test suites

---

## 🧪 Testing Requirements

- Minimum **18 new tests** total (9 for T01-T04 integration + 6+ for X005 unit tests + compile-verification for W001/X004)
- T001–T004 integration tests must run headless (no GPU, no DDS, no Raylib)
- Total execution time for integration tests < 2 seconds

---

## Build Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error CS" | Select-Object -Last 15
dotnet test Hrot.ExCon.Tests --no-build
dotnet test Hrot.Editor.Tests --no-build
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EditorAuthoring" 
```

---

## ⚠️ Quality Standards

- W001: `EditorApplication.cs` must not import Raylib or DDS types
- X004 + X005: No ECS imports in ExCon adapters
- Integration tests (T01-T04): No DDS, no Raylib, headless ECS only  
- All new public types must have XML `<summary>` doc

---

## 📊 Developer Insights

**Q1:** What was the actual `EditorHarness` API? How are systems registered and entities created?

**Q2:** Were the `EditorCargoSystem` etc. systems auto-registered in the world or needed explicit registration in `EditorApplication`?

**Q3:** Did `ContextMenuLogic. BuildMenu` use `IDerEntity` or `Entity`? How did the existing dispatch mechanism work?

**Q4:** What was the hardest part of T003 (zone save pipeline)?

---

## 🎯 Success Criteria

- [ ] `EditorApplication.cs` wires all 8 adapters, 3 systems, 6 panels, 1 rendering layer
- [ ] `ExConMock.cs` (or equivalent) uses `ExConOrbatAdapter` + real service implementations
- [ ] `JsonContextMenuBuilder` + `ExConEntityActionAdapter` created
- [ ] `ContextMenuLogic.BuildMenu` refactored to use `SharedContextMenuPopulator`
- [ ] 9 integration tests in `EditorAuthoringIntegrationTests.cs`
- [ ] 3 behavior catalog tests
- [ ] All test suites passing: `Hrot.ExCon.Tests` (≥388), `Hrot.Editor.Tests` (≥58), `Hrot.ClusterRunner.Integration.Tests` (existing + new)
- [ ] Report written to `.dev/edit-1/reports/BATCH-08-REPORT.md`

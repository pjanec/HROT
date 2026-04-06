# BATCH-06: Phase 4 Part 2 — Complex Editor Adapter + ECS Systems + Rendering Layer

**Batch Number:** BATCH-06  
**Tasks:** EDIT1-A006, EDIT1-A009, EDIT1-A010, EDIT1-A011, EDIT1-A012  
**Phase:** Phase 4 — Hrot.Editor (Part 2)  
**Estimated Effort:** 8–10 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-04 (events), BATCH-05 (adapters) ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch creates the last 5 components of Phase 4:
1. **`EditorEntityContextMenuHandler`** — complex adapter implementing 2 interfaces
2. **`EditorCargoSystem`** — ECS system for embark/disembark execution
3. **`EditorPerceptionSetupSystem`** — ECS system for target seeding
4. **`EditorZoneAuthoringSystem`** — ECS system for zone obstacle + road network
5. **`PerceptionMapLayer`** — rendering layer drawing target-memory links

Work task-by-task. Build must succeed before moving to the next task. Do NOT stop to ask questions.

### Required Reading

1. **Developer workflow:** `.github/skills/developer/SKILL.md`
2. **Design:** `.dev/edit-1/DESIGN.md` §4.F, §4.I, §4.J, §4.K, §4.L
3. **Task specs:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A006, §EDIT1-A009, §EDIT1-A010, §EDIT1-A011, §EDIT1-A012
4. **Previous report:** `.dev/edit-1/reports/BATCH-05-REPORT.md`

---

## Source Code Context

### Existing ECS API Summary

- **`EntityRepository`** — `CreateEntity()`, `IsAlive(Entity)`, `AddComponent<T>(Entity, T)`, `GetComponent<T>(Entity)`, `GetComponentRW<T>(Entity)` (ref return), `HasComponent<T>(Entity)`, `RemoveComponent<T>(Entity)`, `RegisterComponent<T>()`, `RegisterEvent<T>()`
- **`FdpEventBus`** — `ConsumeSequence<T>(out T[] array, out int count)`, `ConsumeManaged<T>(Action<T> action)`, `PublishManaged<T>(T obj)` + `SwapBuffers()`
- **`PassengerBuffer.Passengers`** — inline array of `Entity`; access by index `[i]`; `PassengerBuffer.Capacity = 8`
- **`IsEmbarkedTag`** — `struct { Entity VehicleEntity; }`
- **`TargetMemory`** — `unsafe struct`; static `TargetMemory.AddOrUpdateTarget(ref mem, long entityId, float posX, float posY, float scoreBoost, uint tick)` 
- **`SimTransform`** — search for its definition; expect `Vector3 Position` or `Vector2 Position`
- **`ISelectionState.SelectedEntities`** — `IReadOnlyCollection<Entity>`

### `IEditorLogic` Current State

Current interface in `Hrot.Editor/IEditorLogic.cs`:
```csharp
void NewScenario();
void SaveScenario(string filePath);
void LoadScenario(string filePath);
void ActivateTool(EditorTool tool);
void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents);
IDerRepo View { get; }
Task SwitchToExternalAsync();
Task SwitchToInternalAsync();
SimHostMode CurrentMode { get; }
```

`EditorEntityContextMenuHandler.IEntityActionController` implementations reference  
`_logic.CenterOnEntity(entityId)`, `_logic.SelectEntity(entityId)`, `_logic.OpenRenameDialog(entityId)`.  
**These methods do NOT exist** on `IEditorLogic` yet.

**You must add them:**  
1. Add `void CenterOnEntity(long entityId);` to `IEditorLogic.cs`  
2. Add `void SelectEntity(long entityId);` to `IEditorLogic.cs`  
3. Add `void OpenRenameDialog(long entityId);` to `IEditorLogic.cs`  
4. Implement them in `EditorApplication.cs`:  
   - `CenterOnEntity` → `_bus.PublishManaged(new CenterOnEntityCommand { NetworkId = entityId })`  
   - `SelectEntity` → `_bus.PublishManaged(new SelectEntityCommand { NetworkId = entityId })`  
   - `OpenRenameDialog` → `_bus.PublishManaged(new OpenRenameDialogCommand { NetworkId = entityId })`  
   (If those command types don't exist, create them as simple sealed classes in `Hrot.Editor/Commands/`)

### Other Known API Facts

- **`EditorTool` enum** — check `Hrot.Editor/EditorTool.cs` for values; expect `Edit`, `Route`, `Measure`, `Select`, and potentially others.
- **`DestroyEntityCommand`** — search for it; likely in `FDP.Kernel` or `Hrot.SimHost`.
- **`NetworkIdentity`** component — search in ECS components; has `NetworkId` long.
- **`EditablePolyline`** — search for component type in Hrot.ScenarioEditor or FDP.Toolkit.
- **`RoutePlan`** — search for component type.
- **`ActorCapabilityState`** — check if it has `CanMove` and `CanShoot` flags; look in FDP.Toolkit.Behavior.
- **`PhysicsCollider`** — check component definition; ensure `CollisionLayer` field exists.
- **`PhysicsConstants.EntityCollisionLayer`** — search for `PhysicsConstants` class.
- **`ZoneEnvironmentData`** — in `FDP.Toolkit.CarKinem`; `struct { RoadNetworkBlob RoadNetwork; }`. `Hrot.Editor.csproj` may need reference to `FDP.Toolkit.CarKinem` — check first.
- **`IContextMenuBuilder.AddSeparator()`** — The `IContextMenuBuilder` interface only has `AddItem`, `BeginSubmenu`, `EndSubmenu`. **It does NOT have `AddSeparator()`**. `SharedContextMenuPopulator.PopulateEntityMenu` calls `builder.AddSeparator()` — you need to add `void AddSeparator()` to `IContextMenuBuilder` and update it.

---

## ✅ Tasks

### Task 1: EDIT1-A006 — `EditorEntityContextMenuHandler`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A006  
**Design:** `.dev/edit-1/DESIGN.md` §4.F

**Files:** `Hrot.Editor/UI/EditorEntityContextMenuHandler.cs` (new)

**Pre-work:**
1. Check `IContextMenuBuilder` in `FDP/Toolkits/FDP.Toolkit.ImGui/Abstractions/IEntityContextMenuHandler.cs` — add `void AddSeparator()` if missing.
2. Add `CenterOnEntity`, `SelectEntity`, `OpenRenameDialog` to `IEditorLogic` + `EditorApplication`.
3. Check `DestroyEntityCommand` — if missing, create `Hrot.Editor/Commands/DestroyEntityCommand.cs` with `long NetworkId`.

**Constructor:** `EditorEntityContextMenuHandler(EntityRepository repo, IEditorLogic logic, FdpEventBus bus, IMapPickService pick, ISelectionState selection)`

**Implements:** both `IEntityContextMenuHandler` and `IEntityActionController`.

**`IEntityActionController` implementations (from TASK-DETAIL spec):**
```csharp
public void CenterOnEntity(long entityId) => _logic.CenterOnEntity(entityId);
public void DeleteEntity(long entityId)   => _bus.PublishManaged(new DestroyEntityCommand { NetworkId = entityId });
public void EditOverlay(long entityId)    => { _logic.SelectEntity(entityId); _logic.ActivateTool(EditorTool.Edit); }
public void EditRoute(long entityId)      => { _logic.SelectEntity(entityId); _logic.ActivateTool(EditorTool.Route); }
public void Rename(long entityId)         => _logic.OpenRenameDialog(entityId);
public void ActivateMeasureTool()         => _logic.ActivateTool(EditorTool.Measure);
```

**`IEntityContextMenuHandler.PopulateMenu(Entity entity, IContextMenuBuilder builder)`:**
1. Guard: `if (!_repo.IsAlive(entity)) return;`
2. Read `NetworkIdentity` → `networkId` (find the exact component and field name; likely `long NetworkId`)
3. Check `HasComponent<EditablePolyline>`, `HasComponent<RoutePlan>`
4. Read `TkbIdentity` → `tkbType` (check if this component exists; field may differ)
5. Call `SharedContextMenuPopulator.PopulateEntityMenu(networkId, tkbType, hasPolyline, hasRoute, builder, actions: this)`
6. If entity has `TargetMemory`:
   - Count valid perceivers from `_selection.SelectedEntities`
   - `builder.AddSeparator()`
   - `builder.AddItem($"Mark Target for {perceiverCount} Units...", async void () => { ... })` (single pick)
   - `builder.AddItem($"Mark Area Targets for {perceiverCount} Units...", async void () => { ... })` (area pick)
   - For each pick: `await _pick.PickEntityAsync()` or `await _pick.PickAreaEntitiesAsync()`; then for each (perceiver, target) pair publish `SeedTargetCommand { Perceiver = p, Target = t, ScoreBoost = 1.0f }`

**Tests (minimum 4 in `Hrot.Editor.Tests/UI/ContextMenuHandlerTests.cs`):**
1. Entity with `EditablePolyline` → builder capture contains "Edit Shape" item
2. Entity without `EditablePolyline` and without `RoutePlan` → no "Edit Shape" or "Edit Route"
3. Dead entity (not alive) → builder is never called (zero items added)
4. `DeleteEntity(42L)` → `DestroyEntityCommand { NetworkId = 42 }` published to bus
5. Entity with `TargetMemory` + 2 `SelectedEntities` perceivers → "Mark Target for 2 Units..." label present

---

### Task 2: EDIT1-A009 — `EditorCargoSystem`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A009  
**Design:** `.dev/edit-1/DESIGN.md` §4.I

**File:** `Hrot.Editor/Systems/EditorCargoSystem.cs` (new; also creates `Hrot.Editor/Systems/` directory)

**Pattern:** Look at an existing system in `Hrot.SimHost/Systems/` for exact class declaration syntax. Expected pattern:
```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class EditorCargoSystem : ComponentSystem
{
    protected override void OnUpdate() { ... }
}
```

If `ComponentSystem` requires constructor args (like `EntityRepository world`), use whatever the existing systems use.

**`OnUpdate()` — Embark logic:**
1. `World.Bus.ConsumeSequence<EmbarkEntityCommand>(out var cmds, out int count)`
2. For each `cmd` in `cmds[0..count)`:
   1. Guard: `if (!World.IsAlive(cmd.Passenger) || !World.IsAlive(cmd.Vehicle)) continue;`
   2. Guard: `if (!World.HasComponent<PassengerBuffer>(cmd.Vehicle)) continue;`
   3. `ref var buffer = ref World.GetComponentRW<PassengerBuffer>(cmd.Vehicle);`
   4. `if (buffer.Count >= PassengerBuffer.Capacity) continue;`
   5. `buffer.Passengers[buffer.Count++] = cmd.Passenger;`
   6. If `ActorCapabilityState` exists on passenger: strip `CanMove | CanShoot` (check exact enum flags)
   7. `World.AddComponent(cmd.Passenger, new IsEmbarkedTag { VehicleEntity = cmd.Vehicle });`

**`OnUpdate()` — Disembark logic:**
1. `World.Bus.ConsumeSequence<DisembarkEntityCommand>(out var cmds2, out int count2)`
2. For each cmd: 
   1. Guard alive + has `IsEmbarkedTag`
   2. `ref var tag = ref World.GetComponentRW<IsEmbarkedTag>(cmd.Passenger);`
   3. Get vehicle via `tag.VehicleEntity`; if alive + has `PassengerBuffer`: find and remove `cmd.Passenger` from `buffer.Passengers`, decrement `buffer.Count`
   4. Restore `CanMove | CanShoot` if `ActorCapabilityState` exists
   5. `World.RemoveComponent<IsEmbarkedTag>(cmd.Passenger);`

**Tests (in `Hrot.Editor.Tests/Systems/SystemTests.cs` or similar):**
1. Embark: create vehicle with `PassengerBuffer`, publish `EmbarkEntityCommand`, run system, assert `buffer.Count == 1`
2. Embark capacity: fill to 8, publish another command, assert still `Count == 8` (not 9)
3. Disembark: embark first, disembark, assert `HasComponent<IsEmbarkedTag>(passenger) == false`

---

### Task 3: EDIT1-A010 — `EditorPerceptionSetupSystem`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A010  
**Design:** `.dev/edit-1/DESIGN.md` §4.J

**File:** `Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class EditorPerceptionSetupSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        World.Bus.ConsumeSequence<SeedTargetCommand>(out var cmds, out int count);
        for (int i = 0; i < count; i++)
        {
            ref readonly var cmd = ref cmds[i];
            if (!World.IsAlive(cmd.Perceiver) || !World.IsAlive(cmd.Target)) continue;
            if (!World.HasComponent<TargetMemory>(cmd.Perceiver)) continue;
            if (!World.HasComponent<SimTransform>(cmd.Target)) continue;

            ref var mem   = ref World.GetComponentRW<TargetMemory>(cmd.Perceiver);
            ref readonly var xfm = ref World.GetComponent<SimTransform>(cmd.Target); // check exact API

            TargetMemory.AddOrUpdateTarget(
                ref mem,
                (long)cmd.Target.PackedValue,
                xfm.Position.X, xfm.Position.Y,
                cmd.ScoreBoost,
                World.Tick);
        }
    }
}
```
Adjust `xfm.Position.X/Y` if `SimTransform.Position` is `Vector3` (use `.X` and `.Y`) or `Vector2`.

**⚠️ `unsafe` context required for `TargetMemory` if it has `fixed` fields.** The system file needs `AllowUnsafeBlocks` or `unsafe` method context. Check `Hrot.Editor.csproj` — `AllowUnsafeBlocks` may already be set; if not, add it.

**Tests:**
1. Publish `SeedTargetCommand`, run system, assert `TargetMemory.EntityIds[0] == target.PackedValue`
2. Dead perceiver → system skips silently (no exception)

---

### Task 4: EDIT1-A011 — `EditorZoneAuthoringSystem`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A011  
**Design:** `.dev/edit-1/DESIGN.md` §4.K

**Files:**
1. `Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs`
2. `Hrot.Map.Common/Components/ZoneMembership.cs` — `public sealed class ZoneMembership { public string ZoneName { get; init; } = string.Empty; }`

**`OnUpdate()`:**

*Obstacle branch:*
```csharp
World.Bus.ConsumeManaged<SpawnZoneObstacleCommand>(cmd =>
{
    var e = World.CreateEntity();
    World.AddComponent(e, new SimTransform { Position = new Vector3(cmd.Position.X, cmd.Position.Y, 0f) });
    // If PhysicsCollider + PhysicsConstants exist:
    World.AddComponent(e, new PhysicsCollider { Radius = cmd.Radius });
    World.AddComponent(e, new ZoneMembership { ZoneName = cmd.ZoneName });
});
```

*Zone config branch:*
```csharp
World.Bus.ConsumeManaged<UpdateZoneConfigCommand>(cmd =>
{
    if (string.IsNullOrEmpty(cmd.RoadNetworkPath)) return;
    var blob = RoadNetworkLoader.LoadFromJson(cmd.RoadNetworkPath);
    World.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob });
});
```

**Notes:**
- `ZoneMembership` is a managed component — use `World.AddComponent(entity, new ZoneMembership(...))` (same pattern as regular components; managed objects are stored in a separate table).
- `PhysicsCollider.CollisionLayer` — check if field exists; if `PhysicsConstants.EntityCollisionLayer` doesn't exist just use layer `1`. Do NOT crash; use best judgment.
- `ZoneEnvironmentData` is in `FDP.Toolkit.CarKinem` — `Hrot.Editor.csproj` must reference it. Read the csproj first; if `Hrot.SimHost` is already in references and `Hrot.SimHost.csproj` references `FDP.Toolkit.CarKinem`, use that transitively; otherwise add directly.
- If `RoadNetworkLoader.LoadFromJson` throws on file not found — let it propagate (caller's responsibility to validate path).

**Tests:**
1. Publish `SpawnZoneObstacleCommand`, run system, query entities with `ZoneMembership` → count == 1
2. Publish `UpdateZoneConfigCommand` with a temp JSON file, run system, assert `World.HasSingleton<ZoneEnvironmentData>() == true`

---

### Task 5: EDIT1-A012 — `PerceptionMapLayer`

**Full spec:** `.dev/edit-1/TASK-DETAIL.md` §EDIT1-A012  
**Design:** `.dev/edit-1/DESIGN.md` §4.L

**File:** `Hrot.Editor/Rendering/PerceptionMapLayer.cs`

**IMapLayer full interface:**
```csharp
public interface IMapLayer
{
    string Name { get; }
    int LayerBitIndex { get; }
    void Update(float dt);
    void Draw(RenderContext ctx);
    bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed);
    Entity? PickEntity(Vector2 worldPos);
}
```

**Implementation:**
```csharp
public sealed class PerceptionMapLayer : IMapLayer
{
    private readonly EntityRepository _world;
    
    public string Name => "Perception Links";
    public int LayerBitIndex => 9;  // or as specified in DESIGN.md
    
    public PerceptionMapLayer(EntityRepository world) => _world = world;
    
    public void Update(float dt) { }
    public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    public void Draw(RenderContext ctx)
    {
        // For each entity with TargetMemory + SimTransform:
        // Iterate _world and draw lines between perceiver and tracked targets.
        // Use Raylib.DrawLineEx for each link.
        // Zero allocation: iterate archetypes/query without LINQ.
        // Use unsafe context for fixed TargetMemory arrays.
    }
}
```

To iterate ECS entities without allocating, look at how `PerceptionBroadphaseSystem` or other systems in `Hrot.SimHost/Systems/` iterate all entities with certain components. The pattern will be:
```csharp
_world.ForEach<TargetMemory, SimTransform>((ref TargetMemory mem, ref SimTransform xfm, Entity e) =>
{
    // draw lines
});
```
OR use a stored query built in constructor. Inspect existing systems for the exact API.

For the drawing math:
- Need to convert world-space position to screen-space using `ctx.Camera` (Camera2D). Use `Raylib.GetWorldToScreen2D(worldPos, ctx.Camera)`.
- Draw: `Raylib.DrawLineEx(perceiverScreen, targetScreen, 1.5f, new Color(255, 60, 60, 160))`

**Tests (compile-level smoke test):**
1. Construct `PerceptionMapLayer`; verify it implements `IMapLayer`; no exception on construction
2. `Draw` called with an empty world — no exception

---

## 🔄 MANDATORY WORKFLOW

1. Read `IEditorLogic.cs` → add missing methods → implement in `EditorApplication` → build
2. **A006:** Implement → write tests → confirm compile + tests pass
3. **A009:** Implement → write tests → confirm compile + tests pass
4. **A010:** Implement → write tests → confirm compile + tests pass  
5. **A011:** Implement → write tests → confirm compile + tests pass
6. **A012:** Implement → write smoke test → confirm compile + tests pass

---

## 🧪 Testing Requirements

- Minimum **12 meaningful tests** across all 5 tasks
- Minimum 4 tests for A006
- Tests must use a fake `EntityRepository` or actual isolated world
- Always `_bus.SwapBuffers()` before calling `ConsumeSequence/ConsumeManaged` after publishing

**Existing test project:** `Hrot.Editor.Tests/`

---

## ⚠️ Quality Standards

- Zero DDS/CycloneDDS imports in any file
- No `Hrot.ExCon` imports
- All new classes must have XML `<summary>` doc
- `unsafe` context required anywhere `TargetMemory.fixed` arrays are accessed

---

## Build Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln 2>&1 | Select-String "error CS" | Select-Object -Last 10
dotnet test Hrot.Editor.Tests
dotnet test Hrot.ExCon.Tests --no-build
```

---

## 📊 Developer Insights Required in Report

**Q1:** What additional dependencies (`IEditorLogic` methods, component types) required pre-work?

**Q2:** Did `ComponentSystem` constructor / override approach match expectations? Any deviations from the TASK-DETAIL spec?

**Q3:** What was the exact `ForEach`/query API for iterating ECS components in `PerceptionMapLayer`?

**Q4:** Did `Hrot.Editor.csproj` need `FDP.Toolkit.CarKinem` reference?

**Q5:** Which ECS types were hardest to find (`ActorCapabilityState`, `PhysicsConstants`, `NetworkIdentity`, `SimTransform.Position`)?

---

## 🎯 Success Criteria

- [ ] 5 files created (EditorEntityContextMenuHandler, EditorCargoSystem, EditorPerceptionSetupSystem, EditorZoneAuthoringSystem, PerceptionMapLayer)
- [ ] 1 new component: `ZoneMembership.cs` in `Hrot.Map.Common/Components/`
- [ ] `IEditorLogic` extended with CenterOnEntity, SelectEntity, OpenRenameDialog
- [ ] `EditorApplication` implements new IEditorLogic methods
- [ ] `IContextMenuBuilder.AddSeparator()` added if missing
- [ ] Hrot.Editor builds with zero errors
- [ ] Minimum 12 tests passing
- [ ] Report written to `.dev/edit-1/reports/BATCH-06-REPORT.md`

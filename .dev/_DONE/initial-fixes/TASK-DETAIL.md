# Task Detail — Initial Fixes

**Design Reference:** [DESIGN.md](./DESIGN.md)  
**Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## TASK-IF001: Remove VehicleState Contamination

**Design Reference:** [DESIGN.md § 1.1](./DESIGN.md#11-remove-vehiclestate-contamination)

### Scope
**In:** Delete the single line in `DescriptorMapper.MapToComponents` that unconditionally adds a `VehicleState` component.  
**Out:** Do not touch any other component assignments, TKB template logic, or vehicle-specific paths.

### Location
`Hrot.SimHost/Util/DescriptorMapper.cs` — `MapToComponents` method

### Constraints
- `VehicleState` must only be added by the TKB template, not by `DescriptorMapper`
- Do not introduce a conditional — full deletion is the correct fix
- `SimTransform` alone is sufficient for spatial data on non-vehicle entities

### Success Conditions

**SC1 — Non-vehicle entity has no VehicleState**  
*Setup:* Call `DescriptorMapper.MapToComponents` with a descriptor that has a `WorldPos` but no `TkbType` indicating a wheeled vehicle.  
*Assert:* The returned component list does NOT contain an element of type `VehicleState`.

**SC2 — Vehicle entity still receives VehicleState via TKB template**  
*Setup:* Spawn a wheeled-vehicle descriptor through the full entity creation pipeline (or assert the TKB template adds it separately).  
*Assert:* The resulting ECS entity has a `VehicleState` component.

**SC3 — All existing DescriptorMapper tests remain green** after deletion.

---

## TASK-IF002: Fix Doctrine Preemption

**Design Reference:** [DESIGN.md § 1.2](./DESIGN.md#12-fix-doctrine-preemption)

### Scope
**In:** Add `unchecked { doctrine.InstanceId++; }` in `MissionAdapterSystem` immediately before `World.SetComponent` when `ActiveDoctrineHash` changes.  
**Out:** No changes to hash comparison logic, channel arbitration, or any other system.

### Location
`Hrot.SimHost/Systems/MissionAdapterSystem.cs` — inside the `if (doctrine.ActiveDoctrineHash != doctrineId)` branch

### Constraints
- The increment MUST use `unchecked` to allow natural byte/ushort wrap-around without overflow exceptions
- The increment MUST occur before `World.SetComponent` so the new `InstanceId` is published in the same component write
- Pattern source: `UrbanCombat`'s `DoctrineIngressSystem`

### Success Conditions

**SC1 — InstanceId increments on doctrine change**  
*Setup:* Create an entity with `DoctrineState { ActiveDoctrineHash = A, InstanceId = 5 }`. Invoke `MissionAdapterSystem` with a new doctrine ID `B`.  
*Assert:* After the system runs, `DoctrineState.InstanceId == 6`.

**SC2 — InstanceId wraps around without exception**  
*Setup:* Entity with `DoctrineState { InstanceId = 255 }` (or `MaxValue` for the type). Trigger doctrine change.  
*Assert:* No `OverflowException`; `InstanceId` wraps to `0`.

**SC3 — No increment on same doctrine**  
*Setup:* Trigger `MissionAdapterSystem` with a doctrine ID that matches the current `ActiveDoctrineHash`.  
*Assert:* `InstanceId` is unchanged.

---

## TASK-IF003: Publish EntityMaster DDS Topic

**Design Reference:** [DESIGN.md § 1.3](./DESIGN.md#13-publish-entitymaster-dds-topic)

### Scope
**In:** Manually construct an `AutoCycloneTranslator<EntityMaster>` and add it to the `translators` list in `Program.cs` before `CycloneNetworkModule` is built.  
**Out:** No changes to existing translators, module registration, or DataModel.

### Location
`Hrot.SimHost/Program.cs` — in the translator registration section, before `new CycloneNetworkModule(translators, ...)`

### Constraints
- Use `AutoCycloneTranslator<EntityMaster>` (not a hand-written translator) to remain consistent with the NetworkDemo pattern
- The translator must receive the shared `ddsParticipant` and `entityMap` instances already in scope
- Topic name must be `"EntityMaster"`, partition index `0`
- Do NOT add `[FdpDescriptor]` to the DataModel class — the manual translator is the correct solution

### Success Conditions

**SC1 — EntityMaster translator is present in translators list**  
*Setup:* Inspect or integration-test the program startup path.  
*Assert:* The `translators` list contains exactly one entry whose runtime type is `AutoCycloneTranslator<EntityMaster>`.

**SC2 — IG receives entity creation event after SimHost spawns an entity**  
*Setup (integration):* Start SimHost and IG. SimHost processes a `CreateEntityRequest`.  
*Assert:* IG's `EntityMasterTranslator.OnSampleReceived` fires at least once with the matching entity ID.

---

## TASK-IF004: Fix Ghost Ownership in EntityMasterTranslator

**Design Reference:** [DESIGN.md § 2.1](./DESIGN.md#21-fix-ghost-ownership-theft)

### Scope
**In:** Change the `OwnerNodeId` assignment in `EntityMasterTranslator` from `IgNetworkConstants.LocalNodeId` to `0`.  
**Out:** No other changes to the translator or the network constants.

### Location
`Hrot.IG/Translators/EntityMasterTranslator.cs` — entity creation block, `OwnerNodeId` field assignment

### Constraints
- Value `0` is the FDP convention for "remote / no local authority"
- Never assign `IgNetworkConstants.LocalNodeId` (or any IG-local ID) to a replicated entity's `OwnerNodeId` on a ghost node
- The change must not affect any existing entity creation logic other than ownership tagging

### Success Conditions

**SC1 — Replicated entity has HasAuthority = false**  
*Setup:* Feed a sample `EntityMaster` DDS message to `EntityMasterTranslator.OnSampleReceived`.  
*Assert:* The resulting ECS entity has `NetworkAuthority.HasAuthority == false`.

**SC2 — Dead-reckoning is applied to the entity**  
*Setup (integration):* Start SimHost + IG. SimHost moves an entity.  
*Assert:* `TransformSyncSystem` updates the entity's `SimTransform` on IG — i.e., the entity is not frozen. (Can be verified by checking that `SimTransform` changes between two kernel ticks after a network position update.)

**SC3 — Existing EntityMasterTranslator unit tests remain green.**

---

## TASK-IF005: Register TransformSyncSystem

**Design Reference:** [DESIGN.md § 2.2](./DESIGN.md#22-register-transformsyncsystem)

### Scope
**In:** Call `_kernel.RegisterGlobalSystem(new TransformSyncSystem(driveFromNetwork: true))` in `IgApplication` before `_kernel.Initialize()`.  
**Out:** No other system registrations changed; no changes to `TransformSyncSystem` itself.

### Location
`Hrot.IG/IgApplication.cs` — inside `InitializeEcs()`, after existing system registrations and before `_kernel.Initialize()`

### Constraints
- `driveFromNetwork: true` is mandatory — IG is driven entirely by the network, not by local physics
- Must be registered as a **global** system (not per-world) using `RegisterGlobalSystem`
- Registration order: before `_kernel.Initialize()`

### Success Conditions

**SC1 — TransformSyncSystem is registered**  
*Setup:* Inspect the kernel's system list after `InitializeEcs()`.  
*Assert:* Exactly one `TransformSyncSystem` instance is registered with `driveFromNetwork == true`.

**SC2 — Entity SimTransform updates after NetworkPosition changes**  
*Setup:* Create an entity with `NetworkPosition { X = 10, Y = 20 }`. Tick the kernel once.  
*Assert:* `SimTransform` reflects the interpolated/lerped coordinates (non-zero movement from spawn).

---

## TASK-IF006: Fix Rogue Local Spawning in CreationTool

**Design Reference:** [DESIGN.md § 2.3](./DESIGN.md#23-fix-rogue-local-spawning)

### Scope
**In:** Refactor `CreationTool.HandleClick` to write a `CreateEntityRequest` via `IDdsWriter<CreateEntityRequest>` or `BdcCommandGateway` instead of publishing `SpawnEntityCommand` to `FdpEventBus`.  
**Out:** All other tool logic (selection, TKB type tracking) stays unchanged. Do not modify `FdpEventBus` or any other tool.

### Location
`Hrot.IG/Tools/CreationTool.cs`

### Constraints
- `CreationTool` must NOT hold a reference to `FdpEventBus` after this fix (for entity spawning purposes)
- The DDS `CreateEntityRequest` MUST include at minimum: `RequestId` (new `Guid`), `Owner` (zeroed `NodeId`), `InitialDescriptors` containing one `dtEntityMaster` union and one `dtWorldPos` union
- Coordinate mapping: screen → world position → `GeoPoint { Latitude = worldPos.Y, Longitude = worldPos.X }` (matching existing coordinate convention)
- Use constructor/property injection for `IDdsWriter<CreateEntityRequest>` — do not use a service locator

### Success Conditions

**SC1 — HandleClick writes to IDdsWriter, not FdpEventBus**  
*Setup:* Create a `CreationTool` with a mock `IDdsWriter<CreateEntityRequest>`. Call `HandleClick` at a map position.  
*Assert:* `IDdsWriter.Write` is called exactly once. `FdpEventBus.Publish<SpawnEntityCommand>` is NOT called.

**SC2 — Written request has correct structure**  
*Assert:* The `CreateEntityRequest` passed to `Write` has a non-empty `RequestId`, a zeroed `Owner`, contains a `dtEntityMaster` descriptor with the correct `TkbType`, and a `dtWorldPos` descriptor with non-zero coordinates matching the click position.

**SC3 — SimHost receives and processes the request (integration)**  
*Setup:* Start SimHost + IG. Click on the IG map.  
*Assert:* SimHost's `CreateEntityRequestSystem` processes the request and an entity appears on the map.

---

## TASK-IF007: Uncomment IOS Draw Methods

**Design Reference:** [DESIGN.md § 3.1](./DESIGN.md#31-uncomment-ios-draw-methods)

### Scope
**In:** Remove `//` comment markers from ImGui rendering code in `IosMock.DrawUI()` and all seven `Draw(IIosLogic)` methods in `Hrot.ExCon/Panels/`. Add `using ImGuiNET;` where missing.  
**Out:** No logic changes — only uncomment. Do not alter method signatures, panel data bindings, or test stubs.

### Files
| File | Change |
|---|---|
| `Hrot.ExCon/IosMock.cs` | Uncomment full `DrawUI` body; add `using ImGuiNET;` |
| `Hrot.ExCon/Panels/ConfigPanel.cs` | Uncomment `Draw` ImGui body |
| `Hrot.ExCon/Panels/DiagnosticsPanel.cs` | Uncomment `Draw` ImGui body |
| `Hrot.ExCon/Panels/InspectorPanel.cs` | Uncomment `Draw` ImGui body |
| `Hrot.ExCon/Panels/InteractionPanel.cs` | Uncomment `Draw` ImGui body |
| `Hrot.ExCon/Panels/MissionPanel.cs` | Uncomment `Draw` ImGui body |
| `Hrot.ExCon/Panels/OrbatPanel.cs` | Uncomment `Draw` ImGui body |
| `Hrot.ExCon/Panels/SpawnerPanel.cs` | Uncomment `Draw` ImGui body |

### Constraints
- Do not restructure or rewrite ImGui calls — only uncomment
- Ensure `ImGui.BeginMainMenuBar()` / `ImGui.EndMainMenuBar()` and `ImGui.DockSpaceOverViewport()` are present in `IosMock.DrawUI()`
- All panel `Draw()` calls must be inside the `DockSpace` scope

### Success Conditions

**SC1 — IosMock.DrawUI builds without error after uncomment**

**SC2 — Each panel `Draw` method contains at least one `ImGui.*` call** (verify by grep or compilation)

**SC3 — All existing IOS unit tests remain green** (panels have test-friendly boundaries via `IIosLogic` mock injection — uncomment must not break them)

**SC4 — IOS application displays a docked ImGui layout at startup** with main menu bar, ORBAT, Mission, Interaction, Inspector, Spawner, Config, and Diagnostics panels.

---

## TASK-IF008: Connect IG UI Panels to App Loop

**Design Reference:** [DESIGN.md § 3.2](./DESIGN.md#32-connect-ig-ui-panels-to-app-loop)

### Scope
**In:** Wire the four IG UI panels (`IgDebugPanel`, `EntityInspectorPanel`, `MiniIosPanel`, `PerformanceOverlay`) into `IgApplication`'s field declarations, `InitializeEcs()` initialization, and `Run()` render loop.  
**Out:** No changes to the panel classes themselves. No changes to the ECS systems.

### Location
`Hrot.IG/IgApplication.cs`

### Constraints
- Add `using ImGuiNET;` and `using Hrot.IG.UI;` at the top
- Mouse input to the map MUST be gated: `HandleCameraInput` and canvas `Update` only called when `!ImGui.GetIO().WantCaptureMouse`
- Panel `Draw()` calls MUST occur between `rlImGui.Begin()` and `rlImGui.End()`
- `PerformanceMetrics.Snapshot()` and `EntityInspectorState.Refresh()` MUST be called each frame before rendering
- `GetSelectedEntity()` helper must query the ECS for an entity with `SelectionState` where `IsSelected || IsPrimarySelection` is true; return `Entity.Null` if none

### Required Changes Summary

**Fields to add:**
```
private DebugPanelState _debugPanelState;
private IgDebugPanel _debugPanel;
private EntityInspectorState _inspectorState;
private EntityInspectorPanel _inspectorPanel;
private MiniIosPanelState _miniIosState;
private MiniIosPanel _miniIosPanel;
private PerformanceMetrics _performanceMetrics;
private PerformanceOverlay _performanceOverlay;
```

**InitializeEcs additions (at bottom):**
```
_debugPanelState   = new DebugPanelState(_userConfig);
_debugPanel        = new IgDebugPanel(_debugPanelState);
_inspectorState    = new EntityInspectorState();
_inspectorPanel    = new EntityInspectorPanel(_inspectorState);
_miniIosState      = new MiniIosPanelState();
_miniIosPanel      = new MiniIosPanel(_miniIosState, _eventBus);
_performanceMetrics = new PerformanceMetrics();
_performanceOverlay = new PerformanceOverlay(_performanceMetrics);
```

**Run() render block additions:**
```
// Before HandleCameraInput / _canvas.Update:
if (!ImGui.GetIO().WantCaptureMouse) { ... }

// After _kernel.Update() / _eventBus.SwapBuffers():
_performanceMetrics.Snapshot(_world, Raylib.GetFPS(), Raylib.GetFrameTime() * 1000f);
_inspectorState.Refresh(_world, GetSelectedEntity());

// Between rlImGui.Begin() and rlImGui.End():
_debugPanel.Draw();
_inspectorPanel.Draw();
_miniIosPanel.Draw();
_performanceOverlay.Draw();
```

### Success Conditions

**SC1 — IgApplication builds without error** after changes.

**SC2 — All four panel instances are non-null** after `InitializeEcs()` completes.

**SC3 — Mouse clicks on ImGui panels do not propagate to the map**  
*Assert:* When `ImGui.GetIO().WantCaptureMouse` is true, `HandleCameraInput` is not called that frame.

**SC4 — IG application displays IgDebugPanel, EntityInspectorPanel, MiniIosPanel and PerformanceOverlay** at startup.

**SC5 — Clicking an entity on the map populates EntityInspectorPanel**  
*Assert:* `EntityInspectorState.SelectedEntity` is updated to the clicked entity after the next render frame.

**SC6 — All existing IG unit and integration tests remain green.**

# TASK-DETAIL.md — Scenario Editor Pack & HROT Editor Refactoring (`packs-2`)

**Design Reference:** See [DESIGN.md](./DESIGN.md) for architecture overview, phase goals, and
rationale.

---

## Phase 0: Formalize Logic Pack and Translator Pack Composite Wrappers

**Design Reference:** [DESIGN.md §Phase 0](./DESIGN.md#phase-0-formalize-logic-pack-and-translator-pack-composite-wrappers)

---

### PACK2-P001 — Create Logic Pack Composite Wrappers

**Design Reference:** DESIGN.md §0.A

**Scope:**

- In `Hrot.SimHost`, create `SimHostCoreLogicPack.cs` — an `IEcsModule` that registers
  `GroundKinematicsModule`, `CombatModule`, `DamageAssessmentModule`, and
  `AutonomousPerceptionModule`.
- In `Hrot.CGF`, create `CgfLogicPack.cs` — an `IEcsModule` that registers
  `CognitiveRuntimeModule`, `MissionControlModule`, and `ActionDispatchModule`.
- In `Hrot.Orchestrator`, create `OrchestrationLogicPack.cs` — an `IEcsModule` that registers
  `MasterSyncController` / `SlaveSyncController` and the cluster state handlers.
- Each wrapper's `RegisterSystems` delegate-calls into the individual module
  `RegisterSystems` implementations.

**Out of Scope:**

- Moving any module code; these are thin wrappers only.

**Files:**

| File | Change |
|------|--------|
| `Hrot.SimHost/SimHostCoreLogicPack.cs` | New composite IEcsModule |
| `Hrot.CGF/CgfLogicPack.cs` | New composite IEcsModule |
| `Hrot.Orchestrator/OrchestrationLogicPack.cs` | New composite IEcsModule |

**Constraints:**

- No logic changes to the contained modules; only registration delegation.
- Each wrapper installs the same set of systems as the current production composition root
  registers (no additions or omissions).

**Success Conditions:**

1. *(Unit test — SimHostCoreLogicPack)* Install `SimHostCoreLogicPack` into a test kernel.
   Assert the systems from `GroundKinematicsModule`, `CombatModule`, `DamageAssessmentModule`,
   and `AutonomousPerceptionModule` are all registered.
2. *(Unit test — CgfLogicPack)* Same pattern for CGF modules.
3. *(Regression)* All existing integration tests that test the SimHost or CGF pass unchanged
   (existing `NodeBootstrapper` code may reference the wrappers or keep using direct
   module registration — both are acceptable; the wrappers are additive).

---

### PACK2-P002 — Create Translator Pack Composite Wrappers for Feature Switch

**Design Reference:** DESIGN.md §0.B

**Scope:**

- In `Hrot.Map.Common` (or `Hrot.SimHost`), create `ActuatorIntentsEgressPack.cs` — an
  `IEcsModule` that installs: `NavigationIntentEgressTranslator`,
  `WeaponFireIntentEgressTranslator`, `SpawnEntityCommandEgressTranslator` (PACK2-D005),
  `UpdateEntityCommandEgressTranslator` (PACK2-D005), `DestroyEntityCommandEgressTranslator`
  (PACK2-D005).
- Create `EntityStatesIngressPack.cs` — an `IEcsModule` that installs the **full suite of
  visual and structural ingress translators** required for a complete 2D operational picture
  in External mode:
  - `EntityMasterIngressTranslator`
  - `GeoSpatialIngressTranslator`
  - `EntityInfoIngressTranslator` (unit names, colors, affiliations)
  - `MapVisualOverlayIngressTranslator` (tactical area polygons)
  - `MapRouteIngressTranslator` (multi-point route polylines)
  - `EntityDamageIngressTranslator`

**Prerequisite:** PACK2-D005 (the new egress translators must exist first).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common/Translators/ActuatorIntentsEgressPack.cs` | New composite IEcsModule |
| `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` | New composite IEcsModule |

**Constraints:**

- Must be installable/uninstallable as a unit via `ModuleHostKernel.InstallModulesAsync` /
  `UninstallModulesAsync`.
- `EntityStatesIngressPack` must install **all six translators** listed above. Omitting
  `EntityInfoIngressTranslator`, `MapVisualOverlayIngressTranslator`, or `MapRouteIngressTranslator`
  renders the Editor visually blind in External mode (blank symbol dots, no tactical graphics).

**Success Conditions:**

1. *(Build)* Both composites compile with zero errors.
2. *(Integration)* Install `ActuatorIntentsEgressPack` into a test kernel; publish
   `SpawnEntityCommand`; assert `CreateEntityRequest` reaches the mock DDS writer.
3. *(Integration — full visual pack)* Install `EntityStatesIngressPack`; inject fake DDS
   samples for `EntityMaster`, `WorldPos`, `EntityInfo`, `MapVisualOverlay`, and `MapRoute`.
   Assert: (a) a ghost entity exists in the local repo, (b) the entity has a non-empty name
   (from `EntityInfoIngressTranslator`), (c) a `MapOverlayStyle` component is present (from
   `MapVisualOverlayIngressTranslator`), (d) a `RoutePlan` component is present (from
   `MapRouteIngressTranslator`).

---

## Phase 1: Decouple Map Tools from the Network Edge

**Design Reference:** [DESIGN.md §Phase 1](./DESIGN.md#phase-1-decouple-map-tools-from-the-network-edge)

---

### PACK2-D001 — Refactor `CreationTool` to Emit `SpawnEntityCommand`

**Design Reference:** DESIGN.md §1.A

**Scope:**

- Remove `Action<CreateEntityRequest>` constructor parameter from `CreationTool`.
- Inject `FdpEventBus` (or `Action<SpawnEntityCommand>` for testability).
- On left-click: construct and publish `SpawnEntityCommand` containing `TkbType`, initial
  `SimTransform` (from click position + geographic transform), and optionally `OwnerNodeId`.
- Remove all `using` directives for `Hrot.NED.*` from `CreationTool.cs`.
- Preserve `OnCommandPublished` event (redirect to observe `SpawnEntityCommand`).
- Preserve `autoPopOnPlace`, `_nameResolver`, and multi-placement behaviour.

**Out of Scope:**

- Migrating `CreationTool` into `Hrot.ScenarioEditor` (Phase 2.B).
- The `MapCommandController` wiring (PACK2-D004).

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/Tools/CreationTool.cs` | Replace `Action<CreateEntityRequest>` → `FdpEventBus`; emit `SpawnEntityCommand` |
| `Hrot.IG/Tools/CreationToolConstants.cs` | No change unless NED-specific constants are removed |
| `Hrot.IG.Tests/` (or new test) | Add/update unit tests |

**Constraints:**

- `SpawnEntityCommand` is in `FDP.Toolkit.NetworkSpawning.Events` — add project reference to
  `FDP.Toolkit.NetworkSpawning` from `Hrot.IG` if not already present.
- Do NOT add any `Hrot.NED` using directives back.
- `InitialAttributesJson` carry-through: if `_initialPropertiesJson` was passed to the old
  `CreateEntityRequest`, it must now be included in `SpawnEntityCommand.InitialComponents` as a
  managed component or dedicated field (match existing `SpawnEntityCommand` contract).

**Success Conditions:**

1. *(Unit test)* Instantiate `CreationTool` with a test `FdpEventBus`. Simulate a left-click
   event at a known canvas coordinate. Assert that exactly one `SpawnEntityCommand` is present on
   the bus (via `ConsumeManaged<SpawnEntityCommand>()`), with the correct `TkbType` and a
   `SimTransform` position matching the input coordinates.
2. *(Compile-time)* `CreationTool.cs` has zero `using Hrot.NED` directives.
3. *(Integration)* All existing integration tests in `Hrot.ClusterRunner.Integration.Tests` that
   exercise the IG entity placement flow continue to pass (the ACL translator installed in the IG
   composition root converts `SpawnEntityCommand` back to `CreateEntityRequest` for DDS).

---

### PACK2-D002 — Refactor `EditTool` and `RouteEditTool` to Emit `UpdateEntityCommand`

**Design Reference:** DESIGN.md §1.B

**Scope:**

- Remove `_commandGateway` (`NedCommandGateway`) and `Action<UpdateEntityDescriptorRequest>`
  dependencies from both `EditTool` and `RouteEditTool`.
- Inject `FdpEventBus` into both tools.
- On commit of a modification, emit `UpdateEntityCommand` with `NetworkId` + `ComponentsToUpdate`
  list containing the modified ECS component (e.g. updated `SimTransform`, `EditablePolyline`,
  or `RoutePlan`).
- Remove all `using Hrot.NED.*` directives from both tool files.

**Out of Scope:**

- Migrating tools into `Hrot.ScenarioEditor` (Phase 2.B).

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/Tools/EditTool.cs` | Remove gateway; inject FdpEventBus; emit `UpdateEntityCommand` |
| `Hrot.IG/Tools/RouteEditTool.cs` | Remove gateway; inject FdpEventBus; emit `UpdateEntityCommand` |

**Constraints:**

- `UpdateEntityCommand` already exists in `FDP.Toolkit.NetworkSpawning.Events`.
- `ComponentsToUpdate` must carry a typed ECS component (matched to `NetworkSpawningSystem`
  processing logic) rather than raw DDS descriptors.
- Preserve all vertex-drag, multi-point shape, and route waypoint mechanics.

**Success Conditions:**

1. *(Unit test — EditTool)* Instantiate `EditTool` with a fake entity repo and test bus. Drag an
   entity to a new position and commit. Assert one `UpdateEntityCommand` is on the bus with the
   correct `NetworkId` and a `SimTransform` whose position reflects the drag delta.
2. *(Unit test — RouteEditTool)* Simulate committing a route with three waypoints. Assert one
   `UpdateEntityCommand` on the bus where `ComponentsToUpdate` contains a `RoutePlan` with three
   waypoints matching the input.
3. *(Compile-time)* Both tool files have zero `using Hrot.NED` directives.

---

### PACK2-D003 — Remove Network Branching from Context Menus and Delete Hotkeys

**Design Reference:** DESIGN.md §1.C

**Scope:**

- In `ContextMenuSystem.cs` (and any keyboard-input handler referencing a `DeleteEntityRequest`
  writer), remove the `_networkEnabled` flag branch.
- Remove `IDdsWriter<DeleteEntityRequest>` constructor dependency from `ContextMenuSystem`.
- Always publish `DestroyEntityCommand` to `FdpEventBus` on entity-delete action.

**Out of Scope:**

- The ACL egress translator for `DestroyEntityCommand` (PACK2-D005).

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/Systems/ContextMenuSystem.cs` | Remove `_networkEnabled` branch; always publish `DestroyEntityCommand` |
| `Hrot.IG/IgApplication.cs` (or wherever `ContextMenuSystem` is constructed) | Remove `IDdsWriter<DeleteEntityRequest>` injection |

**Constraints:**

- `DestroyEntityCommand` is in `FDP.Toolkit.NetworkSpawning.Events`.
- After this change the IG relies on `DestroyEntityCommandEgressTranslator` (PACK2-D005) for DDS
  forwarding in distributed deployments.

**Success Conditions:**

1. *(Unit test)* Instantiate `ContextMenuSystem` with a test bus. Right-click an entity and
   select "Delete". Assert exactly one `DestroyEntityCommand` is present on the bus with the
   correct entity network ID.
2. *(Compile-time)* `ContextMenuSystem.cs` has zero `using Hrot.NED` directives and no
   reference to `IDdsWriter<DeleteEntityRequest>`.
3. *(Regression)* All integration tests that exercise entity deletion via the UI continue to
   pass (the ACL translator preserves DDS behaviour for the IG distributed deployment).

---

### PACK2-D004 — Sever `IDdsWriter<CreateEntityRequest>` from `MapCommandController`

**Design Reference:** DESIGN.md §1.D

**Scope:**

- Remove `IDdsWriter<CreateEntityRequest> _createEntityWriter` field and constructor parameter
  from `MapCommandController`.
- Inject `FdpEventBus` instead.
- Replace `_createEntityWriter.Write(request)` with
  `_eventBus.PublishManaged(new SpawnEntityCommand { ... })`.
- **Retain** `IDdsWriter<MapCommandAck> _ackWriter` (lifecycle ACK to ExCon).
- Retain all `MapCommandRequest` (from ExCon) listening and `CreationTool` push-to-canvas logic
  unchanged.

**Out of Scope:**

- `CreationTool` refactoring (PACK2-D001, handled separately).

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/Systems/MapCommandController.cs` | Remove `_createEntityWriter`; inject bus; publish `SpawnEntityCommand` |
| `Hrot.IG/IgApplication.cs` | Update construction to omit `IDdsWriter<CreateEntityRequest>` |

**Constraints:**

- The existing `AreaAuthoringIntegrationTests` and `MiniExConIntegrationTests` observe
  `CreateEntityRequest` arriving on DDS from the IG. Post-refactor, this flow is preserved
  because the ACL egress translator (PACK2-D005) reconstructs the DDS message from the bus event.
  These tests must still pass.

**Success Conditions:**

1. *(Unit test)* Instantiate `MapCommandController` with a test bus and a mock `_ackWriter`.
   Simulate receiving a `MapCommandRequest`. Assert the `CreationTool` is pushed onto the canvas.
   Assert that after simulating a left-click from the tool, one `SpawnEntityCommand` appears on
   the bus.
2. *(Compile-time)* `MapCommandController.cs` has no `IDdsWriter<CreateEntityRequest>` field
   or constructor parameter.
3. *(Integration)* `AreaAuthoringIntegrationTests` and `MiniExConIntegrationTests` pass.

---

### PACK2-D005 — Create ACL Egress Translators for Spawn, Update, and Destroy Commands

**Design Reference:** DESIGN.md §1.E

**Scope:**

Create three new translator classes in `Hrot.Map.Common/Replication/Egress/`:

1. **`SpawnEntityCommandEgressTranslator`** — catches `SpawnEntityCommand` from `FdpEventBus`;
   serialises to `CreateEntityRequest`; writes to DDS.
2. **`UpdateEntityCommandEgressTranslator`** — catches `UpdateEntityCommand`; serialises to
   `UpdateEntityDescriptorRequest`; writes to DDS.
3. **`DestroyEntityCommandEgressTranslator`** — catches `DestroyEntityCommand`; serialises to
   `DeleteEntityRequest`; writes to DDS.

Update the IG composition root (`IgApplication.cs` / `Program.cs`) to install these translators
so the distributed IG deployment forwards entity commands over DDS exactly as before.

**Out of Scope:**

- Installation in the HROT Editor composition root (Phase 5).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` | New file |
| `Hrot.Map.Common/Replication/Egress/UpdateEntityCommandEgressTranslator.cs` | New file |
| `Hrot.Map.Common/Replication/Egress/DestroyEntityCommandEgressTranslator.cs` | New file |
| `Hrot.IG/IgApplication.cs` | Install all three translators in the IG composition root |

**Constraints:**

- Translators must implement the same `IDescriptorTranslator` contract used by other translators.
- `SpawnEntityCommandEgressTranslator` must preserve `InitialAttributesJson` / `InitialDescriptors`
  round-trip fidelity to pass the existing integration tests.

**Success Conditions:**

1. *(Unit test — Spawn)* Publish a `SpawnEntityCommand` to a test bus. Assert the translator
   writes exactly one `CreateEntityRequest` to the mock DDS writer with matching `TkbType` and
   geodetic position.
2. *(Unit test — Destroy)* Publish a `DestroyEntityCommand` to a test bus. Assert one
   `DeleteEntityRequest` is written with the matching `NetworkId`.
3. *(Integration)* All `Hrot.ClusterRunner.Integration.Tests` that observe `CreateEntityRequest`
   or `DeleteEntityRequest` on DDS continue to pass after the Phase 1 tool refactoring.

---

## Phase 2: Extract the Shared Scenario Interaction Logic Pack

**Design Reference:** [DESIGN.md §Phase 2](./DESIGN.md#phase-2-extract-the-shared-scenario-interaction-logic-pack)

---

### PACK2-E001 — Scaffold `Hrot.ScenarioEditor` Project

**Design Reference:** DESIGN.md §2.A

**Scope:**

- Create `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` targeting `net8.0` (or same TFM as
  `Hrot.IG`).
- Add project references: `FDP.Kernel`, `FDP.Toolkit.NetworkSpawning`,
  `FDP.Toolkit.Vis2D`, `Hrot.Map.Common`, `Hrot.Common`.
- **Explicitly exclude** any reference to `CycloneDDS.*` or `Hrot.NED`.
- Create `ScenarioEditorModule.cs` implementing `IEcsModule` with `ExecutionPolicy.Synchronous()`.
- Implement empty `RegisterSystems(ISystemRegistry registry)` stub — populated in PACK2-E002
  and PACK2-E003.
- Add `Hrot.ScenarioEditor` to `IOS-IG-SimHost.sln`.

**Out of Scope:**

- Any tool or rendering logic (done in PACK2-E002, PACK2-E003).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` | New file |
| `Hrot.ScenarioEditor/ScenarioEditorModule.cs` | New file |
| `IOS-IG-SimHost.sln` | Add project reference |

**Success Conditions:**

1. *(Build)* `dotnet build Hrot.ScenarioEditor` succeeds with zero errors and zero warnings.
2. *(Dependency check)* `dotnet list Hrot.ScenarioEditor package` contains no `CycloneDDS` or
   `Hrot.NED` entries (direct or transitive).
3. *(Instantiation test)* A unit test creates `ScenarioEditorModule`, calls
   `RegisterSystems(testRegistry)`, and asserts no exception is thrown.

---

### PACK2-E002 — Migrate Core Interaction Tools into `Hrot.ScenarioEditor`

**Design Reference:** DESIGN.md §2.B

**Scope:**

- Move (not copy) the following files from `Hrot.IG/Tools/` to `Hrot.ScenarioEditor/Tools/`:
  - `CreationTool.cs` / `CreationToolConstants.cs`  
  - `EditTool.cs` / `EditToolConstants.cs`  
  - `RouteEditTool.cs` / `RouteEditToolConstants.cs`  
  - `MeasureTool.cs` / `MeasureToolConstants.cs`  
  - `StandardInteractionTool.cs` / `StandardInteractionToolConstants.cs`  
- Update namespaces from `Hrot.IG.Tools` → `Hrot.ScenarioEditor.Tools`.
- Add project reference `Hrot.IG` → `Hrot.ScenarioEditor`.
- Update all `using` directives in `Hrot.IG` that reference the moved tools.
- Register the tools in `ScenarioEditorModule.RegisterSystems` where applicable.

**Prerequisite:** PACK2-D001, PACK2-D002 must be complete (tools already purged of DDS).

**Out of Scope:**

- Render layers (PACK2-E003).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Tools/*.cs` | New (moved from IG) |
| `Hrot.IG/Tools/*.cs` | Deleted (replaced by project reference) |
| `Hrot.IG.csproj` | Add `<ProjectReference>` to `Hrot.ScenarioEditor` |
| `Hrot.IG/**` (any consumers of old tool namespace) | Update `using` directives |

**Constraints:**

- Namespace update must be mechanical; no logic changes.
- The `Hrot.IG` project must compile cleanly and all existing IG tests pass.

**Success Conditions:**

1. *(Build)* Both `Hrot.IG` and `Hrot.ScenarioEditor` build with zero errors.
2. *(Namespace test)* Reflection test enumerates types in `Hrot.ScenarioEditor` assembly;
   asserts `CreationTool`, `EditTool`, `RouteEditTool`, `MeasureTool`,
   `StandardInteractionTool` are present.
3. *(No-duplication test)* Reflection test asserts none of the above types exist in the
   `Hrot.IG` assembly.
4. *(Regression)* All `Hrot.IG.Tests` pass unchanged.

---

### PACK2-E003 — Extract Visual Rendering Layers into `Hrot.ScenarioEditor`

**Design Reference:** DESIGN.md §2.C

**Scope:**

- Move (not copy) the following from `Hrot.IG/Systems/` to `Hrot.ScenarioEditor/Rendering/`:
  - `MapOverlayRenderLayer.cs`
  - `RouteRenderLayer.cs`
  - `MissionRenderLayer.cs`
  - `SelectionRenderSystem.cs`
- Move `Hrot.IG/Adapters/SstVisualizerAdapter.cs` and `StubVisualizerAdapter.cs` to
  `Hrot.ScenarioEditor/Adapters/`.
- Update namespaces: `Hrot.IG.Systems` → `Hrot.ScenarioEditor.Rendering`, etc.
- Add a `MapCanvasBuilder` static helper (or bootstrapping method on `ScenarioEditorModule`)
  that composes these layers into a `MapCanvas` instance.
- Register render layers in `ScenarioEditorModule.RegisterSystems`.
- `Hrot.IG` updates its imports accordingly.

**Out of Scope:**

- `MapCommandController`, `ContextMenuSystem`, and other IG-only systems that are NOT shared
  (they stay in `Hrot.IG`).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Rendering/*.cs` | New (moved) |
| `Hrot.ScenarioEditor/Adapters/*.cs` | New (moved) |
| `Hrot.IG/Systems/Map*RenderLayer.cs` etc. | Deleted |
| `Hrot.IG` consumers | Update `using` directives |

**Constraints:**

- Render layers must only query ECS via `ISimulationView` and local components (`SimTransform`,
  `RoutePlan`, `SelectionState`, `EditablePolyline`, etc.).
- No `Hrot.NED` or `CycloneDDS` references allowed in the moved files.

**Success Conditions:**

1. *(Build)* Both `Hrot.IG` and `Hrot.ScenarioEditor` build with zero errors.
2. *(Dependency test)* `Hrot.ScenarioEditor` assembly has no transitive dependency on
   `Hrot.NED` (verified via `dotnet list package`).
3. *(Render round-trip test)* Unit test: create a `MapCanvas` via `MapCanvasBuilder`, add a
   fake entity with a `SimTransform`; call the render update; assert no exception and
   `MapCanvas.EntityCount >= 1`.
4. *(Regression)* All IG rendering integration tests pass.

---

### PACK2-A001 — Define `WorldResetEvent` and Hook into Selection / Tool State

**Design Reference:** DESIGN.md §2.D (WorldResetEvent contract)

**Scope:**

- Define `WorldResetEvent` as a new managed event class in
  `Hrot.ScenarioEditor/Events/WorldResetEvent.cs` (alternatively in
  `FDP.Toolkit.NetworkSpawning.Events` if broader sharing is needed).
- Subscribe in every system or tool that caches an `Entity` handle:
  - `SelectionManager` (or equivalent selection state) — flush selected entity set on receipt.
  - `EditTool` — clear any active-edit entity reference.
  - `RouteEditTool` — clear any in-progress route entity reference.
  - Any other tool with an active `Entity` field.
- The event must be consumed **synchronously** before `repo.Clear()` is called in the file
  operation routines.

**Out of Scope:**

- The file operation routines themselves (PACK2-E004).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Events/WorldResetEvent.cs` | New event class |
| `Hrot.ScenarioEditor/Tools/EditTool.cs` | Subscribe; flush active-entity reference on `WorldResetEvent` |
| `Hrot.ScenarioEditor/Tools/RouteEditTool.cs` | Subscribe; flush active-entity reference on `WorldResetEvent` |
| Selection system (wherever `SelectionManager` lives) | Subscribe; clear selection on `WorldResetEvent` |

**Constraints:**

- Subscription must execute synchronously on the main thread before `repo.Clear()` returns.
- No `WorldResetEvent` consumer may call any ECS read or write after receiving the event
  (the repo is about to be wiped).

**Success Conditions:**

1. *(Unit test — flush selection)* Set up `SelectionManager` with one selected entity. Publish
   `WorldResetEvent` on the bus. Assert `SelectionManager.SelectedEntity` is null/empty
   immediately after.
2. *(Unit test — flush EditTool)* Set `EditTool` into an active-edit state. Publish
   `WorldResetEvent`. Assert `EditTool` has no pending entity reference.
3. *(Safety test)* After `WorldResetEvent` + `repo.Clear()`, access the render loop for one
   frame. Assert no `AccessViolationException` or `NullReferenceException` is thrown.

---

### PACK2-E004 — Wire Local Scenario File Operations in `ScenarioEditorModule`

**Design Reference:** DESIGN.md §2.D, §4.A–4.D

**Prerequisite:** PACK2-A001 (`WorldResetEvent` must exist before the file ops use it).

**Scope:**

- Add `ScenarioSerializer` (injected via constructor) to `ScenarioEditorModule`.
- Define a new `WorldResetEvent` managed event class in
  `Hrot.ScenarioEditor/Events/WorldResetEvent.cs` (or `FDP.Toolkit.NetworkSpawning.Events`).
- Implement three methods on `ScenarioEditorModule` (or a dedicated `ScenarioFileService`):
  - `void NewScenario(EntityRepository repo, FdpEventBus bus)` — publishes `WorldResetEvent`
    synchronously, then clears repo + resets `GlobalTime`.
  - `void SaveScenario(EntityRepository repo, string filePath)` — serializes to JSON with
    `SubsystemType = "Hrot.Scenario"` and writes.
  - `void LoadScenario(EntityRepository repo, FdpEventBus bus, string filePath)` — validates
    header (accepts `"Hrot.Scenario"`, `"Hrot.SimHost"`, `"Hrot.CGF"` for compatibility),
    publishes `WorldResetEvent`, clears repo, deserializes.
- Expose these methods via the `IEditorLogic` facade (Phase 3.D / PACK2-U004) for the Editor
  UI Pack panels to invoke.

**Out of Scope:**

- UI wiring (Phase 3.D — `ScenarioBrowserPanel`).
- `ScenarioSerializerBuilder` construction (Phase 4.A in the composition root).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/ScenarioEditorModule.cs` | Add serializer field + three file-op methods |
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` (optional) | Extract to service class if preferred |

**Constraints:**

- **`WorldResetEvent` must be published before `repo.Clear()`** in both `NewScenario` and
  `LoadScenario`. Systems holding cached `Entity` handles (e.g. `SelectionManager`, active
  tool state) must subscribe and flush on receipt to avoid `AccessViolationException`s from
  stale unmanaged pointers.
- **`SaveScenario` stamps `SubsystemType = "Hrot.Scenario"`** — not `"Hrot.Editor"`. Using a
  universal schema identifier ensures execution nodes (SimHost, CGF) can load the authored file.
- `LoadScenario` must accept `"Hrot.Scenario"` and for backwards compatibility also `"Hrot.SimHost"`
  and `"Hrot.CGF"`; throw/log on any other identifier.
- No DDS or network calls in any of the three methods.

**Success Conditions:**

1. *(Unit test — Save/Load round-trip)* Create a repo with two entities; call `SaveScenario` to
   a temp file; call `LoadScenario` from same file into a fresh repo. Assert both entities are
   present with matching `TkbType` and `SimTransform` position.
2. *(Unit test — New clears)* Register a subscriber for `WorldResetEvent`. Call `NewScenario`
   on a repo with entities. Assert: `WorldResetEvent` was published before `repo.Clear()` was
   called; `repo.EntityCount == 0`; `GlobalTime.T == 0`.
3. *(Unit test — Load fires reset)* Register a subscriber for `WorldResetEvent`. Call
   `LoadScenario` with a valid `"Hrot.Scenario"` file. Assert `WorldResetEvent` was
   published prior to entity reconstitution.
4. *(Unit test — Subsystem mismatch)* Call `LoadScenario` with a file bearing an unrecognised
   `SubsystemType`. Assert an exception (or logged error) is raised and the repo is left empty.
5. *(Unit test — Cross-app compatibility)* Call `LoadScenario` with a file saved by
   `"Hrot.SimHost"`. Assert entities are reconstituted successfully (no rejection).

---

## Phase 3: Formalize Host-Specific UI Packs

**Design Reference:** [DESIGN.md §Phase 3](./DESIGN.md#phase-3-formalize-host-specific-ui-packs)

---

### PACK2-U001 — Enforce UI-Logic Separation in Existing Panels

**Design Reference:** DESIGN.md §3.A

**Scope:**

- Audit all panels in `Hrot.IG/UI/` and `Hrot.ExCon/Panels/` for residual DDS writer calls or
  direct ECS mutation.
- For each violation found:
  - Remove `IDdsWriter<T>` fields from panel constructors.
  - Replace direct mutations with either a published FDP event or a call to an injected
    facade method (`IExConLogic`, `MiniExConPanelState`).

**Out of Scope:**

- Creating new panel projects (PACK2-U002 / PACK2-U003 / PACK2-U004).
- Moving panels between projects.

**Files:**

| File | Change (if violation found) |
|------|-----------------------------|
| `Hrot.IG/UI/*.cs` | Remove lingering DDS calls if present |
| `Hrot.ExCon/Panels/*.cs` | Remove lingering DDS calls if present |

**Success Conditions:**

1. *(Audit report)* Document every panel audited and what (if anything) was changed.
2. *(Compile-time)* No panel file in `Hrot.IG/UI/` or `Hrot.ExCon/Panels/` contains a field
   whose type is `IDdsWriter<T>` or `DdsWriter<T>`.
3. *(Regression)* All existing tests pass.

---

### PACK2-U002 — Formalize ExCon UI Pack

**Design Reference:** DESIGN.md §3.B

**Scope:**

- Verify that all `Hrot.ExCon/Panels/` panels delegate exclusively to `IExConLogic` and `IDerRepo`.
- Document the official panel set for the ExCon UI Pack (no code movement required if already in
  `Hrot.ExCon/Panels/`).
- Ensure `OrbatPanel.StartPlacementMode` delegates to `IExConLogic.StartPlacementMode` (no
  direct tool construction).

**Out of Scope:**

- ExCon map-tool sharing (ExCon does not use the shared tool pack directly; it delegates to IG
  via DDS `MapCommandRequest`).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ExCon/Panels/OrbatPanel.cs` | Confirm / fix delegation to `IExConLogic.StartPlacementMode` |
| `Hrot.ExCon/Panels/*.cs` | Fix any remaining direct DDS calls (found in PACK2-U001) |

**Success Conditions:**

1. *(Static analysis)* No panel in `Hrot.ExCon/Panels/` constructs or holds a
   `CreationTool`, `EditTool`, or any other tool type directly.
2. *(Unit test — OrbatPanel)* Mock `IExConLogic`. Call the panel's spawn action. Assert
   `IExConLogic.StartPlacementMode` was invoked once.

---

### PACK2-U003 — Formalize IG UI Pack

**Design Reference:** DESIGN.md §3.C

**Scope:**

- Verify `MiniExConPanel` activates tools via `MiniExConPanelState` which publishes FDP events
  (or activates a `CreationTool` from `Hrot.ScenarioEditor`).
- No panel files need to move; document the IG UI Pack composition in comments/region markers.
- Confirm `IgDebugPanel` reads exclusively from `DebugPanelState` (no DDS reads).
- Confirm `PerformanceOverlay` reads exclusively from `PerformanceMetrics`.

**Out of Scope:**

- Creating new projects.

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/UI/MiniExConPanel.cs` | Ensure spawning goes via `MiniExConPanelState` → FDP event |
| `Hrot.IG/UI/IgDebugPanel.cs` | Confirm no DDS reads |
| `Hrot.IG/UI/PerformanceOverlay.cs` | Confirm no DDS reads |

**Success Conditions:**

1. *(Unit test — MiniExConPanel)* Instantiate with fake `MiniExConPanelState`. Click "Spawn".
   Assert one `SpawnEntityCommand` (or `CreationTool` activation) emitted via the state object.
2. *(Compile-time)* No IG UI panel has a direct `DdsReader<T>` or `DdsWriter<T>` field.

---

### PACK2-U004 — Scaffold HROT Editor UI Pack

**Design Reference:** DESIGN.md §3.D

**Scope:**

- Create `Hrot.Editor/` project (or top-level namespace within `Hrot.Editor.csproj`).
- Define the `IEditorLogic` facade interface in `Hrot.Editor/IEditorLogic.cs`:
  ```csharp
  public interface IEditorLogic
  {
      void NewScenario();
      void SaveScenario(string filePath);
      void LoadScenario(string filePath);
      void ActivateTool(EditorTool tool);
      void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents);
      IDerRepo View { get; }
  }
  ```
- Create `EditorApplication.cs` (or `EditorLogic.cs`) implementing `IEditorLogic`, delegating
  to `ScenarioEditorModule` file methods and publishing FDP events via the internal bus.
- Create minimal bespoke ImGui panels — each binding **exclusively** to `IEditorLogic` (no
  direct `FdpEventBus` references in panels):
  - `ScenarioBrowserPanel` — calls `IEditorLogic.NewScenario`, `.SaveScenario`, `.LoadScenario`.
  - `EditorToolbarPanel` — calls `IEditorLogic.ActivateTool(...)` for each toolbar button.
  - `EntityPropertyInspector` — reads `IEditorLogic.View`; calls
    `IEditorLogic.CommitPropertyEdit` on edit commit.
  - `EditorOrbatPanel` — reads `IEditorLogic.View` for unit hierarchy.
- No DDS dependencies in any panel.

**Out of Scope:**

- Full feature implementation of panels (scaffolds are acceptable; complete UI can be iterative).
- The composition root assembly (Phase 5).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Hrot.Editor.csproj` | New project |
| `Hrot.Editor/IEditorLogic.cs` | New interface |
| `Hrot.Editor/EditorApplication.cs` | New IEditorLogic implementation |
| `Hrot.Editor/UI/ScenarioBrowserPanel.cs` | New — binds to IEditorLogic |
| `Hrot.Editor/UI/EditorToolbarPanel.cs` | New — binds to IEditorLogic |
| `Hrot.Editor/UI/EntityPropertyInspector.cs` | New — binds to IEditorLogic |
| `Hrot.Editor/UI/EditorOrbatPanel.cs` | New — binds to IEditorLogic |

**Success Conditions:**

1. *(Build)* `dotnet build Hrot.Editor` succeeds.
2. *(Panel test — ScenarioBrowserPanel)* Mock `IEditorLogic`. Click "New". Assert
   `IEditorLogic.NewScenario()` was called once; no direct `FdpEventBus` or repo access.
3. *(Panel test — EditorToolbarPanel)* Click "Place Entity" tool button. Assert
   `IEditorLogic.ActivateTool(EditorTool.Spawn)` was called once; no direct bus access.
4. *(Panel test — EntityPropertyInspector)* Edit a property and commit. Assert
   `IEditorLogic.CommitPropertyEdit` was called with the correct `networkId` and components.
5. *(Compile-time)* No panel file in `Hrot.Editor/UI/` holds a field of type `FdpEventBus`,
   `EntityRepository`, `ScenarioEditorModule`, or any DDS type.
6. *(Dependency)* `Hrot.Editor` has no transitive dependency on `Hrot.NED`.

---

## Phase 4: Implement Local Scenario File Operations

**Design Reference:** [DESIGN.md §Phase 4](./DESIGN.md#phase-4-implement-local-scenario-file-operations)

*(Most implementation work for Phase 4 is captured in PACK2-E004 above, as the file operations
are wired directly inside `ScenarioEditorModule`. The tasks below cover the composition root
setup and integration validation.)*

---

### PACK2-F001 — Instantiate the Purified Serializer in the Editor Composition Root

**Design Reference:** DESIGN.md §4.A

**Scope:**

- In `Hrot.Editor/Program.cs` (or the Editor's bootstrap class):

  ```csharp
  var serializer = new ScenarioSerializerBuilder("Hrot.Scenario")
      .RegisterTranslator(new HrotEntityScenarioTranslator())
      .Build();
  var editorModule = new ScenarioEditorModule(serializer, canvas, eventBus);
  ```

- Ensure `ScenarioSerializerBuilder` is from `FDP.Toolkit.Scenario`.
- Verify the `FdpAutoSerializer` JIT-compilation runs at startup (on-first-call or explicit
  `Warm()` method if available).

**Out of Scope:**

- Save/Load/New methods (PACK2-E004 already implements them on `ScenarioEditorModule`).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Program.cs` | Instantiate `ScenarioSerializerBuilder`; inject into module |

**Success Conditions:**

1. *(Integration test)* The Editor application starts up without exception and the serializer
   is non-null when `ScenarioEditorModule` receives it.
2. *(Performance assertion — optional)* Time the first save of a 100-entity repo; assert it
   completes in < 100 ms (validates JIT compilation happened before the hot path).

---

### PACK2-F002 — Validate "Load Empty" in the Editor Composition

**Design Reference:** DESIGN.md §4.B

**Scope:**

- Integration test verifying that `ScenarioBrowserPanel` "New" button ultimately calls
  `ScenarioEditorModule.NewScenario` and the repo ends up with zero entities and `GlobalTime.T == 0`.
- This is an end-to-end validation on top of the unit tests in PACK2-E004.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor.Tests/` (or `Hrot.ScenarioEditor.Tests/`) | New integration test |

**Success Conditions:**

1. *(Integration test)* Create an Editor harness with 10 pre-populated entities. Simulate "New"
   button press. Assert `repo.EntityCount == 0` and `GlobalTime.T == 0.0f`.

---

### PACK2-F003 — Validate "Save Scenario" Round-Trip

**Design Reference:** DESIGN.md §4.C

**Scope:**

- Integration test: populate a repo with at least 5 entities of different `TkbType`s, some with
  `RoutePlan` components. Call `SaveScenario` to a temp file. Assert the file exists and the JSON
  root contains an `"Entities"` array entry for each entity.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor.Tests/` (or `Hrot.ScenarioEditor.Tests/`) | New integration test |

**Success Conditions:**

1. *(Integration test — persistence)* Saved file exists; JSON is parseable;
   `Header.SubsystemType == "Hrot.Scenario"` (NOT `"Hrot.Editor"`).
2. *(Integration test — completeness)* JSON entity count equals repo entity count.

---

### PACK2-F004 — Validate "Load Scenario" Round-Trip

**Design Reference:** DESIGN.md §4.D

**Scope:**

- Integration test: save a 5-entity scenario, then load it into a fresh repo. Assert entities are
  reconstituted with matching `TkbType`, `SimTransform` (position within float tolerance), and
  `RoutePlan` waypoints (if present).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor.Tests/` (or `Hrot.ScenarioEditor.Tests/`) | New integration test (may extend PACK2-F003 test) |

**Success Conditions:**

1. *(Round-trip — TkbType)* Each reloaded entity has the same `TkbType` as the saved one.
2. *(Round-trip — SimTransform)* Positions match within 1e-4 metres.
3. *(Round-trip — RoutePlan)* Entities with routes have the same waypoint count and positions.
4. *(Subsystem mismatch guard)* Loading a file with an unrecognised `SubsystemType` (e.g.
   `"SomeOtherApp"`) into the Editor throws or logs an error; the repo remains empty.
5. *(Cross-app compatibility)* Loading a file saved with `"Hrot.SimHost"` or `"Hrot.CGF"`
   succeeds — entities are reconstituted without error.

---

## Phase 5: Assemble Composition Roots & Implement the Feature Switch

**Design Reference:** [DESIGN.md §Phase 5](./DESIGN.md#phase-5-assemble-composition-roots--implement-the-feature-switch)

---

### PACK2-C001 — Assemble HROT Editor All-In-One Composition Root

**Design Reference:** DESIGN.md §5.A

**Scope:**

- Create `Hrot.Editor/Program.cs` that:
  1. Instantiates a `ModuleHostKernel` with shared `EntityRepository` and `FdpEventBus`.
  2. Installs `SimHostCoreLogicPack` (or equivalent module registration from `Hrot.SimHost`).
  3. Installs `CgfLogicPack` (from `Hrot.CGF`).
  4. Installs `OrchestrationLogicPack` (from `Hrot.Orchestrator`).
  5. Installs `ScenarioEditorModule`.
  6. Instantiates `EditorApplication : IEditorLogic` and wires it to the `Hrot.Editor.UI`
     panels.
  7. Does **NOT** install any Translator Pack (offline default).
  8. Starts the kernel and runs the Raylib window loop.
- Verify `NetworkSpawningSystem` consumes all three command types locally: `SpawnEntityCommand`,
  `UpdateEntityCommand`, `DestroyEntityCommand`. If it only handles spawning, extend it to
  also handle updates and destroys (prerequisite for offline edit/delete to work).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Program.cs` | New — offline All-In-One composition root |

**Success Conditions:**

1. *(Build)* `dotnet build Hrot.Editor` succeeds.
2. *(Smoke test)* Starting the Editor opens a Raylib window, shows the 2D map canvas, and does
   not throw on the first 10 simulation frames.
3. *(Spawn test)* Activate `CreationTool`, simulate a click, assert one entity appears in the
   repo (the local `NetworkSpawningSystem` consumed `SpawnEntityCommand`).
4. *(Edit test)* Activate `EditTool`, simulate a drag-and-commit, assert the entity's
   `SimTransform` is updated in the local repo (`NetworkSpawningSystem` consumed
   `UpdateEntityCommand`).
5. *(Delete test)* Trigger the context-menu delete action, assert the entity is removed from
   the repo (`NetworkSpawningSystem` consumed `DestroyEntityCommand`).

---

### PACK2-C002 — Implement Feature Switch — Eject Local Logic Packs

**Design Reference:** DESIGN.md §5.D

**Scope:**

- Add a `SimHostMode { Internal, External }` enum and a `_currentMode` field to the Editor's
  bootstrap/application class.
- Implement `SwitchToExternalAsync(string ddsEndpoint)`:
  1. Call `await kernel.UninstallModulesAsync(typeof(SimHostCoreLogicPack), typeof(CgfLogicPack))`.
  2. After completion, set `_currentMode = External`.
- Wiring: `EditorToolbarPanel` (or configuration dialog) calls `SwitchToExternalAsync`.

**Out of Scope:**

- Installing translator packs (PACK2-C003).

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Program.cs` (or `EditorApplication.cs`) | Add `SwitchToExternalAsync` method |

**Success Conditions:**

1. *(Unit test)* Mock kernel. Call `SwitchToExternalAsync("localhost")`. Assert
   `UninstallModulesAsync` was called with both pack types.
2. *(Integration)* After the switch, simulate a `SpawnEntityCommand` publish. Assert the local
   `NetworkSpawningSystem` does NOT create an entity (it is uninstalled).

---

### PACK2-C003 — Implement Feature Switch — Snap-In the ACL Translator Packs

**Design Reference:** DESIGN.md §5.E

**Scope:**

- Extend `SwitchToExternalAsync` to also:
  1. Install `ActuatorIntentsEgressPack` (includes `SpawnEntityCommandEgressTranslator`,
     `UpdateEntityCommandEgressTranslator`, `DestroyEntityCommandEgressTranslator`).
  2. Install `EntityStatesIngressPack` (full visual suite: `EntityMasterIngressTranslator`,
     `GeoSpatialIngressTranslator`, `EntityInfoIngressTranslator`,
     `MapVisualOverlayIngressTranslator`, `MapRouteIngressTranslator`,
     `EntityDamageIngressTranslator`).
- Implement `SwitchToInternalAsync`:
  1. Uninstall the Translator Packs.
  2. Reinstall `SimHostCoreLogicPack` and `CgfLogicPack`.
- Wiring: `EditorToolbarPanel` calls `IEditorLogic.ActivateTool` which internally calls
  both switch methods.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Editor/Program.cs` (or `EditorApplication.cs`) | Add translator pack install/uninstall |
| `Hrot.Editor/UI/EditorToolbarPanel.cs` | Add toggle button wiring |

**Success Conditions:**

1. *(Integration — External mode spawn)* In External mode, publish `SpawnEntityCommand`. Assert
   `SpawnEntityCommandEgressTranslator` wrote one `CreateEntityRequest` DDS message (captured via
   a mock `DdsWriter`).
2. *(Integration — External mode full picture)* In External mode, inject fake DDS samples for
   `EntityMaster`, `WorldPos`, `EntityInfo`, `MapVisualOverlay`, and `MapRoute`. Assert the
   entity has a name, a `MapOverlayStyle`, and a `RoutePlan` in the local repo (validates
   the full `EntityStatesIngressPack` translator suite is active).
3. *(Integration — round-trip switch)* Switch External → Internal. Assert `NetworkSpawningSystem`
   is active again (publish `SpawnEntityCommand`, entity appears in repo).
4. *(UI)* `EditorToolbarPanel` toggle calls `IEditorLogic` method; no direct kernel reference
   in the panel.

---

## Phase 6: CGF Subsystem Execution Profile & Headless Integration Tests

**Design Reference:** [DESIGN.md §Phase 6](./DESIGN.md#phase-6-cgf-subsystem-execution-profile--headless-integration-tests)

---

### PACK2-R001 — Extend `RunMode` with `Editor` and `Demo`; Update Configuration Validation

**Design Reference:** DESIGN.md §6.A

**Scope:**

- In `Hrot.ClusterRunner/Configuration/RunMode.cs`:
  - Add `Editor = 1 << 6` (= 64) flag.
  - Add `Demo = Orchestrator | SimHost | CGF | IG | ExCon` macro (alias for `All`; rename `All`
    to `Demo` or keep both for backward compatibility; see constraint below).
- In `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`:
  - Add `"editor"` case to the `--mode` argument parser → `RunMode.Editor`.
  - Add `"demo"` case → `RunMode.Demo`.
  - Add validation: if `RunMode.Editor` is combined with any of `IG`, `ExCon`, `Orchestrator`,
    or `CGF`, throw a `ConfigurationException` with a descriptive message such as:
    `"RunMode.Editor must not be combined with distributed flags (IG, ExCon, Orchestrator, CGF)."`

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner/Configuration/RunMode.cs` | Add `Editor = 64`; add `Demo` macro |
| `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` | Add `"editor"` + `"demo"` parse cases; add validation guard |

**Constraints:**

- Keep `All` as an alias for `Demo` (or remove `All` only if no existing code references it —
  check with a workspace-wide grep before deleting).
- `RunMode.CI` is unchanged.
- `RunMode.Editor` must not start a `DdsParticipant` — verify in `Program.cs` that the DDS
  init path is gated on `mode != RunMode.Editor`.

**Success Conditions:**

1. *(Unit test — parsing)* Parse `"--mode editor"` → assert `config.Mode == RunMode.Editor`.
2. *(Unit test — parsing)* Parse `"--mode demo"` → assert `config.Mode == RunMode.Demo`.
3. *(Unit test — validation)* Provide `RunMode.Editor | RunMode.IG` → assert
   `ConfigurationException` is thrown with a message mentioning the combination.
4. *(Build)* Zero errors after adding the new flag; `All` reference sites compile cleanly.

---

### PACK2-R002 — Complete `CgfSubsystem` Brain-Role Pack Installation

**Design Reference:** DESIGN.md §6.B

**Scope:**

- In `Hrot.ClusterRunner/Services/CgfSubsystem.cs`, extend `Initialize` to install the full
  Brain-role pack set on `CgfApplication`:

  ```csharp
  _app = new CgfApplication(domainId, nodeId: 400);
  _app.Install(new CgfLogicPack());
  _app.Install(new EntityStatesIngressPack(PackRole.Ingress));
  _app.Install(new ActuatorIntentsEgressPack(PackRole.Egress));
  ```

- If `PackRole` does not yet exist, define it as:

  ```csharp
  public enum PackRole { Ingress, Egress }
  ```

  in `Hrot.Map.Common` (or `Hrot.Common`) alongside the composite pack classes (introduced in
  PACK2-P002).

- Ensure `CgfSubsystem.Update` still calls `_app.Tick()` unchanged.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner/Services/CgfSubsystem.cs` | Extend `Initialize` with three pack installations |
| `Hrot.Map.Common/PackRole.cs` (or `Hrot.Common`) | Define `PackRole` enum if not already present |
| `Hrot.Map.Common/Packs/EntityStatesIngressPack.cs` | Accept `PackRole` constructor argument if not already parameterised |
| `Hrot.Map.Common/Packs/ActuatorIntentsEgressPack.cs` | Accept `PackRole` constructor argument if not already parameterised |

**Constraints:**

- **Prerequisite:** PACK2-P002 (composite pack classes) must be complete before this task.
- The `PackRole.Ingress` construction must register only DDS reader subscriptions — no writer
  registrations — and vice-versa for `PackRole.Egress`.
- `CgfSubsystem` must not hold any pack instances after `Initialize` returns (ownership
  transfers to the kernel).

**Success Conditions:**

1. *(Build)* `dotnet build Hrot.ClusterRunner` succeeds with zero errors.
2. *(Unit test — pack composition)* Instantiate a `CgfApplication` in test isolation; call
   `CgfSubsystem.Initialize`. Use the kernel introspection API to assert exactly three modules
   are registered: `CgfLogicPack`, `EntityStatesIngressPack` (Ingress), `ActuatorIntentsEgressPack`
   (Egress).
3. *(Integration — standalone mode)* Start ClusterRunner with `--mode cgf` in a test domain;
   pump 20 frames; assert no exception and the heartbeat is published (existing behaviour
   preserved).

---

### PACK2-R003 — Scaffold `CgfHarness` and `EditorHarness` Test Infrastructure

**Design Reference:** DESIGN.md §6.C

**Scope:**

- Create `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs`:
  - Domain isolation: `DomainId = Interlocked.Increment(ref _domainCounter)` from the same
    static counter used by `HrotRunnerHarness`.
  - Expose `CgfSvc` property of type `CgfSubsystem`.
  - Provide `PumpFrames(int n)` and `PumpUntil(Func<bool> condition, int timeoutMs)`.
  - **Shared-domain constructor**: an overload `CgfHarness(int domainId)` that accepts an
    externally-allocated domain ID, used in `DistributedBrainMuscleIntegrationTests` (IT-4)
    to share a CycloneDDS loopback domain with a `HrotRunnerHarness` instance.

- Create `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`:
  - Instantiates `ModuleHostKernel` with `SimHostCoreLogicPack` + `CgfLogicPack` +
    `ScenarioEditorModule`.
  - Does **not** allocate a CycloneDDS domain ID (no DDS participant).
  - Expose `Kernel`, `Repo` (`EntityRepository`), `Bus` (`FdpEventBus`), `Editor`
    (`IEditorLogic`).
  - Accepts an optional mock `IDdsWriter<CreateEntityRequest>` for asserting no-traffic in
    offline tests.
  - Provide `PumpFrames(int n)` and `PumpUntil(Func<bool> condition, int timeoutMs)`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs` | New file |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | New file |

**Constraints:**

- `EditorHarness` must not import `CycloneDDS.*` namespaces (offline only).
- `CgfHarness` warmup frame count and post-warmup settle must match `HrotRunnerHarness`
  defaults (`WarmupFrames = 20`, `PostWarmupSettleMs = 200`) for consistent test behaviour.

**Success Conditions:**

1. *(Build)* `dotnet build Hrot.ClusterRunner.Integration.Tests` succeeds.
2. *(Smoke test — CgfHarness)* Instantiate `CgfHarness`; call `PumpFrames(20)`; assert no
   exception.
3. *(Smoke test — EditorHarness)* Instantiate `EditorHarness`; assert `Repo` is non-null;
   `PumpFrames(5)`; assert no exception.
4. *(Domain isolation)* Two independently constructed `CgfHarness` instances have different
   `DomainId` values.
5. *(Shared-domain wiring)* Constructing `CgfHarness(domainId: 150)` and
   `new HrotRunnerHarness(RunMode.SimHost, domainId: 150)` results in both using domain 150.

---

### PACK2-R004 — `OfflineEditorIntegrationTests` (IT-1)

**Design Reference:** DESIGN.md §6.D — IT-1

**Scope:**

Create `Hrot.ClusterRunner.Integration.Tests/OfflineEditorIntegrationTests.cs` with three test
methods exercising local command routing via `EditorHarness`:

| Test Method | Bus Event Published | Assert |
|-------------|--------------------|----|
| `SpawnCommand_LocalRepo_NoNetworkTraffic` | `SpawnEntityCommand` | `Repo.EntityCount == 1`; mock DDS writer call count == 0 |
| `EditCommand_UpdatesRepoInPlace` | `UpdateEntityCommand` (new `SimTransform`) | Entity `SimTransform` position matches update; DDS call count == 0 |
| `DeleteCommand_RemovesEntityFromRepo` | `DestroyEntityCommand` | `Repo.EntityCount == 0`; DDS call count == 0 |

**Prerequisite:** PACK2-R003 (`EditorHarness` must exist).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/OfflineEditorIntegrationTests.cs` | New file |

**Constraints:**

- Each test must call `harness.PumpUntil(...)` rather than `PumpFrames(n)` to avoid
  flakiness from frame-count assumptions.
- The mock `IDdsWriter<CreateEntityRequest>` injected at `EditorHarness` construction must be
  a simple `RecordingDdsWriter` (a test double that counts calls), not a full mock framework
  mock, to keep the test file self-contained.
- Tests must be independent — no shared state between methods. Each instantiates its own
  `EditorHarness`.

**Success Conditions:**

1. *(Spawn)* After publishing `SpawnEntityCommand` and pumping until `Repo.EntityCount == 1`:
   assert exactly one entity in the repo; assert the recorded DDS writer was never called.
2. *(Edit)* After spawning and publishing `UpdateEntityCommand` with a 100-metre north offset:
   assert the entity's `SimTransform` latitude/longitude reflects the offset within 1e-4
   degree tolerance; assert DDS writer call count == 0.
3. *(Delete)* After spawning and publishing `DestroyEntityCommand`: assert `Repo.EntityCount == 0`;
   assert DDS writer call count == 0.
4. *(Test isolation)* All three tests pass when run in random order (no shared static state).

---

### PACK2-R005 — `EditorFileIOIntegrationTests` (IT-2) and `FeatureSwitchRcuIntegrationTests` (IT-3)

**Design Reference:** DESIGN.md §6.D — IT-2 and IT-3

**Scope:**

**Part A — `EditorFileIOIntegrationTests`:**

Create `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs` with:

| Test Method | Scenario | Assert |
|-------------|----------|--------|
| `NewScenario_FiresWorldResetEventBeforeClear` | Subscribe `WorldResetEvent`; call `IEditorLogic.NewScenario()` | Event handler was called; `Repo.EntityCount == 0`; `GlobalTime.T == 0` |
| `SaveScenario_SubsystemTypeIsHrotScenario` | 5-entity repo; call `IEditorLogic.SaveScenario(tempPath)` | Parsed JSON `Header.SubsystemType == "Hrot.Scenario"` |
| `LoadScenario_AcceptsHrotSimHostFile` | Write a JSON file with `"Hrot.SimHost"` header; call `IEditorLogic.LoadScenario(...)` | Entities populated; no exception |
| `LoadScenario_RejectsUnknownSubsystemType` | Write a JSON file with `"UnknownApp"` header | Exception or logged error; `Repo.EntityCount == 0` after load attempt |

**Part B — `FeatureSwitchRcuIntegrationTests`:**

Create `Hrot.ClusterRunner.Integration.Tests/FeatureSwitchRcuIntegrationTests.cs` using an
`EditorHarness` with an injected mock DDS writer:

| Test Method | Scenario | Assert |
|-------------|----------|--------|
| `SwitchToExternal_EjectsLogicPacks_InstallsTranslators` | Call `SwitchToExternalAsync()` | Kernel no longer has `CgfLogicPack`; kernel has `ActuatorIntentsEgressPack` |
| `SwitchToExternal_SpawnCommand_ReachesDds` | External mode; publish `SpawnEntityCommand` | Mock DDS writer call count == 1 (`CreateEntityRequest`) |
| `SwitchToInternal_RestoresLogicPacks_EjectsTranslators` | External → Internal; publish `SpawnEntityCommand` | Entity in local repo; DDS writer call count == 0 |
| `RapidToggle_NoRaceCondition` | Toggle External→Internal 5 times; assert after each | `Repo.EntityCount` consistent; no duplicate entities; no exception |

**Prerequisite:** PACK2-R003 (`EditorHarness`), PACK2-C002, PACK2-C003 (Feature Switch).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs` | New file |
| `Hrot.ClusterRunner.Integration.Tests/FeatureSwitchRcuIntegrationTests.cs` | New file |

**Constraints:**

- File I/O tests must use `System.IO.Path.GetTempFileName()` and clean up in a `finally` block
  or `IDisposable.Dispose` on `EditorHarness`.
- Feature Switch tests must call `await SwitchToExternalAsync()` / `await SwitchToInternalAsync()`
  (async RCU, not a synchronous toggle).

**Success Conditions:**

All 8 test methods (4 file I/O + 4 feature switch) pass individually and as a complete suite
without ordering dependency.

---

### PACK2-R006 — `DistributedBrainMuscleIntegrationTests` (IT-4)

**Design Reference:** DESIGN.md §6.D — IT-4

**Scope:**

Create `Hrot.ClusterRunner.Integration.Tests/DistributedBrainMuscleIntegrationTests.cs`.

This test class pairs one `HrotRunnerHarness` (SimHost-only) with one `CgfHarness`, both
sharing the same CycloneDDS loopback domain ID:

```csharp
int domainId = Interlocked.Increment(ref _domainCounter);
using var simHost = new HrotRunnerHarness(RunMode.SimHost, domainId);
using var cgf = new CgfHarness(domainId);
```

Both harnesses perform warmup independently, then tests exercise distributed discovery.

| Test Method | Scenario | Assert |
|-------------|----------|--------|
| `SpawnedEntity_ReachesToCgf_ViaDds` | `simHost.SimHost.TestHook_SpawnEntity(tkbType, pos)` | `PumpUntil(() => cgf.CgfSvc.GhostEntityMap.ContainsKey(networkId), timeoutMs: 5000)` passes |
| `CgfAiIntent_ReachesSimHost_ViaDds` | Spawn + pump until CGF warmup | `PumpUntil(() => simHost.SimHost.TestHook_EntityMap[networkId].CurrentMission != null, 10_000)` passes |
| `DestroyedEntity_PurgedFromCgfGhostRepo` | Spawn, confirm in CGF; then publish `DestroyEntityCommand` | `PumpUntil(() => !cgf.CgfSvc.GhostEntityMap.ContainsKey(networkId), 5000)` passes |

**Prerequisite:** PACK2-R002 (CGF Brain-role packs installed), PACK2-R003 (`CgfHarness`).

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/DistributedBrainMuscleIntegrationTests.cs` | New file |

**Constraints:**

- `GhostEntityMap` (or equivalent TestHook property) must be exposed on `CgfSubsystem`
  in the same style as `SimHostSubsystem.TestHook_EntityMap`. If it does not exist, add it
  in this task (scope extends to `CgfSubsystem.cs`).
- The test must not use `Thread.Sleep` or fixed delays; all waiting must be via `PumpUntil`.
- `timeoutMs` values (5 000 ms for DDS propagation, 10 000 ms for AI mission assignment)
  must be documented as constants at the top of the test class.
- Both harnesses must be disposed in a `finally` block to prevent domain leaks.

**Success Conditions:**

1. *(Spawn reaches CGF)* `SpawnedEntity_ReachesToCgf_ViaDds` passes within the 5-second
   timeout on a developer workstation.
2. *(AI intent round-trip)* `CgfAiIntent_ReachesSimHost_ViaDds` passes within 10 seconds.
3. *(Destroy propagation)* `DestroyedEntity_PurgedFromCgfGhostRepo` passes within 5 seconds.
4. *(Isolation)* Running the 3 tests in any order or in parallel with other test classes does
   not cause CycloneDDS domain conflicts (each run of `DistributedBrainMuscleIntegrationTests`
   uses a freshly incremented domain ID).


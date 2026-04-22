# TASK-DETAIL.md — Scenario File Support, ACL Hardening & Network DRY Refactor (`packs-3`)

**Design Reference:** See [DESIGN.md](./DESIGN.md) for architecture overview, phase goals, and
rationale.

---

## Phase 0: CGF Component Registry Hardening

**Design Reference:** [DESIGN.md §Phase 0](./DESIGN.md#phase-0-cgf-component-registry-hardening)

---

### PACK3-C001 — Create `CgfComponentRegistry`

**Design Reference:** DESIGN.md §0.A

**Context:**
`CgfApplication`'s constructor currently registers ECS components one by one.  The framework
pattern (as demonstrated by `SimHostComponentRegistry`) calls for a centralised static registry
class that groups components by tier and is invoked with a single `RegisterAll(world)` call.

**Scope:**

- Create `Hrot.CGF/CgfComponentRegistry.cs` — a static class with a single
  `public static void RegisterAll(EntityRepository world)` method.
- The method must register components in three tiers (in order):
  1. Call `HrotSharedComponentRegistry.RegisterAll(world)` for network, geo, and lifecycle
     base components.
  2. Register all cognitive and kinematic components currently scattered in
     `CgfApplication`'s constructor (BehaviourTree states, HSM, intent channels,
     LocomotionChannel, WeaponChannel, NavigationIntent, VehicleState, NavState, etc.).
  3. Register the IG presentation components required by `EntityStatesIngressPack`
     (`EntityInfo`, `IgHealthState`, `IgSymbolOverride`, `HistoryTrail`, etc.).
- Replace the ad-hoc constructor registrations in `CgfApplication` with a single call to
  `CgfComponentRegistry.RegisterAll(_world)`.

**Out of Scope:**
- Moving `CognitiveComponentRegistry` or `KinematicComponentRegistry` out of `Hrot.SimHost`
  (future work, tracked separately).

**Files:**

| File | Change |
|------|--------|
| `Hrot.CGF/CgfComponentRegistry.cs` | New file |
| `Hrot.CGF/CgfApplication.cs` | Replace per-component registrations with single `RegisterAll` call |

**Success Conditions:**

1. *(Unit test)* Instantiate a bare `EntityRepository`, call `CgfComponentRegistry.RegisterAll`,
   assert that at least one component from each tier is registered without throwing (cognitive
   tier: `BrainBTreeState`; kinematic: `VehicleState`; IG presentation: `EntityInfo`).
2. *(Compile-time)* `CgfApplication.cs` compiles without error after the substitution.
3. *(Regression)* Existing CGF integration tests (`CgfSubsystemHeadlessTests`,
   `DistributedBrainMuscleIntegrationTests`) continue to pass.

---

## Phase 1: Urban Combat Scenario Extraction & Shared Validation

**Design Reference:** [DESIGN.md §Phase 1](./DESIGN.md#phase-1-urban-combat-scenario-extraction--shared-validation)

---

### PACK3-U001 — Extract `UrbanCombatValidator`

**Design Reference:** DESIGN.md §1.A

**Context:**
The original `UrbanCombatNewScenario.EvaluateTick` caches `Entity` handles that are invalidated
by serialisation/deserialisation.  The validator must be rewritten to resolve actors dynamically
via their persistent `TkbIdentity` components.

**Scope:**

- Create `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatValidator.cs`.
- Class is `public` (non-sealed, non-static).
- `EvaluateTick(uint tick, EntityRepository world) → bool` — each call:
  1. Queries all entities with `TkbIdentity`.
  2. Binds `TkbMilitaryApc (2001)` → `apc` and `TkbInsurgent (2003)` → `insurgent` locals.
  3. Evaluates four sequential latches in order (see DESIGN §1.A for logic).
  4. Returns `true` on `_latchInsurgentKilled`.
  5. Throws `ScenarioFailureException(5, ...)` if `tick > 600`.
- Class holds five `bool` latch fields; no cached `Entity` fields.
- Imports: `FDP.Toolkit.Combat.Components`, `FDP.Toolkit.Behavior.Components`,
  `FDP.Toolkit.Replication.Components`, `Fdp.Kernel`.

**Files:**

| File | Change |
|------|--------|
| `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatValidator.cs` | New file |

**Success Conditions:**

1. *(Unit test)* Build a minimal `EntityRepository` with a `TkbInsurgent` entity whose
   `WeaponChannel.ActiveAction == CombatConstants.ActionIdAimAndFire`.  Call `EvaluateTick`
   once; assert `_latchAmbushFired` set (observable via derived test subclass exposing latch).
2. *(Unit test)* Tick > 600 with no latches set → `ScenarioFailureException` thrown.
3. *(Unit test)* Simulate all four latches firing in sequence across tick calls → returns `true`.

---

### PACK3-U002 — Simplify `UrbanCombatNewScenario`

**Design Reference:** DESIGN.md §1.B

**Scope:**

- In `UrbanCombatNewScenario.cs`:
  - Add `private readonly UrbanCombatValidator _validator = new();`.
  - Replace the body of `EvaluateTick` with `return _validator.EvaluateTick(tick, world);`.
  - Remove the now-redundant latch fields (`_latchAmbushFired`, etc.) from the scenario class.

**Files:**

| File | Change |
|------|--------|
| `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` | Delegate EvaluateTick, remove local latches |

**Success Conditions:**

1. *(Regression)* The existing `UrbanCombatNewScenario` integration test continues to pass
   (same deterministic 600-tick budget, same success signal).
2. *(Compile)* No compile errors or unreachable-code warnings.

---

### PACK3-U003 — Editor Preview / Rewind Integration Test

**Design Reference:** DESIGN.md §1.C

**Scope:**

- Add test class `EditorPreviewAndSaveIntegrationTests` to
  `Hrot.ClusterRunner.Integration.Tests` (e.g. in new file or appended to
  `EditorFileIOIntegrationTests.cs`).
- The test must:
  1. `EditorHarness.NewScenario()` — start fresh.
  2. Publish `SpawnEntityCommand { TkbType = 1001 }`, pump until `repo.EntityCount == 1`.
  3. Get the entity's `NetworkIdentity`.
  4. Publish `UpdateEntityCommand` moving entity to position `(100f, 0, 0)`. Pump 5 frames.
  5. Instantiate `PreviewClusterOpHandler(repo)`.
  6. Send `NodeOpCommand { PayloadJson = "{\"TargetState\": 20}" }` (LoadingPreview) → snapshot
     captured.
  7. Publish `UpdateEntityCommand` moving entity to `(999f, 0, 0)`. Pump 5 frames.
  8. Assert `SimTransform.Position.X == 999f` (preview state visible).
  9. Send `NodeOpCommand { PayloadJson = "{\"TargetState\": 22}" }` (UnloadingPreview) → rewind.
  10. Assert `SimTransform.Position.X == 100f` (state restored).
  11. `logic.SaveScenario(_tempFile)` — file must exist.
  12. `logic.NewScenario()`, pump 2 frames — assert `EntityCount == 0`.
  13. `logic.LoadScenario(_tempFile)`, pump 5 frames.
  14. Assert `EntityCount == 1` and `SimTransform.Position.X == 100f`.
- `IDisposable.Dispose` deletes `_tempFile`.
- Decorated with `[Collection("EditorOfflineTests")]`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/EditorPreviewAndSaveIntegrationTests.cs` | New test file |

**Success Conditions:**

1. *(Test passes)* All 14 steps in the sequence complete without assertion failure.
2. *(CI)* Test runs in < 10 seconds; no DDS or network calls.

---

### PACK3-U004 — Urban Combat File Lifecycle Integration Test

**Design Reference:** DESIGN.md §1.D

**Scope:**

- Add test class `UrbanCombatFileLifecycleTests` to `Hrot.ClusterRunner.Integration.Tests`.
- The single test `UrbanCombatExtractedToJson_ExecutesSuccessfullyInLiveMode` must:
  1. **Extract:** Create `EntityRepository`, `EventAccumulator`, `ModuleHostKernel`. Instantiate
     `UrbanCombatNewScenario`, call `Configure`. Build `ScenarioSerializer`, serialise world.
     Wrap in `HrotScenarioEnvelopeDto` (set `Header`, leave `Zones` null for this test, set
     `Entities` from FDP DOM). Serialise with `HrotJsonOptions`. Write to temp staging dir
     (`C:\FDP_Temp\<scenarioId>\scenario.json`).
  2. **Boot cluster:** `HrotRunnerHarness(RunMode.Orchestrator | RunMode.SimHost, options)`
     where `options.Deterministic = true`, `options.FixedDeltaSeconds = 1f/60f`.
     `CgfHarness(domainId)`.  Pump 20 frames each for DDS discovery.
  3. **Transition:** Inject `ClusterOpRequest` with `TargetState = 31` (OperatingLive),
     `ScenarioId = _scenarioId`.  Call `PumpUntil(CurrentSystemState == OperatingLive, 5000 ms)`.
  4. **Validate:** Instantiate `UrbanCombatValidator`. Loop up to 800 ticks pumping both
     harnesses by 1 frame per iteration.  Call `validator.EvaluateTick(i, simHostHarness.Repo)`
     each iteration.  Set `success = true` on return value `true`.
  5. `Assert.True(success)`.
- `Dispose` deletes the temp staging dir recursively.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/UrbanCombatFileLifecycleTests.cs` | New test file |

**Success Conditions:**

1. *(Test passes deterministically)* The auto-extracted JSON scenario loads successfully and
   all four ambush latches fire within 800 frames.
2. *(Usage of shared validator)* Same `UrbanCombatValidator` instance is used — validates code
   reuse between this test and the original programmatic test.
3. *(Cleanup)* Temp staging dir is deleted even when the test fails (verified by `Dispose`).

---

## Phase 2: Zone Definitions in Scenario Files

**Design Reference:** [DESIGN.md §Phase 2](./DESIGN.md#phase-2-zone-definitions-in-scenario-files)

---

### PACK3-Z001 — `ZoneEnvironmentData` ECS Singleton & `CarKinematicsSystem` Refactor

**Design Reference:** DESIGN.md §2.D

**Scope:**

- Add `ZoneEnvironmentData` struct to `FDP.Toolkit.Geographic` (or `Fdp.Kernel.Environment`
  namespace).  Assign an unused ID from the **Toolkit expansion block** in `GlobalComponentIds.cs`
  (IDs 20–79 are reserved for toolkit components; verify the chosen ID is free and register it
  in that file).  Do **not** use an ID in the 160–199 range, which is reserved for
  application-level components.  Field: `public RoadNetworkBlob RoadNetwork;`.
- Refactor `CarKinematicsSystem.OnUpdate()`:
  - Remove the constructor-injected `RoadNetworkBlob` parameter.
  - At the top of `OnUpdate`, read the road network via the singleton with an empty-blob
    fallback so that **all other vehicle physics (rigid-body integration, steering, RVO
    avoidance) continue to run even when no zone has been loaded**:
    ```csharp
    var roadNetwork = World.HasSingleton<ZoneEnvironmentData>()
        ? World.GetSingleton<ZoneEnvironmentData>().RoadNetwork
        : default; // empty blob — safe for non-road scenarios
    ```
    Never return early from `OnUpdate` based on singleton absence.
- Update all composition roots that previously passed `RoadNetworkBlob` to the constructor of
  `CarKinematicsSystem`.

**Files:**

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Geographic/ZoneEnvironmentData.cs` | New struct |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` | Refactor to read singleton |
| Composition roots in `Hrot.SimHost`, `Hrot.IG`, test harnesses | Update constructor calls |

**Success Conditions:**

1. *(Unit test)* `CarKinematicsSystem.OnUpdate` does not throw and does **not** skip vehicle
   physics when no `ZoneEnvironmentData` singleton is present (vehicles continue to move; only
   road-following behaviour degrades gracefully to an empty network).
2. *(Unit test)* Set `ZoneEnvironmentData` singleton with a valid `RoadNetworkBlob`; assert the
   system performs a navigation tick without error.
3. *(Regression)* All existing `CarKinematicsSystem`-dependent integration tests continue to
   pass after the constructor-parameter removal, including scenarios that do not use road networks
   (e.g. `AutoDriveScenario`, manual-driving tests).

---

### PACK3-Z002 — Application-Layer DTOs for Scenario Envelope

**Design Reference:** DESIGN.md §2.C

**Scope:**

- Create the following DTO classes in `Hrot.Map.Common`, namespace
  `Hrot.Map.Common.Scenario`:
  - `HrotScenarioEnvelopeDto` with properties `Header`, `Zones?`, `Entities?`.
  - `ScenarioHeaderDto` with `SubsystemType` and `SchemaVersion`.
  - `ZoneDefinitionDto` with `RoadNetworkPath?`, `TerrainDatabaseId?`, `Obstacles?`.
  - `ZoneObstacleDto` with `X`, `Y`, `Radius`.
- Define the shared `HrotJsonOptions` static instance in
  `Hrot.Map.Common/HrotSerializerOptions.cs` (or equivalent):
  `PropertyNameCaseInsensitive = true`,
  `PropertyNamingPolicy = CamelCase`,
  `DefaultIgnoreCondition = WhenWritingNull`,
  `WriteIndented = true`.
- No `[JsonPropertyName]` attributes on any DTO class.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common/Scenario/HrotScenarioEnvelopeDto.cs` | New |
| `Hrot.Map.Common/Scenario/ScenarioHeaderDto.cs` | New |
| `Hrot.Map.Common/Scenario/ZoneDefinitionDto.cs` | New |
| `Hrot.Map.Common/Scenario/ZoneObstacleDto.cs` | New |
| `Hrot.Map.Common/HrotSerializerOptions.cs` | New |

**Success Conditions:**

1. *(Round-trip test — unit)* Construct an `HrotScenarioEnvelopeDto` with header + one zone
   (with two obstacles).  Serialise with `HrotJsonOptions` to string.  Deserialise back.
   Assert `Zones["urban_zone"].Obstacles[0].X == expected`, etc.
2. *(Case-insensitivity test)* Deserialise a JSON string with PascalCase keys (e.g.
   `"RoadNetworkPath"`) into `ZoneDefinitionDto` using `HrotJsonOptions`; assert non-null path.
3. *(No magic strings — compile check)* `ZoneDefinitionDto` and siblings contain zero
   `[JsonPropertyName]` attributes in source.

---

### PACK3-Z003 — `IZoneManagerService` and `ZoneManagerService`

**Design Reference:** DESIGN.md §2.E

**Scope:**

- Define `IZoneManagerService` interface in `Hrot.Map.Common`:
  ```csharp
  void LoadZones(EntityRepository repo, Dictionary<string, ZoneDefinitionDto> zones);
  Dictionary<string, ZoneDefinitionDto> GetActiveZones();
  ```
- Implement `ZoneManagerService` in `Hrot.Map.Common/Services/`:
  - `LoadZones`: for each entry in `zones`:
    1. If `RoadNetworkPath` is non-null:
       a. **Dispose the existing singleton before overwriting** to prevent native-array memory
          leaks.  Per `RoadNetworkBlob`'s `NativeArray` ownership contract:
          ```csharp
          if (repo.HasSingleton<ZoneEnvironmentData>())
              repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Dispose();
          ```
       b. `RoadNetworkLoader.LoadFromJson(path)` → `repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob })`.
    2. For each `ZoneObstacleDto` in `Obstacles`, spawn one ECS entity with:
       `SimTransform { Position = new Vector3(obs.X, obs.Y, 0) }` and
       `PhysicsCollider { Radius = obs.Radius, CollisionLayer = PhysicsConstants.EntityCollisionLayer }`.
  - Track loaded zones internally for `GetActiveZones()`.
- Use `HrotJsonOptions` for any internal JSON operations.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common/Services/IZoneManagerService.cs` | New interface |
| `Hrot.Map.Common/Services/ZoneManagerService.cs` | New implementation |
| `Hrot.Map.Common/Hrot.Map.Common.csproj` | Add `<ProjectReference>` to `FDP.Toolkit.Physics.csproj` |

**Constraints:**
- `ZoneManagerService` must call only `RoadNetworkLoader` (FDP toolkit) — no internal JSON
  parsing of road network internals.
- The translation of `ZoneDefinitionDto` → `ZoneEnvironmentData` + ECS entities is entirely
  within `ZoneManagerService`; neither the load handlers nor UI code performs this translation.
- **`Hrot.Map.Common.csproj` must be updated** to add a project reference to
  `FDP.Toolkit.Physics` before `PhysicsCollider` and `PhysicsConstants` will resolve.  Without
  this step the file will not compile.

**Success Conditions:**

1. *(Unit test)* Call `LoadZones` with a zone having a valid `RoadNetworkPath` pointing to
   `Assets/sample_road.json`.  Assert `repo.HasSingleton<ZoneEnvironmentData>()` is `true` and
   `envData.RoadNetwork.Nodes.IsCreated`.
2. *(Unit test — memory safety)* Call `LoadZones` twice with different road network paths.
   Assert `RoadNetwork.Nodes.IsCreated` from the **first** call returns `false` after the second
   call (proving the old blob was disposed, not leaked).
3. *(Unit test)* Call `LoadZones` with a zone having `Obstacles` list of two entries.  Assert
   entities with `PhysicsCollider` count == 2 in `repo`; validate first obstacle `Radius`.
4. *(Unit test)* Call `GetActiveZones()` after `LoadZones`; assert the returned dictionary
   key matches the loaded zone name.

---

### PACK3-Z004 — Custom Load Handlers (`HrotScenarioLoadHandler`, `HrotEditLoadHandler`)

**Design Reference:** DESIGN.md §2.F

**Scope:**

- Create `Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs`.
  - Replaces `ReferenceScenarioLoadHandler` in the `LoadingLive` cluster state handler
    registration.
  - `CommitLoad` sequence — **parse the JSON string exactly once** to avoid double-allocation:
    1. `var dom = JsonNode.Parse(rawJson)?.AsObject();` — single parse into a DOM.
    2. `var envelope = dom.Deserialize<HrotScenarioEnvelopeDto>(HrotJsonOptions);` — populate
       DTO from the already-parsed DOM (no second string parse).
    3. If `envelope?.Zones != null` → `_zoneManagerService.LoadZones(repo, envelope.Zones)`.
    4. `_serializer.Deserialize(repo, dom);` — pass the **pre-parsed `JsonObject` DOM** to the
       FDP toolkit's overload that accepts a `JsonObject` directly, avoiding a third parse.
- Create `Hrot.ScenarioEditor/Handlers/HrotEditLoadHandler.cs`.
  - Replaces `ReferenceEditLoadHandler` in the `LoadingEdit` handler registration.
  - Same `CommitLoad` sequence as above (single parse, DOM passed to both DTO and serializer).
- Both handlers must handle `null` or missing `"Zones"` gracefully (no-op — backwards
  compatible with old scenario files that lack a `"Zones"` section).

**Files:**

| File | Change |
|------|--------|
| `Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` | New |
| `Hrot.ScenarioEditor/Handlers/HrotEditLoadHandler.cs` | New |
| Orchestrator / editor cluster state handler registrations | Replace reference handlers |

**Success Conditions:**

1. *(Unit test)* Feed handler a JSON string without a `"Zones"` key; assert `LoadZones` is
   **not** called and ECS deserialization completes normally.
2. *(Unit test)* Feed handler a JSON string with a valid `"Zones"` key; assert `LoadZones` is
   called once before `ScenarioSerializer.Deserialize`.
3. *(Regression)* Existing `EditorFileIOIntegrationTests` continue to pass with the new handler.

---

### PACK3-Z005 — `ScenarioFileService` Save with Zone Support

**Design Reference:** DESIGN.md §2.G

**Scope:**

- Update `ScenarioFileService.SaveScenario(EntityRepository repo, string filePath)`:
  1. Call `_fdpSerializer.Serialize(repo, new ScenarioHeader("Hrot.Scenario"))` → `fdpDom`.
  2. Build `HrotScenarioEnvelopeDto` { Header, Zones = `_zoneManagerService.GetActiveZones()`,
     Entities = `fdpDom["Entities"]?.AsObject()` }.
  3. `JsonSerializer.Serialize(envelope, HrotJsonOptions)` → write to disk.
- Inject `IZoneManagerService` into `ScenarioFileService` constructor.
- If `GetActiveZones()` returns empty dict, the `Zones` key is omitted from JSON
  (`WhenWritingNull` / empty-dict-as-null handling) to keep output lean.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | Update save logic |

**Success Conditions:**

1. *(Unit/integration test)* Save a scenario with an active zone; deserialise the written file;
   assert `HrotScenarioEnvelopeDto.Zones` is non-null with the expected zone.
2. *(Unit test)* When no zones are active, the serialised file has no `"Zones"` key (or key is
   null/omitted) to preserve backward compatibility.

---

### PACK3-Z006 — Zone Scenario Load Integration Test

**Design Reference:** DESIGN.md §2.H

**Scope:**

- Add test class `ZoneScenarioLoadIntegrationTests` to `Hrot.ClusterRunner.Integration.Tests`.
- The single test `LoadScenario_WithZoneDefinition_PopulatesRoadNetworkAndObstacles` must:
  1. Build `HrotScenarioEnvelopeDto` in code (no file pre-requisite): 1 zone named
     `"urban_combat_zone"` with `RoadNetworkPath = "Assets/sample_road.json"` and 2 obstacles:
     `(50, 25, r=10)` and `(-10, -10, r=5)`.
  2. Serialise to temp file with `HrotJsonOptions`.
  3. `EditorHarness.LoadScenario(tempFile)` → `PumpFrames(5)`.
  4. Assert `repo.HasSingleton<ZoneEnvironmentData>()` is `true`.
  5. Assert `envData.RoadNetwork.Nodes.IsCreated` and `.Segments.IsCreated`.
  6. Query `With<PhysicsCollider>().With<SimTransform>()` → assert count == 2.
  7. Validate obstacle 1: `Position.X == 50f`, `Position.Y == 25f`, `Radius == 10f`.
  8. Validate obstacle 2: `Position.X == -10f`, `Position.Y == -10f`, `Radius == 5f`.
- `IDisposable.Dispose` deletes temp file.
- Decorated `[Collection("EditorOfflineTests")]`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/ZoneScenarioLoadIntegrationTests.cs` | New test file |

**Success Conditions:**

1. *(Test passes fully)* All 8 assertions pass.
2. *(No DDS calls)* Test uses `EditorHarness` only — zero network activity.
3. *(CI)* Test runs in < 5 seconds.

---

## Phase 3: ACL Backdoor Elimination

**Design Reference:** [DESIGN.md §Phase 3](./DESIGN.md#phase-3-acl-backdoor-elimination)

---

### PACK3-A001 — Purge `tryGetPrebuilt` from `SpawnEntityCommandEgressTranslator`

**Design Reference:** DESIGN.md §3.B

**Scope:**

- In `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs`:
  - Delete the `_tryGetPrebuilt` field (type: `Func<Guid, CreateEntityRequest?>`).
  - Delete the constructor overload that accepts the delegate.
  - In `PollIngress`, remove the bypass conditional block that checks `_tryGetPrebuilt` and
    writes the pre-built DDS payload.
  - The standard `BuildCreateEntityRequest(spawnCmd)` path now handles **all** commands.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` | Remove delegate field, ctor overload, bypass block |

**Success Conditions:**

1. *(Compile)* Single constructor remains; all call sites that used the delegate overload are
   updated.
2. *(Unit test, see PACK3-A005)* Translator synthesises correct `CreateEntityRequest` from
   `SpawnEntityCommand` with `EditablePolyline` in `InitialComponents` — no delegate needed.

---

### PACK3-A002 — Remove DTO Cache from `MapCommandController`

**Design Reference:** DESIGN.md §3.C

**Scope:**

- In `Hrot.IG/Systems/MapCommandController.cs` (or wherever located):
  - Delete `_prebuiltRequests` (type: `Dictionary<Guid, CreateEntityRequest>`).
  - Delete `TryDequeuePrebuilt(Guid requestId, out CreateEntityRequest)` method.
  - Simplify `OnAreaEntityCreated` to accept only `SpawnEntityCommand cmd` (no separate
    `CreateEntityRequest` parameter) and immediately `_eventBus.PublishManaged(cmd)`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/Systems/MapCommandController.cs` | Delete cache dict, method, simplify OnAreaEntityCreated |

**Success Conditions:**

1. *(Compile)* `MapCommandController` compiles without the cache fields.
2. *(Unit test)* `OnAreaEntityCreated` with valid `SpawnEntityCommand` → event appears on bus
   (verify via `FdpEventBus.ConsumeManaged<SpawnEntityCommand>`).

---

### PACK3-A003 — `IgApplication` Composition Root Cleanup

**Design Reference:** DESIGN.md §3.D

**Scope:**

- In `Hrot.IG/IgApplication.cs`:
  - Remove `MapCommandController? mapCmdCtrlRef = null;` local variable.
  - Remove the lambda expression that captures it.
  - Construct `SpawnEntityCommandEgressTranslator` using the clean single-argument constructor
    (participant, bus, geoTransform) with no delegate.

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/IgApplication.cs` | Remove side-channel wiring |

**Success Conditions:**

1. *(Compile)* `IgApplication.cs` compiles cleanly with no reference to the removed delegate
   overload.
2. *(Regression)* `OfflineEditorIntegrationTests` and area-authoring integration tests pass.

---

### PACK3-A004 — Fix `AreaAuthoringTool` to Use `InitialComponents`

**Design Reference:** DESIGN.md §3.E

**Scope:**

- Identify all map tool classes (e.g. `AreaAuthoringTool`, `RouteAuthoringTool`) that previously
  called `MapCommandController.OnAreaEntityCreated` with a pre-built `CreateEntityRequest`.
- Refactor each tool to instead construct a `SpawnEntityCommand` whose `InitialComponents`
  list carries the geometry domain objects:
  - `EditablePolyline { Points = ... }` for area polygons.
  - `MapOverlayStyle { ... }` for visual appearance.
  - Route equivalent types for `RouteAuthoringTool`.
- Ensure `SpawnEntityCommandEgressTranslator.BuildCreateEntityRequest` correctly extracts these
  component types and populates the corresponding DDS descriptor entries
  (`dtMapVisualOverlay`, `dtMapRoute`).  If `BuildCreateEntityRequest` requires extension,
  add the geometry translation logic there.

**Files:**

| File | Change |
|------|--------|
| `Hrot.IG/Tools/AreaAuthoringTool.cs` | Use SpawnEntityCommand.InitialComponents |
| `Hrot.IG/Tools/RouteAuthoringTool.cs` (if applicable) | Use SpawnEntityCommand.InitialComponents |
| `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` | Extend BuildCreateEntityRequest for geometry types |

**Success Conditions:**

1. *(Unit test, part of PACK3-A005 Test 1)* Translator produces correct `dtMapVisualOverlay`
   descriptor from `SpawnEntityCommand.InitialComponents`.
2. *(Integration test, PACK3-A005 Test 2)* Area authoring end-to-end test passes with the
   backdoor fully removed.

---

### PACK3-A005 — ACL Verification Tests

**Design Reference:** DESIGN.md §3.F

**Scope:**

Three separate tests proving the clean ACL path:

**Test 1 — Boundary unit test** (`Hrot.Map.Common.Tests`):
- New test `EgressTranslator_SynthesizesDdsPayload_StrictlyFromDomainEvent`.
- `RecordingDdsWriter` (simple test double that records `Write` calls).
- Instantiate `SpawnEntityCommandEgressTranslator(mockWriter, bus, geoTransform)` — no delegate.
- Publish `SpawnEntityCommand { TkbType = 1001, InitialComponents = [new EditablePolyline { Points = [(10,10)] }, new MapOverlayStyle { FillR = 255 }] }`.
- Call `translator.PollIngress(...)`.
- Assert `mockWriter.CallCount == 1`.
- Assert published `CreateEntityRequest` contains descriptor `d._d == EDescriptorType.dtMapVisualOverlay` with `Points.Count == 1`.

**Test 2 — E2E area authoring** (`Hrot.ClusterRunner.Integration.Tests`):
- New test `AreaAuthoring_EndToEnd_NoBackdoor_PublishesCorrectCreateEntityRequest`.
- `HrotRunnerHarness(RunMode.SimHost | RunMode.IG, domainId)`.
- Activate area placement tool via `ExConLogic` / `TestHook_SimulateMapClick`.
- Independent `DdsReader<CreateEntityRequest>` on the same domain.
- Assert exactly 1 `CreateEntityRequest` received with geometry payload; assert
  `MapCommandController._prebuiltRequests` does **not** exist (verified via compile absence or
  reflection).

**Test 3 — Offline editor isolation** (`Hrot.ClusterRunner.Integration.Tests`):
- New test `SpawnCommand_OfflineEditor_NoNetworkCallsMade`.
- `EditorHarness` (no DDS translator packs installed).
- Publish `SpawnEntityCommand`.
- Assert `repo.EntityCount == 1`.
- Assert mock DDS writer call count == 0.

**Files:**

| File | Change |
|------|--------|
| `Hrot.Map.Common.Tests/SpawnEntityCommandEgressTranslatorTests.cs` | New or extended test file |
| `Hrot.ClusterRunner.Integration.Tests/AclBackdoorEliminationTests.cs` | New test file (Tests 2 and 3) |

**Success Conditions:**

1. All three tests pass.
2. No DDS calls in Test 3.
3. Test 2 runs on a dynamically allocated domain ID (no port conflicts).

---

## Phase 4: NetworkGatewaySystem DRY Refactor

**Design Reference:** [DESIGN.md §Phase 4](./DESIGN.md#phase-4-networkgatewaysystem-dry-refactor)

---

### PACK3-N001 — Relocate `NetworkGatewaySystem` to `FDP.Toolkit.Replication`

**Design Reference:** DESIGN.md §4.B

**Scope:**

- Create `FDP/Toolkits/FDP.Toolkit.Replication/Systems/NetworkGatewaySystem.cs`.
- Content: the transport-agnostic logic from the Cyclone clone (PendingNetworkAck handling,
  ConstructionOrder processing, topology peer tracking, ELM promotion).
- Namespace: `FDP.Toolkit.Replication.Systems`.
- Zero CycloneDDS imports.

**Files:**

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/NetworkGatewaySystem.cs` | New (canonical) |

**Success Conditions:**

1. *(Compile)* File compiles with only `Fdp.Kernel`, `FDP.Toolkit.Lifecycle`, and
   `FDP.Toolkit.Replication.Components` references — no Cyclone namespace imports.
2. *(Unit test)* Instantiate `NetworkGatewaySystem` with a mock `INetworkTopology`.  Feed it
   a synthetic `PendingNetworkAck` and verify it calls `EntityLifecycleModule.MarkPeerReady`.

---

### PACK3-N002 — Delete Clones and Legacy Originals

**Design Reference:** DESIGN.md §4.B (relocation plan table)

**Scope:**

- Delete `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/NetworkGatewaySystem.cs`.
- Delete `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/NetworkGatewayModule.cs`.
- Delete legacy `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewaySystem.cs`.
- Delete legacy `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewayModule.cs`.
- Remove any `using` directives referencing the deleted Cyclone-local namespace from other
  files in `ModuleHost.Network.Cyclone`.

**Files:**

| File | Change |
|------|--------|
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/NetworkGatewaySystem.cs` | Delete |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/NetworkGatewayModule.cs` | Delete |
| `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewaySystem.cs` | Delete |
| `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewayModule.cs` | Delete |

**Constraints:**
- Must be done **after** PACK3-N001 and PACK3-N003 are confirmed to compile.

**Success Conditions:**

1. *(Compile)* Entire solution compiles after the four deletions.
2. *(No duplicate symbols)* `grep -r "class NetworkGatewaySystem"` returns exactly one result
   (the new toolkit file).

---

### PACK3-N003 — Rewire `CycloneNetworkModule`

**Design Reference:** DESIGN.md §4.C

**Scope:**

- In `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs`:
  - Replace `using ModuleHost.Network.Cyclone.Systems;` (or whichever local using referenced
    the clone) with `using FDP.Toolkit.Replication.Systems;`.
  - Ensure `_gatewaySystem = new NetworkGatewaySystem(...)` now resolves to the toolkit class.
  - Remove any references to the deleted `NetworkGatewayModule`.

**Files:**

| File | Change |
|------|--------|
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs` | Update using, constructor |

**Success Conditions:**

1. *(Compile)* `CycloneNetworkModule` compiles cleanly after the using swap.
2. *(Regression)* All distributed integration tests that use `CycloneNetworkModule`
   (e.g. ghost promotion tests) continue to pass.

---

### PACK3-N004 — `NetworkGatewaySystem` Integration Test

**Design Reference:** DESIGN.md §4.D

**Scope:**

- Add test class `NetworkGatewayIntegrationTests` to `Hrot.ClusterRunner.Integration.Tests`.
- Single test `GenericNetworkGateway_ResolvesReliableInit_AcrossCycloneTransport`:
  1. Allocate unique `domainId` (thread-safe counter, starting at 350).
  2. `HrotRunnerHarness(RunMode.SimHost | RunMode.IG, domainId)`.
  3. Publish `SpawnEntityCommand { TkbType = ..., InitType = ReliableInitType.AllPeers,
     InitialComponents = [...] }` on SimHost bus.
  4. `PumpUntil` SimHost `NetworkEntityMap` contains the entity (timeout: 60 frames).
  5. `PumpUntil` SimHost entity reaches `EntityLifecycle.Active` (timeout: 150 frames).
  6. `PumpUntil` IG entity reaches `EntityLifecycle.Active` (timeout: 150 frames).
  7. Assert both conditions true.
- Decorated with `[Collection("LogCapture")]`.

**Files:**

| File | Change |
|------|--------|
| `Hrot.ClusterRunner.Integration.Tests/NetworkGatewayIntegrationTests.cs` | New test file |

**Success Conditions:**

1. *(Test passes)* Both SimHost and IG entities reach `EntityLifecycle.Active`.
2. *(Architecture proof)* The test compiling and passing after PACK3-N002 proves
   `CycloneNetworkModule` is correctly using the Replication toolkit system.
3. *(Isolation)* Uses its own incremented domain ID; no interference with other tests.

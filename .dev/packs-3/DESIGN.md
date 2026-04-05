# DESIGN.md — Scenario File Support, ACL Hardening & Network DRY Refactor (`packs-3`)

## Background and Vision

With `packs-2` delivering the **HROT Editor** composition root, the shared
`Hrot.ScenarioEditor` Logic Pack, and the Feature Switch that blends offline and distributed
operation, `packs-3` focuses on **completing the full scenario authoring lifecycle**:

1. **Scenario files become self-contained**: Dynamic ECS entities **plus** the static environment
   assets they depend on (road networks, LOS obstacles) are described together in a single
   JSON file through a new application-level `"Zones"` section.
2. **Urban Combat demo becomes a data-driven scenario**: The existing programmatic demo is
   auto-converted to JSON, gaining the ability to be loaded/edited in the HROT Editor,
   previewed, saved, and run inside the full distributed cluster state machine.
3. **CGF component registry is centralised**: The ad-hoc per-application component registration
   in `CgfApplication` is replaced by a proper `CgfComponentRegistry`, matching the pattern
   already used by SimHost.
4. **ACL backdoor is eliminated**: The hidden `tryGetPrebuilt` side-channel that smuggles raw
   `CreateEntityRequest` DTOs from `MapCommandController` through the egress translator is
   fully removed.  Map tools emit only pure FDP domain events; the ACL translates them.
5. **NetworkGatewaySystem duplication is resolved**: The copy-pasted reliable-initialisation
   state machine in the Cyclone transport pack is deleted and replaced by the canonical,
   transport-agnostic implementation promoted into `FDP.Toolkit.Replication`.

### Guiding Principles

- **Anti-Corruption Layer (ACL):** The FDP engine/toolkits know nothing about JSON file paths,
  scenario envelope schemas, or CycloneDDS structs. Any translation belongs at the HROT
  application boundary.
- **Strongly-typed DTOs — no magic strings:** Every JSON section is modelled by a C# DTO class.
  Case-insensitive JSON serialisation options replace `[JsonPropertyName]` clutter.
- **Strict application / engine separation:** The FDP toolkit layer receives in-memory data
  structs (e.g. `ZoneEnvironmentData`). The application layer owns file I/O, DTO parsing, and
  path resolution.
- **Shared validation logic:** Scenario correctness checks are extracted into standalone
  `Validator` classes shared by the original programmatic test and the new file-driven
  cluster lifecycle test.
- **Headless, autonomous CI tests:** Every architectural boundary is proven by a deterministic,
  memory-bus-speed (or loopback-DDS) integration test that runs without human interaction.

---

## Pack Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                  HROT Application Layer                      │
│  HrotScenarioEnvelopeDto  ZoneDefinitionDto  ZoneObstacleDto │
│  HrotScenarioLoadHandler  HrotEditLoadHandler                │
│  ScenarioFileService (save with Zones)                       │
│  ZoneManagerService (parse DTOs → call FDP loaders → inject) │
└──────────────────────────┬──────────────────────────────────┘
                           │  in-memory FDP data structs only
┌──────────────────────────▼──────────────────────────────────┐
│                  FDP Toolkit / Engine Layer                   │
│  ZoneEnvironmentData (ECS singleton)                         │
│  RoadNetworkLoader   RoadNetworkBlob                         │
│  CarKinematicsSystem (reads singleton, not ctor param)       │
│  ScenarioSerializer  (unchanged — only sees Entities block)  │
│  NetworkGatewaySystem  (promoted to FDP.Toolkit.Replication) │
└─────────────────────────────────────────────────────────────┘
```

---

## Phase 0: CGF Component Registry Hardening

**Goal:** Replace the ad-hoc per-component registration in `CgfApplication`'s constructor with a
single centralised `CgfComponentRegistry`, matching the `SimHostComponentRegistry` pattern and
reducing onboarding friction for the CGF node.

### 0.A — Create `CgfComponentRegistry`

**Context:** `CgfApplication.cs` currently registers dozens of ECS components individually inside
its constructor.  This violates the framework's own convention established by
`SimHostComponentRegistry` (and analogous registries for other nodes).  The registry must cover
three tiers:

| Tier | Contents |
|------|----------|
| Shared base | `HrotSharedComponentRegistry.RegisterAll(world)` — network, geo, lifecycle |
| Cognitive / kinematic | Behaviour-tree, HSM, intent channels, vehicle state (previously scattered in constructor) |
| IG presentation | `EntityInfo`, `IgHealthState`, `IgSymbolOverride`, `HistoryTrail`, etc. (required by the `EntityStatesIngressPack`) |

**Decision:** The registry lives inside `Hrot.CGF` (not `Hrot.SimHost`) so the Brain node can
self-configure without a `Hrot.SimHost` project reference.

**Note:** A later pass should move `CognitiveComponentRegistry` and `KinematicComponentRegistry`
out of `Hrot.SimHost` into `Hrot.Common` so CGF can reference them directly; that migration is
**out of scope** for `packs-3`.

---

## Phase 1: Urban Combat Scenario Extraction & Shared Validation

**Goal:** Transform the programmatic `UrbanCombatNewScenario` into a first-class data-driven
scenario that (a) can be authored in the HROT Editor, (b) can be saved/loaded as JSON, and
(c) proves the full Editor Preview/Rewind lifecycle via a headless integration test.  A second
headless integration test drives the auto-extracted JSON through the complete distributed cluster
state machine from `Idle` → `LoadingLive` → `OperatingLive` → validation.

### 1.A — Extract `UrbanCombatValidator`

**Context:** The original `UrbanCombatNewScenario.EvaluateTick` caches raw `Entity` memory
handles during `Configure()`.  After a scenario is serialised to JSON and loaded back, the ECS
regenerates those handles, breaking the validator.  The fix is to extract the latch logic into a
separate class that resolves actors dynamically via `TkbIdentity` components, which **are**
preserved by `ScenarioSerializer`.

**Contract:**
- `UrbanCombatValidator.EvaluateTick(uint tick, EntityRepository world) → bool` — returns
  `true` on success, throws `ScenarioFailureException` on timeout.
- Internally resolves `TkbMilitaryApc (2001)` and `TkbInsurgent (2003)` via TkbIdentity query
  each tick (no cached handles).
- Sequential latches: `_latchAmbushFired` → `_latchApcHalted` → `_latchInsurgentHit` →
  `_latchInsurgentKilled` → success.
- Tick budget: 600 frames before failure.

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatValidator.cs`

### 1.B — Simplify `UrbanCombatNewScenario`

Update `UrbanCombatNewScenario.EvaluateTick` to delegate to the new `UrbanCombatValidator`
instance, eliminating duplicated latch fields.

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`

### 1.C — Editor Preview / Rewind Integration Test

**Context:** The `EditorHarness` provides a headless, DDS-free composition root for
`ScenarioEditorModule`.  This test proves the memory-snapshot capture (`LoadingPreview`) and
rewind (`UnloadingPreview`) lifecycle, plus `SaveScenario` / `LoadScenario` file round-trip.

**Test class:** `EditorPreviewAndSaveIntegrationTests` in
`Hrot.ClusterRunner.Integration.Tests`

**Lifecycle exercised:**

```
NewScenario → SpawnEntity → MoveEntity  ← edit state
→ LoadingPreview  (snapshot captured)
→ OperatingPreview (move entity to 999f)
→ UnloadingPreview (entity snaps back to 100f)
→ SaveScenario  →  NewScenario  →  LoadScenario
→ Assert entity at 100f, count == 1
```

**Key assertions:**
1. After `UnloadingPreview`: `SimTransform.Position.X == 100f`.
2. After `LoadScenario`: entity count == 1, position == 100f.
3. Uses `PreviewClusterOpHandler` directly (`TargetState 20 → LoadingPreview`,
   `TargetState 22 → UnloadingPreview`).

### 1.D — Urban Combat File Lifecycle Integration Test

**Context:** Proves that an auto-extracted Urban Combat JSON scenario can be loaded by the full
distributed cluster (Orchestrator + SimHost + CGF) and that the exact same ambush narrative
succeeds.

**Test class:** `UrbanCombatFileLifecycleTests` in
`Hrot.ClusterRunner.Integration.Tests`

**Lifecycle:**

```
1. Auto-extract:   spin up offline EntityRepository, run UrbanCombatNewScenario.Configure(),
                   serialize via ScenarioSerializer, wrap in HrotScenarioEnvelopeDto, write to
                   temp staging dir.
2. Boot cluster:   HrotRunnerHarness (Orchestrator + SimHost, deterministic 1/60 s),
                   CgfHarness (same domain).
3. Transition:     ClusterOpRequest TargetState=31 (OperatingLive) with ScenarioId pointing
                   to the extracted file.
4. Pump:           up to 800 frames.
5. Validate:       UrbanCombatValidator shared with the original test.
```

**Key properties:**
- Deterministic (`RunnerOptions.Deterministic = true`, `FixedDeltaSeconds = 1f/60f`).
- `CgfHarness` on the same loopback domain — provides Brain (AI) node.
- Validator resolves entities via `TkbIdentity` — survives serialisation.
- Temp staging dir cleaned up in `Dispose`.

---

## Phase 2: Zone Definitions in Scenario Files

**Goal:** Allow scenario JSON files to declare the static environment (road networks, cylindrical
LOS obstacles) they require.  The scenario loader resolves these assets and injects them into the
FDP engine as ECS singletons / unmanaged entities before any dynamic entity is spawned.

### 2.A — Architecture and Separation of Concerns

```
┌──────────── HROT Application Layer ─────────────────────────┐
│  HrotScenarioEnvelopeDto          (root envelope DTO)        │
│  ScenarioHeaderDto                (Header section DTO)       │
│  ZoneDefinitionDto                (per-zone: road + obstacles)│
│  ZoneObstacleDto                  (X, Y, Radius)             │
│                                                              │
│  ZoneManagerService : IZoneManagerService                    │
│    → calls RoadNetworkLoader (FDP toolkit)                   │
│    → calls repo.SetSingleton<ZoneEnvironmentData>            │
│    → spawns PhysicsCollider entities per obstacle            │
│                                                              │
│  HrotScenarioLoadHandler          (LoadingLive)              │
│  HrotEditLoadHandler              (LoadingEdit)              │
│    → deserialise envelope DTO                                │
│    → call ZoneManagerService before ScenarioSerializer       │
│                                                              │
│  ScenarioFileService.SaveScenario                            │
│    → ScenarioSerializer produces FDP DOM                     │
│    → append Zones node from ZoneManagerService               │
│    → serialise HrotScenarioEnvelopeDto (System.Text.Json)    │
└──────────── ACL boundary ───────────────────────────────────┘
┌──────────── FDP Toolkit / Engine Layer ─────────────────────┐
│  ZoneEnvironmentData (ECS singleton struct, ComponentId 180) │
│    RoadNetworkBlob RoadNetwork;                              │
│  RoadNetworkLoader.LoadFromJson(path)                        │
│  CarKinematicsSystem — reads ZoneEnvironmentData singleton   │
│  SimTransform + PhysicsCollider entities (obstacles)        │
│  ScenarioSerializer — unchanged, sees only Entities block    │
└─────────────────────────────────────────────────────────────┘
```

### 2.B — JSON Scenario Envelope Format

Scenario files produced by the HROT application layer now have four top-level keys:

```jsonc
{
  "Header": {
    "subsystemType": "Hrot.Scenario",  // camelCase, case-insensitive on read
    "schemaVersion": 1
  },
  "Zones": {
    "urban_combat_zone": {
      "roadNetworkPath": "Assets/sample_road.json",
      "terrainDatabaseId": "terrain_basic",
      "obstacles": [
        { "x": 50.0, "y": 25.0, "radius": 10.0 },
        { "x": -10.0, "y": -10.0, "radius": 5.0 }
      ]
    }
  },
  "Entities": { /* FDP engine DOM — unchanged */ }
}
```

**No `[JsonPropertyName]` attributes** — DTOs use plain C# property names; a single
`JsonSerializerOptions` instance (`PropertyNameCaseInsensitive = true`,
`PropertyNamingPolicy = JsonNamingPolicy.CamelCase`) handles both reading and writing.

### 2.C — DTO Definitions (Application Layer)

All DTOs live in the `Hrot.Map.Common.Scenario` namespace (project `Hrot.Map.Common`).

**`HrotScenarioEnvelopeDto`**
```csharp
public class HrotScenarioEnvelopeDto
{
    public ScenarioHeaderDto Header { get; set; } = new();
    public Dictionary<string, ZoneDefinitionDto>? Zones { get; set; }
    public JsonObject? Entities { get; set; }  // raw FDP DOM — not parsed by app layer
}
```

**`ScenarioHeaderDto`**
```csharp
public class ScenarioHeaderDto
{
    public string SubsystemType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
}
```

**`ZoneDefinitionDto`** — one road network + arbitrary obstacles per named zone.
```csharp
public class ZoneDefinitionDto
{
    public string? RoadNetworkPath { get; set; }
    public string? TerrainDatabaseId { get; set; }
    public List<ZoneObstacleDto>? Obstacles { get; set; }
}
```

**`ZoneObstacleDto`** — 2.5D cylinder matching `PhysicsCollider` capabilities.
```csharp
public class ZoneObstacleDto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Radius { get; set; }
}
```

**Design notes:**
- One road network per zone — no merging logic required (KISS).
- Multiple zones per scenario file are supported (the `Zones` dictionary).
- Because only cylinders are supported by the engine's `Intersection2D.RaycastCircle`
  narrow-phase solver, 3D oriented bounding boxes are **not** represented.  Obstacles are
  upright, non-oriented cylinders (2D position + radius).
- The `Entities` property holds the raw `JsonObject` produced by `ScenarioSerializer`; the
  application layer treats it as an opaque blob and does not inspect its contents.

### 2.D — FDP Engine Contract: `ZoneEnvironmentData` ECS Singleton

A new ECS singleton struct lives in (or adjacent to) `FDP.Toolkit.Geographic` / a new
`Fdp.Kernel.Environment` namespace.

```csharp
[ComponentId(XX)]  // Assign an unused ID from the Toolkit expansion block (20–79) in
                   // GlobalComponentIds.cs.  IDs 160–199 are reserved for application-level
                   // components and must NOT be used here.
public struct ZoneEnvironmentData
{
    public RoadNetworkBlob RoadNetwork;
    // Future: ITerrainProvider Terrain;
}
```

`CarKinematicsSystem.OnUpdate()` reads `ZoneEnvironmentData` with an **empty-blob fallback**
so that all non-road-network vehicle physics (rigid-body integration, steering limits, RVO
collision avoidance) continue to execute even when no zone has been loaded.  The system must
**never** return early based solely on the singleton's absence:

```csharp
var roadNetwork = World.HasSingleton<ZoneEnvironmentData>()
    ? World.GetSingleton<ZoneEnvironmentData>().RoadNetwork
    : default; // empty blob — safe for non-road scenarios
```

**Benefit:** The road network can be hot-swapped between scenario loads by simply overwriting
the singleton, without tearing down and rebuilding the `ModuleHostKernel`.  The old singleton
must be `Dispose()`d before overwriting to free its `NativeArray` memory (see §2.E).

### 2.E — `IZoneManagerService` and `ZoneManagerService`

The service is the **translation pivot** between the application-layer DTOs and the FDP engine.

```csharp
// Hrot.Map.Common
public interface IZoneManagerService
{
    void LoadZones(EntityRepository repo, Dictionary<string, ZoneDefinitionDto> zones);
    Dictionary<string, ZoneDefinitionDto> GetActiveZones();
}
```

**`LoadZones` algorithm (per zone entry):**
1. If `RoadNetworkPath` is non-null:
   a. **Dispose before overwrite:** because `RoadNetworkBlob` contains `NativeArray` fields
      allocated from unmanaged memory, overwriting the singleton without disposing the previous
      blob causes a permanent native-memory leak.  Always check and dispose first:
      ```csharp
      if (repo.HasSingleton<ZoneEnvironmentData>())
          repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Dispose();
      ```
   b. `RoadNetworkLoader.LoadFromJson(path)` → `repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob })`.
2. For each `ZoneObstacleDto` in `Obstacles` → create ECS entity with `SimTransform` and
   `PhysicsCollider` (`CollisionLayer = PhysicsConstants.EntityCollisionLayer`).  These static
   entities are automatically indexed by `SpatialHashSystem` and occlude LOS raycasts at zero
   additional cost.

**Dependency note:** `ZoneManagerService` lives in `Hrot.Map.Common`, which must add a
`<ProjectReference>` to `FDP.Toolkit.Physics.csproj` so that `PhysicsCollider` and
`PhysicsConstants` resolve at compile time.

**`GetActiveZones`** returns the data describing the currently loaded zones, used during save.

**Serialiser options instance** (defined once, reused everywhere at the HROT application
boundary):
```csharp
public static readonly JsonSerializerOptions HrotJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
};
```

### 2.F — Custom Load Handlers

Two existing FDP reference load handlers are replaced by HROT-specific implementations:

| Handler | Cluster state | File |
|---------|---------------|------|
| `HrotScenarioLoadHandler` | `LoadingLive` | `Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` |
| `HrotEditLoadHandler`     | `LoadingEdit` | `Hrot.ScenarioEditor/Handlers/HrotEditLoadHandler.cs` |

**Load sequence in both handlers (inside `CommitLoad`) — single JSON parse:**
1. `var dom = JsonNode.Parse(rawJson)?.AsObject();` — parse the string into a DOM **once**.
2. `var envelope = dom.Deserialize<HrotScenarioEnvelopeDto>(HrotJsonOptions);` — populate the
   DTO from the already-parsed DOM (no second string parse).
3. If `envelope?.Zones != null` → `_zoneManagerService.LoadZones(repo, envelope.Zones)`.
4. `_serializer.Deserialize(repo, dom);` — pass the **pre-parsed `JsonObject`** to the FDP
   toolkit's `JsonObject` overload.  This avoids re-parsing the (potentially large) scenario
   string a second time.

**Rationale:** Parsing a large scenario JSON string twice wastes significant heap and CPU.
`ScenarioSerializer` already exposes a `Deserialize(EntityRepository, JsonObject)` overload
for exactly this use-case; the handlers must use it.

### 2.G — `ScenarioFileService` Save with Zones

`ScenarioFileService.SaveScenario` is updated to build and serialise the full envelope:
1. `fdpDom = _serializer.Serialize(repo, new ScenarioHeader("Hrot.Scenario"))`.
2. Build `HrotScenarioEnvelopeDto` with `Header`, `Zones = _zoneManagerService.GetActiveZones()`,
   `Entities = fdpDom["Entities"]?.AsObject()`.
3. `JsonSerializer.Serialize(envelope, HrotJsonOptions)` → write to disk.

### 2.H — Zone Scenario Load Integration Test

**Test class:** `ZoneScenarioLoadIntegrationTests` in `Hrot.ClusterRunner.Integration.Tests`

**Protocol:**
1. Build `HrotScenarioEnvelopeDto` in memory with one named zone (`urban_combat_zone`) containing:
   - `RoadNetworkPath = "Assets/sample_road.json"`.
   - Two `ZoneObstacleDto` entries: `(50, 25, r=10)` and `(-10, -10, r=5)`.
2. Serialise to temp file using `HrotJsonOptions`.
3. `EditorHarness.LoadScenario(tempFile)` → `PumpFrames(5)`.
4. Assert `repo.HasSingleton<ZoneEnvironmentData>()` is `true`.
5. Assert `envData.RoadNetwork.Nodes.IsCreated` and `Segments.IsCreated`.
6. Query `PhysicsCollider + SimTransform` entities → assert count == 2, validate positions and
   radii for both obstacles.

---

## Phase 3: ACL Backdoor Elimination

**Goal:** Completely remove the hidden `tryGetPrebuilt` side-channel that allows
`MapCommandController` to bypass the FDP event bus and push pre-built `CreateEntityRequest` DDS
DTOs directly into the egress translator.  After this phase, the only data path for entity
creation is: Tool → `SpawnEntityCommand` → FDP Event Bus → `SpawnEntityCommandEgressTranslator`
→ `CreateEntityRequest` on the DDS wire.

### 3.A — Background: The Backdoor

The design requirement from `packs-2` was that map tools must emit pure FDP domain events
(`SpawnEntityCommand`) and the `SpawnEntityCommandEgressTranslator` must translate those events
to DDS independently.  Instead:

- `MapCommandController` still caches a pre-built `CreateEntityRequest` keyed by
  `SpawnEntityCommand.RequestId` in a `_prebuiltRequests` dictionary.
- `SpawnEntityCommandEgressTranslator` accepts a `tryGetPrebuilt` delegate via injection and,
  when it finds a pre-built payload for a command, **skips standard serialisation entirely**.
- `IgApplication.InitializeNetwork` wires these two together via an injected lambda.

### 3.B — `SpawnEntityCommandEgressTranslator` Cleanup

**Changes:**
- Remove the `_tryGetPrebuilt` field.
- Remove the constructor overload that accepts the delegate.
- In `PollIngress`, delete the bypass block; only the standard `BuildCreateEntityRequest(cmd)`
  serialisation path remains.

**The standard serialisation path** must fully cover area/route geometries.  The tool-side fix
(§3.E below) ensures `SpawnEntityCommand.InitialComponents` carries the geometry DTOs so the
translator can build the correct `CreateEntityRequest` without the side-channel.

### 3.C — `MapCommandController` DTO Cache Removal

**Changes:**
- Delete `_prebuiltRequests` dictionary.
- Delete `TryDequeuePrebuilt()` method.
- Update `OnAreaEntityCreated` to accept only pure domain data (`SpawnEntityCommand cmd`) and
  immediately publish it to the FDP event bus; it no longer stores or returns DDS structs.

### 3.D — `IgApplication.cs` Composition Root Cleanup

**Changes:**
- Remove the `MapCommandController? mapCmdCtrlRef` local variable.
- Remove the lambda capture.
- Construct `SpawnEntityCommandEgressTranslator` without the delegate argument.

### 3.E — `AreaAuthoringTool` InitialComponents Fix

Now that the backdoor is closed, the area and route authoring tools must place all geometry data
inside `SpawnEntityCommand.InitialComponents`, e.g.:

```csharp
new SpawnEntityCommand
{
    TkbType = ...,
    InitialComponents = new List<object>
    {
        new EditablePolyline { Points = polygon.ToList() },
        new MapOverlayStyle  { FillR = 255, FillG = 0, FillB = 0 }
    }
}
```

`SpawnEntityCommandEgressTranslator.BuildCreateEntityRequest` inspects `InitialComponents`,
finds the geometry types, and constructs the DDS descriptors (`dtMapVisualOverlay`,
`dtMapRoute`) without needing any side-channel.

### 3.F — ACL Verification Tests

Three tests prove the backdoor is gone and the clean path works:

**Test 1 — Boundary unit test** (`Hrot.Map.Common.Tests`):
- Instantiate `SpawnEntityCommandEgressTranslator` without any delegate.
- Publish `SpawnEntityCommand` with `EditablePolyline` + `MapOverlayStyle` in
  `InitialComponents`.
- Assert `RecordingDdsWriter.CallCount == 1`.
- Assert the published `CreateEntityRequest` contains a `dtMapVisualOverlay` descriptor with
  the correct point count.

**Test 2 — E2E area authoring** (`Hrot.ClusterRunner.Integration.Tests`):
- Use `HrotRunnerHarness` (SimHost + IG).
- `ExConLogic.ActivateTool(MapToolType.PlaceArea)`, simulate map click via
  `TestHook_SimulateMapClick`.
- Sniff the CycloneDDS wire with an independent `DdsReader<CreateEntityRequest>`.
- Assert a `CreateEntityRequest` with expected geometry arrives without the backdoor.

**Test 3 — Offline editor isolation** (`Hrot.ClusterRunner.Integration.Tests`):
- `EditorHarness` (DDS translators intentionally absent).
- Publish `SpawnEntityCommand`.
- Assert ECS entity count == 1.
- Assert the mock DDS writer (not installed in offline harness) received zero calls.

---

## Phase 4: NetworkGatewaySystem DRY Refactor

**Goal:** Eradicate the copy-pasted reliable-initialisation state machine duplicated inside the
`ModuleHost.Network.Cyclone` transport pack.  The canonical `NetworkGatewaySystem` (which is
purely ECS-domain, containing zero CycloneDDS calls) is promoted to `FDP.Toolkit.Replication`.
The Cyclone module is updated to reference it; the clone and the legacy `ModuleHost.Core`
originals are deleted.

### 4.A — Root Cause

The developer correctly extracted `NetworkGatewaySystem` out of `ModuleHost.Core`, but placed
the copy inside the Cyclone transport pack instead of the Replication toolkit.  The comment
admits: *"NOTE: This is a COPY of ModuleHost.Core.Network.NetworkGatewayModule …"*.  Because the
system is transport-agnostic (it only touches `PendingNetworkAck`, `ConstructionOrder`, and
`EntityLifecycleModule`), it belongs in `FDP.Toolkit.Replication.Systems`.

### 4.B — Relocation Plan

| File | Action |
|------|--------|
| `FDP.Toolkit.Replication/Systems/NetworkGatewaySystem.cs` | **New** — canonical location |
| `ModuleHost.Network.Cyclone/Systems/NetworkGatewaySystem.cs` | **Delete** (clone) |
| `ModuleHost.Network.Cyclone/Modules/NetworkGatewayModule.cs` | **Delete** (clone) |
| `ModuleHost.Core/Network/NetworkGatewaySystem.cs` | **Delete** (legacy original) |
| `ModuleHost.Core/Network/NetworkGatewayModule.cs` | **Delete** (legacy original — was slated for removal) |
| `ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs` | **Update** — use new namespace |

### 4.C — `CycloneNetworkModule` Rewiring

```csharp
using FDP.Toolkit.Replication.Systems;  // replaces Cyclone-local using

// In constructor:
_gatewaySystem = new NetworkGatewaySystem(
    101, _nodeMapper.LocalNodeId, _topology, _elm, _reliableInitTimeoutFrames);

// In RegisterSystems:
registry.RegisterSystem(_gatewaySystem);
```

The `CycloneNetworkModule` is now a pure composition root and transport bridge; it holds no
domain logic.

### 4.D — NetworkGateway Integration Test

**Test class:** `NetworkGatewayIntegrationTests` in `Hrot.ClusterRunner.Integration.Tests`

**Protocol:**
1. Boot `HrotRunnerHarness(RunMode.SimHost | RunMode.IG, domainId)`.
2. Publish `SpawnEntityCommand` with `ReliableInitType.AllPeers` on SimHost bus.
3. `PumpUntil` entity visible in `NetworkEntityMap`.
4. `PumpUntil` `EntityLifecycle.Active` on SimHost side (proves gateway processed the DDS ACK).
5. `PumpUntil` `EntityLifecycle.Active` on IG side (proves ghost promotion also worked).

**Why the test proves the architecture:** Because we physically deleted the Cyclone-local copy of
`NetworkGatewaySystem`, if this test compiles and passes the `CycloneNetworkModule` is
definitively proven to be wiring the generic toolkit system correctly.

---

## Cross-Cutting Concerns

### JSON Serialisation Convention

A single `HrotJsonOptions` instance (defined in a static class, e.g.
`Hrot.Map.Common.HrotSerializerOptions`) is used at every HROT application boundary for DTO
serialisation/deserialisation.  This instance sets:
- `PropertyNameCaseInsensitive = true` — tolerates both camelCase and PascalCase JSON.
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` — writes camelCase keys.
- `DefaultIgnoreCondition = WhenWritingNull` — omits null fields.
- `WriteIndented = true` — human-readable output.

No `[JsonPropertyName]` attributes are used on DTO classes.

### Test Infrastructure

| Harness | Used by |
|---------|---------|
| `EditorHarness` | Phase 1.C (preview/save), Phase 2.H (zone load), Phase 3.F test 3 |
| `HrotRunnerHarness` (Orchestrator + SimHost) | Phase 1.D (full lifecycle), Phase 3.F test 2, Phase 4.D |
| `CgfHarness` | Phase 1.D (full lifecycle) |

### Dependency Order

Phases must be completed in order where noted:
- Phase 1.A (Validator) must precede 1.B and 1.D.
- Phase 2.C (DTOs) must precede 2.E (ZoneManagerService) and 2.F (Load Handlers).
- Phase 2.D (ZoneEnvironmentData + `CarKinematicsSystem` refactor) must precede 2.E and 2.H.
- Phase 3.E (tool InitialComponents fix) must be done together with 3.B and 3.C, since closing
  the backdoor without fixing the tools would break area authoring.

---

## Summary of Deliverables

| Phase | Key Deliverables |
|-------|-----------------|
| 0 | `CgfComponentRegistry` centralising Brain-node component registration |
| 1 | `UrbanCombatValidator`, simplified `UrbanCombatNewScenario`, two integration tests |
| 2 | `ZoneEnvironmentData`, 4× DTOs, `IZoneManagerService` + `ZoneManagerService`, 2× load handlers, `ScenarioFileService` update, 1 integration test |
| 3 | Purged `tryGetPrebuilt` backdoor, fixed `AreaAuthoringTool`, 3 verification tests |
| 4 | Promoted `NetworkGatewaySystem`, deleted clones, rewired `CycloneNetworkModule`, 1 integration test |

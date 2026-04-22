# BATCH-03: Zone Service, Load Handlers, Save & Integration Test

**Batch Number:** BATCH-03  
**Tasks:** PACK3-Z003, PACK3-Z004, PACK3-Z005, PACK3-Z006  
**Phase:** Phase 2 completion (Zone Definitions in Scenario Files)  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (CgfComponentRegistry), BATCH-02 (ZoneEnvironmentData, DTOs, ZoneDefinitionDto, HrotJsonOptions)

---

## 📋 Onboarding & Workflow

### Developer Instructions

BATCH-02 delivered:
- `ZoneEnvironmentData` ECS singleton (ComponentId 38, Toolkit expansion block)
- `CarKinematicsSystem` refactored to read singleton (no ctor param)
- `HrotScenarioEnvelopeDto`, `ScenarioHeaderDto`, `ZoneDefinitionDto`, `ZoneObstacleDto` DTOs
- `HrotSerializerOptions` with `HrotJsonOptions`

This batch **wires Phase 2 end-to-end**: the `ZoneManagerService` that bridges DTOs to ECS,
the two custom load handlers that replace the FDP reference handlers, the updated
`ScenarioFileService.SaveScenario`, and the zone scenario load integration test.

After this batch, a scenario JSON file with a `"Zones"` section can be loaded by both the
full cluster (`HrotScenarioLoadHandler`) and the offline editor (`HrotEditLoadHandler`), road
networks and obstacles will be injected into ECS, and `SaveScenario` will round-trip the zone
data correctly.

**Important P2 debt item (from BATCH-02 review):** Record in the next batch's instructions
whether the system-ordering audit for flat `_kernelGroup` was started. This batch does NOT
include that audit; mention it in your developer insights if you observe related issues.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/packs-3/ONBOARDING.md`
3. **Design:** `.dev/packs-3/DESIGN.md` — read §Phase 2 (§2.E, §2.F, §2.G, §2.H) carefully
4. **Task Definitions:** `.dev/packs-3/TASK-DETAIL.md` — PACK3-Z003, PACK3-Z004, PACK3-Z005, PACK3-Z006
5. **Previous Review:** `.dev/packs-3/reviews/BATCH-02-REVIEW.md`
6. **Delivered DTOs:** `Hrot.Map.Common/Scenario/`, `Hrot.Map.Common/HrotSerializerOptions.cs`
7. **Delivered Singleton:** `FDP/Toolkits/FDP.Toolkit.CarKinem/ZoneEnvironmentData.cs`

### Source Code Locations
- **Hrot.Map.Common project:** `Hrot.Map.Common/` — add `IZoneManagerService` + `ZoneManagerService` here
- **Hrot.Map.Common.csproj:** Must add `<ProjectReference>` to `FDP.Toolkit.Physics` for `PhysicsCollider` + `PhysicsConstants`
- **FDP Toolkit Physics:** `FDP/Toolkits/FDP.Toolkit.Physics/` — find `PhysicsCollider`, `PhysicsConstants`
- **RoadNetworkLoader:** `FDP/Toolkits/FDP.Toolkit.Geographic/` — find `RoadNetworkLoader.LoadFromJson(path)` + `RoadNetworkBlob`
- **Reference scenario load handler (FDP):** find `ReferenceScenarioLoadHandler` and `ReferenceEditLoadHandler` in `FDP/Toolkits/FDP.Toolkit.Orchestration/` or similar
- **ScenarioSerializer JSON overload:** Look for `ScenarioSerializer.Deserialize(EntityRepository, JsonObject)` — this overload accepts a pre-parsed DOM
- **Hrot.SimHost orchestration handlers:** `Hrot.SimHost/Orchestration/` or `Hrot.ClusterRunner/` — look for where `ReferenceScenarioLoadHandler` is currently registered
- **Hrot.ScenarioEditor project:** `Hrot.ScenarioEditor/` — find `ScenarioFileService.cs` and `HrotEditLoadHandler` location
- **EditorHarness:** `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`
- **Existing Editor tests:** `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs` — must stay passing

### Report Submission
**When done, submit your report to:**  
`.dev/packs-3/reports/BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev/packs-3/questions/BATCH-03-QUESTIONS.md`

---

## Context

Phase 2 is the core zone-loading pipeline. The architectural requirement is:

```
JSON file → HrotScenarioLoadHandler/HrotEditLoadHandler
         → single JsonNode.Parse()
         → [DTO] HrotScenarioEnvelopeDto.Zones → [Service] IZoneManagerService.LoadZones
         → [ECS] ZoneEnvironmentData singleton + PhysicsCollider entities
         → ScenarioSerializer.Deserialize(repo, dom)  ← dom already parsed, no re-parse
```

The **strict ACL boundary**: the FDP toolkit receives only in-memory structs (`ZoneEnvironmentData`,
`PhysicsCollider`). File paths, JSON strings, and app-layer DTOs never reach FDP toolkit code.

---

## 🎯 Batch Objectives

1. Implement `IZoneManagerService` + `ZoneManagerService` — the ACL translation pivot.
2. Create `HrotScenarioLoadHandler` (`LoadingLive`) and `HrotEditLoadHandler` (`LoadingEdit`) replacing the FDP reference handlers.
3. Update `ScenarioFileService.SaveScenario` to serialise zone data into the envelope.
4. Prove the full pipeline with `ZoneScenarioLoadIntegrationTests`.

---

## ✅ Tasks

### Task 1: `IZoneManagerService` and `ZoneManagerService` (PACK3-Z003)

**Files:**
- `Hrot.Map.Common/Services/IZoneManagerService.cs` — **NEW**
- `Hrot.Map.Common/Services/ZoneManagerService.cs` — **NEW**
- `Hrot.Map.Common/Hrot.Map.Common.csproj` — add `<ProjectReference>` to `FDP.Toolkit.Physics`

**Task Definition:** See [TASK-DETAIL.md — PACK3-Z003](../TASK-DETAIL.md#pack3-z003--izonmanagerservice-and-zonemanagerservice)

**Key Requirements:**

**Interface:**
```csharp
public interface IZoneManagerService
{
    void LoadZones(EntityRepository repo, Dictionary<string, ZoneDefinitionDto> zones);
    Dictionary<string, ZoneDefinitionDto> GetActiveZones();
}
```

**`LoadZones` implementation (per zone entry):**
1. If `RoadNetworkPath` is non-null:
   a. **Dispose existing singleton before overwrite** (NativeArray memory leak prevention):
      ```csharp
      if (repo.HasSingleton<ZoneEnvironmentData>())
          repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork.Dispose();
      ```
   b. `RoadNetworkLoader.LoadFromJson(path)` → `repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = blob })`.
2. For each `ZoneObstacleDto` in `Obstacles`, spawn one ECS entity with:
   - `SimTransform { Position = new Vector3(obs.X, obs.Y, 0) }`
   - `PhysicsCollider { Radius = obs.Radius, CollisionLayer = PhysicsConstants.EntityCollisionLayer }`

**`GetActiveZones`:** Returns the dictionary of zones as passed to the last `LoadZones` call.

**`Hrot.Map.Common.csproj` update:** Add project reference to `FDP.Toolkit.Physics.csproj` so
`PhysicsCollider` and `PhysicsConstants` resolve at compile time.

**Tests Required (unit tests in `Hrot.Map.Common.Tests`):**
- Test 1: `LoadZones` with `RoadNetworkPath = "Assets/sample_road.json"` → `repo.HasSingleton<ZoneEnvironmentData>()` is `true`, `RoadNetwork.Nodes.IsCreated`.
- Test 2 (memory safety): `LoadZones` twice with different paths → first `RoadNetwork.Nodes.IsCreated` returns `false` after second call (proves dispose).
- Test 3: `LoadZones` with 2 obstacles → entities with `PhysicsCollider` count == 2.
- Test 4: `GetActiveZones()` after `LoadZones` → returned key matches loaded zone name.

---

### Task 2: Custom Load Handlers (PACK3-Z004)

**Files:**
- `Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` — **NEW**
- `Hrot.ScenarioEditor/Handlers/HrotEditLoadHandler.cs` — **NEW**
- Composition roots: replace `ReferenceScenarioLoadHandler` with `HrotScenarioLoadHandler` for
  `LoadingLive`; replace `ReferenceEditLoadHandler` with `HrotEditLoadHandler` for `LoadingEdit`

**Task Definition:** See [TASK-DETAIL.md — PACK3-Z004](../TASK-DETAIL.md#pack3-z004--custom-load-handlers-hrotscenarioloadhandler-hroteditloadhandler)

**Key Requirements — Single JSON Parse Pattern:**
```csharp
public void CommitLoad(EntityRepository repo, string rawJson)
{
    // 1. Parse ONCE into a DOM
    var dom = JsonNode.Parse(rawJson)?.AsObject();
    
    // 2. Populate DTO from already-parsed DOM (no second string parse)
    var envelope = dom?.Deserialize<HrotScenarioEnvelopeDto>(HrotJsonOptions.Options);
    
    // 3. Load zones before entities
    if (envelope?.Zones != null)
        _zoneManagerService.LoadZones(repo, envelope.Zones);
    
    // 4. Pass pre-parsed DOM to serialiser (no third string parse)
    _serializer.Deserialize(repo, dom);
}
```

**Behaviour when `"Zones"` is null/absent:** no-op — backward compatible with old scenario files.

**Tests Required (unit tests):**
- Test 1: Feed JSON without `"Zones"` key → `LoadZones` is **not** called; `ScenarioSerializer.Deserialize` completes normally.
- Test 2: Feed JSON with valid `"Zones"` key → `LoadZones` called **once** before `Deserialize`.
- Test 3 (regression): `EditorFileIOIntegrationTests` continue to pass.

---

### Task 3: `ScenarioFileService` Save with Zone Support (PACK3-Z005)

**File:** `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` — **UPDATE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-Z005](../TASK-DETAIL.md#pack3-z005--scenariofileservice-save-with-zone-support)

**Key Requirements:**
1. Inject `IZoneManagerService` into `ScenarioFileService` constructor.
2. `SaveScenario` sequence:
   - `fdpDom = _fdpSerializer.Serialize(repo, new ScenarioHeader("Hrot.Scenario"))`.
   - `activeZones = _zoneManagerService.GetActiveZones()` — if empty dict, pass `null` to DTO.
   - Build `HrotScenarioEnvelopeDto { Header = ..., Zones = (activeZones.Count > 0 ? activeZones : null), Entities = fdpDom["Entities"]?.AsObject() }`.
   - `JsonSerializer.Serialize(envelope, HrotJsonOptions.Options)` → write to disk.

**Tests Required:**
- Test 1: Save scenario with active zone → deserialise written file → `Zones` is non-null with expected zone.
- Test 2: No active zones → serialised file has no `"Zones"` key (or null, per `WhenWritingNull`).

---

### Task 4: Zone Scenario Load Integration Test (PACK3-Z006)

**File:** `Hrot.ClusterRunner.Integration.Tests/ZoneScenarioLoadIntegrationTests.cs` — **NEW**

**Task Definition:** See [TASK-DETAIL.md — PACK3-Z006](../TASK-DETAIL.md#pack3-z006--zone-scenario-load-integration-test)

**Key Requirements:**
- `[Collection("EditorOfflineTests")]`
- `IDisposable.Dispose` deletes temp file
- Single test method `LoadScenario_WithZoneDefinition_PopulatesRoadNetworkAndObstacles`
- **Steps:**
  1. Build `HrotScenarioEnvelopeDto` in code: 1 zone `"urban_combat_zone"`, `RoadNetworkPath = "Assets/sample_road.json"`, obstacles: `(50, 25, r=10)` and `(-10, -10, r=5)`.
  2. Serialise to temp file with `HrotJsonOptions`.
  3. `EditorHarness.LoadScenario(tempFile)` → `PumpFrames(5)`.
  4. Assert `repo.HasSingleton<ZoneEnvironmentData>()` is `true`.
  5. Assert `envData.RoadNetwork.Nodes.IsCreated` and `Segments.IsCreated`.
  6. Query `With<PhysicsCollider>().With<SimTransform>()` → assert count == 2.
  7. Validate obstacle 1: `Position.X == 50f`, `Position.Y == 25f`, `Radius == 10f`.
  8. Validate obstacle 2: `Position.X == -10f`, `Position.Y == -10f`, `Radius == 5f`.

**Tests Required:**
- All 8 assertions above pass.
- Zero DDS calls (uses `EditorHarness` only).
- Runs in < 5 seconds.

---

## 🧪 Testing Requirements

### Minimum Test Coverage

| Task | Test Type | Minimum |
|------|-----------|---------|
| Z003 | Unit (Hrot.Map.Common.Tests) | 4 tests (singleton, dispose, obstacles×2, GetActiveZones) |
| Z004 | Unit (mock-based) | 2 unit + regression EditorFileIOIntegrationTests |
| Z005 | Unit/integration | 2 tests (zones present/absent) |
| Z006 | Integration (EditorHarness) | 1 test, 8 assertions |

### Task Order Recommendation

1. **Z003** first — the service is a dependency of Z004 and Z005.
2. **Z004** — create handlers and wire them.
3. **Z005** — update `ScenarioFileService.SaveScenario`.
4. **Z006** — integration test that exercises the full pipeline.

### Test-Driven Task Progression

**MANDATORY WORKFLOW — Test-Driven Task Progression:**

> For each task, before writing production code:
> 1. Read the existing tests to understand what currently passes.
> 2. Write the failing test(s) first (unit or integration as specified).
> 3. Implement the production code to make the tests pass.
> 4. Run the full relevant test suite to confirm no regressions.
> 5. Only then mark the task done in your report.
>
> **Never consider a task complete until all its tests pass AND existing tests remain green.**

Run regressions before submitting:
```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-incremental
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EditorFileIO|ZoneScenario" --no-build
dotnet test Hrot.Map.Common.Tests --no-build
```

---

## 📊 Report Requirements

Submit your report to `.dev/packs-3/reports/BATCH-03-REPORT.md`.

```markdown
# BATCH-03 Report

## Implementation Summary
[Per-task: what was done]

## Tests Added
[List new test methods and files]

## Test Results
[Pass/fail counts, any skips]

## Developer Insights
1. **Issues Encountered:** What problems did you hit? How resolved?
2. **Weak Points Spotted:** Fragile or unclear areas.
3. **Design Decisions Beyond the Spec:** Any choices not explicitly stated?

## Deviations from Spec (if any)
[List with justification]
```

---

## ⚠️ Important Notes

1. **Do NOT start Phase 3 (ACL backdoor elimination) work** — that is BATCH-04.
2. **Single JSON parse discipline**: both handlers must parse the raw JSON string exactly once.
   Passing the pre-parsed `JsonObject` DOM both to `Deserialize<HrotScenarioEnvelopeDto>` and
   to `ScenarioSerializer.Deserialize` is a hard requirement — do not parse the string twice.
3. **Memory safety in `ZoneManagerService`**: always dispose the old `RoadNetworkBlob` before
   writing a new singleton. Missing this causes a NativeArray leak in tests that call `LoadZones`
   twice.
4. **FDP submodule**: all `Hrot.Map.Common` and `Hrot.SimHost`/`Hrot.ScenarioEditor` changes
   are in the parent repo. Only `ZoneEnvironmentData` lives in FDP/ — that was already committed
   in BATCH-02. You should not need to modify FDP/ in this batch.
5. **`Assets/sample_road.json`**: if this file does not exist in the test's working directory,
   the integration test (Z006) will fail. Check if there's an existing test asset directory with
   sample road networks, or place a minimal one in the test project. A minimal `sample_road.json`
   with at least one node and one segment is sufficient.

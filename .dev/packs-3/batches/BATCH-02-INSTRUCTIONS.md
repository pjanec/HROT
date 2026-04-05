# BATCH-02: Editor Preview/Rewind Tests, Urban Combat File Lifecycle & Zone Foundation

**Batch Number:** BATCH-02  
**Tasks:** PACK3-U003, PACK3-U004, PACK3-Z001, PACK3-Z002  
**Phase:** Phase 1 (U003, U004), Phase 2 foundation (Z001, Z002)  
**Estimated Effort:** 12–17 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (provides `UrbanCombatValidator`, `CgfComponentRegistry`)

---

## 📋 Onboarding & Workflow

### Developer Instructions

BATCH-01 completed:  `CgfComponentRegistry`, `UrbanCombatValidator`, `UrbanCombatNewScenario`
delegation, canonical `NetworkGatewaySystem` promotion, and clone deletions.

This batch closes Phase 1 (two integration tests) and lays the FDP/application foundation for
Phase 2 Zone support (`ZoneEnvironmentData` ECS singleton + `CarKinematicsSystem` refactor +
the first set of app-layer DTOs).

Phase 2 work in this batch is **incremental construction only** — no zone *loading* logic yet
(that is BATCH-03). The DTOs, the singleton struct, and the `CarKinematicsSystem` refactor must
compile cleanly and their unit tests must pass.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/packs-3/ONBOARDING.md`
3. **Design:** `.dev/packs-3/DESIGN.md` — read §Phase 1 (§1.C, §1.D) and §Phase 2 (§2.B, §2.C, §2.D)
4. **Task Definitions:** `.dev/packs-3/TASK-DETAIL.md` — PACK3-U003, PACK3-U004, PACK3-Z001, PACK3-Z002
5. **Previous Review:** `.dev/packs-3/reviews/BATCH-01-REVIEW.md`

### Source Code Locations
- **Editor Harness:** `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`
- **Existing Editor tests:** `Hrot.ClusterRunner.Integration.Tests/EditorFileIOIntegrationTests.cs`
- **CGF Harness:** `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs`
- **HrotRunnerHarness:** `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs`
- **PreviewClusterOpHandler / cluster op handlers:** search in `Hrot.ClusterRunner` for `PreviewClusterOpHandler`
- **UrbanCombatNewScenario:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`
- **UrbanCombatValidator:** `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatValidator.cs` (from BATCH-01)
- **CarKinematicsSystem:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs`
- **RoadNetworkBlob / RoadNetworkLoader:** `FDP/Toolkits/FDP.Toolkit.Geographic/` (look for these types)
- **GlobalComponentIds.cs:** `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs`
- **HrotSharedComponentRegistry:** Look for it in `Hrot.Common`
- **Hrot.Map.Common project:** `Hrot.Map.Common/` — add all new DTO files here
- **Integration Tests project:** `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev/packs-3/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev/packs-3/questions/BATCH-02-QUESTIONS.md`

---

## Context

Phase 1 integration tests prove two critical lifecycle paths:
- **U003** (Editor Preview/Rewind): headless DDS-free, relies only on `EditorHarness` and the
  existing `ScenarioEditorModule` logic pack.
- **U004** (Urban Combat File Lifecycle): the most complex test in packs-3 — full distributed
  cluster (Orchestrator + SimHost + CGF) loading an auto-extracted JSON scenario and running it
  to completion using the `UrbanCombatValidator` from BATCH-01.

Phase 2 foundation (Z001, Z002) sets up the FDP-engine-side singleton and the application-layer
DTOs *independently* of each other. No service or handler wiring is added yet — those are
BATCH-03.

---

## 🎯 Batch Objectives

1. Prove the Editor Preview/Rewind lifecycle (save → load → rewind to snapshotted state) works end-to-end via a headless integration test.
2. Prove the Urban Combat scenario can be auto-extracted to JSON and executed successfully in the
   full distributed cluster state machine (Orchestrator + SimHost + CGF).
3. Add `ZoneEnvironmentData` ECS singleton and refactor `CarKinematicsSystem` to read it (removing the constructor parameter).
4. Add the four HROT application-layer DTO classes and shared `HrotJsonOptions` (Hrot.Map.Common).

---

## ✅ Tasks

### Task 1: Editor Preview / Rewind Integration Test (PACK3-U003)

**File:** `Hrot.ClusterRunner.Integration.Tests/EditorPreviewAndSaveIntegrationTests.cs` — **NEW FILE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-U003](../TASK-DETAIL.md#pack3-u003--editor-preview--rewind-integration-test)

**Key Requirements:**

The test class must:
- Be decorated with `[Collection("EditorOfflineTests")]`.
- Hold a `string _tempFile` (generated via `Path.GetTempFileName()`), deleted in `Dispose`.
- Implement `IDisposable`.

The single test method (`EditorPreview_SnapshotsAndRestoresState`) must:
1. Get / create an `EditorHarness` instance.
2. `logic.NewScenario()` — start fresh.
3. Publish `SpawnEntityCommand { TkbType = 1001 }` on the editor bus; pump until `repo.EntityCount == 1`.
4. Get the spawned entity's `NetworkIdentity`.
5. Publish `UpdateEntityCommand` moving entity `SimTransform.Position` to `(100f, 0, 0)`. Pump 5 frames.
6. Instantiate `PreviewClusterOpHandler(repo)` (find the actual constructor by reading the source).
7. Send `NodeOpCommand { PayloadJson = "{\"TargetState\": 20}" }` → this triggers `LoadingPreview` (snapshot captured). Pump sufficiently.
8. Publish `UpdateEntityCommand` moving entity to `(999f, 0, 0)`. Pump 5 frames.
9. Assert `SimTransform.Position.X == 999f` (preview state visible).
10. Send `NodeOpCommand { PayloadJson = "{\"TargetState\": 22}" }` → `UnloadingPreview` (rewind). Pump sufficiently.
11. Assert `SimTransform.Position.X == 100f` (state restored to snapshot).
12. `logic.SaveScenario(_tempFile)` — assert file exists.
13. `logic.NewScenario()`, pump 2 frames — assert `EntityCount == 0`.
14. `logic.LoadScenario(_tempFile)`, pump 5 frames.
15. Assert `EntityCount == 1` and `SimTransform.Position.X == 100f`.

**Important:** Read `EditorHarness.cs`, `EditorFileIOIntegrationTests.cs`, and `PreviewClusterOpHandler.cs` first to understand the exact API surface before writing the test.

**Tests Required:**
- The above sequence (14 assertion points constitute the success criteria).
- Test must run in < 10 seconds with no DDS or network calls.

---

### Task 2: Urban Combat File Lifecycle Integration Test (PACK3-U004)

**File:** `Hrot.ClusterRunner.Integration.Tests/UrbanCombatFileLifecycleTests.cs` — **NEW FILE**

**Task Definition:** See [TASK-DETAIL.md — PACK3-U004](../TASK-DETAIL.md#pack3-u004--urban-combat-file-lifecycle-integration-test)

**Key Requirements:**

This is the most complex test in packs-3. It requires:
- A `HrotScenarioEnvelopeDto` (from PACK3-Z002 below — implement Z002 first).
- `UrbanCombatValidator` (from BATCH-01 — already available).
- `HrotRunnerHarness` with `RunMode.Orchestrator | RunMode.SimHost`.
- `CgfHarness` on the same `domainId`.

**Lifecycle:**
1. **Extract:** Create `EntityRepository`, `EventAccumulator`, `ModuleHostKernel`. Instantiate `UrbanCombatNewScenario`, call `Configure()`. Build `ScenarioSerializer`, serialise world → get `fdpDom`. Wrap in `HrotScenarioEnvelopeDto` (set `Header` with `SubsystemType = "Hrot.Scenario"`, `Zones = null`, `Entities` from `fdpDom["Entities"]?.AsObject()`). Serialise with `HrotJsonOptions`. Write to temp staging dir (e.g. `C:\FDP_Temp\<Guid>\scenario.json`).
2. **Boot cluster:** `HrotRunnerHarness(RunMode.Orchestrator | RunMode.SimHost, options)` where `options.Deterministic = true`, `options.FixedDeltaSeconds = 1f/60f`. `CgfHarness(domainId)`. Pump 20 frames each for DDS discovery.
3. **Transition:** Inject `ClusterOpRequest` with `TargetState = 31` (OperatingLive), `ScenarioId = _scenarioId` (the staging dir path or a key that the load handler resolves). Call `PumpUntil(SystemState == OperatingLive, timeout)`.
4. **Validate:** Instantiate `UrbanCombatValidator`. Loop up to 800 ticks pumping both harnesses 1 frame per iteration. Call `validator.EvaluateTick(tick, simHostHarness.Repo)` each iteration. Break on `true` → `success = true`.
5. `Assert.True(success)`.
6. `Dispose` deletes the temp staging dir.

**Note:** You will need to understand how the cluster loads scenarios from files. Read `HrotRunnerHarness`, `ClusterOpRequest`, and the existing cluster op handler code in `Hrot.SimHost`. The key question is: how does the cluster resolve a `ScenarioId` to a file path? Look at
`ReferenceScenarioLoadHandler` or the existing `ScenarioFileService` to understand this flow.

**Important**: Write the extraction + file writing part first. Get that working and writing a valid JSON file before wiring the cluster boot.

**Tests Required:**
- Test passes: all 4 latches fire within 800 frames.
- `UrbanCombatValidator` reused (validates code reuse with the programmatic test).
- Temp staging dir cleaned up in `Dispose` even on test failure.

---

### Task 3: `ZoneEnvironmentData` ECS Singleton & `CarKinematicsSystem` Refactor (PACK3-Z001)

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Geographic/ZoneEnvironmentData.cs` — **NEW FILE**
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` — **REFACTOR**
- Composition roots that construct `CarKinematicsSystem`: update to remove the `RoadNetworkBlob` constructor parameter

**Task Definition:** See [TASK-DETAIL.md — PACK3-Z001](../TASK-DETAIL.md#pack3-z001--zoneenvironmentdata-ecs-singleton--carkinematicssystem-refactor)

**Key Requirements:**

1. **`ZoneEnvironmentData` struct:**
   - Add to `FDP/Toolkits/FDP.Toolkit.Geographic/ZoneEnvironmentData.cs`.
   - Assign an unused component ID from the **Toolkit expansion block (IDs 20–79)** in `GlobalComponentIds.cs`. Check that the chosen ID is free.
   - Field: `public RoadNetworkBlob RoadNetwork;`.

2. **`CarKinematicsSystem` refactor:**
   - Remove the constructor-injected `RoadNetworkBlob roadNetwork` parameter.
   - At the top of `OnUpdate`, read the singleton with fallback:
     ```csharp
     var roadNetwork = World.HasSingleton<ZoneEnvironmentData>()
         ? World.GetSingleton<ZoneEnvironmentData>().RoadNetwork
         : default; // empty blob — safe for non-road scenarios
     ```
   - **Never return early** based on singleton absence — all non-road-network vehicle physics must continue running even when no zone is loaded.

3. **Update all call sites** that pass `RoadNetworkBlob` to `CarKinematicsSystem`'s constructor.

**Tests Required:**
- Unit test: `CarKinematicsSystem.OnUpdate` does not throw and does **not** skip vehicle physics when no `ZoneEnvironmentData` singleton is present.
- Unit test: Set `ZoneEnvironmentData` singleton with a valid `RoadNetworkBlob`; system performs a navigation tick without error.
- Regression: All existing `CarKinematicsSystem`-dependent integration tests pass after the constructor removal.

---

### Task 4: Application-Layer DTOs for Scenario Envelope (PACK3-Z002)

**New Files in `Hrot.Map.Common/Scenario/`:**
- `HrotScenarioEnvelopeDto.cs`
- `ScenarioHeaderDto.cs`
- `ZoneDefinitionDto.cs`
- `ZoneObstacleDto.cs`

**New File:**
- `Hrot.Map.Common/HrotSerializerOptions.cs`

**Task Definition:** See [TASK-DETAIL.md — PACK3-Z002](../TASK-DETAIL.md#pack3-z002--application-layer-dtos-for-scenario-envelope)

**Key Requirements:**
- Namespace: `Hrot.Map.Common.Scenario` for the DTOs, `Hrot.Map.Common` for `HrotSerializerOptions`.
- **Zero** `[JsonPropertyName]` attributes on any DTO.
- `HrotJsonOptions` static field (or property returning a pre-built `JsonSerializerOptions`):
  - `PropertyNameCaseInsensitive = true`
  - `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
  - `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`
  - `WriteIndented = true`
- DTO properties (see DESIGN.md §2.C and TASK-DETAIL.md §PACK3-Z002 for exact shapes).
- `Entities` in `HrotScenarioEnvelopeDto` is `JsonObject?` — the raw FDP DOM, treated as opaque.

**Tests Required:**
- Round-trip test: construct `HrotScenarioEnvelopeDto` with header + one zone (two obstacles), serialise and deserialise, assert obstacle values.
- Case-insensitivity test: deserialise JSON with PascalCase keys into `ZoneDefinitionDto` using `HrotJsonOptions`; assert non-null path.
- Compile check: no `[JsonPropertyName]` attributes exist on any new DTO type.

---

## 🧪 Testing Requirements

### Minimum Test Coverage

| Task | Test Type | Minimum |
|------|-----------|---------|
| U003 | Integration (headless, no DDS) | 14-step sequence all pass |
| U004 | Integration (distributed cluster) | 1 test, 4 latches within 800 ticks |
| Z001 | Unit + Regression | 2 unit + all CarKinematics regressions |
| Z002 | Unit | 2 unit (round-trip + case-insensitivity) |

### Task Order Recommendation

Do tasks in this order to unblock dependencies:
1. **Z002 first** — DTOs are needed by U004 (for `HrotScenarioEnvelopeDto` JSON extraction step).
2. **U003** — headless, standalone, no dependencies on Z001/Z002.
3. **Z001** — FDP-side refactor (compilation changes to CarKinematicsSystem and call sites).
4. **U004** — requires Z002 (envelope DTO), requires BATCH-01 work (UrbanCombatValidator).

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
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EditorFileIO|EditorPreview|UrbanCombat|CarKinematics" --no-build
```

---

## 📊 Report Requirements

Submit your report to `.dev/packs-3/reports/BATCH-02-REPORT.md`.

```markdown
# BATCH-02 Report

## Implementation Summary
[Per-task: what was done]

## Tests Added
[List new test methods and files]

## Test Results
[Pass/fail counts, any skips]

## Developer Insights
1. **Issues Encountered:** What problems did you hit? How resolved?
2. **Weak Points Spotted:** Fragile or unclear areas noticed.
3. **Design Decisions Beyond the Spec:** Any choices not explicitly stated?

## Deviations from Spec (if any)
[List with justification]
```

---

## ⚠️ Important Notes

1. **Do NOT start PACK3-Z003 or later Phase 2 tasks** (ZoneManagerService, handlers) — those are BATCH-03.
2. **FDP submodule:** `ZoneEnvironmentData.cs` and `CarKinematicsSystem.cs` changes live in `FDP/`. Stage and commit them separately from parent-repo changes.
3. **U004 complexity**: if the full distributed cluster test proves too difficult to wire (e.g. can't find how `ScenarioId` maps to a file path), write as much as possible and document the blocker clearly in the report. **Do not fabricate passing tests.** A clear and honest assessment of what is blocked is more valuable than a fake passing test.
4. **Z001 ID selection**: double-check `GlobalComponentIds.cs` before picking a component ID; a collision will cause a runtime crash that is hard to diagnose.

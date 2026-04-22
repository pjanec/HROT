# Onboarding — Scenario File Support, ACL Hardening & Network DRY Refactor (`packs-3`)

Welcome to the `packs-3` workstream.  This document gets you up to speed quickly.

---

## What Are We Building / Refactoring?

`packs-3` completes the scenario authoring lifecycle and fixes two known architectural violations
left open from `packs-2`.  There are four parallel streams of work:

### 1. Urban Combat Scenario — Data-Driven Lifecycle

The existing `UrbanCombatNewScenario` runs as a purely programmatic C# script.  We convert it
into a first-class **JSON scenario file** that can be:
- loaded into the **HROT Editor** (loadingEdit / OperatingEdit),
- previewed and rewound (loadingPreview / OperatingPreview),
- saved to disk,
- loaded by the full **distributed cluster** (Orchestrator + SimHost + CGF, loadingLive /
  OperatingLive).

Shared validation logic is extracted into a standalone `UrbanCombatValidator` that works for
both the original programmatic test and the new file-driven cluster lifecycle test.

### 2. Zone Definitions in Scenario Files

Scenarios can now declare the **static environment** they depend on (road network, cylindrical
LOS obstacles) in a `"Zones"` section.  The scenario loader resolves these assets and injects
them into the FDP engine **before** any entity is spawned.

Strict Anti-Corruption Layer is enforced:
- The **HROT application layer** owns the JSON DTOs and file-path resolution.
- The **FDP engine** only sees in-memory structs (`ZoneEnvironmentData` ECS singleton,
  `PhysicsCollider` ECS entities) — it never sees file paths, JSON strings, or app-layer DTOs.

### 3. ACL Backdoor Elimination

A hidden side-channel (`tryGetPrebuilt` delegate) allowed `MapCommandController` to push
pre-built `CreateEntityRequest` DDS structs directly into the egress translator, bypassing the
FDP event bus.  This directly violated the `packs-2` ACL mandate.  In `packs-3` the backdoor
is fully removed; map tools now emit only pure `SpawnEntityCommand` events.

### 4. NetworkGatewaySystem DRY Refactor

The reliable-initialisation state machine (`NetworkGatewaySystem`) was copy-pasted from
`ModuleHost.Core` into the Cyclone transport pack.  We promote the canonical,
transport-agnostic implementation to `FDP.Toolkit.Replication`, delete the copies, and rewire
`CycloneNetworkModule` to use the shared class.

---

## Key Documents

| Document | Purpose |
|----------|---------|
| [design_talk.md](./design_talk.md) | Full design conversation — read to understand the *why* |
| [DESIGN.md](./DESIGN.md) | Formal design — phases, architecture decisions, data contracts |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specifications with success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Quick progress checklist |
| [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) | **Read this before starting any work.** Developer workflow, batch system, reporting format |
| [CODE-STANDARDS.md](../.guides/CODE-STANDARDS.md) | Coding standards and conventions |
| [DEBT-TRACKER.md](../DEBT-TRACKER.md) | Technical debt log |

**Context documents from prior packs:**

| Document | What it gives you |
|----------|------------------|
| [packs-2/DESIGN.md](../packs-2/DESIGN.md) | HROT Editor architecture, Feature Switch, ACL Translator Packs |
| [packs-2/TASK-TRACKER.md](../packs-2/TASK-TRACKER.md) | What was already completed (all phases done) |

---

## Relevant Components & Folder Layout

### Scenario Extraction & Validation
| Location | Content |
|----------|---------|
| `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs` | Original programmatic scenario — to be simplified |
| `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatValidator.cs` | **New** shared validator |
| `Hrot.ClusterRunner.Integration.Tests/` | Home for all new integration tests |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | Headless Editor composition root (no DDS) |
| `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` | Full distributed cluster harness |
| `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs` | Headless CGF/Brain harness |

### Zone Definitions
| Location | Content |
|----------|---------|
| `Hrot.Map.Common/Scenario/` | **New** DTO classes (`HrotScenarioEnvelopeDto`, etc.) |
| `Hrot.Map.Common/Services/ZoneManagerService.cs` | **New** zone loading service |
| `Hrot.Map.Common/HrotSerializerOptions.cs` | **New** shared JSON options |
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | Updated save logic |
| `Hrot.ScenarioEditor/Handlers/HrotEditLoadHandler.cs` | **New** LoadingEdit handler |
| `Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs` | **New** LoadingLive handler |
| `FDP/Toolkits/FDP.Toolkit.Geographic/ZoneEnvironmentData.cs` | **New** ECS singleton struct |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` | Refactored to read singleton |
| `Assets/sample_road.json` | Existing road network test asset used by integration tests |

### ACL Backdoor
| Location | Content |
|----------|---------|
| `Hrot.Map.Common/Replication/Egress/SpawnEntityCommandEgressTranslator.cs` | Backdoor removed here |
| `Hrot.IG/Systems/MapCommandController.cs` | DTO cache removed here |
| `Hrot.IG/IgApplication.cs` | Side-channel wiring removed here |
| `Hrot.IG/Tools/AreaAuthoringTool.cs` | Geometry now via `InitialComponents` |

### NetworkGatewaySystem
| Location | Content |
|----------|---------|
| `FDP/Toolkits/FDP.Toolkit.Replication/Systems/NetworkGatewaySystem.cs` | **New** canonical home |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/CycloneNetworkModule.cs` | Rewired to toolkit |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Systems/NetworkGatewaySystem.cs` | **Deleted** (clone) |
| `FDP/ModuleHost/ModuleHost.Network.Cyclone/Modules/NetworkGatewayModule.cs` | **Deleted** (clone) |
| `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewaySystem.cs` | **Deleted** (legacy) |
| `FDP/ModuleHost/ModuleHost.Core/Network/NetworkGatewayModule.cs` | **Deleted** (legacy) |

---

## How to Build

```powershell
# Build the whole solution (from workspace root)
dotnet build IOS-IG-SimHost.sln

# Or use the batch script
.\build_all_standalone.bat
```

## How to Run Integration Tests

```powershell
# Run all integration tests (may take a few minutes)
dotnet test Hrot.ClusterRunner.Integration.Tests

# Run a specific new test class
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "ZoneScenarioLoadIntegrationTests"
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EditorPreviewAndSaveIntegrationTests"
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "UrbanCombatFileLifecycleTests"
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "NetworkGatewayIntegrationTests"
```

---

## Architecture Principles to Keep in Mind

1. **No magic strings in JSON.** Every JSON section has a matching DTO class.  Use `HrotJsonOptions`
   (camelCase, case-insensitive, null-omitting) instead of `[JsonPropertyName]` clutter.

2. **Application layer vs. FDP engine.** DTOs (`HrotScenarioEnvelopeDto`, `ZoneDefinitionDto`,
   etc.) live in `Hrot.Map.Common`.  The FDP engine only ever sees in-memory structs like
   `ZoneEnvironmentData`.  The `ZoneManagerService` is the translation pivot.

3. **One road network per zone.** We deliberately keep the data model simple — no multi-file
   merging.  Each named zone may have at most one `roadNetworkPath`.

4. **Cylindrical obstacles only.** The engine's narrow-phase solver (`Intersection2D.RaycastCircle`)
   supports only 2.5D cylinders.  Obstacles are stored as (X, Y, Radius) in `ZoneObstacleDto`.

5. **Shared validator.** `UrbanCombatValidator` must be used by both the existing programmatic
   test and the new cluster lifecycle test.  No validator logic should be duplicated.

6. **Headless CI tests.** All new tests use `EditorHarness`, `HrotRunnerHarness`, or
   `CgfHarness` — they require no desktop GUI, no manual DDS setup, and no external processes.
   Integration tests run on dynamically allocated loopback domain IDs to avoid port conflicts.

7. **ACL = strict discipline.** After PACK3-A001 through A004 are done, `grep -r "tryGetPrebuilt"`
   should return zero results.  If it doesn't, the work is not complete.

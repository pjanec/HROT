# Onboarding — CGF Scenario Loading via Genesis Pipeline

Welcome to the **cgf-scn** workstream.  This guide is for developers picking up
work on this feature.

---

## 1. What Is Being Built

Currently the CGF (Cognitive Functions) node is a passive observer during
scenario loading — it participates in the cluster handshake without loading any
entities.  The SimHost node loads entity state directly from the scenario JSON.

This workstream changes CGF into the **authoritative entity genesis source**.
All entities defined in the scenario file are injected through CGF's existing
`CreateEntityRequestSystem` → `NetworkSpawningSystem` pipeline, giving every
spawned entity a fresh, collision-free network identity, correct split-authority
(CGF owns cognitive state; SimHost owns kinematic descriptors), and a guaranteed
ELM reliable-init handshake.

The same staging pipeline is also applied to **episode loading** (micro-scenarios
injected into live exercises), fixing existing architectural defects in
`ReferenceEpisodeLoadHandler`.

A related feature introduced in Phase 5 is a **generic, DTO-driven mission editor
UI** that replaces the hardcoded `DrawFireAtTargetParams` methods in `MissionPanel`
with attribute-decorated DTO types and pre-compiled ImGui rendering delegates.

---

## 2. Planning Artifacts

| Document | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Architectural decisions, phased design, component reference table |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, and success conditions (unit test specifications) |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

Read `DESIGN.md` in full before touching any code.  In particular read the
**Architectural Decisions** section to understand why simpler approaches were
rejected.

---

## 3. Folder Layout

### Code being built / modified

| Path | Description |
|---|---|
| `Hrot/Engine/Hrot.Core/Network/` | `ScenarioEntityCreationRequestSource` (C001), `CompositeEntityCreationRequestSource` (C002) |
| `Hrot/Subsystems/Hrot.CGF/Systems/` | `CgfLogicPack.cs` — wired with composite source (C003); `CreateEntityRequestSystem.cs` — unchanged |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/` | `CgfScenarioLoadHandler.cs` (C006), `CgfEpisodeLoadHandler.cs` (C007) |
| `Hrot/Subsystems/Hrot.CGF/` | `CgfApplication.cs` — registration changes (C003, C006, C007, C011), `CgfBehaviorSetup.cs` — remapper/UI registry setup (C011) |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/` | `StagingEntityExtractor.cs` (C004) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/` | `RemapNetworkIdAttribute.cs` (C005a), `MapPickableWorldLocationAttribute.cs` (C008), `MapPickableEntityAttribute.cs` (C008) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/` | `FireAtTargetParamsJsonDto.cs`, `FollowRouteParamsJsonDto.cs`, `MoveToLocationParamsJsonDto.cs` (C005b + C008) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/` | `BehaviorParamRemapperCompiler.cs`, `ScenarioBehaviorRemapper.cs` (C005c, C005d) |
| `Hrot/Engine/Hrot.Presentation/Behavior/` (or `Hrot.UI.Common`) | `BehaviorUiCompiler.cs`, `BehaviorUiRegistry.cs` (C009) |
| `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` | Generic UI integration (C010) |

### Existing code you must understand before coding

| Path | Why relevant |
|---|---|
| `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` | Entry point for all entity creation on CGF; processes `IEntityCreationRequestSource` |
| `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs` | `IEntityCreationRequestSource`, `EntityCreationRequest` definitions |
| `Hrot/Network/Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs` | The only existing production `IEntityCreationRequestSource` implementation |
| `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/NetworkSpawningSystem.cs` | How `SpawnEntityCommand.InitialComponents` are applied to ECS entities |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs` | The handler being replaced on CGF (do NOT modify this file) |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` | How to hydrate a staging `EntityRepository` from JSON |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs` | How translators handle cross-entity references |
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Component type ID constants used to build the exclusion mask |
| `FDP/Engine/Fdp.Core/IComponentTable.cs` | `GetRawObject(int index)` used for boxing-based component extraction |
| `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` | Where behaviors are registered; registration site for remapper and UI registry |
| `Hrot/Engine/Hrot.Presentation/Panels/MissionPanel.cs` | Current hardcoded mission param UI being replaced |

---

## 4. Build and Run Tests

```powershell
# Build the solution
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln

# Run CGF-related tests
dotnet test Hrot\Subsystems\Hrot.CGF.Tests\Hrot.CGF.Tests.csproj

# Run Hrot.Core tests
dotnet test Hrot\Engine\Hrot.Core.Tests\Hrot.Core.Tests.csproj

# Run FDP toolkit tests
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

If no dedicated `Hrot.CGF.Tests` project exists yet, add new tests alongside
existing tests in the closest project (e.g., integration-level tests in
`Hrot.ClusterRunner.Tests`).

---

## 5. Development Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` for the batch-based development
workflow used on this project.  Work proceeds in structured batches; each batch
corresponds to one or more tasks from [TASK-TRACKER.md](./TASK-TRACKER.md).  Do
not begin coding without a batch instruction from the development lead.

Key workflow rules:
- Mark tasks in `TASK-TRACKER.md` as completed only after all success conditions
  in `TASK-DETAIL.md` are met.
- Do not skip phases; Phase 1 (source infrastructure) must be complete before
  Phase 3 (load handler) can be coded.
- The solution must build without errors after each task is completed.

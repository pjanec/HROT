# Onboarding — Shared UI Library & Hrot.Editor Feature Completion (`edit-1`)

Welcome to the `edit-1` workstream.  This document gets you productive quickly.

---

## What Are We Building / Refactoring?

`edit-1` does two things in parallel:

### 1. Extract a Shared ImGui Panel Library (`Hrot.UI.Common`)

The panels the `Hrot.Editor` needs already exist — in `Hrot.ExCon`, `Hrot.IG`, or
`Hrot.SimHost`.  Copying them into the editor would create a maintenance nightmare.

The solution is to extract the rendering-only panel logic into a new class library
(`Hrot.UI.Common`) with **zero coupling to CycloneDDS, DerRepo, or IExConLogic**.  Instead,
each panel depends on a small, focused *Port* interface (e.g. `ISpawnController`,
`IMissionEditorService`).  Both the offline `Hrot.Editor` and the distributed `Hrot.ExCon`
implement these interfaces through *Adapter* classes that speak their respective infrastructure
language (FdpEventBus + ECS for the editor; IDerRepo + DDS for ExCon).

This is a **Ports and Adapters (Hexagonal Architecture)** refactor.  The UI code is written
exactly once; the infrastructure details are isolated in each subsystem.

### 2. New Authoring Features for Urban Combat Scenario

To reproduce the Urban Combat demo entirely through the editor UI (replacing the programmatic
`ScenarioDirector` setup calls), four new capabilities are introduced:

- **Embarkation & Cargo Management** — load infantry into vehicles via ORBAT drag-and-drop.
- **Target Memory Seeding** — link a perceiver to a target entity via context menu + map pick.
- **Static Zone & Obstacle Authoring** — assign road network + drop cylindrical physics
  obstacles onto the map (complements the Zone DTO infrastructure from `packs-3`).
- **Dynamic Behavior Catalog** — mission behavior dropdown filtered per entity TKB type,
  driven by a shared `BehaviorCatalog` in `Hrot.Map.Definitions`.

---

## Planning Artifacts

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Architecture, phases, rationale, component diagram |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Full per-task specs with scope, constraints, success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | One-line progress checklist per task |

Start with **DESIGN.md §Phase 0** before writing any code.

---

## Prerequisites

This workstream builds **on top of `packs-3`**, which must be treated as complete (or
in-progress but with its interfaces stable):

- `ZoneEnvironmentData` ECS singleton (PACK3-Z001)
- `HrotScenarioEnvelopeDto` / `ZoneDefinitionDto` / `ZoneObstacleDto` (PACK3-Z002)
- `IZoneManagerService` + `ZoneManagerService` (PACK3-Z003)
- `HrotScenarioLoadHandler` / `HrotEditLoadHandler` (PACK3-Z004)
- `ScenarioFileService.SaveScenario` with Zones (PACK3-Z005)
- ACL backdoor removed from `SpawnEntityCommandEgressTranslator` (PACK3-A001 – A005)

If any of the above are not yet merged, the `Hrot.Editor` Zone authoring system (EDIT1-A011)
and the zone save pipeline test (EDIT1-T003) will need integration work at that point.

---

## Folder Layout

```
Hrot.UI.Common/                  ← NEW: shared panel library (Phases 0–2)
  Facades/                       ← Port interfaces (ISpawnController, etc.)
  Models/                        ← OrbatNodeViewModel, MapLayerState, MissionCommitResult
  Panels/                        ← SpawnerPanel, MissionPanel, ConfigPanel,
  │                                  SharedOrbatPanel, PreviewPanel, ZoneEditorPanel
  Menus/                         ← SharedContextMenuPopulator

Hrot.Editor/
  Adapters/                      ← EditorSpawnAdapter, EditorMissionService,
  │                                  EditorOrbatAdapter, EditorMapPickAdapter,
  │                                  EditorZoneAdapter, EditorPreviewAdapter,
  │                                  EditorMapConfigAdapter
  Systems/                       ← EditorCargoSystem, EditorPerceptionSetupSystem,
  │                                  EditorZoneAuthoringSystem
  Tools/                         ← ObstaclePlacementTool, ModalBoxSelectionTool
  Rendering/                     ← PerceptionMapLayer
  UI/                            ← EditorEntityContextMenuHandler

Hrot.ExCon/
  Adapters/                      ← ExConOrbatAdapter, ExConMapConfigAdapter (NEW/updated)

Hrot.Map.Definitions/
  Tkb/BehaviorCatalog.cs         ← NEW: TKB → behavior name mapping

FDP/Toolkits/
  FDP.Toolkit.Behavior/Events/   ← EmbarkEntityCommand, DisembarkEntityCommand (NEW)
  FDP.Toolkit.Perception/Events/ ← SeedTargetCommand (NEW)

Hrot.Map.Common/
  Events/                        ← SpawnZoneObstacleCommand, UpdateZoneConfigCommand (NEW)
  Components/ZoneMembership.cs   ← NEW small managed component

Hrot.ClusterRunner.Integration.Tests/
  EditorAuthoringIntegrationTests.cs  ← NEW (Phase 7 tests)
```

---

## Build & Run

Build the full solution from the workspace root:

```powershell
dotnet build IOS-IG-SimHost.sln
```

Run only the Editor authoring integration tests:

```powershell
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "EditorAuthoringIntegration"
```

Run the full test suite (no DDS participants needed for Editor tests):

```powershell
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build
```

Run a single test by name:

```powershell
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "Embarkation_ValidRequest"
    --logger "console;verbosity=detailed" --no-build
```

---

## Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development workflow
used in this project.

**Key rules for this workstream:**

1. Complete Phase 0 (contracts) before implementing anything in Phases 1–6.
   The interfaces are the contracts; changing them mid-stream forces cascading updates.
2. Phase 3 (domain events) must be done before Phase 4 (systems that consume them).
3. Phases 4 and 6 are **independent** — Editor adapters and ExCon adapters can be developed
   in parallel.
4. Phase 7 (tests) should be written **alongside** Phase 4, not after — the tests inform the
   correct adapter signatures.
5. Do not add game logic inside the `Hrot.UI.Common` panels.  If a panel needs domain context,
   expose a new method on the appropriate Port interface.
6. Every ECS mutation must happen inside a `ComponentSystem.OnUpdate()` during the `Input`
   phase — never inside a UI render callback.

# Routes-1 Onboarding Guide

Welcome to the **ROUTES-1** workstream. This document gives you everything you need to get started as a developer on this work.

---

## What Are We Building?

This workstream replaces the legacy ad-hoc trajectory mechanism with a first-class **Route entity** system. Concretely, we are:

- Introducing a new `RoutePlan` ECS managed component that stores an ordered list of waypoints with per-waypoint speed and AI behavior hints.
- Replicating route entities over CycloneDDS using the existing `MapRoute` IDL descriptor.
- Rendering route entities on the IG 2D map (toggled via the "road_graphs" layer).
- Authoring **shared routes** from the IOS/IG map canvas via the established `CMD_START_AUTHORING` interaction.
- Authoring **personal (vehicle-specific) routes** via Shift+Right-Click, replacing the old `ScenarioManager._waypointQueues` mechanism.
- Providing a `RouteEditTool` and `WaypointEditorPanel` for interactive editing of waypoints (move, insert, delete; set speed and AI advice JSON).
- Wiring a `RouteTrajectorySyncSystem` that keeps the `TrajectoryPoolManager` (the high-performance kinematic cache) up to date whenever a route changes.
- Implementing a `RouteContextSystem` that injects per-waypoint "soft advice" JSON into the vehicle's `BrainBlackboard` as it traverses the route.
- Removing the legacy `_waypointQueues` state held inside `SimHostScenarioManager`.

---

## Design & Task Documents

| Document | Purpose |
|---|---|
| [ROUTES1-DESIGN.md](./ROUTES1-DESIGN.md) | Full architectural design — read this first |
| [ROUTES1-TASK-DETAIL.md](./ROUTES1-TASK-DETAIL.md) | Per-task specifications with success conditions |
| [ROUTES1-TASK-TRACKER.md](./ROUTES1-TASK-TRACKER.md) | Progress checklist — update as tasks complete |
| [design-talk.md](./design-talk.md) | The original design conversation; background reading |

---

## How to Read the Design Document

The [ROUTES1-DESIGN.md](./ROUTES1-DESIGN.md) is structured as follows:

1. **§1–2** — Problem statement and current state. Start here to understand what exists today and why it is being replaced.
2. **§3** — Architectural principles. Three core rules that govern all design decisions.
3. **§4** — The new `RoutePlan` ECS component and supporting types. The data model everything else builds on.
4. **§5** — Shared vs. personal routes — how a single component covers both cases.
5. **§6** — DDS replication strategy (ingress/egress translators + coordinate conversion).
6. **§7** — Trajectory pool integration (`RouteTrajectorySyncSystem`).
7. **§8** — Rendering on the IG map.
8. **§9–10** — Authoring flows (shared and personal).
9. **§11** — Editing flow with `RouteEditTool` and `WaypointEditorPanel`.
10. **§12** — Deletion lifecycle.
11. **§13** — AI soft advice pipeline (`RouteContextSystem` → `BrainBlackboard`).
12. **§14** — TKB blueprint for `TacGraphic_Route`.
13. **§15** — Legacy deprecation plan.
14. **§16** — Phase and task breakdown (pointer to tracker).

---

## Relevant Components in the Codebase

### Components You Are Introducing (do not exist yet)

| Name | Planned Location |
|---|---|
| `RoutePlan` managed component | `Hrot.Map.Common/Components/RoutePlan.cs` |
| `RouteWaypoint` struct | same file as `RoutePlan` |
| `PersonalRouteRef` struct | `Hrot.Map.Common/Components/PersonalRouteRef.cs` |
| `RouteTrajectoryCache` struct | `Hrot.Map.Common/Components/RouteTrajectoryCache.cs` |
| `CmdAppendPersonalWaypoint` event | shared event/command layer |
| `MapRouteEgressTranslator` | `Hrot.Map.Common/Replication/Egress/` |
| `MapRouteIngressTranslator` | `Hrot.Map.Common/Replication/Ingress/` |
| `RouteTrajectorySyncSystem` | `Hrot.SimHost/Systems/` |
| `PersonalRouteAuthoringSystem` | `Hrot.SimHost/Systems/` |
| `RouteContextSystem` | `Hrot.SimHost/Systems/` |
| `RouteRenderLayer` | `Hrot.IG/Visualization/` |
| `RouteEditTool` | `Hrot.IG/Tools/` |
| `WaypointEditorPanel` | `Hrot.IG/UI/` |

### Components You Are Modifying

| What | Where | Why |
|---|---|---|
| `GlobalComponentIds` | `FDP/Toolkits/FDP.Toolkit.Replication/` | Add IDs for new components |
| TKB blueprint DB | `Hrot.Map.Definitions/` | Add `TacGraphic_Route` blueprint |
| `SimHostScenarioManager` | `Hrot.SimHost/UI/` | Remove `_waypointQueues` (Phase 9) |
| `IgApplication` | `Hrot.IG/IgApplication.cs` | Wire shift+right-click → CmdAppendPersonalWaypoint (Phase 5) |
| `SimHostTrajectoryLayer` | `Hrot.SimHost/Visualization/` | Extend to show route entities (Phase 6) |

### Key Existing Systems to Understand

| What | Where | Purpose |
|---|---|---|
| `TrajectoryPoolManager` | `FDP/Toolkits/FDP.Toolkit.CarKinem/Trajectory/` | Physics trajectory cache (pool of compiled splines) |
| `CarKinematicsSystem` | `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/` | Vehicle physics — reads `TrajectoryId` from `NavState` |
| `NavState` struct | `FDP/Toolkits/FDP.Toolkit.CarKinem/Core/NavState.cs` | Per-vehicle kinematic state; holds `TrajectoryId`, `ProgressS` |
| `EditablePolyline` | `Hrot.Map.Common/Components/EditablePolyline.cs` | Parallel for area overlay drawings (pattern to follow) |
| `MapVisualOverlayEgressTranslator` | `Hrot.Map.Common/Replication/Egress/` | Pattern to follow for egress translator |
| `MapVisualOverlayIngressTranslator` | `Hrot.Map.Common/Replication/Ingress/` | Pattern to follow for ingress translator |
| `EditTool` | `Hrot.IG/Tools/EditTool.cs` | Pattern to follow for `RouteEditTool` |
| `PointSequenceTool` | `FDP/Toolkits/FDP.Toolkit.Vis2D/Tools/` | Used for route authoring; already implemented |
| `SubEntityCleanupSystem` | `FDP/Toolkits/FDP.Toolkit.Replication/Systems/` | Destroys child entities when parent is destroyed (`PartMetadata`) |
| `PartMetadata` | `FDP/Toolkits/FDP.Toolkit.Replication/Components/` | Parent-child entity lifecycle |
| `TkbEntityTypes` | `Hrot.Map.Definitions/TkbEntityTypes.cs` | `TacGraphic_Route = 8802` already defined |
| `WGS84Transform` / `IGeographicTransform` | `FDP/Toolkits/Fdp.Toolkit.Geographic/` | Cartesian ↔ Geodetic conversion used at DDS boundary |
| `BrainBlackboard` | `FDP/Toolkits/FDP.Toolkit.Behavior/Components/` | Per-vehicle byte buffer for AI state |

---

## How to Build the Project

```powershell
# From the workspace root:
dotnet build IOS-IG-SimHost.sln
```

To run only the relevant test suites:

```powershell
dotnet test Hrot.Map.Common.Tests
dotnet test Hrot.SimHost.Tests
dotnet test Hrot.IG.Tests
```

To run all tests:

```powershell
dotnet test IOS-IG-SimHost.sln
```

---

## Developer Workflow

Before starting development, read the developer process guide:

> **[.dev-workstream/guides/DEV-GUIDE.md](../../.dev-workstream/guides/DEV-GUIDE.md)**

This guide defines how you should work on batches, write reports, handle reviews, and maintain quality standards. Following it is mandatory.

---

## Task Ordering

Tasks within a phase can generally be worked independently once the phase's foundation is ready. The hard dependencies are:

- **T001 must complete before T002, T003, T004, T005, T006, T008** (all phases depend on the component definitions).
- **T006 must complete before T008** (sync system must exist before personal route authoring uses it).
- **T008 and T009 must complete before T015** (personal route system replaces legacy before legacy is removed).
- **T004 + T005 must complete before T007** (egress/ingress must work before shared route authoring is tested end-to-end).

Phases 2–8 can largely be developed in parallel once Phase 1 is complete, but integration testing requires the full stack.

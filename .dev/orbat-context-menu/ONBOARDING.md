# Onboarding — ORBAT Context Menu

Welcome to the **ORBAT Context Menu** workstream. This document orients you on what we are building, where the relevant code lives, and how to get started.

---

## What We Are Building

We are adding a **right-click context menu** to entity rows in the IOS ORBAT panel. The menu gives operators five fast-access actions without leaving the order-of-battle tree:

| Action | When visible | What happens |
|--------|-------------|--------------|
| **Select entity** | Always | Selects the entity on the IG map (optimistic local update + network command) |
| **Center on entity** | Always | Pans the IG camera to the entity's current position |
| **Delete** | Always | Requests entity deletion via SimHost; row is locked until ACK |
| **Edit Route** | Physical entities only | Launches route-drawing tool on the IG; on completion the route is auto-assigned to the vehicle |
| **Abort Mission** | Physical entities only | Sends `CMD_ABORT_ALL` to halt the entity's active doctrine |

"Physical entities" are simulation units (vehicles, soldiers, etc.) as opposed to map graphics (routes, symbols, overlays).  The two mission-targeted actions are intentionally restricted to physical entities only.  **There is no propagation to subordinate entities** — actions always target only the entity that was right-clicked.

This workstream spans four projects: **DataModel** (new command type), **SimHost** (route-assignment bug fix), **IG** (command handling + personal-route orchestration), and **IOS** (ORBAT panel UI).

---

## Planning Documents

| Document | Purpose |
|----------|---------|
| [docs/orbat-context-menu/OC1-DESIGN.md](./OC1-DESIGN.md) | Full architectural design — phases, flows, rationale |
| [docs/orbat-context-menu/OC1-TASK-DETAIL.md](./OC1-TASK-DETAIL.md) | Per-task specifications with success conditions (unit test specs) |
| [docs/orbat-context-menu/OC1-TASK-TRACKER.md](./OC1-TASK-TRACKER.md) | Progress checklist — update as tasks complete |

**Read the design document first.**  The task details reference it by section so you do not need to duplicate context.

---

## Relevant Code Locations

### Shared Contracts — Data Model
- `Bagira.DDS.DataModel/MapMessages.cs` — `CommandType` enum and `MapCommandRequest`.  Add `CMD_DRAW_PERSONAL_ROUTE` here (OC1-C001).

### SimHost
- `Bagira.SimHost/Brains/SimHostNodes.cs` — `FollowRouteParams` struct, `ParseFollowRouteParams`, `Action_WriteFollowRouteChannel`.  The route-assignment fix (OC1-S001) lives here.

### IG
- `Bagira.IG/IgApplication.cs` — The main per-frame `Update()` method with the `MapCommandRequest` dispatch switch.  Add handling for `CMD_SET_SELECTION` (OC1-G001), `CMD_SET_VIEW` (OC1-G002), and `CMD_DRAW_PERSONAL_ROUTE` (OC1-G003) here.
- `Bagira.Map.Common/Commands/BdcCommandGateway.cs` — The async gateway the IG uses for two-step operations (`CreateEntityAsync`, `SendMissionControlRequestAsync`).  Used by OC1-G003.

### IOS
- `Bagira.IOS/Panels/OrbatPanel.cs` — The ORBAT tree panel.  The context menu popup, entity-type gate, and all action wiring go here (OC1-I001 through OC1-I006).
- `Bagira.IOS/IosLogic.cs` — Core IOS logic.  New intent-dispatch methods (`SendSetSelection`, `CenterOnEntity`, `DeleteEntity`, `StartPersonalRouteAuthoring`) go here, along with `_pendingDeleteEntityIds` tracking.
- `Bagira.IOS/Abstractions/IIosLogic.cs` — Interface to extend with the new methods.

### Tests
- `Bagira.SimHost.Tests/` — New test class for OC1-S001.
- `Bagira.IG.Tests/` — New test classes for OC1-G001, OC1-G002, OC1-G003.
- `Bagira.IOS.Tests/` — New test class(es) for OC1-I001 through OC1-I006.

---

## Build and Test

```powershell
# Build the whole solution
dotnet build IOS-IG-SimHost.sln --no-restore

# Run all tests
dotnet test Bagira.DDS.DataModel.Tests
dotnet test Bagira.SimHost.Tests
dotnet test Bagira.IG.Tests
dotnet test Bagira.IOS.Tests
```

---

## Workflow

Read [`.dev-workstream/guides/DEV-GUIDE.md`](.dev-workstream/guides/DEV-GUIDE.md) to understand the **batch-based development workflow** used in this project.  Work is assigned in batches; each batch references specific task IDs from this workstream.  Follow the instructions in that guide for how to implement tasks, write batch reports, and handle reviews.

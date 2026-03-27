# Onboarding — Two-ACK Entity Lifecycle Pattern

Welcome to the **Two-ACK Entity Lifecycle** workstream. This document orients you on what we are building, where to find everything, and how to work effectively.

---

## What We Are Building

We are implementing a **two-phase acknowledgment pattern** for entity creation and deletion in the BDC SST simulation system.

**The problem today:** When the IOS creates an entity, it gets a single `CreateEntityAck` the instant SimHost allocates a network ID. But the real distributed handshake (managed by FDP's `EntityLifecycleModule`) has not finished. If that handshake fails, the entity silently vanishes from the ORBAT tree with no explanation. There is also no `DeleteEntityRequest` at all — non-owning nodes cannot request deletion via a network API.

**What we are building:** A two-phase ACK pattern modelled after the existing, proven `MapCommandAck` design:
- **Phase 1 (InProgress):** SimHost responds immediately to say "I have the entity ID, handshake is underway".
- **Phase 2 (Success/Failure):** Once FDP's ELM finishes the distributed consensus, SimHost sends the terminal result.

The IOS uses this to display entities as "pending" (locked from operator interaction) until fully confirmed, and to show an explicit error modal if creation fails.

**Importantly: FDP is not touched.** All logic lives in `Bagira.SimHost` (which watches FDP's lifecycle transitions) and `Bagira.IOS`.

---

## Design and Task Documents

| Document | Purpose |
|----------|---------|
| [docs/two-ack/TWOACK-DESIGN.md](./TWOACK-DESIGN.md) | Full architectural design — phases, flows, data model, file references |
| [docs/two-ack/TWOACK-TASK-DETAIL.md](./TWOACK-TASK-DETAIL.md) | Per-task specifications with success conditions (unit test specs) |
| [docs/two-ack/TWOACK-TASK-TRACKER.md](./TWOACK-TASK-TRACKER.md) | Progress checklist — update this as tasks complete |

**Read the design document first.** The task detail document references it by section so you do not need to repeat context.

---

## Relevant Code Locations

### Data Model (DDS contract)
- `Bagira.DDS.DataModel/GenericMessages.cs` — All DDS message structs including `CreateUpdateDeleteEntityAck`, `CreateEntityRequest`, and the new `DeleteEntityRequest`. The `SstErrorCode` enum here is being renamed to `SstStatusCode`.

### SimHost (server-side two-ACK logic)
- `Bagira.SimHost/Systems/CreateEntityRequestSystem.cs` — Existing creation handler. Will be updated to send Phase 1 ACK and register with the new finalization system.
- `Bagira.SimHost/Systems/SstRequestFinalizationSystem.cs` — **New file** to create. Watches FDP lifecycle states and sends Phase 2 ACKs.
- `Bagira.SimHost/Systems/DeleteEntityRequestSystem.cs` — **New file** to create. Handles incoming `DeleteEntityRequest` messages.
- `Bagira.SimHost/SimHostApp.cs` and `Bagira.SimHost/Modules/SimHostModule.cs` — Registration points for systems.

### IOS (client-side)
- `Bagira.Runner/Services/IosSubsystem.cs` — DDS ingress wiring. Swap `CreateEntityAck` queue for `CreateUpdateDeleteEntityAck`.
- `Bagira.IOS/Services/DdsEventIngressHandlers.cs` — Ingress handler implementations.
- `Bagira.IOS/IosLogic.cs` — Core logic. Rewrite `ProcessEntityCreationAcks()`, add `_pendingEntities`.
- `Bagira.IOS/Abstractions/IIosLogic.cs` — Interface to extend with `IsEntityPending(int)` and `GlobalAlert`.
- `Bagira.IOS/Panels/MissionPanel.cs` — Add `ImGui.BeginDisabled/EndDisabled` guard when entity is pending.
- `Bagira.IOS/Logic/ContextMenuLogic.cs` — Return empty menu when entity is pending.
- `Bagira.IOS/UI/IosMock.cs` — Draw global error modal when `GlobalAlert` is set.

### FDP (read-only reference — do not modify)
- `FDP/Toolkits/FDP.Toolkit.Lifecycle/EntityLifecycleModule.cs` — The distributed handshake engine. `SstRequestFinalizationSystem` observes its output only.
- `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs` — `EntityLifecycle` enum: `Constructing`, `Active`, `TearDown`, `Ghost`.

### Tests
- `Bagira.DDS.DataModel.Tests/` — Data model tests
- `Bagira.SimHost.Tests/` — SimHost unit tests
- `Bagira.IOS.Tests/` (or `Bagira.IG.Tests/`) — IOS unit and integration tests

---

## How to Build

```powershell
# From the workspace root:
dotnet restore IOS-IG-SimHost.sln
dotnet build IOS-IG-SimHost.sln
```

Run tests for the affected projects:
```powershell
dotnet test Bagira.DDS.DataModel.Tests --no-restore -v q
dotnet test Bagira.SimHost.Tests --no-restore -v q
dotnet test Bagira.IG.Tests --no-restore -v q
```

---

## Developer Workflow

Read the **`.dev-workstream/guides/DEV-GUIDE.md`** document before starting. It explains the batch-based development process: how work is divided into batches, how to write batch reports, how to handle reviews, and the code quality standards expected.

Key points:
- Work is assigned in batch instruction files under `.dev-workstream/batches/`.
- When you finish a batch, submit a report using the template in `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`.
- Each task has explicit success conditions defined in [TWOACK-TASK-DETAIL.md](./TWOACK-TASK-DETAIL.md) — make sure all unit tests specified there are written and passing.
- Phase 1 (data model) **must be complete** before Phase 2 or Phase 3 can start, since both depend on the updated DDS structs.

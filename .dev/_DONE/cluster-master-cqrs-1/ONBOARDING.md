# Onboarding: ClusterMaster CQRS Decoupling

Welcome to the `cluster-master-cqrs-1` workstream. This document gets you up to speed quickly.

---

## What Are We Building?

We are refactoring the cluster state management architecture to achieve the same clean, network-agnostic design on the **master** side that the **slave** side already enjoys.

**In short:**

- `ClusterMaster` currently hard-wires CycloneDDS readers/writers and manually parses raw JSON strings inside business logic. It is impossible to unit test without a live network.
- `ClusterSlave` is already clean — it is network-agnostic and uses a transport interface.
- This workstream makes `ClusterMaster` equally clean by introducing CQRS intent/event structs on the `FdpEventBus` and pushing all DDS and JSON work to thin stateless translator classes.

---

## Key Documents

| Document | Purpose |
|----------|---------|
| [design_talk.md](./design_talk.md) | Full design conversation — read this to understand the *why* |
| [DESIGN.md](./DESIGN.md) | Formal design doc — architecture diagrams, data structures, phases |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specifications with success conditions (unit test specs) |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Quick progress checklist |
| [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) | **Read this before starting work.** Defines the developer workflow, batch system, reporting format |

---

## Solution Structure

The workspace root is `d:\Work\IOS-IG-SimHost-FDP-2`.

```
IOS-IG-SimHost.sln              ← main solution
FDP/FDP.sln                     ← FDP engine sub-solution
FDP/Toolkits/FDP.Toolkit.Orchestration/   ← ClusterSlave, IClusterStateHandler, enums (your main FDP target)
Hrot.Orchestrator/              ← ClusterMaster, Translators (your main Hrot target)
Hrot.Common/Orchestration/      ← DdsOrchestrationTransport, HrotHandlerAdapter (to be reworked/deleted)
Hrot.NED/Orchestration/         ← DDS wire structs & enums (READ-ONLY for FDP code)
Hrot.ClusterRunner/             ← AllInOne runner (composition root for tests)
Hrot.ClusterRunner.Integration.Tests/     ← Integration tests (regression suite)
Hrot.Orchestrator.Integration.Tests/      ← Orchestrator-specific integration tests
.dev/cluster-master-cqrs-1/    ← This design workstream (docs + batch instructions)
```

### Key Files

| File | Description |
|------|-------------|
| `Hrot.Orchestrator/ClusterMaster.cs` | The class being refactored — currently DDS-coupled |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs` | Reference model for how the slave achieves network agnosticism |
| `Hrot.Common/Orchestration/DdsOrchestrationTransport.cs` | Will be deleted after CMC-S007 |
| `FDP/Toolkits/FDP.Toolkit.Orchestration/IOrchestrationTransport.cs` | Will be deleted after CMC-S007 |
| `Hrot.NED/Orchestration/OrchestrationMessages.cs` | All DDS topic structs + Hrot-layer enums (ClusterState, NodeOpType, ClusterOpType) |
| `FDP/Kernel/Fdp.Kernel/FdpEventBus.cs` | Event bus used for CQRS intent routing |

---

## How to Build

```powershell
# Build everything
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln

# Build just the FDP engine (faster for Phase 1-3 work)
dotnet build FDP/FDP.sln

# Run integration tests
dotnet test Hrot.ClusterRunner.Integration.Tests
dotnet test Hrot.Orchestrator.Integration.Tests
```

---

## Architecture in 60 Seconds

The refactoring introduces a strict three-layer separation:

```
[Hrot Application Layer]
  ClusterOpMasterTranslator  — polls DDS ClusterOpRequest → publishes typed intents to bus
  NodeOpMasterTranslator     — consumes ExecuteNodeOpIntent from bus → writes NodeOpCommand to DDS
  NodeOpSlaveTranslator      — polls NodeOpCommand from DDS → publishes ExecuteNodeOpIntent to bus
  (JSON payload DTOs live here; use JsonStringEnumConverter for readable string payloads)

[FDP Domain Layer]             ← your code should NOT reference Hrot.NED here
  ClusterMaster              — consumes typed intents, runs 2PC state machine, publishes events
  ClusterSlave               — consumes ExecuteNodeOpIntent, dispatches to handlers, publishes NodeOpCompletedEvent
  IClusterStateHandler       — pure domain handler; CanHandle(NodeOpType), no JSON
  FDP enums (ClusterState, NodeOpType, ClusterOpType) — mirrors of Hrot.NED enums

[CycloneDDS Network]           ← only translators touch this
```

In **AllInOne mode** (no network): translators are simply not registered. Master and slave share the same `FdpEventBus`. The entire 2PC runs in memory.

---

## Important Rules

1. **No Hrot.NED references inside FDP.Toolkit.Orchestration.** The FDP domain defines its own duplicate enums (Dual-Enum Pattern). Integer values must match.
2. **No JSON parsing inside ClusterMaster or ClusterSlave.** All deserialization happens in translators.
3. **Control-plane events are `[DataPolicy(DataPolicy.NoRecord)]`.** They must never appear in `.fdprec` exercise recordings.
4. **1 ECS World = 1 FdpEventBus = 1 ClusterSlave.** In AllInOne, register all handlers into a single slave instance.
5. **Translators are stateless.** They must not hold transaction state or track in-flight requests.

---

## Developer Workflow

Read [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) for the full workflow. In summary:

1. Receive a batch instruction file from `batches/BATCH-XX-INSTRUCTIONS.md`.
2. Implement the tasks listed.
3. Write a batch report in `reports/BATCH-XX-REPORT.md` using the template.
4. If you have questions, create `questions/BATCH-XX-QUESTIONS.md`.
5. Wait for review feedback in `reviews/BATCH-XX-REVIEW.md`.

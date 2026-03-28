# CGF-1 Onboarding Guide

Welcome to the **CGF-1 workstream** — the foundational infrastructure for the
Distributed Drill Management System and the new `Bagira.CGF` subsystem.

Read this document first; then consult the developer workflow guide and the design docs.

---

## What We Are Building

The CGF-1 workstream (Phases 1–3) delivers the **control-plane infrastructure** that
will underpin all future simulation exercises on the Bagira/FDP platform:

| What | Description |
|------|-------------|
| **Bagira.Orchestrator** | New subsystem acting as the supreme state and time authority for the entire distributed cluster. Hosts the Drill State Machine (DSM), BFS Transition Planner, and Storage Gateway. |
| **Drill State Machine (DSM)** | 13-state directed graph (`Standby` → `RunningLive` → `UnloadingLive` → …) governing the lifecycle of every simulation node. Transitions are coordinated via Two-Phase Commit (2PC). |
| **DrillSlave** | A new component in every subsystem (SimHost, IG, IOS, CGF) that listens to `NodeOpCommand` messages from the Orchestrator and drives local state machine transitions. |
| **Distributed time control** | Seamless switching between real-time and deterministic lockstep modes using a "Future Barrier" mechanism — both nodes swap time strategies on the exact same ECS frame without blocking. |
| **Recording, replay & checkpointing** | LZ4-compressed binary recording of ECS state; instant replay seek via binary-search frame index; non-blocking 3-step checkpointing; portable JSON scenario save/load via SMB Pull Gateway. |
| **Bagira.CGF (skeleton)** | New "Brain" subsystem that will host entity AI (Phase 4). In Phases 1–3 it acts only as a `DrillSlave` and runs a trivial scenario for CI/determinism validation. |

> **Phase 4 (Urban Combat AI — `ConvoyEscort_HSM`, `Ambush_BT`, damage assessment)
> is OUT of scope for CGF-1.** It will begin only after all Phase 3 tasks are
> complete and their CI gates are green.

---

## Where to Find the Design and Task Documents

All CGF-1 documents live under `.dev/cgf-1/` in the repository root:

| Document | Purpose |
|----------|---------|
| [CGF-1-DESIGN.md](./.dev/cgf-1/CGF-1-DESIGN.md) | Full architectural design for Phases 1–3 — read this before writing any code |
| [CGF-1-TASK-DETAIL.md](./.dev/cgf-1/CGF-1-TASK-DETAIL.md) | Detailed per-task specs with unique IDs and explicit success conditions (test specs) |
| [CGF-1-TASK-TRACKER.md](./.dev/cgf-1/CGF-1-TASK-TRACKER.md) | Progress checklist — update this as tasks complete |
| [design-talk.md](./.dev/cgf-1/design-talk.md) | Original design conversation that motivated this workstream |
| [mgmt-DESIGN.md](./.dev/cgf-1/mgmt-DESIGN.md) | Full distributed management architecture reference (read for deep background) |

---

## Repository Folder Layout — Relevant Components

```
IOS-IG-SimHost-FDP-2/
│
├── FDP/                               ← FDP Platform (application-agnostic layer)
│   ├── Kernel/Fdp.Kernel/             ← ECS kernel, FlightRecorder, GlobalTime
│   │   └── FlightRecorder/            ← AsyncRecorder, PlaybackController, RecorderSystem
│   └── Toolkits/
│       ├── FDP.Toolkit.Time/          ← Time controllers (Master/Slave/Stepped/Switchable)
│       │   └── Controllers/           ← SwitchableTimeController, DistributedTimeCoordinator, …
│       └── FDP.Toolkit.Replication/   ← GhostCreationSystem (BypassLifecycle flag)
│
├── Bagira.DDS.DataModel/              ← All DDS message schemas (Bagira layer)
│   └── Orchestration/                 ← NEW: OrchestrationMessages.cs (Stage 1.1)
│
├── Bagira.Orchestrator/               ← NEW project (Stage 1.2)
│   ├── DrillMaster.cs
│   ├── TransitionPlanner.cs
│   ├── StorageGatewayModule.cs
│   └── ReplayMasterModule.cs
│
├── Bagira.CGF/                        ← NEW project (Stage 1.4)
│   └── Modules/Orchestration/DrillSlave.cs
│
├── Bagira.SimHost/
│   └── Modules/Orchestration/         ← NEW: DrillSlave, EcsRecordReplayController,
│       ├── DrillSlave.cs              │         RecordingModule, ReplayModule, handlers
│       ├── EcsRecordReplayController.cs
│       ├── RecordingModule.cs
│       ├── ReplayModule.cs
│       └── Handlers/
│           ├── LiveLoadDsmHandler.cs
│           ├── ReplayLoadDsmHandler.cs
│           ├── EditLoadDsmHandler.cs
│           └── CheckpointDsmHandler.cs
│
├── Bagira.IG/
│   └── Modules/Orchestration/DrillSlave.cs    ← NEW (Stage 1.4)
│
├── Bagira.IOS/
│   └── Orchestration/DrillSlave.cs            ← NEW no-ECS variant (Stage 1.4)
│
└── Bagira.Runner/
    └── Services/                      ← Existing subsystem wiring; gets Orchestrator support
```

---

## Critical Architectural Constraint

> **FDP infrastructure (`Fdp.Kernel` and all `FDP.Toolkit.*` projects) must NEVER
> reference any `Bagira.*` assembly.**

This boundary is hard and enforced at the csproj level. If you add a using directive
for any `Bagira.*` namespace inside a file that lives under `FDP/`, your build will
fail CI.

Practical consequences:
- `DSMState`, `SysOpType`, `NodeOpType` — declared in `Bagira.DDS.DataModel`.
- `IDsmHandler`, `DsmStateChangedEvent`, `DrillSlave`, `DrillMaster` — declared in Bagira layer.
- `ITimeController`, `IRecordReplayController`, `RecordingConfiguration`,
  `CheckpointIOWorker`, `NetworkLifecycleSystemGroup` — declared in FDP layer (generic,
  no DSM awareness).

If you are unsure which layer a file belongs to, check: does it need to know about
`DSMState` or `DrillId`? If yes → Bagira layer. If it only uses `GlobalTime`, `Guid`,
`EntityRepository` → FDP layer is fine.

---

## How to Build

```powershell
# Full solution (from repo root)
dotnet build IOS-IG-SimHost.sln

# Run all tests
dotnet test IOS-IG-SimHost.sln

# Run Orchestrator via Runner
dotnet run --project Bagira.Runner -- --mode orchestrator

# Run SimHost via Runner (connects to Orchestrator automatically)
dotnet run --project Bagira.Runner -- --mode simhost

# Run CGF via Runner
dotnet run --project Bagira.Runner -- --mode cgf

# Run all-in-one (all subsystems in one process)
run_all_together.bat

# Headless deterministic CI scenario
dotnet run --project Bagira.Runner -- --mode ci --scenario MinimalCI_01
```

The `build_all_standalone.bat` script in the repository root builds all subsystem
process variants via the Runner.

---

## Where to Start

For a developer picking this up for the first time, the recommended reading order is:

1. **This document** — you are here.
2. [CGF-1-DESIGN.md §1 and §2](./CGF-1-DESIGN.md#1-system-overview) — system overview
   and the FDP/Bagira architectural boundary.
3. [CGF-1-DESIGN.md §3](./CGF-1-DESIGN.md#3-phase-1--skeleton-control-plane-foundation) —
   Phase 1 design (first batch of work).
4. [CGF-1-TASK-DETAIL.md, tasks CGF1-S0101 through CGF1-S0104](./CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition) —
   the exact work and tests for Phase 1.
5. [mgmt-DESIGN.md §2–§6](./mgmt-DESIGN.md) — full DDS schema and DrillMaster
   internals for deeper reference.

---

## Developer Workflow

All development on this workstream follows the batch-based workflow defined in:

**[.dev/.guides/DEV-GUIDE.md](../.dev/.guides/DEV-GUIDE.md)**

Key points:
- Work is assigned in **batches** corresponding to one or more stages from the task tracker.
- Each batch ends with a written report referencing the task IDs and demonstrating
  the required success conditions pass.
- Do not skip ahead to Phase 2 tasks until all Phase 1 success conditions are green.
- Update [CGF-1-TASK-TRACKER.md](./CGF-1-TASK-TRACKER.md) as tasks complete.

---

## DDS Test Isolation — Contributor Note

**Problem:** Several test assemblies use DDS domain 0. When `dotnet test IOS-IG-SimHost.sln`
runs all assemblies in parallel, DDS participants in different test processes discover each
other and cause intermittent failures (e.g. `DomainIsolation_*`, migration tests).

**Mitigations applied (CGF-1-BATCH-02):**

| Where | Mitigation |
|-------|-----------|
| `Bagira.Orchestrator.Tests` | Uses **domain 15** exclusively; all tests grouped in `[Collection("OrchestratorTests")]` with `DisableParallelization = true`. |
| `Bagira.SimHost.Integration.Tests` | Existing `[Collection("LogCapture")]` with `DisableParallelization = true` — applies to migration and lifecycle tests. |
| `Bagira.SimHost.Integration.Tests` | New CGF-related tests (`DrillSlaveHeartbeatTests`) use **domain 16**. |

**Until a broader multi-assembly domain-isolation strategy is adopted**, CI pipelines that still
see flakes should run integration tests serially:

```powershell
dotnet test IOS-IG-SimHost.sln --maxcpucount:1 -- dotnet test
```

or target individual integration-test assemblies in isolation:

```powershell
dotnet test Bagira.SimHost.Integration.Tests
dotnet test Bagira.Orchestrator.Tests
```


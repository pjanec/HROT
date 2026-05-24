# Stride Mock — Onboarding Guide

Welcome to the **stride-mock** workstream. This guide gives you everything you need to understand what we are building, where the code lives, and how to get started.

---

## What Are We Building?

We are integrating the **Stride 3D engine** into the FDP/HROT distributed simulation cluster as a unified SimHost + IG node. The Stride node will:

- Run **ground vehicle kinematics, path planning, and perception** (currently owned by `SimHostSubsystem`).
- **Render entities in 3D** and act as a "dumb terminal" for tactical gizmos (currently owned by `IgSubsystem`).
- Fully participate in cluster orchestration: recording, replay, file management, diagnostics.

Because the Stride 3D engine is heavy, this workstream first builds a **lightweight proof-of-concept** using Raylib/ImGui. The architecture is designed so the Raylib shell is a drop-in swap for a real Stride app later.

### Key Documents

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Full architecture design — read this first |
| [TASK-DETAILS.md](./TASK-DETAILS.md) | Per-task specs with success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Current progress checklist |
| [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Known issues deferred to later batches |

---

## Important: Read the DEV-GUIDE

Before writing any code, read the **DEV-GUIDE.md** document (located in `.dev/.guides/`). It defines how developers on this project should behave: commit discipline, task sizing, test expectations, and review protocol.

---

## Architecture in 30 Seconds

```
Hrot.Common.Infrastructure
  └─ SharedApplicationBootstrapper (abstract, 7-phase pipeline)
        └─ StrideNodeBootstrapper (concrete, in Hrot.StrideMock)
              ├─ used by StrideMockSubsystem  ← runs inside ClusterRunner
              └─ used by FakeStrideApp        ← runs as a standalone process
```

The key design principle is **DRY**: both the `ClusterRunner` wrapper (`StrideMockSubsystem`) and the standalone app (`FakeStrideApp`) use the exact same `StrideNodeBootstrapper` core. The core has zero dependency on Raylib, ImGui, or the Runner toolkit.

---

## Folder Layout — What to Care About

### New code (this workstream)

```
Hrot/
  Engine/
    Hrot.Common/
      Infrastructure/
        SharedApplicationBootstrapper.cs   ← NEW (Phase 2)
  Subsystems/
    Hrot.StrideMock/                       ← NEW project (Phase 1)
      StrideNodeBootstrapper.cs
      SyncFdpToStrideScript.cs
      FakeStrideEntity.cs / FakeStrideEffect.cs
      StrideMockSubsystem.cs
  Runner/
    Hrot.ClusterRunner/
      Configuration/HrotRunnerConfiguration.cs  ← modified (Phase 4)
      Program.cs                                 ← modified (Phase 4)
    Hrot.FakeStrideApp/                    ← NEW project (Phase 1)
      FakeStrideApp.cs
      Program.cs
```

### Existing code you need to understand

| File | Why it matters |
|------|---------------|
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Reference implementation — StrideNodeBootstrapper mirrors its init logic |
| `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` | `BuildOrchestration()` — called from `SharedApplicationBootstrapper` Phase 5 |
| `Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs` | Thin adapter pattern — StrideMockSubsystem follows the same pattern |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | IG init patterns — refactored in Phase 6 |
| `FDP/Toolkits/Fdp.Toolkits/Runner/ISubsystem.cs` | Interface StrideMockSubsystem implements |
| `FDP/Toolkits/Fdp.Toolkits/Runner/IMapCameraProvider.cs` | Interface for camera sync on tab switch |
| `FDP/Engine/Fdp.Presentation/Raylib/FdpApplication.cs` | Base class for FakeStrideApp |
| `FDP/Engine/Fdp.Presentation/Vis2D/Components/MapCamera.cs` | Pan/zoom camera (HandleInput, Update, BeginMode, EndMode) |
| `Hrot/Engine/Hrot.Common/Infrastructure/HrotNodeBuilder.cs` | Builds HrotNodeContext |
| `Hrot/Runner/Hrot.ClusterRunner/Program.cs` | ResolveAppNodeId, ScanForSubsystems |

---

## How to Build

```powershell
# From the repo root
dotnet build IOS-IG-SimHost-FDP-2.sln

# Or build individual projects
dotnet build Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.csproj
dotnet build Hrot/Runner/Hrot.FakeStrideApp/Hrot.FakeStrideApp.csproj
```

---

## How to Run

### Standalone Fake Stride App (needs a running cluster master)

```powershell
# Terminal 1: start orchestrator
Hrot.ClusterRunner.exe -m orchestrator

# Terminal 2: run the standalone app (connects to cluster via DDS)
Hrot.FakeStrideApp.exe
```

### StrideMock inside ClusterRunner (replacing SimHost)

```powershell
# All in one process — StrideMock + CGF brain + Orchestrator
Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock
```

### StrideMock standalone (without waiting for peers)

```powershell
Hrot.ClusterRunner.exe -m stridemock --no-wait
```

---

## Key Concepts to Understand

### Differential 2-Pass ECS Sync
The `SyncFdpToStrideScript` keeps a `Dictionary<Entity, FakeStrideEntity>` keyed on the **full** `Entity` struct (Index + Generation). Every frame it runs two passes:
1. **Pass 1** — check `repo.IsAlive(e)` for all tracked entities; destroy stale ones.
2. **Pass 2** — query ECS for `SimTransform`; create or update corresponding fake entities.

This works correctly during live simulation, replay, and `ReplaySeek` time jumps — no special-case code needed.

### Dual-Buffer Gizmo Terminal
The node maintains two separate `DebugPrimitiveBuffer` instances:
- **ProducerBuffer** — local ECS systems write gizmos here; published to DDS via loopback.
- **ConsumerBuffer** — populated by `DebugPrimitivesIngressTranslator` from DDS; renderer reads here.

This allows the cluster to select which node's gizmo stream to display (including the local node's own stream) without feedback loops.

### Slave Time Mode
The Stride node never drives its own clock. The `SlaveSyncController` waits for `SwitchTimeModeEvent` from the Cluster Master. Local UI buttons (Pause/Step/Play) send `ClusterOpRequest` over DDS via `ITimeControlGateway` — the master processes them and broadcasts back.

### SharedApplicationBootstrapper Phase Order
The 7-phase order in `SharedApplicationBootstrapper.BootstrapNode()` is fixed to prevent 5 known init traps (see [DESIGN.md §4.2](./DESIGN.md#42-the-5-fragile-init-traps)). Never change the phase order or add registrations outside the designated hook methods.

---

## Node Identity

| Property | Value |
|----------|-------|
| Subsystem name | `"StrideMock"` |
| CLI mode | `stridemock` |
| NodeId offset | `700` (e.g., base 0 → NodeId 700) |
| Node role | `MuscleGround \| Perception \| NavigationSolver \| ImageGenerator` |
| TitleBarColor | Orange `(0.8f, 0.4f, 0.1f, 1f)` |
| Recording file | `<staging>/nodes/node-700/node_700.fdp` |

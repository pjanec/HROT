# Gizmos-2 Headless — Onboarding Guide

Welcome to the **Gizmos-2 Headless** implementation sprint. This document gives a new developer
everything needed to understand the scope of work, navigate the codebase, and start contributing.

---

## 1. What We Are Building

This sprint extends the FDP Gizmo Framework (established in the previous **Gizmos-1** sprint)
with operational infrastructure that enables:

1. **Zero-CPU headless operation** — SimHost, CGF, and ClusterRunner processes consume no CPU
   on gizmo systems when no debug terminal (UI window or remote engine) is connected.

2. **Dynamic terminal attach/detach** — A local Raylib/ImGui window or a remote DDS terminal can
   be hot-plugged into a running process at any time without restarting it.

3. **Live StructInspector sync** — Backend gizmos can push their live DTO state to any connected
   terminal transparently. The `StructInspectorProjector<T>` helper handles this in one line.

4. **Console-driven ClusterRunner** — The ClusterRunner window becomes optional; operators can
   open or close it from the console while the simulation keeps running.

5. **Input isolation** — When multiple subsystems run in the same process, only the active
   perspective receives canvas and gizmo input.

---

## 2. Design and Task Documents

| Document | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Full architectural design — read this first |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | One-line status for every task; start here each day |
| [TASK-DETAILS.md](./TASK-DETAILS.md) | Detailed description and success conditions per task |
| [DEBT-TRACKER.md](./DEBT-TRACKER.md) | Known technical debt introduced during implementation |

---

## 3. Developer Conduct

Read **[DEV-GUIDE.md](../.guides/DEV-GUIDE.md)** before writing any code. It defines branching
strategy, batch workflow, commit hygiene, and how to report blockers.

---

## 4. Key Components and Their Locations

### 4.1 Existing — `Fdp.Toolkits` (core gizmo framework)

```
FDP/Toolkits/Fdp.Toolkits/
  Diagnostics/Gizmos/
    Systems/
      DataDrivenGizmoSystem.cs   ← entity-bound gizmos (PostSimulation)
      GlobalGizmoManager.cs      ← standalone gizmos (PostSimulation)
      StatelessGizmoSystem.cs    ← stateless draw-only gizmos
    IGizmoUiStatePublisher.cs    ← interface: publish GizmoUiState JSON
    Settings/
      GizmoSettingsRegistry.cs   ← ComputeHash(name) used for schema hashes
```

### 4.2 Existing — `Fdp.ModuleHost` (kernel and scheduling)

```
FDP/Engine/Fdp.ModuleHost/
  Scheduling/
    TogglablePostSimulationGroup.cs  ← enable/disable a group of PostSim systems
  ModuleHostKernel.cs                ← InstallModuleAsync / UninstallModuleAsync
  Abstractions/
    IEcsModule.cs                    ← interface for installable modules
```

### 4.3 Existing — `GizmoMap.Presentation` (terminal/UI layer)

```
FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/
  UI/
    ImGuiPropertyTreeAdapter.cs  ← renders StructInspector; ReceiveUiState(GizmoUiState)
```

### 4.4 Existing — `GizmoMap.Example` (transport demos and tests)

```
FDP/ExtDeps/GizmoMap/GizmoMap.Example/
  Transport/
    LocalGizmoTransport.cs   ← in-memory primitive transport (examples/tests only)
    DdsGizmoTransport.cs     ← DDS primitive transport
```

### 4.5 Existing — `Hrot.Common` (shared gizmos)

```
Hrot/Engine/Hrot.Common/
  Diagnostics/Gizmos/
    LayerControlGizmo.cs     ← WILL BE REFACTORED in GZH-011
    LayerControlDto.cs       ← (inside LayerControlGizmo.cs)
```

### 4.6 Existing — `Hrot.ClusterRunner`

```
Hrot/Runner/Hrot.ClusterRunner/
  Program.cs                   ← WILL BE EXTENDED in GZH-012, GZH-013
  Services/                    ← new ConsoleCommandService goes here (GZH-013)
  Systems/
    PerspectiveCoordinatorSystem.cs  ← WILL BE EXTENDED in GZH-014
```

### 4.7 New — files to create

All in `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/`:

```
  Events/
    TerminalLifecycleEvents.cs       ← GZH-001
  GizmoExecutionController.cs        ← GZH-002
  UI/
    StructInspectorProjector.cs      ← GZH-006
  Hub/
    GizmoUiStateHub.cs               ← GZH-007
    LocalGizmoUiStateTransport.cs    ← GZH-008
  Modules/
    LocalTerminalModule.cs           ← GZH-009
    GizmoNetworkTransportModule.cs   ← GZH-010
```

---

## 5. Building the Project

### Prerequisites
- .NET 8 SDK
- CycloneDDS runtime (installed via the solution's ExtDeps)
- Raylib-cs (included in `FDP/ExtDeps`)

### Build

```
dotnet build Hrot.sln
```

or to build only the toolkits layer (faster for most tasks):

```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj
```

### Run unit tests

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
```

### Run the ClusterRunner

```
dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- --mode simhost,ig
dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- --mode simhost,ig --headless
```

---

## 6. Recommended Reading Order

1. [DESIGN.md §1](./DESIGN.md#1-dual-channel-architecture-visual--ui-state) — Dual-channel concept
2. [DESIGN.md §2](./DESIGN.md#2-new-types--fdptoolkits-assembly) — New types overview
3. [DESIGN.md §3](./DESIGN.md#3-togglablepostsimulationgroup-for-gizmos) — How the group is wired
4. [DESIGN.md §12](./DESIGN.md#12-composition-root-wiring-example) — End-to-end wiring example
5. Pick a task from [TASK-TRACKER.md](./TASK-TRACKER.md) and read its detail in
   [TASK-DETAILS.md](./TASK-DETAILS.md)

---

## 7. Key Architectural Invariants to Preserve

- **Never merge the primitive stream with UI-state.** The 64-byte `DebugPrimitive` struct cannot
  carry JSON. `GizmoUiState` uses a separate DDS topic with `TransientLocal` durability.
- **Gizmo managers must always be instantiable, even headless.** Backend tools register gizmos
  at startup regardless of terminal connectivity. Only the `Execute()` loop is gated.
- **Raylib calls are main-thread only.** `InitWindow`, `CloseWindow`, and `rlImGui.Setup` must
  never be called from a background thread or a `Task`.
- **`LocalGizmoUiStateTransport` uses overwrite semantics, not a queue.** This prevents OOM if
  the UI thread is stalled; only the last state per schema is delivered.

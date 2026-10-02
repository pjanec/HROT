<!--STATUS
state: LIVE
doc-type: project reference — what this assembly is and why it exists. Not a buildable design, so no
  build-state/UML gate; the owning design is docs/DESIGN_Stride_Port.md §7.
updated: 2026-08-23
current-answer: the whole file.
known-conflict: none.
-->
# Hrot.NodeComposition

**Project path:** `Hrot/Subsystems/Hrot.NodeComposition/`
**Project file:** `Hrot.NodeComposition.csproj`

---

## Executive Overview

`Hrot.NodeComposition` holds exactly one type: **`StrideNodeBootstrapper`**, the node
composition root the real Stride application consumes (`StrideHrotGame` holds it and
takes it via `AttachBootstrapper`).

It is a concrete `SharedApplicationBootstrapper` that wires a full simulation node:
ModuleHost scheduling, behavior, combat, gizmos, lifecycle, network spawning,
orchestration, scenario, and the IG and SimHost systems. Domain modules (kinematics,
perception, combat, navigation) are injected via the constructor so a Stride-native
implementation can be swapped in later without touching the orchestration code. It
implements the roles `NodeRole.MuscleGround`, `NodeRole.Perception`,
`NodeRole.NavigationSolver`, and `NodeRole.ImageGenerator`.

By design the class must not reference Raylib, ImGui, or `IMapCameraProvider` — it is
engine-agnostic.

### Why not `Hrot.Common`?

`SharedApplicationBootstrapper` lives in `Hrot.Common`, but `StrideNodeBootstrapper`
could not move there with it: it composes `Hrot.SimHost` and `Hrot.IG` systems, and both
of those already reference `Hrot.Common`. Moving the type down would make
`Hrot.Common` depend on `Hrot.SimHost`, inverting that edge into a project-reference
cycle. A composition root has to sit above the subsystems it composes, so it stays in
its own project. See `docs/DESIGN_Stride_Port.md`.

## Dependencies

| Project | Provides |
|---------|----------|
| `Hrot.Common` | `SharedApplicationBootstrapper` base class, `HrotNodeBuilder` |
| `Hrot.SimHost` | `NodeBootstrapper`, `HrotScenarioSerializerFactory` |
| `Hrot.IG` | `VisualEffectState`, `TracerTarget`, `EventToEffectSystem`, `VisualEffectCleanupSystem` |
| `Fdp.Core` | ECS world and core types |
| `Fdp.Toolkits` | `ClusterSlave`, `ScenarioSerializer`, `ISubsystem` |

## Tests

`Hrot.NodeComposition.Tests` covers this project:

| File | Covers |
|------|--------|
| `StrideNodeBootstrapperTests.cs` | `StrideNodeBootstrapper` construction and lifecycle |
| `SharedApplicationBootstrapperTests.cs` | The shared bootstrap contract it implements |

## History

This project is what remains of `Hrot.StrideMock` after the mock subsystem
(`StrideMockSubsystem`, `FakeStrideEntity`, `FakeStrideEffect`, `FakeStrideScript`,
`SyncFdpToStrideScript`) and the standalone `Hrot.FakeStrideApp` host were removed —
the real Stride port superseded them. `StrideNodeBootstrapper` was the one piece still
load-bearing for the real Stride app, so it stayed behind under the renamed project.
The mock's own design record is archived at `docs/designs/stride-mock/DESIGN.md`.

# Behavior Control Subsystem — Onboarding Guide

Welcome to the FDP **Behavior Control Subsystem** project. This document gives you the context and practical guidance you need to start contributing effectively.

---

## What Are We Building?

We are adding a complete **entity behavior control subsystem** to the FDP simulation engine, plus a headless demo application — **"Urban Ambush"** — that showcases the system end-to-end.

The subsystem provides:

- **Two-tier AI**: cheap hardcoded C# brains for background traffic, full state-machine/behavior-tree driven brains for tactical entities.
- **Universal action channels**: zero-allocation, fixed-buffer ECS components that decouple AI decision-making from physics actuation.
- **Toolkit-separated architecture**: five new reusable toolkits (`Behavior`, `Perception`, `Navigation`, `Combat`, `Physics`) on top of the existing FDP infrastructure.
- **FastBTree + FastHSM integration**: first-class adapters for the two existing AI VMs.
- **Asynchronous perception**: `Snapshot-on-Demand` background module for the expensive vision system.
- **2D raycast physics**: a native multi-threaded raycast solver for bullets and line-of-sight.

The demo scenario simulates an urban intersection: background cars and pedestrians go about their business; a military APC convoy drives into an ambush; the APC is disabled by an RPG; its passengers dismount and return fire; panicking civilians flee.

---

## Where Is Everything?

```
FDP/
├── Docs/
│   └── projects/
│       └── behavior-control/        ← YOU ARE HERE
│           ├── DESIGN.md            ← Full architecture and component reference
│           ├── TASK-DETAIL.md       ← Per-task spec + unit test success criteria
│           ├── TASK-TRACKER.md      ← Progress checklist
│           └── ONBOARDING.md        ← This file
│
├── Toolkits/
│   ├── FDP.Toolkit.Behavior/        ← [NEW] Brain orchestration (channels, dispatchers, BT/HSM adapters)
│   ├── FDP.Toolkit.Perception/      ← [NEW] Senses (audio, async vision, target memory)
│   ├── FDP.Toolkit.Navigation/      ← [NEW] Locomotion executor bridge to CarKinem
│   ├── FDP.Toolkit.Combat/          ← [NEW] Weapons, ballistics, damage
│   ├── FDP.Toolkit.Physics/         ← [NEW] 2D batch raycast solver
│   ├── FDP.Toolkit.CarKinem/        ← [EXISTING] Vehicle kinematics, road graph, RVO, spatial hash
│   └── FDP.Toolkit.Tkb/             ← [EXISTING] TKB template database
│
├── Examples/
│   └── Fdp.Examples.UrbanCombat/    ← [NEW] The demo app (thin layer, no heavy logic)
│
├── ExtDeps/
│   ├── FastBTree/                   ← Behavior tree VM (Fbt.Kernel)
│   └── FastHSM/                     ← Hierarchical state machine VM (Fhsm.Kernel)
│
├── Kernel/
│   └── Fdp.Kernel/                  ← ECS core: EntityRepository, ComponentSystem, events
│
└── ModuleHost/
    └── ModuleHost.Core/             ← Module orchestration, SystemPhase, IModule, ModuleHostKernel
```

### Core concepts to know

| Concept | Where defined | Purpose |
|---|---|---|
| `EntityRepository` | `Kernel/Fdp.Kernel` | The ECS "world" — holds all entities and component data |
| `ComponentSystem` | `Kernel/Fdp.Kernel` | Base class for synchronous systems (`SimulationSystemGroup` etc.) |
| `IModule` / `IModuleSystem` | `ModuleHost.Core/Abstractions` | Module registration and async execution policy |
| `ModuleHostKernel` | `ModuleHost.Core` | Orchestrates module lifecycle, executes phase order |
| `SystemPhase` | `ModuleHost.Core/Abstractions` | Input → BeforeSync → Simulation → PostSimulation → Export |
| `SimTransform`, `SimVelocity` | `Kernel/Fdp.Kernel` | Universal spatial presence — every entity with a world position uses `SimTransform` (`Position`+`Rotation`); moving entities also use `SimVelocity` (`Linear`+`Angular`) (**Phase 0**) |
| `VehicleState`, `NavState` | `Toolkits/FDP.Toolkit.CarKinem/Core` | Motor internals (speed, steer) + navigation intent; `VehicleState` no longer holds position/forward after Phase 0 |
| `BehaviorTreeState` | `ExtDeps/FastBTree/src/Fbt.Kernel` | 64-byte per-entity BTree stack state |
| `HsmInstance128` | `ExtDeps/FastHSM/src/Fhsm.Kernel/Data` | Unmanaged HSM instance (state machine state) |
| `TkbDatabase` / `TkbTemplate` | `Toolkits/FDP.Toolkit.Tkb` | Entity blueprints (component presets for spawning) |

---

## Start Reading

1. **[DESIGN.md](./DESIGN.md)** — Read this first. It describes the entire architecture: component layouts, system pipeline, demo scenario, and how all toolkits interconnect.
2. **Design talk** — `Docs/Behavior Control Subsystem Design.json.md` is the original AI-assisted design conversation (5258 lines; Universal Spatial Primitives discussion starts at line 4804). It contains detailed rationale and worked examples for every architectural decision. DESIGN.md references specific line ranges for each major topic.
3. **[TASK-DETAIL.md](./TASK-DETAIL.md)** — Every task has a description and concrete unit-test success conditions. Pick up a task from [TASK-TRACKER.md](./TASK-TRACKER.md) and use this as your spec.

---

## How to Build

The FDP solution uses .NET 8. All projects are standard C# class libraries or console apps.

```powershell
# Build the entire solution from the FDP root
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln

# Build only the new toolkits (once they exist)
dotnet build Toolkits/FDP.Toolkit.Behavior/
dotnet build Toolkits/FDP.Toolkit.Perception/
...

# Run the demo app
dotnet run --project Examples/Fdp.Examples.UrbanCombat/

# Run tests for a specific toolkit
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/
```

The demo app is headless (no window/graphics needed). Its output is plain console text — every significant simulation event is printed as a structured `[FRAME NNNN] ...` line.

---

## How to Develop a Task

1. Read the task section in [TASK-DETAIL.md](./TASK-DETAIL.md).
2. Check existing code that the task references (see "Reference" field).
3. Implement the code, ensuring it builds without errors.
4. Write the unit tests listed under "Success conditions".
5. Run tests: `dotnet test`.
6. Mark the task `[x]` in [TASK-TRACKER.md](./TASK-TRACKER.md).

### Zero-allocation rule (critical)
**No managed heap allocation on the hot path (the simulation loop).** Specifically:
- All components are `struct` (unmanaged value types).
- No `new SomeClass()` inside any system's `OnUpdate` / `Execute` / `Tick`.
- Use `Unsafe.As<byte, TStruct>()` for zero-copy reinterpretation of channel byte buffers.
- If a system builds a temporary list, use `stackalloc` or a pre-allocated `NativeArray` singleton.

### 256-component limit (critical)
The FDP `BitMask256` query system supports at most 256 distinct component types across the entire application. Every new `struct` registered via `world.RegisterComponent<T>()` consumes one slot. The channel design (`fixed byte Params[32]`) was specifically designed to allow thousands of action types without adding more component registrations.

---

## Key Architectural Patterns

### Channel pattern (AI → Physics)
Brain (BTree/HSM) writes an intent to a channel (`LocomotionChannel.ActiveAction = Flee`):
```
BTree Node → writes LocomotionChannel { ActiveAction, Params } 
DispatcherSystem → checks CanMove → calls FleeExecutor.Execute(...)
FleeExecutor → writes NavState { FinalDestination, TargetSpeed }
CarKinematicsSystem → reads NavState → updates VehicleState (position, speed)
```
No system directly updates positions. No system knows about another system's internals.

### Capability gating (damage → AI reaction)
```
HitEvent → DamageSystem → clears ActorCapabilityState.CanMove
→ LocomotionDispatcher fails LocomotionChannel on next frame (Status = Failure)
→ BrainHsm128 receives MobilityLost event via HsmDamageBridgeSystem
→ HSM transitions [Cruising] → [Disabled]
```

### Behavior preemption (Behavior version token)
```
AssignBehaviorEvent → BehaviorIngressSystem → BehaviorState.InstanceId++
→ ChannelArbitrationSystem → clears any channel whose BehaviorInstanceId is stale
→ Old executor OnExit is called by Dispatcher on next tick
```

---

## Key Source Files to Know Before You Start

| File | Why |
|---|---|
| `Kernel/Fdp.Kernel/ComponentSystem.cs` | Base class for your systems |
| `Kernel/Fdp.Kernel/EntityRepository.cs` | ECS world API: CreateEntity, AddComponent, GetComponentRW, etc. |
| `Kernel/Fdp.Kernel/StandardSystemGroups.cs` | `SimulationSystemGroup`, `InitializationSystemGroup` |
| `ModuleHost.Core/Abstractions/SystemPhase.cs` | Phase enum: Input, BeforeSync, Simulation, PostSimulation, Export |
| `ModuleHost.Core/Abstractions/IModule.cs` | Module interface doc + both patterns (System-based vs. Direct) |
| `ModuleHost.Core/ModuleHostKernel.cs` | Kernel: RegisterModule, RegisterGlobalSystem, Update |
| `Toolkits/FDP.Toolkit.CarKinem/Core/NavState.cs` | Navigation intent component bridged by Navigation executors |
| `Toolkits/FDP.Toolkit.CarKinem/Core/VehicleState.cs` | Physics output (Position, Forward, Speed) |
| `ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs` | 64-byte BTree instance state |
| `ExtDeps/FastBTree/src/Fbt.Kernel/IAIContext.cs` | Context interface BTree nodes receive |
| `ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs` | `HsmKernel.UpdateBatch<TInstance, TContext>` — the HSM tick API |
| `Examples/Fdp.Examples.BattleRoyale/Program.cs` | Existing demo app: reference for kernel setup pattern |

---

## About the Demo App

`Fdp.Examples.UrbanCombat` is intentionally thin. It should contain **no infrastructure logic** — only:
- TKB blueprint definitions
- Road graph construction
- Brain authoring (BTree JSON + HSM builder)
- Scenario spawning
- A hardcoded `TrafficBrainSystem` for Tier 1 entities
- `TelemetryReporterSystem` for console output

All simulation mechanics belong in the toolkit layer.

---

## Notes on Testing Style

- Tests use xUnit.
- The `TestWorldFactory.Create()` helper (to be written) creates a minimal `EntityRepository` with only the components registered by the toolkit under test.
- **Start with Phase 0 tests** — `SimComponentTests.cs` in `Fdp.Kernel.Tests` and `VehicleStateRefactorTests.cs` in `FDP.Toolkit.CarKinem.Tests` gate everything else. Do not begin Phase 1 until all Phase 0 tests are green and the entire solution builds.
- Prefer unit tests per-system over end-to-end tests; the integration test in P7-T9 is the single end-to-end assertion.
- Console output from `TelemetryReporterSystem` is the primary observable in integration tests — redirect `Console.Out` to a `StringWriter` and check for expected substrings.

---

## DEV-GUIDE

> ⚠️ A formal `DEV-GUIDE.md` has not been created yet. Until it exists, follow these principles:
> - FDP philosophy first: zero-alloc hot path, unmanaged structs, ECS data-oriented design.
> - All new components registered in the toolkit's `RegisterComponents` helper, not scattered.
> - Every public API must have XML doc comments.
> - Every task must have passing unit tests before being marked done in the tracker.
> - PR/commit messages reference the task ID, e.g. `[BCS-P1-T3] LocomotionDispatcherSystem implementation`.

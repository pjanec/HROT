# IOS-IG-SimHost-FDP Solution Overview

**Date:** 2026-05-23
**Scope:** Master entry-point document for the entire IOS-IG-SimHost-FDP solution.
Covers both sub-solutions (FDP framework and HROT application), their architecture,
external dependencies, cross-cutting concerns, getting-started paths, and a complete
project index.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [The Two Sub-Solutions](#2-the-two-sub-solutions)
3. [Solution-Level Architecture Diagram](#3-solution-level-architecture-diagram)
4. [FDP Framework](#4-fdp-framework)
5. [HROT Application](#5-hrot-application)
6. [External Dependencies (ExtDeps)](#6-external-dependencies-extdeps)
7. [Cross-Cutting Concerns](#7-cross-cutting-concerns)
8. [Getting Started Guide](#8-getting-started-guide)
9. [Complete Project Index](#9-complete-project-index)
10. [Key Architecture Decisions](#10-key-architecture-decisions)
11. [Technology Stack Summary](#11-technology-stack-summary)
12. [Documentation Index](#12-documentation-index)

---

## 1. Executive Summary

IOS-IG-SimHost-FDP is a distributed military simulation platform written in C# 12 / .NET 8.
It is organized in two layers: the **FDP** (Framework for Distributed Processing) provides
a reusable, general-purpose simulation engine based on ECS, hot-pluggable modules,
CycloneDDS networking, Raylib/ImGui rendering, and a binary flight recorder; the **HROT**
(High-Realism Operations Technology) layer builds on FDP to implement a full
combined-arms tactical simulation with a Brain/Muscle split-authority model, a visual
AI behavior authoring suite, a visual scripting (Blueprint) system, and a complete
multi-node cluster spanning an Orchestrator, SimHost (physics), CGF (AI), IG (rendering),
and ExCon (operator station) node. All inter-node communication is DDS pub/sub over
CycloneDDS. A single entry-point executable (`Hrot.ClusterRunner`) can host any
combination of subsystems in one process or distribute them across multiple machines.

---

## 2. The Two Sub-Solutions

### 2.1  FDP -- Framework for Distributed Processing

FDP is the **platform layer**. It is domain-agnostic: nothing in FDP knows about tanks,
soldiers, or missions. It provides:

- An ECS kernel (`Fdp.Core`) with chunk-based unmanaged storage, bitmask queries, a
  double-buffered event bus, deterministic phase permissions, and an LZ4 flight recorder.
- A module host (`Fdp.ModuleHost`) that schedules `IEcsModule` instances with RCU
  hot-plugging, circuit breakers, and snapshot isolation for background threads.
- A visual runtime (`Fdp.Presentation`) built on Raylib + ImGui providing a 2D map
  canvas, an entity inspector, an event browser, a replay browser, and a profiler panel.
- A CycloneDDS adapter (`Fdp.Network.Cyclone`) that bridges ECS components to DDS
  topics, allocates distributed network IDs, and drives ingress/egress via ECS systems.
- A simulation toolkit library (`Fdp.Toolkits`) covering 19 functional domains: AI
  behavior dispatch, ground-vehicle kinematics, combat resolution, distributed entity
  replication, geographic transforms, navigation, perception, cluster orchestration,
  and more.
- Roslyn source generators and analyzers that eliminate registration boilerplate and
  enforce memory-safety invariants at compile time.
- A CLI recording dumper tool that converts binary `.fdp` files to human-readable JSON.

**FDP can be used standalone** (without HROT) to build any simulation application. It
is the base-class library that HROT consumes.

### 2.2  HROT -- High-Realism Operations Technology

HROT is the **application layer**. It implements a military combined-arms simulation
on top of FDP. Key concerns exclusive to HROT:

- A **Brain/Muscle** split-authority model: the CGF node owns all cognitive state
  (behavior trees, mission plans, entity spawn authority); the SimHost node owns all
  physical state (kinematics, physics, combat resolution, sensor coverage).
- A **visual AI behavior authoring** suite: separate graphical editors for Behavior
  Trees and Hierarchical State Machines with hot-reload, live debug overlays, and
  breakpoint support.
- A **Blueprint scripting** system: a visual dataflow editor that compiles `.bp.json`
  graph assets through an 8-stage compiler and Roslyn into hot-loadable C# assemblies.
- A **distributed cluster orchestrator** that drives a cluster state machine over
  two-phase commit, manages recording and replay, controls synchronized wall-clock
  advancement, and acts as a storage gateway for scenario assets.
- A **tactical image generator** (IG) that renders the simulation picture as a 2D
  tactical map in real time, entirely driven by DDS ghost replication from SimHost.
- An **exercise control station** (ExCon/IOS) for the operator: entity placement,
  mission assignment, scenario lifecycle control, and cluster monitoring.

---

## 3. Solution-Level Architecture Diagram

```
+========================================================================+
|                         HROT Application Layer                         |
|                                                                        |
|  +----------------+  +------------+  +----------+  +---------------+  |
|  | Hrot.Orchestrat|  | Hrot.SimHost|  | Hrot.CGF |  |   Hrot.IG     |  |
|  | (ClusterMaster |  | (Muscle /  |  | (Brain / |  | (Image Gen /  |  |
|  |  2PC, state-   |  |  Ground    |  |  AI /    |  |  Ghost repl / |  |
|  |  machine,      |  |  Kinematics|  |  Behavior|  |  2D map       |  |
|  |  asset I/O)    |  |  Combat)   |  |  Trees)  |  |  render)      |  |
|  +----------------+  +------------+  +----------+  +---------------+  |
|                                                                        |
|  +---------------+  +-------------+  +-------------------------------+ |
|  | Hrot.ExCon    |  | Hrot.Editor |  | Blueprints (Core/Compiler/   | |
|  | (IOS /        |  | (Scenario   |  |  Editor/Generators)          | |
|  |  Operator     |  |  Authoring) |  |  Visual scripting system     | |
|  |  Station)     |  +-------------+  +-------------------------------+ |
|  +---------------+                                                     |
|                                                                        |
|  +---------------------------+  +----------------------------------+   |
|  | AI Authoring Editors      |  | Hrot Engine Layer                |   |
|  | BTree.Editor + Hsm.Editor |  | Hrot.Core + Hrot.Common +        |   |
|  | + Editor.AiShared         |  | Hrot.Presentation + UI.Common    |   |
|  +---------------------------+  +----------------------------------+   |
|                                                                        |
|  +------------------------------------------------------------------+  |
|  | HROT Network Layer                                               |  |
|  | Hrot.Network.NED (full 30+ topics)                              |  |
|  | Hrot.Network.BDC (lightweight 2-topic federation)               |  |
|  | Hrot.Network.Orchestration (cluster 2PC protocol)               |  |
|  +------------------------------------------------------------------+  |
+========================================================================+
|                         FDP Framework Layer                            |
|                                                                        |
|  +--------------------+  +---------------------+  +----------------+  |
|  | Fdp.Core           |  | Fdp.ModuleHost       |  | Fdp.Toolkits   |  |
|  | ECS kernel         |  | Module lifecycle,    |  | 19-domain      |  |
|  | Entity/Component   |  | scheduling, RCU      |  | simulation     |  |
|  | EventBus, Recorder |  | hot-plug, snapshots  |  | toolkit        |  |
|  +--------------------+  +---------------------+  +----------------+  |
|                                                                        |
|  +--------------------+  +---------------------+  +----------------+  |
|  | Fdp.Presentation   |  | Fdp.Network.Cyclone  |  | Fdp.Diagnostics|  |
|  | Raylib + ImGui     |  | CycloneDDS adapter   |  | .Contracts +   |  |
|  | MapCanvas, panels  |  | Ingress/Egress/ID    |  | .Network       |  |
|  +--------------------+  +---------------------+  +----------------+  |
|                                                                        |
|  +------------------------------------------------------------------+  |
|  | Roslyn Tooling                                                   |  |
|  | Fdp.Toolkits.Analyzers (BTree/HSM/Gizmo generators, FDP_001)    |  |
|  | Fdp.Toolkit.Tkb.SourceGen (TKB descriptor auto-registration)    |  |
|  +------------------------------------------------------------------+  |
+========================================================================+
|                     External Dependencies (ExtDeps)                    |
|                                                                        |
|  +-------------+  +-----------+  +------------+  +-----------------+  |
|  | FastBTree   |  | FastHSM   |  | GizmoMap   |  | NodeEdit        |  |
|  | (Fbt.Kernel |  | (Fhsm.Ker-|  | (debug vis |  | (node-graph     |  |
|  |  /Compiler) |  |  nel/Comp)|  |  / DDS)    |  |  canvas widget) |  |
|  +-------------+  +-----------+  +------------+  +-----------------+  |
|                                                                        |
|  +----------+  +------------------+  +--------------------------------+|
|  | StructEdit|  | CycloneDDS.NET   |  | Raylib-cs / rlImGui-cs / NLog ||
|  | (property |  | (NuGet DDS bind) |  | (NuGet rendering / logging)   ||
|  |  editor)  |  +------------------+  +--------------------------------+|
|  +----------+                                                           |
+========================================================================+
```

---

## 4. FDP Framework

### 4.1 Purpose and Philosophy

FDP solves three interlocking problems that arise in any serious simulation:

1. **Data explosion** -- Thousands of entities with dozens of components each. FDP
   stores all component data in contiguous unmanaged memory chunks, never on the GC
   heap. Queries are bitmask intersection operations. There is no object graph.

2. **Mixed computational loads** -- Some systems must run at 60 Hz on the main thread
   (physics, input); others are compute-heavy but latency-tolerant (AI, analytics).
   `Fdp.ModuleHost` gives each module an independent execution policy, a private
   read-only snapshot, and a circuit breaker. The main loop never stalls.

3. **Distributed operation** -- Multiple simulation nodes share world state over a
   network. `Fdp.Network.Cyclone` maps each ECS component to a typed DDS topic.
   The same ECS system code that reads from local memory also reads from DDS with
   zero application-layer changes.

### 4.2 Key FDP Components

| Project | Layer | Purpose |
|---------|-------|---------|
| `Fdp.Core` | Infrastructure | ECS kernel: entities, components, queries, event bus, phase system, flight recorder, time |
| `Fdp.ModuleHost` | Framework | Module lifecycle, RCU hot-plug, topo-sorted system scheduler, snapshot isolation |
| `Fdp.Presentation` | UI | Raylib app host, 2D map canvas, ImGui debug panels, window manager |
| `Fdp.Diagnostics.Contracts` | Contracts | `IDebugDrawBuilder`, `DebugPrimitive` -- zero-alloc gizmo emission |
| `Fdp.Diagnostics.Network` | Adapter | DDS transport for gizmo streams (wraps CycloneDDS without leaking it) |
| `Fdp.Network.Cyclone` | Network | CycloneDDS adapter, `CycloneNetworkModule`, ingress/egress ECS systems, distributed ID allocator |
| `Fdp.Toolkits` | Domain | 19-domain simulation toolkit: Behavior, Combat, CarKinem, DER, Geographic, Navigation, Perception, Replay, Scenario, Time sync, and more |
| `Fdp.Toolkits.Analyzers` | Build | Roslyn analyzers (`FDP_001`) and source generators (BTree, HSM, Gizmo dispatch tables) |
| `Fdp.Toolkit.Tkb.SourceGen` | Build | Source generator: auto-registers `[TkbDescriptor]` DTOs via `[ModuleInitializer]` |
| `Fdp.Tools.RecordingDumper` | Tool | CLI: converts `.fdp` binary recordings to JSON (post-mortem analysis, CI regression) |

### 4.3 How to Build with FDP

The minimal scaffolding for a new FDP-based simulation is:

```
1. Create EntityRepository              (Fdp.Core)
2. Create ModuleHostKernel(world)       (Fdp.ModuleHost)
3. kernel.RegisterModule(new MyModule())
4. Derive FdpApplication                (Fdp.Presentation)
   -- OnLoad()   : call kernel.RegisterModule(...)
   -- OnUpdate() : call kernel.Update(deltaTime)
   -- OnDrawWorld() / OnDrawUI() : read world for rendering
5. new MyApp().Run()
```

Networking is added by registering a `CycloneNetworkModule` with the kernel and
implementing `CycloneTranslator<TDds, TView>` subclasses for each DDS topic.

### 4.4 Links to FDP Project Documentation

- [Fdp.Core](FDP/Core/Fdp.Core.md)
- [Fdp.ModuleHost](FDP/Core/Fdp.ModuleHost.md)
- [Fdp.Presentation](FDP/Core/Fdp.Presentation.md)
- [Fdp.Diagnostics.Contracts](FDP/Core/Fdp.Diagnostics.Contracts.md)
- [Fdp.Diagnostics.Network](FDP/Core/Fdp.Diagnostics.Network.md)
- [Fdp.Network.Cyclone](FDP/Network/Fdp.Network.Cyclone.md)
- [Fdp.Toolkits](FDP/Toolkits/Fdp.Toolkits.md)
- [Fdp.Toolkits.Analyzers](FDP/Toolkits/Fdp.Toolkits.Analyzers.md)
- [Tkb.SourceGen](FDP/Toolkits/Tkb.SourceGen.md)
- [Fdp.Tools.RecordingDumper](FDP/Tools/Fdp.Tools.RecordingDumper.md)
- [FDP Core Framework (relationships)](relationships/FDP-Core-Framework.md)
- [FDP Network Stack (relationships)](relationships/FDP-Network-Stack.md)

---

## 5. HROT Application

### 5.1 Purpose

HROT implements a distributed combined-arms tactical simulation. Operators use it to
run training exercises: compose scenarios in the Editor, deploy them to a live cluster,
observe entity behavior in real-time via the IG, issue commands from ExCon, and review
after-action recordings via the Replay Browser. The AI subsystem (CGF) autonomously
drives all simulated units through behavior trees; human operators can override or
redirect units at any time via ExCon.

### 5.2 Node Topology (IOS-IG-SimHost Architecture)

The "IOS-IG-SimHost" naming in the repository reflects the three primary operational
nodes visible to operators:

```
+--------------------+          DDS Domain (default: 0)           +--------------------+
|   Orchestrator     |<------------------------------------------>|  ExCon (IOS)       |
|  (cluster master,  |                                            | (operator console, |
|   2PC, state       |                                            |  scenario lifecycle)|
|   machine)         |                                            +--------------------+
+--------+-----------+                                                     |
         |  NodeOpCommand / NodeOpStatus                     ClusterOpRequest
         |  ClusterState / SysOpStatus                       CreateEntityRequest
         |                                                   MissionControlRequest
         v
+--------+------------------+     NavigationIntent      +--------------------+
|    CGF  (Brain)           |<------------------------->|  SimHost (Muscle)  |
|  Behavior Trees,          |  NavigationStatus         |  Ground kinematics |
|  Mission Planning,        |<-- WorldPos (ghost) ------>  Combat / Ballistics|
|  Entity spawn authority   |                            |  Perception (LOS)  |
+---------------------------+                            +--------+-----------+
         |                                                        |
         |  EntityMaster, WorldPos                               | WorldPos,
         |  NavigationStatus, EntityDamage                       | EntityDamage,
         v                                                        v
+-----------------------------------------------------------------------+
|                    IG  (Image Generator)                              |
|         Ghost replication + 2-D tactical map rendering               |
|         Operator pick / context-menu dispatch to ExCon               |
+-----------------------------------------------------------------------+
```

All nodes can be co-hosted in one process (via `--mode all` in `Hrot.ClusterRunner`)
or distributed across machines. The DDS discovery protocol handles topology changes
transparently.

### 5.3 The Brain/Muscle Split

| Attribute | Brain (CGF) | Muscle (SimHost) |
|-----------|-------------|-----------------|
| NodeRole flags | `Brain` | `MuscleGround \| Perception` |
| ECS components owned | `BehaviorState`, `BrainBlackboard`, `MissionPlan`, `TargetMemory` | `SimTransform`, `WorldPos`, `NavigationStatus`, `PhysicsState` |
| DDS writes | `EntityMaster`, `NavigationIntent`, `WeaponFireIntent` | `WorldPos`, `NavigationStatus`, `EntityDamage` |
| Spawn authority | Default processor for `CreateEntityRequest` | Not a default processor |
| AI systems | BTree interpreter, HSM kernel, mission adapter | None |
| Physics systems | None | Ground kinematics, combat, ballistics, LOS |

### 5.4 Key HROT Subsystems

| Project | Mode token | Role |
|---------|-----------|------|
| `Hrot.Orchestrator` | `orchestrator` | Cluster state machine, 2PC coordinator, NAS asset gateway |
| `Hrot.SimHost` | `simhost` | Ground kinematics, combat resolution, LOS/sensor perception |
| `Hrot.CGF` | `cgf` | AI behavior trees, mission planning, entity lifecycle authority |
| `Hrot.IG` | `ig` | 2-D map rendering, ghost replication, operator pick |
| `Hrot.ExCon` | `excon` / `ios` | Exercise control operator station |
| `Hrot.Editor` | `editor` | Offline scenario authoring, no DDS required |
| `Hrot.AI.Behaviors` | (library) | 8 behavior implementations (MoveToLocation, FollowRoute, FireAtTarget, HullDownAttackRun, PlatoonHillAttack, ...) |
| `Hrot.Blueprints.*` | (library) | Visual scripting: Library / AiPrimitive / Instance dispatch kinds |
| `Hrot.BTree.Editor` | (embedded) | Visual BTree authoring with live debug overlay |
| `Hrot.Hsm.Editor` | (embedded) | Visual HSM authoring with live debug overlay |
| `Hrot.ReplayBrowser` | `replaybrowser` | Offline recording inspection, search, and JSON export |
| `Hrot.StrideMock` | `stridemock` | Stride engine mock node for CI / GPU-free environments |
| `Hrot.ClusterRunner` | -- | Single entry-point executable for the entire cluster |

### 5.5 HROT Engine Layer

The HROT engine layer (not a "subsystem" itself, but the shared foundation of all
subsystems) consists of:

| Project | Role |
|---------|------|
| `Hrot.Core` | Domain vocabulary, network interfaces, `HrotNodeConfig/Context`, dead reckoning |
| `Hrot.Common` | Node bootstrap (`HrotNodeBuilder`), gizmo library, mission control execution, unit hierarchy, genesis intent DTOs |
| `Hrot.Presentation` | HROT-specific renderers, behavior-parameter UI, scenario editor module, shared panels |
| `Hrot.UI.Common` | Hexagonal-architecture UI facades and reusable ImGui panels (Config, Spawner, ORBAT, Mission, Preview) |

### 5.6 HROT Network Layer

| Project | Protocol | Topics | Use Case |
|---------|----------|--------|----------|
| `Hrot.Network.NED` | NED (full) | 30+ DDS topics | All production multi-node runs |
| `Hrot.Network.BDC` | BDC (minimal) | 2 DDS topics (`BDC_EntityMaster`, `BDC_WorldPos`) | Lightweight IG tracking, federation gateways |
| `Hrot.Network.Orchestration` | Orchestration | 7 DDS topics | Cluster 2PC, state machine, heartbeat |

Both NED and BDC implement the same `INetworkFactory` / `IReplicationModule` interfaces.
Selection is configuration-time: higher-level code is protocol-agnostic.

### 5.7 Links to HROT Project Documentation

**Engine layer:**
- [Hrot.Core](Hrot/Engine/Hrot.Core.md)
- [Hrot.Common](Hrot/Engine/Hrot.Common.md)
- [Hrot.Presentation](Hrot/Engine/Hrot.Presentation.md)
- [Hrot.UI.Common](Hrot/Engine/Hrot.UI.Common.md)

**Network layer:**
- [Hrot.Network.NED](Hrot/Network/Hrot.Network.NED.md)
- [Hrot.Network.BDC](Hrot/Network/Hrot.Network.BDC.md)
- [Hrot.Network.Orchestration](Hrot/Network/Hrot.Network.Orchestration.md)

**Runner:**
- [Hrot.ClusterRunner](Hrot/Runner/Hrot.ClusterRunner.md)
- [Hrot.FakeStrideApp](Hrot/Runner/Hrot.FakeStrideApp.md)

**Subsystems:**
- [Hrot.Orchestrator](Hrot/Subsystems/Hrot.Orchestrator.md)
- [Hrot.SimHost](Hrot/Subsystems/Hrot.SimHost.md)
- [Hrot.CGF](Hrot/Subsystems/Hrot.CGF.md)
- [Hrot.IG](Hrot/Subsystems/Hrot.IG.md)
- [Hrot.ExCon](Hrot/Subsystems/Hrot.ExCon.md)
- [Hrot.Editor](Hrot/Subsystems/Hrot.Editor.md)
- [Hrot.AI.Behaviors](Hrot/Subsystems/Hrot.AI.Behaviors.md)
- [Hrot.ReplayBrowser](Hrot/Subsystems/Hrot.ReplayBrowser.md)
- [Hrot.StrideMock](Hrot/Subsystems/Hrot.StrideMock.md)

**Blueprints:**
- [Hrot.Blueprints.Core](Hrot/Blueprints/Hrot.Blueprints.Core.md)
- [Hrot.Blueprints.Compiler](Hrot/Blueprints/Hrot.Blueprints.Compiler.md)
- [Hrot.Blueprints.Editor](Hrot/Blueprints/Hrot.Blueprints.Editor.md)
- [Hrot.Blueprints.Generators](Hrot/Blueprints/Hrot.Blueprints.Generators.md)

**AI Editors:**
- [Hrot.BTree.Editor](Hrot/AI/Hrot.BTree.Editor.md)
- [Hrot.Hsm.Editor](Hrot/AI/Hrot.Hsm.Editor.md)
- [Hrot.Editor.AiShared](Hrot/Editor/Hrot.Editor.AiShared.md)

**Relationship docs:**
- [Hrot Simulation Pipeline](relationships/Hrot-Simulation-Pipeline.md)
- [AI Behavior Authoring](relationships/AI-Behavior-Authoring.md)
- [Blueprint Scripting System](relationships/Blueprint-Scripting-System.md)

---

## 6. External Dependencies (ExtDeps)

External dependencies live under `FDP/ExtDeps/` as source-included projects rather
than NuGet packages. The policy is: embed when the dependency is tightly coupled to
the simulation data model or requires performance-critical modifications that would
not be possible through a public NuGet API.

| Library | Path | What it provides | Why embedded |
|---------|------|-----------------|--------------|
| **FastBTree** (`Fbt.*`) | `FDP/ExtDeps/FastBTree/` | Behavior tree runtime kernel, compiler, fluent builder, source generator attributes (`[BTreeDefinition]`, `[BTreeAction]`, `[BTreeCondition]`) | Core data structures (`BrainBlackboard`, `BehaviorTreeBlob`) must be shared between the kernel and application code; no stable ABI boundary. |
| **FastHSM** (`Fhsm.*`) | `FDP/ExtDeps/FastHSM/` | Hierarchical state machine kernel, compiler, `HsmBuilder` fluent API, HSM instance structs | Same reason as FastBTree; `HsmDefinitionBlob` is co-designed with the simulation's entity component layout. |
| **GizmoMap** | `FDP/ExtDeps/GizmoMap/` | Debug visualization over DDS: `DebugPrimitive` wire type, `GizmoMap.Network` DDS topics, `GizmoMap.Contracts` canonical types | The `DebugPrimitive` type must be identical at the CLR level in every assembly in the process; this is enforced via `TypeForwards.cs` in `Fdp.Diagnostics.Contracts`. |
| **NodeEdit** | `FDP/ExtDeps/NodeEdit/` | Generic node-graph canvas widget (ImGui-based): `NodeEditor.Core` (host interfaces) + `NodeEditor.UI` (canvas renderer) | Used by both the Blueprint editor and the BTree/HSM editors; a shared in-repo version allows simultaneous evolution across all three consumers. |
| **StructEdit** | `FDP/ExtDeps/StructEdit/` | Reflection-driven property editor widget: `StructEdit.Core` (drawer registry) + `StructEdit.Reflection` (auto-discovered drawers) | Requires deep integration with FDP component type system for custom drawer registration. |

### Why Not NuGet?

NuGet packages are appropriate for stable, versioned, independently-releasable libraries.
These five dependencies are effectively "inner-source" co-dependencies: their internal
types appear directly in public interfaces of the consuming projects. Publishing them to
NuGet would require a formal versioning discipline that the project does not currently
need. Source inclusion allows breaking changes to be co-committed atomically.

---

## 7. Cross-Cutting Concerns

### 7.1 DDS Networking -- The Glue

CycloneDDS is the backbone of all inter-node communication. Every piece of simulation
state that crosses a process boundary travels as a DDS topic sample. Key properties:

- **No broker**: DDS uses peer-to-peer discovery. Adding or removing a node requires
  zero reconfiguration on other nodes.
- **Instance lifecycle**: entity existence is governed by `EntityMaster`. Writing the
  key creates the entity across all subscribers; disposing it destroys it. No explicit
  spawn/despawn protocol is required.
- **QoS** determines trade-offs per topic: `WorldPos` is best-effort (UDP; occasional
  loss is fine) while `NodeOpCommand` is reliable (guaranteed delivery). Each topic
  declares its own policy; the application code never sees raw UDP.
- **Anti-Corruption Layer**: all DDS wire types are isolated behind translator classes
  (`CycloneTranslator<TDds, TView>`, `INetworkFactory`). Application-layer code
  (ECS systems, UI panels) never imports CycloneDDS types directly.

See [FDP Network Stack](relationships/FDP-Network-Stack.md) for the full
stack diagram and per-topic QoS table.

### 7.2 ECS Data Model -- The Foundation

All simulation state is stored in `EntityRepository` instances. Components are
unmanaged structs packed in 64 KB `NativeChunkTable` pages. Queries are compiled at
startup from bitmask predicates and run in O(1) on the chunk index. Key invariants:

- **Zero GC on hot paths**: all component access, query iteration, event publish/read,
  and bitmask query evaluation is allocation-free.
- **Phase permissions**: each simulation phase (Input, BeforeSync, Simulation,
  PostSimulation) carries a compile-time permission set. Writing to a read-locked
  component in the wrong phase is a runtime exception in debug builds.
- **Determinism**: the flight recorder captures delta-compressed snapshots of the
  entire world. Playback is deterministic: given the same recording, every subscriber
  sees the same component values at the same frame.

See [FDP Core Framework](relationships/FDP-Core-Framework.md) for the full
ECS layering model.

### 7.3 AI Behavior Authoring -- The Content

AI behaviors are authored visually, compiled to C# at development time, and executed
by the FastBTree / FastHSM kernels at simulation time. The flow is:

```
Visual Editor (BTree / HSM canvas)
    --> emits .cs source via BTreeFluentEmitter / HsmFluentEmitter
    --> compiled by Fbt.Compiler / Fhsm.Compiler into BehaviorTreeBlob / HsmDefinitionBlob
    --> registered by AiBehaviorFactory into BehaviorRegistry
    --> ticked per entity by the CGF Brain-tier ECS systems
```

Hot-reload is supported: the editor watches for assembly file-system changes and swaps
behavior blobs atomically without pausing the simulation loop. The `FbtAssemblyHotReloader`
uses a hash-delta classifier (Cosmetic / Soft / Hard) to decide whether running entity
state is safe to preserve across the reload.

See [AI Behavior Authoring](relationships/AI-Behavior-Authoring.md).

### 7.4 Blueprint Scripting -- Configuration-Driven Behavior

Blueprints complement the BTree/HSM system with a visual dataflow scripting language.
A Blueprint is a `.bp.json` file that is either:

- **Compiled at MSBuild time** by `Hrot.Blueprints.Generators` (a Roslyn
  `IIncrementalGenerator`) into `.g.cs` files baked into the assembly.
- **Compiled at runtime** by `Hrot.Blueprints.Core`'s in-process `InMemoryRoslynCompiler`
  for the editor's Quick Reload workflow.

Three dispatch kinds allow Blueprints to appear as BTree leaf nodes (`AiPrimitive`),
as standalone stateful actors (`Instance`), or as shared utility functions (`Library`).
The `Hrot.Blueprints.Editor` provides the full visual authoring experience with undo/redo,
an inspector, a debug session, and a hot-reload log.

See [Blueprint Scripting System](relationships/Blueprint-Scripting-System.md).

---

## 8. Getting Started Guide

### 8.1 "I Want to Run the Simulation"

1. **Build the solution**:
   ```
   cd FDP && dotnet build FDP.sln -c Release
   cd ..\Hrot && dotnet build (locate Hrot solution file)
   ```
   Or use the provided `build_all_standalone.bat` at the solution root.

2. **Start a full cluster in one process** (development / demo mode):
   ```
   Hrot.ClusterRunner.exe --mode all
   ```
   This starts Orchestrator + SimHost + IG + ExCon + CGF in a single process with a
   Raylib window.

3. **Start individual nodes** (production / multi-machine):
   ```
   # Machine 1 (headless orchestrator + simhost + cgf)
   Hrot.ClusterRunner.exe --mode orchestrator,simhost,cgf --headless

   # Machine 2 (IG with window)
   Hrot.ClusterRunner.exe --mode ig

   # Machine 3 (ExCon / IOS with window)
   Hrot.ClusterRunner.exe --mode excon
   ```

4. **Load a scenario**: in the ExCon window, use the Cluster Scenario panel to select
   a scenario from the NAS/local path configured in `config.json`, then click Load.

5. **Observe** the tactical map in the IG window. Use ExCon to spawn units, assign
   missions, and control exercise pace.

6. **Use the provided bat scripts** at the solution root for common configurations:
   `run_IG.bat`, `run_IOS.bat`, `run_SimHost.bat`, `run_all_together.bat`.

### 8.2 "I Want to Develop a New FDP Module"

1. Add a new C# class library targeting `net8.0` to `FDP/Engine/` or `FDP/Toolkits/`.

2. Reference `Fdp.Core` and optionally `Fdp.ModuleHost`.

3. Implement `IEcsModule`:
   ```csharp
   public sealed class MyModule : IEcsModule
   {
       public string Name => "My.Module";
       public ExecutionPolicy ExecutionPolicy => ExecutionPolicy.SynchronousMainThread;
       public void RegisterSystems(ISystemRegistry registry)
       {
           registry.Add(new MySystem());
       }
   }
   ```

4. Implement one or more `IEcsModuleSystem`:
   ```csharp
   public sealed class MySystem : IEcsModuleSystem
   {
       // declare phase, dependencies, queries
       public void Update(ISimulationView view, float deltaTime) { ... }
   }
   ```

5. Register the module in your application's composition root:
   ```csharp
   kernel.RegisterModule(new MyModule());
   ```

6. Add Roslyn generators to the project if new BTree actions, HSM actions, or gizmo
   projectors are required (reference `Fdp.Toolkits.Analyzers` as an analyzer).

### 8.3 "I Want to Add New AI Behavior"

1. Open `Hrot/Subsystems/Hrot.AI.Behaviors/` in the Editor via `--mode editor`.

2. Create a new BTree definition method in the existing `.cs` behavior files or a new
   file, decorated with `[BTreeDefinition]` and `[BTreeAction]` / `[BTreeCondition]`
   attributes:
   ```csharp
   [BTreeDefinition]
   public static BehaviorTreeBlob MyPatrolBehavior(...)
   { ... }
   ```

3. Use the `Hrot.BTree.Editor` canvas to compose the tree visually, or write it using
   the `BTreeBuilder<BrainBlackboard, BTreeContext>` fluent API directly.

4. Register the behavior in `AiBehaviorFactory` with a unique integer ID (use 3000+
   range per project convention):
   ```csharp
   registry.Register(3020, "MyPatrol", myBlob);
   ```

5. Implement a `ITacticalOrderMapper` if the behavior should be accessible by name from
   ExCon mission orders (e.g. `"PatrolArea"`).

6. Build. The `BTreeActionGenerator` source generator automatically produces the
   dispatch table; no manual registration is needed.

See [AI Behavior Authoring](relationships/AI-Behavior-Authoring.md) and
[Hrot.AI.Behaviors](Hrot/Subsystems/Hrot.AI.Behaviors.md).

### 8.4 "I Want to Extend the Editor"

**Adding a new scenario entity type:**

1. Define the ECS components in `Hrot.Core` (the domain vocab library).
2. Add a spawner entry in `Hrot.Common`'s genesis pipeline.
3. Add the TKB entity type constant to `TkbEntityTypes`.
4. Implement an `ISpawnController` adapter in `Hrot.Editor` and wire it into the
   `SpawnerPanel`.

**Adding a new ORBAT panel capability:**

1. Add a method to the `IOrbatController` facade in `Hrot.UI.Common.Facades`.
2. Implement the method in `EditorOrbatAdapter` (`Hrot.Editor`) and `ExConOrbatAdapter`
   (`Hrot.ExCon`).
3. Add the UI call in `SharedOrbatPanel` -- both shells pick it up automatically.

**Adding a new gizmo projector:**

1. Implement a class with `[GizmoProjector]` in the appropriate project.
2. The `GizmoRegistrarGenerator` source generator discovers it and emits the registration
   call -- no manual wiring needed.

**Adding a Blueprint node type:**

1. Define the node type in the Blueprint schema (`Hrot.Blueprints.Core`).
2. Add a code-emission handler in `Stage7_Emit.cs` (`Hrot.Blueprints.Compiler`).
3. Add a node model class to `Hrot.Blueprints.Editor`'s graph model layer.

---

## 9. Complete Project Index

### 9.1 FDP Engine

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Fdp.Core` | Engine / ECS | ECS kernel: entities, components, queries, event bus, flight recorder, time | [link](FDP/Core/Fdp.Core.md) |
| `Fdp.Core.Benchmarks` | Benchmark | Micro-benchmarks for Fdp.Core hot paths | -- |
| `Fdp.Core.Tests` | Test | Unit tests for Fdp.Core | -- |
| `Fdp.ModuleHost` | Engine / Orchestration | Module lifecycle, scheduling, RCU hot-plug, snapshot isolation | [link](FDP/Core/Fdp.ModuleHost.md) |
| `Fdp.ModuleHost.Benchmarks` | Benchmark | Benchmarks for ModuleHostKernel hot paths | -- |
| `Fdp.ModuleHost.Tests` | Test | Unit tests for Fdp.ModuleHost | -- |
| `Fdp.Presentation` | Engine / UI | Raylib app host, 2D map canvas, ImGui panels, window manager | [link](FDP/Core/Fdp.Presentation.md) |
| `Fdp.Presentation.Tests` | Test | Unit tests for Fdp.Presentation | -- |

### 9.2 FDP Diagnostics

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Fdp.Diagnostics.Contracts` | Diagnostics | `IDebugDrawBuilder`, zero-alloc gizmo emission contracts | [link](FDP/Core/Fdp.Diagnostics.Contracts.md) |
| `Fdp.Diagnostics.Contracts.Tests` | Test | Tests for Diagnostics.Contracts | -- |
| `Fdp.Diagnostics.Network` | Diagnostics / Network | CycloneDDS adapter for gizmo DDS channel | [link](FDP/Core/Fdp.Diagnostics.Network.md) |

### 9.3 FDP Network

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Fdp.Network.Cyclone` | Network | CycloneDDS ECS adapter, ingress/egress systems, distributed ID allocator | [link](FDP/Network/Fdp.Network.Cyclone.md) |
| `Fdp.Network.Cyclone.Tests` | Test | Tests for Fdp.Network.Cyclone | -- |

### 9.4 FDP Toolkits

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Fdp.Toolkits` | Toolkit | 19-domain simulation toolkit (Behavior, Combat, CarKinem, DER, Geographic, Navigation, Perception, Replay, Scenario, Time, Tkb, ...) | [link](FDP/Toolkits/Fdp.Toolkits.md) |
| `Fdp.Toolkits.Analyzers` | Build / Roslyn | Roslyn analyzers and source generators (BTree/HSM/Gizmo dispatch, `FDP_001`) | [link](FDP/Toolkits/Fdp.Toolkits.Analyzers.md) |
| `Fdp.Toolkit.Tkb.SourceGen` | Build / Roslyn | Source gen: auto-registers `[TkbDescriptor]` DTOs | [link](FDP/Toolkits/Tkb.SourceGen.md) |
| `Fdp.Toolkit.DER` | Toolkit | Distributed Entity Repository (separate from main Toolkits) | -- |
| `Fdp.Toolkit.DER.Examples` | Example | DER usage examples | -- |
| `Fdp.Toolkit.DER.Tests` | Test | Tests for Fdp.Toolkit.DER | -- |
| `Fdp.Engine` | Toolkit | Additional engine-level toolkit | -- |
| `Fdp.Engine.Tests` | Test | Tests for Fdp.Engine | -- |

### 9.5 FDP Examples

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Fdp.Examples.CarKinem` | Example | Ground-vehicle kinematics example | -- |
| `Fdp.Examples.CarKinem.Tests` | Test | Tests for CarKinem example | -- |
| `Fdp.Examples.Common` | Example | Common helpers for examples | -- |
| `Fdp.Examples.DDS` | Example | CycloneDDS usage demonstration | -- |
| `Fdp.Examples.DER` | Example | Distributed Entity Repository example | -- |
| `Fdp.Examples.IdAllocatorDemo` | Example | Distributed ID allocator demo | -- |
| `Fdp.Examples.NetworkDemo` | Example | Multi-node DDS networking demo | -- |
| `Fdp.Examples.NetworkDemo.Tests` | Test | Tests for NetworkDemo | -- |
| `Fdp.Examples.Runner` | Example | Multi-example runner host | -- |
| `Fdp.Examples.Scenarios` | Example | Scenario load/save examples | -- |
| `Fdp.Examples.Scenarios.Tests` | Test | Tests for Scenarios example | -- |
| `Fdp.Examples.Showcase` | Example | Full feature showcase | -- |
| `Fdp.Examples.UrbanCombat` | Example | Urban combat scenario example | -- |
| `Fdp.Examples.UrbanCombat.Tests` | Test | Tests for UrbanCombat example | -- |

### 9.6 FDP Tools

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Fdp.Tools.RecordingDumper` | Tool (CLI) | Converts `.fdp` binary recordings to JSON | [link](FDP/Tools/Fdp.Tools.RecordingDumper.md) |

### 9.7 HROT Engine Layer

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Hrot.Core` | Engine | Domain vocabulary, network abstractions, node config, dead reckoning | [link](Hrot/Engine/Hrot.Core.md) |
| `Hrot.Common` | Engine | Node bootstrap, gizmo library, mission control execution, unit hierarchy | [link](Hrot/Engine/Hrot.Common.md) |
| `Hrot.Presentation` | Engine / UI | HROT renderers, behavior-parameter UI, scenario editor module | [link](Hrot/Engine/Hrot.Presentation.md) |
| `Hrot.UI.Common` | Engine / UI | Hexagonal-architecture UI facades, shared ImGui panels | [link](Hrot/Engine/Hrot.UI.Common.md) |

### 9.8 HROT Network Layer

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Hrot.Network.NED` | Network | Full NED protocol (30+ DDS topics) -- production inter-node communication | [link](Hrot/Network/Hrot.Network.NED.md) |
| `Hrot.Network.BDC` | Network | Lightweight BDC protocol (2 DDS topics) -- federation / IG tracking | [link](Hrot/Network/Hrot.Network.BDC.md) |
| `Hrot.Network.Orchestration` | Network | Cluster 2PC protocol, orchestration DDS topics (7) | [link](Hrot/Network/Hrot.Network.Orchestration.md) |

### 9.9 HROT Runner

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Hrot.ClusterRunner` | Executable | Single entry point for the entire cluster; polyglot runner | [link](Hrot/Runner/Hrot.ClusterRunner.md) |
| `Hrot.FakeStrideApp` | Executable | Standalone Raylib host for StrideMock subsystem | [link](Hrot/Runner/Hrot.FakeStrideApp.md) |

### 9.10 HROT Subsystems

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Hrot.Orchestrator` | Subsystem | Cluster state machine, 2PC coordinator, NAS gateway, asset inventory | [link](Hrot/Subsystems/Hrot.Orchestrator.md) |
| `Hrot.SimHost` | Subsystem | Authoritative Muscle node: kinematics, physics, combat, LOS perception | [link](Hrot/Subsystems/Hrot.SimHost.md) |
| `Hrot.CGF` | Subsystem | Brain node: AI behavior trees, mission planning, entity spawn authority | [link](Hrot/Subsystems/Hrot.CGF.md) |
| `Hrot.IG` | Subsystem | Image Generator: 2-D tactical map, ghost replication, operator pick | [link](Hrot/Subsystems/Hrot.IG.md) |
| `Hrot.ExCon` | Subsystem | Exercise Control operator station (IOS): scenario control, monitoring | [link](Hrot/Subsystems/Hrot.ExCon.md) |
| `Hrot.Editor` | Subsystem | Offline scenario authoring, entity placement, mission planning, zone authoring | [link](Hrot/Subsystems/Hrot.Editor.md) |
| `Hrot.AI.Behaviors` | Subsystem / Library | 8 runtime AI behaviors; BTree + HSM definitions, tactical order mappers | [link](Hrot/Subsystems/Hrot.AI.Behaviors.md) |
| `Hrot.ReplayBrowser` | Subsystem | Offline recording inspection, search, diff, JSON export, causality jump | [link](Hrot/Subsystems/Hrot.ReplayBrowser.md) |
| `Hrot.StrideMock` | Subsystem | Stride engine mock node (GPU-free, CI-friendly) | [link](Hrot/Subsystems/Hrot.StrideMock.md) |

### 9.11 HROT Blueprints

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Hrot.Blueprints.Core` | Blueprints | Runtime assembly: asset model, 8-stage compiler pipeline, debug infrastructure | [link](Hrot/Blueprints/Hrot.Blueprints.Core.md) |
| `Hrot.Blueprints.Compiler` | Blueprints | AOT compiler: 7-stage pipeline (Parse through Emit), `netstandard2.0` | [link](Hrot/Blueprints/Hrot.Blueprints.Compiler.md) |
| `Hrot.Blueprints.Editor` | Blueprints / UI | ImGui graph editor, inspector, hot-reload, debug session | [link](Hrot/Blueprints/Hrot.Blueprints.Editor.md) |
| `Hrot.Blueprints.Generators` | Blueprints / Build | Roslyn `IIncrementalGenerator` for `.bp.json` -> `.g.cs` at MSBuild time | [link](Hrot/Blueprints/Hrot.Blueprints.Generators.md) |

### 9.12 HROT AI Editors

| Project | Category | Description | Doc |
|---------|----------|-------------|-----|
| `Hrot.BTree.Editor` | AI Editor | Visual BTree authoring: NodeEdit canvas, fluent emitter, live debug overlay | [link](Hrot/AI/Hrot.BTree.Editor.md) |
| `Hrot.Hsm.Editor` | AI Editor | Visual HSM authoring: states, transitions, orthogonal regions, debug overlay | [link](Hrot/AI/Hrot.Hsm.Editor.md) |
| `Hrot.Editor.AiShared` | AI Editor | Shared infra: catalog, selection, references, refactoring, hot-reload, debug sessions | [link](Hrot/Editor/Hrot.Editor.AiShared.md) |

---

## 10. Key Architecture Decisions

The following decisions define the character of this solution. Understanding them is
essential before proposing structural changes.

**1. ECS over object-oriented entity graph**
All simulation state is flat unmanaged data in `NativeChunkTable` pages. There are no
`Entity` class hierarchies, no virtual dispatch on simulation objects. This decision
enables the zero-allocation hot path, AVX2-accelerated bitmask queries, deterministic
delta compression in the flight recorder, and lock-free snapshot isolation for background
modules.

**2. Module host with RCU hot-plugging**
`ModuleHostKernel` uses a Read-Copy-Update pattern for the module topology. A module
can be installed or removed while the simulation runs at 60 Hz; the only main-thread
cost is an atomic pointer swap at a safe phase boundary. This enables live reloading of
AI behavior assemblies without pausing the simulation.

**3. Brain/Muscle split-authority**
Cognitive state (behavior trees, mission plans) and physical state (kinematics, physics)
are owned by separate nodes. Neither node crosses the authority boundary. The split
allows the CGF (Brain) to be scaled, replaced, or tested independently of the SimHost
(Muscle), and vice versa. It also allows the IG to be a pure read-only ghost node.

**4. Protocol-neutral network interfaces**
`INetworkFactory` and `IReplicationModule` abstract the DDS protocol. The NED (full)
and BDC (lightweight) protocols are drop-in replacements at configuration time. No
subsystem logic knows which protocol it is running on. This also enables the Editor
to operate fully offline by injecting an `OfflineNetworkFactory` that returns null stubs.

**5. Anti-Corruption Layer for all DDS traffic**
Every DDS topic has a corresponding translator class that maps wire types to ECS
components and events. Application-layer code (ECS systems, UI panels) never imports
`CycloneDDS` types. This boundary ensures that a DDS schema change requires editing
only the translator; all downstream systems are unaffected.

**6. Ahead-of-time (AOT) compilation for AI behaviors**
Behavior trees and HSMs are compiled to `BehaviorTreeBlob` / `HsmDefinitionBlob`
binary artifacts at build time, not interpreted at runtime. The blobs are flat byte
arrays; the interpreter is a tight dispatch loop with no GC interaction. This gives
predictable 60 Hz tick latency regardless of tree complexity.

**7. Roslyn source generators over hand-written registrations**
BTree action dispatch tables, HSM action dispatch tables, gizmo registrar tables, and
TKB descriptor registrations are all emitted by Roslyn source generators. Adding a new
behavior, gizmo, or descriptor DTO requires only the domain attribute -- no manual
registration. The generators also enforce invariants at compile time (e.g. `FDP_001`
enforces the 100-byte `BehaviorParameters` limit).

**8. Blueprint scripting as a compile-time and runtime concern**
Blueprint `.bp.json` assets can be compiled at MSBuild time (via
`Hrot.Blueprints.Generators`) for production builds with zero runtime startup cost, or
at runtime by the in-process Roslyn compiler for the editor's Quick Reload workflow.
The same 8-stage compiler pipeline is used for both paths; only the Roslyn finalization
stage differs.

**9. Hexagonal (Ports and Adapters) architecture for UI panels**
Every reusable UI panel depends only on interface facades declared in `Hrot.UI.Common`.
No panel has direct access to `EntityRepository`, `FdpEventBus`, or DDS types. Concrete
adapters implement the facades differently in each shell (Editor: direct ECS calls;
ExCon: DDS command calls). This makes every panel independently unit-testable.

**10. Co-hosted multi-node isolation via per-subsystem FdpEventBus**
When multiple subsystems run in one process (e.g. `--mode all`), each subsystem receives
its own `FdpEventBus` and `EntityRepository`. Events do not bleed across subsystem
boundaries even within a single process. Cross-subsystem communication happens only
via DDS, exactly as it would between separate processes. This prevents co-hosting from
masking bugs that would appear in a distributed deployment.

---

## 11. Technology Stack Summary

| Concern | Technology |
|---------|-----------|
| Language | C# 12.0 |
| Runtime | .NET 8.0 (`net8.0`) |
| Build tool | MSBuild (via `dotnet build`) |
| ECS kernel | Custom (Fdp.Core) |
| DDS middleware | Eclipse CycloneDDS via `CycloneDDS.NET` (NuGet) |
| Behavior trees | FastBTree (`Fbt.Kernel` / `Fbt.Compiler`, ExtDep) |
| Hierarchical state machines | FastHSM (`Fhsm.Kernel` / `Fhsm.Compiler`, ExtDep) |
| Visual scripting | Custom Blueprint system (HROT) |
| 2D rendering | Raylib-cs (NuGet) |
| Debug / editor UI | Dear ImGui via rlImGui-cs (NuGet) |
| Node-graph canvas | NodeEdit (`NodeEditor.Core` + `NodeEditor.UI`, ExtDep) |
| Property editor | StructEdit (`StructEdit.Core` + `.Reflection`, ExtDep) |
| Debug visualization (gizmos) | GizmoMap (ExtDep) |
| Compression (flight recorder) | LZ4 |
| JSON serialization | `System.Text.Json` |
| Logging | NLog |
| Roslyn tooling | `Microsoft.CodeAnalysis` (source generators, analyzers) |
| Test framework | NUnit (inferred from test project conventions) |
| CI entry point | `Hrot.ClusterRunner --mode ci` (headless deterministic harness) |

---

## 12. Documentation Index

All generated documentation lives under `docs/`.

### 12.1 Relationship Documents

| Document | Covers |
|----------|--------|
| [FDP Core Framework](relationships/FDP-Core-Framework.md) | Fdp.Core + Fdp.ModuleHost + Fdp.Presentation + Fdp.Diagnostics.Contracts as a unified framework |
| [Hrot Simulation Pipeline](relationships/Hrot-Simulation-Pipeline.md) | Full IOS/IG/SimHost distributed pipeline with Brain/Muscle split |
| [FDP Network Stack](relationships/FDP-Network-Stack.md) | All network layers from CycloneDDS up through NED/BDC/Orchestration |
| [AI Behavior Authoring](relationships/AI-Behavior-Authoring.md) | BTree/HSM authoring pipeline from editor to runtime execution |
| [Blueprint Scripting System](relationships/Blueprint-Scripting-System.md) | Full Blueprint compiler/editor/generator/runtime stack |

### 12.2 FDP Project Documents

| Document | Project |
|----------|---------|
| [Fdp.Core](FDP/Core/Fdp.Core.md) | ECS kernel |
| [Fdp.ModuleHost](FDP/Core/Fdp.ModuleHost.md) | Module orchestration |
| [Fdp.Presentation](FDP/Core/Fdp.Presentation.md) | Raylib/ImGui visual runtime |
| [Fdp.Diagnostics.Contracts](FDP/Core/Fdp.Diagnostics.Contracts.md) | Debug draw contracts |
| [Fdp.Diagnostics.Network](FDP/Core/Fdp.Diagnostics.Network.md) | Gizmo DDS channel |
| [Fdp.Network.Cyclone](FDP/Network/Fdp.Network.Cyclone.md) | CycloneDDS adapter |
| [Fdp.Toolkits](FDP/Toolkits/Fdp.Toolkits.md) | 19-domain simulation toolkit |
| [Fdp.Toolkits.Analyzers](FDP/Toolkits/Fdp.Toolkits.Analyzers.md) | Roslyn analyzers and generators |
| [Tkb.SourceGen](FDP/Toolkits/Tkb.SourceGen.md) | TKB descriptor source generator |
| [Fdp.Tools.RecordingDumper](FDP/Tools/Fdp.Tools.RecordingDumper.md) | Recording to JSON CLI tool |

### 12.3 HROT Engine Documents

| Document | Project |
|----------|---------|
| [Hrot.Core](Hrot/Engine/Hrot.Core.md) | HROT domain kernel |
| [Hrot.Common](Hrot/Engine/Hrot.Common.md) | Shared foundation (bootstrap, gizmos, missions) |
| [Hrot.Presentation](Hrot/Engine/Hrot.Presentation.md) | HROT engine-level presentation |
| [Hrot.UI.Common](Hrot/Engine/Hrot.UI.Common.md) | Hexagonal UI facades and panels |

### 12.4 HROT Network Documents

| Document | Project |
|----------|---------|
| [Hrot.Network.NED](Hrot/Network/Hrot.Network.NED.md) | Full NED protocol |
| [Hrot.Network.BDC](Hrot/Network/Hrot.Network.BDC.md) | Lightweight BDC protocol |
| [Hrot.Network.Orchestration](Hrot/Network/Hrot.Network.Orchestration.md) | Cluster orchestration protocol |

### 12.5 HROT Runner Documents

| Document | Project |
|----------|---------|
| [Hrot.ClusterRunner](Hrot/Runner/Hrot.ClusterRunner.md) | Cluster executable entry point |
| [Hrot.FakeStrideApp](Hrot/Runner/Hrot.FakeStrideApp.md) | Standalone StrideMock host |

### 12.6 HROT Subsystem Documents

| Document | Project |
|----------|---------|
| [Hrot.Orchestrator](Hrot/Subsystems/Hrot.Orchestrator.md) | Cluster orchestrator |
| [Hrot.SimHost](Hrot/Subsystems/Hrot.SimHost.md) | Authoritative simulation (Muscle) |
| [Hrot.CGF](Hrot/Subsystems/Hrot.CGF.md) | Computer-generated forces (Brain) |
| [Hrot.IG](Hrot/Subsystems/Hrot.IG.md) | Image generator |
| [Hrot.ExCon](Hrot/Subsystems/Hrot.ExCon.md) | Exercise control station |
| [Hrot.Editor](Hrot/Subsystems/Hrot.Editor.md) | Scenario editor |
| [Hrot.AI.Behaviors](Hrot/Subsystems/Hrot.AI.Behaviors.md) | Runtime AI behaviors |
| [Hrot.ReplayBrowser](Hrot/Subsystems/Hrot.ReplayBrowser.md) | Replay inspection tool |
| [Hrot.StrideMock](Hrot/Subsystems/Hrot.StrideMock.md) | Stride engine mock |

### 12.7 HROT Blueprint Documents

| Document | Project |
|----------|---------|
| [Hrot.Blueprints.Core](Hrot/Blueprints/Hrot.Blueprints.Core.md) | Blueprint runtime and compiler core |
| [Hrot.Blueprints.Compiler](Hrot/Blueprints/Hrot.Blueprints.Compiler.md) | Blueprint AOT compiler |
| [Hrot.Blueprints.Editor](Hrot/Blueprints/Hrot.Blueprints.Editor.md) | Blueprint visual editor |
| [Hrot.Blueprints.Generators](Hrot/Blueprints/Hrot.Blueprints.Generators.md) | Blueprint Roslyn source generator |

### 12.8 HROT AI Editor Documents

| Document | Project |
|----------|---------|
| [Hrot.BTree.Editor](Hrot/AI/Hrot.BTree.Editor.md) | Visual BTree editor |
| [Hrot.Hsm.Editor](Hrot/AI/Hrot.Hsm.Editor.md) | Visual HSM editor |
| [Hrot.Editor.AiShared](Hrot/Editor/Hrot.Editor.AiShared.md) | Shared AI editor infrastructure |

---

*This document was generated on 2026-05-23 from the individual project documentation files
listed in Section 12. To update it, re-read the source docs and regenerate this file.*

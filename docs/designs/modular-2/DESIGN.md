# Modular-2: BDC Network Plugin and Assembly Consolidation

## Overview

This workstream introduces a second network protocol (BDC) as a swappable alternative to
the existing NED protocol, while simultaneously consolidating the fragmented assembly graph
into a clean Hexagonal (Ports-and-Adapters) architecture. The two goals are inseparable:
achieving true protocol swapability requires the same strict dependency-inversion that
also justifies the assembly consolidation.

The system must remain runnable with either NED or BDC (never both in parallel). The choice
is made at startup via configuration passed to the composition root.

---

## Guiding Principles

1. **Dependency Inversion:** All arrows point inward toward the domain. Infrastructure
   adapters (network, presentation) depend on the domain; never the reverse.
2. **Physical boundaries match deployment reality:** Assemblies are co-located only when
   they are always deployed together and share the same external dependency set.
3. **Pragmatic DDS coupling:** `CycloneDDS.Runtime` is accepted as a universally available
   base technology across the platform. References to `DdsParticipant` and `DdsWriter<T>`
   in domain code are acceptable, but `DdsParticipant` must only be *instantiated* by the
   Composition Root or via `INetworkFactory`. Subsystems must never call `new DdsParticipant()`.
4. **Protocol ignorance via INetworkFactory:** All simulation-data writes/reads (NED or BDC
   schema types) are routed through `INetworkFactory` interfaces. Domain assemblies
   (`Hrot.Core`, `Hrot.SimHost`, etc.) have zero references to `Hrot.Network.NED` or
   `Hrot.Network.BDC`.
5. **Separate orchestration from simulation data:** Cluster management DDS schemas
   (`NodeOpCommand`, `NodeHeartbeat`, etc.) are application-level concerns that must not
   pollute the reusable FDP engine layer. They live in `Hrot.Network.Orchestration`.
6. **YAGNI:** Dead code (`WaitingRoomCoordinator`, `SubsystemStatusAnnounce`) is deleted,
   not kept "just in case."
7. **Open/Closed for subsystems:** Adding a new subsystem must not require editing
   `Program.cs` or any existing file in `Hrot.ClusterRunner`.

---

## Final Architecture — Assembly Map

### FDP Layer (4 assemblies)

```
Fdp.Core
  Absorbs: Fdp.Kernel, FDP.Interfaces, ModuleHost.Core
  External: MessagePack, K4os.Compression.LZ4, NLog
  Rule: Zero project references to anything above this layer.

Fdp.Engine
  Absorbs: all FDP.Toolkit.* (including Combat.Contracts, Navigation.Contracts),
           FDP.Framework.Runner (ISubsystem, SubsystemOrchestrator, RunnerOptions,
           SubsystemConfig, IWindowRegistrar, IMapCameraProvider)
  External: FastBTree (Fbt.Kernel), FastHSM (Fhsm.Kernel)
  Rule: No Raylib, no ImGui, no CycloneDDS.
  Namespace discipline: Fdp.Engine.Runner namespace must have zero <using> directives
  into Fdp.Engine.Physics / Fdp.Engine.Behavior / etc. The runner loop must remain
  agnostic to the ECS logic it hosts.

Fdp.Presentation
  Absorbs: FDP.Toolkit.Vis2D, FDP.Toolkit.ImGui, FDP.Framework.Raylib
  External: Raylib-cs, rlImGui-cs, ImGui.NET
  Rule: No CycloneDDS.

Fdp.Network.Cyclone
  Absorbs: ModuleHost.Network.Cyclone
  External: FastCycloneDds (CycloneDDS.Runtime, CycloneDDS.Schema, CycloneDDS.Core), NLog
  Rule: No Raylib.
```

**Deleted:** `FDP.Toolkit.Combat.Contracts`, `FDP.Toolkit.Navigation.Contracts`
(their types move into `Fdp.Engine` where cycles no longer exist).
**Deleted:** `FDP.Framework.Raylib.Tests`, `FDP.Framework.Runner.Tests` (superseded;
runner now lives in Fdp.Engine).

### Hrot Layer

```
Hrot.Core   [new name, replaces current Hrot.Common + Hrot.Map.Common + Hrot.Map.Definitions]
  Rule: No Raylib. May reference CycloneDDS.Runtime as a pragmatic base technology
  (to pass DdsParticipant received from outside). Must NOT reference Hrot.Network.NED,
  Hrot.Network.BDC, or Hrot.Network.Orchestration. Defines INetworkFactory and neutral DTOs.
  DdsParticipant is never *constructed* inside Hrot.Core.

Hrot.Presentation   [new name, replaces Hrot.UI.Common + Hrot.ScenarioEditor]
  Depends on: Fdp.Presentation, Hrot.Core
  Rule: No CycloneDDS.

Hrot.Network.Orchestration   [new project]
  Absorbs: Hrot.NED/Orchestration/ (NodeOpCommand, NodeOpStatus, NodeHeartbeat,
           NodeHeartbeatAck, ClusterOpRequest, NodeOpSlaveTranslator, etc.)
  Depends on: Hrot.Core, Fdp.Network.Cyclone
  Rule: Protocol-agnostic cluster management DDS schemas shared across NED and BDC.
  Contains NO simulation-data schemas (no entity replication, no mission control).

Hrot.Network.NED   [new name, replaces Hrot.NED (simulation schemas only) + Hrot.Network]
  Absorbs: Hrot.NED/Messages/, Hrot.NED/Descriptors/ (simulation data only),
           Hrot.Network (NedReplicationModule, translators, routing)
  Depends on: Hrot.Core, Hrot.Network.Orchestration, Fdp.Engine, Fdp.Network.Cyclone
  Rule: Only place that references Hrot NED simulation data DDS schemas.
  Implements INetworkFactory as NedNetworkFactory.

Hrot.Network.BDC   [new project]
  Depends on: Hrot.Core, Hrot.Network.Orchestration, Fdp.Engine, Fdp.Network.Cyclone
  Rule: Only place that references Hrot BDC simulation data DDS schemas.
  Implements INetworkFactory as BdcNetworkFactory.

Hrot.SimHost        [retained as sovereign plugin assembly]
Hrot.CGF            [retained as sovereign plugin assembly]
Hrot.IG             [retained as sovereign plugin assembly]
Hrot.ExCon          [retained as sovereign plugin assembly]
Hrot.Orchestrator   [retained as sovereign plugin assembly]
Hrot.Editor         [retained as offline composition root]
Hrot.ClusterRunner  [retained as thin exe composition root]
```

---

## Dependency Graph

```
flowchart TD
    subgraph Root [Composition Root]
        HrotRunner["Hrot.ClusterRunner (.exe)"]
    end

    subgraph Plugins [Sovereign Subsystem Plugins]
        HrotSimHost["Hrot.SimHost"]
        HrotCgf["Hrot.CGF"]
        HrotIg["Hrot.IG"]
        HrotExCon["Hrot.ExCon"]
        HrotOrch["Hrot.Orchestrator"]
        HrotEditor["Hrot.Editor (offline composition root)"]
    end

    subgraph NetworkAdapters [Network Adapters]
        HrotOrch2["Hrot.Network.Orchestration"]
        HrotNED["Hrot.Network.NED"]
        HrotBDC["Hrot.Network.BDC"]
    end

    subgraph EngineAdapters [Engine Adapters]
        FdpCyclone["Fdp.Network.Cyclone"]
        FdpPresentation["Fdp.Presentation"]
    end

    subgraph AppDomain [Application Domain]
        HrotPresentation["Hrot.Presentation"]
        HrotCore["Hrot.Core (INetworkFactory, DTOs)"]
    end

    subgraph EngineLayer [Engine Layer]
        FdpEngine["Fdp.Engine (Toolkits + Runner loop)"]
        FdpCore["Fdp.Core (ECS Kernel)"]
    end

    HrotRunner --> Plugins
    HrotRunner --> NetworkAdapters
    HrotRunner --> FdpPresentation

    HrotSimHost --> HrotCore
    HrotSimHost --> HrotPresentation
    HrotCgf --> HrotCore
    HrotIg --> HrotCore
    HrotIg --> HrotPresentation
    HrotExCon --> HrotCore
    HrotExCon --> HrotPresentation
    HrotOrch --> HrotCore
    HrotEditor --> HrotCore
    HrotEditor --> HrotPresentation

    HrotOrch2 --> HrotCore
    HrotOrch2 --> FdpCyclone
    HrotNED --> HrotCore
    HrotNED --> HrotOrch2
    HrotNED --> FdpCyclone
    HrotBDC --> HrotCore
    HrotBDC --> HrotOrch2
    HrotBDC --> FdpCyclone

    HrotPresentation --> FdpPresentation
    HrotPresentation --> HrotCore

    HrotCore --> FdpEngine
    FdpCyclone --> FdpEngine
    FdpPresentation --> FdpEngine
    FdpEngine --> FdpCore
```

---

## Phase 1: FDP Layer Consolidation

Goal: Collapse the 20+ fragmented FDP assemblies into 4. This is a prerequisite for Phase 2
because the Hrot layer consolidation produces import conflicts if both the old FDP
fragments and the new Hrot.Core exist simultaneously.

### 1.1 Create Fdp.Core

Merge `Fdp.Kernel`, `FDP.Interfaces`, and `ModuleHost.Core` into a single `Fdp.Core`
project. All source files move; all existing namespaces are preserved verbatim (`Fdp.Kernel`,
`Fdp.Interfaces`, `ModuleHost.Core`). Only the csproj consolidates.

### 1.2 Create Fdp.Engine

Merge all `FDP.Toolkit.*` assemblies and `FDP.Framework.Runner` into `Fdp.Engine`.
This eliminates `FDP.Toolkit.Combat.Contracts` and `FDP.Toolkit.Navigation.Contracts`:
the types they contained (`HitEvent`, `NavigationIntent`, `NavigationStatus`) move into
`Fdp.Engine` alongside the code that previously had to reference them through cycle-breaking
indirection.

The runner hosting types (`ISubsystem`, `SubsystemOrchestrator`, `SubsystemConfig`,
`RunnerOptions`, `IWindowRegistrar`, `IMapCameraProvider`) move into `Fdp.Engine` under the
`Fdp.Engine.Runner` namespace. The `SubsystemOrchestrator` is stripped of all Raylib/ImGui
calls — window initialization and the Raylib main loop move to `Hrot.ClusterRunner/Program.cs`.

### 1.3 Delete WaitingRoomCoordinator

Delete `WaitingRoomCoordinator.cs`, `SubsystemStatusAnnounce.cs`, `SubsystemPeerInfo.cs`.
Remove the `--wait-for` and `--no-wait` CLI options from `RunnerConfiguration.cs`. Remove
the waiting-room code block from `Hrot.ClusterRunner/Program.cs`.

### 1.4 Create Fdp.Presentation

Merge `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`, and `FDP.Framework.Raylib` into
`Fdp.Presentation`. All existing namespaces are preserved.

### 1.5 Create Fdp.Network.Cyclone

Rename/move `ModuleHost.Network.Cyclone` into `Fdp.Network.Cyclone`. Project reference
updates only; source files and namespaces unchanged.

**Not included here:** HROT cluster orchestration schemas (`NodeOpCommand`, `NodeHeartbeat`,
etc.) are application-level types and must NOT be placed in the FDP engine layer. They move
to `Hrot.Network.Orchestration` in Phase 3 (TASK-P3-001).

---

## Phase 2: Hrot Layer Consolidation

Goal: Create `Hrot.Core` (the clean domain layer) and `Hrot.Presentation` (the application
visual adapter) by merging the current fragments. Critically, `Hrot.Core` must be completely
free of NED and CycloneDDS references.

### 2.1 Create Hrot.Core

Merge `Hrot.Common`, `Hrot.Map.Common`, and `Hrot.Map.Definitions` into `Hrot.Core`.
All existing namespaces are preserved.

**Key dependency changes:**
- Remove the `<ProjectReference>` to `Hrot.NED` from `Hrot.Common.csproj`. Any source file
  that references NED simulation data schema types must be moved to `Hrot.Network.NED`
  (TASK-P3-002).
- `CycloneDDS.Runtime` is a pragmatic base dependency and may remain in `Hrot.Core.csproj`.
  However, `HrotNodeBuilder.Build()` must stop instantiating `DdsParticipant` internally.
  The participant must be supplied from outside (by the Composition Root or test harness)
  and passed in via `HrotNodeConfig` or a factory method.
- `HrotNodeContext.NedReplication` is renamed to `HrotNodeContext.Replication` with type
  `IReplicationModule?` (defined in Phase 3).

`INetworkFactory` (Phase 3) will be defined in `Hrot.Core`.

### 2.2 Create Hrot.Presentation

Merge `Hrot.UI.Common` and `Hrot.ScenarioEditor` into `Hrot.Presentation`.
All existing namespaces are preserved.

---

## Phase 3: INetworkFactory Plugin Contract

Goal: Define the neutral plugin contract in `Hrot.Core` that both the NED and BDC network
plugins implement. This is the boundary that makes protocol-swapping possible.

### 3.1 Define INetworkFactory and neutral DTOs

Define `INetworkFactory` in `Hrot.Core`. The factory creates all network-specific
infrastructure from neutral domain types. Representative shape (exact members determined
during implementation based on what each subsystem needs):

```csharp
public interface INetworkFactory
{
    // Creates the replication module for SimHost / IG / CGF.
    IReplicationModule CreateReplicationModule(
        DdsParticipant participant, NodeRole role, NetworkEntityMap entityMap, ...);

    // Creates the ExCon command gateway (replaces INedCommandGateway from Hrot.Map.Common).
    ICommandGateway CreateCommandGateway(DdsParticipant participant, long localNodeId);

    // Creates ingress handlers for ExCon (e.g. ACK readers).
    // IIngressHandler is already neutral — defined in FDP.Toolkit.DER (-> Fdp.Engine).
    IEnumerable<IIngressHandler> CreateExConIngressHandlers(
        DdsParticipant participant, long localNodeId);

    // Creates the aggregate of ExCon egress writers (CreateEntity, MapConfig,
    // DeleteEntity, MapCommand, etc.) — replaces individual IDdsWriter<T> fields
    // currently injected into ExConLogic.
    IExConEgressWriters CreateExConEgressWriters(DdsParticipant participant);
}
```

Define in `Hrot.Core`:

- `IReplicationModule` — replaces `INedReplicationModule` (currently in
  `Hrot.Common/Abstractions/INedReplicationModule.cs`).
- `ICommandGateway` — replaces `INedCommandGateway` (currently in
  `Hrot.Map.Common/Commands/NedCommandGateway.cs`, which references NED schema types).
- `IExConEgressWriters` — neutral aggregate of all ExCon-side egress writers
  (currently `ExConLogic` holds `IDdsWriter<CreateEntityRequest>`,
  `IDdsWriter<MapInteractionConfig>`, `IDdsWriter<DeleteEntityRequest>`, etc.).

Note: `IIngressHandler` is **already neutral** — it is defined in `FDP.Toolkit.DER`
(which becomes part of `Fdp.Engine`). No migration needed for this interface.

### 3.2 Refactor Hrot.Network to Hrot.Network.NED

Rename the existing `Hrot.Network` project to `Hrot.Network.NED` and merge `Hrot.NED`
(the generated DDS schemas) into it. This assembly:
- Implements `INetworkFactory` as `NedNetworkFactory`
- Contains `NedReplicationModule` (implementing `IReplicationModule`)
- Contains all NED command/ingress translators extracted from `ExConSubsystem` and
  `ExConLogic` (see Phase 4)
- References `Hrot.Core`, `Fdp.Engine`, `Fdp.Network.Cyclone`

### 3.3 Create Hrot.Network.BDC

Create a new `Hrot.Network.BDC` project. It:
- Contains the generated BDC DDS schemas (mirroring `Hrot.NED` structure)
- Implements `INetworkFactory` as `BdcNetworkFactory`
- Contains `BdcReplicationModule` (implementing `IReplicationModule`)
- Contains BDC-specific translators implementing `IDescriptorTranslator`

BDC translators map BDC wire structures to exactly the same internal ECS components
(`SimTransform`, `NetworkIdentity`, etc.) that the NED translators currently write.
This is the entire anti-corruption layer—the ECS core layer is untouched.

---

## Phase 4: Subsystem Decoupling

Goal: Remove all direct NED/DDS coupling from the subsystem plugin libraries so they
reference only `INetworkFactory` from `Hrot.Core`.

### 4.1 Remove NED references from ExCon

The most invasively NED-coupled subsystem is `Hrot.ExCon`. Currently:
- `ExConLogic.cs` directly imports NED message types (`CreateEntityRequest`,
  `MapInteractionConfig`, `DeleteEntityRequest`, `MapCommandRequest`, etc.)
- `ExConLogic` holds `IDdsWriter<CreateEntityRequest>`, `IDdsWriter<MapInteractionConfig>`,
  `IDdsWriter<DeleteEntityRequest>`, `IDdsWriter<ClusterOpRequest>` directly
- `MissionControlEgressTranslator` holds a `DdsWriter<MissionControlRequest>` directly
- `MissionControlAckIngressTranslator` holds a `DdsReader<MissionControlAck>` directly
- `INedCommandGateway` (in `Hrot.Map.Common/Commands/NedCommandGateway.cs`) is the
  existing gateway abstraction that directly names NED schema types in its contract
- `Hrot.IG` also uses `INedCommandGateway` (in `IgApplication`) for drag-and-drop
  descriptor updates

Refactoring:
1. Replace `INedCommandGateway` with `ICommandGateway` (from `Hrot.Core`). The neutral
   interface covers create-entity, update-descriptor, and mission-control operations using
   domain DTOs instead of NED schema structs.
2. Replace all `IDdsWriter<NedType>` fields in `ExConLogic` with `IExConEgressWriters`
   injected via constructor.
3. Wire the concrete implementations through the subsystem adapter (see TASK-P4-005)
   via `INetworkFactory.CreateCommandGateway` and
   `INetworkFactory.CreateExConEgressWriters`.
4. Replace `MissionControlEgressTranslator`/`MissionControlAckIngressTranslator` with
   methods on `ICommandGateway` and items from `INetworkFactory.CreateExConIngressHandlers`.

### 4.2 Remove NED references from SimHost

`Hrot.SimHost` currently references `Hrot.NED` and `Hrot.Network` for:
- `NedReplicationModule` construction (via `HrotNodeBuilderReplicationExtensions`)
- NED-specific translator setup in `NodeBootstrapper`

Refactoring:
1. `SimHostApp` / `NodeBootstrapper` accept `INetworkFactory` via constructor or
   via `SubsystemConfig`
2. `HrotNodeBuilderReplicationExtensions.WithReplication` becomes
   `WithReplicationModule(INetworkFactory factory, NodeRole role)` — delegates construction
   to the factory rather than directly instantiating `NedReplicationModule`

### 4.3 Remove NED references from IG, CGF, Orchestrator

`Hrot.IG` and `Hrot.CGF` also reference `Hrot.NED` and `Hrot.Network`.
Apply the same `INetworkFactory` injection pattern as Phase 4.2.

`Hrot.Orchestrator` references `Hrot.NED` for orchestration DDS types (`NodeOpCommand`,
etc.). These belong to `Hrot.Network.Orchestration` (created in Phase 3). After
TASK-P3-001 creates that assembly, update `Hrot.Orchestrator` to reference it.
`Hrot.Orchestrator` may additionally accept an `INetworkFactory` for any simulation-data
channel it needs (e.g. replay ingress).

### 4.4 Move ISubsystem adapters into plugin assemblies

Currently, `SimHostSubsystem.cs`, `IgSubsystem.cs`, `ExConSubsystem.cs`, and
`CgfSubsystem.cs` live in `Hrot.ClusterRunner/Services/`.

Move each `*Subsystem.cs` file into its respective domain assembly:
- `SimHostSubsystem.cs` → `Hrot.SimHost`
- `IgSubsystem.cs` → `Hrot.IG`
- `ExConSubsystem.cs` → `Hrot.ExCon`
- `CgfSubsystem.cs` → `Hrot.CGF`
- `OrchestratorSubsystem.cs` → `Hrot.Orchestrator`
- `EditorSubsystem.cs` → `Hrot.Editor`

`CgfDebugVisualizerAdapter.cs` and `ClusterScenarioPanel.cs` stay with the subsystem
adapter that uses them after the move. `PerspectiveUpdateSubsystem.cs` remains in
`Hrot.ClusterRunner/Services/`. `CiSubsystem.cs` moves to
`Hrot.ClusterRunner/Scenarios/` and is treated as a standard plugin — it implements
`ISubsystem` with `Name => "ci"` and is discovered via reflection like all other
subsystems.

---

## Phase 5: Composition Root Redesign

Goal: Transform `Hrot.ClusterRunner` into a pure composition root that dynamically
discovers subsystem plugins via in-memory reflection.

### 5.1 Delete RunMode enum

Delete `Hrot.ClusterRunner/Configuration/RunMode.cs`.

### 5.2 Replace mode parsing with string-based discovery

Update `HrotRunnerConfiguration.cs`:
- Remove `ParsedMode` (type `RunMode`)
- Remove `ParseModeString` method
- Add `RequestedSubsystems` (`HashSet<string>`, case-insensitive)
- `Validate()` splits `ModeString` on commas and populates `RequestedSubsystems`

### 5.3 Implement in-memory reflection scan in Program.cs

Replace the hardcoded `if (config.ParsedMode.HasFlag(...))` blocks with an
`AppDomain.CurrentDomain.GetAssemblies()` scan. See TASK-DETAIL.md for exact algorithm.

Key points:
- `LoadReferencedAssemblies()` forces eager load of all statically-referenced DLLs.
- Factory instantiation: prefer `INetworkFactory` constructor, fall back to
  parameterless constructor.
- `PerspectiveUpdateSubsystem` is excluded from the scan and always prepended manually.
- All other subsystems, including `CiSubsystem`, are discovered via the reflection
  scanner. No dedicated branch for `--mode ci` exists in `Program.cs`.

### 5.4 Move window management to Program.cs

Extract Raylib window init/close from `SubsystemOrchestrator` into `Program.cs`:
```csharp
if (!config.Headless)
{
    Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
    Raylib.InitWindow(...);
    rlImGui.Setup(true);
}
// ... run orchestrator ...
if (!config.Headless)
{
    rlImGui.Shutdown();
    Raylib.CloseWindow();
}
```

`SubsystemOrchestrator` becomes a pure `while (_running)` iteration loop with no Raylib
imports.

### 5.5 Offline Composition Root for Hrot.Editor

`Hrot.Editor` acts as a parallel offline composition root with no DDS participant.
It implements or references an `OfflineNetworkFactory` that returns no-op stubs for
all `INetworkFactory` methods. This removes `#if DEBUG` / `_networkEnabled` flag patterns
from domain assembly code.

---

## Architecture Rules (Definition of Done)

A build is considered architecturally complete when ALL of the following hold:

**Rule 1 — 4-Assembly FDP Consolidation**
The FDP engine layer is collapsed into exactly: `Fdp.Core`, `Fdp.Engine`, `Fdp.Presentation`,
`Fdp.Network.Cyclone`. All other FDP projects are deleted. `Fdp.Core` and `Fdp.Engine`
have zero PackageReferences to `Raylib-cs`, `rlImGui-cs`, `ImGui.NET`, `CycloneDDS.Runtime`,
`CycloneDDS.Schema`, or `CycloneDDS.Core`.

**Rule 2 — Application Network Layer Separation**
The HROT network layer is divided into exactly three non-overlapping assemblies:
- `Hrot.Network.Orchestration` — cluster management DDS schemas only (`NodeOpCommand`,
  `NodeHeartbeat`, `ClusterOpRequest`, etc.). No simulation data schemas.
- `Hrot.Network.NED` — NED simulation data schemas and `NedNetworkFactory`. No orchestration
  schemas.
- `Hrot.Network.BDC` — BDC simulation data schemas and `BdcNetworkFactory`. No orchestration
  schemas.

**Rule 3 — Pragmatic DDS Coupling**
`CycloneDDS.Runtime` may be referenced by domain assemblies as a universal base technology.
However:
- `DdsParticipant` is only *instantiated* by the Composition Root (`Program.cs` or test
  harnesses) or by the concrete `INetworkFactory` implementations.
- No subsystem (`Hrot.SimHost`, `Hrot.CGF`, `Hrot.IG`, `Hrot.ExCon`, `Hrot.Orchestrator`)
  calls `new DdsParticipant()` or `HrotEnvironment.CreateParticipant()` internally.
- `Hrot.Core`, `Hrot.SimHost`, `Hrot.CGF`, `Hrot.IG`, `Hrot.ExCon` have zero project
  references to `Hrot.Network.NED` or `Hrot.Network.BDC`. All simulation-data writes and
  reads route through `INetworkFactory` interfaces.

**Rule 4 — JIT Air-Gap for Headless**
Every `ISubsystem.Initialize` implementation checks `config.Headless` before allocating
any `MapCanvas`, ImGui panel, or calling any Raylib function. Unit tests verify this by
asserting that `Initialize(headless: true)` completes without throwing
`DllNotFoundException` (i.e. without loading native graphics binaries).

**Rule 5 — Dynamic Composition Root**
`RunMode.cs` does not exist. `Hrot.ClusterRunner/Program.cs` contains no `if` or `switch`
referencing concrete subsystem names except for `PerspectiveUpdateSubsystem`. All other
subsystems, including `CiSubsystem`, are discovered via `AppDomain` reflection.
`Program.cs` is the sole place where `DdsParticipant` is constructed (for the live
network path). The participant is passed into `NedNetworkFactory(participant)` or
`BdcNetworkFactory(participant)` — never directly into subsystems.

**Rule 6 — Namespace Discipline Inside Fdp.Engine**
The `Fdp.Engine.Runner` namespace (`ISubsystem`, `SubsystemOrchestrator`, etc.) has zero
`using` directives into `Fdp.Engine.Physics`, `Fdp.Engine.Behavior`, or any other
simulation-domain namespace inside `Fdp.Engine`. The runner loop must remain agnostic
to the ECS logic it hosts.

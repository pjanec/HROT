# Onboarding: Network Architecture Cleanup and Module Phase Manual

Welcome to the `module-phase-manual` workstream. This document gives you the context you need to start contributing immediately.

---

## What Are We Building / Refactoring?

This workstream contains **five coordinated refactoring efforts** across the FDP engine and the HROT simulation layers. None of them add new simulation features; all of them improve maintainability, remove dead code, and harden architectural contracts.

| Area | Short Description |
|------|------------------|
| **Phase 1: Dead Code Purge** | Delete legacy perception systems, the obsolete `INetworkReplayTarget` replay infrastructure, ACL-violating auto-translator helpers, and the `Fdp.Examples.NetworkDemo` project. |
| **Phase 2: Ordinal Cleanup** | Replace all magic integer literals in `DescriptorOrdinal` properties with named enumerations (`EDescriptorType`, `TimeDescriptorType`, `BdcDescriptorType`). |
| **Phase 3: Network Interface Segregation** | Introduce `INetworkTranslator` as a base interface; give transient event translators (`CycloneNativeEventTranslator` etc.) their own `INetworkEventTranslator` contract instead of forcing them to implement `IDescriptorTranslator`. Remove the `GetDirectionLabel` string-matching hack in `ArchitectureDiagnosticsPanel`. |
| **Phase 4: SystemPhase.Manual** | Add `SystemPhase.Manual = 255` to the FDP ModuleHost framework; add `ISystemRegistry.RegisterManualSystem<T>()` with profiling wrapper support; refactor `AutonomousPerceptionModule` so its four inner systems are registered with and visible to the kernel's diagnostic UI. |
| **Phase 5: Behavior Auto-Registration** | Replace all hardcoded behavior-ID magic strings with a `[BehaviorContract]` attribute on parameter DTOs in `Hrot.Core`. Rebuild `BehaviorCatalog`, `BehaviorUiSetup`, and `CgfBehaviorSetup` via reflection-based auto-discovery. |

---

## Design and Task Documents

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Architecture rationale, detailed design for all five phases, dependency graph, verified codebase facts |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | One section per task: scope, exact file paths, success conditions / unit tests |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Checkbox progress list; update this as tasks are completed |

**Read DESIGN.md first**, then use TASK-DETAIL.md to understand exactly what each task requires.

---

## Folder Layout of Relevant Components

```
FDP/
  Engine/
    Fdp.Core/Abstractions/          IDescriptorTranslator.cs, INetworkTranslator.cs (new)
    Fdp.ModuleHost/Abstractions/    SystemPhase.cs, ISystemRegistry.cs, IEcsModule.cs
    Fdp.ModuleHost/Scheduling/      SystemScheduler.cs, SystemProfileData.cs
    Fdp.Presentation/ImGui/Panels/  ArchitectureDiagnosticsPanel.cs
  Network/
    Fdp.Network.Cyclone/
      Abstractions/                 INetworkReplayTarget.cs (to delete)
      Translators/                  CycloneTranslator.cs, CycloneNativeEventTranslator.cs,
                                    CycloneManagedEventTranslator.cs, MultiInstanceCycloneTranslator.cs,
                                    AutoCycloneTranslator.cs (to delete), ManagedAutoCycloneTranslator.cs (to delete)
      ReplicationBootstrap.cs       (to delete)
  Toolkits/
    Fdp.Toolkits/Time/Translators/  MasterTimeSyncTranslator.cs, SlaveTimeSyncTranslator.cs,
                                    MasterLockstepTranslator.cs, SlaveLockstepTranslator.cs,
                                    SwitchTimeModeDescriptorTranslator.cs
    Fdp.Toolkits/Behavior/          ScenarioBehaviorRemapper.cs
    Fdp.Toolkits/Perception/
      Modules/                      AutonomousPerceptionModule.cs     <- Phase 4 main target
      Systems/                      LocalGridBuilderSystem.cs, VisionBroadphaseSystem.cs,
                                    LosRequestBatchingSystem.cs, SensorTrackDebounceSystem.cs
  Examples/
    Fdp.Examples.NetworkDemo/       (to delete entirely)
    Fdp.Examples.NetworkDemo.Tests/ (to delete entirely)

Hrot/
  Engine/
    Hrot.Core/MapDefinitions/Tkb/   BehaviorCatalog.cs       <- Phase 5 target
    Hrot.Core/MapDefinitions/...    FireAtTargetParamsJsonDto.cs, etc.
    Hrot.Presentation/Behavior/     BehaviorUiSetup.cs       <- Phase 5 target
  Network/
    Hrot.Network.NED/
      AllDescriptors.cs             EDescriptorType enum     <- Phase 2 target
      Replication/Map/Ingress/      EntityMissionIngressTranslator.cs (magic ordinal 50)
                                    EntityMasterIngressTranslator.cs (magic ordinal -2)
                                    MapEntitySymbolIngressTranslator.cs (magic ordinal 40)
    Hrot.Network.BDC/               BdcEntityMasterTranslator.cs, BdcWorldPosTranslator.cs
  Subsystems/
    Hrot.SimHost/
      Modules/                      CombatModule.cs          <- Phase 1 cleanup
      Systems/                      PerceptionBroadphaseSystem.cs (to delete)
                                    ThreatEvaluationAdapterSystem.cs (to delete)
    Hrot.CGF/
      Configuration/                CgfBehaviorSetup.cs      <- Phase 5 target
      Brains/                       CgfNodes.cs              <- Phase 5 target
```

---

## How to Build the Solution

The workspace root contains two solution files:

```bat
# Build everything
dotnet build IOS-IG-SimHost.sln

# Build FDP engine and toolkits only
dotnet build FDP/FDP.sln

# Run all tests
dotnet test IOS-IG-SimHost.sln
```

There is also a convenience batch script at the workspace root:
```bat
build_all_standalone.bat
```

Before finishing any task, confirm `dotnet build IOS-IG-SimHost.sln` produces **zero errors**.

---

## Developer Guide

Before writing code, read the developer guide at:

**[`.dev/.guides/DEV-GUIDE.md`](../.guides/DEV-GUIDE.md)**

It defines the coding standards, commit discipline, batch workflow, and review expectations that apply to all work in this repository.

---

## Key Architecture Concepts (Quick Reference)

- **IEcsModule / ModuleHostKernel**: All simulation logic lives in modules. The kernel schedules their systems. Modules can either register systems via `RegisterSystems` (kernel-driven) or run logic in `Tick` (direct execution). After Phase 4, manual-execution systems still register via `RegisterManualSystem` so the diagnostic UI sees them.

- **IDescriptorTranslator**: A network translator that owns a persistent entity state descriptor (e.g., WorldPos, EntityMission). Has `DescriptorOrdinal`, `ApplyToEntity`, `Dispose`. After Phase 3 it extends `INetworkTranslator`.

- **INetworkEventTranslator** (new in Phase 3): A network translator for transient events (e.g., `FireInteractionEvent`). Has no `DescriptorOrdinal` and does not implement the entity-state methods.

- **EDescriptorType**: Enum that maps DDS topic ordinals for the NED network protocol. Only NED-owned descriptors live here. Other domains (`Fdp.Toolkit.Time`, `Hrot.BDC`) have their own enums.

- **BehaviorContractAttribute** (new in Phase 5): Applied to parameter DTOs in `Hrot.Core`. Contains the behavior's integer ID, behavior-ID string, and which entity categories may use it. The DTO becomes the Single Source of Truth.

- **AutonomousPerceptionModule**: Runs a private four-system LOS pipeline on a scoped event bus. Requires multiple intra-frame bus swaps (hence `SystemPhase.Manual`). After Phase 4, its systems are visible in `ArchitectureDiagnosticsPanel` under the Manual phase.

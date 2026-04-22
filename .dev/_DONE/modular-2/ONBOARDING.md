# Onboarding — Modular-2: BDC Network Plugin and Assembly Consolidation

## Project Overview

This workstream restructures the `IOS-IG-SimHost-FDP` solution to support a second
network protocol (BDC) as a swappable alternative to the existing NED protocol.
The work requires consolidating the highly fragmented assembly graph into a clean
Hexagonal (Ports-and-Adapters) architecture where the simulation domain is completely
decoupled from specific DDS wire formats.

The outcome is a system where `--network ned` and `--network bdc` can be selected at
startup without changing a single line of domain logic, and where headless integration
tests can run without loading CycloneDDS or Raylib binaries.

## Planning Artifacts

| Document | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Architecture overview, assembly map, dependency graph, phases, Definition of Done |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist with task links |

## Folder Layout

### Solution root: `d:\Work\IOS-IG-SimHost-FDP\`

**FDP layer (being consolidated):**
```
FDP/Kernel/Fdp.Kernel/              -> merges into Fdp.Core
FDP/Common/FDP.Interfaces/          -> merges into Fdp.Core
FDP/ModuleHost/ModuleHost.Core/     -> merges into Fdp.Core
FDP/Toolkits/FDP.Toolkit.*/        -> merge into Fdp.Engine
FDP/Framework/FDP.Framework.Runner/ -> merges into Fdp.Engine (runner loop)
FDP/Framework/FDP.Framework.Raylib/ -> merges into Fdp.Presentation
FDP/Toolkits/FDP.Toolkit.Vis2D/    -> merges into Fdp.Presentation
FDP/Toolkits/FDP.Toolkit.ImGui/    -> merges into Fdp.Presentation
FDP/ModuleHost/ModuleHost.Network.Cyclone/ -> becomes Fdp.Network.Cyclone
```

**Hrot layer (being consolidated / refactored):**
```
Hrot.Common/           -> merges into Hrot.Core
Hrot.Map.Common/       -> merges into Hrot.Core
Hrot.Map.Definitions/  -> merges into Hrot.Core
Hrot.UI.Common/        -> merges into Hrot.Presentation
Hrot.ScenarioEditor/   -> merges into Hrot.Presentation
Hrot.NED/              -> merges into Hrot.Network.NED
Hrot.Network/          -> merges into Hrot.Network.NED
```

**Plugin assemblies (retained, but subsystem adapters move INTO them):**
```
Hrot.SimHost/          SimHostSubsystem.cs moves here from Hrot.ClusterRunner/Services/
Hrot.IG/               IgSubsystem.cs moves here
Hrot.ExCon/            ExConSubsystem.cs moves here (also loses NED references)
Hrot.CGF/              CgfSubsystem.cs moves here
Hrot.Orchestrator/     OrchestratorSubsystem.cs moves here
Hrot.Editor/           EditorSubsystem.cs moves here; OfflineNetworkFactory added
```

**Composition roots (retain):**
```
Hrot.ClusterRunner/    exe — dynamic plugin loader; RunMode.cs deleted
```

**New assemblies:**
```
Hrot.Network.NED/      NedNetworkFactory + NedReplicationModule + NED translators
Hrot.Network.BDC/      BdcNetworkFactory + BdcReplicationModule + BDC translators
```

## Build and Test

Build the entire solution:
```
cd d:\Work\IOS-IG-SimHost-FDP
dotnet build IOS-IG-SimHost.sln
```

Build only the FDP layer:
```
cd d:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
```

Run all tests:
```
dotnet test IOS-IG-SimHost.sln
```

Run specific project tests:
```
dotnet test Hrot.SimHost.Tests
dotnet test Hrot.ExCon.Tests
dotnet test Hrot.ClusterRunner.Integration.Tests
```

Run in headless mode (verifies JIT air-gap):
```
cd d:\Work\IOS-IG-SimHost-FDP
dotnet run --project Hrot.ClusterRunner -- --mode simhost --headless
dotnet run --project Hrot.ClusterRunner -- --mode simhost --headless --network bdc
```

## Workflow

All implementation work follows the batch-based development workflow. Read
`FDP/.dev-workstream/guides/DEV-GUIDE.md` before starting implementation.

Key workflow rules:
1. Each task in `TASK-TRACKER.md` is a unit of batch work.
2. Complete all success conditions defined in `TASK-DETAIL.md` before marking a task done.
3. Architecture rules in `DESIGN.md` (Definition of Done section) apply to the entire
   workstream; any violation means the task is not complete.
4. Phases must be done in order: Phase 1 before Phase 2, Phase 2 before Phase 3, etc.
   Within a phase, tasks can be done in parallel where dependencies allow.
5. The build must be green at the end of every task.

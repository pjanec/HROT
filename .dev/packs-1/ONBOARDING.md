# Onboarding — Logic Packs & Translator Packs Refactoring (`packs-1`)

Welcome to the `packs-1` workstream. This document gets you up to speed quickly.

---

## What Are We Building?

We are refactoring the simulation engine to enforce strict **CQRS** boundaries between the Brain
tier (CGF) and the Muscle tier (SimHost), and to decouple all network transport from core domain
logic by introducing **Logic Packs** and **Translator Packs**.

**In short:**

- A node (runner subsystem) is assembled by *choosing which packs to install*. An "All-In-One"
  editor runs Brain + Muscle logic packs without any Translator Pack; they communicate via the
  internal shared ECS and `FdpEventBus`.
- Several systems currently cross the Brain/Muscle ECS boundary, register in wrong modules,
  or embed DDS I/O and JSON parsing directly inside core domain logic.
- This workstream fixes all identified CQRS violations so the network layer becomes a true
  pluggable module — swappable, testable offline, and ready for future protocol changes (e.g.
  adding Bagira BDC SST by replacing translator packs).

The five phases are:

1. **Phase 1** — Fix `RouteContextSystem` cross-domain query via `NavigationStatus.ProgressS`
2. **Phase 2** — Realign `HsmDamageBridgeSystem` and `ApcMobility*` to the Brain tier
3. **Phase 3** — Enforce `NavigationIntent` as the single movement command channel; retire
   legacy `Cmd*` events
4. **Phase 4** — Split `MissionControlRequestSystem`, relocate `UpdateEntityDescriptorRequestSystem`,
   strip `NetworkEntityMap` from physics/combat
5. **Phase 5** — Purify `ClusterMaster` and `ClusterUiCache` of DDS fallback paths

---

## Key Documents

| Document | Purpose |
|----------|---------|
| [design_talk.md](./design_talk.md) | Full design conversation — read this to understand the *why* |
| [DESIGN.md](./DESIGN.md) | Formal design — phases, architecture decisions, data contracts |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specifications with success conditions (unit test specs) |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Quick progress checklist |
| [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) | **Read this before starting any work.** Developer workflow, batch system, reporting format |

---

## Solution Structure

Workspace root: `d:\Work\IOS-IG-SimHost-FDP-2`

```
IOS-IG-SimHost.sln                        ← main solution
FDP/FDP.sln                               ← FDP engine sub-solution

── FDP engine (pure domain — NO CycloneDDS) ───────────────────────────────────────────
FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/  ← NavigationStatus, NavigationIntent ECS structs
FDP/Toolkits/FDP.Toolkit.Navigation/            ← NavigationIntentBridgeSystem, NavigationExecutionSystem
FDP/Toolkits/FDP.Toolkit.Behavior/             ← CognitiveRuntimeModule, HsmDamageBridgeSystem
FDP/Toolkits/FDP.Toolkit.Combat/               ← AimAndFireExecutor, CombatModule
FDP/Toolkits/FDP.Toolkit.Physics/              ← HitResolutionSystem
FDP/Toolkits/FDP.Toolkit.CarKinem/             ← VehicleCommandSystem, NavState
FDP/Toolkits/FDP.Toolkit.Orchestration/        ← ClusterSlave, FdpEventBus, ClusterCqrsEvents

── Hrot integration layer (DDS translators live here) ──────────────────────────────────
Hrot.SimHost/                    ← NodeBootstrapper, SimHostApp, CombatModule registration,
                                   RouteContextSystem, PersonalRouteAuthoringSystem,
                                   MissionControlRequestSystem (to be split)
Hrot.SimHost/Modules/            ← SimHostModule, CombatModule, SimulationLogicModule
Hrot.SimHost/Systems/Routing/    ← RouteContextSystem, PersonalRouteAuthoringSystem
Hrot.SimHost/Network/            ← Egress/Ingress translators (NavigationStatus, Detonation, etc.)
Hrot.Map.Common/Systems/         ← UpdateEntityDescriptorRequestSystem (to be relocated)
Hrot.Map.Common/Replication/     ← Target location for above after PACK-P002
Hrot.Orchestrator/               ← ClusterMaster (to be purified in Phase 5)
Hrot.Orchestrator/Events/        ← ClusterCqrsEvents.cs (new events added here)
Hrot.Orchestrator/Translators/   ← ClusterOpMasterTranslator
Hrot.Common/Orchestration/       ← OrchestrationObserverTranslator (new, Phase 5)
Hrot.ClusterRunner/Services/     ← ClusterUiCache (to be purified in Phase 5)
Hrot.NED/                        ← DDS wire structs — HROT NED data model (READ-ONLY for FDP code)
Hrot.ExCon/                      ← ExConSubsystem (construction site update in Phase 5)

── Tests ─────────────────────────────────────────────────────────────────────────────
Hrot.ClusterRunner.Integration.Tests/   ← Integration test regression suite
Hrot.Orchestrator.Integration.Tests/    ← Orchestrator-specific integration tests
Hrot.SimHost.Integration.Tests/         ← SimHost integration tests
Hrot.SimHost.Tests/                     ← SimHost unit tests
Hrot.Orchestrator.Tests/                ← Orchestrator unit tests

── This workstream ─────────────────────────────────────────────────────────────────
.dev/packs-1/                    ← Design docs, task docs, batch instructions (here)
.dev/.guides/DEV-GUIDE.md        ← Developer workflow guide (read first!)
```

---

## Key Files to Understand

| File | Why It Matters |
|------|----------------|
| `Hrot.SimHost/Systems/Routing/RouteContextSystem.cs` | **Phase 1 target** — cross-domain query violation |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs` | `NavigationStatus` ECS struct — add `ProgressS` here |
| `Hrot.NED/SimDescriptors.cs` | DDS wire struct for `NavigationStatus` — add `ProgressS` here |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmDamageBridgeSystem.cs` | **Phase 2 target** — misplaced in CombatModule |
| `Hrot.SimHost/Modules/CombatModule.cs` | Remove `HsmDamageBridgeSystem` from here |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/CognitiveRuntimeModule.cs` | Add `HsmDamageBridgeSystem` here |
| `Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs` | **Phase 3 target** — emits `CmdFollowTrajectory` |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs` | **Phase 3 target** — legacy Cmd* event handler |
| `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` | **Phase 4 target** — DDS+JSON monolith |
| `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` | **Phase 4 target** — misregistered in core |
| `FDP/Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs` | **Phase 4 target** — NetworkEntityMap leak |
| `Hrot.Orchestrator/ClusterMaster.cs` | **Phase 5 target** — DDS constructors to delete |
| `Hrot.ClusterRunner/Services/ClusterUiCache.cs` | **Phase 5 target** — 7 DdsReader fields to remove |
| `FDP/Kernel/Fdp.Kernel/FdpEventBus.cs` | CQRS event bus (lightweight, no ECS kernel needed) |
| `Hrot.SimHost/NodeBootstrapper.cs` | Node assembly reference — how packs are currently composed |

---

## Build and Test

```powershell
# Build everything
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln

# Build FDP sub-solution separately if needed
dotnet build FDP/FDP.sln

# Run unit tests (fast)
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj

# Run integration tests (slower — require no live DDS)
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
```

> **Note on DDS:** The integration tests use an in-process AllInOne runner. You do **not** need
> a running DDS daemon (RTI or CycloneDDS) to run the test suite.

---

## Developer Workflow

Read [DEV-GUIDE.md](../.guides/DEV-GUIDE.md) for the full batch-based development workflow.
In summary:

1. A Development Lead creates **batch instruction files** describing one or more tasks from
   `TASK-TRACKER.md`.
2. The developer implements the tasks described in the batch instruction file, guided by
   `TASK-DETAIL.md` for spec detail and `DESIGN.md` for architectural context.
3. The developer writes a **batch report** summarising what was done and test results.
4. The Lead reviews the report and either approves or requests changes.

**Never** start work without reading the current batch instruction file.  
**Never** commit code that does not pass the success conditions in `TASK-DETAIL.md`.

# Onboarding: DDS-to-ECS Architectural Cleanup

Welcome to the **DDS-to-ECS** workstream. This document orients you on what we are fixing, where
everything lives, and how to get started.

---

## 1. What We Are Fixing

The Hrot IG and SimHost applications were drafted with a critical architectural shortcut: DDS
network descriptor types (`EntityMaster`, `WorldPos`, `EntityInfo`, `EntityDamage`) were stored
**directly** in the ECS instead of being translated into proper internal ECS components. This
violates the core FDP separation principle and causes serialization bugs, unclear data ownership,
and systems querying raw network DTOs as if they were simulation state.

Beyond the DDS-in-ECS anti-patterns, four additional run-time deviations from the `NetworkDemo`
gold standard cause:
- **Zombie entities** — destroyed SimHost entities never send a DDS dispose, so IG ghosts freeze.
- **Invisible combat** — `FireInteractionEvent` is never distributed over DDS, so IG renders no effects.
- **Stuttering movement** — IG writes directly to `SimTransform` instead of using `NetworkPosition`
  + dead reckoning, causing hard-snapping on every network packet.
- **Clock drift** — the `TimePulseTranslator` was commented out, decoupling SimHost and IG clocks.

Additionally, the IOS/SimHost mission control connection is broken (SimHost ignores
`MissionControlRequest`), the IOS mission UI only views/controls missions but cannot edit them,
and there are no automated end-to-end integration tests for any of the three-node flows.

The gold standard for this codebase is `Fdp.Examples.NetworkDemo` (under `FDP/Toolkits/`) which
demonstrates the correct pattern: DDS types live on the wire, ECS components live in the engine,
and Translators bridge strictly between them.

Additionally, a design review against the `UrbanCombat` golden standard (`FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`) revealed that SimHost's mission pipeline has three compounding deviations that prevent vehicles from ever moving when given a mission: a managed-DTO data model, null BTree interpreters, and a hand-rolled mission adapter that bypasses the toolkit's `MissionDirectorSystem`. These are addressed in Phase 16.

**This refactor cleans up 17 phases of violations and gaps across:**
- `Hrot.NED` (Phases 1–2: strip `[ComponentId]` from DDS types)
- `Hrot.SimHost` (Phases 2–3, 9, 12–13, 16–17: DescriptorMapper, EntityMasterEgressTranslator, network cleanup, events, mission control, mission pipeline, combat readiness)
- `Hrot.IG` (Phases 4–8, 10–12: translator fixes, new components, dead reckoning, time sync, combat events)
- `Hrot.ExCon` (Phase 14: mission editor UI)
- `Hrot.Map.Definitions` (Phase 17: TKB template ECS component attachment)
- `Hrot.ClusterRunner.Integration.Tests` (Phase 15: end-to-end xUnit test harness)

---

## 2. Design and Task Documents

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Architectural principles, full violation inventory, target state per component, phase summary table |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | One section per task — exact code change, success conditions, unit test specs |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist; update each item to `[x]` when done |

**Read in this order:** `DESIGN.md` → `TASK-DETAIL.md` → start implementing.

---

## 3. Key Source Locations

### What We Are Refactoring

| Component | Location |
|-----------|----------|
| DDS descriptor types | `Hrot.NED/GenericDescriptors.cs`, `SimDescriptors.cs` |
| SimHost spawn pipeline | `Hrot.SimHost/Util/DescriptorMapper.cs` |
| SimHost application shell | `Hrot.SimHost/SimHostApp.cs` |
| SimHost egress translators | `Hrot.SimHost/Translators/` |
| SimHost systems (to add) | `Hrot.SimHost/Systems/` (MissionControlRequestSystem, MissionDirectorSystem registration) |
| SimHost mission pipeline | `Hrot.SimHost/SimHostApp.cs` doctrine registration, `Hrot.SimHost/Brains/SimHostNodes.cs` (Phase 16) |
| SimHost combat pipeline | `Hrot.SimHost/Modules/SimulationLogicModule.cs` (Phase 17), `Hrot.SimHost/Hrot.SimHost.csproj` (Phase 17) |
| TKB template definitions | `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs`, `BdcTkbCatalog.cs` (Phase 17) |
| UrbanCombat golden standard | `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`, `Setup/DemoTkbSetup.cs` |
| Runner SimHost subsystem | `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` |
| IG application shell | `Hrot.IG/IgApplication.cs` |
| IG translators | `Hrot.IG/Translators/` |
| IG systems (to add) | `Hrot.IG/Systems/` (DeadReckoningSyncSystem) |
| IG internal ECS components | `Hrot.IG/Components/` |
| IOS mission panel | `Hrot.ExCon/Panels/MissionPanel.cs` |
| Integration test project | `Hrot.ClusterRunner.Integration.Tests/` |

### Where to Look for the Gold Standard

| Reference | Location |
|-----------|----------|
| `FastGeodeticTranslator` (the correct translator pattern) | `FDP/Toolkits/FDP.Toolkit.DER.Examples/` |
| FDP internal `EntityMasterTranslator` | `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/` |
| `NetworkIdentity`, `NetworkOwnership`, `NetworkSpawnRequest` | `FDP/Toolkits/FDP.Toolkit.Replication/Components/` |
| `GeoTransform`, `GeoVelocity`, `SimTransform` | `FDP/Toolkits/Fdp.Toolkit.Geographic/Components/` |
| `GlobalComponentIds` (allocate new IDs here) | `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` |
| `IgSymbolOverride` (already-correct ECS component) | `Hrot.IG/Components/IgSymbolOverride.cs` |

### Test Projects

| Project | Tests for |
|---------|-----------|
| `Hrot.SimHost.Tests` | `DescriptorMapper`, SimHost translators |
| `Hrot.IG.Tests` | IG translators, `IgApplication` panels |
| `Hrot.NED.Tests` | DDS model reflection guards |

---

## 4. Building the Project

### First-Time Setup (once per machine)

```powershell
# 1. Build native CycloneDDS libraries (requires CMake + MSVC)
.\FDP\ExtDeps\FastCycloneDds\build\native-win.ps1

# 2. Restore packages
dotnet restore IOS-IG-SimHost.sln

# 3. Build
dotnet build IOS-IG-SimHost.sln
```

### Day-to-Day

```powershell
# Build
dotnet build IOS-IG-SimHost.sln

# Run all tests
dotnet test IOS-IG-SimHost.sln

# Run tests for a specific project
dotnet test .\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj
dotnet test .\Hrot.IG.Tests\Hrot.IG.Tests.csproj
dotnet test .\Hrot.NED.Tests\Hrot.NED.Tests.csproj
```

---

## 5. Developer Workflow

All development on this workstream follows the project's batch-based workflow. Read the
developer guide before writing any code:

> **`.dev-workstream/guides/DEV-GUIDE.md`** — required reading. Covers how to receive a batch
> instruction file, write a batch report, handle review feedback, and meet quality standards.

See also **`.dev-workstream/guides/CODE-STANDARDS.md`** for naming, comment, and test conventions
that all code in this repo must follow.

---

## 6. Implementation Notes

- **Phase ordering matters.** Phases 1–2 introduce compilation errors (Phase 1 breaks
  `AutoCycloneTranslator<EntityMaster>` in SimHost; Phase 2 removes DTO types from the init
  pipeline). Complete phases sequentially and ensure `dotnet build` is green before committing.
- **Allocate new `GlobalComponentIds`** in `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` for
  `IgEntityData` and `IgHealthState`. Coordinate with the lead to avoid ID collisions.
- **`CycloneTranslator<T, T>`** is the base class pattern used by all IG translators. See
  `WorldPosTranslator.cs` for a working example (it is already correct).
- **`NetworkSpawnRequest.DisType`** — verify this field exists before implementing DDS2ECS-S8T3.
  If it does not, consult the lead; the fallback is a `TkbDatabase.LookupDisType(tkbType)` call.
- **Dead reckoning (Phase 10):** `NetworkPosition` is the ingress anchor in Cartesian space.
  `GeoTransform` is strictly the egress staging buffer (SimHost side). Never use `GeoTransform`
  for incoming position smoothing — see DESIGN.md §7 for the full explanation.
- **Time sync (Phase 11):** The `TimePulseTranslator` class already exists and is correct; only
  the registration was commented out. Fix the `[DdsTopic]` attribute first (S11T1), then
  uncomment (S11T2), then add SimHost egress (S11T3).
- **Integration tests (Phase 15):** Use unique DDS domain IDs per test class to prevent
  cross-talk. `HrotRunnerHarness` handles this automatically. Add `[Collection("Sequential")]`
  if running on a machine with strict port limits.
- **Mission pipeline (Phase 16) — golden standard is `UrbanCombat`:** Before touching any
  SimHost mission code, read `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` in full.
  Pay particular attention to `RegisterDoctrines()` (compiling BTree blobs, building
  `ActionRegistry`, registering real `Interpreter<>`) and `RegisterSystems()` (using
  `MissionDirectorSystem`, not a hand-rolled adapter). The three deviations are analysed in
  DESIGN.md §10. S16T1–S16T5 **must be applied together** — applying any subset leaves the
  pipeline broken.
- **Combat readiness (Phase 17) — golden standard is `UrbanCombat`:** `SimHost` currently has
  no perception, no combat systems, and hollow TKB templates. Before touching
  `SimulationLogicModule`, read `HeadlessDemoApp.RegisterSystems()` (Input/Sim/PostSim split)
  and `Setup/DemoTkbSetup.cs` (`t.AddComponent(new PerceptionReceptor{...})` pattern). The five
  deviations are in DESIGN.md §11. Apply S17T1–S17T5 as a unit. S17T2 **must** precede S17T5
  or entity spawns will panic the ECS kernel with unregistered-component errors.
- **Test coverage is mandatory.** Every task in `TASK-DETAIL.md` lists specific unit tests that
  must be green before the task is considered done.

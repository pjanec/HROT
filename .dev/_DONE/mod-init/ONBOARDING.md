# Onboarding — Module Init Workstream

Welcome to the `mod-init` workstream. This document gives you everything you need to get started.

---

## What Is Being Built

This workstream completes **Phase 4** of the `eyes-and-muscle` design — the modular node architecture initiative.

The short version: three application nodes (`SimHostApp`, `IgApplication`, `CgfApplication`) currently contain ~300 lines of manual network translator boilerplate each, and they cannot use the shared `NedReplicationModule` ACL because it lives in the wrong layer of the dependency graph (`Hrot.ClusterRunner`).

This workstream repositions the module to `Hrot.Common`, then migrates all three applications to use it. The end state is a clean, DRY architecture where any application can be wrapped in a standalone executable with a one-method `Main()`.

See [DESIGN.md](./DESIGN.md) for full architecture context, rationale, and constraints.

---

## Planning Artifacts

| Artifact | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Architecture, decisions, stage-by-stage plan |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task specs with scope, constraints, success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## Key Files and Folders

| Path | What It Is |
|---|---|
| `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` | The module being relocated (Stage 2) |
| `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` | System being moved to `Hrot.Common` (Stage 1) |
| `Hrot.SimHost/Network/SharedTranslatorPack.cs` | Translator pack being moved to `Hrot.Common` (Stage 1) |
| `Hrot.SimHost/Network/KinematicTranslatorPack.cs` | Translator pack being moved to `Hrot.Common` (Stage 1) |
| `Hrot.SimHost/Network/CognitiveTranslatorPack.cs` | Translator pack being moved to `Hrot.Common` (Stage 1) |
| `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` | Reference example — already correctly placed |
| `Hrot.Common/Infrastructure/HrotNodeBuilder.cs` | DRY node bootstrap builder |
| `Hrot.Common/Infrastructure/HrotNodeContext.cs` | Immutable output of the builder |
| `Hrot.SimHost/SimHostApp.cs` | App being refactored in Stage 3 |
| `Hrot.IG/IgApplication.cs` | App being refactored in Stage 3 |
| `Hrot.ClusterRunner/Services/CgfSubsystem.cs` | Updated in Stage 4 |

---

## Build and Test

```powershell
# Full solution build
dotnet build IOS-IG-SimHost.sln --no-restore

# Run all integration tests
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build

# Run SimHost-specific tests
dotnet test Hrot.SimHost.Integration.Tests --no-build

# Run IG tests
dotnet test Hrot.IG.Tests --no-build

# Run a specific test class
dotnet test Hrot.ClusterRunner.Integration.Tests --filter "FullyQualifiedName~EyesAndMuscleIntegrationTests" --no-build --logger "console;verbosity=detailed"
```

---

## Workflow

Read [`.dev-workstream/guides/DEV-GUIDE.md`](../../.dev-workstream/guides/DEV-GUIDE.md) to understand the batch-based development workflow used in this project.

The `mod-init` workstream is sequential by stage:

1. Stage 1 tasks can be done in parallel (S101–S104 are independent of each other, S105 is a prerequisite for S104 only).
2. MODINIT-S106 (boundary validation) must be the last Stage 1 task.
3. Stage 2 begins only after Stage 1 is validated.
4. MODINIT-S301 (SimHostApp) must be green before starting MODINIT-S302 (IgApplication).
5. Stage 4 can begin after Stage 3 is complete.

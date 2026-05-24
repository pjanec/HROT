# Onboarding: ClusterMaster God-Class Refactoring

## What Is Being Refactored

`ClusterMaster` is the distributed Two-Phase Commit (2PC) coordinator for the simulation cluster.
Its job is to fan out operations to nodes, count acknowledgements, and publish completion events.
Over time, six categories of domain-specific side-effects have accumulated inside it, turning it
into a "God Class":

- NAS storage pulls (SerializeLocal completion)
- Episode state tracking (ManageEpisode 2PC)
- Live-from-Replay time freezing / restoration
- Replay seek clock snapping
- Orchestrator context file I/O (save/load)
- Asset prefetch staging (NAS staging before node fan-out)

This workstream extracts each of these into dedicated Process Managers and Aggregators, leaving
`ClusterMaster` as a pure, domain-agnostic 2PC engine.

---

## Planning Artifacts

| Artifact | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Architecture overview, phases, constraints, design decisions |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, and success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## Key Existing Components

All relevant code is in `Hrot/Subsystems/Hrot.Orchestrator/`:

| File | Role |
|---|---|
| `ClusterMaster.cs` | The God Class being refactored |
| `INodeResponseAggregator.cs` | The aggregator contract (already exists) |
| `ReplayConsensusAggregator.cs` | Example aggregator (already implemented) |
| `ReplayProcessManager.cs` | Example Process Manager (already implemented) |
| `OrchestratorSubsystem.cs` | Wiring point -- Initialize() and Update() |
| `GlobalContextClusterOpHandler.cs` | Orchestrator context file I/O (to be extracted) |
| `StorageGatewayModule.cs` | NAS file transfer (to be owned by process managers) |
| `ReplayMasterModule.cs` | Time freeze / restore (to be extracted) |
| `Panels/ClusterUiCache.cs` | CQRS read model -- already has its own ActiveEpisodes tracking |

Integration tests:

| Project | Key test classes |
|---|---|
| `Hrot.ClusterRunner.Integration.Tests` | `ClusterOpE2eScriptTests`, `CgfRecordingIntegrationTests`, `DistributedScenarioLoadTests` |
| `Hrot.Orchestrator.Integration.Tests` | `ScenarioSaveLoadTests` |
| `Hrot.SimHost.Integration.Tests` | `EpisodeInjectionTests` |
| `Hrot.Orchestrator.Tests` | `ClusterMasterEpisodeTests`, `ClusterMasterSeekTests`, `ClusterMasterPrefetchTests`, `ClusterMasterContextHandlerTests` |

---

## Build and Test

Build the solution:
```
cd FDP
dotnet build FDP.sln
```

Run orchestrator unit tests:
```
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Tests/
```

Run orchestrator integration tests:
```
dotnet test Hrot/Subsystems/Hrot.Orchestrator.Integration.Tests/
```

Run cluster runner integration tests (require DDS runtime, may need environment setup):
```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/
```

---

## Development Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development workflow
used in this project.

---

## The Pattern to Follow

Every new Process Manager follows the same structure as the existing `ReplayProcessManager`:

```csharp
public sealed class XxxProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly DomainDependency _dep;

    public XxxProcessManager(FdpEventBus bus, DomainDependency dep)
    {
        _bus = bus;
        _dep = dep;
    }

    public void Tick()
    {
        foreach (var ev in _bus.ReadManaged<SomeEvent>())
        {
            // React to bus events and execute domain logic here.
        }
    }
}
```

Wiring in `OrchestratorSubsystem.Initialize`:
```csharp
_xxxProcessManager = new XxxProcessManager(_bus, dependency);
// Register its aggregator if applicable:
_clusterMaster.RegisterAggregator(_xxxProcessManager.CreateAggregator());
```

Ticking in `OrchestratorSubsystem.Update`:
```csharp
// Phase 3 -- after ClusterMaster.Tick() and ReplayProcessManager.Tick()
_xxxProcessManager?.Tick();
```

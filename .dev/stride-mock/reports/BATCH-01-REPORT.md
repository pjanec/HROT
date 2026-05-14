# BATCH-01 Report

**Status:** COMPLETE — all tasks implemented, built, and tested.

---

## SM-001: Project Scaffolding

### New Projects Created

| Project | Type | Path |
|---|---|---|
| `Hrot.StrideMock` | Class library (net8.0) | `Hrot/Subsystems/Hrot.StrideMock/` |
| `Hrot.StrideMock.Tests` | xUnit test project (net8.0) | `Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.Tests/` |
| `Hrot.FakeStrideApp` | Executable (net8.0) | `Hrot/Runner/Hrot.FakeStrideApp/` |

### Modified Projects

- `Hrot.ClusterRunner.csproj` — added `ProjectReference` to `Hrot.StrideMock`.
- `Hrot.Common.csproj` — added `InternalsVisibleTo` for `Hrot.StrideMock.Tests`.
- `IOS-IG-SimHost.sln` — added all three new projects with build configurations and solution folder nesting.

### Build Results

```
Hrot.StrideMock:        Build succeeded  0 errors  5 warnings
Hrot.FakeStrideApp:     Build succeeded  0 errors  0 warnings
Hrot.ClusterRunner:     Build succeeded  0 errors  8 warnings
```

---

## SM-002: SharedApplicationBootstrapper + Tests

### Implementation

**File:** `Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs`

- Abstract base class with sealed `BootstrapNode(HrotNodeConfig, NodeRole, INetworkFactory)` entry point.
- 7-phase pipeline with strict ordering; subclasses cannot reorder or skip phases.
- 6 abstract hooks: `RegisterDomainComponents`, `BuildSerializer`, `PopulateSystems`, `BuildOrchestration`, `RegisterSpawningPipeline`, `RegisterNetworkTranslators`.
- 2 virtual hooks: `GetAdditionalModules()`, `GetBehaviorRegistry()`.
- Public property `ITimeControlGateway? TimeControl` — set by the base class in Phase 6c.

### Tests

**File:** `Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.Tests/SharedApplicationBootstrapperTests.cs`

```
Test run: 10 total, 10 passed, 0 failed
Time:     0.95 s
```

| Test | Scenario | Result |
|---|---|---|
| SC_SM002_1 | `BootstrapNode_WithMinimalSubclass_Headless_DoesNotThrow` | PASS |
| SC_SM002_2 | `RegisterDomainComponents_RunsBeforeBuildSerializer_ComponentPresentInWorld` | PASS |
| SC_SM002_3 | `PopulateSystems_SystemInSimGroup_PassedToBuildOrchestration` | PASS |
| SC_SM002_4 | `BuildOrchestration_ReceivesLifecycleGroup_FromNedReplication` | PASS |
| SC_SM002_5 | `AbstractAndVirtualHooks_ExactlyAsSpecified_Reflection` | PASS |
| SC_SM002_6 | `KernelInitialize_CalledExactlyOnce_AfterAllTranslators` | PASS |
| SC_SM002_7 | `TimeControl_NonNull_AfterBootstrapWithFactory` | PASS |
| SC_SM002_8 | `TimeTranslators_RegisteredByBaseClass_SlaveSyncController_ReceivesEvent` | PASS |
| SC_SM002_9 | `NedReplication_NonNull_AfterBootstrapWithNedFactory` | PASS |
| SC_SM002_10 | `NedReplication_RegisteredByBaseClass_GhostCreationSystemPresent` | PASS |

---

## Developer Notes

### Circular Dependency: Hrot.Common -> Hrot.Network.NED

`Hrot.Common` cannot reference `Hrot.Network.NED` (circular). The `.WithReplication()` builder
extension that lives in `Hrot.Network.NED` is therefore unavailable inside `SharedApplicationBootstrapper`.
The workaround mirrors the pattern in `SimHostApp.cs`: call `configuredFactory.CreateReplicationModule()`
after `Build()` and patch the context with `context with { NedReplication = ..., GhostCreationSystem = ... }`.

### Two IOrchestrationTranslator Interfaces

`IOrchestrationTranslator` exists in both `Hrot.Common.Infrastructure` and `Hrot.Core.Network`.
`INetworkFactory.CreateOrchestratorTranslators` returns `Hrot.Core.Network.IOrchestrationTranslator`.
The test file disambiguates with a using alias:
`using IOrchestrationTranslator = Hrot.Core.Network.IOrchestrationTranslator;`

### Cyclone System Namespaces

- `CycloneNetworkIngressSystem` is in `Fdp.Network.Cyclone.Modules`
- `CycloneEgressSystem` and `CycloneNetworkCleanupSystem` are in `Fdp.Network.Cyclone.Systems`

Both namespaces must be imported in `SharedApplicationBootstrapper.cs`.

### TestBootstrapper.BuildOrchestration Stub

The `BuildOrchestration` override in tests does not delegate to `NodeBootstrapper.BuildOrchestration`
because that method requires `IScenarioEntityExtractor` when `scenarioSerializer != null` and has
additional required infrastructure (DdsParticipant, CheckpointIOWorker, etc.) that is unavailable in
headless tests. Instead the override records what it received and returns `new ClusterSlave(eventBus, nodeId, "TestNode")`.

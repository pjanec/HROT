# BATCH-06: Composition Root Wiring and Integration Tests

**Batch Number:** BATCH-06  
**Tasks:** CMC-S016, CMC-S017  
**Phase:** Phase 6 — Composition Root and Integration  
**Estimated Effort:** 8–12 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-05 complete

---

## 📋 Context

After Phases 1–5, the domain stack is clean:
- `ClusterMaster(FdpEventBus)` — bus-based, no DDS
- `ClusterSlave(FdpEventBus)` — bus-based, no DDS
- `NodeOpSlaveTranslator`, `NodeOpMasterTranslator`, `ClusterOpMasterTranslator` — ACL/bridge layer

This batch:
1. **CMC-S016:** Wires `ClusterMaster(bus)` into `OrchestratorSubsystem` (alongside the existing DDS path for UI). Wires `NodeOpSlaveTranslator` into slave subsystems.
2. **CMC-S017:** Adds end-to-end integration tests for the AllInOne 2PC scenario.

**Key constraint:** The old DDS-based constructor path stays functional. `AllSubsystemsClusterTransitionTests`, `ClusterOpE2eScriptTests`, and other integration tests that use `HandleClusterOpRequest` MUST continue passing. Do not break them.

### Build & Test Commands

```powershell
dotnet build IOS-IG-SimHost.sln -v q
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj
dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj
```

### Report

`.dev/cluster-master-cqrs-1/reports/BATCH-06-REPORT.md`

---

## ✅ Task 1: CMC-S016 — Update Composition Roots

### 1.1 `OrchestratorSubsystem.cs` — Wire ClusterMaster to Bus

**File:** `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs`

Add a second `FdpEventBus _orchestrationBus` (separate from `_eventBus` which is for time control):
```csharp
private FdpEventBus? _orchestrationBus;
private ClusterOpMasterTranslator? _clusterOpTranslator;
private NodeOpMasterTranslator?    _nodeOpTranslator;
```

In `Initialize()`:
1. Create `_orchestrationBus = new FdpEventBus()`.
2. Construct `_clusterMaster = new ClusterMaster(_orchestrationBus, _config)` — USE BUS CONSTRUCTOR.
3. Create `_clusterOpTranslator = new ClusterOpMasterTranslator(_sysOpRequestReader, _sysOpStatusWriter, _orchestrationBus)`.  
   Note: This requires a DDS `ClusterOpRequest` reader — add `DdsReader<ClusterOpRequest> _sysOpReader` field, instantiate in Initialize.
4. Create `_nodeOpTranslator = new NodeOpMasterTranslator(nodeId => new DdsWriter<NodeOpCommand>(_participant), _nodeOpStatusReader, _orchestrationBus)`.  
   Note: Add `DdsReader<NodeOpStatus> _nodeOpStatusReader` if not already present.
5. Keep `_sysOpWriter` (the `DdsWriter<ClusterOpRequest>`) for `ClusterScenarioPanel` UI injection — it still works.
6. Keep `TimeControlRequested` event handler wiring as-is — time control ops still go via `HandleClusterOpRequest`.

In `Tick()` (call each frame):
```csharp
_clusterOpTranslator?.Tick();
_nodeOpTranslator?.Tick();
```
These must be called BEFORE `_clusterMaster.Tick()` so ingress is processed first.

In `Dispose()`:
- Dispose `_nodeOpStatusReader`, `_sysOpRequestReader` if created here.

**Success check:** `Hrot.ClusterRunner.Integration.Tests` still pass.

### 1.2 `ExConSubsystem.cs` — Wire NodeOpSlaveTranslator

**File:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`

Add:
```csharp
private NodeOpSlaveTranslator? _nodeOpSlaveTranslator;
private FdpEventBus? _orchestrationBus;
```

Note: `ExConSubsystem` already has `_clusterSlave`. But it needs `_orchestrationBus` to be the SAME bus as `OrchestratorSubsystem._orchestrationBus` for AllInOne mode to work. 

**Option A (AllInOne shared bus):** Pass the `orchestrationBus` as a parameter to `ExConSubsystem.Initialize()` via the `SubsystemConfig` or a shared singleton. This requires a mechanism to share the bus between subsystems.

**Option B (Standalone per-subsystem bus + DDS bridge):** Each subsystem has its own bus. The `NodeOpSlaveTranslator` bridges DDS → bus. This works for distributed mode. For AllInOne mode, the translators on both master and slave sides mediate the in-process communication via in-memory "DDS" (fake readers/writers).

**Option C (Accepted complexity — defer AllInOne shared bus to debt):** For now, each subsystem has its own bus + translator. Accept that AllInOne mode goes via DDS locally (loopback). Add a DEBT entry. The integration tests (CMC-S017) will test using directly constructed components with a shared bus — bypassing this wiring.

**Recommendation: Use Option C for BATCH-06.** Wire each slave subsystem independently with its own bus + translator. The AllInOne shared bus optimization is a future improvement.

In `ExConSubsystem.Initialize()`:
```csharp
_orchestrationBus = new FdpEventBus();
var cmdReader     = new DdsReader<NodeOpCommand>(_participant);
var statusWriter  = new DdsWriter<NodeOpStatus>(_participant);
var hbWriter      = new DdsWriter<NodeHeartbeat>(_participant);
_nodeOpSlaveTranslator = new NodeOpSlaveTranslator(
    commandReader:   cmdReader,
    statusWriter:    statusWriter,
    heartbeatWriter: hbWriter,
    bus:             _orchestrationBus,
    nodeId:          iosNodeId);
_clusterSlave = new ClusterSlave(iosNodeId, SubsystemName, _orchestrationBus);
// register handlers on _clusterSlave as before
```

In `Tick()`, call `_nodeOpSlaveTranslator?.Tick()` before `_clusterSlave.Tick()`.

**Warning:** Check whether `_clusterSlave` already exists in `ExConSubsystem` and adjust — avoid constructing it twice.

### 1.3 `NodeBootstrapper.cs` — Wire NodeOpSlaveTranslator for SimHost

**File:** `Hrot.SimHost/NodeBootstrapper.cs`

The `ClusterSlave` already uses the bus constructor (from BATCH-03). Add `NodeOpSlaveTranslator`:
```csharp
var cmdReader  = new DdsReader<NodeOpCommand>(participant);
var statusWr   = new DdsWriter<NodeOpStatus>(participant);
var hbWriter   = new DdsWriter<NodeHeartbeat>(participant);
var slaveTranslator = new NodeOpSlaveTranslator(cmdReader, statusWr, hbWriter, eventBus, nodeId);
```

Register `slaveTranslator.Tick()` in the tick loop (called before `clusterSlave.Tick()`).

### 1.4 `CgfApplication.cs` and `IgApplication.cs`

Same pattern as NodeBootstrapper — add `NodeOpSlaveTranslator` alongside `ClusterSlave`.

---

## ✅ Task 2: CMC-S017 — Integration Tests for CQRS Orchestration

This is the most important part of BATCH-06. Write self-contained integration tests that prove the full 2PC flow works correctly.

### 2.1 AllInOne Full 2PC Test

**File:** `Hrot.Orchestrator.Integration.Tests/CqrsOrchestrationIntegrationTests.cs`

**Test scenario:** One ClusterMaster + one ClusterSlave with a stub handler, shared bus, no DDS.

```csharp
[Fact]
public async Task TransitionState_AllInOne_CompletesCqrsRoundTrip()
{
    // 1. Setup: shared bus, ClusterMaster + ClusterSlave with stub handler
    var bus    = new FdpEventBus();
    var master = new ClusterMaster(bus, ClusterConfiguration.NoMandatory());
    var slave  = new ClusterSlave(1, "SimHost", bus);
    slave.RegisterHandler(new StubAllOpsHandler(nodeId: 1));

    // 2. Register node via heartbeat
    bus.PublishManaged(new NodeHeartbeatEvent
        { NodeId = 1, LocalStateId = (int)ClusterState.Idle, SubsystemName = "SimHost",
          WallTicksUtc = DateTimeOffset.UtcNow.Ticks });
    TickBoth(master, slave, 1);

    // 3. Push TransitionStateIntent
    var txId = Guid.NewGuid();
    bus.PublishManaged(new TransitionStateIntent
        { TransactionId = txId, TargetState = FDP.Toolkit.Orchestration.ClusterState.LoadingLive });

    // 4. Tick until ClusterOpCompletedEvent arrives (max 10 ticks)
    ClusterOpCompletedEvent? completed = null;
    for (int i = 0; i < 10 && completed is null; i++)
    {
        TickBoth(master, slave, 1);
        completed = bus.ConsumeManaged<ClusterOpCompletedEvent>().FirstOrDefault();
    }

    // 5. Assert success
    Assert.NotNull(completed);
    Assert.Equal(OrchestrationStatusCode.Success, completed.Value.StatusCode);
}

private static void TickBoth(ClusterMaster master, ClusterSlave slave, int count)
{
    for (int i = 0; i < count; i++) { master.Tick(); slave.Tick(); }
}
```

The `StubAllOpsHandler` implements `IClusterStateHandler`:
```csharp
private sealed class StubAllOpsHandler : IClusterStateHandler
{
    private readonly int _nodeId;
    public StubAllOpsHandler(int nodeId) => _nodeId = nodeId;
    public bool CanHandle(NodeOpType op) => true;   // handles everything
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        => Task.FromResult<object?>(null);
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) {}
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) {}
}
```

**Additional required tests:**

2. `TransitionState_WithMandatoryNodeNotReady_ReturnsNoOp` — push intent but NO heartbeat registered → assert no `ExecuteNodeOpIntent` fan-out (bootstrap latch prevents it).

3. `ManageEpisode_AllInOne_PublishesEpisodeIntent` — push `ManageEpisodeIntent { IsStart = true, EpisodeId = guid }` → assert `ExecuteNodeOpIntent { Operation = NodeOpType.StartEpisode }` on bus.

4. `CancelOperation_CancelsInFlightTransaction` — start a transition, push `CancelOperationIntent`, tick → assert `ClusterOpCompletedEvent { StatusCode = Failure/Cancelled }`.

5. `NodeOpCompleted_WithFailure_PropagatesFailureStatus` — slave returns failure `NodeOpCompletedEvent { StatusCode = Failure }` → master propagates to `ClusterOpCompletedEvent.StatusCode`.

6. **Echo-chamber regression:** Verify that after `NodeOpCompletedEvent` is published from slave and consumed by master, the slave's bus does NOT contain a new `ExecuteNodeOpIntent` (no infinite loop).

### 2.2 Translator Round-Trip Test

**File:** `Hrot.Orchestrator.Integration.Tests/TranslatorRoundTripTests.cs`

Use in-memory fake DDS readers/writers (implement `DdsReader<T>` and `DdsWriter<T>` stubs or use existing test stubs if they're available). If the FDP toolkit provides test DDS stubs, use them. Otherwise, implement minimal ones.

```csharp
[Fact]
public void ClusterOpRequest_ThroughTranslators_ProducesClusterOpStatus()
{
    var bus   = new FdpEventBus();

    // Fake DDS readers/writers
    var clusterOpReader = new FakeDdsReader<ClusterOpRequest>();
    var clusterOpWriter = new FakeDdsWriter<ClusterOpStatus>();
    var nodeOpWriter    = new FakeDdsWriter<NodeOpCommand>();
    var nodeOpReader    = new FakeDdsReader<NodeOpStatus>();

    // Setup components
    var master       = new ClusterMaster(bus, ClusterConfiguration.NoMandatory());
    var masterXlator = new ClusterOpMasterTranslator(clusterOpReader, clusterOpWriter, bus);
    var nodeXlator   = new NodeOpMasterTranslator(id => nodeOpWriter, nodeOpReader, bus);

    var slave = new ClusterSlave(1, "SimHost", bus);
    slave.RegisterHandler(new StubAllOpsHandler(1));

    // Register node
    bus.PublishManaged(new NodeHeartbeatEvent
        { NodeId = 1, LocalStateId = 0, SubsystemName = "SimHost", WallTicksUtc = 0 });
    masterXlator.Tick(); nodeXlator.Tick(); master.Tick(); slave.Tick();

    // Push ClusterOpRequest via fake DDS reader
    clusterOpReader.Enqueue(new ClusterOpRequest
    {
        RequestId     = Guid.NewGuid(),
        OperationType = Hrot.NED.Descriptors.Orchestration.ClusterOpType.TransitionState,
        PayloadJson   = "{\"TargetState\": \"LoadingLive\"}"
    });

    // Tick translators + domain components until completion
    ClusterOpStatus? result = null;
    for (int i = 0; i < 15 && result is null; i++)
    {
        masterXlator.Tick();
        nodeXlator.Tick();
        master.Tick();
        slave.Tick();
        result = clusterOpWriter.Dequeue();
    }

    Assert.NotNull(result);
    Assert.Equal(OrchestrationStatusCode.Success, result.Value.StatusCode);
}
```

If `DdsReader<T>` / `DdsWriter<T>` lack testable fake implementations, check the codebase for existing `FakeDdsReader`/`FakeDdsWriter` test utilities. If they exist, use them. If not, define minimal inline stubs with an enqueue/dequeue interface.

---

## 🧪 Testing Requirements

All prior test suites MUST still pass. New tests:

| Test | Scenario | Pass condition |
|------|----------|----------------|
| `TransitionState_AllInOne_CompletesCqrsRoundTrip` | AllInOne with stub handler | `ClusterOpCompletedEvent { Success }` |
| `TransitionState_WithMandatoryNodeNotReady_ReturnsNoOp` | Bootstrap latch | No fan-out |
| `ManageEpisode_AllInOne_PublishesEpisodeIntent` | Episode management | Correct `NodeOpType` |
| `CancelOperation_CancelsInFlightTransaction` | Cancellation | Status = Failure/Cancelled |
| `NodeOpCompleted_WithFailure_PropagatesFailureStatus` | Error propagation | Master returns failure |
| **Echo-chamber regression** | No infinite loop | No stray intents |
| `ClusterOpRequest_ThroughTranslators_ProducesClusterOpStatus` | Full translator round-trip | DDS `ClusterOpStatus { Success }` |

Minimum: **7 new tests**

---

## ⚠️ Key Notes

1. **`ClusterConfiguration.NoMandatory()`** — you may need to create this static factory if it doesn't exist. It should return a `ClusterConfiguration` with `Mandatory = Array.Empty<int>()` so the bootstrap latch fires immediately (no mandatory nodes blocking).

2. **Bus buffering:** `FdpEventBus` may require `SwapBuffers()` between publish and consume in tests. Read the `FdpEventBus` API for the actual swap semantics.

3. **`ClusterMaster.Tick()` requires heartbeat before intents.** Register the node via `NodeHeartbeatEvent` and call `master.Tick()` ONCE before pushing `TransitionStateIntent`, otherwise the roster is empty and the request is ignored.

4. **Pre-existing failures:** 3 pre-existing test failures exist (GeoSpatial, TimeSync, TraceLogging). Do not try to fix them. Report them as-is.

5. **Do NOT delete `ClusterOpRequestAdapter.cs`.** It bridges legacy `HandleClusterOpRequest` path. Integration tests in `ClusterRunner.Integration.Tests` depend on it.

---

## 🎯 Success Criteria

- [ ] CMC-S016: `OrchestratorSubsystem` uses `ClusterMaster(orchestrationBus)` with `ClusterOpMasterTranslator` and `NodeOpMasterTranslator` in Tick()
- [ ] CMC-S016: `ExConSubsystem` wired with `NodeOpSlaveTranslator`
- [ ] CMC-S016: `NodeBootstrapper` wired with `NodeOpSlaveTranslator`
- [ ] CMC-S016: `CgfApplication` and `IgApplication` wired with `NodeOpSlaveTranslator`
- [ ] CMC-S017: 6 AllInOne integration tests pass
- [ ] CMC-S017: 1 translator round-trip test passes
- [ ] `dotnet build IOS-IG-SimHost.sln` → 0 errors
- [ ] All existing integration tests still pass
- [ ] TASK-TRACKER.md updated: all 16 tasks checked ✅
- [ ] Report submitted

---

## 📚 References

- DESIGN.md §7: `.dev/cluster-master-cqrs-1/DESIGN.md`
- DESIGN.md §7.2: AllInOne topology  
- DESIGN.md §7.3: Test harness
- TASK-DETAIL.md CMC-S016, CMC-S017
- `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` — current composition root
- `Hrot.ClusterRunner/Services/ExConSubsystem.cs` — slave composition root
- `Hrot.SimHost/NodeBootstrapper.cs` — SimHost slave wiring
- `Hrot.Orchestrator.Integration.Tests/ScenarioSaveLoadTests.cs` — example integration test pattern

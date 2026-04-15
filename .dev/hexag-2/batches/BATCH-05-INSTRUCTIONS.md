# BATCH-05 Instructions: Slave Subsystem Factory Refactor + Composition Root

**Tasks:** HEXAG2-S012, HEXAG2-S009
**Branch:** hexag

## Context
BATCH-04 completed the master-side (OrchestratorSubsystem) factory refactor.
BATCH-05 completes the slave side.

## Changes Required

### 1. Create NedSlaveOrchestrationTranslator.cs
File: `Hrot/Network/Hrot.Network.NED/Factory/NedSlaveOrchestrationTranslator.cs`
- Composite wrapping `NodeOpSlaveTranslator` + `ClusterOpEgressTranslator`
- Implements `ISlaveOrchestrationTranslator` with `Tick()` and `Dispose()`
- `Tick()` calls `_nodeOpTranslator.Tick()` then `_egressTranslator.Tick()`
- `Dispose()` disposes both

### 2. Create NedOrchestrationObserver.cs
File: `Hrot/Network/Hrot.Network.NED/Factory/NedOrchestrationObserver.cs`
- Wraps `OrchestrationObserverTranslator`
- Implements `IOrchestrationObserver` with `Tick()` and `Dispose()`

### 3. Update NedNetworkFactory.cs
- `CreateSlaveOrchestratorTranslators`: return `NedSlaveOrchestrationTranslator(_participant, bus, nodeId)` when `_participant != null`
- `CreateOrchestrationObserver`: return `NedOrchestrationObserver(_participant, bus)` when `_participant != null`

### 4. Rewrite ClusterScenarioPanel.SendRequest bus path
- Change from `_bus.PublishManaged(new ClusterOpIntent{...})` to publish typed intents:
  - PauseTime -> PauseTimeIntent
  - ResumeTime -> ResumeTimeIntent
  - StepTime -> StepTimeIntent
  - SetTimeScale -> SetTimeScaleIntent
  - TransitionState -> parse PayloadJson, publish TransitionStateIntent
  - ManageEpisode -> parse PayloadJson, publish ManageEpisodeIntent
  - SaveScenario -> ExecuteStorageOpIntent{SaveScenario}
  - ExportArchive -> ExecuteStorageOpIntent{Export}
  - ImportArchive -> ExecuteStorageOpIntent{Import}
  - TakeCheckpoint -> TakeCheckpointIntent
  - ReplaySeek -> SeekReplayIntent
  - CancelOperation -> CancelOperationIntent

### 5. Rewrite ClusterOpEgressTranslator.Tick()
- Replace `ConsumeManaged<ClusterOpIntent>()` with consuming each typed intent
- Map typed intents back to DDS ClusterOpRequest messages

### 6. Update ExConSubsystem.cs
- Replace `_nodeOpSlaveTranslator`, `_orchObserverTranslator`, `_clusterOpEgressTranslator` fields
- Add `ISlaveOrchestrationTranslator? _slaveTranslator` and `IOrchestrationObserver? _observer`
- Use `nodeFactory?.CreateSlaveOrchestratorTranslators(_bus, iosNodeId) ?? new NullSlaveOrchestrationTranslator()`
- Use `nodeFactory?.CreateOrchestrationObserver(_bus) ?? new NullOrchestrationObserver()`
- Update Update() and Shutdown() accordingly

### 7. Update NodeBootstrapper.cs
- Replace direct `new NodeOpSlaveTranslator(...)` with factory call

### 8. Update CgfSubsystem.cs (if it uses NodeOpSlaveTranslator directly)

### 9. HEXAG2-S009: Verify composition root
- Find ClusterRunner startup code
- Confirm OrchestratorSubsystem is constructed with INetworkFactory

## Success Conditions
- Zero `new NodeOpSlaveTranslator`, `new OrchestrationObserverTranslator`, `new ClusterOpEgressTranslator` in ExCon/SimHost/CGF
- Zero `ClusterOpIntent` references in ClusterOpEgressTranslator
- All existing tests pass
- `ExConSubsystem_HeadlessMode_InitializesWithoutException` test passes

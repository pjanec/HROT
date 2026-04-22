# BATCH-04 INSTRUCTIONS — hexag-2 Phase 2 Capstone: Factory Implementation

**Tasks:** HEXAG2-S006, HEXAG2-S007, HEXAG2-S008, HEXAG2-DEBT-002, HEXAG2-DEBT-007  
**Prerequisite:** BATCH-03 committed (commit 3b8744f)

---

## Overview

This batch completes the hexagonal decoupling by:
1. Implementing the real DDS orchestration translator composite (S006)
2. Extracting the ID allocator server into its own owned handle (S007)
3. Implementing the master time translators composite
4. Refactoring OrchestratorSubsystem to use INetworkFactory (S008)

After this batch, OrchestratorSubsystem must have zero direct DDS dependencies.

---

## Task 1 — HEXAG2-S006: NedOrchestrationTranslator (new file)

Create `Hrot/Network/Hrot.Network.NED/Factory/NedOrchestrationTranslator.cs`.

This class:
- Owns DdsReader<NodeHeartbeat>, DdsReader<ClusterOpRequest>, DdsWriter<ClusterOpStatus>, DdsReader<NodeOpStatus>
- Owns ClusterOpMasterTranslator and NodeOpMasterTranslator
- Caches per-node DdsWriter<NodeOpCommand> in a private dictionary
- Tick(): poll heartbeats -> PublishManaged<NodeHeartbeatEvent>; call translator Ticks
- Dispose(): disposes all DDS resources

## Task 2 — HEXAG2-S007: HostedIdAllocatorServer (new file)

Create `Hrot/Network/Hrot.Network.NED/Factory/HostedIdAllocatorServer.cs`.

Owns DdsIdAllocatorServer, CancellationTokenSource, Thread. Starts the polling thread in
constructor. Dispose() cancels the CTS and joins the thread with 2-second timeout.

## Task 3 — NedMasterTimeTranslators (new file)

Create `Hrot/Network/Hrot.Network.NED/Factory/NedMasterTimeTranslators.cs`.

Wraps the 3 time translators behind IMasterTimeTranslators:
- _timeModeTranslator (IDescriptorTranslator), _lockstepTranslator (MasterLockstepTranslator),
  _ntpTranslator (IDescriptorTranslator)
- ScanAndPublish(): both timeModeTranslator and lockstepTranslator
- PollIngress(): both timeModeTranslator and lockstepTranslator
- PollNtpIngress(): ntpTranslator only
- Dispose(): disposes all three

## Task 4 — Update NedNetworkFactory

Update CreateOrchestratorTranslators(), CreateIdAllocatorServer(), CreateMasterTimeTranslators()
to return real implementations when _participant != null. Return Null* when _participant == null.

## Task 5 — HEXAG2-S008: Refactor OrchestratorSubsystem

Major refactor: use INetworkFactory for all DDS resource creation.

Fields to ADD:
- `private INetworkFactory? _networkFactory;`
- `private IOrchestrationTranslator? _translator;`
- `private IDisposable? _idAllocatorServerHandle;`
- `private IMasterTimeTranslators? _timeTranslators;`

Fields to REMOVE:
- `_participant`, `_sysOpWriter`
- `_clusterOpTranslator`, `_nodeOpTranslator`
- `_sysOpRequestReader`, `_sysOpStatusWriter`, `_nodeOpStatusReader`, `_heartbeatReader`
- `_idAllocatorServer`, `_idServerCts`, `_idServerThread`
- `_timeModeTranslator`, `_lockstepTranslator`, `_masterTimeSyncTranslator`

Constructor: store `_networkFactory` in INetworkFactory constructor.

Initialize(): replace inline DDS creation with factory calls:
- `_translator = (_networkFactory ?? NullNetworkFactory).CreateOrchestratorTranslators(_bus!, config.NodeId)`
- `_idAllocatorServerHandle = (_networkFactory ?? NullNetworkFactory).CreateIdAllocatorServer()`
- `_timeTranslators = (_networkFactory ?? NullNetworkFactory).CreateMasterTimeTranslators(_bus!, config.NodeId)`
- Use `_networkFactory?.Participant` for GlobalContextClusterOpHandler
- Remove heartbeat bridging loop
- Keep SwapBuffers() call after MasterSyncController construction

Update(): replace individual translator calls:
- Phase 1: `_timeTranslators?.ScanAndPublish(); _timeTranslators?.PollIngress(); _translator?.Tick();`
- Phase 3: remove individual `_clusterOpTranslator?.Tick()` and `_nodeOpTranslator?.Tick()` calls
  (these are now inside `_translator?.Tick()` which is called in Phase 1 before SwapBuffers)
  Wait - in the current design, Tick() is called AFTER SwapBuffers (Phase 3).
  Reconsider: the translator's Tick reads from DDS and writes to bus WRITE buffer.
  It should be called BEFORE SwapBuffers (Phase 1) so the written events are available
  after SwapBuffers. OR it can be called AFTER SwapBuffers (Phase 3) if the intent is
  to process DDS events in the current frame via bus read buffer.
  
  Looking at the current code:
  - Phase 1 (before SwapBuffers): timeModeTranslator.ScanAndPublish + PollIngress
  - Phase 1: heartbeat bridge (DDS->bus WRITE)
  - Phase 2: SwapBuffers
  - Phase 3 (after SwapBuffers): _clusterOpTranslator.Tick() + _nodeOpTranslator.Tick()
  
  So the translator Tick() is currently in Phase 3 (after SwapBuffers). This means intents
  published by the translator become available to ClusterMaster.Tick() in the SAME frame
  (ClusterMaster reads from the WRITE buffer via ConsumeManaged which... wait, actually
  ConsumeManaged reads from the READ buffer, not the WRITE buffer).
  
  Actually: Phase 3 call to translator.Tick() writes intents to WRITE buffer.
  Then _masterSync.Update() and _clusterMaster.Tick() both use ConsumeManaged which reads
  from READ buffer. So the intents from this frame's translator.Tick() are NOT available
  until next frame. That's the 1-frame latency.
  
  Keep translator.Tick() in Phase 3 (after SwapBuffers) for consistency. So:
  - `_translator?.Tick()` goes in Phase 3, replacing the individual calls.
  
  For time translators:
  - ScanAndPublish + PollIngress go in Phase 1 (before SwapBuffers)
  - PollNtpIngress goes in Phase 5 (after SwapBuffers, after UiCache.Update)

Shutdown():
- Dispose `_idAllocatorServerHandle` first (joins thread)
- Dispose `_translator` (tears down DDS objects)
- Dispose `_timeTranslators`
- Remove all old disposal code for individual DDS objects

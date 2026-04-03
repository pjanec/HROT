# Technical Debt Tracker: ClusterMaster CQRS Decoupling

> P1 issues → corrective task 0 in the very next batch (never enter this tracker)  
> P2 issues → scheduled in an upcoming batch  
> P3 issues → logged for awareness, resolved opportunistically  
> ✅ = resolved (row kept for history)

| ID | Priority | Source | Description | Target Batch |
|----|----------|--------|-------------|--------------|
| DEBT-001 | P1-fixed | Design | EventId 9020/9021 collision with IdMessages.cs (9020, 9021 already taken). Design docs corrected to use range 9050-9057 before BATCH-01. | Pre-BATCH-01 ✅ |
| DEBT-002 | P3 | BATCH-03 review | `ExConSubsystem` uses magic string `"ExCon"` as ClusterSlave subsystem name — should use a shared constant to prevent silent mismatch with translator expectations. | BATCH-05 ✅ |
| DEBT-003 | P3 | BATCH-03 review | `ClusterSlave` test constructor uses hard-coded `nodeId=0` / `subsystemName="TestNode"` — defaults are invisible at call site; introduce a named factory or explicit parameter to make test intent clear. | BATCH-05 ✅ |
| DEBT-004 | P2 | BATCH-03 report | `NodeOpCompletedEvent.ResultPayload` is `object?` — bypasses the type system. After Phase 5 translators are stable, document a closed set of known payload types per `ClusterOp` or introduce a discriminated-union approach. | BATCH-07 ✅ |
| DEBT-005 | P3 | BATCH-03 report | `ReferencePrefetchHandler.Commit` body is empty (just logs); handler accumulates preparestate but never clears it. Safe today (single-dispatch), but will replay stale data if concurrent ops are ever allowed. | BATCH-05 ✅ |
| DEBT-006 | P3 | BATCH-03 report | `ReferenceArchiveHandler.PrepareAsync` file-scan has no timeout — slow NFS mounts will stall the op indefinitely. Add a `CancellationToken` with a configurable deadline. | BATCH-06 ✅ |
| DEBT-007 | P2 | BATCH-06 report | `ClusterSlave.Tick()` processes only one `ExecuteNodeOpIntent` per frame (breaks on `_pendingPrepare`). Multi-step cluster transitions are impossible in a single tick; subsequent intents are silently dropped after `SwapBuffers`. Fix: loop until `_pendingPrepare` is null or buffer exhausted. This resolves the 6 pre-existing AllSubsystems/ClusterOpE2e failures. | BATCH-07 ✅ |
| DEBT-008 | P3 | BATCH-06 report | `DdsIdAllocatorServer` is silently set to `null!` in bus-mode `ClusterMaster` constructor. Callers (e.g. `OrchestratorSubsystem`) must manually host the server. Should be an injectable dependency or clearly documented expectation. | BATCH-07 ✅ (documented) |
| DEBT-009 | P3 | BATCH-06 report | `FdpEventBus.SwapBuffers` silently discards all items still in the read buffer. Any event not consumed before `SwapBuffers` is permanently lost. Evaluate draining-queue mode for orchestration path to prevent silent data loss. | Backlog |

| DEBT-010 | P2 | BATCH-05 report | ClusterMaster bus-mode 2PC gap: `ConsumeNodeOpStatuses` has no bus-path consumer so 2PC transaction tracking doesn't function in bus mode. | BATCH-08 ✅ |
| DEBT-011 | P3 | BATCH-05 report | `StorageGatewayModule` lacks high-level archive operations (`ExportArchiveAsync`/`ImportArchiveAsync`/`SaveScenarioAsync`), preventing `EventDrivenStorageGateway` from exercising full functionality. Consider `IArchiveStorageBackend` implementations. | BATCH-08 ✅ |
| DEBT-012 | P3 | BATCH-05 report | Default `JsonStringEnumConverter` accepts integers and can silently deserialize enum values from integers. Adopt a `StrictStringEnumConverter` (no integer values) for external JSON boundaries. | Backlog ✅ |
| DEBT-013 | P2 | BATCH-07 report | `ClusterOpE2eScriptTests` (OverlappingCheckpoints, RecordAndReplaySeek, PreviewStateRestore, LiveFromReplayBranch) time out; root cause appears unrelated to multi-intent queue fixes and needs dedicated investigation. | BATCH-08 |
| DEBT-014 | P3 | BATCH-04 report | Several ClusterMaster test files (8) still exercise the DDS compatibility path rather than bus-mode; migrate these tests to bus-path to reduce maintenance and ensure bus-mode coverage. | BATCH-09 |

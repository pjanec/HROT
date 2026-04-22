# BATCH-05 Review

**Batch:** BATCH-05  
**Tasks:** CMC-S011, CMC-S012, CMC-S013, CMC-S014, CMC-S015  
**Reviewer:** Dev-Lead  
**Decision:** ✅ APPROVED

---

## Quality Assessment

### CMC-S011 — DTOs
- ✅ `StrictStringEnumConverter` correctly rejects integers — security/correctness win
- ✅ `WhenWritingNull` suppresses empty fields
- ✅ 4 tests confirming round-trip and rejection behavior

### CMC-S012 — NodeOpSlaveTranslator
- ✅ Circular dependency correctly avoided by using `JsonDocument` inline
- ✅ Correct `TargetNodeId` filtering — commands for other nodes dropped
- ✅ Heartbeat and status egress correctly bridges bus → DDS

### CMC-S013 — NodeOpMasterTranslator
- ✅ Bus → DDS command egress with `DomainPayload` serialization
- ✅ DDS → bus status ingress
- ✅ `null` DomainPayload → empty `PayloadJson`

### CMC-S014 — ClusterOpMasterTranslator
- ✅ All operation types handled
- ✅ Validation error path (missing TargetState → immediate error, nothing on bus) ✅
- ✅ End-to-end ClusterOpRequest → ClusterMaster test passes

### CMC-S015 — EventDrivenStorageGateway
- ✅ Async dispatch with `CancellationTokenSource` tracking
- ✅ `CancelOperationIntent` cancels correct in-flight operation
- ✅ Uses `IArchiveStorageBackend` interface cleanly

### Bug Fixes
- ✅ `ClusterMaster.ConsumeNodeOpStatuses` null guard — correct fix
- ✅ `EpisodeInjectionTests` BATCH-04 compile regression fixed

### Tests
- 18/18 new tests pass, 79/79 `Hrot.Orchestrator.Tests`

---

## Notes for Phase 6 (BATCH-06)

- `ClusterOpRequestAdapter.cs` (created in BATCH-04) may now overlap with `ClusterOpMasterTranslator` — developer should evaluate consolidation or deletion
- `EventDrivenStorageGateway` uses `IArchiveStorageBackend` — the composition root (BATCH-06/CMC-S016) needs to wire up the concrete implementation
- Time-control ops still bypass the translator and use `HandleClusterOpRequest` — this is intentional, noted in DEBT-TRACKER

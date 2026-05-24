# DEBT TRACKER: ClusterMaster God-Class Refactoring

**Project:** ClusterMaster God-Class Refactoring  
**Maintained by:** Development Lead

---

## Legend

| Priority | Description |
|---|---|
| P1 | Critical -- must fix before next batch ships (becomes Corrective Task 0) |
| P2 | Important -- schedule in the next batch |
| P3 | Minor -- schedule when convenient |

`✅` = Resolved. Rows are never deleted.

---

## Open Items

| ID | Priority | Source | Description | Target |
|---|---|---|---|---|
| DEBT-01 | P2 | BATCH-01-REVIEW | `StorageProcessManagerTests.cs` missing: SC1-SC3 unit tests not written for TASK-S002. Shim manifest inclusion not unit-tested. | ✅ BATCH-02 |
| DEBT-02 | P2 | BATCH-01-REVIEW | `_pendingSerializeTasks`, `SerializeLocalTask`, `HandleSerializeLocalCompletion` remain in `ClusterMaster` (ExportArchive path). Full removal deferred. | TASK-P001 |
| DEBT-03 | P2 | BATCH-01-REVIEW | 7 pre-existing test failures in `Hrot.Orchestrator.Tests` (episode x4, archive x1, fan-out x1, prefetch x1). Not introduced by BATCH-01. | TASK-S003 (episode), TASK-P002 (prefetch), TASK-S003 (fan-out/archive investigation) |
| DEBT-04 | P3 | BATCH-03-REVIEW | `SetMasterSync` kept as Obsolete no-op in `ClusterMaster` instead of deleted. Remove once confirmed no callers exist. | TASK-P001 |
| DEBT-05 | P3 | BATCH-03-REVIEW | `LiveBranchProcessManager.SnapAndPause()` called with empty node set. Should wire active roster. | TASK-P001 |

---

## Resolved Items

*(none yet)*

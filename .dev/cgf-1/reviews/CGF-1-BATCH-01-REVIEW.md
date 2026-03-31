# CGF-1-BATCH-01 Review

**Batch:** CGF-1-BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** APPROVED

---

## Summary

CGF1-S0101–S0103 are implemented: orchestration DDS types in `Hrot.NED.Descriptors.Orchestration`, `ClusterMaster` with `SystemStateTopic` publish and hosted `DdsIdAllocatorServer`, `Hrot.Orchestrator.Standalone`, Runner `--mode orchestrator`, SimHost allocator migration with `LocalIdAllocatorFallbackHost` and `NodeConfiguration` flags. Build is clean (0 warnings). Targeted tests behave well; full-solution parallel test runs still show DDS domain contention (confirmed locally: `DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` fails under parallel full suite, passes alone).

---

## Implementation vs task / design

| Area | Verdict |
|------|---------|
| **S0101** | Structs, enums, namespaces, and QoS on `SystemStateTopic` / `NodeHeartbeat` match [CGF-1-DESIGN.md §3.1](../CGF-1-DESIGN.md#31-stage-11--orchestration-dds-schema). |
| **S0102** | `ClusterMaster` publishes Standby on construct, subscribes `NodeHeartbeat`, `NodeRoster` + prune at 5 s, skeleton `DistributedTransaction`, ID server threaded + `Tick()`. Runner `OrchestratorSubsystem` ticks `ClusterMaster`. Standalone exits on Ctrl+C. |
| **S0103** | No `DdsIdAllocatorServer` field on `SimHostApp`; server on `ClusterMaster`; fallback path via `LocalIdAllocatorFallbackHost`. Migration test allocates entity id `> 0` with orchestrator pumping — **behavioral**, not string-based. |

**Spec / design deltas (non-blocking, tracked as debt):**

- Task text mentions waiting on **orchestrator heartbeat**; implementation keys off **DDS publication match** on the ID allocator — reasonable for S0103 scope, but normative docs should say so.
- Design / later tasks use `NodeOpType.ReplaySeek`; code uses `NodeReplaySeek = 13` with an IDL-clash comment — wire value correct; align naming in design or codegen docs.
- [CGF-1-TASK-DETAIL.md §S0105](../CGF-1-TASK-DETAIL.md) shows `EjectNode(Guid)` while `NodeHeartbeat.NodeId` is `int` — fix in task detail or implementation when S0105 lands.

---

## Test quality

| Test | Assessment |
|------|------------|
| `OrchestrationSchemaTests` | Asserts real attributes and enum values — good. **Gap vs task detail:** success condition asks for a **reflection scan of all types** in `Hrot.NED.Descriptors.Orchestration`; tests use a **fixed list** of seven structs. New topics could ship without test failure. |
| `ClusterMasterBootstrapTests.OrchestratorPublishesStandbyOnStartup` | Validates received `CurrentState` and `TransactionEpoch` within 3 s — good. **Gap vs CGF1-S0102:** spec asks for **exactly one** sample; test accepts the first matching sample and does not count. |
| `DdsIdAllocatorMigrationTests` | Strong: end-to-end spawn + `id > 0`, reflection proves no `DdsIdAllocatorServer` field on `SimHostApp`. |

---

## Issues found

No P1 blockers. No code changes required before merge; address debt in CGF-1-BATCH-02.

---

## Verdict

**APPROVED.** Requirements for BATCH-01 are met; remaining gaps are test strictness, infra flakiness under parallel test hosts, and small hygiene items — captured in [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) with target **CGF-1-BATCH-02**.

---

## Commit message

```
feat(cgf-1): orchestration DDS schema, ClusterMaster, centralized ID allocator (CGF-1-BATCH-01)

Completes CGF1-S0101, CGF1-S0102, CGF1-S0103.

- Add Hrot.NED.Descriptors.Orchestration topics/enums (OrchestrationMessages.cs) and schema tests.
- Add Hrot.Orchestrator (ClusterMaster, NodeRoster, NodeHealthProfile, DistributedTransaction
  skeleton) with hosted DdsIdAllocatorServer; Hrot.Orchestrator.Standalone; Orchestrator.Tests.
- Wire Hrot.ClusterRunner --mode orchestrator via OrchestratorSubsystem.
- SimHost: client-only DdsIdAllocator; LocalIdAllocatorFallbackHost + NodeConfiguration flags;
  DdsIdAllocatorMigrationTests.

Tests: Hrot.NED.Tests, Hrot.Orchestrator.Tests, Hrot.SimHost.Integration.Tests
(migration). Note: parallel full-solution test runs may flake on DDS domain 0 — see DEBT-TRACKER.

Related: .dev/cgf-1/CGF-1-DESIGN.md §3.1–§3.3, CGF-1-TASK-DETAIL.md
```

---

**Next batch:** [CGF-1-BATCH-02](../batches/CGF-1-BATCH-02-INSTRUCTIONS.md)

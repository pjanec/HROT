# CGF-1-BATCH-02 Review

**Batch:** CGF-1-BATCH-02  
**Reviewer:** Development Lead  
**Date:** 2026-03-28  
**Status:** APPROVED — **mandatory P1 follow-up in CGF-1-BATCH-03** (product policy; see Issues)

---

## Summary

Part A debt from BATCH-01 is implemented in code: single `ProcessRequests` path on the ID server thread, domain-isolated orchestrator tests with `[Collection]` + exactly-one `SystemStateTopic` assertion, reflection-based `OrchestrationSchemaTests`, 5 s allocator warning, `NodeRoster` reuse buffer, removal of dead `_profiles`. **CGF1-S0104** is largely present: `Hrot.Common` + `IDsmHandler`, four `ClusterSlave` implementations with background enqueue / main-thread `Tick`, `CgfApplication`, Runner `CgfSubsystem`, SimHost wiring via `NodeBootstrapper.BuildOrchestration`, integration test `OrchestratorReceivesHeartbeatsFromBothNodes` on domain 16. No `IDsmHandler` references under `FDP/` (verified).

---

## Issues found

### Issue 1: IG swallows DDS failures (P1 — fail-fast violation)

**File:** `Hrot.IG/IgApplication.cs` — `InitializeNetwork`, `try` / `catch` around full DDS setup (~lines 743–918).  
**Problem:** Any exception logs `[IG] Network init failed … Running offline.` and sets `_networkEnabled = false`. DDS is treated as optional; **`_drillSlave` is never created** in the catch path, so ClusterSlave silently absent.  
**Required:** When `enableNetwork` is true, DDS initialization **must not** be masked. Propagate failure (rethrow after cleanup, or abort process with explicit fatal message). Same policy must be applied consistently wherever DDS is required (audit SimHost, IOS, Runner).

### Issue 2: SimHost ID allocator fallback (P1 — legacy / silent degradation)

**Files:** `Hrot.SimHost/Network/LocalIdAllocatorFallbackHost.cs`, `SimHostApp.EnsureIdAllocatorRouting`, `NodeConfiguration.IdAllocatorLocalFallback*`.  
**Problem:** Local in-process `DdsIdAllocatorServer` remains as a **fallback**; with `IdAllocatorLocalFallbackEnabled == false`, wait can end with **no match and no throw** (continues without allocator — eventual `AllocateId` failure only later). This hides misconfiguration.  
**Required:** Remove fallback path; **require** orchestrator-hosted allocator; **fail loudly** (throw / fatal) if publication match is not established within a bounded startup window. Update all tests (including `ClusterSlaveHeartbeatTests`, migration tests) to run **ClusterMaster** (or equivalent) — no local server fallback.

### Issue 3: Standalone executables vs Runner-only subsystems (P2 — product consistency)

**Solution:** `IOS-IG-SimHost.sln` includes `Hrot.Orchestrator.Standalone` and `Hrot.CGF.Standalone`. Other subsystems have no separate `.Standalone` exe; production expectation is **Hrot.ClusterRunner** only.  
**Required:** Remove both standalone projects from the solution and delete or retire their project folders; document Runner-only launch in `.dev/cgf-1/CGF-1-ONBOARDING.md`. Keep `CgfApplication` for in-process tests and `CgfSubsystem` for Runner.

### Issue 4: Parameterless `SimHost.ClusterSlave()` (P3 — “DDS-less” escape hatch)

**File:** `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs`, `NodeBootstrapper.BuildOrchestration`.  
**Problem:** No-arg constructor disables heartbeats/commands; `BuildOrchestration` allows `participant == null` for tests. Conflicts with **DDS always required** policy.  
**Required:** Remove or restrict (e.g. `internal` + test assembly friend only) and **throw** when production roles get null participant.

### Issue 5: `ClusterSlave.DispatchCommand` 2PC stub (P2 — correctness / future landmine)

**Files:** `Hrot.IG/.../ClusterSlave.cs`, `Hrot.SimHost/.../ClusterSlave.cs`, `Hrot.ExCon/.../ClusterSlave.cs`.  
**Problem:** `_ = handler.PrepareAsync(...)` without await, then immediate `Commit` — not a real prepare/commit sequence. Acceptable only as explicit stub until S0202; should be documented and covered by debt.

### Issue 6: `OrchestrationSchemaTests` type filter (P3)

**File:** `Hrot.NED.Tests/OrchestrationSchemaTests.cs` — `IsCodeGenType` uses `t.Name.Contains('_')`, which may exclude a future legitimate hand-written struct. Prefer suffix-only rules or an allowlist for codegen types.

### Issue 7: `ClusterMasterBootstrapTests` early exit vs “exactly one”

**File:** `Hrot.Orchestrator.Tests/ClusterMasterBootstrapTests.cs`.  
**Note:** Loop breaks on `received.Count >= 1` then asserts `Count == 1`. If the writer ever emits two samples in one read batch before break, the test fails (good). Consider draining until deadline then asserting single sample for clarity — optional hardening.

---

## Test quality assessment

| Test / area | Verdict |
|-------------|---------|
| `OrchestratorReceivesHeartbeatsFromBothNodes` | **Strong** for roster keys and `LocalClusterState == Standby`. Does **not** assert 1 Hz heartbeat spacing (task says publish at 1 Hz); acceptable for first slice but weak on timing. Uses **`IdAllocatorLocalFallbackEnabled = true`** — conflicts with upcoming removal of fallback. |
| `OrchestrationSchemaTests` (reflection) | **Good** coverage extension vs fixed list; see filter fragility above. |
| `ClusterMasterBootstrapTests` | **Good** domain isolation + exactly-one sample count. |
| FDP boundary | **Good** — `grep IDsmHandler` over `FDP/` is empty. |

---

## Design alignment

- **§3.4 ClusterSlave:** Heartbeat + `ConcurrentQueue` + listener thread + main-thread `Tick` matches the described pattern.  
- **Report** accurately describes `Hrot.Common` rationale (avoid Runner circular refs).  
- **Product policy (this review):** Silent offline mode and allocator fallback are **out of line** with stakeholder direction; **CGF-1-BATCH-03** corrects this before treating Phase 1 as closed.

---

## Verdict

**APPROVED** for completion of CGF-1-BATCH-02 scope and **CGF1-S0104** checklist, with **non-negotiable P1 items** scheduled as **first tasks in CGF-1-BATCH-03** (standalone removal, DDS fail-fast, allocator fallback removal). Do not defer those behind S0105 feature work.

---

## Commit message

```
feat(cgf-1): ClusterSlave foundation + BATCH-01 debt closure (CGF-1-BATCH-02)

Completes CGF1-S0104 and closes seven CGF-1-BATCH-01 debt rows.

- Hrot.Common: IDsmHandler; ClusterSlave in SimHost, IG, IOS, CGF; CgfApplication;
  Runner CgfSubsystem / RunMode.CGF; NodeBootstrapper.BuildOrchestration wiring.
- ClusterMaster: ProcessRequests only on ID server thread; NodeRoster _staleBuffer.
- Tests: domain 15/16 isolation, reflection orchestration schema tests, exactly-one
  SystemStateTopic sample, ClusterSlaveHeartbeatTests, allocator 5s warn log.
- EcsRecordReplayController: IDsmHandler stubs (full 2PC deferred to S0202).

Follow-up (CGF-1-BATCH-03): fail-fast DDS on IG, remove allocator fallback and
Standalone exes, align tests with orchestrator-required allocator.

Related: CGF-1-DESIGN §3.4, CGF-1-TASK-DETAIL §CGF1-S0104, DEBT-TRACKER (BATCH-02 ✅).
```

---

**Next batch:** [CGF-1-BATCH-03](../batches/CGF-1-BATCH-03-INSTRUCTIONS.md)

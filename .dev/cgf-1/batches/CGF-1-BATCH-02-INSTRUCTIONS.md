# CGF-1-BATCH-02: BATCH-01 debt + DrillSlave foundation (CGF1-S0104)

**Batch number:** CGF-1-BATCH-02  
**Tasks:** *Corrective / debt (BATCH-01)* → **CGF1-S0104**  
**Phase:** Phase 1 — Skeleton (Stage 1.4)  
**Estimated effort:** 20–24 hours (~5–7 h debt + ~15–18 h S0104)  
**Priority:** HIGH  
**Dependencies:** [CGF-1-BATCH-01](../reviews/CGF-1-BATCH-01-REVIEW.md) — APPROVED  

---

## Onboarding and workflow

### Developer instructions

Complete **all corrective items in part A first** (so P2/P3 from BATCH-01 does not accumulate), then implement **CGF1-S0104** per task detail. Run tests after each logical chunk; before the report, run **`dotnet test IOS-IG-SimHost.sln`** and resolve failures (including parallel-run DDS issues per part A). Do not ask for permission to fix obvious test or build breaks.

### Required reading (in order)

1. **Workflow:** [.dev/.guides/DEV-GUIDE.md](../../.guides/DEV-GUIDE.md)  
2. **Onboarding:** [.dev/cgf-1/CGF-1-ONBOARDING.md](../CGF-1-ONBOARDING.md)  
3. **Previous review:** [.dev/cgf-1/reviews/CGF-1-BATCH-01-REVIEW.md](../reviews/CGF-1-BATCH-01-REVIEW.md)  
4. **Design:** [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) — §3.4 (Stage 1.4)  
5. **Task detail:** [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md) — **CGF1-S0104**  
6. **Debt targets:** [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) — rows with **Target Fix CGF-1-BATCH-02**  

### Source / test locations (repo root)

| Item | Path |
|------|------|
| DrillMaster, NodeRoster | `Bagira.Orchestrator/` |
| SimHost app | `Bagira.SimHost/SimHostApp.cs`, `Bagira.SimHost/Network/LocalIdAllocatorFallbackHost.cs` |
| Schema tests | `Bagira.DDS.DataModel.Tests/OrchestrationSchemaTests.cs` |
| Orchestrator bootstrap test | `Bagira.Orchestrator.Tests/DrillMasterBootstrapTests.cs` |
| DDS integration | `Bagira.SimHost.Integration.Tests/` (migration + lifecycle / domain isolation) |
| Runner orchestrator | `Bagira.Runner/Services/OrchestratorSubsystem.cs` |
| Solution | `IOS-IG-SimHost.sln` |

### Build and test

```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test IOS-IG-SimHost.sln
```

If parallel full-suite runs remain flaky after part A.2, document residual risk in the report and prefer a **repeatable** local command (e.g. run `Bagira.SimHost.Integration.Tests` alone) for gatekeeping until CI policy is updated.

### Report / questions / review

- **Report:** `.dev/cgf-1/reports/CGF-1-BATCH-02-REPORT.md`  
- **Questions:** `.dev/cgf-1/questions/CGF-1-BATCH-02-QUESTIONS.md`  
- **Review (lead):** `.dev/cgf-1/reviews/CGF-1-BATCH-02-REVIEW.md`  

### Debt

When you resolve a DEBT-TRACKER row, mark it **✅** and set **Target Fix** to `✅ CGF-1-BATCH-02`. New P2/P3 from this batch → [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) with source `CGF-1-BATCH-02`.

---

## Mandatory workflow: test-driven progression

1. **Part A (debt):** each bullet → tests green → next bullet  
2. **Part B (S0104):** follow task detail order; **all** pre-existing tests plus new `DrillSlaveHeartbeatTests` green before report  

Do not start part B until part A P2 items are done (P3 may be combined with B if time-critical, but **P2 first**).

---

## Context

[BATCH-01 review](../reviews/CGF-1-BATCH-01-REVIEW.md) approved the schema/orchestrator/allocator work with follow-ups in [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md). This batch clears **Target Fix = CGF-1-BATCH-02** rows, then delivers **DrillSlave** on every node so the orchestrator can observe **NodeHeartbeat** from SimHost and CGF (and later IG/IOS). **CGF1-S0105** is **out of scope** here — planned for **CGF-1-BATCH-03**.

---

## Part A — Corrective work & debt (BATCH-01 follow-up)

Resolve these DEBT-TRACKER items (and any you mark ✅ in the same pass):

### A.1 — P2: Single `ProcessRequests` path in `DrillMaster` (DEBT-TRACKER: Performance / CGF-1-BATCH-01)

**Problem:** `DdsIdAllocatorServer.ProcessRequests()` runs on the dedicated background thread **and** in `DrillMaster.Tick()`.

**Requirement:** One clear ownership model — e.g. **only** the background loop **or** **only** `Tick()` (if Runner/orchestrator always ticks). Remove redundant calls; keep allocator responsive under load. Verify `Bagira.Orchestrator.Tests` and `DdsIdAllocatorMigrationTests` still pass.

### A.2 — P2: DDS parallel test isolation (DEBT-TRACKER: Testing/Infra / CGF-1-BATCH-01)

**Problem:** Many tests use **domain 0**; parallel test processes interfere (report + review: `DomainIsolation_*`, IOS, Fdp.Tests, NetworkDemo).

**Requirement:** Implement a **minimal, documented** mitigation, for example:

- xUnit **`[Collection("…")]`** grouping for CGF-related integration tests and/or `Bagira.Orchestrator.Tests` so they do not run concurrently with other domain-0 DDS tests in the same process; **and/or**  
- configurable **non-zero default test domain** for new CGF harnesses; **and/or**  
- a short **contributor note** in `.dev/cgf-1/CGF-1-ONBOARDING.md` or `README.md` if CI must use `--maxcpucount:1` until a broader fix lands.

Choose what actually eliminates or greatly reduces flakes **in this repo**; verify with at least one full `dotnet test IOS-IG-SimHost.sln` run (note outcome in report).

### A.3 — P3: `OrchestrationSchemaTests` namespace scan (DEBT-TRACKER: Testing / CGF-1-BATCH-01)

**Requirement:** Match [CGF-1-TASK-DETAIL.md §CGF1-S0101](../CGF-1-TASK-DETAIL.md#cgf1-s0101--orchestration-dds-schema-definition): reflect over **all** `partial struct` types in `Bagira.BDC.SSTD.Orchestration` and assert `[DdsTopic]` + `[DdsIdlFile("bdc-sst-orchestration")]` on each. Keep (or merge with) existing enum/QoS/key tests.

### A.4 — P3: `OrchestratorPublishesStandbyOnStartup` — exactly one sample (DEBT-TRACKER)

**Requirement:** Align with CGF1-S0102: after startup window, assert **exactly one** valid `SystemStateTopic` sample (or document why history depth > 1 makes a different assertion necessary — if so, update task detail in a separate doc PR).

### A.5 — P3: `EnsureIdAllocatorRouting` warning (DEBT-TRACKER)

**Requirement:** When `IdAllocatorLocalFallbackEnabled == false` and no publication match, emit **`FdpLog` warning** at approximately **5 s** into the wait (before the 30 s deadline) so misconfiguration is visible.

### A.6 — P3: `NodeRoster.PruneStale` allocations (DEBT-TRACKER)

**Requirement:** Remove per-tick `new List<int>()` (reuse buffer, stack, or in-place removal).

### A.7 — P3: `DrillMaster._profiles` (DEBT-TRACKER)

**Requirement:** Remove dead `_profiles` dictionary **or** replace `NodeRoster` duplication with a single authoritative structure — no redundant unused state.

---

## Part B — CGF1-S0104: DrillSlave foundation

**Task definition:** [CGF-1-TASK-DETAIL.md §CGF1-S0104](../CGF-1-TASK-DETAIL.md#cgf1-s0104--drillslave-foundation)  
**Design:** [CGF-1-DESIGN.md §3.4](../CGF-1-DESIGN.md#34-stage-14--drillslave-foundation)

### B.1 — Projects

- Add **`Bagira.CGF`** (`net8.0` library) and **`Bagira.CGF.Standalone`** (executable), registered in `IOS-IG-SimHost.sln`.  
- References per design / task (DataModel, Map.Common, Fdp.Kernel, network stack as needed — **no FDP project may reference `Bagira.*` for `IDsmHandler`**).

### B.2 — `IDsmHandler`

- There is no **`Bagira.Common`** assembly today; **create `Bagira.Common`** (minimal `net8.0` library) **or** use another **Bagira-layer** shared project that **IG, IOS, SimHost, CGF, and Orchestrator** can reference **without** pulling inappropriate dependencies. **Do not** place `IDsmHandler` under `FDP/`.  
- Declare **`IDsmHandler`** (and any shared **`PendingMainThreadAction`** / delegate types) per task detail.  
- **Audit:** no `FDP.*` project may reference `IDsmHandler`’s assembly; grep/`dotnet` build confirms.

### B.3 — `DrillSlave` implementations

Implement in:

- `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`  
- `Bagira.IG/Modules/Orchestration/DrillSlave.cs`  
- `Bagira.IOS/Orchestration/DrillSlave.cs` (no-ECS; skip handlers requiring `EntityRepository`)  
- `Bagira.CGF/Modules/Orchestration/DrillSlave.cs`  

**Behavior (normative detail in task + design):**

- Publish **`NodeHeartbeat`** at **1 Hz** (wall-clock `Stopwatch`).  
- Subscribe **`NodeOpCommand`**; on DDS thread **only enqueue** to **`ConcurrentQueue<…>`**; **`Tick()`** (BeforeSync / equivalent phase) **dequeues and dispatches**.

### B.4 — Registration

- Register **SimHost** and **CGF** `DrillSlave` instances in the respective application **`OnLoad` / initialization** paths (exact files per your codebase — mirror existing subsystem registration style).  
- **IG** and **IOS:** wire where those apps join the DDS loop (task requires all four implementations).

### B.5 — Tests

**Success conditions** from task detail — implement **`DrillSlaveHeartbeatTests.OrchestratorReceivesHeartbeatsFromBothNodes`**:

- Orchestrator + SimHost + CGF (in-process harness acceptable).  
- Within **2 s** wall-clock, **`DrillMaster.NodeRoster`** contains **both** node IDs (SimHost and CGF).  
- **`LocalDsmState == DSMState.Standby`** on both heartbeats.

Tests must assert **real roster / state values**, not log strings or null checks only.

---

## Testing requirements

- Part A: no regressions in projects you touch; migration + bootstrap tests remain meaningful.  
- Part B: new integration test above + **all** pre-existing tests pass (per task detail).  
- Test quality: behavior-level assertions (roster IDs, DSM state, heartbeat rate within tolerance if you assert timing).

---

## Report requirements

`.dev/cgf-1/reports/CGF-1-BATCH-02-REPORT.md`: commands, test summary, **which DEBT-TRACKER rows were closed**, developer insights (issues, weak spots, extra decisions, edge cases, suggested commit message).

---

## Success criteria

- [ ] All **CGF-1-BATCH-02** rows in [.dev/DEBT-TRACKER.md](../../DEBT-TRACKER.md) addressed (✅ or consciously deferred with new row).  
- [ ] **CGF1-S0104** success conditions satisfied.  
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors; new code warning-free to repo standard.  
- [ ] `dotnet test IOS-IG-SimHost.sln` outcome documented (parallel flake status).  
- [ ] Report submitted.  

---

## Reference

- [.dev/cgf-1/CGF-1-TASK-DETAIL.md](../CGF-1-TASK-DETAIL.md)  
- [.dev/cgf-1/CGF-1-DESIGN.md](../CGF-1-DESIGN.md) §3.4  
- [.dev/.guides/DEV-LEAD-GUIDE.md](../../.guides/DEV-LEAD-GUIDE.md)  

**Next (lead):** CGF-1-BATCH-03 — **CGF1-S0105** (cluster config, bootstrap latch, ejection, ImGui, orchestrator tests) + remaining doc/spec debt (Target **CGF-1-BATCH-03** in DEBT-TRACKER).

# CGF-1-BATCH-04 Report

**Batch:** CGF-1-BATCH-04  
**Developer:** AI  
**Date:** 2026-03-28  
**Status:** COMPLETE

---

## Summary

All Part A debt items from BATCH-03 review resolved. CGF1-S0201 (BFS Transition Planner) implemented and all new tests pass. Solution builds clean with zero new warnings.

---

## Part A — Debt from BATCH-03 review

### A.0 — Verify test parallel policy (lead-landed)

**Status:** Confirmed. Both `xunit.runner.json` files exist:
- `FDP/Kernel/Fdp.Kernel.Tests/xunit.runner.json` — `parallelizeAssembly: false`, `maxParallelThreads: 1`
- `FDP/ModuleHost/ModuleHost.Core.Tests/xunit.runner.json` — same

Ran `dotnet test IOS-IG-SimHost.sln --nologo` during session. Runner, SimHost, and IG test suites pass (112, 360, 429 tests respectively). DDS flake from those assemblies eliminated. Note: new DDS tests in `Bagira.Orchestrator.Tests` use domain 15 (non-conflicting); the policy of unique domains remains the required approach for any new DDS tests.

### A.1 — `ClusterConfiguration.LoadFrom` fail-fast (P2)

**File:** `Bagira.Orchestrator/ClusterConfiguration.cs`

**Change:** Removed the blanket `catch { return Default; }`. Now:
- **File absent** → returns `Default` (zero-config dev mode preserved).
- **File exists but read fails** → throws `InvalidOperationException` with path + inner exception.
- **File exists but JSON is invalid** → throws `InvalidOperationException` with path + inner exception.
- **Valid JSON that deserializes to null** → throws `InvalidOperationException`.

XML doc updated to explain the rule.

### A.2 — Align S0105 tests with task detail (P3)

**File:** `Bagira.Orchestrator.Tests/DrillMasterBootstrapTests.cs`

**Change:** Both `RejectsCommands_UntilMandatoryNodesReady` and `TransactionHistory_RecordsCompletedTransaction` now use `SysOpType.TransitionState` with `PayloadJson = ((int)DSMState.LoadingLive).ToString()`. This is normatively consistent with when the planner wiring is exercised.

### A.3 — `SurvivingNodes_*` per-node assertion (P3)

**Approach chose:** Option 2 — updated test doc-comment with explicit broadcast-limitation note, updated `CGF-1-TASK-DETAIL.md §CGF1-S0105` with a "Phase 1 broadcast note".

**Rationale:** In Phase 1 `BroadcastNodeOp` writes a single sample to the `NodeOpCommand` DDS topic without per-node key filtering. Any participant/reader in the same domain receives the broadcast. Adding a second in-process participant (to simulate the ejected SimHost) would receive the same sample because domain 15 is shared; the test would trivially fail to prove "no commands after ejection". The correct guarantee requires keyed per-node topic keys which arrive in Phase 2. Three DEBT rows added: one closed ✅ (broadcast documented), one open for Phase 2 per-node isolation.

The test currently validates:
- Ejected node is removed from the roster (strongest possible guarantee in Phase 1).
- Broadcast command set is correct (AbortTransaction + PrepareState(Standby)).

### A.4 — ImGui §3.5 completeness (P3)

**Files modified:**
- `Bagira.Orchestrator/NodeHealthProfile.cs` — added `CpuUsagePercent: float` and `RamUsedBytes: long` properties.
- `Bagira.Orchestrator/DrillMaster.cs` (`IngestHeartbeats`) — copies `hb.CpuUsagePercent` and `hb.RamUsedBytes` into the profile.
- `Bagira.Runner/Services/OrchestratorSubsystem.cs` (`DrawUI`):
  - Health table: 4 → 6 columns, added **CPU %** and **RAM (MB)**.
  - 2PC history table: 3 → 4 columns, added **ACK Latency (ms)** column rendered as `"0"` when `NodeAckLatencyMs` is empty, otherwise `"nodeId:Xms, ..."` per-node summary.

`NodeAckLatencyMs` dictionary is already declared on `DistributedTransaction` from BATCH-03. Populating it with real ACK timing is deferred to CGF1-S0202 (full 2PC round trip). DEBT row updated accordingly.

### A.5 — Documentation hygiene (P3)

**Files modified:**
- `CGF-1-DESIGN.md §6 (New Projects & File Map)`:
  - Removed `Bagira.Orchestrator.Standalone` and `Bagira.CGF.Standalone` rows.
  - Updated `Bagira.Orchestrator` description to say "hosted in Runner".
  - Fixed stale §3.2 sentence "as a separate process via `Bagira.Orchestrator.Standalone`" → "as a subsystem hosted inside `Bagira.Runner`".
- `CGF-1-TASK-DETAIL.md §CGF1-S0102`:
  - Removed step 2 (create Standalone project).
  - Renumbered remaining steps.
  - Added "Runner-only launch" callout box.
  - Removed "Standalone binary runs without exception" success condition.
- `CGF-1-TASK-DETAIL.md §CGF1-S0104`:
  - Removed "and `Bagira.CGF.Standalone` process project" from step 1.

---

## Part B — CGF1-S0201: BFS Transition Planner

### Implementation

**File:** `Bagira.Orchestrator/TransitionPlanner.cs` (new)

**Classes added:**
- `ISysOpStep` — abstract base for step types.
- `TransitionStep(DSMState)` — instructs cluster to transition to a target state.
- `OperationStep(SysOpType, string)` — out-of-band operation appended after the final transition (e.g. ReplaySeek).
- `TransitionPlanner` — BFS planner with full adjacency and `CalculateShortestPath`/`PlanTrajectory` APIs.

**Adjacency note (design errata):** `CGF-1-DESIGN.md §4.1` lists `RunningEdit → LoadingLive` in the adjacency, but the normative trajectory examples (`RunningEdit → RunningLive` = 4 steps) require this edge to be *absent* (BFS would find the 2-step path via `RunningEdit → LoadingLive → RunningLive` otherwise). Removed from planner adjacency. DEBT row added for design §4.1 correction in BATCH-05.

**Payload encoding:** `PlanTrajectory` accepts:
- Plain integer string `"41"` → target state, no seek hint.
- JSON object `{"TargetState":41,"TargetWallTicks":999000}` → target state + seek hint.

When target is `RunningReplay` and `TargetWallTicks` is present, an `OperationStep(ReplaySeek)` is appended.

**Impossible path:** `DSMState.Degraded` has no outgoing planning edges; any request from or to `Degraded` throws `InvalidOperationException` with both state names. The original task-detail entry cited `RunningDryRun → RunningReplay` as the impossible case — BFS proves that path is actually reachable in 6 steps. Task detail updated.

### DrillMaster wiring

**File:** `Bagira.Orchestrator/DrillMaster.cs`

- Added `_planner: TransitionPlanner` field.
- Added `_currentDsmState: DSMState` field (tracks current published state; initialized to `Standby`).
- `ProcessSysOpRequests`: for `SysOpType.TransitionState` requests, calls `_planner.PlanTrajectory(...)`. On `InvalidOperationException` (unreachable path) responds with `OpStatus.Failure` and continues. On success, populates `TargetDsmState` and `TotalSteps` from the planned trajectory.

### Tests

**File:** `Bagira.Orchestrator.Tests/TransitionPlannerTests.cs` (new)

8 tests, all pure unit (no DDS, no ECS), part of the non-parallel `OrchestratorTests` collection:

| Test | Status |
|------|--------|
| `StandbyToLoadingEdit_Produces_SingleStep` | ✅ Pass |
| `RunningLiveToRunningReplay_Produces_FourSteps` | ✅ Pass |
| `RunningLiveToRunningReplayWithSeek_Produces_FiveSteps` | ✅ Pass |
| `RunningEditToRunningLive_Produces_FourSteps` | ✅ Pass |
| `ImpossibleRequest_ThrowsInvalidOperationException` (Degraded → RunningLive) | ✅ Pass |
| `SameState_ReturnsEmptyQueue` | ✅ Pass |
| `RunningDryRunToRunningReplay_Produces_SixSteps` (documents design error) | ✅ Pass |
| `TransitionToDegraded_ThrowsInvalidOperationException` | ✅ Pass |

---

## Test results

| Suite | Before | After |
|-------|--------|-------|
| `Bagira.Orchestrator.Tests` | 5 pass (BATCH-03) | **13 pass** |
| `Bagira.Runner.Tests` | 112 pass | 112 pass |
| `Bagira.SimHost.Tests` | 360 pass | 360 pass |
| `Bagira.IG.Tests` | 429 pass | 429 pass |

Solution build: **0 errors, 0 new warnings**.

---

## Task detail changes

- `CGF-1-TASK-DETAIL.md §CGF1-S0105`: updated `SurvivingNodes` test to reflect broadcast limitation; added Phase 1 note.
- `CGF-1-TASK-DETAIL.md §CGF1-S0201`: updated "impossible" test case from `RunningDryRun → RunningReplay` (incorrect) to `Degraded → RunningLive`; added note explaining BFS finds 6-step reachable path; updated payload encoding to `{"TargetState":41,"TargetWallTicks":N}` form.

---

## Deferred items / DEBT rows added

| Priority | Description | Target |
|----------|-------------|--------|
| P3 | `TransitionPlanner` payload protocol: unify on JSON-object form in Phase 2+ | CGF-1-BATCH-05 |
| P3 | `SurvivingNodes` per-node DDS isolation: requires keyed per-node topic keys | CGF-1-BATCH-05 |
| P3 | `CGF-1-DESIGN.md §4.1` adjacency still lists `RunningEdit → LoadingLive` erroneously | CGF-1-BATCH-05 |

---

## Files changed

| File | Change |
|------|--------|
| `Bagira.Orchestrator/ClusterConfiguration.cs` | A.1: fail-fast on invalid config file |
| `Bagira.Orchestrator/NodeHealthProfile.cs` | A.4: add CpuUsagePercent, RamUsedBytes |
| `Bagira.Orchestrator/DrillMaster.cs` | A.4: propagate CPU/RAM from heartbeat; B: add planner field + wiring |
| `Bagira.Orchestrator/TransitionPlanner.cs` | **NEW** — BFS planner + step types |
| `Bagira.Orchestrator.Tests/DrillMasterBootstrapTests.cs` | A.2: TransitionState payload; A.3: broadcast-limitation doc-comment |
| `Bagira.Orchestrator.Tests/TransitionPlannerTests.cs` | **NEW** — 8 planner tests |
| `Bagira.Runner/Services/OrchestratorSubsystem.cs` | A.4: CPU%/RAM + ACK latency columns |
| `.dev/cgf-1/CGF-1-DESIGN.md` | A.5: remove Standalone rows; fix §3.2 sentence |
| `.dev/cgf-1/CGF-1-TASK-DETAIL.md` | A.3: broadcast note; A.5: remove Standalone; B: fix S0201 impossible test case + payload encoding |
| `.dev/DEBT-TRACKER.md` | Close BATCH-03 items; add 3 new BATCH-04 debt rows |

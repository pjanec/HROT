# BATCH-OFX-02: navig-2 Fixes

**Batch Number:** BATCH-OFX-02  
**Tasks:** OFX-001, OFX-010, OFX-011, OFX-018, OFX-019, OFX-024, OFX-025  
**Source:** `.dev/other-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/other-fixes-1/TASK-TRACKER.md`  
**Priority:** HIGH -- OFX-001 (Hybrid dead code), OFX-019 (FollowPath stuck Running), OFX-018 (time budget missing)  
**Dependencies:** BATCH-OFX-01 (done)

---

## Onboarding & Workflow

This batch covers all navig-2 defects:
1. **Algorithm** (OFX-001, OFX-010, OFX-018, OFX-019): Real behavior bugs in navigation backend selection and execution
2. **Spec-drift** (OFX-011, OFX-024): API surface deviations from design
3. **SC-anchor** (OFX-025): Missing/weak navigation test coverage

Work in priority order: OFX-001, OFX-019, OFX-018, OFX-010, OFX-011, OFX-024, OFX-025.

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/other-fixes-1/TASK-DETAIL.md` -- all 7 tasks
2. **Navigation Design DD:** Find `Navigation_Design_v2_0.md` via graph search (referenced in tasks)
3. **Navigation Fake DD:** Find `DD-Fake-Nav` via graph search
4. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
5. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture`. Find symbols with `mcp_codebase-memo_search_graph`.

---

## MANDATORY WORKFLOW (per task, in order)

For **each task**:
1. **Define success condition** before implementing
2. **Implement the fix**
3. **Write tests** -- behavioral verification
4. **Run all tests** -- ALL must pass
5. **Fix failures at root cause**
6. Only then move to next task

---

## Tasks

### Task 1: OFX-001 -- Nav backend auto-select checks only start point; Hybrid is dead code (HIGH)

**Task Definition:** [OFX-001](../TASK-DETAIL.md#ofx-001----nav-backend-auto-select-checks-only-the-start-point-hybrid-is-dead-code)

**Success Condition:** `SelectBackend` checks both start AND end proximity to the road network. Mixed proximity (one near, one far) returns `Hybrid`. Tests verify the three-case routing.

**What to do:**
1. Read `PathfindingSolverSystem.SelectBackend`
2. Add end-point proximity check; add Hybrid case for mixed proximity
3. Write tests: both near -> RoadGraph; mixed -> Hybrid; both far -> Navmesh

**Tests Required:**
- Both endpoints near road -> `NavRoadGraph`
- Mixed endpoints -> `Hybrid`
- Both endpoints far -> `Navmesh`

---

### Task 2: OFX-019 -- FollowPathExecutor doesn't map FailedBlocked to Failure (MEDIUM)

**Task Definition:** [OFX-019](../TASK-DETAIL.md#ofx-019----followpathexecutor-doesnt-map-failedblocked-to-failure---stuck-running-forever)

**Success Condition:** `FollowPathExecutor` maps `FailedBlocked` -> Failure (not Running). Tests verify the state mapping for all outcome codes.

**What to do:**
1. Read `FollowPathExecutor.Execute` and `MoveToExecutor.Execute`
2. Add `FailedBlocked` -> Failure case to `FollowPathExecutor`
3. Write a test: set outcome to FailedBlocked; assert executor returns Failure (not Running)

**Tests Required:**
- `FailedBlocked` outcome -> Failure result
- Existing outcomes still map correctly

---

### Task 3: OFX-018 -- ReplanTimeBudget guard absent (MEDIUM)

**Task Definition:** [OFX-018](../TASK-DETAIL.md#ofx-018----replantimebudget-guard-absent-replan-bounded-only-by-maxreplans-count)

**Success Condition:** `MoveToParams` has a `ReplanTimeBudget` field; the replan guard checks both count and elapsed time. Tests verify that replan stops when time budget is exceeded even if count limit is not reached.

**What to do:**
1. Read `MoveToParams` and `NavigationExecutionSystem.Execute`
2. Add `ReplanTimeBudget` (float, seconds) to `MoveToParams`
3. Track elapsed time since last replan; stop when `elapsed >= ReplanTimeBudget`
4. Write test: set small `ReplanTimeBudget`; verify replanning stops after budget expires

**Tests Required:**
- Replan stops when `ReplanTimeBudget` expires (before count limit)

---

### Task 4: OFX-010 -- FakeDtCrowdProvider separation threshold/formula deviate (MEDIUM)

**Task Definition:** [OFX-010](../TASK-DETAIL.md#ofx-010----fakedtcrowdprovider-separation-threshold--formula--nearbyagentcount-range-deviate-from-design)

**Success Condition:** Separation force uses the designed threshold `(combinedR*1.5)^2`; `NearbyAgentCount` uses `(combinedR*4)^2`. Push formula uses `delta.Normalized/max(sqrt(d),0.01)*SeparationWeight`. Tests verify agents in the 1.0-1.5x band receive separation force.

**What to do:**
1. Read `FakeDtCrowdProvider.Update`
2. Fix the two thresholds and the push formula per DD-Fake-Nav §4.3
3. Write test: two agents at 1.2x combined radius; verify separation force applied and `NearbyAgentCount > 0`

**Tests Required:**
- Agents at 1.2x combined radius receive separation force
- `NearbyAgentCount` counts agents in 4x radius

---

### Task 5: OFX-011 -- FakeNavmeshProvider.BlockPolygon is layer-agnostic (MEDIUM)

**Task Definition:** [OFX-011](../TASK-DETAIL.md#ofx-011----fakenavmeshproviderblockpolygon-is-layer-agnostic-design-requires-per-layer-scoping)

**Success Condition:** `BlockPolygon(int polygonId, NavLayerMask layer)` blocks only in the specified layer. Tests verify blocking one layer doesn't affect another.

**What to do:**
1. Add `NavLayerMask layer` parameter to the interface and implementation
2. Scope the block to the specified layer
3. Write test: block polygon in Infantry layer; assert Vehicle-layer traversal still works

**Tests Required:**
- Blocking in Infantry layer doesn't block Vehicle layer

---

### Task 6: OFX-024 -- IFakeNavmeshProviderTestApi.BumpVersion missing (LOW)

**Task Definition:** [OFX-024](../TASK-DETAIL.md#ofx-024----ifakenavmeshprovidertestapibumpversion-missing)

**Success Condition:** `BumpVersion(BoundingBox2D, NavLayerMask)` exists on the interface and implementation. Tests verify it increments the version without blocking.

**What to do:**
1. Add `BumpVersion(BoundingBox2D, NavLayerMask)` to interface and impl
2. Write test: call `BumpVersion`; assert version incremented

**Tests Required:**
- `BumpVersion` increments navmesh version

---

### Task 7: OFX-025 -- FakeDtCrowd separation test asserts only NearbyAgentCount (LOW)

**Task Definition:** [OFX-025](../TASK-DETAIL.md#ofx-025----fakedtcrowd-separation-test-asserts-only-nearbyagentcount-not-velocity-divergence)

**Success Condition:** Tests verify that agents crossing paths actually diverge (velocity changes) and that agents surrounded by stationary agents have near-zero velocity. Specific position/velocity assertions.

**What to do:**
1. Read existing `FakeDtCrowdProviderTests`
2. Add tests for crossing-paths velocity divergence and surrounded-agent near-zero velocity

**Tests Required:**
- Two agents crossing paths: velocities diverge (min separation checked)
- Agent surrounded by three stationary agents: velocity near zero

---

## Quality Standards

- **OFX-001**: Must test all three cases (RoadGraph, Hybrid, Navmesh) with distinct endpoint positions
- **OFX-025**: Must assert actual velocity/position values, not just `NearbyAgentCount > 0`

## Report

Write report to:
`d:\WORK\IOS-IG-SimHost-FDP\.dev\other-fixes-1\reports\BATCH-OFX-02-REPORT.md`

## Workspace Root
`d:\WORK\IOS-IG-SimHost-FDP`

# SIM-BATCH-06: Integration Testing (Phase S6)

**Batch Number:** SIM-BATCH-06  
**Tasks:** TASK-S6.1, TASK-S6.2, TASK-S6.3  
**Phase:** S6  
**Estimated Effort:** 24 hours (3.0 days)  
**Priority:** HIGH  
**Dependencies:** Phase S5 (Main Application Shell)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! With the main simulation application up and running locally, Phase S6 focuses exclusively on End-to-End (E2E) integration testing. You will spin up actual simulated environments inside your test runner, passing DDS payloads through network adapters automatically!

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/design/TASK-DETAILS-SIMHOST.md#phase-s6-integration-testing-3-days`

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost.Integration.Tests/` (Create this project if it doesn't exist yet, or run integration tests inside `Bagira.SimHost.Tests/Integration/`)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-06-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-06-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Run Integration Test → **Passes** ✅
2. **Task 2:** Implement → Run Integration Test → **Passes** ✅
3. **Task 3:** Implement → Run Integration Test → **Passes** ✅

**DO NOT** move to the next task until:
- ✅ Current test written and evaluates correctly
- ✅ Test framework cleans up processes correctly

---

## Context

Inside `Bagira.SimHost.Integration.Tests/`, we need test classes that spin up the *entire* backend locally with `DomainParticipant` objects mirroring real networking IO, communicating via `.SendCreateRequest(..)` simulating our IOS/IG clients.

---

## 🎯 Batch Objectives
- Define the network simulation client test host.
- Execute full network entity payload request bounds tests.
- Evaluate vehicle logic updates against network translation over time.
- Capture simulated performance stability overhead.

---

## ✅ Tasks

### Task 1: Test Entity Creation Flow (TASK-S6.1)

**File:** `Bagira.SimHost.Integration.Tests/EntityCreationFlowTests.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s61-test-entity-creation-flow)

**Requirements:**
1. Re-create the `SimHost` node execution pipeline within a standalone asynchronous mock IOS client runner. (Refer to the instructions for sample setups utilizing `SimHostInstance`)
2. Publish `CreateEntityRequest` simulating an IOS node.
3. Assert that you receive `CreateEntityAck` inside a defined timeout via a spawned `EntityMaster` DDS topic.

---

### Task 2: Test Mission Execution (TASK-S6.2)

**File:** `Bagira.SimHost.Integration.Tests/MissionExecutionFlowTests.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s62-test-mission-execution)

**Requirements:**
1. Wait for `EntityCreationFlow` ACK inside your test runtime.
2. Publish `EntityMission` pointing the network entity to `MoveToLocation`.
3. Start the node loop for 10 seconds locally.
4. Capture `GeoSpatial` read points verifying the vehicle was translated accurately over frame ticks.

---

### Task 3: Performance Testing (TASK-S6.3)

**File:** `Bagira.SimHost.Integration.Tests/PerformanceTests.cs`

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s63-performance-testing)

**Requirements:**
1. Modify your test wrapper logic to allow capturing basic frame time elapsed.
2. Spin up 100 Entities.
3. Assure application stays consistently above 58 FPS on average. (Min 55!)

---

## 📊 Report Requirements

**Q1 Performance Bottlenecks:** During the `S6.3` test, were there any major blockers parsing `SimHost` loops inside MS Test contexts natively? Are metrics reasonably stable?
**Q2 Integration Harness:** How complex was creating `MockIOSClient` utilizing the raw `DomainParticipant`? Should we extract this to a broader `DDS.TestMocks` library in the future?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-S6.1, S6.2, S6.3 completed.
- [ ] Pipeline tests entity creations natively resolving through ECS and Network loops.
- [ ] Vehicle movement physics execute cleanly against `EntityMission` data structures within MS test boundaries.
- [ ] Performance metrics evaluate properly.
- [ ] Report submitted via markdown file.

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md) - See Phase S6

# TwoAck-BATCH-01: Two-ACK Entity Lifecycle Pattern (Full Pipeline)

**Batch Number:** TwoAck-BATCH-01
**Tasks:** TWOACK-DM001, TWOACK-DM002, TWOACK-DM003, TWOACK-SH001, TWOACK-SH002, TWOACK-SH003, TWOACK-IOS001, TWOACK-IOS002, TWOACK-IOS003, TWOACK-IOS004
**Phase:** Complete Pipeline (DataModel, SimHost, IOS Adapter)
**Estimated Effort:** ~12-14 hours
**Priority:** HIGH
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch introduces the comprehensive Two-ACK pattern requested in the Two-ACK refactoring design. The problem we are addressing is the "half-baked entity" behavior, where an entity receives a generic `CreateEntityAck` too early, allowing operators to manipulate it even when the core lifecycle hasn't fully propagated.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Design Document:** `docs/two-ack/TWOACK-DESIGN.md` - Read to understand the underlying architecture and the 3 Phases.
3. **Tracker:** `docs/two-ack/TWOACK-TASK-TRACKER.md` - High-level completion statuses for reference.
4. **Task Definitions:** `docs/two-ack/TWOACK-TASK-DETAIL.md` - Detailed specifications containing conditions of success for each individual task you will execute.

### Source Code Location
- **Primary Work Area (DataModel):** `Hrot.NED`
- **Primary Work Area (SimHost):** `Hrot.SimHost`
- **Primary Work Area (IOS):** `Hrot.ClusterRunner` and `Hrot.ExCon`
- **Test Projects:** `Hrot.NED.Tests`, `Hrot.SimHost.Tests`, `Hrot.IG.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/TwoAck-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/TwoAck-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (DataModel):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (SimHost):** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3 (IOS):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task group until:
- ✅ Current task group implementation complete
- ✅ Current task group tests written
- ✅ **ALL tests passing** (including previous tests in the solution)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures. DataModel dictates SimHost semantics, and SimHost semantics dictate IOS UI processing.

---

## Context

The batch encapsulates the complete end-to-end delivery of the Two-ACK synchronization flow pattern. The fundamental driver here is maintaining strict single-responsibility boundaries. We will define a `DeleteEntityRequest` and expand `CreateUpdateDeleteEntityAck` so that `Hrot.ExCon` correctly halts UI modifications using `ImGui` locks while intermediate transactions are executing.

FDP components are left entirely unaffected by any of this and must remain untouched. 

---

## 🎯 Batch Objectives
Implement the complete Two-ACK entity logic spanning the Data Model, SimHost State Observer logic, and the IOS client integration points. Deliver fully-tested code confirming behavior correctness and exact edge cases around error states.

---

## ✅ Tasks

### Task Group 1: Data Model Unification
**Target Files:** `Hrot.NED/GenericMessages.cs`
**Scope:** Set up the DDS Contracts for `DeleteEntityRequest` & `CreateUpdateDeleteEntityAck` along with the Enum refactor.

- **[TWOACK-DM001] Add `DeleteEntityRequest` struct**
  - **Details:** See [TWOACK-DM001 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-dm001--add-deleteentityrequest-to-datamodel)
- **[TWOACK-DM002] Rename `SstErrorCode` to `SstStatusCode`**
  - **Details:** See [TWOACK-DM002 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-dm002--rename-ssterrorcode-to-sststatuscode)
- **[TWOACK-DM003] Expand `CreateUpdateDeleteEntityAck` and Retire `CreateEntityAck`**
  - **Details:** See [TWOACK-DM003 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-dm003--expand-createupdatedeleteentityack-and-retire-createentityack)

### Task Group 2: SimHost Two-ACK Pipeline
**Target Files:** `Hrot.SimHost/Systems/SstRequestFinalizationSystem.cs`, `CreateEntityRequestSystem.cs`, `DeleteEntityRequestSystem.cs`
**Scope:** Build the state tracking components that hook into FDP transitions locally and correctly map `IsAlive` changes into the expanded acknowledgement payloads.

- **[TWOACK-SH001] Create `SstRequestFinalizationSystem`**
  - **Details:** See [TWOACK-SH001 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-sh001--create-sstrequestfinalizationsystem)
- **[TWOACK-SH002] Update `CreateEntityRequestSystem` for Two-ACK**
  - **Details:** See [TWOACK-SH002 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-sh002--update-createentityrequestsystem-for-two-ack)
- **[TWOACK-SH003] Create `DeleteEntityRequestSystem`**
  - **Details:** See [TWOACK-SH003 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-sh003--create-deleteentityrequestsystem)

### Task Group 3: IOS Client Adaptation
**Target Files:** `Hrot.ClusterRunner/Services/IosSubsystem.cs`, `Hrot.ExCon/Services/DdsEventIngressHandlers.cs`, `IosLogic.cs`, `MissionPanel.cs`, `ContextMenuLogic.cs`
**Scope:** Update the operator view to cleanly interpret and manage Two-ACK signals globally inside the ImGui framework. Establish explicit success mapping, or failure dismissible alerts.

- **[TWOACK-IOS001] Update IOS Ingress Pipeline**
  - **Details:** See [TWOACK-IOS001 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-ios001--update-ios-ingress-pipeline)
- **[TWOACK-IOS002] Rewrite `ProcessEntityCreationAcks` for Two-ACK State Machine**
  - **Details:** See [TWOACK-IOS002 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-ios002--rewrite-processentitycreationacks-for-two-ack-state-machine)
- **[TWOACK-IOS003] Lock UI for Pending Entities**
  - **Details:** See [TWOACK-IOS003 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-ios003--lock-ui-for-pending-entities)
- **[TWOACK-IOS004] Surface Explicit Creation Errors to Operator**
  - **Details:** See [TWOACK-IOS004 Definition](docs/two-ack/TWOACK-TASK-DETAIL.md#twoack-ios004--surface-explicit-creation-errors-to-operator)

---

## 🧪 Testing Requirements

The exact Success Conditions defined for each individual task explicitly dictate the Unit Test patterns.
See `docs/two-ack/TWOACK-TASK-DETAIL.md` specific implementation constraints.
Test behavior via exact status codes mapping locally vs the DDS pipeline inputs.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value" or assert purely structural definitions without asserting logical flows.
- **REQUIRED:** Tests that verify actual behavior, e.g., verifying `ImGui.BeginDisabled()` was wrapped over interaction scopes during InProgress sequences, or that explicit mock callbacks are invoked.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them.
- **REQUIRED:** Document design decisions YOU made beyond the spec.
- **REQUIRED:** Share insights on code quality and improvement opportunities.
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation.

---

## 📊 Report Requirements

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed in the local `_tracked` hashing or the ImGui UI rendering steps?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task Group 1 (DataModel) completed and compiling completely warning free. Tests proving `StatusCode` and structs mapping.
- [ ] Task Group 2 (SimHost) completed, routing correctly between local observer loops. Validated locally via `IsAlive` scenarios.
- [ ] Task Group 3 (IOS) UI bindings configured successfully showing proper block interactions or visual alerts.
- [ ] All required tests defined in the Task Details documentation are passing. 
- [ ] Report submitted answering all insight queries.

---

## 📚 Reference Materials
- **Task Tracker:** `docs/two-ack/TWOACK-TASK-TRACKER.md` - Overall tracking layout.
- **Design Layout:** `docs/two-ack/TWOACK-DESIGN.md` - Core architecture guidelines.
- **Task Specifics:** `docs/two-ack/TWOACK-TASK-DETAIL.md` - Detailed spec requirements.

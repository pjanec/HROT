# IOS-BATCH-03: IOS Application Shell

**Batch Number:** IOS-BATCH-03  
**Tasks:** IOS.8.1 (IOS Main Logic), IOS.8.2 (IOS Program & CLI)  
**Phase:** IOS-P8 (Application Shell)  
**Estimated Effort:** ~16 hours  
**Priority:** HIGH  
**Dependencies:** IOS-BATCH-02, SHARED Components (P2, P3, P4)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Great progress so far! BATCH-02 successfully yielded our abstract Operator panels. Your assignment in BATCH-03 is to assemble the **IOS Application Shell** (Phase P8). This involves bridging the backend logic (`IosLogic`) with the standalone command-line entrypoint (`Program`) and orchestrating the primary `Raylib` event loop.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Context for the overall goal
3. **Task Details:** `docs/design/TASK-DETAILS-IOS.md` - Phase P9 Application Shell mapping (Note: P8 tasks correspond to section P9 in the design doc, tasks P9.1 and P9.2)
4. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - Emphasize memory and loop allocations!
5. **Debt Tracker:** `.dev-workstream/IOS-DEBT-TRACKER.md` - Please account for IOS-DEBT-034 in your implementation.

### Source Code Location
- **Primary Work Area:** `Bagira.IOS/`
- **Solution File:** `IOS-IG-SimHost.sln`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IOS-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IOS-BATCH-03-QUESTIONS.md`

---

## Context

This batch wires the abstract UI panels to the live `IDerRepo` and encapsulates the application into an executable.

**Related Tasks:**
- Task IOS.8.1: [IOS Main Logic](docs/design/TASK-DETAILS-IOS.md#p91-ios-main-logic-1-day) - Implements `IosLogic.cs`, acting as the brain and network traffic cop.
- Task IOS.8.2: [IOS Program & CLI](docs/design/TASK-DETAILS-IOS.md#p92-ios-program--cli-1-day) - Implements `Program.cs` and `IosMock.cs` encapsulating Raylib logic and arguments.

---

## 🎯 Batch Objectives
- Architect `IosLogic` as the state holder bridging the interaction paradigms introduced in BATCH-01 (e.g., handling placement configurations and context IDs).
- Synchronize network egress and ingress, acknowledging the threading debt from BATCH-02. **(Corrective action required inline)**
- Construct the main entry-point `Program.cs` and the update/rendering orchestrator `IosMock.cs`.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Tasks

### Corrective Task 0 (IOS-DEBT-034)
**Description:** From BATCH-02 review, `InteractionPanel.AddLog` is not thread-safe and could be invoked heavily from asynchronous DDS operations. Address this by front-loading a `ConcurrentQueue` strategy either directly within `InteractionPanel` OR inside `IosLogic` that subsequently drains onto the main thread prior to drawing UI panels natively. 

### Task 1: IOS Main Logic (IOS.8.1)
**Files:** `Bagira.IOS/IosLogic.cs`, `Bagira.IOS/IIosLogic.cs` (Expand if necessary)
**Task Definition:** See [TASK-DETAILS-IOS.md P9.1](docs/design/TASK-DETAILS-IOS.md#p91-ios-main-logic-1-day)

**Description:** Implement `IosLogic` conforming to `IIosLogic`.
**Requirements:** Register topics on the underlying `IDerRepo` cleanly. Establish the primary tracking for `_activeContextId` allowing map clicks to trigger localized spawn commands (with the `TkbType`). Process asynchronous events by explicitly calling `Poll` and `Flush`. 

**Tests Required:**
- ✅ Verify Click processing drops invalid configurations or mismatched Context IDs.
- ✅ Assert that `StartPlacementMode` emits the appropriately formatted patch mapped to the ID parameter.

### Task 2: IOS Program & CLI (IOS.8.2)
**Files:** `Bagira.IOS/Program.cs`, `Bagira.IOS/IosMock.cs`
**Task Definition:** See [TASK-DETAILS-IOS.md P9.2](docs/design/TASK-DETAILS-IOS.md#p92-ios-program--cli-1-day)

**Description:** Setup standard execution and argument parsing. 
**Requirements:** Connect the application lifecycle over to Raylib's infinite loop structure. Wire the panels rendered in preceding batches using `IosMock` to inject references natively. Capture `--domain` and `--node` parameters functionally.

**Tests Required:**
- ✅ Write basic lifecycle bounds verifying `Update(dt)` behaves sanely without crashing null references. Ensure `IosMock` coordinates updates downwards to `IosLogic`.

---

## 🧪 Testing Requirements

Since Raylib interactions require a physical window to perform adequately, you must isolate non-UI behavior extensively. Ensure all network logic is completely encapsulated within `IosLogic`, bypassing CLI-specific static references natively. Follow standard `Moq` patterns simulating MapClick and Sync loops. Let the tests prove thread behavior for `IOS-DEBT-034`.

---

## 📊 Report Requirements

Upon completion, generate `.dev-workstream/reports/IOS-BATCH-03-REPORT.md` answering the following context questions:

**Developer Insights**
**Q1:** What mechanisms did you employ to handle the safe concurrent draining of the Event Log (DEBT-034)?
**Q2:** Did you identify edge cases related to Raylib lifecycle shutdown or orphaned dependencies while implementing `IosMock.cs`?
**Q3:** The original spec lacks clarity around the lifetime of the UI Panels versus the `IosMock`. How did you wire their initialization contexts?
**Q4:** Were there any network serialization disparities between `MapClickEvent` and the creation payloads structured?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 1 and 2 implemented mapping exactly to the intended Main/IosLogic specs.
- [ ] All required tests passing without superficial "shallow testing". 
- [ ] Program is theoretically executable as a Standalone Raylib CLI window without immediately terminating unexpectedly.
- [ ] Report submitted addressing the questionnaire context.

---

## ⚠️ Common Pitfalls to Avoid
- **Hiding `Poll`/`Flush`**: If `IosLogic.Update()` doesn’t properly trigger Repo polling, network entities will never enter rendering pipelines!
- **Leaked Application Loops**: Avoid while(true) blocking unless cleanly utilizing the Raylib window exit flags properly. 

---

## 📚 Reference Materials
- **Task Tracker:** `docs/design/TASK-TRACKER.md`
- **Task Definitions:** `docs/design/TASK-DETAILS-IOS.md` (P9.1 and P9.2)
- **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`

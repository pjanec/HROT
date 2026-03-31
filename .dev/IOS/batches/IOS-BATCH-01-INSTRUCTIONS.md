# IOS-BATCH-01: IOS Mock Core Services

**Batch Number:** IOS-BATCH-01  
**Tasks:** Project Setup (P5.1, P5.2), IOS.6.1 (Request Transaction Manager), IOS.6.2 (Mission Editor Service), IOS.6.3 (Context Menu Logic)  
**Phase:** IOS-P5 (Setup) & IOS-P6 (Services)  
**Estimated Effort:** ~20 hours  
**Priority:** HIGH  
**Dependencies:** SHARED Components (P2, P3, P4)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the IOS Mock development! This is the first batch for the IOS application. You will handle project creation, setting up dependencies, and building the core backend services (Transaction Management, Mission Editing, and Context Menus) that support the future UI panels.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Context for the overall goal
3. **Task Details:** `docs/design/TASK-DETAILS-IOS.md` - Technical specifications (specifically Phases P5 and P6)
4. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - CRITICAL: read section 0 and 1 before coding regarding testing and magic numbers.
5. **Debt Tracker:** `.dev-workstream/DEBT-TRACKER.md` - Check for any relevant technical debt!

### Source Code Location
- **Primary Work Area:** `Hrot.ExCon/`
- **Solution File:** `IOS-IG-SimHost.sln`
- **Dependencies from:** `Hrot.NED/`, `Hrot.Map.Common/`, `Hrot.Map.Definitions/`, `FDP/FDP.Toolkit.DER/`, `FDP/FDP.Toolkit.Commands/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IOS-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IOS-BATCH-01-QUESTIONS.md`

---

## Context

This batch initializes the standalone IOS Mock application and builds the data management services required before UI development. 

**Related Tasks:**
- Task P5.1 & P5.2: [Project Setup](docs/design/TASK-DETAILS-IOS.md#phase-p5-project-setup-05-days) - Creates `Hrot.ExCon`
- Task IOS.6.1: [Request Transaction Manager](docs/design/TASK-DETAILS-IOS.md#p61-request-transaction-manager-05-days) - Correlates request/response IDs
- Task IOS.6.2: [Mission Editor Service](docs/design/TASK-DETAILS-IOS.md#p62-mission-editor-service-1-day) - Handles mission patching and optimism
- Task IOS.6.3: [Context Menu Logic](docs/design/TASK-DETAILS-IOS.md#p73-context-menu-logic-05-days) - Strategy pattern for dynamic map interactions

---

## 🎯 Batch Objectives
- Create the `Hrot.ExCon` console application.
- Build the core services for tracking backend DDS requests, optimistic mission locking, and contextual interactions.
- Ensure thorough test coverage verifying actual logic and error conditions.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Tasks

### Corrective Task 0 (IOS-DEBT-028)
**Description:** `TASK-TRACKER.md` is missing Phase P5 (Project Setup) which is defined in `TASK-DETAILS-IOS.md`. Ensure you complete Project Setup before proceeding with Phase P6. This corrects IOS-DEBT-028.

### Task 1: Project Setup (P5.1, P5.2)

**File:** `Hrot.ExCon/Hrot.ExCon.csproj`, `IOS-IG-SimHost.sln`
**Task Definition:** See [TASK-DETAILS-IOS.md Phase P5](docs/design/TASK-DETAILS-IOS.md#phase-p5-project-setup-05-days)

**Description:** Initialize the C# console app and add referenced libraries.
**Requirements:** Use specific `.NET` versions and paths. Follow the exact dependencies listed in the design document. No code apart from project scaffolds needed. Use precise project paths for existing dependencies (e.g. `Hrot.NED/Hrot.NED.csproj`).

### Task 2: Request Transaction Manager (IOS.6.1)

**Files:** `Hrot.ExCon/Services/IRequestTransactionManager.cs`, `Hrot.ExCon/Services/RequestTransactionManager.cs`
**Task Definition:** See [TASK-DETAILS-IOS.md P6.1](docs/design/TASK-DETAILS-IOS.md#p61-request-transaction-manager-05-days)

**Description:** Implement robust tracking of async requests with timeout capabilities.
**Requirements:** See design document for interface requirements. No magic numbers; if `5000` is the default timeout, extract it to a `public const double DefaultTimeoutMs = 5000;`. 

**Tests Required:**
- ✅ Verify pending requests are stored and retrieved successfully
- ✅ Verify `CheckTimeouts()` correctly flags stale items as failed 
- ✅ Verify `CompleteRequest()` accurately succeeds/fails requests

### Task 3: Mission Editor Service (IOS.6.2)

**Files:** `Hrot.ExCon/Services/IMissionEditorService.cs`, `Hrot.ExCon/Services/MissionEditorService.cs`
**Task Definition:** See [TASK-DETAILS-IOS.md P6.2](docs/design/TASK-DETAILS-IOS.md#p62-mission-editor-service-1-day)

**Description:** Service for reading current mission data, modifying it, and committing it using pessimistic/optimistic concurrency patterns using DDS commands.
**Requirements:** Follow interface from design document. Ensure proper asynchronous processing (`TaskCompletionSource`) and timeouts when `ACK`s are not received.

**Tests Required:**
- ✅ Valid `CommitMissionAsync` completes successfully when `OnAckReceived` triggers success.
- ✅ Timed-out requests result in an error message internally without throwing exceptions.
- ✅ Optimistic locking (validating version mismatches) is properly requested.

### Task 4: Context Menu Logic (IOS.6.3)

**Files:** `Hrot.ExCon/Logic/IContextMenuLogic.cs`, `Hrot.ExCon/Logic/ContextMenuLogic.cs`
**Task Definition:** See [TASK-DETAILS-IOS.md P7.3](docs/design/TASK-DETAILS-IOS.md#p73-context-menu-logic-05-days) - Note the numbering typo in the design doc!

**Description:** Emits `ContextActionsUpdate` to dynamically control IG menus when entity selection changes.
**Requirements:** Strategy pattern implementation as designated. Ensure correct payload definitions per the provided code snippet in the design rules.

**Tests Required:**
- ✅ Given a Strategy, the exact correct payload/menu items are returned and pushed on `SelectionChanged`.
- ✅ Ensure proper JSON serialization matches the expected API without faults.

---

## 🧪 Testing Requirements

- **All logic files** (`Managers`, `Services`, `Logic`) require comprehensive xUnit tests. Aim for ~15-20 minimum test cases.
- **Mocks**: Mock out the DDS writers and `IDerRepo` using Moq, or custom stubs appropriately to simulate incoming/outgoing events.
- **No shallow tests**: As per `.dev-workstream/guides/CODE-STANDARDS.md`, ensure tests evaluate real program scenarios. Check for offsets, exact internal states, and boolean correctness. Example: for timeouts, use mocked timers or simulated date-injection, don't rely on `Thread.Sleep()`.

---

## 📊 Report Requirements

Upon completion, generate `.dev-workstream/reports/IOS-BATCH-01-REPORT.md` answering the following context questions:

**Developer Insights**
**Q1:** What issues did you encounter during implementation of asynchronous `TaskCompletionSource` with DDS acknowledgments?
**Q2:** Did you spot any weak points or awkward areas in the provided `Hrot.ExCon` project layout or its references? What structure would you improve?
**Q3:** What design decisions did you make beyond the instructions? (e.g., regarding dependency injection or internal data structures handling threads)?
**Q4:** What edge cases did you discover around timeout handling that weren't thoroughly covered in the specification?
**Q5:** Are there any performance concerns or optimization opportunities you noticed in the `ContextMenuLogic` strategy implementation?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 1: `Hrot.ExCon` console application created and builds cleanly with all dependencies.
- [ ] Task 2: IOS.6.1 Transaction manager implements full interface with passing timeout tests.
- [ ] Task 3: IOS.6.2 Mission editor gracefully handles request timeouts and proper `ACK` resolutions.
- [ ] Task 4: IOS.6.3 Context menu logic successfully pushes correct tool items depending on arbitrary states.
- [ ] All required tests passing without superficial "shallow testing".
- [ ] Report submitted addressing the questionnaire.

---

## ⚠️ Common Pitfalls to Avoid
- **Hard-coded constants** – Refer directly to `.dev-workstream/guides/CODE-STANDARDS.md` rule #1.
- **Shallow testing** – Tests checking "is not null" instead of checking correct values or behavior.
- **Timeout tests** – Make sure your unit tests are deterministic when testing timeout scenarios.

---

## 📚 Reference Materials
- **Task Tracker:** `docs/design/TASK-TRACKER.md`
- **Task Definitions:** `docs/design/TASK-DETAILS-IOS.md` (P5, P6.1, P6.2, P7.3)
- **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`

# IOS-BATCH-02: IOS Mock UI Panels

**Batch Number:** IOS-BATCH-02  
**Tasks:** IOS.7.1 (Config Panel), IOS.7.2 (ORBAT Panel), IOS.7.3 (Mission Panel), IOS.7.4 (Event Log Panel), IOS.7.5 (Spawner Panel)
**Phase:** IOS-P7 (IOS UI Panels)  
**Estimated Effort:** ~24 hours  
**Priority:** HIGH  
**Dependencies:** IOS-BATCH-01, SHARED Components (P2, P3, P4)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! In BATCH-01, the core IOS services (Transaction Manager, Mission Editor, Context Menus) were successfully provisioned. In this batch, you will build the primary Raylib-based user interfaces (`rlImGui` panels) that the IOS operator will utilize.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - Context for the overall goal
3. **Task Details:** `docs/design/TASK-DETAILS-IOS.md` - Phase P7 mapping to UI Panels (specifically tasks named `P8.1` to `P8.5` within the doc).
4. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - CRITICAL: read carefully! No magic numbers or shallow tests!
5. **Debt Tracker:** `.dev-workstream/IOS-DEBT-TRACKER.md` - Track deferred structural items! Note that we use a project-specific debt tracker named `IOS-DEBT-TRACKER.md`.

### Source Code Location
- **Primary Work Area:** `Hrot.ExCon/Panels/`
- **Solution File:** `IOS-IG-SimHost.sln`
- **Tests Location:** `Hrot.ExCon.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IOS-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IOS-BATCH-02-QUESTIONS.md`

---

## Context

This batch constructs the visual operator interfaces (`rlImGui`) mapping to the underlying data layer. 

**Related Tasks:**
- Task IOS.7.1: [Configuration Panel](docs/design/TASK-DETAILS-IOS.md#p81-configuration-panel-05-days) - Implements `ConfigPanel.cs`
- Task IOS.7.2: [ORBAT Hierarchy Panel](docs/design/TASK-DETAILS-IOS.md#p82-orbat-hierarchy-panel-1-day) - Implements `OrbatPanel.cs` 
- Task IOS.7.3: [Mission Panel](docs/design/TASK-DETAILS-IOS.md#p83-mission-panel-1-day) - Implements `MissionPanel.cs`
- Task IOS.7.4: [Interaction Panel](docs/design/TASK-DETAILS-IOS.md#p84-interaction-panel-event-log-05-days) - Implements `InteractionPanel.cs`
- Task IOS.7.5: [Spawner Panel](docs/design/TASK-DETAILS-IOS.md#p85-spawner-panel-1-day) - Implements `SpawnerPanel.cs`

---

## 🎯 Batch Objectives
- Provide fully functional ImGui rendering structures for system configuration, rendering ORBAT entity lists, editing/jumping missions, viewing ingress/egress transactions, and spawning TKB targets.
- Abstract the `IosLogic` parameter where appropriate (or use interfaces) for decoupling unit testing from live UI state.
- Ensure thorough test coverage verifying state modifications caused by UI events.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task ...**

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Tasks

### Task 1: Configuration Panel (IOS.7.1)
**Files:** `Hrot.ExCon/Panels/ConfigPanel.cs`
**Requirements:** Implementation from snippet provided in TASK-DETAILS-IOS. Ensure patch formats generate proper JSON structures correlating to interaction types.

### Task 2: ORBAT Hierarchy Panel (IOS.7.2)
**Files:** `Hrot.ExCon/Panels/OrbatPanel.cs`
**Requirements:** Render an explicit command structure hierarchy based on `info.CommanderId`. Ensure recursive node generation handles depth gracefully and doesn't stack overflow on malformed cycles (guard for this!).

### Task 3: Mission Panel (IOS.7.3)
**Files:** `Hrot.ExCon/Panels/MissionPanel.cs`
**Requirements:** Interface directly with `MissionEditorService` built in BATCH-01. Display `MissionTask` array elements visually and provide buttons corresponding to sending jump/abort network commands.

### Task 4: Interaction Panel / Event Log (IOS.7.4)
**Files:** `Hrot.ExCon/Panels/InteractionPanel.cs`
**Requirements:** Visual diagnostic panel rendering up to a maximum number of history logs. Avoid allocations strictly in UI render logic—maintain an internal ring-buffer or tightly controlled list capacity.

### Task 5: Spawner Panel (IOS.7.5)
**Files:** `Hrot.ExCon/Panels/SpawnerPanel.cs`
**Requirements:** Must interrogate real/mock TKB catalogs. Make sure that filtering logic is case-insensitive. Provide controls defining the `eAffiliation` state correctly when `IosLogic.StartPlacementMode` is invoked.

---

## 🧪 Testing Requirements

Since testing ImGui renders directly can be complicated, **your unit tests must validate the class state and side-effects instead.**
- Verify that clicking "SEND CONFIG PATCH" actually calls `logic.SendConfigPatch(expectedJson)`.
- Mock the `IosLogic` or its underlying services to assert these behaviors. Do not simply assert the classes instantiate! Check rule #0 in `CODE-STANDARDS.md`.

---

## 📊 Report Requirements

Upon completion, generate `.dev-workstream/reports/IOS-BATCH-02-REPORT.md` answering the following context questions:

**Developer Insights**
**Q1:** How did you tackle UI event-driven state mutations without having the ImGui framework actively running in tests?
**Q2:** Did you notice any allocations or GC spikes occurring during the recursive rendering of the ORBAT tree? What choices did you make to minimize this?
**Q3:** What design decisions did you make beyond the UI layouts provided? Did you enhance or decouple the logic interface further?
**Q4:** Did you encounter any missing fields in the DataModels required for the Mission representations?
**Q5:** Are there any synchronization issues between external DDS reads and UI draw frames that must be considered when wiring the final Application Shell (IOS Phase 8)?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 1 through 5 implemented thoroughly and accurately modeling the layout mocks.
- [ ] All logical data manipulations invoked within these panels are thoroughly tested with reliable Stubs/Mocks.
- [ ] All required tests passing without superficial "shallow testing". Let the code assert!
- [ ] Report submitted addressing the questionnaire context.

---

## ⚠️ Common Pitfalls to Avoid
- **Hard-coded constants** – e.g. Maximum size limits for Event Log. Refer directly to `.dev-workstream/guides/CODE-STANDARDS.md` rule #1.
- **LINQ in Draw loops** – Avoid LINQ or heavy runtime allocations inside ImGui `Draw()` update frames! Pre-cache these structures.
- **Malformed Recursive ORBATS** – Add defensive checks so circular references (Unit A commands Unit B who commands Unit A) do not crash the view.

---

## 📚 Reference Materials
- **Task Tracker:** `docs/design/TASK-TRACKER.md`
- **Task Definitions:** `docs/design/TASK-DETAILS-IOS.md` (P8.1 to P8.5 mapping for Phase P7 logic)
- **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`

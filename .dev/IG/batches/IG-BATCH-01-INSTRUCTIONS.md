# IG-BATCH-01: Core Infrastructure & MapCanvas

**Batch Number:** IG-BATCH-01  
**Tasks:** IG.1.1 (Create Project), IG.1.5 (Create Test Project), IG.1.2 (Setup MapCanvas)  
**Phase:** IG1 (Core Infrastructure)  
**Estimated Effort:** ~9 hours (1.1 days)  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the first batch of the IG Mock implementation! This batch focuses on foundational setup. You will establish the primary application project, the test project, and implement the basic rendering canvas with camera controls using Raylib.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Code Standards:** `.dev-workstream/guides/CODE-STANDARDS.md` - MANDATORY READING (especially Test Quality, No Magic Numbers)
3. **Task Tracker:** `docs/design/TASK-TRACKER.md` - See overall Phase IG1
4. **Task Definitions:** `docs/design/TASK-DETAILS-IG.md` - Detailed specifications for tasks IG.1.1, IG.1.2, IG.1.5
5. **Design Document:** `docs/design/DESIGN-IG.md` - Technical specifications

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`
- **Test Project:** `Hrot.IG.Tests/`
- **Solution File:** `IOS-IG-SimHost.sln`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/IG-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/IG-BATCH-01-QUESTIONS.md`

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

## Context

This batch initializes the IG mock component, introducing the Raylib frontend window and mapping basic camera components. The infrastructure created here serves as the groundwork for future network integration and entity rendering batches.

**Related Tasks:**
- `docs/design/TASK-DETAILS-IG.md` - IG.1.1 (Create Project)
- `docs/design/TASK-DETAILS-IG.md` - IG.1.5 (Create Test Project)
- `docs/design/TASK-DETAILS-IG.md` - IG.1.2 (Setup MapCanvas)

---

## 🎯 Batch Objectives
- Create the main `Hrot.IG` project and link all prerequisite toolkit libraries from the FDP core structure.
- Initialize `Hrot.IG.Tests` project.
- Create an empty functioning Raylib window (`IgApplication`) with basic `MapCanvas` and `MapCamera` control functionalities.

---

## ✅ Tasks

### Task 1: IG.1.1 Create Hrot.IG Project

**File:** `Hrot.IG/Hrot.IG.csproj`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.1.1)

**Description:** Set up the IG project structure and link necessary DLLs/packages.
**Requirements:**
- Create `Hrot.IG` as a `net8.0` console application.
- Follow precisely the package list (`Raylib-cs`, `rlImGui`, `CycloneDDS.NET`, `NLog`) and internal project references outlined in the task definition.
- Set up the folder structure as defined (`Components/`, `Systems/`, `Tools/`, `Translators/`, `UI/`, `Adapters/`).
- Add the project to the central `IOS-IG-SimHost.sln`.

**Tests Required:**
- ✅ Verify the project compiles without warnings. 

---

### Task 2: IG.1.5 Create Hrot.IG.Tests Project

**File:** `Hrot.IG.Tests/Hrot.IG.Tests.csproj`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.1.5)

**Description:** Set up the test project.
**Requirements:**
- Create `Hrot.IG.Tests` matching `net8.0` test project template.
- Reference `Hrot.IG` project.
- Add to `IOS-IG-SimHost.sln`.

**Tests Required:**
- ✅ Ensure a default `[TestMethod]` passes.

---

### Task 3: IG.1.2 Setup MapCanvas with Camera Controls

**File:** `Hrot.IG/Program.cs` and `Hrot.IG/IgApplication.cs`  
**Task Definition:** See `docs/design/TASK-DETAILS-IG.md` (Task IG.1.2)

**Description:** Initialize Raylib 1600x900 window and construct MapCanvas/MapCamera.
**Requirements:**
- Implement the `IgApplication` layout exactly as referenced in the design document snippet.
- Include pan/zoom controls matching the specification:
  - Initial position: (5000, 5000) meters, Zoom: 0.5 (2 m/px)
  - Zoom limits: 0.01 to 5.0
  - Arrow keys pan (10 m/s), Middle mouse drag pan
  - Mouse wheel zoom (1.2x per tick), +/- keys zoom
- Present Debug information text on screen (Camera pos, zoom, mouse world coordinates).
- Ensure explicit constants are used for window sizes and zoom limits. Do not use magic numbers directly in the update logic without proper `const` definitions (`CODE-STANDARDS.md`).

**Tests Required:**
- ✅ **Unit Testing**: You must write logic tests verifying the constraints of `MapCamera` scaling and repositioning. Avoid just testing property presence—test the actual coordinate constraints logic.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I instantiate the class" or "assert true".
- **REQUIRED:** Tests that verify actual clamping behavior of camera constraints (e.g. attempting to zoom past max zoom clamps correctly).
- **Quality check:** Reference `.dev-workstream/guides/CODE-STANDARDS.md` section 0.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights in your report:

## Developer Insights

**Q1:** What issues did you encounter during the project structuring and Raylib initialization? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase or the `FDP.Toolkit.Vis2D` APIs while implementing the canvas? 

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider regarding camera inputs/handling?

**Q4:** Did you notice any performance concerns or optimization opportunities?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task IG.1.1 completed (Compiles successfully, all refs included).
- [ ] Task IG.1.5 completed (Test project functional).
- [ ] Task IG.1.2 completed (Raylib window opens, pan/zoom works natively, 60fps stable, debug overlay visible).
- [ ] `Hrot.IG.Tests` contains verification tests for behavior logic.
- [ ] All code conforms to `CODE-STANDARDS.md` limits (No Magic Numbers rule applied to window/zoom defaults).
- [ ] Developer Report submitted.

---

## ⚠️ Common Pitfalls to Avoid
- Forgetting to add projects properly to the main solution.
- Using incorrect input scaling algorithms for zoom or breaking panning scaling logic.
- Having "magic numbers" scattered in camera logic—extract them to named `const` fields or use configuration where appropriate.

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-IG.md` (See Tasks IG.1.1, IG.1.2, IG.1.5)
- **Standards:** `.dev-workstream/guides/CODE-STANDARDS.md`

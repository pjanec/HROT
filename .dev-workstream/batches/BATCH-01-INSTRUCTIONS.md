# BATCH-01: Infrastructure Validation

**Batch Number:** BATCH-01  
**Tasks:** P1.1, P1.2  
**Phase:** Phase 1: Infrastructure Validation (Shared Components)  
**Estimated Effort:** 1.5 days (12 hours)  
**Priority:** HIGH  
**Dependencies:** None  

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the IOS-IG-SimHost-FDP project. This is the first implementation batch, focusing on validating the existing codebase and infrastructure. Your goal is to ensure the development environment is stable and all existing tests pass before we start adding new features.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Tracker:** `docs/design/TASK-TRACKER.md` - See P1.1, P1.2 details
3. **Task Details:** `docs/design/TASK-DETAILS-SHARED.md` - See Phase 1 details (Sections P1.1, P1.2)
4. **Design Document:** `docs/design/DESIGN-SHARED.md` - For context on the shared architecture
5. **Edge Cases & Mitigations:** `docs/design/EDGE-CASES-AND-MITIGATIONS.md` - Review critical issues

### Source Code Location
- **Solution File:** `FDP/FDP.sln`
- **Tests Location:** Various test projects within the solution (see Task 2 details)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-01-QUESTIONS.md`

---

## Context

Before adding the new IOS, IG Mock, and SimHost components, we must ensure the `FDP` core framework is functionally correct. This batch validates the build process and existing test suites for ID allocation, TKB rendering, geographic transforms, and entity lifecycle.

**Related Tasks:**
- [P1.1](../docs/design/TASK-DETAILS-SHARED.md#task-p11-build-existing-fdp-solution) - Build FDP.sln
- [P1.2](../docs/design/TASK-DETAILS-SHARED.md#task-p12-run-existing-infrastructure-tests) - Run critical infrastructure tests

---

## 🎯 Batch Objectives
- Verify `FDP.sln` builds cleanly in both Debug and Release configurations.
- Validate core infrastructure components (ID allocation, TKB, Geo, Lifecycle) via existing unit tests.
- Identify and document any baseline failures to be fixed before feature work begins.

---

## ✅ Tasks

### Task 1: Build Existing FDP Solution (P1.1)

**File:** `FDP/FDP.sln` (EXISTING)  
**Task Definition:** See [TASK-DETAILS-SHARED.md](../docs/design/TASK-DETAILS-SHARED.md#task-p11-build-existing-fdp-solution)

**Description:** Open the main solution and verify it builds successfully without errors.

**Requirements:**
1. Open `FDP/FDP.sln` in Visual Studio.
2. Restore all NuGet packages.
3. Build the entire solution in **Debug** configuration.
4. Build the entire solution in **Release** configuration.
5. Fix any compilation errors if they occur. (Note: Warnings are acceptable for now but should be noted).
6. Verify all projects are loaded and compiled.

**Design Reference:** N/A (Infrastructure task)

**Tests Required:**
- ✅ Solution builds with 0 errors in Debug.
- ✅ Solution builds with 0 errors in Release.

---

### Task 2: Run Existing Infrastructure Tests (P1.2)

**File:** `Multiple Test Projects` (EXISTING)  
**Task Definition:** See [TASK-DETAILS-SHARED.md](../docs/design/TASK-DETAILS-SHARED.md#task-p12-run-existing-infrastructure-tests)

**Description:** Run specific test suites to validate the core framework functionalities required for the new components.

**Target Test Projects & Verification Scenarios:**

1. **ID Allocation Tests**
   - **Project:** `ModuleHost.Network.Cyclone.Tests`
   - **Focus:** `DdsIdAllocatorTests.cs`
   - **Verify:** Server allocates unique IDs, client correctly buffers blocks.

2. **TKB Database Tests**
   - **Project:** `FDP.Toolkit.Tkb.Tests` (Located in `FDP/Toolkits/FDP.Toolkit.Tkb.Tests`)
   - **Focus:** `TkbDatabaseTests.cs`
   - **Verify:** Template registration, retrieval, and requirement checks work.

3. **Geographic Transform Tests**
   - **Project:** `Fdp.Toolkit.Geographic.Tests`
   - **Focus:** `WGS84TransformTests.cs`
   - **Verify:** Lat/Lon ↔ Cartesian conversion accuracy (within 0.1m for 10km radius).

4. **Network Entity Map Tests**
   - **Project:** `FDP.Toolkit.Replication.Tests`
   - **Focus:** `NetworkEntityMapTests.cs`
   - **Verify:** ID mapping logic and graveyard cleanup.

5. **Entity Lifecycle Tests**
   - **Project:** `FDP.Toolkit.Lifecycle.Tests`
   - **Focus:** `EntityLifecycleModuleTests.cs`
   - **Verify:** Constructing → Active → TearDown state transitions.

**Instructions:**
1. Open Test Explorer.
2. Run all tests in the projects listed above.
3. Document any failures in your report.
4. If failures occur, investigate and attempt to fix them. If a fix is complex (> 2 hours), document it as a blocker/issue in the report instead of spending days debugging.

**Design Reference:** N/A (Infrastructure validation)

**Tests Required:**
- ✅ All ID allocation tests pass
- ✅ All TKB tests pass
- ✅ All geographic transform tests pass
- ✅ All network entity map tests pass
- ✅ All lifecycle tests pass

---

## 🧪 Testing Requirements
- **Pass Rate:** 100% of existing tests in the targeted projects must pass.
- **Exceptions:** If a test is fundamentally broken due to external dependencies or legacy issues unrelated to current work, disable it with `[Ignore]` and document the reason in the report.
- **New Tests:** No new tests are required for this batch (validation only).

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please answer the following in your report (`.dev-workstream/reports/BATCH-01-REPORT.md`):

**✅ Developer Insights:**
- **Q1:** Did you encounter any build errors? If so, how did you resolve them?
- **Q2:** Which tests (if any) failed initially? What was the cause?
- **Q3:** Are there any tests that seem flaky or unreliable?
- **Q4:** Did you notice any warnings or code quality issues that we should address later?
- **Q5:** Is the development environment setup smooth, or were there friction points?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `FDP.sln` builds successfully in Debug and Release modes.
- [ ] All specified test suites pass (ID, TKB, Geo, Replication, Lifecycle).
- [ ] A report is submitted detailing any fixes made or issues found.

---

## ⚠️ Common Pitfalls to Avoid
- **NuGet Cache:** If packages fail to restore, try clearing your local NuGet cache.
- **Do not ignore errors:** If the solution builds with errors, do not proceed to running tests. Fix the build first.
- **Environment:** Ensure you have the .NET SDK versions required by the solution installed.

---

## 📚 Reference Materials
- [TASK-DETAILS-SHARED.md](../docs/design/TASK-DETAILS-SHARED.md) - Section P1.1, P1.2
- [TASK-TRACKER.md](../docs/design/TASK-TRACKER.md)

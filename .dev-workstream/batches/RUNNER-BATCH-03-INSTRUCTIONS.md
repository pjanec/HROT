# RUNNER-BATCH-03: Subsystem Refactoring (Phase R2)

**Batch Number:** RUNNER-BATCH-03
**Tasks:** R2.1, R2.2, R2.3, R2.4, R2.5, R2.6, R2.7, R2.8, R2.9
**Phase:** R2 - Subsystem Refactoring
**Estimated Effort:** 24-30 hours
**Priority:** High
**Dependencies:** RUNNER-BATCH-02 complete

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to Phase R2! The Runner shell is built, and it's time to adapt all three existing subsystems (SimHost, IG, IOS) into embeddable components implementing the `ISubsystem` interface.

**The Goal:** Refactor all subsystems to function as libraries that can be embedded in `Runner.exe` or wrapped in thin standalone console applications (e.g. `Bagira.SimHost.Standalone`).

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream\README.md`
2. **Task Details:** `docs\design\TASK-DETAILS-RUNNER.md` — Phase R2 tasks (start at R2.1)
3. **Previous Review:** `.dev-workstream\reviews\RUNNER-BATCH-02-REVIEW.md`
4. **Code Standards:** `.dev-workstream\guides\CODE-STANDARDS.md`

### Architect Context
- **Architecture Review (2026-02-26):**
  - **SimHost:** Use `EntityRepository` + `ModuleHostKernel`. DO NOT USE `FdpWorld` or `CarKinemModule` (obsolete).
  - **IOS:** `DerRepo` takes no constructor arguments.
  - **Render Ownership:** `ISubsystem.DrawWorld()` is for Raylib only. `ISubsystem.DrawUI()` is for ImGui. Orchestrator manages `rlImGui.Begin()`.

### Source Code Location
- **Primary Work Areas:** `Bagira.SimHost`, `Bagira.IG`, `Bagira.IOS`
- **New Standalone App Projects:** `Bagira.SimHost.Standalone`, `Bagira.IG.Standalone`, `Bagira.IOS.Standalone`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream\reports\RUNNER-BATCH-03-REPORT.md`

---

## Context

Each subsystem needs an `ISubsystem` implementation class that delegates logic to the existing kernels, ensuring no infinite loops are built into initialization or updates. The `SubsystemOrchestrator` will execute the update loops and manage rendering. 

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **SimHost (R2.1 - R2.3):** Implement → Write tests → **ALL tests pass** ✅
2. **IG (R2.4 - R2.6):** Implement → Write tests → **ALL tests pass** ✅
3. **IOS (R2.7 - R2.9):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next subsystem until the current one works completely in both standalone and runner modes.

---

## 🎯 Batch Objectives

- Extract `SimHostSubsystem`, `IgSubsystem`, and `IosSubsystem` classes implementing `ISubsystem`.
- Create thin `Standalone` applications for backward compatibility.
- Ensure DDS domains and configurations wire properly.

---

## ✅ Tasks

### Task 1: Refactor SimHost Subsystem (R2.1, R2.2, R2.3)
**Task Definition:** See TASK-DETAILS-RUNNER.md (R2.1 - R2.3)

**Description:**
1. Extract `SimHostConfiguration.cs` and create `SimHostSubsystem.cs`.
2. Map `Initialize(config)` to create `EntityRepository` and `ModuleHostKernel`.
3. Create project `Bagira.SimHost.Standalone` with `Program.cs` utilizing CommandLineParser to run `SimHostSubsystem` manually.
4. Provide integration tests ensuring embeddability works!

### Task 2: Refactor IG Subsystem (R2.4, R2.5, R2.6)
**Task Definition:** Extrapolated from TASK-DETAILS-RUNNER.md (Similar to R2.1-R2.3)

**Description:**
1. Write `IgSubsystem.cs` that incorporates `IgApplication` logic without dominating the event loop.
2. Ensure IG delegates ImGui to `DrawUI()` and raylib to `DrawWorld()`.
3. Create `Bagira.IG.Standalone` wrapper.

### Task 3: Refactor IOS Subsystem (R2.7, R2.8, R2.9)
**Task Definition:** Extrapolated from TASK-DETAILS-RUNNER.md

**Description:**
1. Write `IosSubsystem.cs` utilizing DER architecture for state management.
2. Initialize `DerRepo` natively without constructor injection.
3. Ensure IOS panel logic operates completely in `DrawUI()`, without Raylib manipulation.
4. Create `Bagira.IOS.Standalone` wrapper.

---

## 🧪 Testing Requirements

- Maintain standalone application integration tests validating `--headless` execution for each subsystem.
- Tests validating proper initialization logic without crashing.
- **Ensure no infinite internal loops** from existing programs prevent orchestrator usage.

---

## 📊 Report Requirements

Follow standards strictly. Gather professional feedback:
- **Issues Encountered:** Problems scaling ImGui inside orchestrators?
- **Weak Points Spotted:** Any issues pulling out kernel loops?
- **Design Decisions Made:** Where do standalone wrappers reside in the directory structure?

---

## 📚 Reference Materials
- **Task Defs:** `docs/design/TASK-DETAILS-RUNNER.md`

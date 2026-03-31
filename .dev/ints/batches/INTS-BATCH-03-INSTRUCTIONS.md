# INTS-BATCH-03: Debug Instrumentation & End-to-End Validation

**Batch Number:** INTS-BATCH-03  
**Tasks:** CORRECTIVE-0, INTS-P3-011, INTS-P3-012, INTS-P3-013, INTS-P3-014  
**Phase:** Phase 3 - Debug Instrumentation & End-to-End Validation  
**Estimated Effort:** 8-12 hours  
**Priority:** HIGH  
**Dependencies:** INTS-BATCH-02 must be completed and merged.

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch finalizes the integration troubleshooting phase by establishing robust tracing across all DDS and event borders to provide full telemetry. Finally, we establish the automated end-to-end entity lifecycle regression test.

**⚠️ CRITICAL DIRECTIVE: ARCHITECTURAL PURITY ⚠️**
You have proven to be exceptionally fast, but we value **clean and elegant architecture over simple, dirty solutions.**
In the previous batch, a circular dependency was "solved" by invoking `BdcTkbCatalog` registration via `Reflection`. This is an unacceptable architectural workaround. Bypassing compile-time safety to meet an interface requirement is fragile. 

In this batch and all future batches, you MUST reconsider the assembly dependencies and apply robust architectural patterns (e.g. inverted dependencies, interfaces, shared contracts) instead of dirty hacks. If it feels like a hack, it is a hack, and it will be rejected.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs\design\TASK-DETAILS-Integration-Troubleshooting.md` - See detailed task specifications
3. **Design Document:** `docs\design\DESIGN-Integration-Troubleshooting.md` - Technical context
4. **Developer Guidance (Project Rules):** `.dev-workstream/guides/CODE-STANDARDS.md`
5. **Previous Review:** `.dev-workstream/reviews/INTS-BATCH-02-REVIEW.md` - Note the architectural failures.

### Source Code Location
- **Primary Work Areas:** 
  - `Hrot.Map.Common`
  - `Hrot.SimHost`
  - `Hrot.IG`
  - `Hrot.ClusterRunner`
  - `Hrot.Integration.Tests` (New or existing)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/INTS-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/INTS-BATCH-03-QUESTIONS.md`

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

Phase 3 implements trace logging directly at system edges where messages traverse bounded contexts (such as DDS ingestion and egress, and IO mapping layers). Afterwards, you will write a complete multi-process integration test spanning the full pipeline.

**Related Tasks:**
- **CORRECTIVE-0** - Remove Reflection Hack in HrotEnvironment
- [INTS-P3-011](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-011--trace-logging-simhost-entity-spawn-flow-1) - Trace SimHost Entity Spawn
- [INTS-P3-012](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-012--trace-logging-ig-entity-ingress--render-flow-2) - Trace IG Ingress & Render
- [INTS-P3-013](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-013--trace-logging-ig-map-drawings--ios-interactions-flows-36) - Trace Map Drawings & IOS Interactions
- [INTS-P3-014](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-014--integration-test-end-to-end-entity-lifecycle) - E2E Integration Test

---

## 🎯 Batch Objectives
Ensure full logging visibility of network transactions between internal Hrot subsystems, construct a clean multi-app Integration validation, and resolve the architectural reflection debt from Phase 2.

---

## ✅ Tasks

### Task 0: Corrective Action - Remove Reflection Hack in HrotEnvironment (CORRECTIVE-0)
**Files:** `Hrot.Map.Common/HrotEnvironment.cs`, `Hrot.Map.Definitions/Tkb/BdcTkbCatalog.cs`, etc.
**Description:** 
The previous batch utilized `System.Reflection` in `CreateTkb()` to register the `BdcTkbCatalog` dynamically and dodge a circular dependency. 
- Eliminate the reflection code.
- Provide a clean, elegant structural fix to allow `CreateTkb()` to safely wire up the catalog. This may mean utilizing an `ITkbRegistrar` interface injected at runtime, moving the registration bootstrap logic back to the compositional roots (SimHost/IG), or splitting the assembly. **Solve the architecture properly.**
- Ensure tests still pass.

### Task 1: Trace Logging: SimHost Entity Spawn (Flow 1) (INTS-P3-011)
**Files:** SimHost Egress / Spawning Systems
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-011--trace-logging-simhost-entity-spawn-flow-1)

### Task 2: Trace Logging: IG Entity Ingress & Render (Flow 2) (INTS-P3-012)
**Files:** IG Ingress / Rendering Systems
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-012--trace-logging-ig-entity-ingress--render-flow-2)

### Task 3: Trace Logging: IG Map Drawings & IOS Interactions (Flows 3–6) (INTS-P3-013)
**Files:** IG / IOS Logic and DDS transaction handling
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-013--trace-logging-ig-map-drawings--ios-interactions-flows-36)

### Task 4: Integration Test: End-to-End Entity Lifecycle (INTS-P3-014)
**Files:** `Hrot.SimHost.Integration.Tests` / `Hrot.Integration.Tests`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-014--integration-test-end-to-end-entity-lifecycle)

---

## 🧪 Testing and Technical Requirements

**Guidelines Override:**
- **xUnit Framework:** All new unit/integration tests MUST use xUnit.
- **DDS Domain Isolation:** Do not hardcode domain ID `0`. The test must utilize domain `10` to avoid contamination.
- **FdpLog Standard:** Debug prints and logging MUST utilize `FdpLog` from the FDP kernel. Using `Console.WriteLine` or standard logging frameworks is invalid. The task details ask for `[TRACE]` prefixes on `Console.WriteLine` or `ILogger`; override those directives and use `FdpLog` with standard Debug/Trace severity instead.

---

## 📊 Report Requirements

Provide a copy of this layout in your `.dev-workstream/reports/INTS-BATCH-03-REPORT.md` report, filling in details:

**Developer Insights**

**Q1:** What architectural adjustments did you make to resolve the Reflection hack from the previous batch? Why was your new approach functionally superior?

**Q2:** What issues did you encounter during implementation? How did you resolve them?

**Q3:** Did you spot any weak points in the existing codebase? What would you improve?

**Q4:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q5:** What edge cases did you discover that weren't mentioned in the spec?

**Q6:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 0: Reflection workaround is deleted and replaced with an elegant dependency structure
- [ ] Tasks 1-3: Trace logging correctly placed
- [ ] Task 4: End-to-end integration test runs headless, spawns an entity, confirms registry presence on the reader, and resolves the StyleComponent flawlessly.
- [ ] Test boundaries and setups are respected. 
- [ ] Report submitted addressing developer feedback explicitly

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md)
- **Design:** [DESIGN-Integration-Troubleshooting.md](../../docs/design/DESIGN-Integration-Troubleshooting.md)

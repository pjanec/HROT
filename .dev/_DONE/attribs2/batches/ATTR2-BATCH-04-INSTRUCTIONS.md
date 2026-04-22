# BATCH-04: System Finalization & Debt Resolution

**Batch Number:** ATTR2-BATCH-04  
**Tasks:** ATTR2-DEBT-06, ATTR2-DEBT-07  
**Phase:** Optimization and Production Wiring  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** ATTR2-BATCH-03

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! With Phase 6 completed, the complete binary pipeline from IG UI input down to SimHost ECS updates has been built and verified. However, we have a few lingering tech-debt items blocking a clean production release. This batch focuses on finalizing the Dependency Injection setup to ensure the new classes are alive in production, and decoupling a legacy factory pattern that accidentally binds binary operations to the JSON compiler.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-GUIDE.md`
2. **Review Doc:** `.dev-workstream/reviews/ATTR2-BATCH-03-REVIEW.md` 
3. **Debt Tracker:** `docs/attribs2/ATTR2-DEBT-TRACKER.md`

### Source Code Location
- **Primary Work Areas:**
  - `Hrot.Map.Common/Replication/Patching/EcsPatchContext.cs` (and factory locations)
  - `Hrot.Map.Common/Replication/Utils/JsonAttributeCompiler.cs`
  - `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`
  - `Hrot.Map.Common/Systems/CreateEntityRequestSystem.cs`
  - `Hrot.IG/IgApplication.cs` (or DI entry point)
- **Test Projects:** `Hrot.Map.Common.Tests`, `Hrot.IG.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR2-BATCH-04-REPORT.md`

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

### Task 1: Standalone `EcsPatchContext` Factory (ATTR2-DEBT-06)

**File:** `Hrot.Map.Common/Replication/Utils/JsonAttributeCompiler.cs` & Associated consumers  

**Description:** In Batch 3, the `UpdateEntityAttributeRequestSystem` binary branch was forced to use the `_jsonCompiler` field just to gain access to `CreatePatchContext()`. 
**Requirements:**
- Extract the context creation logic from `JsonAttributeCompiler.CreatePatchContext` into a standalone factory (e.g., `EcsPatchContextFactory` or a static utility). 
- Update `JsonAttributeCompiler` to consume or wrap this factory if necessary, or refactor the callers (`CreateEntityRequestSystem`, `UpdateEntityAttributeRequestSystem`).
- The binary branches in the aforementioned ECS systems should be completely independent of the `_jsonCompiler` when setting up `EcsPatchContext`/`BinaryPatchContext`. The warning handling "binary records skipped because no json compiler is injected" must be safely replaced with logic that works universally.

**Tests Required:**
- ✅ Verify that `UpdateEntityAttributeRequestSystem` can successfully apply `AttributeRecords` even when initialized with `JsonAttributeCompiler = null`.

---

### Task 2: IG DI Wiring for `CreationTool` (ATTR2-DEBT-07)

**File:** `Hrot.IG/IgApplication.cs` (or equivalent DI registration class)  

**Description:** The Edge Compiler `JsonToRecordCompiler` currently only exists in unit tests and factory files. The live UI `CreationTool` must have it injected for production usage.
**Requirements:**
- Identify the root DI mapping for the IG module.
- Retrieve the constructed `JsonToRecordCompiler` from `EdgeCompilerFactory.Build()` (if available/accessible) or build it directly during the module bootstrapping phase.
- Inject the compiler instance properly when initializing `CreationTool`.

**Tests Required:**
- ✅ Verify through an integration test or explicit unit check that `CreationTool` construction in DI contains a non-null `EdgeCompiler`.

---

## 📊 Report Requirements

**Developer Insights:**
**Q1:** How did decoupling `EcsPatchContext` change the module visibility of `EntityRepository` and related properties? Did you notice any other internal architecture tightly coupled to `JsonAttributeCompiler`?
**Q2:** When wiring the Edge Compiler to the IG UI DI container, did you encounter any singleton/transient lifecycle concerns given its zero-allocation architecture?

---

## 🎯 Success Criteria
- [ ] Task 1 Completed: `EcsPatchContext` constructed without `JsonAttributeCompiler`.
- [ ] Task 1 Completed: Binary interpretation works independent of JSON dependencies in ECS systems.
- [ ] Task 2 Completed: Production `CreationTool` initialized securely with `JsonToRecordCompiler` in DI container.
- [ ] No regression failures across `Hrot.IG.Tests` or `Hrot.Map.Common.Tests`.
- [ ] Report submitted.

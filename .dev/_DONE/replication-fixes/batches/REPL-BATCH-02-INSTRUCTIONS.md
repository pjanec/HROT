# BATCH-02: Corrections, Phase 3 Wiring & Phase 5 Translator Unification (Part A)

**Batch Number:** REPL-BATCH-02
**Tasks:** REPL-C01, REPL-C02, REPL-P3-T1, REPL-P3-T2, REPL-P3-T3, REPL-P5-T1, REPL-P5-T2
**Phase:** Corrective, Phase 3 & Phase 5 (Part A)
**Estimated Effort:** ~10 hours (1.2 days)
**Priority:** HIGH
**Dependencies:** REPL-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back to the Replication Fixes workstream. BATCH-01 introduced critical `IModuleSystem` and ECS-as-Staging updates, but it failed architectural rules (Zero-Allocation on the Hot Path) and broke the overall solution build due to signature alterations. Additionally, an architectural pivot was introduced to unify all Translators into the shared `Hrot.Map.Common` directory. 

**This batch comes with EXPLICIT autonomy instructions:**
You are to work autonomously until **ALL DONE**. The entire solution MUST compile cleanly. You shall **NOT** ask for obvious things. Do not ask me for permission to start a build or run tests—you are an intelligent developer. Use your intelligence to decide your steps autonomously, fix the root causes of errors, execute `dotnet build`, verify your logic, and *only* submit a report once everything is successful.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md` - How to work with batches
2. **Architectural Rules:** `FDP/Docs/architectural-rules.md` - Specifically TIER 1 Rule 3: Zero-Allocation (MANDATORY)
3. **Previous Review:** `.dev-workstream/reviews/REPL-BATCH-01-REVIEW.md` - Learn from your feedback
4. **Task Tracker:** `docs/replication-fixes/REPL-TASK-TRACKER.md` - Overall context
5. **Task Details:** `docs/replication-fixes/REPL-TASK-DETAIL.md` - Specific implementation steps

### Source Code Location
- **Primary Work Area:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/`, `Hrot.IG/`, `Hrot.ClusterRunner/Services/`, `Hrot.Map.Common/`

### Report Submission
**When done, submit your report to:**
`.dev-workstream/reports/REPL-BATCH-02-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests (where applicable):**

1. **Task 1:** Implement → Compile → **Verify/Run Tests** ✅
2. **Task 2:** Implement → Compile → **Verify/Run Tests** ✅
3. **Task 3:** Implement → Compile → **Verify/Run Tests** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task compiles successfully
- ✅ All related tests are passing

---

## 🎯 Batch Objectives
- **Fix (C01):** Resolve hot-path dynamic allocations by building `EntityQuery` objects once and caching them (fixing BATCH-01 violations).
- **Fix (C02):** Fix global build by resolving mismatched constructor errors in `Fdp.Examples.NetworkDemo.csproj`.
- **Phase 3:** Complete app module wiring in `IgApplication.cs`, `SimHostSubsystem.cs`, and `NetworkDemoApp.cs`.
- **Phase 5 Part A:** Move all `Hrot.IG` ingress translators into the shared `Hrot.Map.Common.Replication.Ingress` library space, including appropriate namespace renaming.

---

## ✅ Tasks

### Task 1: Fix Zero-Allocation Rule Violations (REPL-C01)
**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostPromotionSystem.cs` and `SubEntityCleanupSystem.cs`
**Problem:** `repo.Query().With<T>().Build()` allocates heap memory when called dynamically inside `Execute()` every frame.
**Action:** Refactor these files to correctly build and cache the `EntityQuery` inside an `OnInitialized` override (or via lazy initialization caching with the `repo` singleton state) to ensure absolute zero allocation inside the `.Execute(...)` loop.

### Task 2: Fix Solution Compilation (REPL-C02)
**File:** `FDP/Examples/Fdp.Examples.NetworkDemo/Configuration/DemoTopology.cs`
**Problem:** `GhostCreationSystem` and `GhostPromotionSystem` new signature requirements broke compiling dependencies.
**Action:** Update dependencies to pass requisite parameters locally within the Demo Topology. The whole repo MUST pass `dotnet build`.

### Phase 3: Module Wiring
**Tasks:** REPL-P3-T1, REPL-P3-T2, REPL-P3-T3
**Files:** `Hrot.IG/IgApplication.cs`, `Hrot.ClusterRunner/Services/SimHostSubsystem.cs`, `FDP/Examples/Fdp.Examples.NetworkDemo/NetworkDemoApp.cs`
**Reference:** `docs/replication-fixes/REPL-TASK-DETAIL.md#repl-p3-t1-update-igapplication--pass-entitymap--wire-ghostcreationsystem`
**Action:** Refactor Replication module integrations to decouple from ISerializationRegistry logic.

### Phase 5: Setup Common Sub-Project (REPL-P5-T1)
**File:** `Hrot.Map.Common/Hrot.Map.Common.csproj`
**Action:** Update references to include `FDP.Toolkit.Replication` and `ModuleHost.Network.Cyclone` so it can handle the translation logic from IG and SimHost globally. Run `dotnet restore` securely. Follow `REPL-TASK-DETAIL.md#repl-p5-t1-update-hrotmapcommon-project-references`.

### Phase 5: Migrate IG Ingress Translators (REPL-P5-T2)
**Files:** `Hrot.IG/Translators/*.cs`
**Action:** Relocate exactly 6 mapped Translator files to `Hrot.Map.Common/Replication/Ingress/`. Rename files, classes, and namespaces to explicitly contain the `IngressTranslator` postfix according strictly to `REPL-TASK-DETAIL.md#repl-p5-t2-migrate-ig-ingress-translators`. Make sure they still utilize the ECS-as-Staging properties built during Phase 2. Ensure IG continues to access these correctly in `IgApplication` by matching imports.

---

## ⚠️ Quality Standards

**❗ CODE QUALITY EXPECTATIONS**
- **REQUIRED:** You MUST check `dotnet build` from root regularly. Broken code builds will be completely rejected.
- **REQUIRED:** Code must strictly abide by the rules in `FDP/Docs/architectural-rules.md`. Zero Heap Allocations on Hot Paths checks are crucial for C01.

**❗ REPORT QUALITY EXPECTATIONS**
- Document your actual workflow, including the compiler errors you faced. Do not create fake boilerplate context.

---

## 📊 Report Requirements

Write your report inside `.dev-workstream/reports/REPL-BATCH-02-REPORT.md`.

**Answer the following questions:**
1. How did you resolve the zero-allocation query caching problem? What mechanism did you use?
2. Did moving the Ingress translators affect any internal dependencies that had to be untangled? 
3. Based on the Phase 5 logic, how much identical translation logic do you expect we'll be able to merge/reduce in BATCH-03 between the Egress/Ingress domains?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] REPL-C01 & C02 fixed so that `dotnet build` executes perfectly.
- [ ] REPL-P3-T1 to REPL-P3-T3 implemented.
- [ ] REPL-P5-T1 and REPL-P5-T2 implemented precisely.
- [ ] Report submitted with insights and feedback.

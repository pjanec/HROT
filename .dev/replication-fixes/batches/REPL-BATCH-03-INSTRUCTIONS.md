# BATCH-03: Phase 5 Translator Unification (Part B) & Phase 4 Integration Tests

**Batch Number:** REPL-BATCH-03
**Tasks:** REPL-C03, REPL-C04, REPL-C05, REPL-P5-T3, REPL-P5-T4, REPL-P5-T5, REPL-P5-T6, REPL-P4-T1, REPL-P4-T2, REPL-P4-T3, REPL-P4-T4
**Phase:** Phase 5 (Part B) & Phase 4
**Estimated Effort:** ~10 hours (1.2 days)
**Priority:** HIGH
**Dependencies:** REPL-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back to the third and final batch of the Replication Fixes workstream. BATCH-02 relocated the IG Ingress Translators to the shared `Hrot.Map.Common` directory. This batch will migrate the remaining SimHost Egress translators and `DescriptorMapper`, finalizing the unification. Following the merge, you will complete the definitive integration tests validating all of Phase 1-4's ECS-as-Staging properties and anti-zombie measures.

**This batch comes with EXPLICIT autonomy instructions:**
You are to work autonomously until **ALL DONE**. The entire solution MUST compile cleanly. You shall **NOT** ask for obvious things. Do not ask me for permission to start a build or run tests—you are an intelligent developer. Use your intelligence to decide your steps autonomously, execute `dotnet build`, execute `dotnet test`, verify your logic, and *only* submit a report once everything is successful.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md` - How to work with batches
2. **Architectural Rules:** `FDP/Docs/architectural-rules.md` - Core design rules and guidelines (MANDATORY)
3. **Previous Review:** `.dev-workstream/reviews/REPL-BATCH-02-REVIEW.md` - Learn from your feedback
4. **Task Tracker:** `docs/replication-fixes/REPL-TASK-TRACKER.md` - Overall context
5. **Task Details:** `docs/replication-fixes/REPL-TASK-DETAIL.md` - Specific implementation steps

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/Translators/`, `Hrot.Map.Common/Replication/`, `Hrot.IG/IgApplication.cs`
- **Test Project:** `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission
**When done, submit your report to:**
`.dev-workstream/reports/REPL-BATCH-03-REPORT.md`

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
- **Phase 5 Part B:** Move all remaining `Hrot.SimHost` Egress translators into `Hrot.Map.Common.Replication.Egress` library space. Relocate `DescriptorMapper` logic into Utils.
- **Phase 5 Part B Wiring:** Update `SimHostModule.cs` to access `Map.Common`.
- **Phase 4 Integration Tests:** Prove all functionality via autonomous integration tests via the `HrotRunnerHarness`.

---

## ✅ Tasks

### Task 1: Fix Stalled Entity Promotion (REPL-C03)
**Problem:** A `Ghost` entity is strictly an invalid, incomplete staging entity. FDP's default `EntityQuery` legitimately filters these out to protect systems from processing garbage data. When `GhostPromotionSystem.cs` triggers promotion, it currently just sets the state to `Constructing` and manually publishes a `ConstructionOrder`. It completely bypasses calling `EntityLifecycleModule.BeginConstruction(...)`! Because ELM is unaware of the entity, it ignores all `ConstructionAck` events. The entity gets permanently stuck in `Constructing` and never becomes `Alive`.
**Action:** 
Fix `FDP/Toolkits/FDP.Toolkit.Replication/Systems/GhostPromotionSystem.cs` so that it formally registers the entity with ELM via `BeginConstruction` during promotion. Note: `GhostPromotionSystem` will need access to `EntityLifecycleModule`, so you must provide it through `ReplicationLogicModule`. **DO NOT take any shortcuts (like forcing state to Active locally). IG is NOT a purely ghost node by design; it can potentially create entities itself, so it must fully participate in correct FDP lifecycle transitions.** The entity MUST successfully complete the pipeline and reach `EntityLifecycle.Alive`.

### Task 2: Remove "Ghost-Only" Assumptions from Ingress Translators (REPL-C04)
**Files:** `Hrot.Map.Common/Replication/Ingress/*.cs` (Migrated in BATCH-02)
**Action:** The developer previously added comments and logic assuming "IG is a ghost-only node". This is architecturally incorrect! IG is a full FDP node and can create entities. The reason Ingress translators have empty `ScanAndPublish` methods is strictly because they are *Ingress* translators, NOT because the application is a "ghost-only node". Purge all comments stating "IG is a ghost-only node" and ensure no shortcuts are taken inside the translators based on this false assumption.

### Task 3: Data-Oriented Component Initialization (REPL-C05)
**Problem:** In `BdcTkbBuilder.cs`, prototype definitions like `SimVehicleDef` and `IgVisualDef` are being added as managed components instead of mapping them properly at load-time to pure structs as the ECS design requires. 
**Action:** Implement "Option B" from the architectural review to resolve this Technical Debt. Convert definitions to runtime structs and discard the def post-init.
1. Update `Hrot.Map.Definitions/Tkb/BdcTkbBuilder.cs` methods to map `SimVehicleDef` and `IgVisualDef` to unmanaged structs (`VehicleParams`, `VisualData`) instantly instead of attaching the `Def` directly.
2. Remove the `[ComponentId(X)]` attribute from purely static definition classes (`SimVehicleDef`, `IgVisualDef`).
3. Free up their old byte keys in `Fdp.Kernel/GlobalComponentIds.cs`.
4. Create the new `VisualData` struct to hold `ModelPath`/`SymbolCode` (via `FixedStringXX`) and update `StyleResolutionSystem` to pull from it instead of `IgVisualDef`.

### Phase 5: Migrate SimHost Egress Translators (REPL-P5-T3)
**Files:** `Hrot.SimHost/Translators/*.cs`
**Action:** Relocate 3 mapped Translator files to `Hrot.Map.Common/Replication/Egress/`. Rename files, classes, and namespaces to explicitly contain the `EgressTranslator` postfix according strictly to `REPL-TASK-DETAIL.md#repl-p5-t3-migrate-simhost-egress-translators`. Make sure they shed any SimHost-specific logging bounds.

### Phase 5: Migrate EntityMission Translators (REPL-P5-T4)
**File:** `Hrot.SimHost/Translators/EntityMissionTranslator.cs`
**Action:** Because `EntityMission` feature has both Ingress AND Egress components, separate the Ingress translation and Egress translation into two separate classes directly inside `Hrot.Map.Common/Replication/` within their respective folders. Maintain their ECS mapping properties. Reference `REPL-TASK-DETAIL.md#repl-p5-t4-migrate-entitymission-translators`.

### Phase 5: Migrate DescriptorMapper (REPL-P5-T5)
**File:** `Hrot.SimHost/Util/DescriptorMapper.cs`
**Action:** Move this DDS-to-ECS mapping class into `Hrot.Map.Common/Replication/Utils/DescriptorMapper.cs`. Reorganize as an explicit shared utility for Latency testing, IG spawn prediction, and direct SimHost request parsing.

### Phase 5: Update Composition Roots (REPL-P5-T6)
**Action:** Update `Hrot.SimHost/Modules/SimHostModule.cs` and verify `Hrot.IG/IgApplication.cs` accesses this fully successfully post-migration. Use `dotnet build` iteratively.

### Phase 4: Integration Test Coverage
**Tasks:** REPL-P4-T1, REPL-P4-T2, REPL-P4-T3, REPL-P4-T4
**Files:** `Hrot.ClusterRunner.Integration.Tests/*.cs`
**Reference:** `docs/replication-fixes/REPL-TASK-DETAIL.md#repl-p4-t1-replicationphaseexecutiontests--systems-execute-each-frame`
**Action:** Implement all verification integration tests using `HrotRunnerHarness`. These must autonomously test the Ghost ECS-as-Staging lifecycle and anti-zombie rules. 
**Pro Tip:** Your integration tests testing intermediate `Ghost` states MUST utilize `.WithLifecycle(EntityLifecycle.All)` or `.WithLifecycle(EntityLifecycle.Ghost)` in their queries if they are searching for entities holding temporary components like `NetworkSpawnRequest`. Your tests validating end-to-end rendering logic (like `IgHasEntity`) should NOT, because they need to verify the entity actually became `Alive`. 

---

## ⚠️ Quality Standards

**❗ CODE QUALITY EXPECTATIONS**
- **REQUIRED:** Check `dotnet build` from root.
- **REQUIRED:** Rules in `FDP/Docs/architectural-rules.md` still apply. Be especially cautious defining single-frame transient transient interactions in test mocks (Tier 3 Rule 9).

**❗ TEST QUALITY EXPECTATIONS**
- **REQUIRED:** Tests must actually assert the behaviors (map destruction, correct Ghost lifecycle status, accurate coordinates preserved). Assert values, not just strings.
- **REQUIRED:** You MUST execute `dotnet test` successfully across the newly built `Hrot.ClusterRunner.Integration.Tests.csproj` and provide output in the report.

**❗ REPORT QUALITY EXPECTATIONS**
- Document test outcomes carefully. If an edge case arose from integrating Ingress/Egress, specify how you corrected it dynamically.

---

## 📊 Report Requirements

Write your report inside `.dev-workstream/reports/REPL-BATCH-03-REPORT.md`.

**Answer the following questions:**
1. Did the separation of `EntityMission` into distinct Ingress/Egress models uncover any misalignments in the prior `Hrot.SimHost` class structure?
2. Did any of the new Phase 4 tests fail on their first run? If so, why and how was it addressed?
3. What edge cases did you discover during Integration test hooks implementation?
4. How well did the automated tests validate the actual `EntityLifecycle.Ghost` state? Have you captured runtime behaviors correctly?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] REPL-C03 implemented so entities properly transition to `Alive` via the full ELM pipeline (NO shortcuts!).
- [ ] REPL-C04 implemented to remove false "ghost-only" architectural assumptions.
- [ ] REPL-C05 implemented. Definitions are transient, ECS holds pure unmanaged components.
- [ ] REPL-P5-T3 to REPL-P5-T6 implemented and solution passes `dotnet build`.
- [ ] REPL-P4-T1 to REPL-P4-T4 integration tests written.
- [ ] `dotnet test` output explicitly proves passing integrations for all Fix tracking requirements.
- [ ] Report submitted with insights and feedback.

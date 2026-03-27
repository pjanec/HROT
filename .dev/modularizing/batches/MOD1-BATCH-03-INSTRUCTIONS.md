# MOD1-BATCH-03: Network Translator Packs & Node Bootstrapper

**Batch Number:** MOD1-BATCH-03  
**Tasks:** CT-MOD1-C2, MOD1-P3T1, MOD1-P3T2, MOD1-P3T3, MOD1-P3T4, MOD1-P3T5  
**Phase:** Phase 3 — Network Translator Packs + Node Bootstrapper  
**Estimated Effort:** 10-12 hours  
**Priority:** CRITICAL  
**Dependencies:** MOD1-BATCH-02 (Needs Fixes Resolved)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to MOD1 Phase 3. 

**🚨 STOP RIGHT HERE: CRITICAL BLOCKER 🚨**
The previous batch (BATCH-02) attempted to fix a `Bagira.Runner` crash where dragging/spawning an entity resulted in `System.InvalidOperationException: Entity missing NavigationIntent` at `MoveToExecutor.OnEnter`. The attempted fix merely added the component to the `SimHostComponentRegistry`; **this DID NOT FIX the issue.** A registry tells the engine a component exists; it does *not* automatically ensure the data struct actually gets attached to the entity's runtime template when spawned via UI.

When running `Bagira.Runner -x all`, the application completely crashes when "Spawn moving entity" is initiated. **You must fix this before undertaking ANY other task.** It is paramount to run and verify the runner using `-x all` arguments in integration testing.

After CT-MOD1-C2 is resolved, you will process Phase 3: tearing down the `SimHostApp.OnLoad` God-Class in favor of declarative `NodeRole`-based bootstrapper composition, and implementing fully concrete DDD translator mappings for our Navigation intents.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - See Phase 3 tasks.
3. **Design Document:** `docs/modularizing/MOD1-DESIGN.md` - See §3.3
4. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-02-REVIEW.md` - Details why CT-MOD1-C failed.

### Source Code Location
- **Primary Work Areas:**
  - `Bagira.SimHost/Network/`
  - `Bagira.SimHost/`
  - `Bagira.SimHost.Standalone/Config/`
- **Test Projects:**
  - `Bagira.SimHost.Tests/`
  - `Bagira.SimHost.Integration.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/MOD1-BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task CT-MOD1-C2:** Implement → Fix `Bagira.Runner` → **ALL tests AND `-x all` process works** ✅
2. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
3. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
4. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
5. **Task 4:** Implement → Write tests → **ALL tests pass** ✅
6. **Task 5:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

Phase 3 transitions the architecture from a monolithic initialization path into role-directed composition via `NodeBootstrapper`. This involves extracting translators into static Factory Packs and configuring dynamic roles via config files. Additionally, this batch provides the concrete wire-to-engine implementation for Phase 1 `NavigationIntent` elements.

**Related Tasks:**
- `CT-MOD1-C2` (Priority 1 Blocker)
- [MOD1-P3T1](../docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t1--create-domain-specific-translator-packs) through [MOD1-P3T5](../docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t5--dds-discovery-config--entry-point-role-selection)

---

## 🎯 Batch Objectives
- **Solve the `Bagira.Runner` entity generation crash explicitly under `-x all`.**
- Deliver NodeRole composition separating `Brain`, `MuscleGround`, `ImageGenerator`, and `AllInOne`.
- Encapsulate ECS translation layers in `KinematicTranslatorPack`, `CognitiveTranslatorPack`, and `SharedTranslatorPack`.
- Expose the DDS-to-ECS mapping for CQRS navigation intent logic securely.

---

## ✅ Tasks

### Corrective Task CT-MOD1-C2

**Files:** Template/Blueprint loaders / Spawner inside the Runner/SimHost.

**Description:**
Fix the `InvalidOperationException: Entity missing NavigationIntent`. When an entity is created dynamically by the user or runner process, `NavigationIntent` and `NavigationStatus` components are missing from the instantiated template. You must trace the runtime spawn pipeline (likely involving `TargetType`/`EntityMaster` mapping or the TKB mapping) and ensure the ECS structures physically latch to the newly spawned entities. 

**Tests Required:**
- ✅ Ensure you can launch the application (or integration test equivalent) representing `Bagira.Runner -x all` and successfully create a moving entity without exceptions.
- ✅ Assert `NavigationIntent` is truly coupled to the instantiated entities.

---

### Task 1: MOD1-P3T1

**Files:** `Bagira.SimHost/Network/SharedTranslatorPack.cs`, `KinematicTranslatorPack.cs`, `CognitiveTranslatorPack.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P3T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t1--create-domain-specific-translator-packs)

**Description:** Extract translator construction into structured packs yielding `IEnumerable<IDescriptorTranslator>`. Ensure Navigation implementations are instantiated natively. 

**Tests Required:**
- ✅ Verify all three factory methods return correctly initialized enumerables without breaking tests.

---

### Task 2: MOD1-P3T2

**Files:** Domain Component Registries (Cognitive, Kinematic, Combat).

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P3T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t2--create-domain-specific-component-registries)

**Description:** Create explicit registries supplementing `BagiraSharedComponentRegistry`, delegating cleanly inside standard execution flow.

**Tests Required:**
- ✅ Check `SimHostComponentRegistry.RegisterAll` continues to provide idempotency.

---

### Task 3: MOD1-P3T3

**Files:** `Bagira.SimHost/NodeRole.cs`, `Bagira.SimHost/NodeBootstrapper.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P3T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t3--create-noderole-and-nodebootstrapper)

**Description:** Abstract setup into `NodeBootstrapper`. Implement role flags preventing Kinematics on Brain and Brain on Muscle nodes.

**Tests Required:**
- ✅ `NodeBootstrapper_AllInOne_RegistersAllModuleClasses`
- ✅ `NodeBootstrapper_Brain_DoesNotRegisterKinematicModule`
- ✅ `NodeBootstrapper_MuscleGround_DoesNotRegisterCognitiveModules`

---

### Task 4: MOD1-P3T4

**Files:** Egress/Ingress mapping logic inside `Bagira.SimHost/Network/Navigation*Translator.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P3T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t4--implement-concrete-navigation-translator-classes)

**Description:** Create fully functional egress and ingress translations for Nav status. Call `IGeographicTransform` specifically during the wire egress (keeping internal executor execution purely Cartesian). Map local component structs to the `.DDS` descriptor equivalents manually. 

**Tests Required:**
- ✅ Unit tests executing precisely the Egress translator map behavior (sending 1 DDS struct for 1 owned entity ECS struct).
- ✅ Unit tests checking the reverse Ingress translator map behavior safely ignoring unresolved network mapping IDs.

---

### Task 5: MOD1-P3T5

**Files:** `NodeConfiguration.cs`, Config XML/JSONs, `SimHostApp.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P3T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p3t5--dds-discovery-config--entry-point-role-selection)

**Description:** Expose standard parameters from standalone console arguments. Wire `--role` directly into the Node Bootstrapper.

**Tests Required:**
- ✅ `NodeConfiguration_LoadFrom_ReturnsDefaults_WhenFileAbsent`
- ✅ Evaluate `SimHostApp_AllInOneRole_StartsAndProcessesOneTick` safely executing via bootstrapper logic.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Checking "did the mock receive a string argument?".
- **REQUIRED:** CT-MOD1-C2 MUST HAVE Integration tests validating `Bagira.Runner -x all` operations explicitly executing component addition safely on instantiation. 

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please submit `.dev-workstream/reports/MOD1-BATCH-03-REPORT.md` completing the following:

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider? (Specifically, how did you track down the entity template instantiation mapping for CT-C2?)

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed while isolating the `NodeBootstrapper` paths?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Bagira.Runner spawned entities contain the correct baseline structs.
- [ ] `Bagira.Runner -x all` test execution passes flawlessly without throwing `Missing NavigationIntent` errors on spawn operations.
- [ ] `NodeRole` definitions seamlessly configure modules independently across boundaries.
- [ ] Translators implement precise ECS-to-DDS mappings without bleeding references across boundaries. 
- [ ] All specified unit/integration testing bars are achieved.

---

## ⚠️ Common Pitfalls to Avoid
- Neglecting to actually instantiate the new entities within Bagira templates properly, defaulting back to just registry declarations.
- Permitting non-owned entities to map Egress data (make sure ownership constraints hold).
- Duplicating Component IDs inside the domain registries.

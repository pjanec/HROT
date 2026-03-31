# MOD1-BATCH-07: Phase 7 IG Ground Clamping Module & Architecture Corrections

**Batch Number:** MOD1-BATCH-07  
**Tasks:** CT-MOD1-M, CT-MOD1-K, CT-MOD1-L, MOD1-P7T1, MOD1-P7T2, MOD1-P7T3, MOD1-P7T4, MOD1-P7T5  
**Phase:** Phase 7 (IG Ground Clamping Module)  
**Estimated Effort:** 10-12 hours  
**Priority:** NORMAL  
**Dependencies:** MOD1-BATCH-06

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BATCH-07. 

**🚨 CRITICAL ARCHITECTURE CORRECTIONS 🚨**
In Batch 06, you deviated heavily from the data-oriented design principles mandated by the specification.
0. **Third-Party Submodule Violation:** YOU MODIFIED `Fbt.Kernel`! `FDP\ExtDeps\FastBTree\src\Fbt.Kernel\BTreeActionNode.cs` and `IAIContext.cs` are part of a submodule and must never be touched. You must completely revert those files to their upstream states. The BTreeContext stubs must be left as no-ops.
1. **ECS Violation:** You were explicitly provided code for `PhysicsQueryActionNode` and `PathfindingActionNode` that retrieved `RaycastBatchData` via `world.GetSingletonRef()`. Instead, you injected `IRaycastService` and `IPathfindingService`. This OOP service injection **defeats the entire purpose of the NativeArray batching optimizations we are building.** You must remove these services and use the ECS singletons natively.
2. **Encapsulation Leak:** You exposed `LosRequestBatchingSystem` as a public property on `AutonomousPerceptionModule` because it inherited from `ComponentSystem`. `IModule` implementations must perfectly encapsulate their systems. You must refactor that system so it registers natively via the `ISystemRegistry`.

Once these corrections are made and tested, proceed to Phase 7: solving the Heterogeneous Terrain Correlation problem. You will build a ground clamping engine that allows remote nodes to dictate when the IG should forcibly clamp entities to the local geographical mesh.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - See Phase 7 tasks. Focus heavily on §3.7 in `MOD1-DESIGN.md` for context on terrain resolution.
3. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-06-REVIEW.md` 

### Source Code Location
- **Primary Work Areas:**
  - `FDP.Toolkit.Behavior/`, `FDP.Toolkit.Physics/`, `FDP.Toolkit.Navigation/` (for Corrections)
  - `Hrot.NED.Descriptors/`
  - `FDP.Toolkit.Geographic/`
  - `Hrot.IG/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-07-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task CT-MOD1-M:** Revert Submodule (`Fbt.Kernel`) Changes → **ALL tests pass** ✅
2. **Task CT-MOD1-K:** Fix ActionNode ECS implementations → **ALL tests pass** ✅
3. **Task CT-MOD1-L:** Fix AutonomousPerceptionModule encapsulation → **ALL tests pass** ✅
4. **Task 1 (P7T1):** Implement DDS Descriptor & Wire Enum → **ALL tests pass** ✅
5. **Task 2 (P7T2):** Implement Clamping ECS Components → **ALL tests pass** ✅
6. **Task 3 (P7T3):** Implement `ITerrainProvider` & Ingress Translator → **ALL tests pass** ✅
7. **Task 4 (P7T4):** Implement execution systems (`Submit`, `Solver`, etc.) → **ALL tests pass** ✅
8. **Task 5 (P7T5):** Implement `IgGroundClampingModule` & `TransformSyncSystem` → **ALL tests pass** ✅

---

## ✅ Tasks

### Corrective Task CT-MOD1-M: REVERT `Fbt.Kernel` Submodule Modifications

**Files:** `FDP\ExtDeps\FastBTree\src\Fbt.Kernel\BTreeActionNode.cs`, `FDP\ExtDeps\FastBTree\src\Fbt.Kernel\IAIContext.cs`

**Description:**
- **REVERT** all changes made to these files. They belong to a third-party Git submodule (`FastBTree`).
- Previously, you were told to delete the raycast/pathfinding methods from these interfaces. DO NOT DO THAT. Instead, leave those interface method signatures intact as they belong to the upstream library, and leave their dummy stubs in `BTreeContext` as no-op fallbacks.

---

### Corrective Task CT-MOD1-K: Eliminate OOP Services from BTree Nodes

**Files:** `PhysicsQueryActionNode.cs`, `PathfindingActionNode.cs`, `FDP.Toolkit.Behavior` tests.

**Description:**
- Completely remove `IRaycastService` and `IPathfindingService` from the constructors and fields of the action nodes.
- Implement the exact methods (`RequestRaycast`, `GetRaycastResult`, `RequestPath`, `GetPathResult`) as they were written in the `MOD1-TASK-DETAIL.md` spec (Sections P6T4 and P6T5), leveraging `world.GetSingletonRef<RaycastBatchData>()` and `world.GetSingletonRef<PathfindingBatchData>()`.
- Correct any unit tests that previously mocked these services to instead instantiate an `EntityRepository` with the corresponding Singleton data.

---

### Corrective Task CT-MOD1-L: Encapsulate Perception Module Systems

**Files:** `AutonomousPerceptionModule.cs`, `LosRequestBatchingSystem.cs`.

**Description:**
- `AutonomousPerceptionModule` must not expose `public LosRequestBatchingSystem LosRequestBatching;`
- Refactor `LosRequestBatchingSystem` so that it can be passed effectively to `ISystemRegistry.AddToGroup(...)` inside `RegisterSystems()`. If this requires converting it from a `ComponentSystem` to an `IModuleSystem` or `DelegatingSystem`, do so. 
- Ensure that the bootstrapper/composition root no longer expects to pull fields directly off the module instance.

---

### Task 1: MOD1-P7T1

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P7T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p7t1--groundclampingoverride-dds-descriptor--eclampingmode-enum)

**Description:** Define the `GroundClampingOverride` DDS struct and the `EClampingMode` wire enumeration in `Hrot.NED.Descriptors`, setting up the network contract. Maintain the engine-side enum separately in `FDP.Toolkit.Geographic`.

---

### Task 2: MOD1-P7T2

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P7T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p7t2--ecs-components-groundclampingconfig-groundclampingstate-terrainquerybatchdata)

**Description:** Construct the three primary ECS structs handling local clamping configuration, transient clamping state, and the zero-allocation batch querying buffer. Ensure `IsClampingActive` evaluates the truth table accurately.

---

### Task 3: MOD1-P7T3

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P7T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p7t3--iterrainprovider-interface--groundclampingoverridetranslator)

**Description:** Outline the `ITerrainProvider` abstract boundary and implement the Ingress-only translator `GroundClampingOverrideTranslator` to map network overrides into local ECS config buffers.

---

### Task 4: MOD1-P7T4

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P7T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p7t4--three-phase-execution-systems)

**Description:** Construct the rigorous three-phase execution systems: `Initialization`, `Submit`, `Solver`, and `Resolution`. Pay extreme attention to the topological loop and ensure entities fall back to original elevations dynamically.

---

### Task 5: MOD1-P7T5

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P7T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p7t5--iggroundclampingmodule--transformsyncsystem-z-offset-application)

**Description:** Expose standard parameters to establish `IgGroundClampingModule`. Modify `TransformSyncSystem.SyncToIg()` in the IG domain to sum `SimTransform.Position.Z + GroundClampingState.VisualOffsetZ`.

---

## 📊 Report Requirements

Please submit `.dev-workstream/reports/MOD1-BATCH-07-REPORT.md` completing the following:

**Developer Insights**

**Q1:** For CT-MOD1-M, confirming everything builds after reverting the submodule files—did leaving the stubs as no-ops cause any unexpected side effects?

**Q2:** During CT-MOD1-K, how did the transition from mocked services to direct `EntityRepository` Singletons change the structure of your BTree node unit tests? 

**Q2:** For CT-MOD1-L, what was the minimal path to allow `LosRequestBatchingSystem` to conform to `ISystemRegistry` without breaking its internal ECS update logic?

**Q3:** The three-phase clamping pipeline (P7T4) is highly time-sensitive. Were you able to prove via integration tests that the `Resolution` phase precisely applies interpolations within the same frame?

**Q4:** Did bridging the `GroundClampingOverrideTranslator` (P7T3) expose any deserialization complexities mapping the DDS enum to the engine-side enum?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] NO modifications exist in `FDP\ExtDeps\FastBTree\src\Fbt.Kernel\`. The git submodule is clean.
- [ ] `IRaycastService` and `IPathfindingService` are annihilated and action nodes are reading memory securely via ECS batch singletons.
- [ ] `AutonomousPerceptionModule` perfectly encapsulates its internal payload of logic systems.
- [ ] Ground clamping execution sequences perform end-to-end natively on IG targets without GC allocations.
- [ ] Clamping state is cleanly replicated across the network border via DDS overrides.

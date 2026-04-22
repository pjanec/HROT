# ATTR-BATCH-03: System Integration & Unified Routing

**Batch Number:** ATTR-BATCH-03  
**Tasks:** ATTR-S5T1, ATTR-S5T2, ATTR-S5T3, ATTR-S5T4, ATTR-S6T1, ATTR-S6T2  
**Phase:** Phase 5 (Registration and Integration) & Phase 6 (Unified Descriptor Routing)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** ATTR-BATCH-02  

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to ATTR-BATCH-03! This is the final batch of the Attributes-to-ECS feature.

Now that the foundational wire formats are updated, and the zero-allocation `JsonAttributeCompiler` parsing logic is completed and rigorously tested, you will inject this compiler directly into the real SimHost components. This involves spinning it up during SimHost app initialization using the `AttributeCompilerBuilder`, wiring it into the creation and update systems, and finally unifying `DescriptorMapper` logic.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Design Document:** `docs/attribs-to-ecs/ATTR-DESIGN.md` (specifically Phase 5 & 6)
3. **Task Definitions:** `docs/attribs-to-ecs/ATTR-TASK-DETAIL.md` (Phase 5 & 6)
4. **Previous Review:** `.dev-workstream/reviews/ATTR-BATCH-02-REVIEW.md`

### Source Code Location
- **Primary Work Area:**
  - `Hrot.SimHost/SimHostApp.cs`
  - `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`
  - `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`
  - `Hrot.Map.Common/Replication/Utils/DescriptorMapper.cs`
- **Test Projects:**
  - `Hrot.Map.Common.Tests/Hrot.Map.Common.Tests.csproj`
  - `Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/ATTR-BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including all previous batch integration tests)

---

## Context

The isolated compiler now needs a list of property names targeting setters to be fully functional. You will build that routing table and distribute it into the systems. 

- `CreateEntityRequestSystem` will use `ListPatchContext` to fold dynamic attribute patches directly onto components before entity spawn events.
- `UpdateEntityAttributeRequestSystem` will use `EcsPatchContext` over the live network IDs, bypassing string deserialization overhead inside the tight update loops.
- `DescriptorMapper` will become the final consumer of the unified JSON path translation, letting us discard the duplicate construction algorithms for `dtEntityInfo` and `dtWorldPos` entirely.

**Related Tasks:**
- [ATTR-S5T1 & ATTR-S5T4](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t1--register-component-paths-in-simhost-startup) - Dependency injection setup and ordinal registration.
- [ATTR-S5T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t2--update-createentityrequestsystem-to-use-jsonattributecompiler) - Update Entity Creation
- [ATTR-S5T3](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t3--updateentityattributerequestsystem-full-json-pipeline-integration) - Update Attribute Request loop
- [ATTR-S6T1 & ATTR-S6T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s6t1--descriptormapper-dtentityinfo-uses-routing-delegates) - Refactor DescriptorMapper to share compiler logic.

---

## 🎯 Batch Objectives
- Define the routing delegates for `Name`, `Affiliation`, and `GeoPoint`.
- Inject `JsonAttributeCompiler` into the SimHost DDS consumer systems.
- Connect live patching in `UpdateEntityAttributeRequestSystem` (replacing the temporary acknowledged stubs from Batch 1) using `EcsPatchContext` and `FlushDirtyMarks`.
- Wire `CreateEntityRequestSystem` using `ListPatchContext` to fold properties onto component pools before spawning.
- Re-map Descriptor parsing logic in `DescriptorMapper` to use the delegate table.

---

## ✅ Tasks

### Task 1: ATTR-S5T1 & ATTR-S5T4 (Register Component Paths and Ordinals)

**File:** `Hrot.SimHost/SimHostApp.cs` (or create `AttributeCompilerFactory.cs` next to it)  
**Task Definition:** [ATTR-S5T1](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t1--register-component-paths-in-simhost-startup), [ATTR-S5T4](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t4--register-descriptor-ordinals-in-simhost-compiler-startup)

**Description:** Register the compiler using `AttributeCompilerBuilder` providing setters for properties.

**Requirements:**
- Register `IgEntityData` references for `"Name"` and `"Affiliation"`, passing their correct `descriptorOrdinal` targeting `EDescriptorType.dtEntityInfo`.
- Register leaf values for `GeoPoint` fields that target `SimTransform`. (e.g. `GeoPoint.Latitude`). Supply the ordinal `EDescriptorType.dtWorldPos`. Note you may need a helper accumulator tracking `lat/lon/alt` if `ToCartesian` requires all 3 coordinates simultaneously.
- Provide the generated `JsonAttributeCompiler` singleton instance into the DI/constructor pipelines for `CreateEntityRequestSystem` and `UpdateEntityAttributeRequestSystem`.

**Tests Required:**
- ✅ `SimHostAttributeCompiler_Name_Registered`
- ✅ `SimHostAttributeCompiler_Affiliation_Registered`
- ✅ `SimHostAttributeCompiler_Affiliation_PreservesExistingName`
- ✅ `AttributeCompiler_NamePatch_TriggersEntityInfoDirtyOnEcsPatchContext`
- ✅ `AttributeCompiler_GeoPatch_TriggersWorldPosDirty`

---

### Task 2: ATTR-S5T2 (Update CreateEntityRequestSystem)

**File:** `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`  
**Task Definition:** [ATTR-S5T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t2--update-createentityrequestsystem-to-use-jsonattributecompiler)

**Description:** Wire `ListPatchContext` into the creation path.

**Requirements:**
- Take `JsonAttributeCompiler?` inside constructor.
- Inside `ProcessPendingRequest` apply `_jsonCompiler.Compile(..., new ListPatchContext(...))` onto the `allComponents` list.
- Reassign `allComponents` utilizing `context.FlushComponents()`.

**Tests Required:**
- ✅ `CreateEntityRequestSystem_InitialAttributesJson_PatchesName`
- ✅ `CreateEntityRequestSystem_InitialAttributesJson_DoesNotOverwriteAffiliation`
- ✅ `CreateEntityRequestSystem_NullJson_NoPatch`

---

### Task 3: ATTR-S5T3 (UpdateEntityAttributeRequestSystem Integration)

**File:** `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`  
**Task Definition:** [ATTR-S5T3](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s5t3--updateentityattributerequestsystem-full-json-pipeline-integration)

**Description:** Replace the acknowledged batch 1 stubs with live ECS integration.

**Requirements:**
- Take `JsonAttributeCompiler` through constructor.
- Build `EcsPatchContext` wrapping the entity network Id block.
- Feed `_jsonCompiler.Compile` traversing real modifications dynamically.
- IMPORTANT: run `context.FlushDirtyMarks()` immediately after to flag `SmartEgressUtil`.

**Tests Required:**
- ✅ `UpdateEntityAttributeRequestSystem_JsonPatch_PatchesNameOnLiveEntity`
- ✅ `UpdateEntityAttributeRequestSystem_JsonPatch_FlushDirtyMarksCalledForEntityInfoOrdinal`
- ✅ `UpdateEntityAttributeRequestSystem_DualFieldPatch_BothApplied_SingleDirtyFlush`
- ✅ `UpdateEntityAttributeRequestSystem_UnknownEntityId_AcksEntityNotFound`
- ✅ `UpdateEntityAttributeRequestSystem_EmptyJson_AcksSuccess_NoMutation`

---

### Task 4: ATTR-S6T1 & ATTR-S6T2 (DescriptorMapper Uses Routing Delegates)

**File:** `Hrot.Map.Common/Replication/Utils/DescriptorMapper.cs`  
**Task Definition:** [ATTR-S6T1](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s6t1--descriptormapper-dtentityinfo-uses-routing-delegates), [ATTR-S6T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s6t2--descriptormapper-dtgeospatial-uses-routing-delegates)

**Description:** Refactor Descriptor mapping so the initial static arrays build off the same logic loops that the JSON property patchers use.

**Requirements:**
- Update `dtEntityInfo` cases to use the shared compiler. (Fallback to `CommanderId` direct modifications as noted).
- Update the `dtWorldPos` conversion cases to share target coordinate transform delegates.

**Tests Required:**
- ✅ `DescriptorMapper_WithCompiler_DtEntityInfoProducesIgEntityData`
- ✅ `DescriptorMapper_WithCompiler_NoDuplicateIgEntityData`
- ✅ `DescriptorMapper_WorldPos_SharedDelegate_ProducesSameResultAsDirectPath`

---

## 🧪 Testing Requirements

**Quality Standard:** Integration integration integration! 

We've covered the logic and bounds cases in the last batch; this batch covers actual system mechanics. Spin up tests validating that an entity emitted effectively intercepts patching before `repo.Bus.Publish(SpawnEntityCommand)`, and after emission via `UpdateEntityAttributeRequest`. Validate that SmartEgress logs distinct Ordinal triggers.

---

## 📊 Report Requirements

When completing the batch, submit `.dev-workstream/reports/ATTR-BATCH-03-REPORT.md`.

**Developer Insights**  
**Q1:** What difficulties did you encounter when wiring up the multi-coordinate `GeoPoint` struct logic for `SimTransform` conversions?  
**Q2:** Phase 6 centralizes `dtWorldPos` and `dtEntityInfo` mapping via `DescriptorMapper`. Does this structure feel sustainable going forward compared to hardcoded maps, or do you perceive any code duplication risk vectors in the delegate injection structures?  
**Q3:** The entire ATTR architectural objective is "Zero-Allocation JSON patching". Through your testing profiling, were you able to verify any lingering allocations triggered during the hot path `UpdateEntityAttributeRequestSystem` loop across this PR?  
**Q4:** In what scenarios could a caller bypass the compile safety bounds of `FlushComponents()` and `FlushDirtyMarks()` within these refactored systems?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Tasks ATTR-S5T1 to ATTR-S6T2 are completed.
- [ ] Application compiler injection succeeds without circularly dependent components.
- [ ] `CreateEntityRequestSystem` maps properties to list arrays without erroring.
- [ ] `UpdateEntityAttributeRequestSystem` streams natively into ECS memory limits natively bypassing chunk ticks.
- [ ] `DescriptorMapper` logic accurately refactored.
- [ ] Passed new test suites against all implementations.
- [ ] Report submitted.

---

## ⚠️ Common Pitfalls to Avoid
- **Duplicating Coordinate logic:** `ToCartesian(lat, long, alt)` requires ALL positions natively tracked on a map. A Json path parsing stream encounters them one token at a time. Do not attempt to process a 0-valued lat/long transformation without an appropriate accumulator structure catching the `StartObject` and `EndObject` triggers to wrap it sequentially over the target `SimTransform` object.

---

## 📚 Reference Materials
- [docs/attribs-to-ecs/ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md)
- [docs/attribs-to-ecs/ATTR-DESIGN.md](docs/attribs-to-ecs/ATTR-DESIGN.md)

# ATTR-BATCH-01: DDS API Migration & IG Pipe Simplification

**Batch Number:** ATTR-BATCH-01  
**Tasks:** ATTR-S1T1, ATTR-S1T2, ATTR-S2T1  
**Phase:** Phase 1 (DDS API Migration) & Phase 2 (IG Pipe Simplification)  
**Estimated Effort:** 4-6 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the **ATTR** workstream. This workstream upgrades the **entity attribute patch** pipeline across the IOS → IG → SimHost stack, replacing a fixed-enum discriminated union with a flexible JSON format, and processing it via a zero-allocation `ref struct` state machine.

This first batch tackles the foundational data model changes (Phase 1) and the IG tool simplification (Phase 2). You will replace the legacy DDS fields with new JSON string fields and simplify the IG's `CreationTool` to act as a dumb pipe that forwards this JSON verbatim.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Onboarding Guide:** `docs/attribs-to-ecs/ONBOARDING.md` - Orientation of what we are building
3. **Design Document:** `docs/attribs-to-ecs/ATTR-DESIGN.md` - Full architectural context
4. **Task Definitions:** `docs/attribs-to-ecs/ATTR-TASK-DETAIL.md` - Detailed task specifications
5. **Tracker:** `docs/attribs-to-ecs/ATTR-TASK-TRACKER.md` - Update your progress here

### Source Code Location
- **Primary Work Area:**
  - `Bagira.DDS.DataModel/GenericMessages.cs`
  - `Bagira.IG/Tools/CreationTool.cs`
- **Test Projects:**
  - `Bagira.DDS.DataModel.Tests/Bagira.DDS.DataModel.Tests.csproj`
  - `Bagira.IG.Tests/Bagira.IG.Tests.csproj`
  - `Bagira.Map.Common.Tests/Bagira.Map.Common.Tests.csproj`
  - `Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/ATTR-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including downstream tests broken by the API changes)

---

## Context

We are migrating away from `List<EntityAttributePayload> InitialAttributes` in `CreateEntityRequest` and `AttributeId`/`Payload` in `UpdateEntityAttributeRequest`. They are replaced with `string InitialAttributesJson` and `string AttributePatchJson`. This allows flexible entity configuration without continuously modifying the DDS IDL and code generator. 

The IG `CreationTool` will no longer parse JSON to build a `dtEntityInfo` descriptor; instead, it simply forwards the JSON received from the IOS directly to the SimHost.

**Related Tasks:**
- [ATTR-S1T1](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s1t1--replace-initialattributes-with-initialattributesjson-in-createentityrequest) - Migrate CreateEntityRequest
- [ATTR-S1T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s1t2--replace-attributeidpayload-in-updateentityattributerequest-with-attributepatchjson) - Migrate UpdateEntityAttributeRequest
- [ATTR-S2T1](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s2t1--creationtool-forward-json-verbatim-remove-dtentityinfo-descriptor) - Simplify CreationTool

---

## 🎯 Batch Objectives
- Change the DDS wire format for Entity creation and modification to use JSON strings.
- Remove all legacy `EntityAttribute` and `EntityAttributePayload` types.
- Refactor the IG `CreationTool` to stop emitting `dtEntityInfo` and instead forward `initialPropertiesJson`.
- Fix any compilation breakages in SimHost systems and tests downstream caused by these structural changes.

---

## ✅ Tasks

### Task 1: ATTR-S1T1 (Migrate CreateEntityRequest)

**File:** `Bagira.DDS.DataModel/GenericMessages.cs`  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s1t1--replace-initialattributes-with-initialattributesjson-in-createentityrequest)

**Description:** Replace `InitialAttributes` list with `InitialAttributesJson` string in the DDS message `CreateEntityRequest`.

**Requirements:**
- Remove `public List<EntityAttributePayload>? InitialAttributes;`
- Add `public string? InitialAttributesJson;`
- Wait to remove `EntityAttributePayload` and `EntityAttribute` until ATTR-S1T2.
- Update downstream code in `Bagira.SimHost` and `Bagira.IG` that constructs `CreateEntityRequest` to fix compilation.

**Tests Required:**
- ✅ `CreateEntityRequest_HasInitialAttributesJsonField` (assert field exists and is string)
- ✅ `CreateEntityRequest_HasNoInitialAttributesField` (assert legacy field removed)
- ✅ Existing downstream tests in `Bagira.SimHost.Tests` and `Bagira.Map.Common.Tests` must continue to compile and pass after adapting constructor calls.

---

### Task 2: ATTR-S1T2 (Migrate UpdateEntityAttributeRequest)

**File:** `Bagira.DDS.DataModel/GenericMessages.cs`  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s1t2--replace-attributeidpayload-in-updateentityattributerequest-with-attributepatchjson)

**Description:** Replace `AttributeId` + `Payload` with `AttributePatchJson` string in the DDS message `UpdateEntityAttributeRequest`.

**Requirements:**
- Remove `public EntityAttribute AttributeId;` and `public EntityAttributePayload Payload;`
- Add `public string AttributePatchJson;`
- Fully remove `EntityAttribute` enum and `EntityAttributePayload` struct/union as they are no longer used anywhere.
- Update downstream code in `Bagira.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs` and all related tests to fix compilation.

**Tests Required:**
- ✅ `UpdateEntityAttributeRequest_HasAttributePatchJsonField`
- ✅ `UpdateEntityAttributeRequest_HasNoAttributeIdField`
- ✅ `UpdateEntityAttributeRequest_HasNoPayloadField`
- ✅ `GenericMessages_EntityAttribute_EnumDoesNotExist` (ensure enum is completely removed)

---

### Task 3: ATTR-S2T1 (Simplify CreationTool)

**File:** `Bagira.IG/Tools/CreationTool.cs`  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s2t1--creationtool-forward-json-verbatim-remove-dtentityinfo-descriptor)

**Description:** `CreationTool` drops parsing `initialPropertiesJson` into `dtEntityInfo` and forwards it as raw JSON.

**Requirements:**
- In `BuildAndPublishCreateRequest`, remove the `dtEntityInfo` entry from the published `InitialDescriptors` list. It should now only contain `dtEntityMaster` and `dtGeoSpatial`.
- Remove `entityName` local variable resolving from the spawning path.
- Remove `aff` local variable from the spawning path.
- Assign `InitialAttributesJson = _initialPropertiesJson` directly.
- Remove `ParseNameFromJson` helper method.
- **DO NOT REMOVE**: `ParseAffiliationFromJson` (needed for ghost rendering).
- **DO NOT REMOVE**: `_nameResolver` field and constructor parameter.

**Tests Required:**
- ✅ `CreationTool_EmitsOnly_EntityMaster_And_GeoSpatial_Descriptors` (assert only 2 descriptors, no dtEntityInfo)
- ✅ `CreationTool_SetsInitialAttributesJson_FromInitialPropertiesJson` (assert exact JSON string forwarded)
- ✅ `CreationTool_InitialAttributesJson_IsNull_WhenNoPropertiesJson`
- ✅ `CreationTool_GhostColor_StillReflectsAffiliation`
- ✅ Update existing passing tests in `Bagira.IG.Tests/CreationToolTests.cs` to expect `InitialDescriptors.Count == 2`.

---

## 🧪 Testing Requirements

**Quality Standard:** Tests must verify ACTUAL BEHAVIOR, not just parameter existence or names.

- Since this batch heavily modifies core primitive messaging types, the primary testing goal is ensuring the whole solution still builds cleanly and that all existing system integration test coverage remains active and passing.
- Focus on asserting the new reflection metadata for the message structs, verifying fields are definitively present/removed.
- For `CreationTool`, run the specific behaviors under test: click simulate, intercept request, assert on exact payload properties.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

When completing the batch, submit `.dev-workstream/reports/ATTR-BATCH-01-REPORT.md`. Include the following questions:

**Developer Insights**
**Q1:** What compilation issues or downstream breakages did you encounter when changing the core DDS API? How did you resolve them efficiently?
**Q2:** Did you spot any weak points in the existing codebase while updating the SimHost or IG side to accommodate the API changes?
**Q3:** What edge cases or testing difficulties did you discover that weren't mentioned in this explicitly?
**Q4:** Are there any improvements you would make to `CreationTool` or how we inject generic properties into the IG tools?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task ATTR-S1T1 completed (fields swapped in `CreateEntityRequest`)
- [ ] Task ATTR-S1T2 completed (fields swapped in `UpdateEntityAttributeRequest`, legacy enum/structs deleted)
- [ ] Task ATTR-S2T1 completed (`CreationTool` forwarded as verbatim JSON, `dtEntityInfo` removed)
- [ ] The entire solution compiles with zero errors/warnings.
- [ ] All specified test requirements met and all tests pass `dotnet test`.
- [ ] Report submitted to `.dev-workstream/reports/ATTR-BATCH-01-REPORT.md` answering the required questions.

---

## ⚠️ Common Pitfalls to Avoid
- **Forgetting downstream updates:** The `EntityAttributePayload` was used across multiple systems, tests, and tools. Finding all references is critical.
- **Accidentally deleting ghost visuals code:** Do not delete `ParseAffiliationFromJson` in `CreationTool`. It is still needed for resolving the `_affiliationForDisplay` for drawing ghost outlines.

---

## 📚 Reference Materials
- **Task Defs:** [docs/attribs-to-ecs/ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md)
- **Onboarding:** [docs/attribs-to-ecs/ONBOARDING.md](docs/attribs-to-ecs/ONBOARDING.md)
- **Design:** [docs/attribs-to-ecs/ATTR-DESIGN.md](docs/attribs-to-ecs/ATTR-DESIGN.md)

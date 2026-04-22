# BATCH-03: System Integration & Client Injection

**Batch Number:** ATTR2-BATCH-03  
**Tasks:** CORRECTIVE-0, ATTR2-P5T1, ATTR2-P5T2, ATTR2-P6T1  
**Phase:** Phases 5 & 6 combined  
**Estimated Effort:** 8-10 hours  
**Priority:** HIGH  
**Dependencies:** ATTR2-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome backing to Phase 5 & 6 of the ATTR2 system pipeline. Now that the Edge Compiler and the core Binary Interpreter are built and tested, we need to plug them into the live networking and edge systems.
First, you must resolve an architectural violation regarding JSON serialization attributes leaking into our DDS struct definitions. Clean it up, then integrate the interpreter into the main `CreateEntityRequestSystem` and `UpdateEntityAttributeRequestSystem` in SimHost, and inject the edge compiler into the client `CreationTool` interface.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-GUIDE.md`
2. **Batch 02 Review:** `.dev-workstream/reviews/ATTR2-BATCH-02-REVIEW.md` (See `[JsonInclude]` violation)
3. **Design Document:** `docs/attribs2/ATTR2-DESIGN.md` (Focus on Phase 5 and Phase 6 integration)
4. **Task Definitions:** `docs/attribs2/ATTR2-TASK-DETAIL.md` (See details for P5x, P6x tasks)

### Source Code Location
- **Primary Work Areas:**
  - `Hrot.NED/GenericMessages.cs`
  - `Hrot.NED.Tests/AttributeRecordTests.cs`
  - `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs`
  - `Hrot.Map.Common/Replication/Systems/UpdateEntityAttributeRequestSystem.cs`
  - `Hrot.IG/CreationTool.cs` (or equivalent client system path as specified)
- **Test Projects:** `Hrot.NED.Tests`, `Hrot.SimHost.Tests` 

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR2-BATCH-03-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
...

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## ✅ Tasks

### Task 0: Remove `[JsonInclude]` Pollution (CORRECTIVE-0)

**File:** `Hrot.NED/GenericMessages.cs` / `Hrot.NED.Tests/AttributeRecordTests.cs` (REFACTOR)  

**Description:** To serialize public fields for testing, the previous developer added `[JsonInclude]` to all DDS struct fields. This violates separation of concerns.
**Requirements:**
- Remove **all** `[JsonInclude]` attributes from `GenericMessages.cs`.
- Remove `using System.Text.Json.Serialization;` from that file.
- Update `Hrot.NED.Tests/AttributeRecordTests.cs` to supply a `JsonSerializerOptions` instance with `IncludeFields = true` when calling `JsonSerializer.Serialize` and `Deserialize` to fix the previously failing assertions.
- Ensure all 16 tests in DataModel still pass.

---

### Task 1: `CreateEntityRequestSystem` Binary Branch (ATTR2-P5T1)

**File:** `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p5t1--createentityrequestsystem-binary-branch)

**Description:** Inject the Binary Interpreter into initial entity creation.
**Requirements:**
- Modify the system to construct/fetch the `BinaryInterpreter`.
- Within the request processing loop, check if `request.InitialAttributeRecords != null && request.InitialAttributeRecords.Count > 0`.
- If present, apply the binary records via `BinaryInterpreter.Apply()` BEFORE processing any remaining `InitialAttributesJson`.
- The binary records take precedence, but if `InitialAttributesJson` has overlapping keys, the nested fallback is acceptable.

**Tests Required:**
- ✅ Verify that a `CreateEntityRequest` containing `InitialAttributeRecords` successfully produces an entity with the correct ECS component states (e.g. `IgEntityData.Name`). Ensure the fallback behavior with JSON continues to pass old tests.

---

### Task 2: `UpdateEntityAttributeRequestSystem` Binary Branch (ATTR2-P5T2)

**File:** `Hrot.Map.Common/Replication/Systems/UpdateEntityAttributeRequestSystem.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p5t2--updateentityattributerequestsystem-binary-branch)

**Description:** Inject the pipeline into runtime entity networking.
**Requirements:**
- The system must use a `BinaryInterpreter` to process `request.AttributeRecords` if present.
- Process binary records first, then process JSON records (if the payload `AttributePatchJson` isn't empty).
- Make sure SmartEgress triggers on the `IEntityPatchContext` (e.g. `ListPatchContext` or `EcsPatchContext`) wrapping the target entity.

**Tests Required:**
- ✅ Verify `UpdateEntityAttributeRequest` successfully mutates runtime properties via binary structures and sets appropriate dirty marks using the `BinaryPatchContext`.

---

### Task 3: `CreationTool` EdgeCompiler Injection (ATTR2-P6T1)

**File:** `Hrot.IG/CreationTool.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p6t1--creationtool-edgecompiler-injection)

**Description:** Make the client emit binary streams natively on entity spawn.
**Requirements:**
- Upon entity creation, the UI generates `_initialPropertiesJson`.
- Call `JsonToRecordCompiler.Compile` to transform this JSON string into a list of `AttributeRecord`s.
- Send the `AttributeRecord`s via `CreateEntityRequest.InitialAttributeRecords`.
- Use an `ArrayPool<AttributeRecord>` for the `Compile` output buffering to maintain zero-allocations during the conversion step. Don't forget to return the rent.

**Tests Required:**
- ✅ An integration/unit test confirming that when the `CreationTool` prepares a network request, the generated `CreateEntityRequest` has its `InitialAttributeRecords` correctly populated.

---

## 🧪 Testing Requirements
- **No shallow tests.** You must read the value written inside the ECS buffers to verify routing accuracy.
- You MUST ensure the Corrective Task leaves the DDS DataModel completely clean of JSON serialization semantics.

---

## 📊 Report Requirements

**Developer Insights:**
**Q1:** How did removing `[JsonInclude]` affect the overall test suite? Was `IncludeFields = true` sufficient for all sub-structs out of the box?
**Q2:** When mixing binary and JSON payloads on `UpdateEntityAttributeRequestSystem`, was there any conflict with ECS authority guards during the separated parsing phases?
**Q3:** During `CreationTool` implementation, what size of buffer did you rent from `ArrayPool` for `Compile`?

---

## 🎯 Success Criteria
- [ ] Task 0 Completed: No `[JsonInclude]` in DDS types.
- [ ] Phase 5 & 6 fully implemented according to Task Specifications.
- [ ] Integration tests pass showing live components reacting to binary packets.
- [ ] Report submitted.

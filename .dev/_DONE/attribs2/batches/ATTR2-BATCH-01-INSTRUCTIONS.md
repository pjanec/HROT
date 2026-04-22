# BATCH-01: Binary Contract & Schema Foundation

**Batch Number:** ATTR2-BATCH-01  
**Tasks:** ATTR2-P1T1, ATTR2-P1T2, ATTR2-P1T3  
**Phase:** Phase 1: Binary Contract & Schema Foundation  
**Estimated Effort:** 3-4 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the ATTR2 workstream. This first batch establishes the core binary DDS contract and attribute IDs used for transmitting entity updates without JSON parsing on the host. We are implementing the data structures and updating the existing DDS messages to support binary attributes while retaining backward compatibility.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-GUIDE.md` - How to work with batches
2. **ATTR2 Onboarding:** `docs/attribs2/ONBOARDING.md` - Context for the ATTR2 pipeline
3. **Design Document:** `docs/attribs2/ATTR2-DESIGN.md` - Technical specifications (specifically sections §3.1 and §3.4)
4. **Task Definitions:** `docs/attribs2/ATTR2-TASK-DETAIL.md` - See Phase 1 tasks details

### Source Code Location
- **Primary Work Area:** `Hrot.NED` and `FDP/Toolkits/FDP.Toolkit.Replication`
- **Test Project:** `Hrot.NED.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR2-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/ATTR2-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Verify static compilation ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

This batch focuses on creating the new IDL-compatible struct definitions, schema identifiers, and extending the wire messages to carry them in an optional list. This is the foundation upon which the rest of the ATTR2 system builds.

**Related Tasks:**
- [ATTR2-P1T1](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p1t1--attributevalueunion-and-attributerecord-dds-types) - `AttributeValueUnion` and `AttributeRecord` DDS Types
- [ATTR2-P1T2](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p1t2--attributeid-schema-constants) - `AttributeId` Schema Constants
- [ATTR2-P1T3](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p1t3--update-wire-messages-createentityrequest-updateentityattributerequest) - Update Wire Messages

---

## 🎯 Batch Objectives
Establish the new DDS wire types and extend existing messages with binary attribute list fields — achieving zero runtime behaviour changes.

---

## ✅ Tasks

### Task 1: `AttributeValueUnion` and `AttributeRecord` DDS Types (ATTR2-P1T1)

**File:** `Hrot.NED/GenericMessages.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p1t1--attributevalueunion-and-attributerecord-dds-types)

**Description:** Add two new C# structs for holding binary attribute values over DDS wire.
**Requirements:**
- Add `AttributeValueUnion` with tagged-union approaches (`Int32`, `Int64`, `Float32`, `Float64`, `Bool`, `String`, `Vec3f`, `Vec3d`, `Vec4f`). Must be `[DdsManaged]`. Must include `AttributeValueType` discriminator enum.
- Add `AttributeRecord` struct: `ushort AttributeId`, `short SubIndex1`, `short SubIndex2`, `AttributeValueUnion Value`.
- See existing `GenericMessages.cs` for IDL attributes conventions `[DdsIdlFile(...)]`. Needs to follow exactly.
- Fixed-size arrays must be represented as C# arrays.
- Do NOT modify any existing types in the file.

**Tests Required (in `Hrot.NED.Tests`):**
- ✅ Round-trip serializing an `AttributeRecord` (with type Float64) to JSON via `JsonSerializer` without data loss.
- ✅ Test `String` branch correctly populates and other branches stay default/zero.
- ✅ Test `Vec3d` branch correctly stores values `[1.0, 2.0, 3.0]`.
- ✅ Verify `AttributeValueType` covers all nine types.

---

### Task 2: `AttributeId` Schema Constants (ATTR2-P1T2)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/AttributeIds.cs` (NEW FILE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p1t2--attributeid-schema-constants)

**Description:** Create constants for well-known attribute IDs.
**Requirements:**
- Create static class containing `ushort` constants: `Name=1`, `Affiliation=2`, `GeoLat=10`, `GeoLon=11`, `GeoAlt=12`.
- Document the reserved numeric range strategy in code comments.
- Do not reference ECS components directly. Reference `System` namespace only.

**Tests Required:**
- ✅ Static compilation in isolation. No runtime tests needed. File should compile.

---

### Task 3: Update Wire Messages (ATTR2-P1T3)

**File:** `Hrot.NED/GenericMessages.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p1t3--update-wire-messages-createentityrequest-updateentityattributerequest)

**Description:** Add binary attributes fields to existing entity request messages.
**Requirements:**
- Add `[DdsManaged] public List<AttributeRecord>? InitialAttributeRecords;` to `CreateEntityRequest`. Place it after existing fields.
- Add `[DdsManaged] public List<AttributeRecord>? AttributeRecords;` to `UpdateEntityAttributeRequest`. Place it after existing fields.
- Include XML doc comments referencing `ATTR2-DESIGN.md §3.1`.
- Keep the existing `InitialAttributesJson` and `AttributePatchJson` fields untouched.

**Tests Required (in `Hrot.NED.Tests`):**
- ✅ Verify `CreateEntityRequest` construction with `InitialAttributeRecords = null`.
- ✅ Verify `CreateEntityRequest` construction with a non-null list of 2 records.
- ✅ Verify `UpdateEntityAttributeRequest` construction defaults `AttributeRecords` to null.
- ✅ Run all existing tests in `Hrot.NED.Tests` and ensure they pass (zero regressions).

---

## 🧪 Testing Requirements
- Code must pass all existing `Hrot.NED.Tests` without failures.
- Implement the unit tests under `Hrot.NED.Tests` for Task 1 and 3 as specified.
- Tests must verify actual behavior — storing and retrieving data correctly, defaults correctly — avoiding superficial string-presence assertions.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

When completing your report, answer the following questions to capture your experience:

**Developer Insights:**
**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** Did you spot any weak points in the existing `GenericMessages.cs` codebase? What would you improve?
**Q3:** What design decisions did you make regarding the struct layout of `AttributeValueUnion` (e.g. `[StructLayout(LayoutKind.Explicit)]` vs not) beyond the instructions? What alternatives did you consider?
**Q4:** What edge cases did you discover that weren't mentioned in the spec?
**Q5:** Are there any performance concerns or optimization opportunities you noticed when designing these structs?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task 1 completed: Types defined and unit tests passing.
- [ ] Task 2 completed: Schema constants implemented and statically checked.
- [ ] Task 3 completed: Wire messages updated and unit tests passing.
- [ ] All overall project tests pass to guarantee zero regressions.
- [ ] Report submitted to `.dev-workstream/reports/ATTR2-BATCH-01-REPORT.md`.

---

## 📚 Reference Materials
- **Task Defs:** [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md)
- **Design:** [ATTR2-DESIGN.md](../../docs/attribs2/ATTR2-DESIGN.md)
- **Onboarding:** [ONBOARDING.md](../../docs/attribs2/ONBOARDING.md)

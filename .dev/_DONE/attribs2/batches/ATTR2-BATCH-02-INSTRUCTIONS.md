# BATCH-02: Edge Compiler & Binary Interpreter Core

**Batch Number:** ATTR2-BATCH-02  
**Tasks:** CORRECTIVE-0, ATTR2-P2T1, ATTR2-P2T2, ATTR2-P3T1, ATTR2-P4T1, ATTR2-P4T2, ATTR2-P4T3  
**Phase:** Phases 2, 3 & 4 combined  
**Estimated Effort:** 12-16 hours  
**Priority:** HIGH  
**Dependencies:** ATTR2-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back to Phase 2, 3 & 4 of the ATTR2 system. We are scaling up the batch size. In this batch, we will correct an architectural violation from the previous batch, and then implement the Edge Compiler (`JsonToRecordCompiler`), the Core Binary Interpreter dispatch loop, and wire up the SimHost implementations for handling attributes. Note there are complex task items spanning across toolkits and the Hrot.SimHost project. Pay close attention to zero-allocation hot paths and correct testing abstractions.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-GUIDE.md`
2. **Batch 01 Review:** `.dev-workstream/reviews/ATTR2-BATCH-01-REVIEW.md` (Contains critical feedback on CycloneDDS union definitions)
3. **Design Document:** `docs/attribs2/ATTR2-DESIGN.md`
4. **Task Definitions:** `docs/attribs2/ATTR2-TASK-DETAIL.md` - See details for P2x, P3x, P4x tasks. 

### Source Code Location
- **Primary Work Areas:**
  - `Hrot.NED/`
  - `FDP/Toolkits/FDP.Toolkit.Replication/` 
  - `Hrot.SimHost/`
- **Test Projects:** `Hrot.NED.Tests`, `FDP.Toolkit.Replication` unit tests, `Hrot.SimHost.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR2-BATCH-02-REPORT.md`

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

## Context

We need to fix the union generation first. Then we build the JSON-to-Binary Edge Compiler that will live on the IG/client. After that, we need the receiver dispatch engine (the Binary Interpreter) and its target subsystem implementations located in the SimHost domain. 

---

## 🎯 Batch Objectives
- **Corrective:** Rewrite `AttributeValueUnion` to be a valid CycloneDDS union using C# attributes.
- **Phase 2:** Build the Edge Compiler side that transforms JSON text into binary struct sequences.
- **Phase 3:** Introduce the core fast dispatch loop, context mapping, and scratchpad handling components.
- **Phase 4:** Create installers to route basic SimHost logic (Name, Affiliation, GeoLocation) via the new Core.

---

## ✅ Tasks

### Task 0: Implement Correct DDS Union Pattern (CORRECTIVE-0)

**File:** `Hrot.NED/GenericMessages.cs` (REFACTOR)  

**Description:** An architectural pattern for CycloneDDS unions was missed in BATCH-01, resulting in a flat struct struct mapping that CycloneDDS does not generate consistent IDL metadata for. 
**Requirements:**
- Refactor `AttributeValueUnion` to use `[DdsUnion]`, `[DdsDiscriminator]`, and `[DdsCase(..)]` correctly.
- Discard the flat struct multiple-field approach with a single discriminator enum field without attributes. You MUST annotate it properly.
- Look exactly at `Hrot.NED/AllDescriptors.cs` (e.g. `EntityDescriptorUnion`) to understand how CycloneDDS requires the union schema attributes to be formatted.
- Ensure all 8 unit tests in `Hrot.NED.Tests/AttributeRecordTests.cs` (from BATCH-01) still pass. Adjust the struct logic if anything breaks.

---

### Task 1: `JsonToRecordCompiler` and Builder (ATTR2-P2T1)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/` (NEW FILES)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p2t1--jsontorecordcompiler-and-jsontorecordcompilerbuilder)

**Description:** Zero-allocation edge compiler mapping JSON fields to `AttributeRecord` streams.
**Requirements:**
- See spec for `JsonToRecordCompilerBuilder.cs` (fluent hash-path builder) and `JsonToRecordCompiler.cs` (the hot path runner using `Utf8JsonReader`).
- Implement the nested logic explicitly (flat keys, nested objects, and array-indexing via numeric strings mapped to `SubIndex1/2`).
- IMPORTANT: No dictionary lookups on the compiler hot path during `.Compile()` outside of examining the readonly `_routes` routing dictionary. Stack size should be strictly bounded.
- Use the existing `JsonAttributeCompiler.HashPath` (FNV-1a) function instead of duplicating it. 

**Tests Required (in `FDP.Toolkit.Replication` or `Hrot.SimHost.Tests`):**
- ✅ Test all 9 scenarios described in the Task Description doc (e.g. flat field, dotted path, nested object, integer keys).
- ✅ Implement zero allocation test (using `GC.GetTotalAllocatedBytes`) to prove `.Compile()` allocates 0 bytes.

---

### Task 2: `EdgeCompilerFactory` Registration (ATTR2-P2T2)

**File:** `Hrot.SimHost/AttributeCompilerFactory.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p2t2--edgecompilerfactory-domain-schema-registration)

**Description:** Add static registry for domain schemas matching the old compiler.
**Requirements:**
- Register paths: `Name` (Id 1), `Affiliation` (Id 2), `GeoPoint.Latitude` (Id 10), `GeoPoint.Longitude` (Id 11), `GeoPoint.Altitude` (Id 12).
- Must mirror `AttributeCompilerFactory`. Return the immutable `JsonToRecordCompiler` built by the builder.

**Tests Required:**
- ✅ Compiler validates successfully, and feeding JSON with all 5 keys emits exactly 5 binary records. Missing unknown paths emit exactly 0 records.

---

### Task 3: Binary Interpreter Core Setup (ATTR2-P3T1)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/` (NEW FILES)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p3t1--ibinaryattributeinstaller-binarypatchcontext-binaryinterpreterbuilder-binaryinterpreter)

**Description:** Build the new Interpreter structure elements and runtime class.
**Requirements:**
- Implement the interface, the builder, the patch context struct, and the runtime state mapping.
- Memory layout: The `Apply` function must route via array indices using `_handlers[record.AttributeId]`. Do not use runtime dictionaries.
- Correctly track SubsystemFlushers with bitmasks and run them at the tail end of `Apply()`. 

**Tests Required:**
- ✅ Comprehensive tests of the interpreter routing via manual builder hooks: Basic dispatch, Ignoring unknown IDs, Flusher called exactly once per bit, Flusher skipping untouched mask bits, Offset calculations on the context scratchpad.

---

### Task 4: Entity Data Installer (ATTR2-P4T1)

**File:** `Hrot.SimHost/Installers/EntityDataAttributeInstaller.cs` (NEW FILE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p4t1--entitydataattributeinstaller)

**Description:** Hook `Name` and `Affiliation` writes for ECS via `dtEntityInfo`.
**Requirements:**
- Delegate to `ctx.PatchContext.CanWriteManaged<IgEntityData>()`.
- Reuse Enum mapping (`MapAffiliationString`/`MapAffiliationInt`) from `AttributeCompilerFactory`. Do NOT duplicate mapping logic.
- Do NOT use the scratchpad since no delayed accumulation is needed.

**Tests Required:**
- ✅ Send name, expect mutation. Send affiliation, expect mutation. Check authority guard blocking modifications. Mask bits must be dirtied.

---

### Task 5: Sim Transform Installer (ATTR2-P4T2)

**File:** `Hrot.SimHost/Installers/SimTransformAttributeInstaller.cs` (NEW FILE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p4t2--simtransformattributeinstaller)

**Description:** Hook `GeoLat`, `GeoLon`, `GeoAlt` writes for ECS.
**Requirements:**
- Rely heavily on scratchpad offset allocation (`GeoCoordScratchpad`). Wait until flusher to compute Cartesian vectors exactly once per batch.
- Only initialize coordinates via reverse geography once per packet (`!scratch.Initialized`). Take `IGeographicTransform` in the constructor. 

**Tests Required:**
- ✅ Test partial updates properly pulling old coordinate states. Test 3-record packet flushing only once. Validate authority guards.

---

### Task 6: Binary Interpreter Factory Integration (ATTR2-P4T3)

**File:** `Hrot.SimHost/AttributeCompilerFactory.cs` (UPDATE)  
**Task Definition:** See [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md#attr2-p4t3--binaryinterpreterfactory-simhost-wiring)

**Description:** Wire it all up inside `Hrot.SimHost`.
**Requirements:**
- Make a `BuildBinaryInterpreter(IGeographicTransform? geoTransform)` adding both Phase 4 Installers.
- Conditionally add `SimTransformAttributeInstaller` if transform non-null. 

**Tests Required:**
- ✅ Verify behavior when constructed with null transform. Verify integration via IgsApplication DI checks.

---

## 🧪 Testing Requirements
- **No shallow tests.** You must read the value written inside the buffer to verify routing and execution accuracy to actual structs. 
- You MUST write the allocation assertion verifying stack usage logic for the edge compiler `Compile` function (it is a strict success requirement). Memory performance is critical.

---

## 📊 Report Requirements

**Developer Insights:**
**Q1:** What major issues did you encounter during the CycloneDDS Corrective Task 0? How did you confirm CycloneDDS compiler acceptance?
**Q2:** During `JsonToRecordCompiler` architecture, what was the hardest part about preventing array allocation with `Utf8JsonReader` nesting logic?
**Q3:** Mention design choices you made dealing with the `BinaryInterpreter` runtime lookups and memory offsets. Did you change `Span<>` or `MemoryMarshal` techniques?
**Q4:** Are there any optimizations that should be refactored or pulled from memory abstractions to squeeze even more speed from the hot paths? What are your reflections on the performance profile?

---

## 🎯 Success Criteria
- [ ] Task 0 Completed: CycloneDDS codegen builds `AttributeValueUnion` fine and BATCH-01 tests run green.
- [ ] Phase 2, 3, 4 fully implemented according to Task Specifications.
- [ ] Comprehensive non-shallow edge-routing and flusher test suite.
- [ ] Edge Compiler hits 0 allocations.
- [ ] Report submitted.

---

## 📚 Reference Materials
- **Task Defs:** [ATTR2-TASK-DETAIL.md](../../docs/attribs2/ATTR2-TASK-DETAIL.md)
- **Design:** [ATTR2-DESIGN.md](../../docs/attribs2/ATTR2-DESIGN.md)

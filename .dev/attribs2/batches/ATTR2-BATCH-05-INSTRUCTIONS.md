# BATCH-05: ATTR2 Debt Resolution & Micro-Optimizations

**Batch Number:** ATTR2-BATCH-05  
**Tasks:** ATTR2-DEBT-01, ATTR2-DEBT-02, ATTR2-DEBT-03, ATTR2-DEBT-04, ATTR2-DEBT-05  
**Phase:** Tech Debt / Optimization  
**Estimated Effort:** 10-14 hours  
**Priority:** MEDIUM  
**Dependencies:** ATTR2-BATCH-04

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the final optimization pass of the ATTR2 Binary Pipeline saga. We have successfully implemented a fast, zero-allocation attribute updating framework. However, there are lingering structural anomalies and micro-optimizations that we've deferred to keep momentum up. This batch groups those remaining `P3` and `P4` debt items from earlier development phases.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-GUIDE.md`
2. **Review Doc:** `.dev-workstream/reviews/ATTR2-BATCH-04-REVIEW.md` 
3. **Debt Tracker:** `docs/attribs2/ATTR2-DEBT-TRACKER.md` (See items 01-05 context)

### Source Code Location
- **Primary Work Areas:**
  - `Hrot.NED/GenericMessages.cs` and potentially `GenericPrimitives.cs`
  - `FDP/Toolkits/FDP.Toolkit.Replication/Patching/JsonToRecordCompiler.cs`
  - `FDP/Toolkits/FDP.Toolkit.Replication/Patching/BinaryPatchContext.cs`
  - `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`
  - `Hrot.SimHost/Installers/SimTransformAttributeInstaller.cs`
- **Test Projects:** `Hrot.NED.Tests`, `FDP.Toolkit.Replication.Tests`, `Hrot.SimHost.Tests`, `Hrot.Map.Common.Tests`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR2-BATCH-05-REPORT.md`

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

### Task 1: OpaqueData Allocation Concern (ATTR2-DEBT-01)

**File:** `Hrot.NED/GenericMessages.cs` / `Hrot.Map.Common/Systems/UpdateEntityAttributeRequestSystem.cs`

**Description:** `CreateUpdateDeleteEntityAck.OpaqueData` using `List<byte>?` creates heap trash per-message allocation for ACK bitmasks. 
**Requirements:**
- Update `OpaqueData` to leverage bounded allocations (e.g. fixed byte sequence in DDS terms, or simply an `byte[]` array depending on how `System.Text.Json` serializers interact with standard collections). Actually, inside DDS IDL terms, `List<byte>` operates normally, but investigate reducing the heap pressure of instantiating `new List<byte>(opaqueMask.ToArray())` inside the `WriteAck` paths. 
- Try to change `OpaqueData` into a `byte[]?` array, allowing `ToArray()` calls directly without the `List` wrapper overhead, or replace with pooled arrays if viable logic exists around ACK emission. 

---

### Task 2: Primitive IDL Extraction (ATTR2-DEBT-02)

**File:** `Hrot.NED/GenericMessages.cs` → `Hrot.NED/GenericPrimitives.cs`

**Description:** Generic primitives (`Vec3f`, `Vec3d`, `Vec4f`) currently reside in `GenericMessages.cs` alongside the `AttributeRecord` definitions which is a code smell.
**Requirements:**
- Extract `Vec3f`, `Vec3d`, and `Vec4f` into a newly created `Hrot.NED/GenericPrimitives.cs`.
- Ensure all DDS annotations (`[DdsStruct]`, `[DdsIdlFile("bdc-sst-generic-msgs")]`) remain correctly preserved so CycloneDDS IDL compilation does not complain.
- Ensure all referencing projects and tests compile cleanly.

---

### Task 3: Edge Compiler String Interning (ATTR2-DEBT-03)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/JsonToRecordCompiler.cs`

**Description:** The Edge Compiler parses string payloads natively but currently allocates new C# raw `string` objects every parse for valid property values, creating some GC pressure in cases where the identical strings are continually sent (e.g., standard faction enums: `"FORCE_OPPOSING"`).
**Requirements:**
- Introduce a basic string interning mechanism or pool for string values within `JsonToRecordCompiler` for `AttributeValueType.KindString` matches. Since these payloads are likely to have high degrees of duplication, checking a small pool/dictionary before returning a new string on the hot path will lower GC usage.
- Ensure it does not break zero-allocations or introduce excessive locking contentions.

---

### Task 4: Scratchpad Predictable Zeroing (ATTR2-DEBT-04)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/BinaryInterpreter.cs` / Domain Installers

**Description:** Domain Installers currently rely on checking an `Initialized` boolean inside scratchpad structs to know if it's the first hit, leading to branching inside the `Apply` loop per-attribute.
**Requirements:**
- Inside `BinaryInterpreter.Apply()` or `CreateContext()`, use `Span<byte>.Clear()` to zero out the entire scratchpad proactively before any installer touches it.
- Remove `Initialized` flags from the Domain Installer scratchpad variables (e.g., `SimTransformAttributeInstaller`) if they can purely rely on the presence of zero-values or tracking variable initialized externally. Note: if pre-filling requires knowledge of a "first hit", find a cleaner loop-invariant approach to handle pre-rolls.

---

### Task 5: Concrete Dispatches (ATTR2-DEBT-05)

**File:** `FDP/Toolkits/FDP.Toolkit.Replication/Patching/JsonToRecordCompiler.cs`

**Description:** The `_routes` field is currently defined as `IReadOnlyDictionary`, enforcing virtual method dispatch which incurs micro-overheads.
**Requirements:**
- Change the typing of the routing structure to a concrete `Dictionary<ulong, EdgeSchemaEntry>` or investigate mapping the ulong hashes to an array-backed structure (e.g., perfect hashing or flat bucket array) avoiding all virtual dispatch on hot paths.
- Balance performance improvement with memory utilization.

---

## 📊 Report Requirements

**Developer Insights:**
**Q1:** What changes were enforced on the test suite when converting `Vec` structs to their own `GenericPrimitives.cs` file? Were any namespace considerations necessary?
**Q2:** Regarding String interning, did you favor an internal `Dictionary` cache or a generic memory cache for strings? How did performance scale?
**Q3:** To achieve array-based dispatch for Edge Schema mapping without massive memory loss, what structure did you pivot to?

---

## 🎯 Success Criteria
- [ ] Task 1 Completed: `CreateUpdateDeleteEntityAck.OpaqueData` allocations minimized.
- [ ] Task 2 Completed: `Vec*` primitives reside in `GenericPrimitives.cs`.
- [ ] Task 3 Completed: String interning implemented smoothly reducing duplicate JSON string allocations.
- [ ] Task 4 Completed: `Apply` scratchpads predictably zeroed via `Span`.
- [ ] Task 5 Completed: Virtual dispatch removed from `_routes`.
- [ ] Code passes all relevant tests in `.Tests` projects.
- [ ] Developer Report (`ATTR2-BATCH-05-REPORT.md`) answers provided.

# ATTR-BATCH-02: Zero-Allocation Compiler & Delegate Registry

**Batch Number:** ATTR-BATCH-02  
**Tasks:** ATTR-S3T1, ATTR-S3T2, ATTR-S4T1, ATTR-S4T2, ATTR-S4T3  
**Phase:** Phase 3 (Zero-Allocation Compiler Core) & Phase 4 (Pre-Compiled Delegate Registry)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** ATTR-BATCH-01  

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to ATTR-BATCH-02. This batch forms the core mechanic of the attributes-to-ecs feature: a zero-allocation JSON compiler backed by `Utf8JsonReader` and `stackalloc`, wired to a registry of pre-compiled native delegates. 

You will implement the streaming parser and the surrounding interface abstractions that will allow the parser to mutate both lists of pre-spawn components and live ECS repositories without knowing the difference. 

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Onboarding Guide:** `docs/attribs-to-ecs/ONBOARDING.md` - Reference for new developers
3. **Design Document:** `docs/attribs-to-ecs/ATTR-DESIGN.md` - Specifically Phase 3 and Phase 4
4. **Task Definitions:** `docs/attribs-to-ecs/ATTR-TASK-DETAIL.md`
5. **Previous Review:** `.dev-workstream/reviews/ATTR-BATCH-01-REVIEW.md` - Learn from feedback

### Source Code Location
- **Primary Work Area:**
  - `Bagira.Map.Common/Replication/Utils/JsonAttributeCompiler.cs` (NEW)
  - `Bagira.Map.Common/Replication/Utils/IEntityPatchContext.cs` (NEW)
  - `Bagira.Map.Common/Replication/Utils/AttributeCompilerBuilder.cs` (NEW)
  - `Bagira.Map.Common/Replication/Utils/ListPatchContext.cs` (NEW)
  - `Bagira.Map.Common/Replication/Utils/EcsPatchContext.cs` (NEW)
- **Test Project:**
  - `Bagira.Map.Common.Tests/Bagira.Map.Common.Tests.csproj`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/ATTR-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/ATTR-BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅
5. **Task 5:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

The previous batch prepared our endpoints by swapping generic structured enums/unions for pure JSON strings. Now we need to process those JSON strings arriving at the SimHost inside the core replication simulation loops, where GC pressure must remain zero.

Normally, deserializing JSON involves dynamic memory allocation. You will avoid this by combining `Utf8JsonReader` with a `stackalloc` struct state machine that tracks node depth and incrementally hashes object paths, translating them into O(1) lookups that dispatch tightly-bound delegates directly against live ECS objects.

**Related Tasks:**
- [ATTR-S3T1](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s3t1--create-jsonattributecompiler-with-utf8jsonreader-streaming) - Create compiler with Utf8JsonReader
- [ATTR-S3T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s3t2--fnv-1a-incremental-path-hashing) - Implement FNV-1a path hashing
- [ATTR-S4T1](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s4t1--define-delegate-types-and-ientitypatchcontext) - Define IEntityPatchContext and delegate types
- [ATTR-S4T2](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s4t2--create-attributecompilerbuilder) - Create AttributeCompilerBuilder
- [ATTR-S4T3](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s4t3--create-listpatchcontext-and-ecspatchcontext) - Implement contexts

---

## 🎯 Batch Objectives
- Build out the pure compiler logic (the `JsonAttributeCompiler` and `AttributeCompilerBuilder`).
- Provide common context dispatching patterns via the `IEntityPatchContext`.
- Ensure everything written in this batch passes aggressive unit-level correctness checking. We do not integrate it into the active Systems until Batch 3.

---

## ✅ Tasks

*(Note: S4T1 should be done first structurally so the compiler has its delegate and interface abstractions to program against, even though it is documented under Phase 4).*

### Task 1: ATTR-S4T1 (Define Context and Delegates)

**File:** `Bagira.Map.Common/Replication/Utils/IEntityPatchContext.cs` (NEW)  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s4t1--define-delegate-types-and-ientitypatchcontext)

**Description:** Define the dual-mode delegate types and the interface that wraps live/spawn component retrieval.

**Requirements:**
- Define `ValueAttributeSetter<T>` passing `ref T component, ReadOnlySpan<int> indices, ref Utf8JsonReader reader`
- Define `ReferenceAttributeSetter<T>` passing `T component, ...`
- Define `IEntityPatchContext` interface

**Tests Required:**
- ✅ `IEntityPatchContext_ValueAttributeSetter_AcceptsRef` (Compile-time test verifying ref pass)

---

### Task 2: ATTR-S4T2 (Create Builder)

**File:** `Bagira.Map.Common/Replication/Utils/AttributeCompilerBuilder.cs` (NEW)  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s4t2--create-attributecompilerbuilder)

**Description:** Provide a registration builder for `ValueAttributeSetter` and `ReferenceAttributeSetter` paths over string keys.

**Requirements:**
- Compute FNV-1a hashes inside the builder for property registrations.
- Block duplicate hash paths.
- Store `descriptorOrdinal` inside the internal `RoutingEntry` struct.

**Tests Required:**
- ✅ `AttributeCompilerBuilder_RegisterValuePath_CanBuildAndCompile`
- ✅ `AttributeCompilerBuilder_DuplicatePath_Throws`
- ✅ `AttributeCompilerBuilder_RegisterReferencePath_CanBuildAndCompile`
- ✅ `AttributeCompilerBuilder_EmptyBuilder_BuildsValidCompilerThatIgnoresAllJson`

---

### Task 3: ATTR-S4T3 (Create Implementations of the Context)

**Files:** `Bagira.Map.Common/Replication/Utils/ListPatchContext.cs`, `EcsPatchContext.cs` (NEW)  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s4t3--create-listpatchcontext-and-ecspatchcontext)

**Description:** Implement the actual contexts wrapping list-based parsing (CreateEntity path) and live-ECS patching (UpdateAttribute path).

**Requirements:**
- Implement lazy instantiations and boxed unmanaged abstractions in `ListPatchContext`. `ListPatchContext.FlushComponents()` retrieves the finalized collection.
- Implement live ECS `GetUnmanagedComponent<T>` mapping onto `repo.GetComponentRW<T>` in `EcsPatchContext`.
- Deduplicate dirty marks per component and trigger `SmartEgressUtil.MarkDirty` in `EcsPatchContext.FlushDirtyMarks()`.

**Tests Required:**
- ✅ `ListPatchContext_GetManagedComponent_ReturnsExistingInstance`
- ✅ `ListPatchContext_GetManagedComponent_CreatesDefaultWhenMissing`
- ✅ `ListPatchContext_FlushComponents_ContainsExactlyOnePerType`
- ✅ `ListPatchContext_OverwriteFlaw_DualPatch_BothChangesPreserved`
- ✅ `EcsPatchContext_GetUnmanagedComponent_ReturnsRefToEcs`
- ✅ `EcsPatchContext_FlushDirtyMarks_CallsSmartEgressForTouchedComponents`
- ✅ `EcsPatchContext_FlushDirtyMarks_DeduplicatesOrdinals`

---

### Task 4 & 5: ATTR-S3T1 & ATTR-S3T2 (Create Streaming Zero-Alloc Compiler Core)

**File:** `Bagira.Map.Common/Replication/Utils/JsonAttributeCompiler.cs` (NEW)  
**Task Definition:** See [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s3t1--create-jsonattributecompiler-with-utf8jsonreader-streaming) and [ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md#attr-s3t2--fnv-1a-incremental-path-hashing)

**Description:** Build the `Utf8JsonReader` state machine and `ulong` hashing stack based against constants.

**Requirements:**
- FNV-1a string hashing incrementally parsing bytes into deep stacks.
- Numeric property replacement via `*` wildcard mechanism.
- `stackalloc ulong[MaxDepth]` and `stackalloc int[MaxDepth * MaxArrayDimensions]` logic.
- Dispatch against `_routes` populated by `AttributeCompilerBuilder`.

**Tests Required:**
- ✅ `JsonAttributeCompiler_NullJson_DoesNotThrow`
- ✅ `JsonAttributeCompiler_EmptyJson_DoesNotThrow`
- ✅ `JsonAttributeCompiler_FlatStringProperty_InvokesDelegate`
- ✅ `JsonAttributeCompiler_NestedProperty_InvokesCorrectDelegate`
- ✅ `JsonAttributeCompiler_UnknownProperty_IsIgnored`
- ✅ `FnvHash_SamePathSameHash` and `DifferentPathDifferentHash`
- ✅ `FnvHash_ArrayIndexNormalisedToWildcard`
- ✅ `FnvHash_DepthRestoreOnEndObject`

---

## 🧪 Testing Requirements

**Quality Standard:** DO NOT test implementation details. DO test behavior. DO assert ACTUAL output.

This batch focuses entirely on data structures, parsers, and delegates. The exact requirements of offset, type matching, and value assertion are extremely critical. Assert all property value edge cases deeply. Ensure your `GetUnmanagedComponent` references legitimately work by checking if a property patched into `ref T` is retrieved as modified over `repo.GetComponentRO<T>`.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

When completing the batch, submit `.dev-workstream/reports/ATTR-BATCH-02-REPORT.md`.

**Developer Insights**  
**Q1:** The `EcsPatchContext` involves extracting deep ECS values. Were there any difficulties mapping the generic `GetComponentRW<T>` out to a generic ref return without violating boxing constraints? How did you resolve them?  
**Q2:** During `JsonAttributeCompiler`'s implementation, did you spot any risk vectors with `Utf8JsonReader` inside `ref structs` propagating out to the delegates?  
**Q3:** The FNV-1a stack hash effectively creates an abstract representation of the JSON graph. Could we repurpose this exact algorithm elsewhere for zero-alloc serialization mapping?  
**Q4:** Did any tests expose a flaw in the `ListPatchContext` merging structure? 

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Task ATTR-S4T1, ATTR-S4T2, ATTR-S4T3 completed. All builders and contexts are robust and unboxed smoothly.
- [ ] Task ATTR-S3T1, ATTR-S3T2 completed. `JsonAttributeCompiler` translates JSON payloads into ECS changes dynamically under zero heap allocations.
- [ ] All 20+ unit tests specific to the struct bounds outlined have passed successfully.
- [ ] Submitted report.

---

## ⚠️ Common Pitfalls to Avoid
- **Boxing your value types:** The largest risk vector in `ListPatchContext` is accidentally assigning an unmanaged `struct` to `var item = _list[x]` locally instead of a strongly tracked boxed reference lookup, defeating the `ref` propagation.
- **Accidentally dropping Utf8JsonReader State:** Keep in mind that `Utf8JsonReader` is passed as `ref`. The user delegate **must** read its token completely off the stack, or else the `JsonAttributeCompiler` loop will get stuck endlessly matching the same node or crash.

---

## 📚 Reference Materials
- **Task Defs:** [docs/attribs-to-ecs/ATTR-TASK-DETAIL.md](docs/attribs-to-ecs/ATTR-TASK-DETAIL.md)
- **Design:** [docs/attribs-to-ecs/ATTR-DESIGN.md](docs/attribs-to-ecs/ATTR-DESIGN.md)

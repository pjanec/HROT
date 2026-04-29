# BATCH-01: Fbt.Compiler — Foundation, BTreeBuilder, and NodeDebugMetadata

**Batch Number:** BATCH-01
**Tasks:** FBT-001, FBT-002, FBT-004
**Phase:** Phase 1 — Fbt.Compiler Fluent Builder Foundation
**Estimated Effort:** 8-10 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the first batch of the Fluent BTree workstream. You will create a **new** `Fbt.Compiler` project in the FastBTree solution, expose `TreeCompiler.FlattenToBlob` as a public API, implement the `BTreeBuilder<TBlackboard>` fluent API, and add `NodeDebugMetadata` to `BehaviorTreeBlob`. Each task must be completed with tests passing before proceeding to the next.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/fluent-btree/DESIGN.md` — Full architecture (especially sections 2.1, 2.6)
2. **Task Details:** `.dev/fluent-btree/TASK-DETAIL.md` — See FBT-001, FBT-002, FBT-004 in full detail
3. **Existing Kernel Code:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` — understand what's already there before touching it
4. **Reference Pattern:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Fhsm.Compiler.csproj` — mirror this project structure

### Source Code Location

- **Existing kernel:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` (modify: `BehaviorTreeBlob.cs`, `Serialization/TreeCompiler.cs`)
- **New project to create:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/` (new `Fbt.Compiler.csproj`)
- **Test project (add to):** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/` (existing, add `Fbt.Compiler` reference)
- **Solution file:** `FDP/ExtDeps/FastBTree/FastBTree.sln` (add new project)

### Build and Test Commands

```powershell
# Build everything
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln --no-restore

# Run tests
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj --no-build
```

### Report Submission

**When done, submit your report to:**
`.dev/fluent-btree/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/fluent-btree/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (FBT-001):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (FBT-002):** Implement → Write tests → **ALL tests pass** ✅
3. **Task 3 (FBT-004):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

Complete the entire batch without stopping to ask permission for obvious things. Run tests, fix issues, repeat until everything passes. Write the report only when all tasks are done.

---

## Context

The FastBTree library currently only supports JSON-based tree authoring (`TreeCompiler.CompileFromJson`). This batch creates the foundation for type-safe C# fluent authoring. The `Fbt.Compiler` project is the new home for the fluent API — it lives above `Fbt.Kernel` in the dependency chain.

**Key constraint:** `Fbt.Kernel` must remain dependency-free (no new external packages). `Fbt.Compiler` is where `System.Linq.Expressions` and debug metadata live.

---

## 🎯 Batch Objectives

1. Expose `TreeCompiler.FlattenToBlob` as a public API that `BTreeBuilder` can use.
2. Create the `Fbt.Compiler` project with `BTreeBuilder<TBlackboard>` — a fluent API for building behavior trees in C# without JSON.
3. Add `NodeDebugMetadata` to `BehaviorTreeBlob` so nodes carry source location and comments.

---

## ✅ Tasks

### Task 1: Public `FlattenToBlob` Overload on `TreeCompiler` (FBT-001)

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` (MODIFY)
**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-001

**What to do:**
- Make the existing private `FlattenToBlob(BuilderNode, string)` into a **public static** method.
- It must also calculate `StructureHash` and `ParamHash`, and invoke `TreeValidator.Validate` — throwing `BehaviorTreeBuildException` on failure (not `InvalidOperationException`; see constraint below).
- Refactor `CompileFromJson` to call this new public method internally (parse JSON → BuilderNode → call public `FlattenToBlob`). `CompileFromJson` behavior must be unchanged from external perspective.
- Add `BehaviorTreeBuildException` to `Fbt.Kernel` if it doesn't exist yet (or reuse existing exception type if one exists already). Check `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` first.
- **New constraint from spec:** `FlattenToBlob` must also throw `BehaviorTreeBuildException` when a nested Parallel or nested Repeater is detected (the validator already may cover this; if so, confirm it with a test and let the exception propagate).

**Also update `BuilderNode`:**
- Add a **public parameterless constructor** to `BuilderNode` so that `BTreeBuilder<TBlackboard>` can create nodes programmatically (not just from JSON). Existing JSON constructor stays. Properties (`Type`, `MethodName`, `WaitTime`, etc.) already exist — they just need to be settable from the new constructor.

**Tests to write** (in `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/TreeCompilerTests.cs` or a new file):
- `FlattenToBlob_EquivalentTree_ProducesSameBlob` — compare JSON-compiled vs directly-compiled blob structure
- `FlattenToBlob_StructureHash_IgnoresMethodNames` — same shape, different method names → same hash
- `FlattenToBlob_ParamHash_DiffersOnFloatParamChange` — Wait(1.0f) vs Wait(2.0f) → different param hashes
- `FlattenToBlob_NestedRepeater_ThrowsBehaviorTreeBuildException`
- `FlattenToBlob_NestedParallel_ThrowsBehaviorTreeBuildException`
- All existing `SerializationTests` (if any) must still pass

**Verify the `BehaviorTreeBuildException` message contains the illegal node type name ("Repeater" or "Parallel").**

---

### Task 2: `BTreeBuilder<TBlackboard>` Fluent API (FBT-002)

**Files to create:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Fbt.Compiler.csproj` (NEW PROJECT)
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` (NEW FILE)

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-002

**Project setup (`Fbt.Compiler.csproj`):**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Fbt.Kernel\Fbt.Kernel.csproj" />
  </ItemGroup>
</Project>
```

Add it to `FDP/ExtDeps/FastBTree/FastBTree.sln`.

**`BTreeBuilder<TBlackboard>` must provide:**
- Composites: `Selector`, `Sequence`, `Parallel` — each accepts a `Action<BTreeBuilder<TBlackboard>> children` lambda and optional `Guid visualId = default`.
- Decorators: `Inverter`, `Repeater(int count, ...)`, `Wait(float duration, ...)`, `Cooldown(float duration, ...)`
- Leaves: `Action(NodeLogicDelegate<TBlackboard, TContext> delegate, Guid visualId = default)`, `Condition(NodeLogicDelegate<TBlackboard, TContext> delegate, Guid visualId = default)`
- `BehaviorTreeBlob Compile(string treeName)` — calls `TreeCompiler.FlattenToBlob`
- `ActionRegistry<TBlackboard, TContext> GetRegistry()` — returns the accumulated action registry

**Key implementation notes:**
- Each leaf method generates a stable `string` key for the `ActionRegistry` using `delegate.Method.DeclaringType!.FullName + "." + delegate.Method.Name`.
- The builder accumulates an internal `BuilderNode` tree and an `ActionRegistry<TBlackboard, TContext>`.
- When `visualId` is `default` (`Guid.Empty`), auto-assign `Guid.NewGuid()`.
- `TContext` is an additional generic parameter on `BTreeBuilder` (i.e., `BTreeBuilder<TBlackboard, TContext>` where `TContext : struct, IAIContext`) OR the `Action`/`Condition` leaf methods are generic overloads. Keep it clean — see how `ActionRegistry<TBlackboard, TContext>` is already defined.

**Fluent chaining:** All builder methods return `this` for chaining **within** a child-building lambda. The `Compile()` method is the terminal call.

Example usage pattern:
```csharp
var builder = new BTreeBuilder<MyBlackboard, MyContext>();
var blob = builder
    .Sequence(seq => seq
        .Condition(MyCondition)
        .Action(MyAction))
    .Compile("MyTree");
var registry = builder.GetRegistry();
```

**Tests to write** (add to `Fbt.Tests`, new file `Unit/BTreeBuilderTests.cs`):
- `Compile_SimpleSequence_ProducesCorrectBlob` — verify node count and types
- `Compile_InterpreterExecutesCorrectly_ConditionFails` — create interpreter from builder, tick with failing condition
- `Compile_NestedComposites_CorrectSubtreeOffsets` — Selector(Sequence(Cond, Action), Action)
- `Compile_DuplicateDelegate_SingleMethodNameEntry` — same delegate used twice → 1 entry in MethodNames
- `Compile_NestedRepeater_ThrowsBehaviorTreeBuildException` — propagates validator exception
- `Compile_VisualIdProvided_StoredInDebugMetadata` — explicit Guid in builder, verify in DebugMetadata
- `Compile_VisualIdOmitted_AutoAssignedNonEmpty` — no explicit Guid, verify auto-assigned

Note: DebugMetadata tests in Task 2 validate the VisualId plumbing from the builder → blob path. The full `NodeDebugMetadata` class is created in Task 3 (FBT-004) — do Task 2 and Task 3 together if it simplifies the implementation; just ensure both are complete and tested before moving on.

---

### Task 3: `NodeDebugMetadata` + `BehaviorTreeBlob.DebugMetadata` (FBT-004)

**Files to modify:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeBlob.cs` — add `[NonSerialized] public NodeDebugMetadata[]? DebugMetadata`
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/NodeDebugMetadata.cs` (NEW FILE in Fbt.Compiler)

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-004

**`NodeDebugMetadata` class** (in `Fbt.Compiler` namespace, NOT `Fbt.Kernel`):
```csharp
public class NodeDebugMetadata
{
    public string Label = string.Empty;
    public string SourceFile = string.Empty;
    public int LineNumber;
    public string CustomComment = string.Empty;
    public string VisualId = string.Empty;
}
```

**`BehaviorTreeBlob` change:**
Add to `BehaviorTreeBlob.cs`:
```csharp
/// <summary>
/// Per-node debug metadata (managed, not serialized). Null for blobs compiled from JSON.
/// When non-null, length equals Nodes.Length.
/// </summary>
[NonSerialized]
public NodeDebugMetadata[]? DebugMetadata;
```

Because `NodeDebugMetadata` lives in `Fbt.Compiler` but `BehaviorTreeBlob` is in `Fbt.Kernel`, you have a dependency problem. Resolve by defining `NodeDebugMetadata` in `Fbt.Kernel` instead (it's a simple data class; it can be `[NonSerialized]` and never referenced by serialization code). Check the design constraint: "Add `NodeDebugMetadata` class in `Fbt.Compiler` (not `Fbt.Kernel`)". The constraint says this because it's managed/debug-only. **To avoid a circular dependency, put `NodeDebugMetadata` in `Fbt.Kernel` anyway** — `Fbt.Kernel` already allows managed classes (it's not a struct-only assembly). This is a deliberate deviation: note it in your report.

**Builder integration:**
- Every fluent method on `BTreeBuilder` accepts `[CallerFilePath] string sourceFile = ""` and `[CallerLineNumber] int lineNumber = 0` as trailing optional parameters.
- When `Compile()` is called, after calling `TreeCompiler.FlattenToBlob`, the builder populates `blob.DebugMetadata` by walking the captured node tree and mapping each `BuilderNode` to its metadata entry.
- Auto-labels:
  - Sequence → `"Sequence"`, Selector → `"Selector"`, Parallel → `"Parallel(policy)"`
  - Wait → `"Wait(Xs)"` where X is the duration (e.g., `"Wait(2.0s)"`)
  - Repeater → `"Repeater(Nx)"` where N is count
  - Cooldown → `"Cooldown(Xs)"`
  - Inverter → `"Inverter"`
  - Action/Condition → method name (from delegate)
- `BinaryTreeSerializer` must NOT be modified — `[NonSerialized]` handles this automatically.

**Tests to write** (add to `Fbt.Tests`, new file `Unit/NodeDebugMetadataTests.cs`):
- `DebugMetadata_IsPopulatedByBuilder_WithCallerInfo` — verify SourceFile and LineNumber
- `DebugMetadata_AutoLabel_SequenceNode` — label equals `"Sequence"`
- `DebugMetadata_AutoLabel_WaitNode_IncludesDuration` — label starts with `"Wait("`
- `DebugMetadata_BinarySerializerRoundTrip_MetadataIsNull` — serialize → deserialize → `DebugMetadata == null`
- `DebugMetadata_Length_EqualsNodeCount` — `blob.DebugMetadata.Length == blob.Nodes.Length`
- `DebugMetadata_JsonCompiledBlob_IsNull` — blobs from `CompileFromJson` have `null` DebugMetadata

---

## 🧪 Testing Requirements

- **Minimum:** 18 tests across all three tasks
- **Test project:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/` — update `Fbt.Tests.csproj` to add a `ProjectReference` to `Fbt.Compiler.csproj`
- **All existing tests must continue to pass**
- Tests must verify **actual behavior** — not just compilation or string presence
- Integration tests (Interpreter created from builder, actually ticked) are mandatory for FBT-002

---

## ⚠️ Quality Standards

**TEST QUALITY EXPECTATIONS**
- NOT ACCEPTABLE: Tests that only check that a property is not null
- REQUIRED: Tests that verify actual values — node types, counts, hashes, delegate invocations, exception messages

**REPORT QUALITY EXPECTATIONS**
- REQUIRED: Document design decisions made beyond the spec (especially the NodeDebugMetadata placement decision)
- REQUIRED: Document any issues encountered and how you resolved them
- REQUIRED: Provide the final passing test count

---

## 📊 Report Requirements

Create `.dev/fluent-btree/reports/BATCH-01-REPORT.md` with:

```markdown
# BATCH-01 Report

## Summary
[Brief description of what was implemented]

## Tasks Completed
- [ ] FBT-001: Public FlattenToBlob overload
- [ ] FBT-002: BTreeBuilder<TBlackboard> fluent API
- [ ] FBT-004: NodeDebugMetadata + BehaviorTreeBlob.DebugMetadata

## Test Results
Total passing: XX / XX
[List test files created]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** What design decisions did you make beyond the spec? What alternatives did you consider?

**Q3:** Did you spot any weak points in the existing codebase that should be addressed?

**Q4:** What edge cases did you discover that weren't mentioned in the instructions?

**Suggested commit message:**
```
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Fbt.Compiler.csproj` created and added to solution
- [ ] `TreeCompiler.FlattenToBlob(BuilderNode, string)` is public and validates/hashes
- [ ] `CompileFromJson` delegates to public `FlattenToBlob` (no behavior change)
- [ ] `BTreeBuilder<TBlackboard, TContext>` exists and compiles correct blobs
- [ ] `NodeDebugMetadata` exists and is populated by builder
- [ ] `BehaviorTreeBlob.DebugMetadata` field exists, `[NonSerialized]`
- [ ] All tests pass: `dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj`
- [ ] No compiler errors or warnings in `FastBTree.sln`
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

- `BuilderNode` only has a JSON constructor — you MUST add a parameterless/programmatic constructor for the builder to use.
- `FlattenToBlob` must compute hashes AND validate before returning — don't forget either step.
- `NodeDebugMetadata` placement: the design says `Fbt.Compiler` but that creates a circular dependency. Put it in `Fbt.Kernel` and note this decision in your report.
- `TreatWarningsAsErrors` is enabled — fix all warnings.
- `DebugMetadata` length must equal `Nodes.Length` — map each `BuilderNode` in depth-first order to the correct metadata slot.

---

## 📚 Reference Materials

- **Task Defs:** `.dev/fluent-btree/TASK-DETAIL.md` — FBT-001, FBT-002, FBT-004
- **Design:** `.dev/fluent-btree/DESIGN.md` — §2.1 (Fluent Builder), §2.6 (Node Debug Metadata)
- **Existing kernel:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs`
- **Existing blob:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeBlob.cs`
- **Reference compiler project:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Fhsm.Compiler.csproj`
- **Test project:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj`

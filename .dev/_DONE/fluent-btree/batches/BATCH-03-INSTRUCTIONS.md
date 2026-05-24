# BATCH-03: Phase 1 Tests, Marker Attributes, and Schema Exporter

**Batch Number:** BATCH-03
**Tasks:** FBT-006, FBT-010, FBT-007
**Phase:** Phase 1 (tests) + Phase 2 (attributes foundation)
**Estimated Effort:** 7-9 hours
**Priority:** HIGH
**Dependencies:** BATCH-01, BATCH-02 (all Phase 1 implementation complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes Phase 1 of the Fluent BTree workstream:
1. **FBT-006** — Full validation test suite for Phase 1 (covering negative paths — `BuilderValidationTests`). The individual task tests were written in BATCH-01 and BATCH-02; this task adds the consolidated validation tests and fills any gaps.
2. **FBT-010** — Define marker attributes (`[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, `[FbtRegistrar]`) in `Fbt.Kernel`.
3. **FBT-007** — `BTreeSchemaExporter` static class in `Fbt.Compiler` — scans assemblies for `[BTreeAction]`/`[BTreeCondition]` methods and emits a `BTreeSchema.json`.

### Required Reading (IN ORDER)

1. **Task Details:** `.dev/fluent-btree/TASK-DETAIL.md` — FBT-006, FBT-010, FBT-007 in full
2. **Design Document:** `.dev/fluent-btree/DESIGN.md` — §2.3 (attributes), §2.10 (schema exporter)
3. **Previous Reviews:** `.dev/fluent-btree/reviews/BATCH-01-REVIEW.md`, `.dev/fluent-btree/reviews/BATCH-02-REVIEW.md`
4. **Existing builder:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`
5. **Reference HSM action generator attributes:** `FDP/ExtDeps/FastHSM/src/Fbt.Kernel/` (check if FastHSM has attribute classes to mirror)

### Source Code Location

- **FBT-006 tests:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BuilderValidationTests.cs` (NEW FILE)
- **FBT-010 attributes:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/` (new subdirectory with 4 attribute files)
- **FBT-007 schema exporter:**
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeSchemaExporter.cs` (NEW FILE)
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeSchema.cs` (NEW FILE — schema data types)
- **FBT-007 tests:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BTreeSchemaExporterTests.cs` (NEW FILE)

### Build and Test Commands

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln --no-restore -v quiet 2>&1 | Select-String "error|Build succeeded|FAILED"
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 3
```

### Report Submission

**When done, submit your report to:**
`.dev/fluent-btree/reports/BATCH-03-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1 (FBT-006):** Write validation tests → **ALL tests pass** ✅
2. **Task 2 (FBT-010):** Define attributes → **ALL tests pass** ✅
3. **Task 3 (FBT-007):** Implement schema exporter with tests → **ALL tests pass** ✅

Complete the entire batch without stopping to ask for confirmation. Fix all errors and run all tests until everything passes, then write the report.

---

## Context

Phase 1 implementation is complete (BATCH-01 + BATCH-02). This batch closes Phase 1 by completing the test suite, introduces the marker attributes required by Phase 2 (source generator), and implements the schema exporter utility.

The attributes (`[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, `[FbtRegistrar]`) are defined in `Fbt.Kernel` (already a dependency of `Fbt.Compiler`), making them available to user code and the future `Fbt.SourceGen` source generator.

---

## 🎯 Batch Objectives

1. Consolidate validation tests for the full Phase 1 pipeline (including the mandatory negative-path tests from the FBT-006 spec).
2. Define the four marker attributes in `Fbt.Kernel`.
3. Implement `BTreeSchemaExporter` that scans assemblies for marked delegates and produces a JSON schema file.

---

## ✅ Tasks

### Task 1: Phase 1 Validation Tests (FBT-006)

**File:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BuilderValidationTests.cs` (NEW FILE)

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-006

Check what validation tests already exist in BATCH-01/02 tests. The FBT-006 task requires a **dedicated `BuilderValidationTests` class** with these mandatory tests:

**Mandatory tests in `BuilderValidationTests`:**
- `NestedRepeater_ThrowsBehaviorTreeBuildException` — `.Repeater(2, r => r.Repeater(3, ...))` must throw `BehaviorTreeBuildException` with message containing "Repeater" and "nested"
- `NestedParallel_ThrowsBehaviorTreeBuildException` — `.Parallel(0, p => p.Parallel(0, ...))` must throw with "Parallel" and "nested"
- `DtoTooLarge_ThrowsBehaviorTreeBuildException` — Using expression binding with a DTO struct whose `sizeof` exceeds 128 bytes must throw `BehaviorTreeBuildException` at `Compile()` time. Since `BehaviorTreeBuildException` is currently thrown for nested nodes, but NOT for DTO-too-large (this wasn't implemented in FBT-001 for expression binding), you need to ADD this check to the `Action<TValue>` / `Condition<TValue>` overloads in `BTreeBuilder.cs`: **after computing `Marshal.OffsetOf`, check if `Marshal.SizeOf<TBlackboard>() > 128` and throw `BehaviorTreeBuildException` if so.** The check should be `Marshal.SizeOf<TBlackboard>() > BehaviorConstants.BrainBlackboardByteSize`. Check what `BehaviorConstants` is — look in `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` for any constants file; if it doesn't exist, define the constant as `private const int MaxBlackboardSize = 128` directly in the check, or create a `BehaviorConstants.cs` in `Fbt.Kernel`.
- `ValidTree_DoesNotThrow` — a correctly structured tree compiles without exception (control test)

**Additional recommended tests** to fill any coverage gaps not already covered by BATCH-01/02:
- `EmptyBuilder_Compile_ThrowsInvalidOperationException` — calling `Compile()` with no nodes added
- `TwoRootNodes_Compile_ThrowsInvalidOperationException` — builder with two top-level entries
- `Condition_ReturnsFailure_ActionNotCalled` — verify that in a Sequence, when condition fails, the action is never called (integration)
- Any SC from TASK-FBT-001/002/003/004/005 that doesn't yet have a test (cross-reference with existing test files)

Minimum 6 tests in `BuilderValidationTests`.

---

### Task 2: Marker Attributes (FBT-010)

**Files to create (in `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/`):**
- `BTreeActionAttribute.cs`
- `BTreeConditionAttribute.cs`
- `BTreeDefinitionAttribute.cs`
- `FbtRegistrarAttribute.cs`

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-010

**Attribute definitions:**

```csharp
// BTreeActionAttribute.cs
using System;

namespace Fbt
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeActionAttribute : Attribute { }
}

// BTreeConditionAttribute.cs
using System;

namespace Fbt
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeConditionAttribute : Attribute { }
}

// BTreeDefinitionAttribute.cs
using System;

namespace Fbt
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDefinitionAttribute : Attribute
    {
        public string TreeName { get; }

        public BTreeDefinitionAttribute(string treeName)
        {
            TreeName = treeName;
        }
    }
}

// FbtRegistrarAttribute.cs
using System;

namespace Fbt
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class FbtRegistrarAttribute : Attribute { }
}
```

**Tests** (add to `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BuilderValidationTests.cs` or create a new `AttributeTests.cs`):
- `BTreeActionAttribute_CanBeAppliedToMethod` — verify a static method can be annotated; check `method.GetCustomAttribute<BTreeActionAttribute>() != null`
- `BTreeDefinitionAttribute_ExposesTreeName` — annotate a method with `[BTreeDefinition("TestTree")]`, verify `attr.TreeName == "TestTree"`
- `FbtRegistrarAttribute_CanBeAppliedToClass`

3 tests for FBT-010.

---

### Task 3: `BTreeSchemaExporter` (FBT-007)

**Files to create:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeSchema.cs` — schema data types
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeSchemaExporter.cs` — static exporter class
- `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/BTreeSchemaExporterTests.cs` — tests

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-007

**`BTreeSchema.cs`** (namespace `Fbt.Compiler`):
```csharp
public record ActionDescriptor(
    string MethodName,
    string DeclaringType,
    string BlackboardDtoType,
    string FieldName,
    int FieldOffset);

public record ConditionDescriptor(
    string MethodName,
    string DeclaringType,
    string BlackboardDtoType,
    string FieldName,
    int FieldOffset);

public record BTreeSchema(
    ActionDescriptor[] Actions,
    ConditionDescriptor[] Conditions,
    string[] BlackboardDtoTypes);
```

**`BTreeSchemaExporter.cs`** (namespace `Fbt.Compiler`):
- `public static BTreeSchema Export(IEnumerable<Assembly> assemblies)` — scans assemblies for `[BTreeAction]` and `[BTreeCondition]` methods
- `public static void ExportToJson(BTreeSchema schema, string outputPath)` — serialises to JSON via `System.Text.Json`
- For each found `[BTreeAction]` / `[BTreeCondition]` method:
  - `MethodName` = `method.Name`
  - `DeclaringType` = `method.DeclaringType?.FullName ?? ""`
  - If the method's first parameter is a ref to a reusable delegate value type (i.e., `ref TValue`), use `TValue` as `BlackboardDtoType` and use `Marshal.OffsetOf` for the field; otherwise use the full blackboard type name and `FieldOffset = -1` (no projection).
  - In practice for the schema exporter: inspect the method signature. If it matches `(ref TValue, ref BehaviorTreeState, ref TContext)`, it's a reusable delegate; if it matches `(ref TBlackboard, ref BehaviorTreeState, ref TContext, int)`, it's a full-blackboard delegate. The schema exporter simply records what it finds — it does NOT need to compute offsets for full-blackboard delegates.
  - For simplicity: the scanner looks at parameter types. If the method has 3 parameters (excluding `this`), it's a reusable delegate and the first param type is `TValue`. If 4 parameters, it's a full-blackboard delegate and `FieldOffset = -1`.
- `BlackboardDtoTypes` = distinct set of all `BlackboardDtoType` values across actions and conditions.
- Wrap each assembly in `try/catch` and skip non-reflectable assemblies.
- `ExportToJson` must NOT throw if Actions and Conditions are empty.

**Tests** (new file `BTreeSchemaExporterTests.cs`):
- `Export_FindsAllMarkedMethods_InTestAssembly` — define test methods in the test file with `[BTreeAction]` / `[BTreeCondition]`, export from `Assembly.GetExecutingAssembly()`, verify counts
- `Export_FieldOffset_IsCorrect_ForReusableDelegate` — `[BTreeAction]` method with `ref float` first param on a struct where float is at offset 4; verify `FieldOffset == 4` (you'll need to compute this from the method signature and the known struct layout — or use a known struct and verify the offset)
- `ExportToJson_ProducesValidJson_ThatRoundTrips` — export, write to temp file, deserialize via `System.Text.Json`, verify counts match
- `ExportToJson_EmptyAssembly_DoesNotThrow` — pass `new Assembly[0]`; verify empty schema returned without exception
- `Export_NonReflectableAssembly_DoesNotThrow` — skip test if no way to generate a non-reflectable assembly; otherwise pass a mock/null entry — simply verify no exception

5 tests for FBT-007.

**Important — scanning for reusable delegate signatures:**
The test methods you define in the test file (for scanning) should look like this:
```csharp
// These are in the TEST file — they serve as schema-scan targets
[BTreeAction]
private static NodeStatus TestSchemaAction(ref float value, ref BehaviorTreeState state, ref MockContext ctx)
    => NodeStatus.Success;

[BTreeCondition]
private static NodeStatus TestSchemaCondition(ref int value, ref BehaviorTreeState state, ref MockContext ctx)
    => data > 0 ? NodeStatus.Success : NodeStatus.Failure;
```

For the field offset test: since we don't know which struct the `ref float` belongs to from the method signature alone (the schema exporter has no blackboard context here), the `FieldOffset` for reusable delegates in the schema exporter is better set to `-1` (unknown at schema scan time) unless the method is annotated with additional metadata. **Simplification:** for this task, set `FieldOffset = -1` for all scanned methods. The Roslyn source generator (Phase 2) computes real offsets from type symbols at compile time. The schema exporter just enumerates what actions/conditions exist; the authoring tool resolves offsets through the source generator. Note this simplification in your report.

---

## 🧪 Testing Requirements

- **Minimum:** 14 new tests (6 validation + 3 attributes + 5 schema)
- **All 108 existing tests must continue to pass**
- Validation tests must verify exception message content (not just type)
- Schema exporter tests must verify actual scan results, not just "no exception"

---

## ⚠️ Quality Standards

**TEST QUALITY EXPECTATIONS**
- `BuilderValidationTests` must cover actual negative paths — test that invalid constructs throw with informative messages
- Schema exporter tests must verify actual counts of found methods

**CODE QUALITY EXPECTATIONS**
- `TreatWarningsAsErrors` is enabled — no warnings allowed
- All attributes must have `sealed` and correct `AttributeUsage`
- `ExportToJson` must use `System.Text.Json` (no Newtonsoft)
- `BTreeSchema` types should be `record` or `record class` for clean serialization

---

## 📊 Report Requirements

Create `.dev/fluent-btree/reports/BATCH-03-REPORT.md` with:

```markdown
# BATCH-03 Report

## Summary

## Tasks Completed
- [ ] FBT-006: BuilderValidationTests (Phase 1 validation test suite)
- [ ] FBT-010: Marker attributes ([BTreeAction], [BTreeCondition], [BTreeDefinition], [FbtRegistrar])
- [ ] FBT-007: BTreeSchemaExporter + BTreeSchema

## Test Results
Total passing: XX / XX
[List new test files]

## Developer Insights

**Q1:** Issues encountered and how resolved?

**Q2:** Design decisions made beyond the spec?

**Q3:** Weak points observed?

**Q4:** Edge cases discovered?

**Suggested commit message:**
```
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `BuilderValidationTests` exists with all 4 mandatory tests + extras
- [ ] `BTreeActionAttribute`, `BTreeConditionAttribute`, `BTreeDefinitionAttribute`, `FbtRegistrarAttribute` exist in `Fbt.Kernel/Attributes/`
- [ ] `BTreeSchemaExporter.Export` and `ExportToJson` exist in `Fbt.Compiler`
- [ ] All tests pass: `dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj`
- [ ] No compiler errors or warnings
- [ ] Report submitted

---

## ⚠️ Common Pitfalls

- Check that `BehaviorConstants` or a max-blackboard-size constant is defined before adding the DTO-too-large check. Create it if needed in `Fbt.Kernel`.
- The `DtoTooLarge_ThrowsBehaviorTreeBuildException` test uses **expression binding** (the `Condition<TValue>` or `Action<TValue>` overload). The check for DTO-too-large must be added to those overloads in `BTreeBuilder.cs`, not to `TreeCompiler.FlattenToBlob` directly (the blob doesn't know about DTO sizes — the builder does via `Marshal.SizeOf<TBlackboard>()`).
- For schema exporter attribute scanning: use `method.IsDefined(typeof(BTreeActionAttribute), false)` rather than `GetCustomAttributes` for performance.
- `System.Text.Json` requires records to have appropriate constructors or be annotated; `record` with positional parameters works cleanly.

---

## 📚 Reference Materials

- **Task Defs:** `.dev/fluent-btree/TASK-DETAIL.md` — FBT-006, FBT-010, FBT-007
- **Design:** `.dev/fluent-btree/DESIGN.md` — §2.3, §2.10
- **Existing builder:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`
- **Kernel directory:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/`
- **FastHSM reference:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` (check for attribute/constant examples)

# BATCH-02: Expression-Based Offset Resolution + Graph Data Structures

**Batch Number:** BATCH-02
**Tasks:** FBT-003, FBT-005
**Phase:** Phase 1 — Fbt.Compiler Fluent Builder Foundation (continuation)
**Estimated Effort:** 7-9 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (Fbt.Compiler project must exist, BTreeBuilder must be working)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch adds two features to `Fbt.Compiler`:
1. **FBT-003:** Generic overloads on `BTreeBuilder<TBlackboard, TContext>` that accept an `Expression<Func<TBlackboard, TValue>>` lambda to identify a blackboard field, compute its byte offset at builder time (not at tick time), and register a curried closure in the `ActionRegistry`.
2. **FBT-005:** Graph data structures (`BehaviorTreeGraph`, `BehaviorTreeNode`, etc.) in the `Fbt.Compiler.Graph` namespace, plus a `ToGraph()` method on `BTreeBuilder`.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/fluent-btree/DESIGN.md` — §2.2 (Lambda-Based Blackboard Parameter Binding), §2.10 (Future Authoring Tool Support)
2. **Task Details:** `.dev/fluent-btree/TASK-DETAIL.md` — TASK-FBT-003, TASK-FBT-005 in full
3. **Previous Review:** `.dev/fluent-btree/reviews/BATCH-01-REVIEW.md` — understand what was built
4. **Existing builder:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` — your starting point
5. **FastHSM graph reference:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/` — mirror this structure

### Source Code Location

- **Modify:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`
- **New files in Fbt.Compiler:**
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/ReusableDelegates.cs` — delegate types
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Graph/BehaviorTreeGraph.cs`
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Graph/BehaviorTreeNode.cs`
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Graph/CompositeNode.cs`
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Graph/DecoratorNode.cs`
  - `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Graph/LogicNode.cs`
- **Test project:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/` (already references Fbt.Compiler)

### Build and Test Commands

```powershell
# Restore + build
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/ExtDeps/FastBTree/FastBTree.sln --no-restore -v quiet 2>&1 | Select-String "error|Build succeeded|FAILED"

# Run tests
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!|Error" | Select-Object -Last 3
```

### Report Submission

**When done, submit your report to:**
`.dev/fluent-btree/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/fluent-btree/questions/BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1 (FBT-003):** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2 (FBT-005):** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until all tests pass. Complete the full batch without stopping to ask for confirmation. Fix all errors, run all tests, write the report only when everything passes.

---

## Context

BATCH-01 produced a `BTreeBuilder<TBlackboard, TContext>` that works with `NodeLogicDelegate<TBlackboard, TContext>` — delegates that receive the full blackboard by reference. This batch adds a more powerful pattern: delegates that receive only a **projected sub-field** of the blackboard via a curried closure, so reusable delegates can be written against strongly-typed sub-DTOs rather than the full blackboard.

The graph data structures (FBT-005) are pure data — no execution logic — intended for the future authoring tool.

---

## 🎯 Batch Objectives

1. Add `Condition<TValue>` and `Action<TValue>` generic overloads that accept `Expression<Func<TBlackboard, TValue>>` field selectors, compute the byte offset at builder time, and register curried closures in the `ActionRegistry`.
2. Add `BehaviorTreeGraph` and related node classes to `Fbt.Compiler.Graph`, plus `ToGraph()` on `BTreeBuilder`.

---

## ✅ Tasks

### Task 1: Expression-Based Blackboard Parameter Binding (FBT-003)

**Files to modify/create:**
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` — add generic `Action<TValue>` / `Condition<TValue>` overloads
- `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/ReusableDelegates.cs` — new file with delegate type definitions

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-003 for full spec.

**Delegate types** (in namespace `Fbt.Compiler`, file `ReusableDelegates.cs`):
```csharp
public delegate NodeStatus ReusableConditionDelegate<TValue>(
    ref TValue data, ref BehaviorTreeState state, ref BTreeContext ctx)
    where TValue : unmanaged;

public delegate NodeStatus ReusableActionDelegate<TValue>(
    ref TValue data, ref BehaviorTreeState state, ref BTreeContext ctx)
    where TValue : unmanaged;
```

Wait — `BTreeContext` is not a real type in the existing codebase. The existing `ActionRegistry<TBlackboard, TContext>` uses `TContext` as a type parameter. The `ReusableXDelegate<TValue>` must also accept `TContext` or a concrete context type. Check what context types exist in `Fbt.Kernel`:
- `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/IAIContext.cs` — see if there's a concrete context or just the interface.
- Adapt the delegate types to use `TContext` as a type parameter too, or use the `IAIContext` interface. Mirror the style used in `NodeLogicDelegate<TBlackboard, TContext>`.

**Correct delegate signature** (mirror `NodeLogicDelegate<TBlackboard, TContext>`):
```csharp
public delegate NodeStatus ReusableConditionDelegate<TValue, TContext>(
    ref TValue data, ref BehaviorTreeState state, ref TContext ctx)
    where TValue : unmanaged
    where TContext : struct, IAIContext;

public delegate NodeStatus ReusableActionDelegate<TValue, TContext>(
    ref TValue data, ref BehaviorTreeState state, ref TContext ctx)
    where TValue : unmanaged
    where TContext : struct, IAIContext;
```

**Generic overloads on `BTreeBuilder<TBlackboard, TContext>`:**
```csharp
public BTreeBuilder<TBlackboard, TContext> Condition<TValue>(
    Expression<Func<TBlackboard, TValue>> fieldSelector,
    ReusableConditionDelegate<TValue, TContext> logic,
    Guid visualId = default,
    [CallerFilePath] string sourceFile = "",
    [CallerLineNumber] int lineNumber = 0)
    where TValue : unmanaged
```
and the matching `Action<TValue>` overload.

**How the curried closure works:**

1. Walk the `Expression` tree to extract the member name:
   ```csharp
   // fieldSelector: dto => dto.AmmoCount
   // MemberExpression.Member.Name == "AmmoCount"
   if (fieldSelector.Body is not MemberExpression memberExpr)
       throw new ArgumentException("fieldSelector must be a direct field/property access", nameof(fieldSelector));
   string memberName = memberExpr.Member.Name;
   ```

2. Compute byte offset using `Marshal.OffsetOf<TBlackboard>`:
   ```csharp
   // TBlackboard must have StructLayout(LayoutKind.Sequential) for offset to be reliable
   IntPtr offset = Marshal.OffsetOf<TBlackboard>(memberName);
   ```

3. Register a `NodeLogicDelegate<TBlackboard, TContext>` closure in the `ActionRegistry` that projects the blackboard and calls `logic`:
   ```csharp
   NodeLogicDelegate<TBlackboard, TContext> curried = (ref TBlackboard bb, ref BehaviorTreeState st, ref TContext ctx, int _) =>
   {
       ref TValue projected = ref Unsafe.As<TBlackboard, TValue>(
           ref Unsafe.AddByteOffset(ref bb, offset));
       return logic(ref projected, ref st, ref ctx);
   };
   ```
   Note: `Unsafe.AddByteOffset` takes a `nint` or `IntPtr`; use the overload that matches your .NET 8 target.

4. Generate registry key combining delegate method info AND byte offset (for stable deduplication across different fields):
   ```csharp
   string key = $"{logic.Method.DeclaringType!.FullName}.{logic.Method.Name}@{offset}";
   ```

**Important constraints from DESIGN.md §2.2 and TASK-DETAIL.md FBT-003:**
- `Marshal.OffsetOf` is called ONCE at tree-build time, never during `Interpreter.Tick`.
- `TBlackboard` is already `struct` (constrained by `BTreeBuilder<TBlackboard, TContext>`).
- `TValue` must be `unmanaged` (enforced by generic constraint).
- Only direct field/property access is required; nested access may throw `ArgumentException`.
- The `Unsafe` calls do NOT require an `unsafe` block — use the safe `System.Runtime.CompilerServices.Unsafe` API.
- Add `<PackageReference Include="System.Runtime.CompilerServices.Unsafe" .../>` to `Fbt.Compiler.csproj` only if .NET 8 doesn't include it natively. On net8.0 it is included via BCL; do NOT add a package reference if it already works.

**Tests to write** (new file `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/ExpressionBindingTests.cs`):
- `Condition_LambdaFieldSelector_ComputesCorrectByteOffset` — struct with known layout; verify the closure reads from the correct offset by setting that field and checking condition result
- `Action_LambdaFieldSelector_MutatesCorrectField` — action delegate decrements `ref TValue`; verify the correct field of the blackboard changes
- `Condition_FieldB_WhenFieldBPositive_ReturnsSuccess` — `struct { int FieldA; float FieldB; }`, condition reads `FieldB`; set FieldB > 0 and verify `Success`
- `Condition_FieldB_WhenFieldBNegative_ReturnsFailure` — same struct, FieldB < 0
- `Action_DecrementsAmmo_Correctly` — `struct { int AmmoCount; }`, action decrements; tick twice; verify AmmoCount decreases by 2
- `ExpressionBinding_InvalidExpression_ThrowsArgumentException` — pass a non-member expression (e.g., a constant) and verify `ArgumentException`
- `ExpressionBinding_RegistryKey_IsStableAcrossBuilds` — build tree twice with same delegate+field; verify MethodNames entries are deduplicated

Minimum 7 tests for FBT-003.

---

### Task 2: Graph Data Structures for Authoring Tool (FBT-005)

**Files to create (all in `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Graph/`):**
- `BehaviorTreeGraph.cs`
- `BehaviorTreeNode.cs`
- `CompositeNode.cs`
- `DecoratorNode.cs`
- `LogicNode.cs`

**Also modify:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` — add `ToGraph(string treeName)` method

**Task Definition:** See `.dev/fluent-btree/TASK-DETAIL.md` → TASK-FBT-005 for the full class structure.

**Key class definitions** (namespace `Fbt.Compiler.Graph`):

```csharp
// BehaviorTreeGraph.cs
public class BehaviorTreeGraph
{
    public string TreeName = string.Empty;
    public Guid TreeId = Guid.NewGuid();
    public BehaviorTreeNode? RootNode;
}

// BehaviorTreeNode.cs (abstract)
public abstract class BehaviorTreeNode
{
    public Guid VisualId = Guid.NewGuid();
    public NodeType Type;
    public BehaviorTreeNode? Parent;
    public float UiPosX;
    public float UiPosY;
    public string CustomComment = string.Empty;
}

// CompositeNode.cs
public class CompositeNode : BehaviorTreeNode
{
    public List<BehaviorTreeNode> Children = new List<BehaviorTreeNode>();
    public int ParallelPolicy;
}

// DecoratorNode.cs
public class DecoratorNode : BehaviorTreeNode
{
    public BehaviorTreeNode? Child;
    public float Duration;
    public int RepeatCount;
}

// LogicNode.cs
public class LogicNode : BehaviorTreeNode
{
    public string DelegateName = string.Empty;
    public string TargetDtoType = string.Empty;
    public string TargetFieldName = string.Empty;
}
```

**`BTreeBuilder<TBlackboard, TContext>.ToGraph(string treeName)`:**
- Converts the accumulated `BuilderEntry` tree to a `BehaviorTreeGraph`.
- Maps each `BuilderEntry.Node.Type` to the correct graph node subclass.
- Composites → `CompositeNode` with children from `BuilderEntry.ChildEntries`.
- Decorators (Repeater, Inverter, Cooldown, Wait) → `DecoratorNode`.
- Leaves (Action, Condition) → `LogicNode` with `DelegateName` from the registered method name.
- Expression-bound leaves (from FBT-003 overloads) → `LogicNode` with `TargetDtoType` and `TargetFieldName` populated.
- `VisualId` on each graph node must match the `VisualId` from the corresponding `NodeDebugMetadata` in the builder entry, ensuring round-trip identity.
- `Parent` references must be correctly set.
- The graph must call `ToGraph()` before `Compile()` is required — it operates on the builder state, not the compiled blob.

**Tests to write** (new file `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/GraphTests.cs`):
- `ToGraph_SimpleSequence_ProducesCorrectRootNode` — verify `RootNode is CompositeNode` with `Type == Sequence`
- `ToGraph_ChildCount_MatchesBuilderChildren` — sequence with 2 actions → `CompositeNode.Children.Count == 2`
- `ToGraph_LeafNodes_AreLogicNodes` — action/condition entries → `LogicNode`
- `ToGraph_AllNodes_HaveUniqueNonEmptyVisualIds` — no two nodes share a `VisualId`, none is `Guid.Empty`
- `ToGraph_ParentRefs_AreCorrect` — child's `Parent == root`
- `ToGraph_ExpressionBound_LogicNode_HasTargetFieldName` — expression-bound action → `TargetFieldName` is the field name
- `ToGraph_TreeId_IsNonEmpty` — `graph.TreeId != Guid.Empty`

Minimum 7 tests for FBT-005.

---

## 🧪 Testing Requirements

- **Minimum:** 14 new tests (7 for FBT-003 + 7 for FBT-005)
- **All 94 existing tests must continue to pass**
- Tests must verify **actual values** — byte offsets, field mutations, correct node subclass types, parent references
- Do NOT write tests that only check "node is not null" — check actual field values and behavior

---

## ⚠️ Quality Standards

**TEST QUALITY EXPECTATIONS**
- REQUIRED: FBT-003 tests must actually tick the interpreter with a known blackboard state and verify the result — not just verify that the registry key was registered
- REQUIRED: FBT-005 tests must verify the full graph structure (node types, children, parent refs, VisualId uniqueness)

**CODE QUALITY EXPECTATIONS**
- `TreatWarningsAsErrors` is enabled — fix all warnings
- `Unsafe` calls must NOT use `unsafe` blocks — use `System.Runtime.CompilerServices.Unsafe` static methods only
- `Marshal.OffsetOf<TBlackboard>(memberName)` — if `TBlackboard` does not have `[StructLayout(LayoutKind.Sequential)]`, the offset may be unreliable. Add a guard or require sequential layout in the generic constraint check. At minimum, document this requirement in a comment.

---

## 📊 Report Requirements

Create `.dev/fluent-btree/reports/BATCH-02-REPORT.md` with:

```markdown
# BATCH-02 Report

## Summary
[Brief description of what was implemented]

## Tasks Completed
- [ ] FBT-003: Expression-based blackboard offset resolution
- [ ] FBT-005: Graph data structures + BTreeBuilder.ToGraph()

## Test Results
Total passing: XX / XX (including all 94 existing tests)
[List new test files]

## Developer Insights

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** What design decisions did you make beyond the spec?

**Q3:** Did you spot any weak points or improvement opportunities?

**Q4:** Edge cases discovered?

**Suggested commit message:**
```
```

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `BTreeBuilder.Condition<TValue>` and `Action<TValue>` overloads exist and register curried closures
- [ ] Byte offset computed via `Marshal.OffsetOf` at builder time, used in closure via `Unsafe.AddByteOffset`
- [ ] `BehaviorTreeGraph`, `BehaviorTreeNode`, `CompositeNode`, `DecoratorNode`, `LogicNode` exist in `Fbt.Compiler.Graph`
- [ ] `BTreeBuilder.ToGraph(string)` returns a correctly structured graph
- [ ] All tests pass: `dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj`
- [ ] No compiler errors or warnings in `FastBTree.sln`
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

- The `Unsafe.AddByteOffset` signature differs between .NET versions. On net8.0 use: `Unsafe.AddByteOffset(ref T source, nint byteOffset)`. Cast `Marshal.OffsetOf<T>()` result to `nint` if needed.
- `[StructLayout(LayoutKind.Sequential)]` is needed on blackboard DTOs for `Marshal.OffsetOf` to be reliable. Add a check or comment about this requirement.
- `ToGraph()` must work BEFORE calling `Compile()` — it reads the builder's internal `_entries` tree, not the compiled blob.
- `VisualId` on graph nodes must match the builder's assigned `VisualId` (not generate new random ones), to maintain the round-trip identity guarantee.
- Expression `lambda.Body` might be wrapped in `UnaryExpression` (boxing conversion). Check for `Convert(MemberExpression)` as well as bare `MemberExpression`.

---

## 📚 Reference Materials

- **Task Defs:** `.dev/fluent-btree/TASK-DETAIL.md` — FBT-003, FBT-005
- **Design:** `.dev/fluent-btree/DESIGN.md` — §2.2, §2.10
- **Existing builder:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs`
- **Reference graph classes:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/`
- **Previous review:** `.dev/fluent-btree/reviews/BATCH-01-REVIEW.md`

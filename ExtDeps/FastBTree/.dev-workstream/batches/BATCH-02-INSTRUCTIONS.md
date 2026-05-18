# BATCH-02: Core Interpreter Implementation

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Batch Number:** 02  
**Phase:** Phase 1 - Core (Week 2)  
**Assigned:** 2026-01-04  
**Estimated Effort:** 5-7 days  
**Prerequisite:** BATCH-01 must be complete ✅

---

## 📋 Batch Overview

This batch implements the **interpreter execution engine** - the heart of the behavior tree system. You will:

1. Define the `IAIContext` interface and related types
2. Implement the `ITreeRunner` interface
3. Build the `Interpreter<TBB, TCtx>` class with full execution logic
4. Implement all core node types (Sequence, Selector, Action, Condition, Inverter)
5. Add delegate binding and action registry
6. Create comprehensive test coverage with test actions

**Critical Success Factors:**
- ✅ Resumable execution (skip already-processed nodes)
- ✅ Observer abort logic (guard re-evaluation)
- ✅ Delegate caching for performance
- ✅ Zero allocations during tree execution
- ✅ All tests passing with various tree structures

---

## 📚 Required Reading

**BEFORE starting, read these design documents:**

1. **[docs/design/02-Execution-Model.md](../../docs/design/02-Execution-Model.md)** - Interpreter architecture (CRITICAL!)
2. **[docs/design/03-Context-System.md](../../docs/design/03-Context-System.md)** - IAIContext design
3. **[docs/design/00-Architecture-Overview.md § 3](../../docs/design/00-Architecture-Overview.md)** - Execution principles

**Key Concepts to Understand:**
- Resumable state machine pattern
- "Skip already-processed" optimization
- Observer abort vs. normal selector
- Delegate binding for zero reflection overhead

---

## 🎯 Tasks

### Task 1: IAIContext Interface & Types

**Objective:** Define the context abstraction for external system integration.

**Files to Create:**
- `src/Fbt.Kernel/IAIContext.cs`
- `src/Fbt.Kernel/RaycastResult.cs`
- `src/Fbt.Kernel/PathResult.cs`

**IAIContext Specification:** See [03-Context-System.md § 2.1](../../docs/design/03-Context-System.md#21-iaicontext)

**Acceptance Criteria:**
- [x] `IAIContext` interface with properties: `DeltaTime`, `Time`, `FrameCount`
- [x] Methods for batched operations:
  - `int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance)`
  - `RaycastResult GetRaycastResult(int requestId)`
  - `int RequestPath(Vector3 from, Vector3 to)`
  - `PathResult GetPathResult(int requestId)`
- [x] Methods for parameters:
  - `float GetFloatParam(int index)`
  - `int GetIntParam(int index)`
- [x] Result structures: `RaycastResult`, `PathResult` (see design doc)
- [x] XML documentation on all members

**RaycastResult Structure:**
```csharp
public struct RaycastResult
{
    public bool IsReady;
    public bool Hit;
    public Vector3 HitPoint;
    public Vector3 HitNormal;
    public float Distance;
}
```

**PathResult Structure:**
```csharp
public struct PathResult
{
    public bool IsReady;
    public bool Success;
    public int PathId;
    public float PathLength;
}
```

**Note:** Keep IAIContext minimal for this batch. Extended methods (animation, damage) will come in BATCH-04.

**Tests:** Create `tests/Fbt.Tests/Unit/IAIContextTests.cs` to verify interface compiles and result structures are usable.

---

### Task 2: Node Logic Delegate Signature

**Objective:** Define the signature for all user action/condition methods.

**File:** `src/Fbt.Kernel/NodeLogicDelegate.cs`

**Specification:** See [02-Execution-Model.md § 4.1](../../docs/design/02-Execution-Model.md#41-standard-signature)

**Acceptance Criteria:**
- [x] Generic delegate: `NodeLogicDelegate<TBlackboard, TContext>`
- [x] Signature: `NodeStatus (ref TBlackboard, ref BehaviorTreeState, ref TContext, int paramIndex)`
- [x] Generic constraints: `where TBlackboard : struct` and `where TContext : struct, IAIContext`
- [x] XML documentation explaining each parameter

```csharp
public delegate NodeStatus NodeLogicDelegate<TBlackboard, TContext>(
    ref TBlackboard blackboard,
    ref BehaviorTreeState state,
    ref TContext context,
    int paramIndex)
    where TBlackboard : struct
    where TContext : struct, IAIContext;
```

---

### Task 3: ITreeRunner Interface

**Objective:** Define the common interface for all execution engines.

**File:** `src/Fbt.Kernel/Runtime/ITreeRunner.cs`

**Specification:** See [02-Execution-Model.md § 2.1](../../docs/design/02-Execution-Model.md#21-itreerunner)

**Acceptance Criteria:**
- [x] Generic interface: `ITreeRunner<TBlackboard, TContext>`
- [x] Method: `NodeStatus Tick(ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] XML documentation

**Directory Structure:**
- Create `src/Fbt.Kernel/Runtime/` directory for execution-related classes

---

### Task 4: Action Registry

**Objective:** Store and cache action delegate bindings.

**File:** `src/Fbt.Kernel/Runtime/ActionRegistry.cs`

**Acceptance Criteria:**
- [x] Generic class: `ActionRegistry<TBlackboard, TContext>`
- [x] Method: `void Register(string methodName, NodeLogicDelegate<TBlackboard, TContext> action)`
- [x] Method: `bool TryGetAction(string methodName, out NodeLogicDelegate<TBlackboard, TContext> action)`
- [x] Internal dictionary storage
- [x] XML documentation

**Example Usage:**
```csharp
var registry = new ActionRegistry<OrcBlackboard, MockContext>();
registry.Register("Attack", OrcActions.Attack);
registry.Register("Patrol", OrcActions.Patrol);
```

**Tests:** Verify registration and retrieval in `ActionRegistryTests.cs`.

---

### Task 5: Interpreter - Core Structure

**Objective:** Implement the interpreter class skeleton.

**File:** `src/Fbt.Kernel/Runtime/Interpreter.cs`

**Specification:** See [02-Execution-Model.md § 3.2](../../docs/design/02-Execution-Model.md#32-core-implementation)

**Acceptance Criteria:**
- [x] Generic class: `Interpreter<TBlackboard, TContext> : ITreeRunner<TBlackboard, TContext>`
- [x] Constructor: `Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)`
- [x] Private field: `BehaviorTreeBlob _blob`
- [x] Private field: `NodeLogicDelegate<TBlackboard, TContext>[] _actionDelegates`
- [x] Method: `NodeStatus Tick(ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] Private method: `NodeStatus ExecuteNode(int nodeIndex, ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] Private method: `NodeLogicDelegate<TBlackboard, TContext>[] BindActions(BehaviorTreeBlob, ActionRegistry)`

**Tick Implementation (Simplified for Now):**
```csharp
public NodeStatus Tick(
    ref TBlackboard blackboard,
    ref BehaviorTreeState state,
    ref TContext context)
{
    // === HOT RELOAD CHECK (Stub for now) ===
    // Will implement in BATCH-04
    
    // === EXECUTE TREE ===
    var result = ExecuteNode(0, ref blackboard, ref state, ref context);
    
    // === CLEANUP ===
    if (result != NodeStatus.Running)
    {
        state.RunningNodeIndex = 0;
    }
    
    return result;
}
```

**ExecuteNode Dispatcher:**
```csharp
private NodeStatus ExecuteNode(
    int nodeIndex,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    ref var node = ref _blob.Nodes[nodeIndex];
    
    return node.Type switch
    {
        NodeType.Sequence => ExecuteSequence(nodeIndex, ref node, ref bb, ref state, ref ctx),
        NodeType.Selector => ExecuteSelector(nodeIndex, ref node, ref bb, ref state, ref ctx),
        NodeType.Action => ExecuteAction(nodeIndex, ref node, ref bb, ref state, ref ctx),
        NodeType.Condition => ExecuteAction(nodeIndex, ref node, ref bb, ref state, ref ctx), // Same as Action
        NodeType.Inverter => ExecuteInverter(nodeIndex, ref node, ref bb, ref state, ref ctx),
        _ => NodeStatus.Failure // Unknown node type
    };
}
```

---

### Task 6: Sequence Implementation

**Objective:** Implement resumable sequence composite.

**Specification:** See [02-Execution-Model.md § 3.3](../../docs/design/02-Execution-Model.md#33-resumable-sequence)

**Acceptance Criteria:**
- [x] Method: `ExecuteSequence(int nodeIndex, ref NodeDefinition, ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] Skip already-succeeded children using `RunningNodeIndex` and `SubtreeOffset`
- [x] Return `Running` immediately if child returns `Running`
- [x] Return `Failure` immediately if child fails
- [x] Return `Success` only if all children succeed
- [x] **NO allocations** (stack-based only)

**Key Logic:**
```csharp
// If RunningNodeIndex > currentChildIndex + SubtreeOffset,
// this child already succeeded - skip it
if (state.RunningNodeIndex > 0 && 
    state.RunningNodeIndex > (currentChildIndex + childNode.SubtreeOffset))
{
    // Skip this child
    currentChildIndex += childNode.SubtreeOffset;
    continue;
}
```

**Tests Required:**
```csharp
[Fact]
public void Sequence_AllSucceed_ReturnsSuccess()

[Fact]
public void Sequence_FirstFails_ReturnsFailureImmediately()

[Fact]
public void Sequence_SecondReturnsRunning_ResumesCorrectly()
```

---

### Task 7: Selector Implementation

**Objective:** Implement resumable selector composite.

**Specification:** See [02-Execution-Model.md § 3.4](../../docs/design/02-Execution-Model.md#34-resumable-selector)

**Acceptance Criteria:**
- [x] Method: `ExecuteSelector(int nodeIndex, ref NodeDefinition, ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] Skip already-failed children using `RunningNodeIndex` and `SubtreeOffset`
- [x] Return `Running` immediately if child returns `Running`
- [x] Return `Success` immediately if child succeeds
- [x] Return `Failure` only if all children fail

**Key Difference from Sequence:**
- Selector skips failed children (not succeeded)
- Succeeds on first success (not all succeeding)

**Tests Required:**
```csharp
[Fact]
public void Selector_FirstSucceeds_SkipsRest()

[Fact]
public void Selector_AllFail_ReturnsFailure()

[Fact]
public void Selector_SecondReturnsRunning_ResumesCorrectly()
```

---

### Task 8: Action/Condition Execution

**Objective:** Execute leaf nodes via cached delegates.

**Specification:** See [02-Execution-Model.md § 3.6](../../docs/design/02-Execution-Model.md#36-actionleaf-execution)

**Acceptance Criteria:**
- [x] Method: `ExecuteAction(int nodeIndex, ref NodeDefinition, ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] Retrieve delegate from `_actionDelegates[node.PayloadIndex]`
- [x] Invoke delegate with all parameters
- [x] Update `state.RunningNodeIndex` if result is `Running`
- [x] Clear `state.RunningNodeIndex` if node finishes (Success/Failure) and was running

**Implementation:**
```csharp
private NodeStatus ExecuteAction(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    var actionDelegate = _actionDelegates[node.PayloadIndex];
    var status = actionDelegate(ref bb, ref state, ref ctx, node.PayloadIndex);
    
    if (status == NodeStatus.Running)
    {
        state.RunningNodeIndex = (ushort)nodeIndex;
    }
    else if (state.RunningNodeIndex == nodeIndex)
    {
        state.RunningNodeIndex = 0;
    }
    
    return status;
}
```

**Tests:** Use test actions from TestActions.cs (you'll create this).

---

### Task 9: Inverter Decorator

**Objective:** Implement inverter decorator (flips child result).

**Acceptance Criteria:**
- [x] Method: `ExecuteInverter(int nodeIndex, ref NodeDefinition, ref TBlackboard, ref BehaviorTreeState, ref TContext)`
- [x] Execute single child (nodeIndex + 1)
- [x] Flip result: Success ↔ Failure
- [x] Preserve `Running` status

**Implementation:**
```csharp
private NodeStatus ExecuteInverter(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    var childIndex = nodeIndex + 1;
    var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
    
    return result switch
    {
        NodeStatus.Success => NodeStatus.Failure,
        NodeStatus.Failure => NodeStatus.Success,
        _ => result // Running stays Running
    };
}
```

**Tests:**
```csharp
[Fact]
public void Inverter_FlipsSuccess()

[Fact]
public void Inverter_FlipsFailure()

[Fact]
public void Inverter_PreservesRunning()
```

---

### Task 10: Delegate Binding

**Objective:** Cache action delegates for performance.

**Specification:** See [02-Execution-Model.md § 3.7](../../docs/design/02-Execution-Model.md#37-delegate-binding)

**Acceptance Criteria:**
- [x] Method: `BindActions(BehaviorTreeBlob, ActionRegistry)` implemented
- [x] Returns array of delegates matching `blob.MethodNames` length
- [x] Looks up each method name in registry
- [x] If method not found, uses fallback delegate that returns `Failure`
- [x] Logs warning for missing methods (use `Console.WriteLine` for now)

**Tests:**
```csharp
[Fact]
public void BindActions_CachesAllDelegates()

[Fact]
public void BindActions_MissingMethod_UsesFallback()
```

---

### Task 11: Test Actions & Fixtures

**Objective:** Create reusable test actions for comprehensive testing.

**File:** `tests/Fbt.Tests/TestFixtures/TestActions.cs`

**Test Actions to Implement:**
```csharp
public static class TestActions
{
    public static NodeStatus AlwaysSuccess(
        ref TestBlackboard bb,
        ref BehaviorTreeState state,
        ref MockContext ctx,
        int paramIndex)
    {
        ctx.CallCount++;
        return NodeStatus.Success;
    }
    
    public static NodeStatus AlwaysFailure(
        ref TestBlackboard bb,
        ref BehaviorTreeState state,
        ref MockContext ctx,
        int paramIndex)
    {
        ctx.CallCount++;
        return NodeStatus.Failure;
    }
    
    public static NodeStatus IncrementCounter(
        ref TestBlackboard bb,
        ref BehaviorTreeState state,
        ref MockContext ctx,
        int paramIndex)
    {
        bb.Counter++;
        return NodeStatus.Success;
    }
    
    public static NodeStatus ReturnRunningOnce(
        ref TestBlackboard bb,
        ref BehaviorTreeState state,
        ref MockContext ctx,
        int paramIndex)
    {
        // First call: Running, second call: Success
        ref int counter = ref state.LocalRegisters[0];
        counter++;
        
        return counter >= 2 ? NodeStatus.Success : NodeStatus.Running;
    }
    
    public static NodeStatus CheckFlag(
        ref TestBlackboard bb,
        ref BehaviorTreeState state,
        ref MockContext ctx,
        int paramIndex)
    {
        return bb.Flag ? NodeStatus.Success : NodeStatus.Failure;
    }
}
```

**Update MockContext:**
```csharp
// In tests/Fbt.Tests/TestFixtures/MockContext.cs
public struct MockContext : IAIContext
{
    public float DeltaTime;
    public int CallCount;
    
    // IAIContext implementation (minimal stub)
    public float Time => 0;
    public int FrameCount => 0;
    
    public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance)
        => 0;
    
    public RaycastResult GetRaycastResult(int requestId)
        => new RaycastResult { IsReady = true };
    
    public int RequestPath(Vector3 from, Vector3 to)
        => 0;
    
    public PathResult GetPathResult(int requestId)
        => new PathResult { IsReady = true, Success = true };
    
    public float GetFloatParam(int index) => 1.0f;
    public int GetIntParam(int index) => 1;
}
```

---

### Task 12: Interpreter Tests

**Objective:** Comprehensive testing of interpreter execution.

**File:** `tests/Fbt.Tests/Unit/InterpreterTests.cs`

**Required Tests:**

```csharp
[Fact]
public void Interpreter_EmptyTree_ReturnsSuccess()

[Fact]
public void Sequence_AllSucceed_ReturnsSuccess()

[Fact]
public void Sequence_FirstFails_ReturnsFailureImmediately()

[Fact]
public void Sequence_DoesNotExecuteChildrenAfterFailure()

[Fact]
public void Selector_FirstSucceeds_SkipsRest()

[Fact]
public void Selector_AllFail_ReturnsFailure()

[Fact]
public void Inverter_FlipsSuccess()

[Fact]
public void Inverter_FlipsFailure()

[Fact]
public void Inverter_PreservesRunning()

[Fact]
public void RunningNode_ResumesOnNextTick()

[Fact]
public void RunningNode_SkipsAlreadyProcessedChildren()

[Fact]
public void ActionNode_UpdatesRunningNodeIndex()

[Fact]
public void ActionNode_ClearsRunningNodeIndexWhenDone()

[Fact]
public void DelegateBinding_CachesActions()

[Fact]
public void DelegateBinding_MissingAction_ReturnsFallback()
```

**Helper Methods in Test Class:**
```csharp
private BehaviorTreeBlob CreateSimpleSequence()
{
    return new BehaviorTreeBlob
    {
        TreeName = "TestSequence",
        Nodes = new[]
        {
            new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
            new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 },
            new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }
        },
        MethodNames = new[] { "AlwaysSuccess", "AlwaysSuccess" }
    };
}
```

**Minimum Test Count:** 15+ tests

---

## 📊 Deliverables

### Code Files (Must Create)

**Source (src/Fbt.Kernel/):**
- [ ] `IAIContext.cs`
- [ ] `RaycastResult.cs`
- [ ] `PathResult.cs`
- [ ] `NodeLogicDelegate.cs`
- [ ] `Runtime/ITreeRunner.cs`
- [ ] `Runtime/ActionRegistry.cs`
- [ ] `Runtime/Interpreter.cs`

**Tests (tests/Fbt.Tests/):**
- [ ] `Unit/IAIContextTests.cs`
- [ ] `Unit/ActionRegistryTests.cs`
- [ ] `Unit/InterpreterTests.cs`
- [ ] `TestFixtures/TestActions.cs` (updated)
- [ ] `TestFixtures/MockContext.cs` (updated to implement IAIContext)

### Test Coverage

**Minimum Required:**
- [x] IAIContext: Interface compiles, result structures usable
- [x] ActionRegistry: Registration and retrieval tested
- [x] Interpreter: ≥15 tests covering:
  - All composite types (Sequence, Selector)
  - Inverter decorator
  - Action execution
  - Resume logic
  - Delegate binding

**Total Test Count Expected:** ~20-25 new tests

---

## ✅ Definition of Done

**Batch is DONE when:**

1. **Code Quality**
   - [x] All source files created and compile
   - [x] Zero compiler warnings
   - [x] XML documentation on all public APIs
   - [x] Code follows .NET conventions

2. **Functionality**
   - [x] Interpreter executes simple trees correctly
   - [x] Sequence logic works (early exit on failure)
   - [x] Selector logic works (early exit on success)
   - [x] Inverter flips results correctly
   - [x] Resume logic skips already-processed nodes
   - [x] Action delegates cached and invoked

3. **Testing**
   - [x] All tests passing (100% pass rate)
   - [x] Minimum 20 new tests
   - [x] Edge cases tested (empty trees, single nodes)
   - [x] Resume scenarios tested

4. **Performance**
   - [x] No allocations during tree execution (verify in tests)
   - [x] Delegate invocation (not reflection)
   - [x] Stack-based execution (no heap allocations)

---

## 🚨 Critical Notes

### Zero Allocation Requirement

**You MUST verify zero allocations during execution:**
- Use structs, not classes for hot path
- No LINQ in execution code
- No string operations during tick
- No boxing/unboxing

**Test this by:**
- Running tree multiple times
- Using memory profiler (optional, but recommended)
- Code review for allocation patterns

### Resumable Execution is Critical

**The "skip already-processed" logic is performance-critical:**

```csharp
// In Sequence:
if (state.RunningNodeIndex > (currentChildIndex + childNode.SubtreeOffset))
{
    // This child already succeeded - skip it
    continue;
}

// In Selector:
if (state.RunningNodeIndex > (currentChildIndex + childNode.SubtreeOffset))
{
    // This child already failed - skip it
    continue;
}
```

**Without this, trees re-evaluate from root every frame = terrible performance!**

### Delegate Caching is Mandatory

**Do NOT use `MethodInfo.Invoke` in execution:**
- ❌ BAD: `methodInfo.Invoke(...)` - Slow, allocates
- ✅ GOOD: `_actionDelegates[i](...)` - Fast, zero alloc

---

## 📝 Reporting Requirements

When complete, create: `.dev-workstream/reports/BATCH-02-REPORT.md`

**Use template:** `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

**Must include:**
1. Executive summary
2. Task completion table
3. Files created/modified
4. Test results (pass/fail counts)
5. **Performance notes** (any allocations found?)
6. Any additional work done
7. Known issues or concerns

---

## ❓ Questions?

**For Questions:**
- Create: `.dev-workstream/reports/BATCH-02-QUESTIONS.md`
- Use template: `.dev-workstream/templates/QUESTIONS-TEMPLATE.md`

**For Blockers:**
- Create/Update: `.dev-workstream/reports/BLOCKERS-ACTIVE.md`
- Update immediately when blocked

---

## 🎯 Success Criteria Summary

**You succeed when:**
- ✅ 7 source files created
- ✅ 4+ test files created/updated
- ✅ All tests passing (≥20 new tests)
- ✅ Zero warnings
- ✅ Zero allocations during execution
- ✅ Interpreter correctly executes simple behavior trees
- ✅ Resume logic working (skip processed nodes)
- ✅ Batch report complete

**Estimated Time:** 5-7 days

---

**This is the core of the entire system. Take your time and get it right! 🚀**

*Batch Issued: 2026-01-04*  
*Development Leader: FastBTree Team Lead*  
*Prerequisite: BATCH-01 Complete ✅*

# BATCH-04: Example Trees & Extended Node Types

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Batch Number:** 04  
**Phase:** Phase 2 - Expansion (Week 4)  
**Assigned:** 2026-01-04  
**Estimated Effort:** 4-5 days  
**Prerequisites:** BATCH-01 ✅, BATCH-02 ✅, BATCH-03 ✅ (Phase 1 complete!)

---

## 📋 Batch Overview

**Phase 1 is complete! You now have a functional BT library.**

This batch extends the library with:
1. Additional node types (Wait, Repeater, Parallel)
2. Real-world example trees (JSON files)
3. Simple console demonstration program
4. Extended test coverage for new features

**Critical Success Factors:**
- ✅ Wait node with timer support
- ✅ Repeater decorator working
- ✅ Example trees execute correctly
- ✅ Console demo showcases library features
- ✅ All tests passing (including new node types)

---

## 📚 Required Reading

**BEFORE starting, review:**

1. **Phase 1 completion** - You have: data structures, interpreter, serialization
2. **[docs/design/01-Data-Structures.md § 3](../../docs/design/01-Data-Structures.md)** - AsyncToken usage
3. **[docs/design/02-Execution-Model.md § 3.5](../../docs/design/02-Execution-Model.md)** - Wait/Repeater nodes

**Key Concepts:**
- Wait nodes use AsyncToken for timer tracking
- Repeater nodes use LocalRegisters for counter
- Example trees demonstrate real-world patterns

---

## 🎯 Tasks

### Task 1: Wait Node Implementation

**Objective:** Implement Wait decorator using AsyncToken for timing.

**File:** Update `src/Fbt.Kernel/Runtime/Interpreter.cs`

**Specification:** See [02-Execution-Model.md § 3.5](../../docs/design/02-Execution-Model.md#35-wait-decorator)

**Acceptance Criteria:**
- [x] Add `NodeType.Wait` case to `ExecuteNode` dispatcher
- [x] Implement `ExecuteWait()` method
- [x] Use `AsyncToken` to pack/unpack timer state
- [x] Store duration in `FloatParams[node.PayloadIndex]`
- [x] Return `Running` until elapsed time exceeds duration
- [x] Return `Success` when timer completes

**Implementation:**
```csharp
private NodeStatus ExecuteWait(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    // Get duration from FloatParams
    float duration = _blob.FloatParams[node.PayloadIndex];
    
    // Check if we're resuming a wait
    if (state.RunningNodeIndex == nodeIndex)
    {
        // Unpack async token
        var token = new AsyncToken(state.AsyncData);
        float startTime = token.FloatA;
        
        // Check if duration has elapsed
        float elapsed = ctx.Time - startTime;
        if (elapsed >= duration)
        {
            state.RunningNodeIndex = 0;
            return NodeStatus.Success;
        }
        
        return NodeStatus.Running;
    }
    else
    {
        // First execution - pack start time
        var token = AsyncToken.FromFloat(ctx.Time, 0);
        state.AsyncData = token.PackedValue;
        state.RunningNodeIndex = (ushort)nodeIndex;
        return NodeStatus.Running;
    }
}
```

**Tests Required:**
```csharp
[Fact]
public void Wait_FirstTick_ReturnsRunning()

[Fact]
public void Wait_BeforeDuration_ReturnsRunning()

[Fact]
public void Wait_AfterDuration_ReturnsSuccess()

[Fact]
public void Wait_FromJSON_ExecutesCorrectly()
```

---

### Task 2: Repeater Decorator

**Objective:** Implement Repeater decorator with iteration count.

**File:** Update `src/Fbt.Kernel/Runtime/Interpreter.cs`

**Acceptance Criteria:**
- [x] Add `NodeType.Repeater` case to dispatcher
- [x] Implement `ExecuteRepeater()` method
- [x] Store repeat count in `IntParams[node.PayloadIndex]`
- [x] Use `LocalRegisters[0]` to track current iteration
- [x] Execute child until count reached
- [x] Return `Success` when all iterations complete
- [x] Reset counter when repeater completes

**Implementation:**
```csharp
private NodeStatus ExecuteRepeater(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    int repeatCount = _blob.IntParams[node.PayloadIndex];
    
    unsafe
    {
        ref int currentIteration = ref state.LocalRegisters[0];
        
        // If not running, start fresh
        if (state.RunningNodeIndex == 0)
        {
            currentIteration = 0;
        }
        
        while (currentIteration < repeatCount)
        {
            int childIndex = nodeIndex + 1;
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
            
            if (result == NodeStatus.Running)
            {
                return NodeStatus.Running;
            }
            
            if (result == NodeStatus.Failure)
            {
                currentIteration = 0; // Reset on failure
                return NodeStatus.Failure;
            }
            
            // Child succeeded, increment counter
            currentIteration++;
            
            // If more iterations remain, continue
            if (currentIteration < repeatCount)
            {
                // Reset child for next iteration
                // (This may need adjustment based on child type)
                continue;
            }
        }
        
        // All iterations complete
        currentIteration = 0;
        state.RunningNodeIndex = 0;
        return NodeStatus.Success;
    }
}
```

**Tests:**
```csharp
[Fact]
public void Repeater_CountZero_ReturnsSuccess()

[Fact]
public void Repeater_CountThree_ExecutesThreeTimes()

[Fact]
public void Repeater_ChildFails_Aborts()

[Fact]
public void Repeater_FromJSON_WorksCorrectly()
```

---

### Task 3: Example Tree - Simple Patrol

**Objective:** Create a real-world patrol tree as JSON.

**File:** `examples/trees/simple-patrol.json`

**Content:**
```json
{
  "TreeName": "SimplePatrol",
  "Version": 1,
  "Root": {
    "Type": "Sequence",
    "Children": [
      {
        "Type": "Action",
        "Action": "FindRandomPatrolPoint"
      },
      {
        "Type": "Action",
        "Action": "MoveToTarget"
      },
      {
        "Type": "Wait",
        "WaitTime": 2.0
      }
    ]
  }
}
```

**Test:** Verify this compiles and loads correctly.

---

### Task 4: Example Tree - Guard Behavior

**Objective:** Create a selector-based guard tree.

**File:** `examples/trees/guard-behavior.json`

**Content:**
```json
{
  "TreeName": "GuardBehavior",
  "Version": 1,
  "Root": {
    "Type": "Selector",
    "Children": [
      {
        "Type": "Sequence",
        "Children": [
          {
            "Type": "Condition",
            "Action": "IsEnemyVisible"
          },
          {
            "Type": "Action",
            "Action": "ChaseEnemy"
          },
          {
            "Type": "Action",
            "Action": "Attack"
          }
        ]
      },
      {
        "Type": "Sequence",
        "Children": [
          {
            "Type": "Action",
            "Action": "FindRandomPatrolPoint"
          },
          {
            "Type": "Action",
            "Action": "MoveToTarget"
          },
          {
            "Type": "Wait",
            "WaitTime": 3.0
          }
        ]
      }
    ]
  }
}
```

**Purpose:** Demonstrates "If enemy visible, chase and attack; otherwise patrol"

---

### Task 5: Console Demo Application

**Objective:** Create simple console app demonstrating FastBTree.

**File:** `examples/Fbt.Examples.Console/Program.cs`

**Project:** Create new console project

```bash
dotnet new console -n Fbt.Examples.Console -o examples/Fbt.Examples.Console
dotnet add examples/Fbt.Examples.Console reference src/Fbt.Kernel
```

**Program.cs Content:**
```csharp
using System;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;

namespace Fbt.Examples.Console
{
    // Simple blackboard for demo
    public struct DemoBlackboard
    {
        public int PatrolPointX;
        public int PatrolPointY;
        public int EnemyDistance;
        public bool EnemyVisible;
    }
    
    // Simple context for demo
    public struct DemoContext : IAIContext
    {
        public float DeltaTime { get; set; }
        public float Time { get; set; }
        public int FrameCount { get; set; }
        
        // Minimal implementation (stubbed)
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new RaycastResult { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new PathResult { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 1.0f;
        public int GetIntParam(int index) => 1;
    }
    
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("=== FastBTree Console Demo ===\n");
            
            // Load and compile tree
            string json = File.ReadAllText("../../../trees/simple-patrol.json");
            System.Console.WriteLine($"Loading tree from JSON...");
            
            var blob = TreeCompiler.CompileFromJson(json);
            System.Console.WriteLine($"Tree compiled: {blob.TreeName}");
            System.Console.WriteLine($"  Nodes: {blob.Nodes.Length}");
            System.Console.WriteLine($"  Methods: {blob.MethodNames.Length}");
            System.Console.WriteLine();
            
            // Register actions
            var registry = new ActionRegistry<DemoBlackboard, DemoContext>();
            registry.Register("FindRandomPatrolPoint", FindRandomPatrolPoint);
            registry.Register("MoveToTarget", MoveToTarget);
            
            // Create interpreter
            var interpreter = new Interpreter<DemoBlackboard, DemoContext>(blob, registry);
            
            // Simulate ticks
            var bb = new DemoBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new DemoContext();
            
            System.Console.WriteLine("Executing tree...\n");
            
            for (int frame = 0; frame < 5; frame++)
            {
                ctx.Time = frame * 0.1f;
                ctx.DeltaTime = 0.1f;
                ctx.FrameCount = frame;
                
                System.Console.WriteLine($"Frame {frame}:");
                var result = interpreter.Tick(ref bb, ref state, ref ctx);
                System.Console.WriteLine($"  Result: {result}");
                System.Console.WriteLine($"  Blackboard: Point=({bb.PatrolPointX}, {bb.PatrolPointY})");
                System.Console.WriteLine();
                
                if (result != NodeStatus.Running)
                    break;
            }
            
            System.Console.WriteLine("Demo complete!");
        }
        
        static NodeStatus FindRandomPatrolPoint(
            ref DemoBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int paramIndex)
        {
            bb.PatrolPointX = new Random().Next(-100, 100);
            bb.PatrolPointY = new Random().Next(-100, 100);
            System.Console.WriteLine($"  [Action] Found patrol point: ({bb.PatrolPointX}, {bb.PatrolPointY})");
            return NodeStatus.Success;
        }
        
        static NodeStatus MoveToTarget(
            ref DemoBlackboard bb,
            ref BehaviorTreeState state,
            ref DemoContext ctx,
            int paramIndex)
        {
            System.Console.WriteLine($"  [Action] Moving to target: ({bb.PatrolPointX}, {bb.PatrolPointY})");
            return NodeStatus.Success;
        }
    }
}
```

**Acceptance Criteria:**
- [x] Project compiles
- [x] Loads JSON tree
- [x] Executes tree and prints output
- [x] Demonstrates blackboard usage
- [x] Shows multi-frame execution with Wait node

---

### Task 6: Extended Interpreter Tests

**Objective:** Add tests for new node types.

**File:** Update `tests/Fbt.Tests/Unit/InterpreterTests.cs`

**New Tests:**
```csharp
[Fact]
public void Wait_ExecutesCorrectly()
{
    string json = @"{ 
        ""TreeName"": ""WaitTest"",
        ""Root"": { ""Type"": ""Wait"", ""WaitTime"": 1.0 }
    }";
    
    var blob = TreeCompiler.CompileFromJson(json);
    var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, new ActionRegistry<TestBlackboard, MockContext>());
    
    var bb = new TestBlackboard();
    var state = new BehaviorTreeState();
    var ctx = new MockContext { Time = 0.0f };
    
    // First tick
    var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
    Assert.Equal(NodeStatus.Running, result1);
    
    // Tick before duration
    ctx.Time = 0.5f;
    var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
    Assert.Equal(NodeStatus.Running, result2);
    
    // Tick after duration
    ctx.Time = 1.1f;
    var result3 = interpreter.Tick(ref bb, ref state, ref ctx);
    Assert.Equal(NodeStatus.Success, result3);
}

[Fact]
public void Repeater_ExecutesCorrectly()
{
    string json = @"{ 
        ""TreeName"": ""RepeatTest"",
        ""Root"": { 
            ""Type"": ""Repeater"",
            ""RepeatCount"": 3,
            ""Children"": [ { ""Type"": ""Action"", ""Action"": ""IncrementCounter"" } ]
        }
    }";
    
    var blob = TreeCompiler.CompileFromJson(json);
    var registry = new ActionRegistry<TestBlackboard, MockContext>();
    registry.Register("IncrementCounter", TestActions.IncrementCounter);
    
    var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
    var bb = new TestBlackboard();
    var state = new BehaviorTreeState();
    var ctx = new MockContext();
    
    var result = interpreter.Tick(ref bb, ref state, ref ctx);
    
    Assert.Equal(NodeStatus.Success, result);
    Assert.Equal(3, bb.Counter); // Executed 3 times
}
```

---

## 📊 Deliverables

### Code Files

**Source (Updates):**
- [ ] `src/Fbt.Kernel/Runtime/Interpreter.cs` (add ExecuteWait, ExecuteRepeater)
- [ ] `src/Fbt.Kernel/NodeType.cs` (Wait and Repeater already defined from BATCH-01)

**Examples:**
- [ ] `examples/trees/simple-patrol.json`
- [ ] `examples/trees/guard-behavior.json`
- [ ] `examples/Fbt.Examples.Console/Program.cs`
- [ ] `examples/Fbt.Examples.Console/Fbt.Examples.Console.csproj`

**Tests:**
- [ ] Update `tests/Fbt.Tests/Unit/InterpreterTests.cs` (4+ new tests)
- [ ] Update `tests/Fbt.Tests/TestFixtures/MockContext.cs` (add Time property)

### Test Coverage

**Minimum Required:**
- [x] Wait node: 4+ tests
- [x] Repeater node: 3+ tests
- [x] Example trees: Compile and validate
- [x] Console demo: Runs successfully

**Total New Tests Expected:** ~8-10

---

## ✅ Definition of Done

**Batch is DONE when:**

1. **Code Quality**
   - [x] Wait and Repeater implemented in Interpreter
   - [x] Zero compiler warnings
   - [x] XML documentation on new methods

2. **Functionality**
   - [x] Wait node uses AsyncToken correctly
   - [x] Repeater executes correct iteration count
   - [x] Example JSON trees compile
   - [x] Console demo runs and produces output

3. **Testing**
   - [x] All tests passing (70+ total)
   - [x] Wait and Repeater tested
   - [x] Integration tests with example trees

4. **Documentation**
   - [x] Console demo has README
   - [x] Example trees documented

---

## 🚨 Critical Notes

### AsyncToken Usage for Wait

**The AsyncToken is critical for timer tracking:**

```csharp
// Pack start time
var token = AsyncToken.FromFloat(ctx.Time, 0);
state.AsyncData = token.PackedValue;

// Later, unpack and check
var token = new AsyncToken(state.AsyncData);
float startTime = token.FloatA;
float elapsed = ctx.Time - startTime;
```

**Test timer logic extensively!**

### Repeater Counter Management

**Use LocalRegisters for iteration tracking:**

```csharp
unsafe
{
    ref int currentIteration = ref state.LocalRegisters[0];
    // Increment, reset, etc.
}
```

**Ensure proper reset on completion and failure!**

---

## 📝 Reporting Requirements

When complete, create: `.dev-workstream/reports/BATCH-04-REPORT.md`

**Must include:**
1. Executive summary
2. Task completion table
3. Files created/modified
4. Test results
5. Console demo output screenshot/log
6. Known issues

---

## 🎯 Success Criteria Summary

**You succeed when:**
- ✅ Wait and Repeater nodes working
- ✅ 2 example JSON trees created
- ✅ Console demo runs successfully
- ✅ All tests passing (70+ tests)
- ✅ Zero warnings
- ✅ Example output demonstrates library features

**Estimated Time:** 4-5 days

---

**This batch makes FastBTree usable with real examples! 🚀**

*Batch Issued: 2026-01-04*  
*Development Leader: FastBTree Team Lead*  
*Prerequisites: Phase 1 Complete ✅*

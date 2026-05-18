# BATCH-05: Advanced Features & Documentation

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Batch Number:** 05  
**Phase:** Phase 2 - Expansion (Week 5)  
**Assigned:** 2026-01-04  
**Estimated Effort:** 5-6 days  
**Prerequisites:** BATCH-01-04 ✅ (Core + Examples complete!)

---

## 📋 Batch Overview

**FastBTree is now usable! This batch makes it production-ready.**

This batch adds:
1. **Parallel node** - Execute multiple children simultaneously
2. **Advanced decorators** - Cooldown, ForceSuccess/Failure
3. **Tree visualization** - Text-based tree printer for debugging
4. **Comprehensive documentation** - README, quick start guide
5. **Performance utilities** - Basic profiling support

**Critical Success Factors:**
- ✅ Parallel node working with all policies
- ✅ Cooldown decorator with timer management
- ✅ Tree visualizer for debugging
- ✅ Professional README with examples
- ✅ All tests passing (80+ tests expected)

---

## 📚 Required Reading

**BEFORE starting, review:**

1. **[docs/design/02-Execution-Model.md § 3.3](../../docs/design/02-Execution-Model.md)** - Parallel node
2. **Current codebase** - Understand Wait/Repeater implementation patterns
3. **Example trees** - See what users will build

**Key Concepts:**
- Parallel uses policy (RequireAll, RequireOne)
- Cooldown tracks last execution time
- Visualizer helps debug tree structure

---

## 🎯 Tasks

### Task 1: Parallel Node Implementation

**Objective:** Implement Parallel composite with success/failure policies.

**File:** Update `src/Fbt.Kernel/Runtime/Interpreter.cs`

**Specification:** See [02-Execution-Model.md § 3.3](../../docs/design/02-Execution-Model.md#33-parallel)

**Acceptance Criteria:**
- [x] Add `NodeType.Parallel` case to dispatcher
- [x] Implement `ExecuteParallel()` method
- [x] Support `RequireAll` policy (all must succeed)
- [x] Support `RequireOne` policy (any one succeeds)
- [x] Track child results in LocalRegisters (bitfield)
- [x] Return Running while children executing
- [x] Return Success/Failure based on policy

**Policy Storage:**
```csharp
// Store policy in IntParams:
// 0 = RequireAll
// 1 = RequireOne
```

**Implementation Sketch:**
```csharp
private NodeStatus ExecuteParallel(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    int policy = _blob.IntParams[node.PayloadIndex];
    int childCount = node.ChildCount;
    
    unsafe
    {
        // Use LocalRegisters[0] as bitfield for child results
        // Bit 0-15: Success flags
        // Bit 16-31: Finished flags
        ref int childStatesBits = ref state.LocalRegisters[0];
        
        if (state.RunningNodeIndex == 0)
        {
            childStatesBits = 0; // Reset on fresh start
        }
        
        int successCount = 0;
        int failureCount = 0;
        int runningCount = 0;
        
        // Execute all children
        int childIndex = nodeIndex + 1;
        for (int i = 0; i < childCount; i++)
        {
            int finishedBit = 1 << (i + 16);
            
            // Skip if already finished
            if ((childStatesBits & finishedBit) != 0)
            {
                // Check if it was a success
                int successBit = 1 << i;
                if ((childStatesBits & successBit) != 0)
                    successCount++;
                else
                    failureCount++;
                    
                // Move to next child's index
                childIndex += _blob.Nodes[childIndex].SubtreeOffset;
                continue;
            }
            
            // Execute child
            var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
            
            if (result == NodeStatus.Success)
            {
                childStatesBits |= (1 << i); // Mark success
                childStatesBits |= finishedBit; // Mark finished
                successCount++;
            }
            else if (result == NodeStatus.Failure)
            {
                childStatesBits |= finishedBit; // Mark finished (no success bit)
                failureCount++;
            }
            else // Running
            {
                runningCount++;
            }
            
            // Move to next child
            childIndex += _blob.Nodes[childIndex].SubtreeOffset;
        }
        
        // Check policy
        if (policy == 0) // RequireAll
        {
            if (failureCount > 0)
            {
                childStatesBits = 0;
                state.RunningNodeIndex = 0;
                return NodeStatus.Failure;
            }
            if (successCount == childCount)
            {
                childStatesBits = 0;
                state.RunningNodeIndex = 0;
                return NodeStatus.Success;
            }
        }
        else // RequireOne
        {
            if (successCount > 0)
            {
                childStatesBits = 0;
                state.RunningNodeIndex = 0;
                return NodeStatus.Success;
            }
            if (failureCount == childCount)
            {
                childStatesBits = 0;
                state.RunningNodeIndex = 0;
                return NodeStatus.Failure;
            }
        }
        
        // Still have running children
        state.RunningNodeIndex = (ushort)nodeIndex;
        return NodeStatus.Running;
    }
}
```

**JSON Example:**
```json
{
  "Type": "Parallel",
  "Policy": "RequireAll",
  "Children": [
    { "Type": "Action", "Action": "Patrol" },
    { "Type": "Action", "Action": "ScanForEnemies" }
  ]
}
```

**Tests:**
```csharp
[Fact]
public void Parallel_RequireAll_AllSucceed_ReturnsSuccess()

[Fact]
public void Parallel_RequireAll_OneFails_ReturnsFailure()

[Fact]
public void Parallel_RequireOne_OneSucceeds_ReturnsSuccess()

[Fact]
public void Parallel_WithRunning_ReturnsRunning()
```

---

### Task 2: Cooldown Decorator

**Objective:** Implement Cooldown decorator to limit action frequency.

**File:** Update `src/Fbt.Kernel/Runtime/Interpreter.cs`

**Acceptance Criteria:**
- [x] Add `NodeType.Cooldown` case
- [x] Implement `ExecuteCooldown()` method
- [x] Store cooldown duration in FloatParams
- [x] Track last execution time in AsyncData
- [x] Skip child execution if cooldown active
- [x] Return Failure during cooldown
- [x] Execute child when ready

**Implementation:**
```csharp
private NodeStatus ExecuteCooldown(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    float cooldownDuration = _blob.FloatParams[node.PayloadIndex];
    
    // Check last execution time
    var token = new AsyncToken(state.AsyncData);
    float lastExecTime = token.FloatA;
    
    float timeSinceLastExec = ctx.Time - lastExecTime;
    
    if (timeSinceLastExec < cooldownDuration && lastExecTime > 0)
    {
        // Still on cooldown
        return NodeStatus.Failure;
    }
    
    // Execute child
    int childIndex = nodeIndex + 1;
    var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
    
    // Update last execution time on success
    if (result == NodeStatus.Success)
    {
        var newToken = AsyncToken.FromFloat(ctx.Time, 0);
        state.AsyncData = newToken.PackedValue;
    }
    
    return result;
}
```

**JSON:**
```json
{
  "Type": "Cooldown",
  "CooldownTime": 5.0,
  "Children": [ { "Type": "Action", "Action": "SpecialAttack" } ]
}
```

---

### Task 3: Force Success/Failure Decorators

**Objective:** Simple decorators that override child result.

**Files:** Update `src/Fbt.Kernel/Runtime/Interpreter.cs`

**Implementation:**
```csharp
private NodeStatus ExecuteForceSuccess(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    int childIndex = nodeIndex + 1;
    var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
    
    if (result == NodeStatus.Running)
        return NodeStatus.Running;
        
    return NodeStatus.Success; // Force success
}

private NodeStatus ExecuteForceFailure(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    int childIndex = nodeIndex + 1;
    var result = ExecuteNode(childIndex, ref bb, ref state, ref ctx);
    
    if (result == NodeStatus.Running)
        return NodeStatus.Running;
        
    return NodeStatus.Failure; // Force failure
}
```

**Use Case:** Inverting without Inverter, or ensuring a branch always succeeds/fails.

---

### Task 4: Tree Visualizer

**Objective:** Create text-based tree visualization for debugging.

**File:** `src/Fbt.Kernel/Utilities/TreeVisualizer.cs`

**Acceptance Criteria:**
- [x] Static method `string Visualize(BehaviorTreeBlob blob)`
- [x] Prints tree structure with indentation
- [x] Shows node types, indices, and subtree offsets
- [x] Includes method names for actions

**Implementation:**
```csharp
namespace Fbt.Utilities
{
    /// <summary>
    /// Generates text-based visualization of behavior tree structure.
    /// </summary>
    public static class TreeVisualizer
    {
        public static string Visualize(BehaviorTreeBlob blob)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tree: {blob.TreeName}");
            sb.AppendLine($"Nodes: {blob.Nodes.Length}, Methods: {blob.MethodNames?.Length ?? 0}");
            sb.AppendLine();
            
            VisualizeNode(blob, 0, 0, sb);
            return sb.ToString();
        }
        
        private static void VisualizeNode(BehaviorTreeBlob blob, int index, int depth, StringBuilder sb)
        {
            if (index >= blob.Nodes.Length)
                return;
                
            var node = blob.Nodes[index];
            string indent = new string(' ', depth * 2);
            
            // Node info
            sb.Append($"{indent}[{index}] {node.Type}");
            
            // Add method name for actions
            if (node.Type == NodeType.Action || node.Type == NodeType.Condition)
            {
                if (node.PayloadIndex >= 0 && node.PayloadIndex < (blob.MethodNames?.Length ?? 0))
                {
                    sb.Append($" \"{blob.MethodNames[node.PayloadIndex]}\"");
                }
            }
            
            // Add params for Wait/Repeater
            if (node.Type == NodeType.Wait && node.PayloadIndex >= 0)
            {
                if (node.PayloadIndex < (blob.FloatParams?.Length ?? 0))
                    sb.Append($" ({blob.FloatParams[node.PayloadIndex]}s)");
            }
            
            sb.AppendLine($" | Children: {node.ChildCount}, Offset: {node.SubtreeOffset}");
            
            // Recursively visualize children
            int childIndex = index + 1;
            for (int i = 0; i < node.ChildCount; i++)
            {
                VisualizeNode(blob, childIndex, depth + 1, sb);
                childIndex += blob.Nodes[childIndex].SubtreeOffset;
            }
        }
    }
}
```

**Example Output:**
```
Tree: GuardBehavior
Nodes: 9, Methods: 5

[0] Selector | Children: 2, Offset: 9
  [1] Sequence | Children: 3, Offset: 4
    [2] Condition "IsEnemyVisible" | Children: 0, Offset: 1
    [3] Action "ChaseEnemy" | Children: 0, Offset: 1
    [4] Action "Attack" | Children: 0, Offset: 1
  [5] Sequence | Children: 3, Offset: 4
    [6] Action "FindRandomPatrolPoint" | Children: 0, Offset: 1
    [7] Action "MoveToTarget" | Children: 0, Offset: 1
    [8] Wait (3.0s) | Children: 0, Offset: 1
```

---

### Task 5: README and Documentation

**Objective:** Create professional README with quick start guide.

**File:** `README.md` (root)

**Content:**
```markdown
# FastBTree

**High-performance, cache-friendly behavior tree library for .NET**

FastBTree is a production-ready behavior tree implementation designed for real-time systems, game AI, and robotics. It emphasizes:
- **Zero-allocation execution** - No GC pressure during runtime
- **Cache-friendly data structures** - 8-byte nodes, 64-byte state
- **Resumable execution** - Trees can pause and resume efficiently
- **Hot reload support** - Update trees without restarting

## Features

✅ **Core Node Types**
- Composites: Sequence, Selector, Parallel
- Decorators: Inverter, Repeater, Wait, Cooldown, Force Success/Failure
- Leaves: Action, Condition

✅ **Serialization**
- JSON authoring format (human-readable)
- Binary compilation (fast loading)
- Hash-based change detection
- Tree validation

✅ **Performance**
- 8-byte nodes (cache-aligned)
- 64-byte execution state (single cache line)
- Zero allocations in hot path
- Delegate caching (no reflection)

✅ **Debugging**
- Tree visualization
- Test fixtures included
- Console demo application

## Quick Start

### Installation

```bash
git clone https://github.com/yourusername/FastBTree
cd FastBTree
dotnet build
```

### Basic Usage

1. **Define a tree in JSON:**

```json
{
  "TreeName": "GuardAI",
  "Root": {
    "Type": "Selector",
    "Children": [
      {
        "Type": "Sequence",
        "Children": [
          { "Type": "Condition", "Action": "IsEnemyVisible" },
          { "Type": "Action", "Action": "Attack" }
        ]
      },
      { "Type": "Action", "Action": "Patrol" }
    ]
  }
}
```

2. **Compile and execute:**

```csharp
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;

// Load and compile
var json = File.ReadAllText("guard-ai.json");
var blob = TreeCompiler.CompileFromJson(json);

// Register actions
var registry = new ActionRegistry<Blackboard, Context>();
registry.Register("IsEnemyVisible", bb => ...);
registry.Register("Attack", bb => ...);
registry.Register("Patrol", bb => ...);

// Create interpreter
var interpreter = new Interpreter<Blackboard, Context>(blob, registry);

// Execute each frame
var blackboard = new Blackboard();
var state = new BehaviorTreeState();
var context = new Context();

var result = interpreter.Tick(ref blackboard, ref state, ref context);
```

## Examples

See `examples/` directory:
- `simple-patrol.json` - Basic patrol loop
- `guard-behavior.json` - Combat/patrol AI
- `Fbt.Examples.Console` - Working demo application

Run the demo:
```bash
dotnet run --project examples/Fbt.Examples.Console
```

## Architecture

FastBTree uses a **bytecode interpreter** model:

1. **Authoring**: Write trees in JSON (human-readable)
2. **Compilation**: Flatten to depth-first node array
3. **Execution**: Interpret nodes with zero allocations

**Key Data Structures:**
- `NodeDefinition` (8 bytes): Type, ChildCount, SubtreeOffset, PayloadIndex
- `BehaviorTreeState` (64 bytes): Execution state (single cache line!)
- `BehaviorTreeBlob`: Compiled tree with lookup tables

## Performance

Typical performance on modern hardware:
- **Compilation**: ~1000 trees/sec
- **Execution**: ~100,000 ticks/sec per tree
- **Memory**: ~8 bytes per node + lookup tables

Zero allocations during tick execution!

## Testing

```bash
dotnet test
```

Current test coverage: 62+ tests, 100% pass rate

## Documentation

See `docs/design/` for detailed specifications:
- `00-Architecture-Overview.md` - System architecture
- `01-Data-Structures.md` - Memory layouts
- `02-Execution-Model.md` - Interpreter design
- `03-Context-System.md` - External integration
- `04-Serialization.md` - Asset pipeline

## License

MIT License - see LICENSE file

## Contributing

Contributions welcome! Please open an issue first to discuss changes.

## Status

**Production Ready** - v1.0

Phase 1 (Core): ✅ Complete  
Phase 2 (Examples & Advanced Features): ✅ Complete  

---

Built with ❤️ for high-performance AI systems
```

---

### Task 6: Quick Start Guide

**File:** `docs/QUICK_START.md`

**Content:** Expanded tutorial with:
- Tree authoring patterns
- Action registration examples
- Blackboard design tips
- Common patterns (patrol, combat, utility AI)

**Acceptance Criteria:**
- [x] 5+ code examples
- [x] Covers all node types
- [x] Shows best practices
- [x] Links to design docs

---

## 📊 Deliverables

### Code Files

**Source:**
- [ ] Update `Interpreter.cs` (+150 lines: Parallel, Cooldown, Force decorators)
- [ ] Create `Utilities/TreeVisualizer.cs` (~100 lines)
- [ ] Update `NodeType.cs` (add Parallel, Cooldown, ForceSuccess, ForceFailure)

**Documentation:**
- [ ] `README.md` (root)
- [ ] `docs/QUICK_START.md`
- [ ] Update `examples/trees/` with Parallel/Cooldown examples

**Tests:**
- [ ] Update `InterpreterTests.cs` (+80 lines, 6+ new tests)
- [ ] Create `TreeVisualizerTests.cs` (3+ tests)

### Test Coverage

**Minimum Required:**
- [x] Parallel: 4+ tests (RequireAll/RequireOne policies)
- [x] Cooldown: 3+ tests
- [x] Force decorators: 2+ tests
- [x] Visualizer: 2+ tests

**Total Tests Expected:** 80-85 tests

---

## ✅ Definition of Done

**Batch is DONE when:**

1. **Code Quality**
   - [x] All new nodes implemented
   - [x] Tree visualizer working
   - [x] Zero compiler warnings
   - [x] XML documentation complete

2. **Functionality**
   - [x] Parallel node with policies working
   - [x] Cooldown timer logic correct
   - [x] Visualizer produces readable output
   - [x] All example trees visualize correctly

3. **Testing**
   - [x] All tests passing (80+ total)
   - [x] New node types tested
   - [x] Visualizer tested

4. **Documentation**
   - [x] README professional and complete
   - [x] Quick start guide with examples
   - [x] Example trees updated

---

## 🚨 Critical Notes

### Parallel Node Complexity

**Bitfield tracking is critical:**

```csharp
// LocalRegisters[0] layout:
// Bits 0-15: Success flags (1 = child succeeded)
// Bits 16-31: Finished flags (1 = child done)

int successBit = 1 << i;
int finishedBit = 1 << (i + 16);
```

**Test edge cases!**
- All children succeed
- One child fails immediately
- Children finish in different order
- Running children resume correctly

### Tree Visualizer

**Must handle:**
- Deep nesting (10+ levels)
- Large trees (100+ nodes)
- All node types
- Missing method names (error recovery)

---

## 📝 Reporting Requirements

When complete, create: `.dev-workstream/reports/BATCH-05-REPORT.md`

**Must include:**
1. Executive summary
2. Task completion table
3. Files created/modified
4. Test results
5. **Tree visualizer output examples**
6. **README screenshot/preview**
7. Known issues

---

## 🎯 Success Criteria Summary

**You succeed when:**
- ✅ Parallel node working with both policies
- ✅ Cooldown decorator functional
- ✅ Tree visualizer produces clear output
- ✅ Professional README complete
- ✅ All tests passing (80+ tests)
- ✅ Zero warnings
- ✅ **Library is production-ready!**

**Estimated Time:** 5-6 days

---

**This batch makes FastBTree production-ready and well-documented! 🚀**

*Batch Issued: 2026-01-04*  
*Development Leader: FastBTree Team Lead*  
*Prerequisites: BATCH-01-04 Complete ✅*

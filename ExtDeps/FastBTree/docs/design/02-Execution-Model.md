# FastBTree Execution Model

**Version:** 1.0.0  
**Date:** 2026-01-04

---

## 1. Overview

This document defines the execution model for behavior trees, covering:

- **Interpreter architecture** (Phase 1: v1.0)
- **JIT compiler design** (Phase 2: future optimization)
- **Node execution semantics**
- **Resumable state machine** pattern
- **Observer abort** implementation

---

## 2. Execution Interface

### 2.1 ITreeRunner

Common interface for all execution engines.

```csharp
namespace Fbt
{
    /// <summary>
    /// Interface for behavior tree execution engines.
    /// </summary>
    public interface ITreeRunner<TBlackboard, TContext>
        where TBlackboard : struct
        where TContext : struct, IAIContext
    {
        /// <summary>
        /// Execute one tick of the behavior tree.
        /// </summary>
        /// <param name="blackboard">Entity's blackboard data.</param>
        /// <param name="state">Entity's runtime state.</param>
        /// <param name="context">Execution context (time, queries, etc.).</param>
        /// <returns>Final status (Success/Failure/Running).</returns>
        NodeStatus Tick(
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context);
    }
}
```

---

## 3. Interpreter (Phase 1)

### 3.1 Architecture

The interpreter uses **resumable recursion** to traverse the tree:

```
┌─────────────────────────────────────────┐
│         Tick() Entry Point              │
│  1. Hot Reload Check                    │
│  2. Call ExecuteNode(rootIndex=0)       │
│  3. Cleanup (clear state if done)       │
└─────────────────────────────────────────┘
              ↓
┌─────────────────────────────────────────┐
│    ExecuteNode(int nodeIndex)           │
│  • Switch on node.Type                  │
│  • Recursive for composites             │
│  • Direct call for leaves               │
└─────────────────────────────────────────┘
              ↓
    ┌─────────┴─────────┐
    ↓                   ↓
┌─────────┐      ┌──────────┐
│Composite│      │   Leaf   │
│  Logic  │      │  Logic   │
└─────────┘      └──────────┘
```

### 3.2 Core Implementation

```csharp
namespace Fbt.Runtime
{
    /// <summary>
    /// Interpreter-based behavior tree runner.
    /// Advantages: Debuggable, no IL generation complexity.
    /// Disadvantage: Slower than JIT (but still very fast with cached delegates).
    /// </summary>
    public class Interpreter<TBlackboard, TContext> : ITreeRunner<TBlackboard, TContext>
        where TBlackboard : struct
        where TContext : struct, IAIContext
    {
        private readonly BehaviorTreeBlob _blob;
        private readonly NodeLogicDelegate<TBlackboard, TContext>[] _actionDelegates;
        
        public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry)
        {
            _blob = blob;
            _actionDelegates = BindActions(blob, registry);
        }
        
        public NodeStatus Tick(
            ref TBlackboard blackboard,
            ref BehaviorTreeState state,
            ref TContext context)
        {
            // === HOT RELOAD CHECK ===
            if (state.RunningBlobHash != _blob.StructureHash)
            {
                // Structure changed - must reset
                state.Reset();
                state.RunningBlobHash = _blob.StructureHash;
            }
            
            // === EXECUTE TREE ===
            var result = ExecuteNode(0, ref blackboard, ref state, ref context);
            
            // === CLEANUP ===
            if (result != NodeStatus.Running)
            {
                // Tree completed - clear running state
                state.RunningNodeIndex = 0;
            }
            
            return result;
        }
        
        private NodeStatus ExecuteNode(
            int nodeIndex,
            ref TBlackboard bb,
            ref BehaviorTreeState state,
            ref TContext ctx)
        {
            ref var node = ref _blob.Nodes[nodeIndex];
            
            switch (node.Type)
            {
                case NodeType.Sequence:
                    return ExecuteSequence(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Selector:
                    return ExecuteSelector(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Parallel:
                    return ExecuteParallel(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Action:
                    return ExecuteAction(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Condition:
                    return ExecuteCondition(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Inverter:
                    return ExecuteInverter(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Repeater:
                    return ExecuteRepeater(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                case NodeType.Wait:
                    return ExecuteWait(nodeIndex, ref node, ref bb, ref state, ref ctx);
                    
                default:
                    return NodeStatus.Failure; // Unknown node type
            }
        }
    }
}
```

### 3.3 Resumable Sequence

The sequence composite demonstrates the **"skip already-succeeded children"** optimization:

```csharp
private NodeStatus ExecuteSequence(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    int currentChildIndex = nodeIndex + 1; // First child immediately follows parent
    
    for (int i = 0; i < node.ChildCount; i++)
    {
        ref var childNode = ref _blob.Nodes[currentChildIndex];
        
        // === RESUME OPTIMIZATION ===
        // If we're resuming deeper in the tree (RunningNodeIndex > currentChild),
        // it means this child already succeeded in a previous frame.
        // Skip it to avoid re-executing.
        
        if (state.RunningNodeIndex > 0 && 
            state.RunningNodeIndex > (currentChildIndex + childNode.SubtreeOffset))
        {
            // This child is "to the left" of the running node
            // In a Sequence, that means it succeeded
            currentChildIndex += childNode.SubtreeOffset;
            continue;
        }
        
        // === EXECUTE CHILD ===
        var result = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);
        
        if (result == NodeStatus.Running)
            return NodeStatus.Running;
            
        if (result == NodeStatus.Failure)
            return NodeStatus.Failure; // Sequence fails on first child failure
        
        // Child succeeded -> move to next
        currentChildIndex += childNode.SubtreeOffset;
    }
    
    // All children succeeded
    return NodeStatus.Success;
}
```

**Key Points:**
- **Skipping logic:** Avoids re-executing succeeded children
- **Early exit:** Returns immediately on `Running` or `Failure`
- **No allocations:** Pure stack-based recursion
- **Cache-friendly:** Linear array traversal

### 3.4 Resumable Selector

Inverse logic of Sequence:

```csharp
private NodeStatus ExecuteSelector(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehavitorTreeState state,
    ref TContext ctx)
{
    int currentChildIndex = nodeIndex + 1;
    
    for (int i = 0; i < node.ChildCount; i++)
    {
        ref var childNode = ref _blob.Nodes[currentChildIndex];
        
        // === RESUME OPTIMIZATION ===
        // In Selector: if running node is to the right, 
        // children to the left must have FAILED
       
        if (state.RunningNodeIndex > 0 &&
            state.RunningNodeIndex > (currentChildIndex + childNode.SubtreeOffset))
        {
            // Skip already-failed children
            currentChildIndex += childNode.SubtreeOffset;
            continue;
        }
        
        // === EXECUTE CHILD ===
        var result = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);
        
        if (result == NodeStatus.Running)
            return NodeStatus.Running;
            
        if (result == NodeStatus.Success)
            return NodeStatus.Success; // Selector succeeds on first child success
        
        // Child failed -> try next
        currentChildIndex += childNode.SubtreeOffset;
    }
    
    // All children failed
    return NodeStatus.Failure;
}
```

### 3.5 Observer Abort Pattern

**Observer decorators** re-evaluate guards even when not actively running.

```csharp
private NodeStatus ExecuteObserverSelector(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    int currentChildIndex = nodeIndex + 1;
    
    for (int i = 0; i < node.ChildCount; i++)
    {
        ref var childNode = ref _blob.Nodes[currentChildIndex];
        
        // === GUARD RE-EVALUATION ===
        // Even if we're running a later child, re-check earlier guards
        bool isRunningLaterNode = state.RunningNodeIndex > 0 &&
            state.RunningNodeIndex > (currentChildIndex + childNode.SubtreeOffset);
        
        if (isRunningLaterNode)
        {
            // Check if this child is a "guard" (Condition/Observer)
            if (IsGuardNode(childNode.Type))
            {
                // Re-evaluate the guard
                var guardResult = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);
                
                if (guardResult == NodeStatus.Success)
                {
                    // ABORT! Higher priority succeeded
                    state.TreeVersion++; // Invalidate async operations
                    state.RunningNodeIndex = 0; // Reset
                    return NodeStatus.Success; // Switch to this branch
                }
            }
            
            // Guard failed or not a guard -> skip
            currentChildIndex += childNode.SubtreeOffset;
            continue;
        }
        
        // === NORMAL EXECUTION ===
        var result = ExecuteNode(currentChildIndex, ref bb, ref state, ref ctx);
        
        if (result == NodeStatus.Running)
            return NodeStatus.Running;
        if (result == NodeStatus.Success)
            return NodeStatus.Success;
        
        currentChildIndex += childNode.SubtreeOffset;
    }
    
    return NodeStatus.Failure;
}

private bool IsGuardNode(NodeType type)
{
    return type == NodeType.Condition || type == NodeType.Observer;
}
```

**Key Points:**
- **Reactive:** Guards checked every frame, even when not active
- **Interrupt:** Sets `TreeVersion++` to invalidate zombie async requests
- **Performance:** Only re-evaluates conditions (cheap), not full subtrees

### 3.6 Action/Leaf Execution

```csharp
private NodeStatus ExecuteAction(
    int nodeIndex,
    ref NodeDefinition node,
    ref TBlackboard bb,
    ref BehaviorTreeState state,
    ref TContext ctx)
{
    // Get the cached delegate for this action
    var actionDelegate = _actionDelegates[node.PayloadIndex];
    
    // Execute the user's logic
    var status = actionDelegate(
        ref bb,
        ref state,
        ref ctx,
        node.PayloadIndex); // Pass payload for parameter lookup
    
    // Update running state
    if (status == NodeStatus.Running)
    {
        state.RunningNodeIndex = (ushort)nodeIndex;
    }
    else if (state.RunningNodeIndex == nodeIndex)
    {
        // We just finished - clear running marker
        state.RunningNodeIndex = 0;
    }
    
    return status;
}
```

### 3.7 Delegate Binding

Caching delegates for performance:

```csharp
private NodeLogicDelegate<TBlackboard, TContext>[] BindActions(
    BehaviorTreeBlob blob,
    ActionRegistry<TBlackboard, TContext> registry)
{
    var delegates = new NodeLogicDelegate<TBlackboard, TContext>[blob.MethodNames.Length];
    
    for (int i = 0; i < blob.MethodNames.Length; i++)
    {
        string methodName = blob.MethodNames[i];
        
        if (registry.TryGetAction(methodName, out var del))
        {
            delegates[i] = delegate;
        }
        else
        {
            // Fallback: log error and use failure delegate
            Console.WriteLine($"[BT] Warning: Action '{methodName}' not found");
            delegates[i] = (ref TBlackboard _, ref BehaviorTreeState __, ref TContext ___, int ____) 
                => NodeStatus.Failure;
        }
    }
    
    return delegates;
}
```

---

## 4. Action Delegate Signature

### 4.1 Standard Signature

All user actions must match this signature:

```csharp
namespace Fbt
{
    /// <summary>
    /// Delegate signature for behavior tree node logic.
    /// </summary>
    public delegate NodeStatus NodeLogicDelegate<TBlackboard, TContext>(
        ref TBlackboard blackboard,
        ref BehaviorTreeState state,
        ref TContext context,
        int paramIndex) // NEW: Access to parameters
        where TBlackboard : struct
        where TContext : struct, IAIContext;
}
```

### 4.2 Example User Action

```csharp
public static class CombatActions
{
    public static NodeStatus Attack(
        ref OrcBlackboard bb,
        ref BehaviorTreeState state,
        ref GameContext ctx,
        int paramIndex)
    {
        // Check preconditions
        if (bb.TargetEntityId == 0)
            return NodeStatus.Failure;
        
        if (!ctx.IsEntityAlive(bb.TargetEntityId))
            return NodeStatus.Failure;
        
        // Execute attack
        ctx.TriggerAnimation(bb.SelfEntityId, "Attack");
        ctx.DealDamage(bb.TargetEntityId, 10f);
        
        return NodeStatus.Success;
    }
    
    public static NodeStatus MoveToTarget(
        ref OrcBlackboard bb,
        ref BehaviorTreeState state,
        ref GameContext ctx,
        int paramIndex)
    {
        var targetPos = ctx.GetEntityPosition(bb.TargetEntityId);
        
        // === ASYNC PATTERN ===
        var token = AsyncToken.Unpack(state.AsyncHandles[0]);
        
        // Validate token
        if (!token.IsValid(state.TreeVersion))
            token = new AsyncToken(0, 0);
        
        // Issue request if needed
        if (token.RequestID == 0)
        {
            int reqId = ctx.RequestPath(bb.Position, targetPos);
            state.AsyncHandles[0] = new AsyncToken(reqId, state.TreeVersion).Pack();
            return NodeStatus.Running;
        }
        
        // Poll result
        var result = ctx.GetPathResult(token.RequestID);
        
        if (!result.IsReady)
            return NodeStatus.Running;
        
        // Process result
        state.AsyncHandles[0] = 0; // Clear
        
        return result.Success ? NodeStatus.Success : NodeStatus.Failure;
    }
}
```

---

## 5. JIT Compiler (Phase 2 - Future)

### 5.1 Architecture

The JIT compiler generates `DynamicMethod` that emits IL directly:

```
┌────────────────────────────────────────┐
│      TreeCompiler.Compile()            │
│  1. Create DynamicMethod               │
│  2. Emit resume switch (jump table)    │
│  3. Emit node logic (inline)           │
│  4. Return delegate                    │
└────────────────────────────────────────┘
              ↓
┌────────────────────────────────────────┐
│     Generated IL (Pseudocode)          │
│                                         │
│  // Resume jump table                  │
│  switch (state.RunningNodeIndex) {     │
│    case 0: goto Label_Root;            │
│    case 5: goto Label_Attack;          │
│  }                                      │
│                                         │
│  Label_Root:                            │
│    // Sequence logic...                │
│                                         │
│  Label_Attack:                          │
│    call Attack_Method                  │
│    // Store running index...           │
│    ret                                  │
│                                         │
└────────────────────────────────────────┘
```

### 5.2 JIT-Friendly Design Decisions

To keep the interpreter **JIT-ready**, we follow these principles:

1. **Flat Array:** No recursive tree objects (already flat)
2. **Resumable State:** `RunningNodeIndex` maps directly to IL labels
3. **No Virtual Calls:** All logic is static/direct calls
4. **Explicit Jumps:** `SubtreeOffset` directly translates to `br` (branch) instructions

### 5.3 Example IL Generation (Conceptual)

```csharp
private void EmitSequence(ILGenerator il, int nodeIndex, Label[] labels)
{
    ref var node = ref _blob.Nodes[nodeIndex];
    Label failLabel = il.DefineLabel();
    
    int currentChild = nodeIndex + 1;
    
    for (int i = 0; i < node.ChildCount; i++)
    {
        // Mark label for this child (for resume jumps)
        il.MarkLabel(labels[currentChild]);
        
        // Emit child logic recursively
        EmitNode(il, currentChild, labels);
        
        // Stack now has: [NodeStatus]
        
        // Check for Running
        il.Emit(OpCodes.Dup);           // [Status, Status]
        il.Emit(OpCodes.Ldc_I4_2);      // [Status, Status, 2 (Running)]
        il.Emit(OpCodes.Beq, returnRunningLabel); // if == Running, exit
        
        // Check for Failure
        il.Emit(OpCodes.Brfalse, failLabel); // if == 0 (Failure), jump to fail
        
        // Success -> continue to next child
        currentChild += _blob.Nodes[currentChild].SubtreeOffset;
    }
    
    // All succeeded
    il.Emit(OpCodes.Ldc_I4_1); // Load Success
    il.Emit(OpCodes.Ret);
    
    // Failure label
    il.MarkLabel(failLabel);
    il.Emit(OpCodes.Ldc_I4_0); // Load Failure
    il.Emit(OpCodes.Ret);
}
```

---

## 6. Performance Characteristics

### 6.1 Interpreter

**Throughput:**
- ~1-5 μs per tick (simple trees, cached delegates)
- ~10,000-50,000 entities @ 60 FPS (single-threaded)

**Overhead:**
- Delegate invocation: ~2-3 ns per call (negligible)
- Recursion: Managed stack frames (~10-20 bytes each)
- Switch statements: Branch prediction friendly

### 6.2 JIT Compiler (Projected)

**Throughput:**
- ~0.5-2 μs per tick (10-50% faster than interpreter)
- Diminishing returns for action-heavy trees (actions dominate time)

**Trade-offs:**
- Compilation time: ~5-50 ms per tree (one-time cost)
- Debugging difficulty: IL debugging is hard
- Code complexity: 5-10× more complex than interpreter

**Decision:** Start with interpreter, profile, then decide.

---

## 7. Execution Flow Diagram

```
┌──────────────────────────────────────────────────────┐
│                  Game Loop                           │
└──────────────────────────────────────────────────────┘
                     │
                     ↓
┌──────────────────────────────────────────────────────┐
│         For each entity with BT component:           │
│  1. runner.Tick(ref blackboard, ref state, ref ctx)  │
└──────────────────────────────────────────────────────┘
                     │
                     ↓
┌──────────────────────────────────────────────────────┐
│              Interpreter.Tick()                      │
│  ┌────────────────────────────────────────────────┐  │
│  │ 1. Hot Reload Check                            │  │
│  │    if (hash mismatch) → state.Reset()          │  │
│  ├────────────────────────────────────────────────┤  │
│  │ 2. Execute Tree                                │  │
│  │    result = ExecuteNode(0, ...)                │  │
│  ├────────────────────────────────────────────────┤  │
│  │ 3. Cleanup                                     │  │
│  │    if (result != Running) → clear state        │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
                     │
                     ↓
┌──────────────────────────────────────────────────────┐
│           ExecuteNode(nodeIndex)                     │
│  ┌────────────────────────────────────────────────┐  │
│  │ Switch on node.Type:                           │  │
│  │   Sequence → ExecuteSequence()                 │  │
│  │   Selector → ExecuteSelector()                 │  │
│  │   Action   → ExecuteAction() → UserDelegate()  │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
                     │
                     ↓
┌──────────────────────────────────────────────────────┐
│                   User Action                        │
│  static NodeStatus Attack(ref BB, ref State, ...)    │
│    - Check preconditions                             │
│    - Execute via context (batched)                   │
│    - Return Success/Failure/Running                  │
└──────────────────────────────────────────────────────┘
```

---

**Next Document:** `03-Context-System.md`

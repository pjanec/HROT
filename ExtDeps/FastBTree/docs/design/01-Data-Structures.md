# FastBTree Data Structures

**Version:** 1.0.0  
**Date:** 2026-01-04

---

## 1. Overview

This document defines the memory layout and data structures for the FastBTree library. All structures are designed for:

- **Cache efficiency** (64-byte alignment where critical)
- **Zero allocation** (blittable types, no managed references in hot path)
- **ECS compatibility** (unmanaged structs)
- **Serialization-friendly** (explicit layouts, predictable sizes)

---

## 2. Core Enumerations

### 2.1 NodeStatus

Return value from all node execution.

```csharp
namespace Fbt
{
    /// <summary>
    /// Result of a node's execution.
    /// </summary>
    public enum NodeStatus : byte
    {
        /// <summary>Node failed to complete its task.</summary>
        Failure = 0,
        
        /// <summary>Node successfully completed its task.</summary>
        Success = 1,
        
        /// <summary>Node is still executing (multi-frame).</summary>
        Running = 2
    }
}
```

**Design Notes:**
- `byte` size (minimal memory footprint)
- Ordered for logical branching (Failure=0 allows `brfalse` in IL)
- Three states only (no "Error" or "Invalid" - those are exceptions)

### 2.2 NodeType

Defines the type of a node in the tree.

```csharp
namespace Fbt
{
    /// <summary>
    /// Type of behavior tree node.
    /// </summary>
    public enum NodeType : byte
    {
        // Core Composites
        Root = 0,
        Selector = 1,
        Sequence = 2,
        Parallel = 3,
        
        // Leaves
        Action = 10,
        Condition = 11,
        Wait = 12,
        
        // Decorators
        Inverter = 20,
        Repeater = 21,
        Cooldown = 22,
        ForceSuccess = 23,
        ForceFailure = 24,
        UntilSuccess = 25,
        UntilFailure = 26,
        
        // Advanced
        Service = 30,
        Observer = 31,
        Subtree = 40
    }
}
```

**Design Notes:**
- Grouped by category (composites, leaves, decorators)
- Leaves room for future additions
- `byte` size (used in NodeDefinition for compactness)

---

## 3. Node Definition (The "Bytecode")

### 3.1 NodeDefinition Structure

The fundamental unit of the tree blob. Stored in a flat array.

```csharp
using System.Runtime.InteropServices;

namespace Fbt
{
    /// <summary>
    /// Single node in the behavior tree bytecode.
    /// Size: 8 bytes (tightly packed).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NodeDefinition
    {
        /// <summary>Type of this node.</summary>
        public NodeType Type;           // 1 byte
        
        /// <summary>Number of immediate children.</summary>
        public byte ChildCount;         // 1 byte
        
        /// <summary>
        /// Distance to next sibling (in node indices).
        /// Used for skipping entire subtrees.
        /// NextSiblingIndex = CurrentIndex + SubtreeOffset
        /// </summary>
        public ushort SubtreeOffset;    // 2 bytes
        
        /// <summary>
        /// Index into lookup tables:
        /// - For Action/Condition: index into MethodNames[]
        /// - For Wait: index into FloatParams[] (duration)
        /// - For Decorator params: index into IntParams[]
        /// - For Subtree: index into SubtreeAssetIds[]
        /// </summary>
        public int PayloadIndex;        // 4 bytes
        
        // Total: 8 bytes
    }
}
```

**Memory Layout:**
```
Offset  Size  Field
  0      1    Type
  1      1    ChildCount
  2      2    SubtreeOffset
  4      4    PayloadIndex
Total: 8 bytes
```

**Example Tree Encoding:**
```
Tree:
  Root
    ├─ Selector
    │   ├─ Condition("HasTarget")
    │   └─ Action("Attack")
    └─ Action("Patrol")

Array:
[0] Root(1 child, offset=6)
[1] Selector(2 children, offset=4)
[2] Condition(0 children, offset=1, payload=0 → "HasTarget")
[3] Action(0 children, offset=1, payload=1 → "Attack")
[4] Action(0 children, offset=1, payload=2 → "Patrol")
```

---

## 4. Behavior Tree Blob (Asset)

### 4.1 BehaviorTreeBlob Class

The immutable, shared asset representing a compiled tree.

```csharp
using System;

namespace Fbt
{
    /// <summary>
    /// Compiled behavior tree asset (immutable, shared across entities).
    /// </summary>
    [Serializable]
    public class BehaviorTreeBlob
    {
        // ===== Metadata =====
        
        /// <summary>Name of this tree (e.g., "OrcCombat").</summary>
        public string TreeName;
        
        /// <summary>Version number for compatibility checking.</summary>
        public int Version = 1;
        
        /// <summary>
        /// Hash of node structure (types + hierarchy).
        /// Used for hot reload detection.
        /// </summary>
        public int StructureHash;
        
        /// <summary>
        /// Hash of parameters (floats, ints).
        /// Used for soft reload (parameter-only changes).
        /// </summary>
        public int ParamHash;
        
        // ===== Core Data =====
        
        /// <summary>
        /// The bytecode: flat array of nodes (depth-first order).
        /// </summary>
        public NodeDefinition[] Nodes;
        
        // ===== Lookup Tables =====
        
        /// <summary>
        /// Method names for Action/Condition nodes.
        /// PayloadIndex in NodeDefinition indexes into this.
        /// Example: ["Attack", "Patrol", "HasTarget"]
        /// </summary>
        public string[] MethodNames;
        
        /// <summary>
        /// Float parameters (e.g., Wait durations, ranges).
        /// Example: [2.0f, 5.0f, 10.0f]
        /// </summary>
        public float[] FloatParams;
        
        /// <summary>
        /// Integer parameters (e.g., repeat counts, thresholds).
        /// Example: [3, 10, 100]
        /// </summary>
        public int[] IntParams;
        
        /// <summary>
        /// Asset IDs for subtree references (if using runtime linking).
        /// Example: ["Patrol", "CombatTactics"]
        /// </summary>
        public string[] SubtreeAssetIds;
        
        // ===== Compiled Delegates (Optional) =====
        
        /// <summary>
        /// JIT-compiled delegate (if using JIT mode).
        /// Null in interpreter mode.
        /// </summary>
        [NonSerialized]
        public object CompiledDelegate; // Typed as object to avoid generic in blob
    }
}
```

**Memory Characteristics:**
- **Shared:** One instance per tree type (all Orcs share one blob)
- **Immutable:** Never modified after compilation
- **Size:** ~8 bytes × node count + lookup table sizes
  - Example: 100-node tree ≈ 800 bytes + tables ≈ 1-2 KB total

---

## 5. Behavior Tree State (Runtime)

### 5.1 BehaviorTreeState Structure

Per-entity runtime state. **Critical: Must fit in 64 bytes for cache line optimization.**

```csharp
using System.Runtime.InteropServices;

namespace Fbt
{
    /// <summary>
    /// Per-entity behavior tree runtime state.
    /// Size: Exactly 64 bytes (single cache line).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct BehaviorTreeState
    {
        // ===== HEADER (8 bytes) =====
        
        /// <summary>
        /// Index of currently running node (0 = not running/root start).
        /// Using ushort allows 65,535 nodes max (sufficient).
        /// </summary>
        [FieldOffset(0)]
        public ushort RunningNodeIndex;
        
        /// <summary>
        /// Current depth in the execution stack (for subtrees).
        /// </summary>
        [FieldOffset(2)]
        public ushort StackPointer;
        
        /// <summary>
        /// Tree generation/version for async safety.
        /// Incremented on abort or reset.
        /// </summary>
        [FieldOffset(4)]
        public uint TreeVersion;
        
        // ===== EXECUTION STACK (16 bytes) =====
        
        /// <summary>
        /// Stack of node indices (for subtree calls).
        /// Each level stores the running node index at that depth.
        /// Max depth: 8 levels (sufficient for most trees).
        /// </summary>
        [FieldOffset(8)]
        public fixed ushort NodeIndexStack[8];
        
        // ===== LOCAL REGISTERS (16 bytes) =====
        
        /// <summary>
        /// General-purpose storage for node-local state.
        /// [0-3]: Available for nodes to store counters, flags, etc.
        /// Commonly: [0] = loop counter, [1] = auxiliary state
        /// </summary>
        [FieldOffset(24)]
        public fixed int LocalRegisters[4];
        
        // ===== ASYNC HANDLES (24 bytes) =====
        
        /// <summary>
        /// Storage for async operation handles.
        /// Each handle is a packed ulong: (TreeVersion << 32) | RequestID
        /// [0-2]: Three concurrent async operations max
        /// </summary>
        [FieldOffset(40)]
        public fixed ulong AsyncHandles[3];
        
        // ===== HOT RELOAD (0 bytes - reuses existing) =====
        // Note: We can reuse TreeVersion for hot reload hash checking
        
        // TOTAL: 64 bytes exactly
        
        // ===== Helper Properties =====
        
        /// <summary>Get/set current running node at current stack depth.</summary>
        public ushort CurrentRunningNode
        {
            get => NodeIndexStack[StackPointer];
            set => NodeIndexStack[StackPointer] = value;
        }
        
        /// <summary>Reset state to initial values.</summary>
        public void Reset()
        {
            RunningNodeIndex = 0;
            StackPointer = 0;
            TreeVersion++;
            
            // Clear stacks and registers
            for (int i = 0; i < 8; i++)
                NodeIndexStack[i] = 0;
            for (int i = 0; i < 4; i++)
                LocalRegisters[i] = 0;
            for (int i = 0; i < 3; i++)
                AsyncHandles[i] = 0;
        }
        
        /// <summary>Push node index onto stack (entering subtree).</summary>
        public void PushNode(ushort nodeIndex)
        {
            if (StackPointer < 7) // Max depth check
            {
                StackPointer++;
                NodeIndexStack[StackPointer] = nodeIndex;
            }
            // else: Stack overflow - handle gracefully or log error
        }
        
        /// <summary>Pop from stack (exiting subtree).</summary>
        public void PopNode()
        {
            if (StackPointer > 0)
            {
                NodeIndexStack[StackPointer] = 0; // Clear for safety
                StackPointer--;
            }
        }
    }
}
```

**Memory Layout Diagram:**
```
┌────────────────────────────────────────────────────┐
│ Offset 0-7: Header                                 │
│   0-1: RunningNodeIndex (ushort)                   │
│   2-3: StackPointer (ushort)                       │
│   4-7: TreeVersion (uint)                          │
├────────────────────────────────────────────────────┤
│ Offset 8-23: Execution Stack (8 × ushort)         │
│   NodeIndexStack[0..7]                             │
├────────────────────────────────────────────────────┤
│ Offset 24-39: Local Registers (4 × int)           │
│   LocalRegisters[0..3]                             │
├────────────────────────────────────────────────────┤
│ Offset 40-63: Async Handles (3 × ulong)           │
│   AsyncHandles[0..2]                               │
└────────────────────────────────────────────────────┘
Total: 64 bytes (single cache line)
```

---

## 6. Async Token

### 6.1 AsyncToken Structure

Safe wrapper for async operations with version checking.

```csharp
namespace Fbt
{
    /// <summary>
    /// Token for async operations (pathfinding, raycasts, etc.).
    /// Packs request ID with tree version for zombie-request detection.
    /// </summary>
    public readonly struct AsyncToken
    {
        /// <summary>The actual request ID from external system.</summary>
        public readonly int RequestID;
        
        /// <summary>TreeVersion when this request was made.</summary>
        public readonly uint Version;
        
        public AsyncToken(int requestId, uint version)
        {
            RequestID = requestId;
            Version = version;
        }
        
        /// <summary>Pack into ulong for storage in AsyncHandles[].</summary>
        public ulong Pack() => ((ulong)Version << 32) | (uint)RequestID;
        
        /// <summary>Unpack from ulong storage.</summary>
        public static AsyncToken Unpack(ulong packed)
        {
            int id = (int)(packed & 0xFFFFFFFF);
            uint version = (uint)(packed >> 32);
            return new AsyncToken(id, version);
        }
        
        /// <summary>Check if this token is valid for current tree version.</summary>
        public bool IsValid(uint currentTreeVersion)
            => RequestID != 0 && Version == currentTreeVersion;
    }
}
```

---

## 7. Blackboard (User-Defined)

### 7.1 Blackboard Contract

The blackboard is a **compile-time defined struct** provided by the user.

```csharp
namespace Fbt
{
    /// <summary>
    /// Example blackboard for Orc AI.
    /// User defines their own blackboard types as unmanaged structs.
    /// </summary>
    public struct OrcBlackboard
    {
        // Entity References
        public int SelfEntityId;
        public int TargetEntityId;
        public int InteractionTargetId;
        
        // Spatial Data
        public Vector3 Position;
        public Vector3 Destination;
        public float ForwardAngle;
        
        // State
        public float Health;
        public float AggroRange;
        public bool IsAlerted;
        
        // Timers (managed by actions)
        public float LastTauntTime;
        public float CombatTimer;
    }
}
```

**Guidelines:**
- **Struct only** (no classes, no managed references)
- Prefer `float`, `int`, `Vector3` (blittable types)
- No arrays or collections (use fixed buffers if needed)
- Size should be reasonable (< 256 bytes recommended)

---

## 8. Context Interface

### 8.1 IAIContext

Abstraction for external systems (physics, pathfinding, etc.).

```csharp
using System.Numerics;

namespace Fbt
{
    /// <summary>
    /// Context providing external services to behavior tree nodes.
    /// Allows for testability (mock implementations).
    /// </summary>
    public interface IAIContext
    {
        // ===== Time =====
        float DeltaTime { get; }
        float Time { get; }
        int FrameCount { get; }
        
        // ===== Physics Queries (Batched) =====
        int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance);
        RaycastResult GetRaycastResult(int requestId);
        
        int RequestOverlapSphere(Vector3 center, float radius);
        OverlapResult GetOverlapResult(int requestId);
        
        // ===== Pathfinding (Batched) =====
        int RequestPath(Vector3 from, Vector3 to);
        PathResult GetPathResult(int requestId);
        
        // ===== Random =====
        int RandomInt(int min, int max);
        float RandomFloat(float min, float max);
        
        // ===== Entity Queries =====
        Vector3 GetEntityPosition(int entityId);
        bool IsEntityAlive(int entityId);
        
        // ===== Animation/Commands =====
        void TriggerAnimation(int entityId, string animationName);
        void DealDamage(int targetId, float amount);
        
        // ===== Parameter Lookup =====
        float GetFloatParam(int index);
        int GetIntParam(int index);
    }
    
    public struct RaycastResult
    {
        public bool IsReady;
        public bool Hit;
        public int HitEntityId;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
    }
    
    public struct PathResult
    {
        public bool IsReady;
        public bool Success;
        public int PathId; // Handle to path data
    }
    
    public struct OverlapResult
    {
        public bool IsReady;
        public int HitCount;
        public unsafe fixed int HitEntityIds[16]; // Max 16 hits
    }
}
```

---

## 9. Memory Budget Summary

### Typical Memory Usage

**Per Entity:**
```
BehaviorTreeState: 64 bytes
Blackboard:        ~128 bytes (user-defined)
Total per entity:  ~192 bytes
```

**For 10,000 entities:**
```
State + Blackboard: 10,000 × 192 = 1.92 MB
```

**Shared Assets:**
```
BehaviorTreeBlob (100-node tree): ~2 KB
If 10 unique tree types:          ~20 KB
```

**Total for 10K entities:**
```
~2 MB (excellent cache efficiency)
```

---

## 10. Validation Rules

### 10.1 Design Constraints

**NodeDefinition:**
- ✅ Must be blittable (no managed references)
- ✅ SubtreeOffset must be > 0 and ≤ remaining array length
- ✅ PayloadIndex must be valid for its table
- ✅ ChildCount must match actual children in array

**BehaviorTreeState:**
- ✅ Must be exactly 64 bytes
- ✅ Must be `unsafe struct` with `fixed` buffers
- ✅ No managed references allowed
- ✅ All indices must be validated before use

**BehaviorTreeBlob:**
- ✅ Nodes array must be depth-first traversal
- ✅ All lookup tables must be dense (no null entries)
- ✅ StructureHash must be deterministic
- ✅ Root node must be at index 0

---

**Next Document:** `02-Execution-Model.md`

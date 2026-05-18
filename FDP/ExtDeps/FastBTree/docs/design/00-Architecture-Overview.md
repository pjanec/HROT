# FastBTree Architecture Overview

**Version:** 1.0.0  
**Date:** 2026-01-04  
**Namespace:** Fbt  
**Library:** Fbt.Kernel

---

## 1. Executive Summary

FastBTree is a high-performance, data-oriented behavior tree library for C# designed for game AI systems. It prioritizes:

- **Zero-allocation runtime** (no GC pressure)
- **Cache-friendly memory layout** (64-byte aligned components)
- **ECS compatibility** (DOTS/Arch style)
- **Full testability** (deterministic, replayable)
- **JIT-friendly design** (interpreter-first, JIT-ready)

---

## 2. Core Principles

### 2.1 Data-Oriented Design

**Separation of Definition and State:**
- **Definition** (Immutable): `BehaviorTreeBlob` - shared across all entities using the same tree
- **State** (Mutable): `BehaviorTreeState` - per-entity runtime state (64 bytes)

**Memory Layout:**
```
┌─────────────────────┐
│ BehaviorTreeBlob    │ ← Shared Asset (Immutable)
│  - NodeDefinition[] │
│  - MethodNames[]    │
│  - FloatParams[]    │
└─────────────────────┘
         ↓ (referenced by multiple entities)
┌─────────────────────┐
│ Entity 1            │
│  BehaviorTreeState  │ ← 64 bytes
└─────────────────────┘
┌─────────────────────┐
│ Entity 2            │
│  BehaviorTreeState  │ ← 64 bytes
└─────────────────────┘
```

### 2.2 "Bytecode" Approach

Behavior trees are compiled into a **flat array** of node definitions (depth-first traversal):

```
Index  Type       ChildCount  SubtreeOffset  PayloadIndex
  0    Root          1            6             -
  1    Selector      2            5             -
  2    Sequence      2            2             -
  3    Condition     0            1             0  → "HasTarget"
  4    Action        0            1             1  → "Attack"
  5    Action        0            1             2  → "Patrol"
```

**Benefits:**
- Linear traversal (cache-friendly)
- Zero allocations during execution
- Simple serialization
- JIT-friendly structure

### 2.3 Execution Modes

**Dual-Mode Engine:**

```
              ┌─────────────────┐
              │ ITreeRunner     │ ← Common Interface
              └─────────────────┘
                      ▲
                      │
         ┌────────────┴─────────────┐
         │                          │
┌────────────────┐         ┌────────────────┐
│ Interpreter    │         │ JIT Compiler   │
│ (Development)  │         │ (Production)   │
│  - Debuggable  │         │  - Fast        │
│  - Breakpoints │         │  - Optimized   │
└────────────────┘         └────────────────┘
```

**Phase 1:** Interpreter-only (v1.0)  
**Phase 2:** Add JIT compiler based on performance profiling

---

## 3. System Architecture

### 3.1 Core Components

```
┌──────────────────────────────────────────────────────┐
│                   Fbt.Kernel                         │
├──────────────────────────────────────────────────────┤
│                                                       │
│  ┌────────────────┐  ┌─────────────────┐            │
│  │ Data           │  │ Execution       │            │
│  │  - NodeDef     │  │  - ITreeRunner  │            │
│  │  - TreeBlob    │  │  - Interpreter  │            │
│  │  - TreeState   │  │  - JITCompiler  │            │
│  └────────────────┘  └─────────────────┘            │
│                                                       │
│  ┌────────────────┐  ┌─────────────────┐            │
│  │ Serialization  │  │ Tooling         │            │
│  │  - JsonLoader  │  │  - Flattener    │            │
│  │  - BinWriter   │  │  - Validator    │            │
│  └────────────────┘  └─────────────────┘            │
│                                                       │
└──────────────────────────────────────────────────────┘
```

### 3.2 External Integration

```
┌─────────────────────────────────────────────────────┐
│                 Game/Demo Application               │
├─────────────────────────────────────────────────────┤
│                                                      │
│  ┌──────────────┐        ┌──────────────┐          │
│  │ IAIContext   │◄───────│ User Actions │          │
│  │  (Abstract)  │        │  - Attack()  │          │
│  └──────────────┘        │  - MoveTo()  │          │
│         ▲                └──────────────┘          │
│         │                                           │
│  ┌──────┴───────┐                                   │
│  │              │                                    │
│  │  GameContext │  ReplayContext                    │
│  │  (Runtime)   │  (Testing)                        │
│  └──────────────┘  └──────────────┘                │
│                                                      │
└─────────────────────────────────────────────────────┘
```

---

## 4. Key Design Decisions

### 4.1 Cache Line Optimization

**Target:** 64 bytes per `BehaviorTreeState`

```cpp
[StructLayout(LayoutKind.Explicit, Size = 64)]
public unsafe struct BehaviorTreeState
{
    // Header: 8 bytes
    [FieldOffset(0)]  public ushort RunningNodeIndex;
    [FieldOffset(2)]  public ushort StackPointer;
    [FieldOffset(4)]  public uint TreeVersion;
    
    // Stack: 16 bytes (8 levels × 2 bytes)
    [FieldOffset(8)]  public fixed ushort NodeIndexStack[8];
    
    // Registers: 16 bytes
    [FieldOffset(24)] public fixed int LocalRegisters[4];
    
    // Async Handles: 24 bytes (3 × 8 bytes)
    [FieldOffset(40)] public fixed ulong AsyncHandles[3];
    
    // Total: 64 bytes
}
```

**Why ushort for indices?**
- 65,535 max nodes per tree (sufficient for any practical tree)
- Saves 50% space vs int
- Fits entire state in single cache line

### 4.2 Subtree Strategy

**Support Both Modes:**

1. **Monolithic Baking** (Preferred):
   - Subtrees inlined during compilation
   - Single flat array at runtime
   - Best performance (instruction cache locality)
   - Larger binary size

2. **Runtime Linking** (Optional):
   - NodeType.Subtree references external blob
   - Requires tracking blob stack
   - Smaller binary, more memory if shared
   - Slight performance cost

**Decision:** Implement monolithic first, add linking if needed.

### 4.3 Async Query Batching

**Context-based batching for parallel execution:**

```csharp
public interface IAIContext
{
    // Batched Operations
    int RequestRaycast(Vector3 origin, Vector3 direction);
    RaycastStatus GetRaycastStatus(int requestId);
    
    int RequestPath(Vector3 from, Vector3 to);
    PathStatus GetPathStatus(int requestId);
    
    // Frame Management
    void BeginFrame();
    void ProcessBatch(); // Execute all pending queries
    void EndFrame();
}
```

**Flow:**
1. Entity ticks → issues `RequestRaycast()`
2. Returns `Running` with handle in `AsyncHandles[0]`
3. End of frame: `Context.ProcessBatch()`
4. Next frame: Entity polls `GetRaycastStatus()`

### 4.4 Hot Reload Safety

**Hash-Based Versioning:**

```csharp
public class BehaviorTreeBlob
{
    public int StructureHash;  // Node types & hierarchy
    public int ParamHash;      // Float/Int parameters
    // ...
}

public struct BehaviorTreeState
{
    public int RunningBlobHash;
    // ...
}
```

**Reset Logic:**
```csharp
if (state.RunningBlobHash != blob.StructureHash)
{
    // Hard reload: structure changed
    state.Reset();
    state.RunningBlobHash = blob.StructureHash;
}
else if (state.RunningParamHash != blob.ParamHash)
{
    // Soft reload: only parameters changed
    state.RunningParamHash = blob.ParamHash;
    // Continue execution
}
```

---

## 5. Development Phases

### Phase 1: Core (v1.0)
- [x] Data structures (NodeDefinition, TreeBlob, TreeState)
- [x] Interpreter execution engine
- [x] Core node types (Sequence, Selector, Action, Condition, Inverter)
- [x] JSON serialization
- [x] IAIContext abstraction
- [x] Unit test framework

### Phase 2: Demo & Testing
- [ ] ImGui.NET visualization
- [ ] Raylib 2D demo application
- [ ] Multiple demo scenes
- [ ] Recording/replay system
- [ ] Performance profiling
- [ ] Golden Run tests

### Phase 3: Advanced Features
- [ ] All decorator types (Repeater, Cooldown, Observer)
- [ ] Service nodes (periodic updates)
- [ ] Parallel composite
- [ ] Subtree support
- [ ] Dependency tracking

### Phase 4: Optimization
- [ ] JIT compiler (if benchmarks justify)
- [ ] SIMD optimizations
- [ ] Further cache optimization

---

## 6. Non-Goals (v1.0)

- ❌ Visual editor (JSON-based workflow only)
- ❌ Groot2 compatibility (custom JSON only)
- ❌ Dynamic/dictionary blackboards (compile-time only)
- ❌ Blueprint/visual scripting
- ❌ Multi-threading (entities tick sequentially)

---

## 7. Success Criteria

### Performance Targets
- **Throughput:** 10,000+ entities @ 60 FPS (simple trees)
- **Latency:** < 0.1ms per entity tick (average)
- **Memory:** 64 bytes per entity + shared blob
- **GC Pressure:** Zero allocations during runtime

### Quality Targets
- **Test Coverage:** 100% for core logic
- **Determinism:** Bit-perfect replay of recorded sessions
- **Debuggability:** Step-through with full state inspection
- **Maintainability:** < 5,000 LOC for core kernel

---

## 8. Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| JIT complexity too high | Medium | High | Start with interpreter, defer JIT |
| 64-byte constraint too limiting | Low | Medium | Profile first, adjust if needed |
| Async batching overhead | Low | Medium | Benchmark vs immediate queries |
| JSON workflow friction | Medium | Low | Good error messages, validation |
| Hot reload bugs | Medium | High | Extensive testing, hash validation |

---

## 9. References

- Original spec: `docs/reference-archive/BT1-001-initial-spec.md`
- Unity DOTS: Entity Component System architecture patterns
- BehaviorTree.CPP: Industry-standard BT implementation
- Unreal Engine: Observer abort patterns (adapted for data-oriented)

---

**Next Documents:**
- `01-Data-Structures.md` - Detailed memory layouts
- `02-Execution-Model.md` - Interpreter & JIT design
- `03-Context-System.md` - IAIContext & testability
- `04-Serialization.md` - JSON format & asset pipeline
- `05-Testing-Strategy.md` - Unit, integration, golden run tests
- `06-Demo-Application.md` - ImGui + Raylib demo design

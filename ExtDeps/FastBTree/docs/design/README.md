# FastBTree Design Documents - Index

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Namespace:** Fbt  
**Library:** Fbt.Kernel  
**Version:** 1.0.0  
**Date:** 2026-01-04

---

## Document Overview

This directory contains the complete detailed design documentation for the FastBTree library. All documents should be read in order for full understanding.

### Design Documents

| # | Document | Description | Status |
|---|----------|-------------|--------|
| 00 | [Architecture Overview](./00-Architecture-Overview.md) | Core principles, system architecture, phased approach | ✅ Complete |
| 01 | [Data Structures](./01-Data-Structures.md) | Memory layouts, NodeDefinition, BehaviorTreeState (64-byte), AsyncToken | ✅ Complete |
| 02 | [Execution Model](./02-Execution-Model.md) | Interpreter architecture, resumable state machine, observer aborts | ✅ Complete |
| 03 | [Context System](./03-Context-System.md) | IAIContext interface, batched queries, testing contexts | ✅ Complete |
| 04 | [Serialization](./04-Serialization.md) | JSON format, binary format, compilation pipeline, dependency tracking | ✅ Complete |
| 05 | [Testing Strategy](./05-Testing-Strategy.md) | Unit tests, integration tests, golden run regression tests | ✅ Complete |
| 06 | [Demo Application](./06-Demo-Application.md) | ImGui+Raylib demo, multiple scenes, visual debugging, profiling | ✅ Complete |

---

## Quick Reference

### Key Design Decisions

**Architecture:**
- ✅ Interpreter-first (v1.0), JIT-ready design
- ✅ Data-oriented (separation of definition/state)
- ✅ Flat array "bytecode" approach
- ✅ 64-byte cache-aligned BehaviorTreeState

**Execution:**
- ✅ Resumable state machine (no re-evaluation)
- ✅ Observer aborts via guard clause injection
- ✅ Async safety with TreeVersion + AsyncToken
- ✅ Hot reload with hash-based validation

**Context:**
- ✅ Full IAIContext abstraction (day 1)
- ✅ Batched queries for parallel processing
- ✅ Mock/Replay contexts for testing
- ✅ Deterministic random for golden runs

**Serialization:**
- ✅ Custom JSON primary format
- ✅ Binary format for runtime assets
- ✅ Automatic dependency tracking
- ✅ Monolithic baking with subtree support

**Testing:**
- ✅ xUnit framework
- ✅ 100% coverage target for core logic
- ✅ Unit + Integration + Golden Run tests
- ✅ CI/CD pipeline ready

**Demo:**
- ✅ ImGui.NET + Raylib
- ✅ 4 demo scenes (Patrol, Combat, Crowd, Playback)
- ✅ Visual tree debugger
- ✅ Performance profiling
- ✅ Recording/replay system

---

## Implementation Roadmap

### Phase 1: Core (Weeks 1-3)

**Week 1: Foundation**
- [ ] Data structures (NodeDefinition, BehaviorTreeState, enums)
- [ ] Node delegate signature
- [ ] Basic IAIContext interface
- [ ] Unit tests for data structures

**Week 2: Interpreter**
- [ ] Interpreter core (Tick, ExecuteNode)
- [ ] Sequence/Selector implementation
- [ ] Action/Condition execution
- [ ] Basic decorators (Inverter)
- [ ] Unit tests for execution

**Week 3: Serialization**
- [ ] JSON parser → BuilderNode
- [ ] BuilderNode → NodeDefinition[] flattener
- [ ] Binary serializer
- [ ] Tree validation
- [ ] Integration tests

**Deliverable:** Working interpreter with JSON loading, full unit test coverage

---

### Phase 2: Demo & Testing (Weeks 4-6)

**Week 4: Context & Async**
- [ ] GameContext implementation
- [ ] MockContext for tests
- [ ] Async batching system
- [ ] AsyncToken validation
- [ ] Hot reload logic

**Week 5: Demo Application**
- [ ] Raylib + ImGui setup
- [ ] Simple ECS implementation
- [ ] Patrol scene
- [ ] Combat scene
- [ ] TreeVisualizer UI

**Week 6: Recording & Profiling**
- [ ] GoldenRunRecorder
- [ ] ReplayContext
- [ ] PerformanceMonitor
- [ ] Recording UI
- [ ] Golden run tests

**Deliverable:** Full demo app with 4 scenes, recording/replay, visual debugging

---

### Phase 3: Polish (Weeks 7-8)

**Week 7: Advanced Features**
- [ ] All decorators (Repeater, Cooldown, etc.)
- [ ] Service nodes
- [ ] Parallel composite
- [ ] Observer decorator
- [ ] Subtree support

**Week 8: Optimization & Documentation**
- [ ] Performance profiling
- [ ] Memory optimization
- [ ] API documentation
- [ ] User guide
- [ ] Example trees

**Deliverable:** Feature-complete v1.0 with documentation

---

### Phase 4: Future (Post-v1.0)

- [ ] JIT compiler (if benchmarks justify)
- [ ] Visual tree editor (standalone tool)
- [ ] Groot2 format support
- [ ] Multi-threading support
- [ ] Unity/Godot integration examples

---

## File Organization

```
FastBTree/
├── docs/
│   ├── design/                     ← You are here
│   │   ├── 00-Architecture-Overview.md
│   │   ├── 01-Data-Structures.md
│   │   ├── 02-Execution-Model.md
│   │   ├── 03-Context-System.md
│   │   ├── 04-Serialization.md
│   │   ├── 05-Testing-Strategy.md
│   │   └── 06-Demo-Application.md
│   ├── reference-archive/
│   │   └── BT1-001-initial-spec.md
│   └── api/                        ← Generated docs (future)
├── src/
│   └── Fbt.Kernel/                 ← Core library
│       ├── Data/
│       ├── Runtime/
│       ├── Serialization/
│       └── Tools/
├── demos/
│   └── FastBTreeDemo/              ← ImGui+Raylib demo
├── tests/
│   └── Fbt.Tests/                  ← xUnit tests
└── examples/
    └── Trees/                      ← Sample JSON trees
```

---

## Key Interfaces

### Core Types

```csharp
// Data
public struct NodeDefinition          // 8 bytes
public struct BehaviorTreeState       // 64 bytes
public class BehaviorTreeBlob
public enum NodeStatus : byte
public enum NodeType : byte

// Execution
public interface ITreeRunner<TBB, TCtx>
public class Interpreter<TBB, TCtx>

// Context
public interface IAIContext
public delegate NodeStatus NodeLogicDelegate<TBB, TCtx>(
    ref TBB blackboard,
    ref BehaviorTreeState state,
    ref TCtx context,
    int paramIndex)
```

### Example Usage

```csharp
// 1. Load tree
var blob = TreeCompiler.CompileFromJson(File.ReadAllText("orc.json"));

// 2. Create runner
var registry = new ActionRegistry<OrcBlackboard, GameContext>();
registry.Register("Attack", OrcActions.Attack);
registry.Register("Patrol", OrcActions.Patrol);

var runner = new Interpreter<OrcBlackboard, GameContext>(blob, registry);

// 3. Per-entity tick
ref var blackboard = ref entity.Blackboard;
ref var state = ref entity.BehaviorState;
ref var context = ref gameContext;

var result = runner.Tick(ref blackboard, ref state, ref context);
```

---

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| State Size | 64 bytes | Single cache line |
| Tick Time | < 0.1ms | Per entity average |
| Throughput | 10K entities @ 60fps | With simple trees |
| GC Pressure | Zero | During runtime |
| Compilation | < 50ms | Per tree (one-time) |
| Memory/Entity | ~200 bytes | State + Blackboard |

---

## Dependencies

### Runtime
- .NET 8.0
- System.Numerics (vectors)
- System.Runtime.InteropServices (memory)
- System.Text.Json (serialization)

### Demo
- Raylib-cs 5.0+
- ImGui.NET 1.90+

### Testing
- xUnit 2.6+
- xUnit.Assert

---

## API Stability

| Component | Stability | Notes |
|-----------|-----------|-------|
| NodeDefinition | 🟢 Stable | Binary-compatible |
| BehaviorTreeState | 🟢 Stable | 64-byte layout frozen |
| IAIContext | 🟡 Evolving | May add methods |
| Interpreter | 🟢 Stable | Core algorithm final |
| Serialization | 🟢 Stable | JSON schema versioned |

---

## Questions & Clarifications

### Answered in Design Process

1. ✅ **Interpreter vs JIT?** → Interpreter first, JIT-ready design
2. ✅ **Cache line size?** → 64 bytes (achievable with ushort indices)
3. ✅ **Subtree approach?** → Monolithic baking primary, linking optional
4. ✅ **Test coverage?** → 100% unit + integration + golden runs
5. ✅ **Demo features?** → All must-haves + recording/replay/profiling
6. ✅ **JSON vs Groot?** → Custom JSON primary
7. ✅ **Blackboard?** → Compile-time structs (type-safe)
8. ✅ **Async queries?** → Batched via context
9. ✅ **Hot reload?** → Hash-based with soft/hard reload
10. ✅ **Observer aborts?** → Guard clause injection (JIT-friendly)

### Open for Implementation

- Exact IL generation strategy (if JIT implemented)
- SIMD optimization opportunities
- Multi-threading model (batching across threads)
- Unity/Godot integration patterns

---

## Getting Started

**For Developers:**
1. Read [00-Architecture-Overview.md](./00-Architecture-Overview.md)
2. Study [01-Data-Structures.md](./01-Data-Structures.md)
3. Understand [02-Execution-Model.md](./02-Execution-Model.md)
4. Review example code in [06-Demo-Application.md](./06-Demo-Application.md)

**For Users (Future):**
1. Read API documentation (TBD)
2. Study example trees in `examples/Trees`
3. Run demo application
4. Copy template project

---

## Contact & Contribution

**Project Lead:** [Your Name]  
**Repository:** TBD  
**License:** TBD  
**Status:** Design Phase Complete, Ready for Implementation

---

## Changelog

### 2026-01-04 - Design Phase Complete
- ✅ All 7 design documents completed
- ✅ Architecture solidified
- ✅ API contracts defined
- ✅ Implementation roadmap created
- 🎯 Ready to proceed with Phase 1 implementation

---

**Next Step:** Begin Phase 1 implementation with data structures and unit test framework.

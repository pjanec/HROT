# FastBTree

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Build](https://img.shields.io/badge/build-passing-brightgreen)
![Tests](https://img.shields.io/badge/tests-75%20passing-brightgreen)
![License](https://img.shields.io/badge/license-MIT-green)

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
- Warning detection for known limitations (nested Parallels/Repeaters)

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
registry.Register("IsEnemyVisible", (ref Blackboard bb, ref BehaviorTreeState s, ref Context c, int p) => ...);
registry.Register("Attack", (ref Blackboard bb, ref BehaviorTreeState s, ref Context c, int p) => ...);
registry.Register("Patrol", (ref Blackboard bb, ref BehaviorTreeState s, ref Context c, int p) => ...);

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

Benchmarked on: Intel Core i7-7700HQ CPU 2.80GHz (Kaby Lake), .NET 10.0

### Interpreter Performance

| Benchmark | Mean | Allocated |
|-----------|------|-----------|
| SimpleSequence_Tick (3 nodes) | 30.13 ns | **0 B** |
| ComplexTree_Tick (21 nodes) | 100.15 ns | **0 B** |
| SimpleSequence_Resume | 21.88 ns | **0 B** |

**Key Metrics:**
- ✅ **Zero allocations** in hot path
- ✅ Extremely fast execution (~100ns for complex trees)
- ✅ Cache-friendly processing

### Compilation Performance

| Benchmark | Mean | Allocated |
|-----------|------|-----------|
| CompileSimpleTree | 7.41 μs | 2.82 KB |
| CompileComplexTree | 17.13 μs | 7.42 KB |
| SaveBinary | 273.41 μs | 4.30 KB |
| LoadBinary | 189.24 μs | 4.76 KB |

**Key Metrics:**
- ✅ Fast compilation (microseconds)
- ✅ Efficient binary save/load operations

### Memory Footprint

- NodeDefinition: 8 bytes (cache-aligned)
- BehaviorTreeState: 64 bytes (single cache line)
- Typical 20-node tree: ~160 bytes + lookup tables

## Testing

```bash
dotnet test
```

Current test coverage: 80+ tests, 100% pass rate

## Documentation

See `docs/design/` for detailed specifications:
- `00-Architecture-Overview.md` - System architecture
- `01-Data-Structures.md` - Memory layouts
- `02-Execution-Model.md` - Interpreter design
- `03-Context-System.md` - External integration
- `04-Serialization.md` - Asset pipeline



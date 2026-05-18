# FastHSM

**High-Performance Hierarchical State Machine Library for C#**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8+](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)

---

## 🚀 Features

- ✨ **Zero-Allocation Runtime** - No GC pressure in hot paths
- ⚡ **Cache-Friendly** - Flat arrays, fixed-size structs (64B/128B/256B)
- 🔄 **Hierarchical States** - Nested states with entry/exit actions
- 🎯 **Event-Driven** - Priority-based event queues
- 🔍 **Debug Tracing** - Zero-allocation runtime diagnostics
- 🛠️ **Hot Reload** - Update state machines at runtime
- 🧪 **Deterministic** - Reproducible execution for testing
- 🎮 **ECS-Ready** - Designed for Entity Component Systems

---

## 📦 Installation

### NuGet (Coming Soon)

```bash
dotnet add package FastHSM
```

### From Source

```bash
git clone https://github.com/yourusername/FastHSM.git
cd FastHSM
dotnet build
```

---

## 🎯 Quick Start

### 1. Define Your State Machine

```csharp
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

// Build state machine
var builder = new HsmBuilder("TrafficLight");

var red = builder.State("Red").OnEntry("OnEnterRed");
var green = builder.State("Green").OnEntry("OnEnterGreen");
var yellow = builder.State("Yellow").OnEntry("OnEnterYellow");

red.On(TimerExpiredEvent).GoTo(green);
green.On(TimerExpiredEvent).GoTo(yellow);
yellow.On(TimerExpiredEvent).GoTo(red);

red.Initial();
```

### 2. Define Actions

```csharp
[HsmAction(Name = "OnEnterRed")]
public static unsafe void OnEnterRed(void* instance, void* context, ushort eventId)
{
    Console.WriteLine("🔴 RED - Stop!");
}

[HsmAction(Name = "OnEnterGreen")]
public static unsafe void OnEnterGreen(void* instance, void* context, ushort eventId)
{
    Console.WriteLine("🟢 GREEN - Go!");
}
```

### 3. Compile & Run

```csharp
// Compile
var graph = builder.Build();
HsmNormalizer.Normalize(graph);
HsmGraphValidator.Validate(graph, out var errors);
var flattened = HsmFlattener.Flatten(graph);
var blob = HsmEmitter.Emit(flattened);

// Create instance
var instance = new HsmInstance64();
HsmInstanceManager.Initialize(&instance, blob);

// Update loop
var context = new MyContext();
HsmKernel.Update(blob, ref instance, context, deltaTime);
```

---

## 📚 Documentation

- **[Getting Started](docs/user/GETTING-STARTED.md)** - Step-by-step tutorial
- **[API Reference](docs/user/API-REFERENCE.md)** - Complete API documentation
- **[Examples](docs/user/EXAMPLES.md)** - Real-world examples
- **[Architecture](docs/user/ARCHITECTURE.md)** - How FastHSM works
- **[Performance Guide](docs/user/PERFORMANCE.md)** - Best practices
- **[Migration Guide](docs/user/MIGRATION.md)** - Upgrading between versions

---

## 🎮 Examples

### Traffic Light

Simple state machine with timed transitions:

```bash
cd examples/Fhsm.Examples.Console
dotnet run
```

See `examples/Fhsm.Examples.Console/TrafficLightExample.cs` for full code.

---

## 🏗️ Architecture

FastHSM uses a **data-oriented design** inspired by behavior tree systems:

```
┌─────────────────┐
│ HsmBuilder      │ ← Fluent API for defining state machines
└────────┬────────┘
         ↓
┌─────────────────┐
│ Compiler        │ ← Normalizes, validates, flattens
└────────┬────────┘
         ↓
┌─────────────────┐
│ HsmDefinitionBlob│ ← Immutable "ROM" (flat arrays)
└────────┬────────┘
         ↓
┌─────────────────┐
│ HsmKernel       │ ← Runtime interpreter
└────────┬────────┘
         ↓
┌─────────────────┐
│ HsmInstance     │ ← Mutable "RAM" (64B/128B/256B)
└─────────────────┘
```

**Key Concepts:**
- **ROM (Definition):** Immutable state machine definition (shared across instances)
- **RAM (Instance):** Mutable per-instance state (active states, events, timers)
- **Kernel:** Executes transitions, evaluates guards, invokes actions
- **Compiler:** Converts high-level API to optimized binary format

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run specific test suite
dotnet test --filter "FullyQualifiedName~Kernel"
```

---

## 🎯 Design Goals

1. **Zero-Allocation Runtime** - No GC pressure (fixed-size structs, stack allocation)
2. **Cache-Friendly** - Flat arrays, sequential access patterns
3. **Deterministic** - Reproducible execution (fixed ordering, explicit RNG)
4. **ECS-Compatible** - Unmanaged structs, batched updates, void* context
5. **Fast Compilation** - Compile-time source generation for action dispatch
6. **Debuggable** - Runtime tracing, hot reload, state introspection

---

## 📖 Key Features Explained

### Hierarchical States

States can contain substates (nested hierarchy):

```csharp
var combat = builder.State("Combat");
var attacking = combat.AddChild("Attacking");
var defending = combat.AddChild("Defending");
```

### Entry/Exit Actions

Actions run when entering or exiting states:

```csharp
state.OnEntry("OnEnterState");
state.OnExit("OnExitState");
```

### Activities

Long-running behaviors that run every tick while in a state:

```csharp
state.Activity("UpdateAnimation");
```

### Guards

Conditional transitions (only taken if guard returns true):

```csharp
state.On(AttackEvent)
     .If("IsEnemyInRange")
     .GoTo(attacking);
```

### Event Priority

Events can have priority (Interrupt > Normal > Low):

```csharp
var evt = new HsmEvent
{
    EventId = StunEvent,
    Priority = EventPriority.Interrupt
};
```

### Debug Tracing

Enable tracing for diagnostics:

```csharp
var traceBuffer = new HsmTraceBuffer();
HsmKernelCore.SetTraceBuffer(traceBuffer);

// Enable per-instance
instance.Header.Flags |= InstanceFlags.DebugTrace;
```

---

## 🔧 Configuration

### Instance Size Tiers

Choose instance size based on complexity:

```csharp
var instance64 = new HsmInstance64();   // 4 regions, 4 timers, small queue
var instance128 = new HsmInstance128(); // 8 regions, 8 timers, medium queue
var instance256 = new HsmInstance256(); // 16 regions, 16 timers, large queue
```

### Performance Tuning

See [Performance Guide](docs/user/PERFORMANCE.md) for:
- Batch processing multiple instances
- Minimizing transitions per tick
- Optimal event queue sizing
- Action/guard optimization

---

## 🤝 Contributing

Contributions welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

---

## 📄 License

MIT License - see [LICENSE](LICENSE) for details.

---

## 🙏 Acknowledgments

- Inspired by [FastBTree](docs/btree-design-inspiration/) architecture
- Design influenced by UML State Machine specification
- ECS patterns from Unity DOTS

---

## 📬 Contact

- **Issues:** [GitHub Issues](https://github.com/yourusername/FastHSM/issues)
- **Discussions:** [GitHub Discussions](https://github.com/yourusername/FastHSM/discussions)

---

**Built with ❤️ for high-performance game development**

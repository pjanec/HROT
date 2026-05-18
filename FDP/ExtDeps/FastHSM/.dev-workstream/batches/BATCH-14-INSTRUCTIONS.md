# BATCH-14: User Documentation

**Batch Number:** BATCH-14  
**Tasks:** TASK-E02 (Documentation)  
**Phase:** Phase E - Examples & Polish  
**Estimated Effort:** 5-7 days  
**Priority:** HIGH  
**Dependencies:** BATCH-12.1

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to **BATCH-14**! This batch creates **comprehensive user-facing documentation** for the FastHSM library.

This enables developers to learn how to use FastHSM effectively.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `.dev-workstream/TASK-DEFINITIONS.md` - See TASK-E02
3. **Existing Example:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs` - Reference implementation
4. **Design Overview:** `docs/design/IMPLEMENTATION-PACKAGE-README.md` - Understand the system

### Documentation Location

- **Project README:** `README.md` (NEW - project root)
- **Getting Started:** `docs/user/GETTING-STARTED.md` (NEW)
- **API Reference:** `docs/user/API-REFERENCE.md` (NEW)
- **Examples Guide:** `docs/user/EXAMPLES.md` (NEW)
- **Architecture Overview:** `docs/user/ARCHITECTURE.md` (NEW)
- **Performance Guide:** `docs/user/PERFORMANCE.md` (NEW)
- **Migration Guide:** `docs/user/MIGRATION.md` (NEW - for future v2+)

### Questions File

`.dev-workstream/questions/BATCH-14-QUESTIONS.md`

---

## Context

**Core implementation complete (BATCH-12.1).** Now create **user-facing documentation** so developers can effectively use FastHSM.

**This batch creates:**
1. **README.md** - Project overview and quick start
2. **Getting Started** - Step-by-step tutorial
3. **API Reference** - Complete API documentation
4. **Examples Guide** - Walkthrough of examples
5. **Architecture Overview** - How FastHSM works (high-level)
6. **Performance Guide** - Best practices and optimization tips
7. **Migration Guide** - For future versions

**Related Task:**
- [TASK-E02](../TASK-DEFINITIONS.md#task-e02-documentation) - Documentation

---

## 🎯 Batch Objectives

Create comprehensive, user-friendly documentation:
- Enable new users to get started in <15 minutes
- Provide complete API reference
- Explain key concepts clearly
- Show real-world examples
- Document best practices
- Prepare for future migrations

---

## ✅ Tasks

### Task 1: Project Root README

**Create:** `README.md` (project root)

```markdown
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
```

---

### Task 2: Getting Started Guide

**Create:** `docs/user/GETTING-STARTED.md`

This should be a **step-by-step tutorial** covering:

1. **Prerequisites**
   - .NET 8+ SDK
   - IDE (Visual Studio, Rider, or VSCode)
   - Basic C# knowledge
   - Understanding of state machines (link to external resources)

2. **Installation**
   - Adding the package
   - Setting up the project
   - Verifying installation

3. **Your First State Machine**
   - Building a simple two-state machine (On/Off)
   - Defining states
   - Adding transitions
   - Compiling the machine
   - Creating an instance
   - Running updates

4. **Adding Actions**
   - Defining action methods with `[HsmAction]`
   - Entry actions
   - Exit actions
   - Transition actions
   - Registering actions

5. **Working with Events**
   - Defining event IDs
   - Creating events
   - Enqueuing events
   - Event priority
   - Event payloads

6. **Context Data**
   - Defining context structs
   - Passing context to actions
   - Reading context data
   - Modifying state based on context

7. **Hierarchical States**
   - Creating parent/child relationships
   - Entry/exit ordering
   - Transition resolution

8. **Guards**
   - Conditional transitions
   - Defining guard methods
   - Guard evaluation order

9. **Activities**
   - Long-running behaviors
   - Activity lifecycle

10. **Next Steps**
    - See Examples
    - See API Reference
    - See Performance Guide

**Format:** Clear code examples for each section, expected output, common mistakes.

---

### Task 3: API Reference

**Create:** `docs/user/API-REFERENCE.md`

Comprehensive API documentation organized by namespace:

#### Fhsm.Compiler

**HsmBuilder**
- Purpose, usage pattern
- Methods: `State()`, `Build()`
- Example usage

**StateBuilder**
- Methods: `OnEntry()`, `OnExit()`, `Activity()`, `AddChild()`, `Initial()`
- Chainable pattern

**TransitionBuilder**
- Methods: `GoTo()`, `If()`, `Do()`
- Chainable pattern

**HsmNormalizer**
- Static method: `Normalize()`
- What it does, when to call

**HsmGraphValidator**
- Static method: `Validate()`
- Validation rules
- Error messages

**HsmFlattener**
- Static method: `Flatten()`
- What it produces

**HsmEmitter**
- Static method: `Emit()`
- What it produces

#### Fhsm.Kernel.Data

**HsmDefinitionBlob**
- Immutable definition
- Properties: `Header`, `States`, `Transitions`, etc.
- Thread-safe sharing

**HsmInstance64/128/256**
- Instance sizes
- When to use each
- Memory layout

**HsmEvent**
- Structure
- EventId, Priority, Flags, Payload
- Creating events

**EventPriority**
- Interrupt, Normal, Low
- Ordering guarantees

**InstanceFlags**
- Available flags
- DebugTrace, Error, Paused

**StateFlags**
- Available flags
- IsComposite, HasHistory, etc.

#### Fhsm.Kernel

**HsmKernel**
- `Update()` - Single instance update
- `UpdateBatch()` - Batch processing
- `Trigger()` - Force initial entry

**HsmInstanceManager**
- `Initialize()` - Setup new instance
- `Reset()` - Clear instance state
- `SelectTier()` - Choose instance size

**HsmEventQueue**
- `TryEnqueue()` - Add event
- `TryDequeue()` - Remove event
- `TryPeek()` - Look at next event
- `Clear()` - Empty queue
- `GetCount()` - Queue size

**HsmValidator**
- `ValidateDefinition()` - Check blob
- `ValidateInstance()` - Check instance
- Runtime validation

**HsmTraceBuffer**
- `WriteTransition()`, `WriteEventHandled()`, etc.
- `GetTraceData()` - Read trace
- `Clear()` - Reset buffer
- `FilterLevel` - Control verbosity

#### Fhsm.Kernel.Attributes

**[HsmAction]**
- Marks action methods
- `Name` property (optional)
- Method signature requirements

**[HsmGuard]**
- Marks guard methods
- `Name` property (optional)
- `UsesRNG` property
- Method signature requirements
- Return value requirements

#### Fhsm.SourceGen

**HsmActionGenerator**
- Automatic source generation
- How it works
- Generated code structure

---

### Task 4: Examples Guide

**Create:** `docs/user/EXAMPLES.md`

Detailed walkthrough of each example:

#### Example 1: Traffic Light

**Location:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs`

**What it demonstrates:**
- Simple linear state sequence
- Timed transitions
- Entry actions
- Activities
- Event handling

**Code walkthrough:**
- Step-by-step explanation
- Key concepts highlighted
- Expected output
- Variations to try

#### Example 2: Character Controller (Future)

**What it would demonstrate:**
- Hierarchical states (Movement → Walking/Running/Jumping)
- Guards (can jump only if grounded)
- Context data (player input)
- Interrupt events (stun, death)

#### Example 3: AI Agent (Future)

**What it would demonstrate:**
- Complex hierarchy (Patrol/Combat/Flee)
- History states (return to last patrol point)
- Orthogonal regions (movement + animation)
- Activity behaviors

**For now:** Document the traffic light example thoroughly, include placeholders for future examples.

---

### Task 5: Architecture Overview

**Create:** `docs/user/ARCHITECTURE.md`

**High-level** explanation (user-facing, not implementation details):

#### 1. Core Concepts

**State Machines 101**
- What is a state machine?
- Why use them?
- When are they useful?

**Hierarchical State Machines**
- Nested states
- Entry/exit order
- Transition bubbling

#### 2. FastHSM Design

**Data-Oriented Philosophy**
- ROM vs RAM
- Why it matters for performance
- Cache-friendly layout

**Compilation Model**
- Builder → Graph → Blob
- Why pre-compile?
- Benefits of immutable definitions

**Runtime Model**
- Interpreter pattern
- 4-phase tick (Entry, RTC, Activity, Idle)
- Run-to-completion semantics

#### 3. Key Subsystems

**Compiler**
- What it does
- Validation rules
- Optimization passes

**Kernel**
- Update loop
- Event processing
- Transition execution
- LCA algorithm (conceptual, not implementation)

**Event System**
- Priority queues
- Tier-specific strategies
- Overflow handling

**Tracing**
- Zero-allocation logging
- Filtering options
- Integration with tools

#### 4. Memory Model

**Instance Tiers**
- 64B vs 128B vs 256B
- Trade-offs
- How to choose

**Event Queue**
- Per-instance queues
- Capacity limits
- Priority handling

**History Slots**
- What they store
- Stable allocation

#### 5. Integration Patterns

**ECS Integration**
- Unmanaged components
- Batched updates
- Context passing

**Game Loop Integration**
- When to call Update()
- Delta time handling
- Frame-independent logic

#### 6. Determinism

**Why it matters**
- Multiplayer
- Replays
- Testing

**How FastHSM ensures it**
- Fixed ordering
- Explicit RNG
- No floating-point time

---

### Task 6: Performance Guide

**Create:** `docs/user/PERFORMANCE.md`

#### 1. Performance Characteristics

**What's Fast**
- Batched updates
- Flat state machines
- Guards (cheap)
- Event processing

**What's Expensive**
- Deep hierarchies (>5 levels)
- Frequent transitions
- Large action dispatch
- Trace logging (Tier 3)

#### 2. Best Practices

**State Machine Design**
- Keep hierarchies shallow
- Minimize transitions per tick
- Use activities for continuous behavior
- Batch related logic

**Action Optimization**
- Keep actions small
- Avoid allocations
- Cache computations in context
- Use unsafe code judiciously

**Event Management**
- Choose appropriate queue size
- Use priorities wisely
- Avoid event spam
- Defer expensive events

**Instance Sizing**
- Start small (64B)
- Profile before upgrading
- Don't over-allocate

#### 3. Batching

**UpdateBatch API**
- Process multiple instances
- Shared context
- Cache benefits
- How to structure

**Code Example:**
```csharp
Span<HsmInstance64> instances = stackalloc HsmInstance64[100];
var sharedContext = new GameContext { /* ... */ };
HsmKernel.UpdateBatch(blob, instances, sharedContext, deltaTime);
```

#### 4. Profiling

**What to measure**
- Update time per instance
- Transitions per tick
- Event queue depth
- Trace buffer growth

**Tools**
- BenchmarkDotNet examples
- Memory profiling
- Trace analysis

#### 5. Common Pitfalls

**Allocations**
- Capturing in delegates → Use static methods
- Boxing in actions → Use void* correctly
- String concatenation → Use interpolation

**Cache Misses**
- Random instance access → Use batches
- Large context structs → Keep small
- Indirect data → Use indices

**Logic Errors**
- Infinite RTC loops → Validator catches
- Event floods → Set appropriate limits
- History bugs → Test thoroughly

---

### Task 7: Migration Guide

**Create:** `docs/user/MIGRATION.md`

(For future use, prepare structure now)

#### v1.0 → v2.0 (Future)

**Breaking Changes**
- List any breaking API changes
- Rationale for each change
- Migration path

**Deprecated Features**
- What's deprecated
- Alternative approaches
- Timeline for removal

**New Features**
- What's new
- How to adopt
- Benefits

**Code Examples**
- Before/after comparisons
- Migration checklist

---

## 📊 Success Criteria

- [ ] TASK-E02 completed (Documentation)
- [ ] README.md created (project root)
- [ ] Getting Started guide (step-by-step tutorial)
- [ ] API Reference (all public APIs documented)
- [ ] Examples guide (traffic light walkthrough)
- [ ] Architecture overview (high-level design)
- [ ] Performance guide (best practices)
- [ ] Migration guide (structure for future)
- [ ] All code examples tested and working
- [ ] Clear, professional writing
- [ ] Diagrams for complex concepts
- [ ] Cross-references between documents

---

## ⚠️ Common Pitfalls

1. **Too Technical:** Remember the audience (users, not implementers)
2. **Missing Examples:** Every concept needs a code example
3. **Outdated Code:** All examples must compile and run
4. **Broken Links:** Test all internal document links
5. **Unclear Structure:** Use clear headings and navigation
6. **No Diagrams:** Complex concepts need visuals
7. **Copy-Paste Errors:** Test all code snippets

---

## 📚 Reference

- **Task:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) - TASK-E02
- **Existing Example:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs`
- **Design Docs:** `docs/design/` (for technical details)
- **Style Guide:** Keep it clear, concise, code-first

---

## 📝 Documentation Standards

### Writing Style

- **Clear:** Use simple language
- **Concise:** Get to the point quickly
- **Code-First:** Show, don't just tell
- **Consistent:** Use same terminology throughout
- **Accessible:** Explain jargon on first use

### Code Examples

- **Complete:** Must compile and run
- **Realistic:** Show real-world patterns
- **Commented:** Explain non-obvious parts
- **Tested:** Actually execute the code

### Structure

- **Hierarchical:** Clear heading levels
- **Navigable:** Table of contents for long docs
- **Linked:** Cross-reference related sections
- **Scannable:** Use lists, tables, code blocks

---

**This makes FastHSM accessible to all developers!** 📖

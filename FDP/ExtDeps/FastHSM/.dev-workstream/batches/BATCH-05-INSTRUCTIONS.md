# BATCH-05: Compiler - Graph Builder & Parser

**Effort:** 3-4 days  
**Phase:** Phase 2 - Compiler (START)

---

## Context

Phase 1 (Data Layer) complete. Now build the **Compiler** that transforms user-defined state machines into `HsmDefinitionBlob`.

**Compiler Pipeline:**
```
User API → Graph → Normalizer → Validator → Flattener → Blob Emitter
```

This batch: **Graph Builder** + initial data structures.

---

## Architecture Overview

The compiler uses an **intermediate graph representation** before flattening:

1. **Builder API** - Fluent C# API for defining machines
2. **Graph Nodes** - In-memory tree structure (StateNode, TransitionNode)
3. **Normalization** - Resolve implicit states, assign IDs
4. **Validation** - Check rules, detect errors
5. **Flattening** - Convert tree → flat arrays
6. **Emission** - Serialize to HsmDefinitionBlob

**This batch:** Tasks 1-2 (Builder API + Graph Nodes).

---

## Task 1: Graph Node Structures

**Folder:** `src/Fhsm.Compiler/Graph/` (NEW)  
**Files:** `StateNode.cs`, `TransitionNode.cs`, `RegionNode.cs`

Create internal graph representation. These are mutable builder objects.

### StateNode.cs

```csharp
using System;
using System.Collections.Generic;

namespace Fhsm.Compiler.Graph
{
    /// <summary>
    /// Intermediate representation of a state during compilation.
    /// Mutable graph node before flattening.
    /// </summary>
    internal class StateNode
    {
        public Guid StableId { get; set; }  // For hot reload stability
        public string Name { get; set; }
        public StateNode? Parent { get; set; }
        
        public List<StateNode> Children { get; } = new();
        public List<TransitionNode> Transitions { get; } = new();
        public List<RegionNode> Regions { get; } = new();
        
        // State configuration
        public bool IsInitial { get; set; }
        public bool IsHistory { get; set; }
        public bool IsDeepHistory { get; set; }
        public bool IsParallel { get; set; }
        
        // Actions (function names - resolved later)
        public string? OnEntryAction { get; set; }
        public string? OnExitAction { get; set; }
        public string? ActivityAction { get; set; }
        public string? TimerAction { get; set; }
        
        // Computed during flattening
        public ushort FlatIndex { get; set; } = 0xFFFF;
        public byte Depth { get; set; }
        
        public StateNode(string name, Guid? stableId = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            StableId = stableId ?? Guid.NewGuid();
        }
        
        public void AddChild(StateNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            child.Parent = this;
            Children.Add(child);
        }
        
        public void AddTransition(TransitionNode transition)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));
            Transitions.Add(transition);
        }
    }
}
```

### TransitionNode.cs

```csharp
namespace Fhsm.Compiler.Graph
{
    internal class TransitionNode
    {
        public StateNode Source { get; set; }
        public StateNode Target { get; set; }
        
        public ushort EventId { get; set; }
        public string? GuardFunction { get; set; }  // Optional guard
        public string? ActionFunction { get; set; }  // Optional action
        
        public byte Priority { get; set; } = 128;  // Default normal
        public bool IsInternal { get; set; }  // Internal vs External
        
        public TransitionNode(StateNode source, StateNode target, ushort eventId)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            EventId = eventId;
        }
    }
}
```

### RegionNode.cs

```csharp
namespace Fhsm.Compiler.Graph
{
    internal class RegionNode
    {
        public string Name { get; set; }
        public StateNode InitialState { get; set; }
        
        public RegionNode(string name, StateNode initial)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            InitialState = initial ?? throw new ArgumentNullException(nameof(initial));
        }
    }
}
```

---

## Task 2: State Machine Graph Container

**File:** `src/Fhsm.Compiler/Graph/StateMachineGraph.cs`

Root container for the graph.

```csharp
using System;
using System.Collections.Generic;

namespace Fhsm.Compiler.Graph
{
    /// <summary>
    /// Root container for state machine graph before compilation.
    /// </summary>
    internal class StateMachineGraph
    {
        public string Name { get; set; }
        public Guid MachineId { get; set; }
        
        public StateNode RootState { get; set; }
        public Dictionary<string, StateNode> States { get; } = new();
        public List<TransitionNode> GlobalTransitions { get; } = new();
        
        // Event definitions
        public Dictionary<string, ushort> EventNameToId { get; } = new();
        
        // Function registrations
        public HashSet<string> RegisteredActions { get; } = new();
        public HashSet<string> RegisteredGuards { get; } = new();
        
        public StateMachineGraph(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MachineId = Guid.NewGuid();
            
            // Create implicit root
            RootState = new StateNode("__Root");
            States["__Root"] = RootState;
        }
        
        public void AddState(StateNode state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (States.ContainsKey(state.Name))
                throw new InvalidOperationException($"State '{state.Name}' already exists");
            
            States[state.Name] = state;
        }
        
        public StateNode? FindState(string name)
        {
            return States.TryGetValue(name, out var state) ? state : null;
        }
    }
}
```

---

## Task 3: Fluent Builder API

**File:** `src/Fhsm.Compiler/HsmBuilder.cs`

Public API for users to define machines.

```csharp
using System;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler
{
    /// <summary>
    /// Fluent API for building state machines.
    /// </summary>
    public class HsmBuilder
    {
        private readonly StateMachineGraph _graph;
        
        public HsmBuilder(string machineName)
        {
            _graph = new StateMachineGraph(machineName);
        }
        
        public StateBuilder State(string name)
        {
            var state = new StateNode(name);
            _graph.AddState(state);
            _graph.RootState.AddChild(state);  // Top-level states are children of root
            
            return new StateBuilder(state, _graph);
        }
        
        public HsmBuilder Event(string eventName, ushort eventId)
        {
            _graph.EventNameToId[eventName] = eventId;
            return this;
        }
        
        public HsmBuilder RegisterAction(string functionName)
        {
            _graph.RegisteredActions.Add(functionName);
            return this;
        }
        
        public HsmBuilder RegisterGuard(string functionName)
        {
            _graph.RegisteredGuards.Add(functionName);
            return this;
        }
        
        // Internal: Get graph for compiler
        internal StateMachineGraph GetGraph() => _graph;
    }
    
    /// <summary>
    /// Builder for configuring a single state.
    /// </summary>
    public class StateBuilder
    {
        private readonly StateNode _state;
        private readonly StateMachineGraph _graph;
        
        internal StateBuilder(StateNode state, StateMachineGraph graph)
        {
            _state = state;
            _graph = graph;
        }
        
        public StateBuilder OnEntry(string actionName)
        {
            _state.OnEntryAction = actionName;
            return this;
        }
        
        public StateBuilder OnExit(string actionName)
        {
            _state.OnExitAction = actionName;
            return this;
        }
        
        public StateBuilder Activity(string actionName)
        {
            _state.ActivityAction = actionName;
            return this;
        }
        
        public StateBuilder Initial()
        {
            _state.IsInitial = true;
            return this;
        }
        
        public StateBuilder History()
        {
            _state.IsHistory = true;
            return this;
        }
        
        public StateBuilder Child(string childName, Action<StateBuilder> configure)
        {
            var child = new StateNode(childName);
            _state.AddChild(child);
            _graph.AddState(child);
            
            var childBuilder = new StateBuilder(child, _graph);
            configure?.Invoke(childBuilder);
            
            return this;
        }
        
        public TransitionBuilder On(string eventName)
        {
            if (!_graph.EventNameToId.TryGetValue(eventName, out ushort eventId))
                throw new InvalidOperationException($"Event '{eventName}' not registered");
            
            return new TransitionBuilder(_state, eventId, _graph);
        }
    }
    
    /// <summary>
    /// Builder for configuring a transition.
    /// </summary>
    public class TransitionBuilder
    {
        private readonly StateNode _source;
        private readonly ushort _eventId;
        private readonly StateMachineGraph _graph;
        private readonly TransitionNode _transition;
        
        internal TransitionBuilder(StateNode source, ushort eventId, StateMachineGraph graph)
        {
            _source = source;
            _eventId = eventId;
            _graph = graph;
            _transition = new TransitionNode(source, null!, eventId);  // Target set later
        }
        
        public TransitionBuilder GoTo(string targetStateName)
        {
            var target = _graph.FindState(targetStateName);
            if (target == null)
                throw new InvalidOperationException($"Target state '{targetStateName}' not found");
            
            _transition.Target = target;
            _source.AddTransition(_transition);
            return this;
        }
        
        public TransitionBuilder Guard(string guardName)
        {
            _transition.GuardFunction = guardName;
            return this;
        }
        
        public TransitionBuilder Action(string actionName)
        {
            _transition.ActionFunction = actionName;
            return this;
        }
        
        public TransitionBuilder Priority(byte priority)
        {
            _transition.Priority = priority;
            return this;
        }
    }
}
```

---

## Task 4: Tests

**File:** `tests/Fhsm.Tests/Compiler/BuilderTests.cs`

Test the builder API creates correct graph structures.

**Minimum 15 tests:**
- Builder creates graph
- State() adds state to graph
- Event() registers event ID
- OnEntry/OnExit/Activity set actions
- Child() creates hierarchy
- On().GoTo() creates transition
- Guard/Action configure transition
- Multiple children work
- Multiple transitions work
- FindState() lookup works
- Duplicate state names throw
- Unknown event throws
- Unknown target state throws
- Initial flag sets correctly
- History flag sets correctly

---

## Implementation Notes

### Design Decisions

**Why intermediate graph?**  
Easier to validate, normalize, and transform than working directly with flat arrays. Compiler passes can mutate the graph.

**Why Guid StableId?**  
Hot reload: if user renames "Idle" → "Standing", Guid stays same, blob indices stable.

**Why string function names?**  
Source generation will resolve these to function pointers later. Compiler just tracks names.

**Why separate builders?**  
Fluent API is ergonomic: `State("Idle").OnEntry("Init").On("Start").GoTo("Run")`

### Graph Validation (Next Batch)

After building, next batch will validate:
- No orphan states
- Initial states exist
- No circular parent chains
- Transitions target valid states
- Function names registered

---

## Project Setup

Create `src/Fhsm.Compiler/Fhsm.Compiler.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Fhsm.Kernel\Fhsm.Kernel.csproj" />
  </ItemGroup>
</Project>
```

Add to solution:
```
dotnet sln add src/Fhsm.Compiler/Fhsm.Compiler.csproj
```

Update test project to reference Compiler:
```xml
<ItemGroup>
  <ProjectReference Include="..\..\src/Fhsm.Compiler/Fhsm.Compiler.csproj" />
</ItemGroup>
```

---

## Success Criteria

- [ ] Fhsm.Compiler project created and builds
- [ ] Graph nodes (StateNode, TransitionNode, RegionNode) implemented
- [ ] StateMachineGraph container works
- [ ] HsmBuilder fluent API functional
- [ ] 15+ tests, all passing
- [ ] Can build simple 3-state machine via API
- [ ] Report submitted

---

## Reference

- `docs/design/HSM-Implementation-Design.md` Section 2 (Compiler)
- `docs/btree-design-inspiration/README.md` - See "Builder Pattern"

**Report to:** `.dev-workstream/reports/BATCH-05-REPORT.md`

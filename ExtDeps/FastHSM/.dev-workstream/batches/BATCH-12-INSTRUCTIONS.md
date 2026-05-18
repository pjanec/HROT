# BATCH-12: Complete Example & Integration

**Batch Number:** BATCH-12  
**Tasks:** TASK-K07 (Activity Execution), TASK-E01 (Console Example)  
**Phase:** Phase 3 - Kernel + Examples  
**Estimated Effort:** 5-7 days  
**Priority:** HIGH  
**Dependencies:** BATCH-11

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to **BATCH-12**! This batch completes the kernel with activity execution and creates a **working end-to-end example** demonstrating the entire HSM system.

This is the **integration milestone** - everything comes together!

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `.dev-workstream/TASK-DEFINITIONS.md` - See TASK-K07, TASK-E01
3. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Full document review
4. **Previous Reviews:** All BATCH-08 through BATCH-11 reviews

### Source Code Location

- **Kernel Update:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)
- **Example Project:** `src/Fhsm.Examples.Console/` (UPDATE existing project)
- **Test Project:** `tests/Fhsm.Tests/Examples/` (NEW)

### Questions File

`.dev-workstream/questions/BATCH-12-QUESTIONS.md`

---

## Context

**Source generation complete (BATCH-11).** Actions and guards dispatch correctly.

**This batch:**
1. **Activity Execution** - Execute per-state activities during Activity phase
2. **Console Example** - Full working state machine (traffic light or similar)
3. **Integration Test** - End-to-end test proving the entire system works

**Related Tasks:**
- [TASK-K07](../TASK-DEFINITIONS.md#task-k07-activity-execution) - Activity Execution
- [TASK-E01](../TASK-DEFINITIONS.md#task-e01-console-example) - Console Example

---

## 🎯 Batch Objectives

Complete the kernel and demonstrate it works:
- Activities execute during Activity phase
- Full example: compiler → runtime → actions → output
- Prove the entire design is functional

---

## ✅ Tasks

### Task 1: Activity Execution (TASK-K07)

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Replace Activity phase stub with real execution.

#### Activity Phase Logic

```csharp
private static void ProcessActivityPhase(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    void* contextPtr,
    float deltaTime)
{
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
    
    // Execute activities for all active states
    for (int r = 0; r < regionCount; r++)
    {
        ushort leafId = activeLeafIds[r];
        if (leafId == 0xFFFF) continue;
        
        // Walk up from leaf to root, executing activities
        ushort current = leafId;
        while (current != 0xFFFF)
        {
            ref readonly var state = ref definition.GetState(current);
            
            // Execute activity if present
            if (state.ActivityActionId != 0 && state.ActivityActionId != 0xFFFF)
            {
                ExecuteAction(state.ActivityActionId, instancePtr, contextPtr, 0);
            }
            
            current = state.ParentIndex;
        }
    }
    
    // Return to Idle
    header->Phase = InstancePhase.Idle;
}
```

**Update ProcessInstancePhase:**

```csharp
case InstancePhase.Activity:
    ProcessActivityPhase(definition, instancePtr, instanceSize, contextPtr, deltaTime);
    break;
```

---

### Task 2: Console Example Project Setup

**Update:** `src/Fhsm.Examples.Console/Fhsm.Examples.Console.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Fhsm.Kernel\Fhsm.Kernel.csproj" />
    <ProjectReference Include="..\Fhsm.Compiler\Fhsm.Compiler.csproj" />
    <ProjectReference Include="..\Fhsm.SourceGen\Fhsm.SourceGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

---

### Task 3: Traffic Light Example

**Create:** `src/Fhsm.Examples.Console/TrafficLightExample.cs`

```csharp
using System;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

namespace Fhsm.Examples.Console
{
    /// <summary>
    /// Simple traffic light state machine example.
    /// 
    /// States: Red → Green → Yellow → Red
    /// Events: TimerExpired
    /// </summary>
    public static unsafe class TrafficLightExample
    {
        // Event IDs
        private const ushort TimerExpiredEvent = 1;
        
        // Context data
        private struct TrafficLightContext
        {
            public int TickCount;
        }
        
        // Actions
        [HsmAction(Name = "OnEnterRed")]
        public static void OnEnterRed(void* instance, void* context, ushort eventId)
        {
            System.Console.WriteLine("🔴 RED - Stop!");
        }
        
        [HsmAction(Name = "OnEnterGreen")]
        public static void OnEnterGreen(void* instance, void* context, ushort eventId)
        {
            System.Console.WriteLine("🟢 GREEN - Go!");
        }
        
        [HsmAction(Name = "OnEnterYellow")]
        public static void OnEnterYellow(void* instance, void* context, ushort eventId)
        {
            System.Console.WriteLine("🟡 YELLOW - Caution!");
        }
        
        [HsmAction(Name = "OnExitRed")]
        public static void OnExitRed(void* instance, void* context, ushort eventId)
        {
            System.Console.WriteLine("  Exiting Red state...");
        }
        
        // Activity (runs every tick while in state)
        [HsmAction(Name = "RedActivity")]
        public static void RedActivity(void* instance, void* context, ushort eventId)
        {
            var ctx = (TrafficLightContext*)context;
            System.Console.WriteLine($"  [Red Activity - Tick {ctx->TickCount}]");
        }
        
        public static void Run()
        {
            System.Console.WriteLine("=== Traffic Light State Machine ===\n");
            
            // 1. Build state machine
            var builder = new HsmBuilder("TrafficLight");
            
            // Define states
            var red = builder.State("Red")
                .OnEntry("OnEnterRed")
                .OnExit("OnExitRed")
                .Activity("RedActivity");
            
            var green = builder.State("Green")
                .OnEntry("OnEnterGreen");
            
            var yellow = builder.State("Yellow")
                .OnEntry("OnEnterYellow");
            
            // Define transitions
            red.On(TimerExpiredEvent).GoTo(green);
            green.On(TimerExpiredEvent).GoTo(yellow);
            yellow.On(TimerExpiredEvent).GoTo(red);
            
            // Set initial state
            builder.InitialState(red);
            
            // 2. Compile to blob
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            
            if (!HsmGraphValidator.Validate(graph, out var errors))
            {
                System.Console.WriteLine($"Validation failed: {string.Join(", ", errors)}");
                return;
            }
            
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            System.Console.WriteLine($"Compiled: {blob.Header.StateCount} states, {blob.Header.TransitionCount} transitions\n");
            
            // 3. Create instance
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            System.Console.WriteLine($"Instance initialized (Tier: 64B)\n");
            
            // 4. Run simulation
            var context = new TrafficLightContext { TickCount = 0 };
            
            // Trigger to start
            HsmKernel.Trigger(ref instance);
            
            for (int i = 0; i < 10; i++)
            {
                context.TickCount = i;
                
                System.Console.WriteLine($"\n--- Tick {i} ---");
                
                // Fire timer event every 3 ticks
                if (i % 3 == 2)
                {
                    System.Console.WriteLine("⏰ Timer expired!");
                    var evt = new HsmEvent
                    {
                        EventId = TimerExpiredEvent,
                        Priority = EventPriority.Normal
                    };
                    
                    HsmEventQueue.TryEnqueue(&instance, 64, evt);
                }
                
                // Update
                HsmKernel.Update(blob, ref instance, context, 0.016f);
                
                System.Threading.Thread.Sleep(500); // Visual delay
            }
            
            System.Console.WriteLine("\n=== Simulation Complete ===");
        }
    }
}
```

---

### Task 4: Program Entry Point

**Update:** `src/Fhsm.Examples.Console/Program.cs`

```csharp
namespace Fhsm.Examples.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("FastHSM Examples\n");
            
            TrafficLightExample.Run();
            
            System.Console.WriteLine("\nPress any key to exit...");
            System.Console.ReadKey();
        }
    }
}
```

---

### Task 5: Integration Test

**Create:** `tests/Fhsm.Tests/Examples/IntegrationTests.cs`

```csharp
using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Fhsm.Compiler;

namespace Fhsm.Tests.Examples
{
    public unsafe class IntegrationTests
    {
        private static int _entryCount = 0;
        private static int _exitCount = 0;
        private static int _activityCount = 0;
        private static int _transitionCount = 0;
        
        [HsmAction(Name = "TestEntry")]
        public static void TestEntry(void* instance, void* context, ushort eventId)
        {
            _entryCount++;
        }
        
        [HsmAction(Name = "TestExit")]
        public static void TestExit(void* instance, void* context, ushort eventId)
        {
            _exitCount++;
        }
        
        [HsmAction(Name = "TestActivity")]
        public static void TestActivity(void* instance, void* context, ushort eventId)
        {
            _activityCount++;
        }
        
        [HsmAction(Name = "TestTransition")]
        public static void TestTransition(void* instance, void* context, ushort eventId)
        {
            _transitionCount++;
        }
        
        [Fact]
        public void End_To_End_State_Machine_Works()
        {
            // Reset counters
            _entryCount = 0;
            _exitCount = 0;
            _activityCount = 0;
            _transitionCount = 0;
            
            // Build
            var builder = new HsmBuilder("TestMachine");
            var stateA = builder.State("A")
                .OnEntry("TestEntry")
                .OnExit("TestExit")
                .Activity("TestActivity");
            
            var stateB = builder.State("B")
                .OnEntry("TestEntry");
            
            stateA.On(1).GoTo(stateB).Do("TestTransition");
            builder.InitialState(stateA);
            
            // Compile
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            Assert.True(HsmGraphValidator.Validate(graph, out _));
            
            var flattened = HsmFlattener.Flatten(graph);
            var blob = HsmEmitter.Emit(flattened);
            
            // Create instance
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);
            
            // Trigger
            HsmKernel.Trigger(ref instance);
            
            int context = 0;
            
            // First update: Entry to A
            HsmKernel.Update(blob, ref instance, context, 0.016f);
            Assert.Equal(1, _entryCount); // OnEnter A
            
            // Activity should run
            HsmKernel.Update(blob, ref instance, context, 0.016f);
            Assert.Equal(1, _activityCount); // Activity A
            
            // Fire event
            var evt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);
            
            // Update: Transition A → B
            HsmKernel.Update(blob, ref instance, context, 0.016f);
            Assert.Equal(1, _exitCount); // OnExit A
            Assert.Equal(1, _transitionCount); // Transition action
            Assert.Equal(2, _entryCount); // OnEnter B
            
            // Activity should not run (B has no activity)
            HsmKernel.Update(blob, ref instance, context, 0.016f);
            Assert.Equal(1, _activityCount); // Still 1 (from A)
        }
    }
}
```

---

## 🧪 Testing Requirements

**Minimum 8 tests:**

### Activity Tests (3)
1. Activity executes during Activity phase
2. Multiple active states execute activities
3. Activity not executed if state has no activity

### Integration Tests (5)
4. End-to-end state machine (build → compile → run)
5. Transition with entry/exit/transition actions
6. Activity execution in active state
7. Event handling triggers transitions
8. Multiple state changes work correctly

---

## 📊 Success Criteria

- [ ] TASK-K07 completed (Activity execution)
- [ ] TASK-E01 completed (Console example runs)
- [ ] Traffic light example prints correct output
- [ ] Integration test passes (end-to-end proof)
- [ ] 8+ tests, all passing
- [ ] 169+ total tests passing
- [ ] Console app runs without errors
- [ ] Output demonstrates state changes

---

## ⚠️ Expected Output

When running the console example, you should see:

```
=== Traffic Light State Machine ===

Compiled: 3 states, 3 transitions

Instance initialized (Tier: 64B)

--- Tick 0 ---
🔴 RED - Stop!
  [Red Activity - Tick 0]

--- Tick 1 ---
  [Red Activity - Tick 1]

--- Tick 2 ---
⏰ Timer expired!
  Exiting Red state...
🟢 GREEN - Go!

--- Tick 3 ---

--- Tick 4 ---

--- Tick 5 ---
⏰ Timer expired!
🟡 YELLOW - Caution!

...
```

---

## 📚 Reference

- **Tasks:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) - TASK-K07, TASK-E01
- **Design:** `docs/design/HSM-Implementation-Design.md` - Full document
- **All Reviews:** BATCH-08 through BATCH-11

---

**This is the integration milestone - everything must work together!** 🚀

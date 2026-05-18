# Gap Tasks: Implementation Roadmap

**Date:** 2026-01-12  
**Based On:** `.dev-workstream/GAP-ANALYSIS.md`  
**Priority:** P0 → P1 → P2 → P3

---

## P0 - Critical Tasks (Blocks Core Functionality)

### TASK-G01: Global Transition Checking
**Priority:** P0  
**Gap:** 3.2 in GAP-ANALYSIS.md  
**Design Ref:** Section 3.4 Step A

**Description:**
Implement global transition checking in `HsmKernelCore.SelectTransition`. Global transitions (e.g., Death, Stun interrupts) must be checked **before** per-state transitions, as per Architect Decision Q7.

**Scope:**
1. Modify `SelectTransition` to check `definition.GlobalTransitions` first
2. If global transition enabled (guard passes), return immediately with priority 255
3. Only check per-state transitions if no global transition found

**Files to Modify:**
- `src/Fhsm.Kernel/HsmKernelCore.cs` (add global transition loop before Step B)

**Tests:**
- Add test: "Global transition preempts local transition"
- Add test: "Global transition with guard check"

**Design Code Reference:**
```csharp
// Step A: Check global interrupts
var globalTransitions = definition.GlobalTransitions;
foreach (ref readonly var trans in globalTransitions)
{
    if (trans.TriggerEventId == evt.EventId)
    {
        if (EvaluateGuard(definition, trans.GuardId, instancePtr, contextPtr))
        {
            candidate = new TransitionCandidate
            {
                TransitionIndex = trans.Index,
                Priority = 255, // Highest
            };
            return true;
        }
    }
}
```

---

### TASK-G02: Command Buffer Integration
**Priority:** P0  
**Gap:** 3.1 in GAP-ANALYSIS.md  
**Design Ref:** Sections 3.1-3.6, Directive 1

**Description:**
Integrate command buffer into action/guard signatures. Actions must write side effects to a command buffer (instead of directly modifying external state), enabling deterministic replay and deferred execution.

**Scope:**
1. Change action signature from `(void* instance, void* context, ushort eventId)` to `(void* instance, void* context, void* commandsPtr)`
2. Update `HsmKernelCore` to create/pass `HsmCommandWriter` through all phases
3. Update `HsmActionDispatcher` signature
4. Update source generator to emit new signature
5. Update all example actions (Traffic Light, Visual Demo)

**Files to Modify:**
- `src/Fhsm.Kernel/HsmKernelCore.cs` (create command writer, pass to action invoke)
- `src/Fhsm.SourceGen/HsmActionGenerator.cs` (change signature)
- `examples/Fhsm.Examples.Console/TrafficLightExample.cs` (update actions)
- `demos/Fhsm.Demo.Visual/Actions.cs` (update actions)

**Tests:**
- Add test: "Action writes command to buffer"
- Add test: "Guard reads but doesn't write"
- Add test: "Commands persisted after action execution"

**Design Code Reference:**
```csharp
// In HsmKernelCore:
var cmdWriter = new HsmCommandWriter(commandPage, capacity: 4080);
InvokeAction(definition, actionId, instancePtr, contextPtr, ref cmdWriter);

// Action signature:
[UnmanagedCallersOnly]
public static void MyAction(void* instance, void* context, void* commandsPtr)
{
    ref HsmCommandWriter writer = ref Unsafe.AsRef<HsmCommandWriter>(commandsPtr);
    writer.TryWriteCommand(...);
}
```

---

### TASK-G03: Hot Reload Manager
**Priority:** P0  
**Gap:** 4.1 in GAP-ANALYSIS.md  
**Design Ref:** Section 4.1

**Description:**
Implement hot reload system for live reloading of HSM definitions. Supports both soft reload (parameter-only changes) and hard reset (structural changes), as per hash-based versioning design.

**Scope:**
1. Create `HotReloadManager` class
2. Implement `TryReload` method (compare hashes, decide soft/hard)
3. Implement `HardReset` (increment generation, clear state, reinitialize)
4. Integrate with `HsmKernelCore` (optional auto-detect in `UpdateBatchCore`)

**Files to Create:**
- `src/Fhsm.Kernel/HotReloadManager.cs`

**Files to Modify:**
- `src/Fhsm.Kernel/HsmKernelCore.cs` (optional: auto-detect hash mismatch → hard reset)

**Tests:**
- Add test: "Soft reload preserves instance state"
- Add test: "Hard reset clears active states"
- Add test: "Generation increments on hard reset"
- Add test: "No reload if hashes match"

**Design Code Reference:**
```csharp
public class HotReloadManager
{
    private readonly Dictionary<uint, HsmDefinitionBlob> _loadedBlobs = new();
    
    public ReloadResult TryReload(
        uint machineId,
        HsmDefinitionBlob newBlob,
        Span<HsmInstance128> instances)
    {
        if (!_loadedBlobs.TryGetValue(machineId, out var oldBlob))
            return ReloadResult.NewMachine;
        
        bool structureChanged = newBlob.Header.StructureHash != oldBlob.Header.StructureHash;
        bool parameterChanged = newBlob.Header.ParameterHash != oldBlob.Header.ParameterHash;
        
        if (!structureChanged && !parameterChanged)
            return ReloadResult.NoChange;
        
        if (structureChanged)
        {
            // Hard reload
            foreach (ref var instance in instances)
            {
                if (instance.Header.MachineId == machineId)
                    HardReset(ref instance, newBlob);
            }
            _loadedBlobs[machineId] = newBlob;
            return ReloadResult.HardReset;
        }
        else
        {
            // Soft reload
            _loadedBlobs[machineId] = newBlob;
            return ReloadResult.SoftReload;
        }
    }
    
    private unsafe void HardReset(ref HsmInstance128 instance, HsmDefinitionBlob newBlob) { /* ... */ }
}
```

---

## P1 - High Priority Tasks

### TASK-G04: RNG Wrapper with Debug Tracking
**Priority:** P1  
**Gap:** 1.2 in GAP-ANALYSIS.md  
**Design Ref:** Section 1.6, Directive 3

**Description:**
Implement `HsmRng` ref struct for deterministic random number generation. Must include debug-only access tracking for replay validation, as mandated by Architect Directive 3.

**Scope:**
1. Create `HsmRng` ref struct with XorShift32 implementation
2. Add `NextFloat()`, `NextInt()`, `NextBool()` methods
3. Add `#if DEBUG` access counting
4. Update source generator to inject RNG tracking for guards marked `[HsmGuard(UsesRNG=true)]`
5. Add helper method to create RNG from instance: `HsmRng.FromInstance(instance)`

**Files to Create:**
- `src/Fhsm.Kernel/HsmRng.cs`

**Files to Modify:**
- `src/Fhsm.SourceGen/HsmActionGenerator.cs` (check `UsesRNG` flag, emit tracking code)
- `src/Fhsm.Kernel/HsmKernelCore.cs` (pass RNG to guards if needed)

**Tests:**
- Add test: "RNG produces deterministic sequence"
- Add test: "Seed advances on each call"
- Add test: "Debug tracking increments access count"
- Add test: "Two instances with same seed produce same values"

**Design Code Reference:**
```csharp
public unsafe ref struct HsmRng
{
    private uint* _seedPtr;
    #if DEBUG
    private int* _debugAccessCount;
    #endif
    
    public HsmRng(uint* seedPtr)
    {
        _seedPtr = seedPtr;
        #if DEBUG
        _debugAccessCount = null;
        #endif
    }
    
    public float NextFloat()
    {
        uint x = *_seedPtr;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        *_seedPtr = x;
        
        #if DEBUG
        if (_debugAccessCount != null)
            (*_debugAccessCount)++;
        #endif
        
        return (x >> 8) * (1.0f / 16777216.0f);
    }
}
```

---

### TASK-G05: Timer Cancellation on Exit
**Priority:** P1  
**Gap:** 3.3 in GAP-ANALYSIS.md  
**Design Ref:** Section 3.5 - ExecuteExitPath

**Description:**
Implement timer cancellation when exiting a state. Prevents "orphaned" timers from firing after their owning state has exited.

**Scope:**
1. Add `CancelTimers(byte* instancePtr, ushort stateId)` method to `HsmKernelCore`
2. Call `CancelTimers` in `ExecuteExitPath` for each exited state
3. Clear timer deadline (set to 0) for any timer owned by the exited state

**Files to Modify:**
- `src/Fhsm.Kernel/HsmKernelCore.cs` (add `CancelTimers` method, call in `ExecuteExitPath`)

**Tests:**
- Add test: "Timer cancelled when state exits"
- Add test: "Timer fires if state still active"
- Add test: "Multiple timers cancelled on deep exit"

**Design Code Reference:**
```csharp
private static unsafe void ExecuteExitPath(...)
{
    for (int i = 0; i < pathLen; i++)
    {
        ref readonly var state = ref definition.States[path[i]];
        
        // Cancel timers owned by this state
        CancelTimers(instancePtr, path[i]);
        
        // ... (rest of exit logic)
    }
}

private static void CancelTimers(byte* instancePtr, ushort stateId)
{
    uint* timers = GetTimerArray(instancePtr, instanceSize, out int count);
    for (int i = 0; i < count; i++)
    {
        // Clear timer (could check if it "belongs" to this state, but simpler to clear all)
        timers[i] = 0;
    }
}
```

---

### TASK-G06: Deferred Queue Merge
**Priority:** P1  
**Gap:** 3.4 in GAP-ANALYSIS.md  
**Design Ref:** Section 3.3 - RTC Loop

**Description:**
Implement deferred event queue merging at RTC boundaries. Events marked `IsDeferred` should be moved from deferred queue to active queue after transitions complete.

**Scope:**
1. Add `MergeDeferredQueue(byte* instancePtr, int instanceSize)` method to `HsmKernelCore`
2. Call `MergeDeferredQueue` at end of RTC loop (after all microsteps)
3. Move events from `DeferredTail` → `ActiveTail`

**Files to Modify:**
- `src/Fhsm.Kernel/HsmKernelCore.cs` (add `MergeDeferredQueue` method, call in `ProcessInstancePhase`)

**Tests:**
- Add test: "Deferred event moves to active queue"
- Add test: "Deferred event processed in next RTC cycle"
- Add test: "Multiple deferred events merged correctly"

**Design Code Reference:**
```csharp
private static void ProcessInstancePhase(...)
{
    switch (header->Phase)
    {
        case InstancePhase.RTC:
            // ... (RTC loop) ...
            microstepCount++;
            
            // Merge deferred queue at RTC boundary
            MergeDeferredQueue(instancePtr, instanceSize);
            break;
    }
}

private static void MergeDeferredQueue(byte* instancePtr, int instanceSize)
{
    // Move events from deferred tail to active tail
    // Preserve priority ordering
}
```

---

### TASK-G07: Tier Budget Validation
**Priority:** P1  
**Gap:** 2.2 in GAP-ANALYSIS.md  
**Design Ref:** Section 2.5 - CheckTierBudget

**Description:**
Implement tier budget validation in compiler. Calculate required instance size based on state count, region count, timer/history slots. Auto-promote tier if needed (or error in strict mode).

**Scope:**
1. Add `CheckTierBudget` method to `HsmGraphValidator`
2. Compute required size: `baseSize + (maxRegions * 2) + (timerSlots * 4) + (historySlots * 2) + eventQueueSize`
3. Compare against tier budget (64/128/256)
4. Auto-promote or emit error/warning

**Files to Modify:**
- `src/Fhsm.Compiler/HsmGraphValidator.cs` (add `CheckTierBudget` method)

**Tests:**
- Add test: "Simple machine fits in Tier 1"
- Add test: "Complex machine auto-promotes to Tier 2"
- Add test: "Strict mode errors on over-budget"

**Design Code Reference:**
```csharp
private void CheckTierBudget(StateMachineGraph graph)
{
    int maxRegions = 0;
    int timerSlots = 0;
    int historySlots = 0;
    
    foreach (var state in graph.AllStates)
    {
        maxRegions = Math.Max(maxRegions, state.Regions.Count);
        if (state.IsHistory) historySlots++;
        if (state.TimerAction != null) timerSlots++;
    }
    
    int required = ComputeInstanceSize(maxRegions, timerSlots, historySlots);
    
    if (required > selectedTier)
    {
        if (StrictMode)
            _errors.Add($"Machine requires {required}B but tier is {selectedTier}");
        else
            _warnings.Add("Auto-promoting tier to next size");
    }
}
```

---

## P2 - Medium Priority Tasks

### TASK-G08: Trace Symbolication Tool
**Priority:** P2  
**Gap:** 4.2 in GAP-ANALYSIS.md  
**Design Ref:** Section 4.3

**Description:**
Implement `TraceSymbolicator` to convert binary trace records to human-readable log. Uses debug metadata to resolve IDs → names.

**Scope:**
1. Create `TraceSymbolicator` class
2. Implement `Symbolicate(ReadOnlySpan<TraceRecord> records)` method
3. Map state IDs, event IDs, action IDs to names via `HsmDebugMetadata`
4. Format output as structured log

**Files to Create:**
- `src/Fhsm.Kernel/TraceSymbolicator.cs` (or in a new `Fhsm.Tools` project)

**Tests:**
- Add test: "Symbolicate state enter/exit"
- Add test: "Symbolicate transition with event name"
- Add test: "Fallback to ID if name not found"

---

### TASK-G09: Indirect Event Validation
**Priority:** P2  
**Gap:** 2.4 in GAP-ANALYSIS.md  
**Design Ref:** Section 2.5 - ValidateIndirectEvents, Directive 2

**Description:**
Add compiler validation for events with large payloads (>16B). Must be marked `IsIndirect` and warn if also deferrable.

**Scope:**
1. Add `ValidateIndirectEvents` method to `HsmGraphValidator`
2. Check all event definitions for `PayloadSize > 16`
3. Error if not marked `IsIndirect`
4. Warn if `IsIndirect + IsDeferrable` (dangling reference risk)

**Files to Modify:**
- `src/Fhsm.Compiler/HsmGraphValidator.cs`

---

### TASK-G10: Fail-Safe State Transition
**Priority:** P2  
**Gap:** 3.5 in GAP-ANALYSIS.md  
**Design Ref:** Section 3.3 - RTC Loop

**Description:**
Add fail-safe transition for pathological machines that exceed microstep budget repeatedly.

**Scope:**
1. Add `ForceTransitionToFailSafe(byte* instancePtr)` method
2. Check `ConsecutiveClamps > 5` in RTC loop
3. Force transition to state index 0 (fail-safe)

---

### TASK-G11: Command Buffer Paged Allocator
**Priority:** P2  
**Gap:** 1.4 in GAP-ANALYSIS.md  
**Design Ref:** Section 1.4.1

**Description:**
Implement paged allocator for command buffers with global pool and lane reservations.

**Scope:**
1. Create `CommandPagePool` class (global pool)
2. Implement `AllocateNewPage()` in `HsmCommandWriter`
3. Add lane reservation logic (critical vs best-effort)
4. Implement page linking

---

### TASK-G12: Bootstrapper & Registry
**Priority:** P2  
**Gap:** 4.3 in GAP-ANALYSIS.md  
**Design Ref:** Section 4.4

**Description:**
Implement global registry for managing multiple HSM definitions and their dispatch tables.

**Scope:**
1. Create `HsmBootstrapper` static class
2. Implement `Register(definitionId, blob, guards[], actions[])`
3. Implement `Shutdown()` for cleanup
4. Add validation for linker table completeness

---

## P3 - Low Priority Tasks

### TASK-G13: CommandLane Enum
**Priority:** P3  
**Gap:** 1.1

Add `CommandLane` enum (Animation, Navigation, Gameplay, Blackboard, Audio, VFX, Message) and integrate with command buffer.

---

### TASK-G14: JSON Input Parser
**Priority:** P3  
**Gap:** 2.1

Implement JSON → BuilderGraph parser for alternative authoring format.

---

### TASK-G15: Slot Conflict Validation
**Priority:** P3  
**Gap:** 2.3

Build exclusion graph and validate timer/history slot conflicts for orthogonal regions.

---

### TASK-G16: LinkerTableEntry Struct
**Priority:** P3  
**Gap:** 1.3

Add formal `LinkerTableEntry` struct to blob (currently using raw ActionIds/GuardIds arrays).

---

### TASK-G17: XxHash64 Implementation
**Priority:** P3  
**Gap:** 2.7

Replace SHA256 with XxHash64 for structure/parameter hashes (as per design spec).

---

### TASK-G18: Debug Metadata Export
**Priority:** P3  
**Gap:** 4.4

Export `.blob.debug` sidecar file with state/event/action names and source locations.

---

### TASK-G19: Full Orthogonal Region Support
**Priority:** P3  
**Gap:** 5.2

Implement region arbitration and `OutputLaneMask` for conflict detection.

---

### TASK-G20: Deep History Support
**Priority:** P3  
**Gap:** 5.3

Implement deep history restore logic (currently only shallow history works).

---

## Task Summary

**Total Tasks:** 20  
**P0 (Critical):** 3  
**P1 (High):** 4  
**P2 (Medium):** 5  
**P3 (Low):** 8

**Recommended Order:**
1. TASK-G01: Global Transitions (P0) - Quick win
2. TASK-G03: Hot Reload (P0) - High value
3. TASK-G02: Command Buffer (P0) - Largest change
4. TASK-G05: Timer Cancellation (P1) - Quick win
5. TASK-G06: Deferred Queue (P1) - Medium complexity
6. TASK-G04: RNG Wrapper (P1) - Medium complexity
7. TASK-G07: Tier Budget (P1) - Compiler polish
8. P2 tasks as needed
9. P3 tasks deferred to v2.0

---

**Next:** Begin implementation with TASK-G01 (Global Transitions).

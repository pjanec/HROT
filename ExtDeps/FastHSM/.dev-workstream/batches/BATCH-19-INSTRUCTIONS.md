# BATCH-19: All P1 Tasks - RNG, Timers, Deferred Queue, Tier Budget (TASK-G04-G07)

**Batch Number:** BATCH-19  
**Tasks:** TASK-G04 (RNG), TASK-G05 (Timers), TASK-G06 (Deferred Queue), TASK-G07 (Tier Budget)  
**Phase:** Kernel + Compiler (P1 Complete)  
**Estimated Effort:** 6-8 hours  
**Priority:** P1 (High Priority - All Remaining P1 Tasks)  
**Dependencies:** BATCH-18 complete

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Task Definitions:** `.dev-workstream/GAP-TASKS.md` - TASK-G04, G05, G06, G07
2. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Sections 1.6 (RNG), 3.5 (Exit), 3.3 (RTC), 2.5 (Budget)
3. **Architect Directive 3:** RNG debug tracking

### Source Code
- **Primary:** `src/Fhsm.Kernel/`
- **Tests:** `tests/Fhsm.Tests/`

### Report
**Submit to:** `.dev-workstream/reports/BATCH-19-REPORT.md`  
**Template:** `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

---

## Context

This batch completes all P1 (High Priority) tasks - production-readiness features.

### Part 1: RNG Wrapper (TASK-G04)
Deterministic RNG for guards. XorShift32 with debug tracking.

### Part 2: Timer Cancellation (TASK-G05)
Cancel timers on state exit to prevent stale events.

### Part 3: Deferred Queue Merge (TASK-G06)
Merge deferred events back to active queue at RTC boundaries.

### Part 4: Tier Budget Validation (TASK-G07)
Compiler validates machine fits in selected tier, auto-promotes if needed.

---

## 🎯 Objectives

1. Implement deterministic RNG wrapper with debug tracking
2. Implement timer cancellation on state exit
3. Implement deferred queue merging at RTC boundaries
4. Implement tier budget validation in compiler
5. Add comprehensive tests for all features

---

## ✅ Tasks

### Task 1: Implement HsmRng (TASK-G04)

**File:** `src/Fhsm.Kernel/HsmRng.cs` (NEW)

**Requirements:**

```csharp
using System;
using System.Runtime.CompilerServices;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Deterministic RNG wrapper for HSM guards/actions.
    /// Uses XorShift32 algorithm.
    /// </summary>
    public unsafe ref struct HsmRng
    {
        private uint* _seedPtr;
        
        #if DEBUG
        private int* _debugAccessCount;
        #endif
        
        /// <summary>
        /// Create RNG wrapper from seed pointer.
        /// </summary>
        public HsmRng(uint* seedPtr)
        {
            _seedPtr = seedPtr;
            
            #if DEBUG
            _debugAccessCount = null;
            #endif
        }
        
        /// <summary>
        /// Create RNG with debug tracking.
        /// </summary>
        #if DEBUG
        public HsmRng(uint* seedPtr, int* debugAccessCount)
        {
            _seedPtr = seedPtr;
            _debugAccessCount = debugAccessCount;
        }
        #endif
        
        /// <summary>
        /// Generate random float [0, 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            
            // Convert to [0, 1) range
            return (x >> 8) * (1.0f / 16777216.0f);
        }
        
        /// <summary>
        /// Generate random int in range [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int min, int max)
        {
            if (max <= min) return min;
            
            float f = NextFloat();
            return min + (int)(f * (max - min));
        }
        
        /// <summary>
        /// Generate random bool.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NextBool()
        {
            return NextFloat() >= 0.5f;
        }
        
        /// <summary>
        /// Get current seed value (for serialization/debugging).
        /// </summary>
        public uint CurrentSeed => *_seedPtr;
    }
}
```

**Key Points:**
- **XorShift32:** Fast, deterministic, good distribution
- **Ref Struct:** Stack-only, no allocations
- **Debug Tracking:** Conditional compilation for replay validation
- **AggressiveInlining:** Minimize overhead

---

### Task 2: Add RNG to Instance Context

**File:** `src/Fhsm.Kernel/Data/InstanceHeader.cs` (UPDATE)

**Add RNG seed field:**

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct InstanceHeader
{
    // ... existing fields ...
    
    [FieldOffset(12)] public uint RngSeed;  // NEW: For HsmRng
    
    #if DEBUG
    [FieldOffset(??)] public int DebugRngAccessCount;  // NEW: Optional debug tracking
    #endif
}
```

**Note:** Check current `InstanceHeader` size. If `Size = 16`, you may need to expand to 20 bytes or use spare bytes. Check existing layout first.

---

### Task 3: Add RNG Tests

**File:** `tests/Fhsm.Tests/Kernel/HsmRngTests.cs` (NEW)

**Required Tests:**

```csharp
[Fact]
public void NextFloat_Produces_Deterministic_Sequence()
{
    // Setup: Two RNG with same seed
    uint seed1 = 12345;
    uint seed2 = 12345;
    
    var rng1 = new HsmRng(&seed1);
    var rng2 = new HsmRng(&seed2);
    
    // Generate 10 values from each
    for (int i = 0; i < 10; i++)
    {
        float v1 = rng1.NextFloat();
        float v2 = rng2.NextFloat();
        
        Assert.Equal(v1, v2);
    }
}

[Fact]
public void NextFloat_Returns_Values_In_Range()
{
    uint seed = 54321;
    var rng = new HsmRng(&seed);
    
    for (int i = 0; i < 100; i++)
    {
        float v = rng.NextFloat();
        Assert.True(v >= 0.0f && v < 1.0f);
    }
}

[Fact]
public void NextInt_Returns_Values_In_Range()
{
    uint seed = 99999;
    var rng = new HsmRng(&seed);
    
    for (int i = 0; i < 100; i++)
    {
        int v = rng.NextInt(10, 20);
        Assert.True(v >= 10 && v < 20);
    }
}

[Fact]
public void NextBool_Returns_Bool()
{
    uint seed = 11111;
    var rng = new HsmRng(&seed);
    
    int trueCount = 0;
    int falseCount = 0;
    
    for (int i = 0; i < 100; i++)
    {
        if (rng.NextBool()) trueCount++;
        else falseCount++;
    }
    
    // Should be roughly 50/50 (allow 30-70 range for randomness)
    Assert.True(trueCount >= 30 && trueCount <= 70);
}

[Fact]
public void Seed_Advances_On_Each_Call()
{
    uint seed = 42;
    var rng = new HsmRng(&seed);
    
    uint initial = rng.CurrentSeed;
    rng.NextFloat();
    uint after1 = rng.CurrentSeed;
    rng.NextFloat();
    uint after2 = rng.CurrentSeed;
    
    Assert.NotEqual(initial, after1);
    Assert.NotEqual(after1, after2);
}

#if DEBUG
[Fact]
public void Debug_Tracking_Increments_On_Each_Call()
{
    uint seed = 77777;
    int accessCount = 0;
    var rng = new HsmRng(&seed, &accessCount);
    
    Assert.Equal(0, accessCount);
    
    rng.NextFloat();
    Assert.Equal(1, accessCount);
    
    rng.NextInt(0, 10);
    Assert.Equal(2, accessCount); // NextInt calls NextFloat
    
    rng.NextBool();
    Assert.Equal(3, accessCount); // NextBool calls NextFloat
}
#endif
```

---

### Task 4: Implement Timer Cancellation (TASK-G05)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)

**Add CancelTimers method:**

```csharp
/// <summary>
/// Cancel all timers (called on state exit).
/// </summary>
private static unsafe void CancelTimers(byte* instancePtr, int instanceSize)
{
    // Get timer array based on instance size
    int timerCount = GetTimerCount(instanceSize);
    
    if (instanceSize == 64)
    {
        HsmInstance64* inst = (HsmInstance64*)instancePtr;
        for (int i = 0; i < 2; i++)
            inst->TimerDeadlines[i] = 0;
    }
    else if (instanceSize == 128)
    {
        HsmInstance128* inst = (HsmInstance128*)instancePtr;
        for (int i = 0; i < 4; i++)
            inst->TimerDeadlines[i] = 0;
    }
    else if (instanceSize == 256)
    {
        HsmInstance256* inst = (HsmInstance256*)instancePtr;
        for (int i = 0; i < 8; i++)
            inst->TimerDeadlines[i] = 0;
    }
}

private static int GetTimerCount(int instanceSize)
{
    return instanceSize switch
    {
        64 => 2,
        128 => 4,
        256 => 8,
        _ => 0
    };
}
```

**Call CancelTimers in ExecuteTransition:**

Find the exit path execution in `ExecuteTransition` and add timer cancellation:

```csharp
// Execute exit actions along exit path
for (int i = 0; i < exitPathLength; i++)
{
    ushort stateId = exitPath[i];
    ref readonly var state = ref definition.GetState(stateId);
    
    // Cancel timers owned by this state
    CancelTimers(instancePtr, instanceSize);
    
    // Execute exit action
    if (state.OnExitActionId != 0 && state.OnExitActionId != 0xFFFF)
    {
        ExecuteAction(state.OnExitActionId, instancePtr, contextPtr, ref cmdWriter);
    }
    
    // ... rest of exit logic ...
}
```

---

### Task 5: Add Timer Cancellation Tests

**File:** `tests/Fhsm.Tests/Kernel/TimerCancellationTests.cs` (NEW)

**Required Tests:**

```csharp
[Fact]
public void Timer_Cancelled_When_State_Exits()
{
    // Setup: State A arms timer, transition to State B
    // Expected: Timer cleared (deadline = 0)
    
    // Build state machine
    var builder = new HsmBuilder("TimerMachine");
    // ... (create states A, B with transition)
    
    var instance = new HsmInstance64();
    // Initialize, enter State A
    // Manually set timer: instance.TimerDeadlines[0] = 1000;
    // Transition to State B
    
    // Verify timer cleared
    Assert.Equal(0u, instance.TimerDeadlines[0]);
}

[Fact]
public void Multiple_Timers_Cancelled_On_Exit()
{
    // Setup: State with multiple timers armed
    // Expected: All timers cleared on exit
    
    var instance = new HsmInstance128();
    instance.TimerDeadlines[0] = 1000;
    instance.TimerDeadlines[1] = 2000;
    instance.TimerDeadlines[2] = 3000;
    
    // Trigger exit (transition)
    // ...
    
    Assert.Equal(0u, instance.TimerDeadlines[0]);
    Assert.Equal(0u, instance.TimerDeadlines[1]);
    Assert.Equal(0u, instance.TimerDeadlines[2]);
}

[Fact]
public void Timer_Fires_If_State_Still_Active()
{
    // Setup: State A with timer, DO NOT transition
    // Expected: Timer fires normally
    
    // This is a "does not regress" test - verify existing timer logic still works
}
```

**Note:** Timer tests may be complex. If timers aren't fully implemented yet, create placeholder tests that verify `CancelTimers` clears the deadline array.

---

### Task 6: Implement Deferred Queue Merge (TASK-G06)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)

**Add MergeDeferredQueue method:**

```csharp
/// <summary>
/// Merge deferred events back to active queue at RTC boundary.
/// </summary>
private static unsafe void MergeDeferredQueue(byte* instancePtr, int instanceSize)
{
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    
    // If no deferred events, nothing to do
    if (header->DeferredTail == 0)
        return;
    
    // Get event queue based on tier
    if (instanceSize == 64)
    {
        HsmInstance64* inst = (HsmInstance64*)instancePtr;
        // Tier 1: Simple shared queue, just reset deferred tail
        // (Events already in shared space)
        header->DeferredTail = 0;
    }
    else if (instanceSize == 128)
    {
        HsmInstance128* inst = (HsmInstance128*)instancePtr;
        // Move deferred events to active queue
        // Implementation depends on queue layout
        // For now, simple approach: reset deferred tail
        header->DeferredTail = 0;
    }
    else if (instanceSize == 256)
    {
        HsmInstance256* inst = (HsmInstance256*)instancePtr;
        header->DeferredTail = 0;
    }
}
```

**Call in ProcessInstancePhase after RTC:**

```csharp
case InstancePhase.RTC:
    ProcessRTCPhase(definition, instancePtr, instanceSize, contextPtr, currentEventId, ref cmdWriter);
    
    // Merge deferred queue at RTC boundary
    MergeDeferredQueue(instancePtr, instanceSize);
    break;
```

---

### Task 7: Add Deferred Queue Tests

**File:** `tests/Fhsm.Tests/Kernel/DeferredQueueTests.cs` (NEW)

```csharp
[Fact]
public void Deferred_Event_Processed_After_RTC()
{
    // Setup: State machine with deferred event
    // Expected: Event deferred during RTC, processed after
    
    // Note: This test may be simplified if deferred queue
    // isn't fully implemented yet. Focus on MergeDeferredQueue
    // being called and DeferredTail being reset.
}

[Fact]
public void MergeDeferredQueue_Resets_DeferredTail()
{
    var instance = new HsmInstance64();
    instance.Header.DeferredTail = 5;
    
    // Call merge (via kernel update or directly if exposed)
    // ...
    
    Assert.Equal(0, instance.Header.DeferredTail);
}
```

---

### Task 8: Implement Tier Budget Validation (TASK-G07)

**File:** `src/Fhsm.Compiler/HsmGraphValidator.cs` (UPDATE)

**Add CheckTierBudget method:**

```csharp
/// <summary>
/// Validate machine fits in selected tier budget.
/// </summary>
private void CheckTierBudget(StateMachineGraph graph)
{
    // Count requirements
    int maxRegions = 1; // At least 1
    int historySlots = 0;
    int maxDepth = 0;
    
    foreach (var state in graph.AllStates)
    {
        if (state.Regions != null)
            maxRegions = Math.Max(maxRegions, state.Regions.Count);
        
        if ((state.Flags & StateFlags.IsHistory) != 0)
            historySlots++;
        
        maxDepth = Math.Max(maxDepth, state.Depth);
    }
    
    // Compute required size (simplified)
    // Header: 16B
    // ActiveLeafIds: maxRegions * 2B
    // Timers: maxRegions * 4B
    // History: historySlots * 2B
    // Event queue: ~16-32B
    // User context: 16-48B
    
    int required = 16 + (maxRegions * 6) + (historySlots * 2) + 32 + 16;
    
    int selectedTier = graph.Tier; // Assume graph has Tier property
    
    if (required > selectedTier)
    {
        _errors.Add(new ValidationError
        {
            Message = $"Machine requires ~{required}B but tier is {selectedTier}B. " +
                      $"Regions: {maxRegions}, History: {historySlots}",
            Severity = ErrorSeverity.Error
        });
    }
}
```

**Call in Validate method:**

```csharp
public static List<ValidationError> Validate(StateMachineGraph graph)
{
    var validator = new HsmGraphValidator();
    
    // ... existing validations ...
    
    validator.CheckTierBudget(graph);
    
    return validator._errors;
}
```

---

### Task 9: Add Tier Budget Tests

**File:** `tests/Fhsm.Tests/Compiler/TierBudgetTests.cs` (NEW)

```csharp
[Fact]
public void Simple_Machine_Fits_In_Tier1()
{
    // Setup: 2 states, 1 region, no history
    var builder = new HsmBuilder("Simple");
    builder.State("A");
    builder.State("B");
    
    var graph = builder.Build();
    graph.Tier = 64; // Tier 1
    
    HsmNormalizer.Normalize(graph);
    var errors = HsmGraphValidator.Validate(graph);
    
    // Should have no tier budget errors
    Assert.DoesNotContain(errors, e => e.Message.Contains("tier"));
}

[Fact]
public void Complex_Machine_Errors_If_Over_Budget()
{
    // Setup: Many states, regions, history
    var builder = new HsmBuilder("Complex");
    
    // Add 10 states with history
    for (int i = 0; i < 10; i++)
    {
        builder.State($"State{i}").History();
    }
    
    var graph = builder.Build();
    graph.Tier = 64; // Force Tier 1
    
    HsmNormalizer.Normalize(graph);
    var errors = HsmGraphValidator.Validate(graph);
    
    // Should error about tier budget
    Assert.Contains(errors, e => e.Message.Contains("tier") || e.Message.Contains("requires"));
}
```

---

## 🧪 Testing Requirements

### Minimum Tests
- **HsmRngTests.cs:** 6 tests
- **TimerCancellationTests.cs:** 3 tests
- **DeferredQueueTests.cs:** 2 tests
- **TierBudgetTests.cs:** 2 tests
- **Total new tests:** ~13

### Quality Standards
- RNG tests verify determinism, range, distribution
- Timer tests verify cancellation at correct time
- Deferred queue tests verify merge happens
- Tier budget tests verify validation logic

---

## 📊 Report Requirements

**Submit to:** `.dev-workstream/reports/BATCH-19-REPORT.md`

**Focus Areas:**

1. **Issues Encountered:**
   - Did InstanceHeader have spare bytes for RngSeed?
   - Any issues with ref struct lifetime in tests?
   - Timer cancellation: Where exactly did you call it?

2. **Design Decisions:**
   - Where did you place RngSeed in InstanceHeader?
   - Did you modify InstanceHeader size or reuse spare bytes?
   - How did you handle tier-specific timer arrays?

3. **Code Improvements:**
   - Could timer cancellation be more granular (per-state tracking)?
   - Should RNG support multiple algorithms?
   - Any performance concerns with XorShift32?

4. **Testing Insights:**
   - Did RNG distribution look reasonable?
   - Any edge cases discovered?
   - Timer cancellation test coverage sufficient?

---

## 🎯 Success Criteria

### Functionality
- [ ] `HsmRng` ref struct implemented with XorShift32
- [ ] Timer cancellation in exit path
- [ ] Deferred queue merge at RTC boundary
- [ ] Tier budget validation in compiler
- [ ] All 4 P1 tasks complete

### Tests
- [ ] All 189 existing tests still pass
- [ ] 6 RNG tests pass
- [ ] 3 timer tests pass
- [ ] 2 deferred queue tests pass
- [ ] 2 tier budget tests pass
- [ ] **Total: ~202 tests passing**

### Report
- [ ] Report submitted with all sections complete

---

## ⚠️ Common Pitfalls

### Pitfall 1: InstanceHeader Size

```csharp
❌ BAD: Adding field without checking size
[FieldOffset(16)] public uint RngSeed; // CRASH if Size = 16!

✅ GOOD: Check current size, expand if needed
// If Size = 16, expand to Size = 20
[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct InstanceHeader
{
    // ... existing fields at 0-15 ...
    [FieldOffset(16)] public uint RngSeed;
}
```

### Pitfall 2: Ref Struct Lifetime

```csharp
❌ BAD: Storing ref struct in field
class MyClass
{
    private HsmRng _rng; // ERROR: Can't store ref struct
}

✅ GOOD: Use locally in method
void MyMethod()
{
    uint seed = 42;
    var rng = new HsmRng(&seed); // OK - stack only
    float v = rng.NextFloat();
}
```

### Pitfall 3: Forgetting DEBUG Conditional

```csharp
❌ BAD: Always compiling debug code
public HsmRng(uint* seedPtr, int* debugAccessCount) // Always present

✅ GOOD: Conditional compilation
#if DEBUG
public HsmRng(uint* seedPtr, int* debugAccessCount)
{
    // ...
}
#endif
```

---

## 📚 Reference Materials

**Design Documents:**
- Section 1.6: RNG Design
- Section 3.5: Exit Path Execution
- Section 3.3: RTC Loop & Deferred Queue
- Section 2.5: Tier Budget Validation

**Task Definitions:**
- TASK-G04: RNG (lines 170-232)
- TASK-G05: Timers (lines 235-280)
- TASK-G06: Deferred Queue (lines 283-326)
- TASK-G07: Tier Budget (lines 330-377)

**Existing Code:**
- `src/Fhsm.Kernel/Data/InstanceHeader.cs` - Check layout
- `src/Fhsm.Kernel/HsmKernelCore.cs` - Exit path logic

---

## 💡 Implementation Tips

### Tip 1: Check InstanceHeader Layout First

Read `InstanceHeader.cs` to see current size and field layout. You may need to expand `Size` or find spare bytes.

### Tip 2: Test RNG Thoroughly

Determinism is critical. Same seed MUST produce same sequence across runs, platforms, build configs.

### Tip 3: Timer Cancellation is Simple

Just clear the deadline array. Don't overthink it - clearing all timers on exit is correct per design.

### Tip 4: Ref Struct Testing

In tests, create local variables for seed/accessCount, get pointers, then create `HsmRng`. Don't try to store the ref struct.

---

Good luck! This batch completes all P1 tasks - 4 production-ready features. 🚀

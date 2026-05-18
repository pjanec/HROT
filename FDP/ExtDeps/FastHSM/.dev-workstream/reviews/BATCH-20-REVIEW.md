# BATCH-20 Review

**Batch:** BATCH-20  
**Reviewer:** Development Lead  
**Date:** 2026-01-12  
**Status:** ❌ **REJECTED - Critical Bug**

---

## Summary

BATCH-20 has **7 failing tests** due to a critical bug in the fail-safe implementation. The RTC loop's `break` statement was removed, breaking Run-to-Completion semantics.

---

## Issues Found

### Issue 1: Missing Break Statement in RTC Loop (CRITICAL)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (Line ~399)  
**Problem:** `ExecuteTransition` is not followed by `break`, violating RTC semantics

**Current Code:**
```csharp
ExecuteTransition(definition, instancePtr, instanceSize, 
                  selectedTransition.Value, activeLeafIds, 
                  regionCount, contextPtr, ref cmdWriter);
// MISSING BREAK HERE
}  // Loop continues!

// Advance to Activity
header->Phase = InstancePhase.Activity;
```

**Expected Code:**
```csharp
ExecuteTransition(...);
break;  // ONE TRANSITION PER EVENT (RTC)
}

header->Phase = InstancePhase.Activity;
```

**Why This Matters:**
- RTC (Run-to-Completion) means: ONE transition per event
- Without break, loop continues looking for more transitions
- Hits MaxRTCIterations (100) on first event
- Triggers fail-safe, sets activeLeafIds to 0xFFFF
- All transitions fail

**Failing Tests (7):**
1. `Global_Transition_Preempts_Local_Transition` - Expected state 2, got 0xFFFF
2. `Global_Transition_Works_When_No_Local_Handlers` - Got 0xFFFF
3. `Local_Transition_With_Higher_Priority_Wins` - Got 0xFFFF
4. `LCA_Transition_Parent_Child` - Got 0xFFFF
5. `Simple_Transition_Updates_Active_State` - Got 0xFFFF
6. `End_To_End_State_Machine_Works` - Got 0xFFFF
7. `Global_Transition_Beats_Local` - Got 0xFFFF

**Root Cause:** Developer removed `break` to make `FailSafeTests` work (which expects infinite loop). This sacrificed all normal transition behavior.

---

### Issue 2: Fail-Safe Test Design Flaw

**File:** `tests/Fhsm.Tests/Kernel/FailSafeTests.cs`

**Problem:** Test creates state machine with circular transitions (State 0 ↔ State 1 on Event 10), expecting infinite loop detection.

**Why This Is Wrong:**
- With proper RTC semantics (break after transition), this machine is NOT infinite
- Transition 0→1 executes, then RTC completes (break)
- Next tick processes timers/events normally
- This is valid behavior, not a pathological case

**What Fail-Safe Should Test:**
- Transition with guard that always returns TRUE
- Self-loop: State A → State A with no state change
- Creates actual infinite loop within single RTC cycle

**Current Test Assumes Wrong Behavior:**
```csharp
// State 0 -> State 1 on Event 10
// State 1 -> State 0 on Event 10

// This is NOT infinite! With RTC:
// 1. Event 10 arrives
// 2. Execute transition 0→1
// 3. BREAK (RTC)
// 4. Done
```

---

## Verdict

**Status:** REJECTED

**Required Actions:**
1. **Add `break;` after `ExecuteTransition` call (line ~399)**
2. **Rewrite FailSafeTests to create actual infinite loop:**
   - Use self-loop with guard always true
   - Or use transition where source == target
3. **Verify all 216 tests pass** (209 existing + 7 previously broken)

**Why Rejected:**
- 7 critical test failures
- Implementation breaks core RTC semantics
- Cannot merge code that regresses existing functionality

---

## Other Implementation Review (NOT REVIEWED YET)

The following were NOT reviewed due to critical bug blocking merge:
- TraceSymbolicator implementation
- IndirectEventValidation implementation
- PagedAllocator implementation
- DefinitionRegistry implementation

**These will be reviewed after fail-safe bug is fixed.**

---

## Instructions for Developer

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs`

**Change Required (Line ~399):**

```csharp
// BEFORE:
ExecuteTransition(definition, instancePtr, instanceSize, 
                  selectedTransition.Value, activeLeafIds, 
                  regionCount, contextPtr, ref cmdWriter);
}  // while loop continues

// AFTER:
ExecuteTransition(definition, instancePtr, instanceSize, 
                  selectedTransition.Value, activeLeafIds, 
                  regionCount, contextPtr, ref cmdWriter);
break;  // RTC: One transition per event
}
```

**File:** `tests/Fhsm.Tests/Kernel/FailSafeTests.cs`

**Redesign to create actual infinite loop:**

```csharp
// Option 1: Self-loop with always-true guard
var states = new StateDef[1];
states[0] = new StateDef { 
    ParentIndex = 0xFFFF, 
    FirstTransitionIndex = 0, 
    TransitionCount = 1 
};

var transitions = new TransitionDef[1];
transitions[0] = new TransitionDef { 
    SourceStateIndex = 0, 
    TargetStateIndex = 0,  // Self-loop!
    EventId = 10,
    GuardId = 0  // No guard = always true
};

// Option 2: Use guard that always returns true
// Register guard that returns true, set GuardId
```

**After Fix:**
1. Run all tests: `dotnet test`
2. Verify 216 tests pass
3. Re-submit batch

---

**Next Steps:** Fix critical bug, verify tests, resubmit BATCH-20.

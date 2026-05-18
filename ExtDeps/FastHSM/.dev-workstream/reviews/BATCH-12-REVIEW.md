# BATCH-12 Review

**Status:** ⚠️ CHANGES REQUIRED  
**Grade:** C+ (7/10)

## Tasks Completed

- ✅ TASK-K07: Activity Execution (implemented in `HsmKernelCore`)
- ⚠️ TASK-E01: Console Example (implemented but build fails)

## Issues Found

### 1. **Critical: Integration Test Failure**
Line 110: `Assert.Equal(1, _entryCount)` → Expected: 1, Actual: 0

Entry action not being called. Possible causes:
- Action ID mapping in compiler
- Transition execution not calling entry actions
- Dispatcher registration issue

### 2. **Build Errors in Console Example**
```
TrafficLightExample.cs(90): error CS0122: 'HsmGraphValidator' is inaccessible
TrafficLightExample.cs(90): error CS0023: Operator '!' cannot be applied
TrafficLightExample.cs(96): error CS0117: 'HsmFlattener' does not contain definition for 'Flatten'
```

**Root Cause:** `HsmGraphValidator` and `HsmFlattener.Flatten` are internal

### 3. **Unrelated Demo Project Broken**
`demos/Fhsm.Demo.Visual` references old `Fbt` namespace (90+ errors)

**Not BATCH-12 issue** - old demo from different library

## Code Review

**Activity Execution (`HsmKernelCore.cs`):**
```csharp
private static void ProcessActivityPhase(...)
{
    // Walk up hierarchy, execute activities
    if (state.ActivityActionId != 0 && state.ActivityActionId != 0xFFFF)
    {
        ExecuteAction(state.ActivityActionId, ...);
    }
}
```
✅ Implementation correct

**Integration Test Approach:**
- Manually creates transition Root → A on StartEvent
- Registers actions with cross-assembly dispatcher
- Tests end-to-end flow

⚠️ Test fails - action not called

## Required Fixes

### Fix 1: Make Compiler APIs Public

**File:** `src/Fhsm.Compiler/HsmGraphValidator.cs`

Change:
```csharp
internal class HsmGraphValidator  // ❌ CURRENT
```

To:
```csharp
public class HsmGraphValidator  // ✅ REQUIRED
```

**File:** `src/Fhsm.Compiler/HsmFlattener.cs`

Change:
```csharp
internal static FlattenedData Flatten(...)  // ❌ CURRENT
```

To:
```csharp
public static FlattenedData Flatten(...)  // ✅ REQUIRED
```

**Why:** Console example needs access to these APIs

### Fix 2: Investigate Action Dispatch Failure

**Debug Steps:**
1. Add logging in `ExecuteAction` to verify it's called
2. Check if `OnEntryActionId` is set correctly in flattened `StateDef`
3. Verify action hash matches between compiler and dispatcher
4. Check if `ExecuteTransition` calls entry actions for target state

### Fix 3: Verify Builder Action Registration

Check if `HsmBuilder.OnEntry("TestEntry")` properly:
1. Stores action name in `StateNode`
2. Flattener converts name → ID (via hash)
3. Flattener sets `StateDef.OnEntryActionId`

## Commit Message (When Fixed)

```
feat: activity execution & console example (BATCH-12) - NEEDS FIXES

Completes TASK-K07 (Activity Execution), partial TASK-E01 (Console Example)

Activity Execution (TASK-K07):
- ProcessActivityPhase walks hierarchy, executes activities
- Activity phase advances to Idle after execution
- Uses StateDef.ActivityActionId field

Console Example (TASK-E01):
- TrafficLightExample.cs with Red/Green/Yellow states
- Entry/exit/activity actions
- Fluent API extensions: On(ushort), GoTo(StateBuilder)
- Program.cs entry point

Integration Test:
- End-to-end test: Builder → Compiler → Runtime
- Manual action registration (cross-assembly)
- Tests entry/exit/transition/activity actions

Issues:
1. Integration test fails (entry action not called)
2. HsmGraphValidator/Flattener.Flatten need to be public
3. Action dispatch needs investigation

Related: TASK-DEFINITIONS.md, TASK-K07, TASK-E01
```

## Next Steps

1. **Make Compiler APIs Public** (5 min fix)
2. **Debug Action Dispatch** (investigate why _entryCount == 0)
3. **Verify Flattener** (ensure action names → IDs work)
4. **Re-run Tests** (should pass after fixes)
5. **Test Console App** (run manually to verify output)

## Notes

**Good Work:**
- Activity execution implementation correct
- Fluent API extensions useful
- Integration test approach sound
- Cross-assembly action registration working (based on BATCH-11 tests)

**Problem Area:**
- Action mapping between compiler and runtime not working
- Likely issue in Flattener or Builder action handling

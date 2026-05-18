# BATCH-12.1: Fix Action Dispatch & Console Example

**Batch Number:** BATCH-12.1 (Corrective)  
**Parent Batch:** BATCH-12  
**Estimated Effort:** 2-3 days  
**Priority:** HIGH (Corrective)

---

## 📋 Background

This is a **corrective batch** addressing issues found in BATCH-12 review.

**Original Batch:** `.dev-workstream/batches/BATCH-12-INSTRUCTIONS.md`  
**Review with Issues:** `.dev-workstream/reviews/BATCH-12-REVIEW.md`

**Critical Issue:** Integration test fails - entry actions not being called (expected: 1, actual: 0).

---

## 🎯 Objectives

Fix three critical issues from BATCH-12:

1. **Issue 1: Action Dispatch Failure**
   - **Why it's a problem:** Entry/exit/transition actions not executing
   - **What needs to change:** Fix action ID mapping in Flattener

2. **Issue 2: Internal Compiler APIs**
   - **Why it's a problem:** Console example can't build
   - **What needs to change:** Make APIs public

3. **Issue 3: Verify End-to-End Flow**
   - **Why it's a problem:** Need to prove entire system works
   - **What needs to change:** Integration test must pass

---

## ✅ Tasks

### Task 1: Make Compiler APIs Public

**Update:** `src/Fhsm.Compiler/HsmGraphValidator.cs`

**Change line 8:**
```csharp
// BEFORE:
internal class HsmGraphValidator

// AFTER:
public class HsmGraphValidator
```

**Update:** `src/Fhsm.Compiler/HsmFlattener.cs`

**Change line 21:**
```csharp
// BEFORE:
internal static FlattenedData Flatten(StateMachineGraph graph)

// AFTER:
public static FlattenedData Flatten(StateMachineGraph graph)
```

---

### Task 2: Debug Action Dispatch Failure

**Root Cause Investigation:**

The entry action is not being called. Possible causes:

1. **Flattener not generating action IDs correctly**
2. **Builder not storing action names properly**
3. **StateDef fields not being populated**
4. **Transition execution not calling entry actions**

**Step 1: Verify Builder Stores Action Names**

**File:** `src/Fhsm.Compiler/Graph/StateNode.cs`

Check that `StateNode` has properties:
```csharp
public string? OnEntryAction { get; set; }
public string? OnExitAction { get; set; }
public string? ActivityAction { get; set; }
```

**Step 2: Verify Flattener Generates Action IDs**

**File:** `src/Fhsm.Compiler/HsmFlattener.cs`

In `FlattenStates` method, ensure action names are converted to IDs:

```csharp
private static StateDef[] FlattenStates(
    StateMachineGraph graph,
    Dictionary<string, ushort> actionTable,
    Dictionary<string, ushort> guardTable)
{
    var states = graph.States;
    var result = new StateDef[states.Count];
    
    for (int i = 0; i < states.Count; i++)
    {
        var node = states[i];
        var def = new StateDef();
        
        // ... other fields ...
        
        // ACTION IDs - THIS IS CRITICAL
        def.OnEntryActionId = GetActionId(node.OnEntryAction, actionTable);
        def.OnExitActionId = GetActionId(node.OnExitAction, actionTable);
        def.ActivityActionId = GetActionId(node.ActivityAction, actionTable);
        
        result[i] = def;
    }
    
    return result;
}

private static ushort GetActionId(string? actionName, Dictionary<string, ushort> actionTable)
{
    if (string.IsNullOrEmpty(actionName))
        return 0; // No action
    
    if (actionTable.TryGetValue(actionName, out ushort id))
        return id;
    
    // Action not in table - this is a problem!
    throw new InvalidOperationException($"Action '{actionName}' not found in action table");
}
```

**CRITICAL:** Verify `BuildActionTable` collects action names from ALL sources:
- State entry actions (`node.OnEntryAction`)
- State exit actions (`node.OnExitAction`)
- State activities (`node.ActivityAction`)
- Transition actions (`transition.ActionName`)

**Step 3: Add Diagnostic Logging**

Temporarily add console output to verify action dispatch:

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs`

In `ExecuteAction`:
```csharp
private static void ExecuteAction(
    ushort actionId,
    byte* instancePtr,
    void* contextPtr,
    ushort eventId)
{
    // DIAGNOSTIC (remove after fixing)
    System.Console.WriteLine($"[DEBUG] ExecuteAction called: actionId={actionId}");
    
    HsmActionDispatcher.ExecuteAction(actionId, instancePtr, contextPtr, eventId);
}
```

In `ExecuteTransition` (exit actions):
```csharp
if (state.OnExitActionId != 0 && state.OnExitActionId != 0xFFFF)
{
    System.Console.WriteLine($"[DEBUG] Executing exit action for state {stateId}: actionId={state.OnExitActionId}");
    ExecuteAction(state.OnExitActionId, instancePtr, contextPtr, transition.EventId);
}
```

In `ExecuteTransition` (entry actions):
```csharp
if (state.OnEntryActionId != 0 && state.OnEntryActionId != 0xFFFF)
{
    System.Console.WriteLine($"[DEBUG] Executing entry action for state {stateId}: actionId={state.OnEntryActionId}");
    ExecuteAction(state.OnEntryActionId, instancePtr, contextPtr, transition.EventId);
}
```

**Step 4: Verify Hash Computation Matches**

Ensure the hash function in Flattener matches the one in SourceGen:

**File:** `src/Fhsm.Compiler/HsmFlattener.cs`

```csharp
private static ushort ComputeHash(string name)
{
    // FNV-1a hash (MUST match SourceGen)
    uint hash = 2166136261;
    foreach (char c in name)
    {
        hash ^= c;
        hash *= 16777619;
    }
    return (ushort)(hash & 0xFFFF);
}
```

Compare with `src/Fhsm.SourceGen/HsmActionGenerator.cs` line 155.

---

### Task 3: Fix BuildActionTable

**File:** `src/Fhsm.Compiler/HsmFlattener.cs`

**Current Implementation (verify it looks like this):**
```csharp
private static Dictionary<string, ushort> BuildActionTable(StateMachineGraph graph)
{
    var actions = new HashSet<string>();
    
    // Collect from states
    foreach (var state in graph.States)
    {
        if (!string.IsNullOrEmpty(state.OnEntryAction))
            actions.Add(state.OnEntryAction);
        
        if (!string.IsNullOrEmpty(state.OnExitAction))
            actions.Add(state.OnExitAction);
        
        if (!string.IsNullOrEmpty(state.ActivityAction))
            actions.Add(state.ActivityAction);
    }
    
    // Collect from transitions
    foreach (var state in graph.States)
    {
        foreach (var transition in state.Transitions)
        {
            if (!string.IsNullOrEmpty(transition.ActionName))
                actions.Add(transition.ActionName);
        }
    }
    
    // Build table
    var table = new Dictionary<string, ushort>();
    foreach (var action in actions.OrderBy(a => a)) // Sort for determinism
    {
        ushort id = ComputeHash(action);
        table[action] = id;
    }
    
    return table;
}
```

**If missing, add this method!**

---

### Task 4: Remove Diagnostic Logging

After tests pass, remove all `System.Console.WriteLine` debug statements added in Task 2.

---

## 🧪 Testing Requirements

**Existing tests must pass:**
- All 161 tests from previous batches
- Integration test: `End_To_End_State_Machine_Works` must pass

**Verification:**
```bash
dotnet test tests/Fhsm.Tests/Fhsm.Tests.csproj --filter "FullyQualifiedName~IntegrationTests"
```

**Expected Output:**
```
Passed!  - Failed: 0, Passed: 1
```

**Console Example:**
```bash
dotnet run --project src/Fhsm.Examples.Console
```

**Expected Output:**
```
=== Traffic Light State Machine ===

Compiled: 3 states, 3 transitions

🔴 RED - Stop!
...
```

---

## 📊 Success Criteria

- [ ] `HsmGraphValidator` is public
- [ ] `HsmFlattener.Flatten` is public
- [ ] `BuildActionTable` collects all action names
- [ ] Action IDs computed with FNV-1a hash
- [ ] Integration test passes (entry action called)
- [ ] All 162+ tests passing
- [ ] Console example builds without errors
- [ ] No diagnostic logging left in code

---

## 🔍 Debugging Checklist

If integration test still fails after fixes:

1. [ ] Print `blob.ActionIds` array - are action IDs present?
2. [ ] Print `StateDef.OnEntryActionId` for state A - is it non-zero?
3. [ ] Print action hash for "TestEntry" - does it match?
4. [ ] Check if `ExecuteTransition` is being called
5. [ ] Check if entry actions are being executed in `ExecuteTransition`
6. [ ] Verify `HsmActionDispatcher.GetAction(entryId)` returns non-zero IntPtr
7. [ ] Check if actions are registered before running test

---

## 📚 Reference

- **Parent Batch:** [BATCH-12-INSTRUCTIONS.md](../batches/BATCH-12-INSTRUCTIONS.md)
- **Review:** [BATCH-12-REVIEW.md](../reviews/BATCH-12-REVIEW.md)
- **Design:** `docs/design/HSM-Implementation-Design.md` - §2.3 (Source Gen), §3.5 (Actions)

---

**This is a critical fix - the entire action dispatch system depends on it!** 🚨

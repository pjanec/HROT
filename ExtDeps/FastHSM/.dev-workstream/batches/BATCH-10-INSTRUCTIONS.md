# BATCH-10: Kernel LCA & Transition Execution

**Batch Number:** BATCH-10  
**Tasks:** TASK-K05 (LCA Algorithm), TASK-K06 (Transition Execution)  
**Phase:** Phase 3 - Kernel  
**Estimated Effort:** 6-8 days  
**Priority:** HIGH  
**Dependencies:** BATCH-08, BATCH-09

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to **BATCH-10**! This batch implements the **core transition execution logic** - the heart of the HSM runtime. You'll implement the Least Common Ancestor (LCA) algorithm and full transition execution with exit/entry actions.

This is a **complex, critical batch**. Take time to understand the design thoroughly.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `.dev-workstream/TASK-DEFINITIONS.md` - See TASK-K05, TASK-K06 details
3. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Section 3.4 (LCA), 3.5 (Transition Execution)
4. **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q3 (History), Q6 (Cost)
5. **Previous Reviews:** 
   - `.dev-workstream/reviews/BATCH-08-REVIEW.md` - Kernel entry
   - `.dev-workstream/reviews/BATCH-09-REVIEW.md` - Event pipeline

### Source Code Location

- **Primary Work Area:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)
- **Test Project:** `tests/Fhsm.Tests/Kernel/` (NEW FILE: `TransitionExecutionTests.cs`)

### Report Submission

**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-10-REPORT.md`

Use template: `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-10-QUESTIONS.md`

---

## Context

**Event pipeline complete (BATCH-09).** Transitions are selected but execution is stubbed.

**This batch implements:**
1. **LCA Algorithm** - Find exit/entry paths between states
2. **Transition Execution** - Execute exit actions, transition action, entry actions
3. **History State Handling** - Restore/save history (Architect Q3)

**Related Tasks:**
- [TASK-K05](../TASK-DEFINITIONS.md#task-k05-lca-algorithm) - LCA Algorithm
- [TASK-K06](../TASK-DEFINITIONS.md#task-k06-transition-execution) - Transition Execution

---

## 🎯 Batch Objectives

Replace `ExecuteTransitionStub` with full transition execution:
- Compute LCA (Least Common Ancestor) between source and target
- Execute exit actions (source → LCA)
- Execute transition action
- Execute entry actions (LCA → target)
- Handle history states (save/restore)

---

## ✅ Tasks

### Task 1: LCA Algorithm (TASK-K05)

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Implement the Least Common Ancestor algorithm to find the exit/entry path.

#### LCA Theory

Given source state S and target state T:
1. Build ancestor chain for S: [S, parent(S), parent(parent(S)), ..., root]
2. Build ancestor chain for T: [T, parent(T), parent(parent(T)), ..., root]
3. Find first common ancestor (LCA)
4. Exit path: S → LCA (exclusive)
5. Entry path: LCA → T (exclusive)

**Example:**
```
Root
├── A
│   ├── A1
│   └── A2
└── B
    └── B1

Transition: A1 → B1
- A1 ancestors: [A1, A, Root]
- B1 ancestors: [B1, B, Root]
- LCA: Root
- Exit: [A1, A]
- Entry: [B, B1]
```

#### Implementation

```csharp
private struct TransitionPath
{
    public ushort LCA;
    public ushort ExitCount;  // Number of states to exit
    public ushort EntryCount; // Number of states to enter
    public unsafe fixed ushort ExitPath[16];  // Max depth 16
    public unsafe fixed ushort EntryPath[16];
}

private static unsafe TransitionPath ComputeLCA(
    HsmDefinitionBlob definition,
    ushort sourceStateId,
    ushort targetStateId)
{
    var path = new TransitionPath();
    
    // Special case: same state (self-transition)
    if (sourceStateId == targetStateId)
    {
        path.LCA = sourceStateId;
        path.ExitCount = 0;
        path.EntryCount = 0;
        return path;
    }
    
    // Build ancestor chains
    Span<ushort> sourceChain = stackalloc ushort[16];
    Span<ushort> targetChain = stackalloc ushort[16];
    
    int sourceDepth = BuildAncestorChain(definition, sourceStateId, sourceChain);
    int targetDepth = BuildAncestorChain(definition, targetStateId, targetChain);
    
    // Find LCA (walk from root down)
    ushort lca = 0xFFFF;
    int commonDepth = 0;
    
    for (int i = 0; i < Math.Min(sourceDepth, targetDepth); i++)
    {
        // Chains are stored root-first
        if (sourceChain[i] == targetChain[i])
        {
            lca = sourceChain[i];
            commonDepth = i + 1;
        }
        else
        {
            break;
        }
    }
    
    path.LCA = lca;
    
    // Build exit path (source → LCA, exclusive)
    // Exit order: leaf to root
    path.ExitCount = (ushort)(sourceDepth - commonDepth);
    for (int i = 0; i < path.ExitCount; i++)
    {
        path.ExitPath[i] = sourceChain[sourceDepth - 1 - i];
    }
    
    // Build entry path (LCA → target, exclusive)
    // Entry order: root to leaf
    path.EntryCount = (ushort)(targetDepth - commonDepth);
    for (int i = 0; i < path.EntryCount; i++)
    {
        path.EntryPath[i] = targetChain[commonDepth + i];
    }
    
    return path;
}

private static int BuildAncestorChain(
    HsmDefinitionBlob definition,
    ushort stateId,
    Span<ushort> chain)
{
    int depth = 0;
    ushort current = stateId;
    
    // Build chain from leaf to root
    Span<ushort> tempChain = stackalloc ushort[16];
    
    while (current != 0xFFFF && depth < 16)
    {
        tempChain[depth++] = current;
        ref readonly var state = ref definition.GetState(current);
        current = state.ParentIndex;
    }
    
    // Reverse to root-first order
    for (int i = 0; i < depth; i++)
    {
        chain[i] = tempChain[depth - 1 - i];
    }
    
    return depth;
}
```

---

### Task 2: Transition Execution (TASK-K06)

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Replace `ExecuteTransitionStub` with full execution logic.

#### Execution Steps

1. **Compute LCA path**
2. **Execute exit actions** (leaf → LCA)
3. **Save history** (if exiting composite with history)
4. **Execute transition action**
5. **Execute entry actions** (LCA → leaf)
6. **Restore history** (if entering history state)
7. **Update active state**

#### Implementation

```csharp
private static unsafe void ExecuteTransition(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    TransitionDef transition,
    ushort* activeLeafIds,
    int regionCount,
    void* contextPtr)
{
    ushort sourceStateId = transition.SourceStateIndex;
    ushort targetStateId = transition.TargetStateIndex;
    
    // Compute LCA path
    var path = ComputeLCA(definition, sourceStateId, targetStateId);
    
    // 1. Execute exit actions (leaf → LCA)
    for (int i = 0; i < path.ExitCount; i++)
    {
        ushort stateId = path.ExitPath[i];
        ref readonly var state = ref definition.GetState(stateId);
        
        if (state.ExitActionId != 0xFFFF)
        {
            ExecuteAction(state.ExitActionId, instancePtr, contextPtr, transition.EventId);
        }
        
        // Save history if this state has history
        if ((state.Flags & StateFlags.HasHistory) != 0)
        {
            SaveHistory(instancePtr, instanceSize, stateId, activeLeafIds[0]); // TODO: region handling
        }
    }
    
    // 2. Execute transition action
    if (transition.ActionId != 0xFFFF)
    {
        ExecuteAction(transition.ActionId, instancePtr, contextPtr, transition.EventId);
    }
    
    // 3. Execute entry actions (LCA → leaf)
    ushort finalLeafId = targetStateId;
    
    for (int i = 0; i < path.EntryCount; i++)
    {
        ushort stateId = path.EntryPath[i];
        ref readonly var state = ref definition.GetState(stateId);
        
        // Check if history state
        if ((state.Flags & StateFlags.IsHistory) != 0)
        {
            // Restore history
            ushort restoredLeaf = RestoreHistory(instancePtr, instanceSize, stateId);
            if (restoredLeaf != 0xFFFF)
            {
                finalLeafId = restoredLeaf;
                break; // History state determines final leaf
            }
        }
        
        if (state.EntryActionId != 0xFFFF)
        {
            ExecuteAction(state.EntryActionId, instancePtr, contextPtr, transition.EventId);
        }
        
        // If composite, resolve to initial child
        if ((state.Flags & StateFlags.IsComposite) != 0)
        {
            ushort initialChild = state.FirstChildIndex;
            if (initialChild != 0xFFFF)
            {
                // Continue entry into initial child
                // (This is simplified - full implementation needs recursive resolution)
                finalLeafId = initialChild;
            }
        }
    }
    
    // 4. Update active state
    // TODO: Determine which region this transition affects
    activeLeafIds[0] = finalLeafId;
}

private static void ExecuteAction(
    ushort actionId,
    byte* instancePtr,
    void* contextPtr,
    ushort eventId)
{
    // Action execution via function pointers (source gen)
    // For now, stub (will be implemented in later batch)
}

private static unsafe void SaveHistory(
    byte* instancePtr,
    int instanceSize,
    ushort compositeStateId,
    ushort activeLeafId)
{
    // Get history slots
    ushort* historySlots = GetHistorySlots(instancePtr, instanceSize, out int slotCount);
    
    // Find slot for this composite state
    // (Slot index is determined by compiler based on StableId sort - Architect Q3)
    // For now, use simple mapping: compositeStateId % slotCount
    int slotIndex = compositeStateId % slotCount;
    
    if (slotIndex < slotCount)
    {
        historySlots[slotIndex] = activeLeafId;
    }
}

private static unsafe ushort RestoreHistory(
    byte* instancePtr,
    int instanceSize,
    ushort historyStateId)
{
    // Get history slots
    ushort* historySlots = GetHistorySlots(instancePtr, instanceSize, out int slotCount);
    
    // Find slot for this history state
    int slotIndex = historyStateId % slotCount;
    
    if (slotIndex < slotCount)
    {
        return historySlots[slotIndex];
    }
    
    return 0xFFFF; // No history
}

private static unsafe ushort* GetHistorySlots(byte* instancePtr, int instanceSize, out int count)
{
    switch (instanceSize)
    {
        case 64: count = 2; return (ushort*)(instancePtr + 32); // After timers
        case 128: count = 4; return (ushort*)(instancePtr + 40);
        case 256: count = 8; return (ushort*)(instancePtr + 64);
        default: count = 0; return null;
    }
}
```

---

### Task 3: Update RTC Phase

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Replace the call to `ExecuteTransitionStub` with `ExecuteTransition`:

```csharp
private static void ProcessRTCPhase(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    void* contextPtr,
    ushort currentEventId)
{
    const int MaxRTCIterations = 100;
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
    
    for (int iteration = 0; iteration < MaxRTCIterations; iteration++)
    {
        TransitionDef? selectedTransition = SelectTransition(
            definition,
            instancePtr,
            instanceSize,
            activeLeafIds,
            regionCount,
            currentEventId,
            contextPtr);
        
        if (selectedTransition == null)
        {
            break;
        }
        
        // REPLACE THIS LINE:
        // ExecuteTransitionStub(...);
        
        // WITH:
        ExecuteTransition(
            definition,
            instancePtr,
            instanceSize,
            selectedTransition.Value,
            activeLeafIds,
            regionCount,
            contextPtr);
        
        // Check for completion event or continue chain
        break; // For now, single transition per event
    }
    
    if (iteration >= MaxRTCIterations)
    {
        EnterFailSafeState(instancePtr, activeLeafIds, regionCount);
    }
    
    header->Phase = InstancePhase.Activity;
}
```

---

## 🧪 Testing Requirements

**File:** `tests/Fhsm.Tests/Kernel/TransitionExecutionTests.cs` (NEW)

**Minimum 25 tests:**

### LCA Tests (10)
1. Same state (self-transition) → LCA is self, no exit/entry
2. Parent-child transition → LCA is parent
3. Sibling transition → LCA is common parent
4. Deep hierarchy (3+ levels) → LCA computed correctly
5. Exit path order (leaf to root)
6. Entry path order (root to leaf)
7. Root transition → LCA is root
8. Exit count correct
9. Entry count correct
10. Max depth (16 levels) handled

### Transition Execution Tests (10)
11. Exit actions executed in correct order
12. Entry actions executed in correct order
13. Transition action executed between exit/entry
14. Self-transition executes exit then entry
15. History saved on exit from composite
16. History restored on entry to history state
17. Initial state resolved for composite entry
18. Active state updated correctly
19. Multiple regions handled (first region for now)
20. Action execution stubbed (no crash)

### Integration Tests (5)
21. Full transition: A1 → B1 (exit A1, A; enter B, B1)
22. Transition with history: Exit composite, re-enter via history
23. Transition chain (multiple transitions in RTC)
24. Fail-safe after 100 iterations
25. Phase advances to Activity after RTC

---

## 📊 Report Requirements

Your report **MUST** include:

### 1. Implementation Summary
- LCA algorithm implementation approach
- Transition execution flow
- History handling strategy

### 2. Test Results
- Full test output (all 176+ tests)
- Specific results for 25 new tests
- Any failures explained

### 3. Design Decisions
- How did you handle region selection for transitions?
- How did you map history slots to states?
- Any deviations from the instructions?

### 4. Mandatory Questions

**Q1:** Explain the LCA algorithm in your own words. Why is it necessary?

**Q2:** What is the exit/entry order for a transition from `A.A1.A1a` to `B.B1`? (Assume common root)

**Q3:** How does history state restoration work? What happens if no history is saved?

**Q4:** What is the purpose of the fail-safe limit (100 iterations) in the RTC loop?

**Q5:** How would you extend this implementation to support orthogonal regions (multiple active states)?

### 5. Known Issues/Limitations
- Any edge cases not handled
- Simplifications made
- TODOs for future batches

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-K05 completed (LCA algorithm with ancestor chains)
- [ ] TASK-K06 completed (Full transition execution with exit/entry/history)
- [ ] `ExecuteTransitionStub` replaced with `ExecuteTransition`
- [ ] 25+ new tests, all passing
- [ ] All previous tests still passing (176+ total)
- [ ] No compiler warnings
- [ ] Report submitted with all mandatory questions answered
- [ ] Code follows thin shim pattern (no generic expansion in core logic)

---

## ⚠️ Common Pitfalls to Avoid

1. **Exit/Entry Order:** Exit is leaf→root, Entry is root→leaf. Don't reverse!
2. **LCA Exclusive:** LCA state itself doesn't exit or enter.
3. **History Slot Mapping:** Use consistent mapping (compiler will assign stable slots later).
4. **Self-Transition:** Must exit then enter the same state.
5. **Null Checks:** Always check for 0xFFFF (invalid index).
6. **Stack Overflow:** Limit ancestor chain depth to 16.
7. **Action Stubs:** Don't implement full action dispatch yet (next batch).

---

## 📚 Reference Materials

- **Task Defs:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) - TASK-K05, TASK-K06
- **Design:** `docs/design/HSM-Implementation-Design.md` - §3.4 (LCA), §3.5 (Transition)
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q3 (History), Q6 (Cost)
- **Previous:** `.dev-workstream/reviews/BATCH-09-REVIEW.md` - Event pipeline

---

**Good luck! This is the core of the HSM engine. Take your time and test thoroughly.** 🚀

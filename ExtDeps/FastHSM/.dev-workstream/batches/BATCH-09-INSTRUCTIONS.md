# BATCH-09: Kernel Event Pipeline - Timers, Events & RTC Loop

**Batch Number:** BATCH-09  
**Tasks:** TASK-K02 (Timers), TASK-K03 (Events), TASK-K04 (RTC Loop)  
**Phase:** Phase 3 - Kernel  
**Estimated Effort:** 5-7 days (substantial batch)

---

## Context

Core kernel infrastructure complete (BATCH-08). Now implement the **complete event processing pipeline**.

**This batch implements the full tick cycle:**
1. **Timer Phase** - Decrement timers, fire events
2. **Event Phase** - Process event queue with priority
3. **RTC Phase** - Run-to-completion loop with transition selection

**Related Tasks:**
- [TASK-K02](../TASK-DEFINITIONS.md#task-k02-timer-decrement) - Timer Decrement
- [TASK-K03](../TASK-DEFINITIONS.md#task-k03-event-processing) - Event Processing
- [TASK-K04](../TASK-DEFINITIONS.md#task-k04-rtc-loop) - RTC Loop

---

## Task 1: Timer Phase (TASK-K02)

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Implement timer decrement and event firing.

### Timer Logic

```csharp
private static void ProcessTimerPhase(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    float deltaTime)
{
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    
    // Get timer array based on tier
    uint* timers = GetTimerArray(instancePtr, instanceSize, out int timerCount);
    
    // Get active state configuration
    ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
    
    // Decrement all timers
    for (int i = 0; i < timerCount; i++)
    {
        if (timers[i] > 0)
        {
            // Decrement (convert deltaTime to uint ticks)
            uint deltaTicks = (uint)(deltaTime * 1000); // Assuming ms
            
            if (timers[i] > deltaTicks)
            {
                timers[i] -= deltaTicks;
            }
            else
            {
                timers[i] = 0;
                
                // Timer fired - enqueue event
                FireTimerEvent(definition, instancePtr, instanceSize, i, activeLeafIds, regionCount);
            }
        }
    }
}

private static void FireTimerEvent(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    int timerIndex,
    ushort* activeLeafIds,
    int regionCount)
{
    // Find which state owns this timer
    // Timer indices map to states that have TimerActionId set
    // Need to search active states for matching timer
    
    for (int r = 0; r < regionCount; r++)
    {
        ushort leafId = activeLeafIds[r];
        if (leafId == 0xFFFF) continue;
        
        // Walk up from leaf, checking each state for timer
        ushort current = leafId;
        while (current != 0xFFFF)
        {
            ref readonly var state = ref definition.GetState(current);
            
            // Check if this state's timer matches
            // (Implementation detail: need to track timer-to-state mapping)
            // For now, create a timer event
            
            var timerEvent = new HsmEvent
            {
                EventId = 0xFFFE, // Special timer event ID
                Priority = EventPriority.Normal,
                Timestamp = 0 // Set by enqueue
            };
            
            HsmEventQueue.TryEnqueue(instancePtr, instanceSize, timerEvent);
            return;
        }
    }
}
```

### Helper Methods

```csharp
private static unsafe uint* GetTimerArray(byte* instancePtr, int instanceSize, out int count)
{
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    
    switch (instanceSize)
    {
        case 64:
            count = 2;
            return (uint*)(instancePtr + 24); // Offset from HsmInstance64
        case 128:
            count = 4;
            return (uint*)(instancePtr + 24);
        case 256:
            count = 8;
            return (uint*)(instancePtr + 32);
        default:
            count = 0;
            return null;
    }
}

private static unsafe ushort* GetActiveLeafIds(byte* instancePtr, int instanceSize, out int count)
{
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    
    switch (instanceSize)
    {
        case 64:
            count = 2;
            return (ushort*)(instancePtr + 16);
        case 128:
            count = 4;
            return (ushort*)(instancePtr + 16);
        case 256:
            count = 8;
            return (ushort*)(instancePtr + 16);
        default:
            count = 0;
            return null;
    }
}
```

---

## Task 2: Event Phase (TASK-K03)

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Implement event processing with budget and priority.

### Event Processing Logic

```csharp
private static void ProcessEventPhase(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    void* contextPtr)
{
    const int MaxEventsPerTick = 10; // Budget to prevent infinite loops
    
    int eventsProcessed = 0;
    
    while (eventsProcessed < MaxEventsPerTick)
    {
        // Try to dequeue highest priority event
        if (!HsmEventQueue.TryDequeue(instancePtr, instanceSize, out HsmEvent evt))
        {
            break; // No more events
        }
        
        eventsProcessed++;
        
        // Check if event is deferred
        if ((evt.Flags & EventFlags.IsDeferred) != 0)
        {
            // Re-enqueue at low priority (defer to next tick)
            evt.Priority = EventPriority.Low;
            evt.Flags &= ~EventFlags.IsDeferred;
            HsmEventQueue.TryEnqueue(instancePtr, instanceSize, evt);
            continue;
        }
        
        // Process event (store for RTC phase)
        InstanceHeader* header = (InstanceHeader*)instancePtr;
        StoreCurrentEvent(instancePtr, evt);
        
        // Trigger RTC phase
        header->Phase = InstancePhase.RTC;
        return; // Process RTC, then return to process more events
    }
    
    // No events, return to idle
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    header->Phase = InstancePhase.Idle;
}

private static void StoreCurrentEvent(byte* instancePtr, in HsmEvent evt)
{
    // Store event in scratch space for RTC phase
    // Use Reserved field in InstanceHeader or scratch registers
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    
    // Store event ID in scratch (we'll need to add CurrentEventId to InstanceHeader)
    // For now, assume we have a field
    // header->CurrentEventId = evt.EventId;
}
```

---

## Task 3: RTC Loop Phase (TASK-K04)

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Implement run-to-completion loop with transition selection.

### RTC Loop Logic

```csharp
private static void ProcessRTCPhase(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    void* contextPtr,
    ushort currentEventId)
{
    const int MaxRTCIterations = 100; // Fail-safe limit
    
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
    
    for (int iteration = 0; iteration < MaxRTCIterations; iteration++)
    {
        // Select transition for current event
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
            // No transition found, event consumed
            break;
        }
        
        // Execute transition (will be fully implemented in BATCH-10)
        // For now, just update active state
        ExecuteTransitionStub(
            definition,
            instancePtr,
            instanceSize,
            selectedTransition.Value,
            activeLeafIds,
            regionCount);
        
        // Check if transition chain continues (target state has transitions)
        // For now, assume single transition per event
        break;
    }
    
    if (iteration >= MaxRTCIterations)
    {
        // Enter fail-safe state
        EnterFailSafeState(instancePtr, activeLeafIds, regionCount);
    }
    
    // RTC complete, advance to Activity phase
    header->Phase = InstancePhase.Activity;
}

private static TransitionDef? SelectTransition(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    ushort* activeLeafIds,
    int regionCount,
    ushort eventId,
    void* contextPtr)
{
    // 1. Check global transitions first (Architect Q7)
    var globalSpan = definition.GlobalTransitions;
    for (int i = 0; i < globalSpan.Length; i++)
    {
        ref readonly var gt = ref globalSpan[i];
        if (gt.EventId == eventId)
        {
            // Check guard (if present)
            if (gt.GuardId == 0xFFFF || EvaluateGuard(gt.GuardId, instancePtr, contextPtr, eventId))
            {
                // Convert to TransitionDef-like structure for execution
                return new TransitionDef
                {
                    SourceStateIndex = activeLeafIds[0], // Use first active state
                    TargetStateIndex = gt.TargetStateIndex,
                    EventId = gt.EventId,
                    GuardId = gt.GuardId,
                    ActionId = gt.ActionId,
                    Flags = gt.Flags,
                    Cost = 0 // Global transitions don't have cost
                };
            }
        }
    }
    
    // 2. Check transitions for each active leaf (walk up hierarchy)
    TransitionDef? bestTransition = null;
    byte highestPriority = 0;
    
    for (int r = 0; r < regionCount; r++)
    {
        ushort leafId = activeLeafIds[r];
        if (leafId == 0xFFFF) continue;
        
        // Walk up from leaf to root
        ushort current = leafId;
        while (current != 0xFFFF)
        {
            ref readonly var state = ref definition.GetState(current);
            
            // Check transitions for this state
            if (state.FirstTransitionIndex != 0xFFFF)
            {
                for (ushort t = 0; t < state.TransitionCount; t++)
                {
                    ushort transIndex = (ushort)(state.FirstTransitionIndex + t);
                    ref readonly var trans = ref definition.GetTransition(transIndex);
                    
                    if (trans.EventId == eventId)
                    {
                        // Extract priority from flags
                        byte priority = (byte)((trans.Flags >> 8) & 0x0F);
                        
                        // Check guard
                        if (trans.GuardId == 0xFFFF || EvaluateGuard(trans.GuardId, instancePtr, contextPtr, eventId))
                        {
                            // Select if higher priority
                            if (bestTransition == null || priority > highestPriority)
                            {
                                bestTransition = trans;
                                highestPriority = priority;
                            }
                        }
                    }
                }
            }
            
            // Move up hierarchy
            current = state.ParentIndex;
        }
    }
    
    return bestTransition;
}

private static bool EvaluateGuard(
    ushort guardId,
    byte* instancePtr,
    void* contextPtr,
    ushort eventId)
{
    // Guard evaluation will use function pointers (source gen)
    // For now, stub: always return true
    return true;
}

private static void ExecuteTransitionStub(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    TransitionDef transition,
    ushort* activeLeafIds,
    int regionCount)
{
    // Full implementation in BATCH-10 (LCA, exit/entry actions)
    // For now, just update active state to target
    
    // Simple: set first region to target state
    activeLeafIds[0] = transition.TargetStateIndex;
}

private static void EnterFailSafeState(byte* instancePtr, ushort* activeLeafIds, int regionCount)
{
    // Set all regions to root (state 0)
    for (int i = 0; i < regionCount; i++)
    {
        activeLeafIds[i] = 0;
    }
    
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    header->Flags |= InstanceFlags.Error;
}
```

---

## Task 4: Update Phase Orchestration

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Wire phases together in `ProcessInstancePhase`:

```csharp
private static void ProcessInstancePhase(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    void* contextPtr,
    float deltaTime,
    InstanceHeader* header)
{
    switch (header->Phase)
    {
        case InstancePhase.Idle:
            // Process timers even when idle (they can trigger Entry)
            ProcessTimerPhase(definition, instancePtr, instanceSize, deltaTime);
            
            // Check if any events in queue
            if (HsmEventQueue.GetCount(instancePtr, instanceSize) > 0)
            {
                header->Phase = InstancePhase.Entry;
            }
            break;
            
        case InstancePhase.Entry:
            // Entry actions (stub for now, will be BATCH-10)
            // For now, just advance to Event phase
            ProcessEventPhase(definition, instancePtr, instanceSize, contextPtr);
            break;
            
        case InstancePhase.RTC:
            // Get current event from storage
            ushort eventId = 0; // TODO: retrieve stored event
            ProcessRTCPhase(definition, instancePtr, instanceSize, contextPtr, eventId);
            break;
            
        case InstancePhase.Activity:
            // Activity execution (stub for now, will be BATCH-10)
            // For now, return to Idle
            header->Phase = InstancePhase.Idle;
            break;
    }
}
```

---

## Task 5: Tests

**File:** `tests/Fhsm.Tests/Kernel/EventPipelineTests.cs` (NEW)

**Minimum 30 tests covering all three tasks:**

### Timer Tests (10)
1. Timer decrements by deltaTime
2. Timer fires event when reaches zero
3. Multiple timers handled
4. Timer event enqueued correctly
5. Timer event has correct priority
6. Timers work for all tiers (64/128/256)
7. Zero timer ignored
8. Timer event triggers state machine
9. Multiple timer firings in one tick
10. Timer overflow handled

### Event Phase Tests (10)
11. Event dequeued in priority order
12. Deferred event re-enqueued
13. Event budget prevents infinite loop
14. Empty queue handled
15. Multiple events processed per tick
16. Interrupt priority processed first
17. Normal priority before Low
18. Deferred flag cleared on re-enqueue
19. Event consumption works
20. Phase transitions to RTC when event processed

### RTC Phase Tests (10)
21. Transition selected by event ID
22. Priority determines selection
23. Guard blocks transition
24. No transition found → event consumed
25. Multiple transitions → highest priority wins
26. Global transitions checked first
27. Hierarchy walk works (leaf to root)
28. Active state updated after transition
29. Fail-safe after max iterations
30. RTC advances to Activity phase

---

## Implementation Notes

### Event Storage

You'll need to store current event for RTC phase. Options:
1. Add `CurrentEventId` field to InstanceHeader (breaks size)
2. Use scratch registers (HsmInstance has scratch space)
3. Pass event through call chain

**Recommendation:** Use scratch register approach.

### Timer-to-State Mapping

Timer indices need to map to states. Two approaches:
1. **Linear search:** Walk active states, check TimerActionId
2. **Pre-computed map:** Store in instance during initialization

For v1, linear search is acceptable (small number of timers).

### Guard Function Pointers

Guards need function pointers. For this batch, stub with `return true`. Source generation (binding) comes later.

### Priority Extraction

```csharp
byte priority = (byte)((flags >> 8) & 0x0F); // 4 bits at offset 8
```

Verify this matches `TransitionFlags` enum layout.

---

## Success Criteria

- [ ] TASK-K02 completed (Timer decrement & firing)
- [ ] TASK-K03 completed (Event processing with priority & budget)
- [ ] TASK-K04 completed (RTC loop with transition selection)
- [ ] Phase orchestration wired correctly
- [ ] 30+ tests, all passing
- [ ] No compiler warnings
- [ ] Report submitted

---

## Reference

- **Tasks:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) TASK-K02, TASK-K03, TASK-K04
- **Design:** `docs/design/HSM-Implementation-Design.md` §3.2 (Event), §3.3 (RTC)
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` Q4 (RNG in guards), Q7 (Global table)

**Report to:** `.dev-workstream/reports/BATCH-09-REPORT.md`

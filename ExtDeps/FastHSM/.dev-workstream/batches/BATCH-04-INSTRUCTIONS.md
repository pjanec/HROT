# BATCH-04: Definition Blob + Instance Management + Validation

**Effort:** 2-3 days  
**Phase:** Complete Data Layer + Core Infrastructure

---

## Overview

You're building the **complete data infrastructure** for the HSM kernel. This includes:
1. The ROM container (HsmDefinitionBlob) with efficient accessors
2. Instance lifecycle management (allocation, initialization, reset)
3. Event queue operations with tier-specific strategies (Architect's critical fix)
4. Validation helpers for runtime safety

**Why this matters:** The kernel will use these APIs for all state machine operations. Your design choices for the instance manager and event queue will directly impact performance in ECS systems processing thousands of entities per frame.

---

## Context: The Architect's Event Queue Fix

Review BATCH-02: You implemented three instance tiers (64B, 128B, 256B). Each has different event queue strategies:

**Tier 1 (64B - Crowd):** 24 bytes for events = single shared queue (1 event max). **Overwrite oldest normal event if interrupt arrives when full.**

**Tier 2/3 (128B/256B):** Hybrid strategy = 24-byte reserved slot for interrupts + shared ring buffer for normal/low priority.

You need to implement the **event enqueue logic** that respects these strategies. The kernel will call `TryEnqueueEvent()` and expect correct priority handling.

---

## Task 1: Definition Blob Container

**Files:** 
- `src/Fhsm.Kernel/Data/HsmDefinitionHeader.cs`
- `src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs`

### HsmDefinitionHeader (32 bytes)

Standard header with magic number validation and hot reload hashes.

```csharp
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct HsmDefinitionHeader
{
    [FieldOffset(0)] public uint Magic;             // 0x4D534846 ('FHSM')
    [FieldOffset(4)] public ushort FormatVersion;
    [FieldOffset(6)] public ushort Reserved1;
    
    [FieldOffset(8)] public uint StructureHash;     // Topology hash
    [FieldOffset(12)] public uint ParameterHash;    // Logic hash
    
    [FieldOffset(16)] public ushort StateCount;
    [FieldOffset(18)] public ushort TransitionCount;
    [FieldOffset(20)] public ushort RegionCount;
    [FieldOffset(22)] public ushort GlobalTransitionCount;
    [FieldOffset(24)] public ushort EventDefinitionCount;
    [FieldOffset(26)] public ushort ActionCount;
    
    [FieldOffset(28)] public uint Reserved2;
    
    public const uint MagicNumber = 0x4D534846;
    
    public bool IsValid() => Magic == MagicNumber;
}
```

### HsmDefinitionBlob

Container with span-based accessors for zero-allocation access. Use `ReadOnlySpan<T>` properties.

**Key design decision:** Spans provide zero-cost views into the arrays. The kernel will iterate these spans in hot loops, so no allocation overhead is critical.

Add bounds-checked indexed accessors for random access patterns.

---

## Task 2: Instance Manager

**File:** `src/Fhsm.Kernel/HsmInstanceManager.cs`

Create a static utility class that handles instance lifecycle. The kernel needs to:
- Initialize new instances with correct tier selection
- Reset instances to initial state
- Clear all instance data for hot reload

### Required API

```csharp
public static class HsmInstanceManager
{
    /// <summary>
    /// Initialize a new instance. Sets phase to Entry, clears all state.
    /// </summary>
    public static unsafe void Initialize<T>(T* instance, HsmDefinitionBlob definition) 
        where T : unmanaged;
    
    /// <summary>
    /// Reset instance to initial state. Clears active states, history, events.
    /// Preserves DefinitionId.
    /// </summary>
    public static unsafe void Reset<T>(T* instance) where T : unmanaged;
    
    /// <summary>
    /// Select appropriate tier based on machine complexity.
    /// Returns 64, 128, or 256.
    /// </summary>
    public static int SelectTier(HsmDefinitionBlob definition);
}
```

### Tier Selection Logic

Implement heuristics based on machine complexity:

```
Tier 1 (64B): 
  - StateCount <= 8
  - HistorySlots <= 2
  - MaxDepth <= 3
  - RegionCount <= 1
  
Tier 2 (128B):
  - StateCount <= 32
  - HistorySlots <= 4
  - MaxDepth <= 6
  - RegionCount <= 2
  
Tier 3 (256B):
  - Everything else
```

**Why:** ECS systems need to batch similar instances for cache efficiency. Auto-selecting tiers based on definition complexity helps developers avoid manual tuning.

---

## Task 3: Event Queue Operations

**File:** `src/Fhsm.Kernel/HsmEventQueue.cs`

Implement event enqueue/dequeue with **tier-specific strategies** (Architect's critical fix from review).

### Required API

```csharp
public static class HsmEventQueue
{
    /// <summary>
    /// Enqueue event respecting priority and tier strategy.
    /// Tier 1: Overwrite oldest normal if interrupt arrives when full.
    /// Tier 2/3: Reserved slot for interrupts, shared ring for others.
    /// Returns false if queue full and event cannot be added.
    /// </summary>
    public static unsafe bool TryEnqueue<T>(T* instance, in HsmEvent evt) 
        where T : unmanaged;
    
    /// <summary>
    /// Dequeue highest priority event. Returns false if queue empty.
    /// </summary>
    public static unsafe bool TryDequeue<T>(T* instance, out HsmEvent evt) 
        where T : unmanaged;
    
    /// <summary>
    /// Peek at next event without removing. Returns false if empty.
    /// </summary>
    public static unsafe bool TryPeek<T>(T* instance, out HsmEvent evt) 
        where T : unmanaged;
    
    /// <summary>
    /// Clear all events in queue.
    /// </summary>
    public static unsafe void Clear<T>(T* instance) where T : unmanaged;
    
    /// <summary>
    /// Get current event count in queue.
    /// </summary>
    public static unsafe int GetCount<T>(T* instance) where T : unmanaged;
}
```

### Implementation Strategy

**Challenge:** You have three different instance types (HsmInstance64/128/256) with different buffer layouts. You need a generic solution that works with `where T : unmanaged`.

**Approach:** 
1. Cast instance pointer to `InstanceHeader*` to access common fields
2. Calculate buffer offset: `(byte*)instance + sizeof(InstanceHeader)`
3. Read `EventQueueHead`, `EventQueueTail`, `EventQueueCount` from header
4. Implement ring buffer logic with proper wraparound
5. For Tier 1: Special case interrupt overwrite logic
6. For Tier 2/3: Check reserved slot first for interrupts

**Tier 1 Overwrite Logic:**
```
If queue full (count == capacity) and new event is Interrupt:
  - Find oldest Normal priority event
  - Overwrite it with interrupt
  - Adjust head/tail/count accordingly
Else if queue full:
  - Return false (cannot enqueue)
```

**Tier 2/3 Reserved Slot:**
```
If event is Interrupt:
  - Check reserved slot (first 24 bytes of buffer)
  - If empty, write there
  - If occupied, return false (only 1 interrupt at a time)
Else:
  - Use shared ring buffer (offset 24+)
  - Standard FIFO with wraparound
```

---

## Task 4: Instance Validation

**File:** `src/Fhsm.Kernel/HsmValidator.cs`

Create validation helpers for runtime safety checks. These will be used in Debug builds to catch invalid state.

```csharp
public static class HsmValidator
{
    /// <summary>
    /// Validate definition blob structure.
    /// Checks magic number, counts, structure integrity.
    /// </summary>
    public static bool ValidateDefinition(HsmDefinitionBlob blob, out string error);
    
    /// <summary>
    /// Validate instance state.
    /// Checks active leaf IDs are valid, depth constraints, etc.
    /// </summary>
    public static unsafe bool ValidateInstance<T>(T* instance, HsmDefinitionBlob definition, out string error)
        where T : unmanaged;
    
    /// <summary>
    /// Validate state ID is valid for this definition.
    /// </summary>
    public static bool IsValidStateId(HsmDefinitionBlob blob, ushort stateId);
    
    /// <summary>
    /// Validate transition ID is valid for this definition.
    /// </summary>
    public static bool IsValidTransitionId(HsmDefinitionBlob blob, ushort transitionId);
}
```

### Validation Rules

**Definition validation:**
- Magic number matches
- StateCount > 0
- Root state exists (index 0, ParentIndex == 0xFFFF)
- No state has ParentIndex >= StateCount
- Transition target states are valid
- No circular parent chains

**Instance validation:**
- DefinitionId != 0
- All active leaf IDs < definition.StateCount
- CurrentDepth <= MaxDepth from definition
- EventQueueHead/Tail/Count are consistent
- Phase is valid enum value

---

## Task 5: Comprehensive Tests

**File:** `tests/Fhsm.Tests/Data/DataLayerIntegrationTests.cs`

Write integration tests covering all components working together.

**Minimum 30 tests covering:**

**Blob (8 tests):**
- Header size, offsets, magic validation
- Blob creation, span accessors, indexed accessors
- Bounds checking throws
- Empty blobs work

**Instance Manager (8 tests):**
- Initialize sets correct defaults
- Reset clears state but preserves DefinitionId
- Tier selection logic (test each tier's thresholds)
- Works with all three instance types

**Event Queue (10 tests):**
- Enqueue/dequeue basic flow
- Priority ordering (interrupt > normal > low)
- Tier 1 overwrite logic (interrupt overwrites normal when full)
- Tier 2/3 reserved slot logic
- Queue full returns false
- Wraparound works correctly
- Clear empties queue
- GetCount returns correct value

**Validation (4 tests):**
- Valid definition passes
- Invalid magic fails
- Invalid state IDs caught
- Instance validation catches bad state

---

## Implementation Notes

### Memory Layout Detective Work

You'll need to determine buffer sizes and offsets dynamically:

```csharp
int bufferSize = sizeof(T) - sizeof(InstanceHeader);
int capacity = bufferSize / sizeof(HsmEvent);
```

For Tier 2/3 reserved slot detection:
```csharp
bool hasTier2Strategy = capacity > 1; // More than 1 event fits
```

### Pointer Arithmetic

You'll be doing a lot of:
```csharp
var header = (InstanceHeader*)instance;
byte* buffer = (byte*)instance + sizeof(InstanceHeader);
HsmEvent* eventPtr = (HsmEvent*)(buffer + index * sizeof(HsmEvent));
```

Make sure your offset calculations are correct. Off-by-one errors will corrupt memory.

### Ring Buffer Math

Standard ring buffer with head/tail pointers:
```
Enqueue: buffer[tail] = event; tail = (tail + 1) % capacity; count++;
Dequeue: event = buffer[head]; head = (head + 1) % capacity; count--;
```

### Tier Detection at Runtime

You can detect tier from sizeof:
```csharp
int tier = sizeof(T) switch
{
    64 => 1,
    128 => 2,
    256 => 3,
    _ => throw new ArgumentException("Invalid instance size")
};
```

---

## Success Criteria

- [ ] All structs/classes compile without warnings
- [ ] 30+ tests, all passing
- [ ] Event queue implements Architect's tier-specific strategies correctly
- [ ] Tier 1 overwrite logic verified by tests
- [ ] Tier 2/3 reserved slot logic verified by tests
- [ ] Instance manager works with all three instance types
- [ ] Validation catches common errors
- [ ] XML docs on all public APIs

---

## Report Requirements

**File:** `.dev-workstream/reports/BATCH-04-REPORT.md`

Include:
1. Task completion summary
2. Full test results (count, pass/fail)
3. Any design decisions you made (e.g., how you handled type detection)
4. Any edge cases you discovered and how you handled them
5. Time spent

**No mandatory questions.** Your implementation quality speaks for itself.

---

## References

- `docs/design/HSM-Implementation-Design.md` - Sections 1.2 (Blob), 1.3 (Instance Layout), 3.2 (Event Processing)
- `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q1 (Event Queue Fix)
- Your BATCH-02 implementation (Instance structs with EventBuffer fixed arrays)
- BTree inspiration: `docs/btree-design-inspiration/01-Data-Structures.md`

---

**This completes Phase 1 (Data Layer) and provides core infrastructure for Phase 2 (Compiler). Take your time to get the event queue logic right - it's the most complex part.**

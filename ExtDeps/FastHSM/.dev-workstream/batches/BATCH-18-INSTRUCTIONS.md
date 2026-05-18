# BATCH-18: Hot Reload Manager + Test Fixes (TASK-G03 + BATCH-17 Fixes)

**Batch Number:** BATCH-18  
**Tasks:** TASK-G03 (Hot Reload Manager), BATCH-17 test fixes  
**Phase:** Tooling (P0)  
**Estimated Effort:** 4-5 hours  
**Priority:** HIGH (P0 Critical)  
**Dependencies:** BATCH-17 (Command Buffer Integration)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch combines:
1. **Critical fixes** from BATCH-17 (missing test coverage)
2. **Hot Reload Manager** implementation (TASK-G03) - full live reloading of HSM definitions

**Required Reading (IN ORDER):**
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **BATCH-17 Review:** `.dev-workstream/reviews/BATCH-17-REVIEW.md` - See test gaps
3. **Task Definition:** `.dev-workstream/GAP-TASKS.md` - See TASK-G03
4. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Section 4.1 (Hot Reload)
5. **Architect Decisions:** Section 4.1, responses to Q37-Q38 in design talk

### Source Code Location
- **Primary Work Area:** `src/Fhsm.Kernel/`
- **Test Project:** `tests/Fhsm.Tests/`

### Report Submission

**CRITICAL: You MUST submit a report for this batch.**

**Submit to:** `.dev-workstream/reports/BATCH-18-REPORT.md`  
**Template:** `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

---

## Context

### Part 1: BATCH-17 Test Fixes

BATCH-17 review identified insufficient test coverage. Only 1 of 3-4 required tests was implemented.

**Missing Tests:**
- Multiple actions writing to same buffer
- Command buffer lifecycle/reset behavior

### Part 2: Hot Reload Manager (TASK-G03)

From the design document (Section 4.1):

> **Hot reload is split into two modes:**
> 1. **Soft Reload** - Structure hash unchanged, parameters changed → Preserve instance state
> 2. **Hard Reset** - Structure hash changed → Clear runtime state, increment generation, restart

**Why This Matters:**
- **Dev Productivity:** Change state machine logic without restarting
- **Safety:** Structural changes force safe reset; parameter changes preserve state
- **Determinism:** Generation counter invalidates stale timers/references

---

## 🎯 Batch Objectives

1. Fix test coverage gaps from BATCH-17
2. Implement full hot reload system with hash-based versioning
3. Support both soft reload (parameters) and hard reset (structure)
4. Add comprehensive tests for reload scenarios

---

## ✅ Tasks

### Task 1: Fix Missing Tests from BATCH-17

**File:** `tests/Fhsm.Tests/Kernel/CommandBufferIntegrationTests.cs` (UPDATE)  

**Add 2 missing tests:**

#### Test 2: Multiple Actions Write to Same Buffer

```csharp
[Fact]
public void Multiple_Actions_Write_To_Same_Buffer()
{
    // Setup:
    // - Register 3 test actions that each write unique commands
    // - Create state machine with transition: A -> B
    //   - Exit action on A: writes command 0xAA
    //   - Transition action: writes command 0xBB  
    //   - Entry action on B: writes command 0xCC
    // - Trigger transition
    
    // Expected:
    // - Command buffer contains all 3 commands in order: AA, BB, CC
    // - BytesWritten = 3
    
    // Implementation notes:
    // - Use HsmBuilder to create state machine
    // - Register actions with [HsmAction] attributes
    // - Use HsmKernel.Update with command page
    // - Read commands from CommandPage.Data
}
```

#### Test 3: Command Buffer Lifecycle

```csharp
[Fact]
public void Command_Buffer_Used_Across_Multiple_Updates()
{
    // Setup:
    // - Create state machine with 2 states
    // - Each state has entry action that writes unique command
    
    // Scenario:
    // 1. Initialize to State A (writes command 0xAA)
    // 2. Read command buffer → verify 0xAA present
    // 3. Create NEW CommandPage (simulates buffer reset)
    // 4. Transition to State B (writes command 0xBB)
    // 5. Read command buffer → verify ONLY 0xBB present (not 0xAA)
    
    // This validates:
    // - Each update uses fresh command buffer
    // - No command leakage between updates
}
```

**Why These Tests Matter:**
- Test 2: Validates command accumulation in single update (exit + transition + entry)
- Test 3: Validates buffer lifecycle (no leakage between updates)

---

### Task 2: Implement HotReloadManager (TASK-G03)

**File:** `src/Fhsm.Kernel/HotReloadManager.cs` (NEW FILE)

**Requirements:**

#### 2.1 Core Class Structure

```csharp
using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Manages hot reload of HSM definitions.
    /// Supports soft reload (parameters) and hard reset (structure).
    /// </summary>
    public class HotReloadManager
    {
        private readonly Dictionary<uint, HsmDefinitionBlob> _loadedBlobs = new();
        
        public ReloadResult TryReload<TInstance>(
            uint machineId,
            HsmDefinitionBlob newBlob,
            Span<TInstance> instances)
            where TInstance : unmanaged
        {
            // Implementation
        }
        
        private unsafe void HardReset<TInstance>(
            ref TInstance instance,
            HsmDefinitionBlob newBlob)
            where TInstance : unmanaged
        {
            // Implementation
        }
    }
    
    public enum ReloadResult
    {
        NewMachine,    // First time loading this machine ID
        NoChange,      // Hashes match, no reload needed
        SoftReload,    // Parameters changed, state preserved
        HardReset,     // Structure changed, state cleared
    }
}
```

#### 2.2 TryReload Logic

**Algorithm:**

```
1. Check if machineId exists in _loadedBlobs
   - If not: Store newBlob, return ReloadResult.NewMachine

2. Compare hashes:
   - structureChanged = newBlob.Header.StructureHash != oldBlob.Header.StructureHash
   - parameterChanged = newBlob.Header.ParameterHash != oldBlob.Header.ParameterHash

3. If neither changed:
   - Return ReloadResult.NoChange

4. If structure changed:
   - Iterate instances span
   - For each instance where instance.Header.MachineId == machineId:
     - Call HardReset(ref instance, newBlob)
   - Store newBlob in _loadedBlobs
   - Return ReloadResult.HardReset

5. If only parameters changed:
   - Store newBlob in _loadedBlobs (instances keep running with old state)
   - Return ReloadResult.SoftReload
```

**Key Points:**
- **Soft Reload:** Only swaps blob, instances continue with existing state
- **Hard Reset:** Clears instance state, increments generation, restarts machine

#### 2.3 HardReset Implementation

**From Design Doc (Section 4.1):**

```csharp
private unsafe void HardReset<TInstance>(
    ref TInstance instance,
    HsmDefinitionBlob newBlob)
    where TInstance : unmanaged
{
    // Cast to InstanceHeader (first 16 bytes of any instance)
    fixed (void* ptr = &instance)
    {
        InstanceHeader* header = (InstanceHeader*)ptr;
        
        // 1. Increment generation (invalidates timers)
        header->Generation++;
        
        // 2. Clear runtime state
        header->Phase = InstancePhase.Idle;
        header->MicroStep = 0;
        header->QueueHead = 0;
        header->ConsecutiveClamps = 0;
        
        // 3. Update machine ID
        header->MachineId = (uint)newBlob.Header.StructureHash;
    }
    
    // 4. Clear tier-specific state
    // Note: This is tricky - we need to clear ActiveLeafIds, Timers, History, Queue
    // But layout differs per tier (64B, 128B, 256B)
    
    // Option 1: Use sizeof(TInstance) to determine tier and cast appropriately
    int instanceSize = sizeof(TInstance);
    
    if (instanceSize == 64)
    {
        fixed (void* ptr = &instance)
        {
            HsmInstance64* inst64 = (HsmInstance64*)ptr;
            ClearInstance64State(inst64);
        }
    }
    else if (instanceSize == 128)
    {
        fixed (void* ptr = &instance)
        {
            HsmInstance128* inst128 = (HsmInstance128*)ptr;
            ClearInstance128State(inst128);
        }
    }
    else if (instanceSize == 256)
    {
        fixed (void* ptr = &instance)
        {
            HsmInstance256* inst256 = (HsmInstance256*)ptr;
            ClearInstance256State(inst256);
        }
    }
}

private unsafe void ClearInstance64State(HsmInstance64* instance)
{
    // Clear active leaves (1 region)
    instance->ActiveLeafIds[0] = 0xFFFF;
    
    // Clear timers (1 timer)
    instance->TimerDeadlines[0] = 0;
    
    // Clear history (2 slots)
    instance->HistorySlots[0] = 0xFFFF;
    instance->HistorySlots[1] = 0xFFFF;
    
    // Clear event queue (reset counters)
    instance->QueueTail = 0;
    instance->EventCount = 0;
}

private unsafe void ClearInstance128State(HsmInstance128* instance)
{
    // Clear active leaves (2 regions)
    for (int i = 0; i < 2; i++)
        instance->ActiveLeafIds[i] = 0xFFFF;
    
    // Clear timers (2 timers)
    for (int i = 0; i < 2; i++)
        instance->TimerDeadlines[i] = 0;
    
    // Clear history (4 slots)
    for (int i = 0; i < 4; i++)
        instance->HistorySlots[i] = 0xFFFF;
    
    // Clear event queue
    instance->QueueTail = 0;
    instance->DeferredTail = 0;
    instance->EventCount = 0;
}

private unsafe void ClearInstance256State(HsmInstance256* instance)
{
    // Clear active leaves (4 regions)
    for (int i = 0; i < 4; i++)
        instance->ActiveLeafIds[i] = 0xFFFF;
    
    // Clear timers (4 timers)
    for (int i = 0; i < 4; i++)
        instance->TimerDeadlines[i] = 0;
    
    // Clear history (8 slots)
    for (int i = 0; i < 8; i++)
        instance->HistorySlots[i] = 0xFFFF;
    
    // Clear event queue
    instance->QueueTail = 0;
    instance->DeferredTail = 0;
    instance->EventCount = 0;
}
```

**Design Notes:**
- **Generation Counter:** Increment prevents stale timer references from firing
- **Phase Reset:** Set to `Idle` so next update starts fresh
- **MachineId Update:** Set to new StructureHash for tracking
- **Preserve:** User context (last 16-48 bytes) is NOT cleared - application data persists

---

### Task 3: Add Hot Reload Tests

**File:** `tests/Fhsm.Tests/Kernel/HotReloadTests.cs` (NEW FILE)

**Required Tests:**

#### Test 1: No Change Detection

```csharp
[Fact]
public void No_Reload_When_Hashes_Match()
{
    // Setup:
    // - Create HotReloadManager
    // - Create machine blob
    // - Call TryReload twice with same blob
    
    // Expected:
    // - First call: ReloadResult.NewMachine
    // - Second call: ReloadResult.NoChange
}
```

#### Test 2: Soft Reload Detection

```csharp
[Fact]
public void Soft_Reload_When_Only_Parameters_Change()
{
    // Setup:
    // - Create 2 blobs with SAME structure hash, DIFFERENT parameter hash
    // - Load first blob
    // - Create instance in active state
    // - Load second blob
    
    // Expected:
    // - TryReload returns ReloadResult.SoftReload
    // - Instance state PRESERVED (activeLeafIds unchanged)
    // - Manager stores new blob
}
```

#### Test 3: Hard Reset Detection

```csharp
[Fact]
public void Hard_Reset_When_Structure_Changes()
{
    // Setup:
    // - Create 2 blobs with DIFFERENT structure hash
    // - Load first blob, instance in state 5
    // - Load second blob
    
    // Expected:
    // - TryReload returns ReloadResult.HardReset
    // - Instance activeLeafIds cleared (0xFFFF)
    // - Generation incremented
    // - MachineId updated to new structure hash
}
```

#### Test 4: Generation Increment on Hard Reset

```csharp
[Fact]
public void Hard_Reset_Increments_Generation()
{
    // Setup:
    // - Instance with generation = 5
    // - Trigger hard reset
    
    // Expected:
    // - Instance.Header.Generation = 6
}
```

#### Test 5: Multiple Instances Hard Reset

```csharp
[Fact]
public void Hard_Reset_Affects_Only_Matching_MachineId()
{
    // Setup:
    // - Create 2 instances: MachineId = 100 and MachineId = 200
    // - Trigger hard reset for MachineId = 100
    
    // Expected:
    // - Instance 100: State cleared
    // - Instance 200: State unchanged
}
```

**Test Utilities:**

You'll need helper methods to create test blobs with specific hashes:

```csharp
private HsmDefinitionBlob CreateBlobWithHashes(ulong structureHash, ulong parameterHash)
{
    var header = new HsmDefinitionHeader
    {
        StructureHash = structureHash,
        ParameterHash = parameterHash,
        StateCount = 2,
        TransitionCount = 1,
    };
    
    var states = new StateDef[2];
    states[0] = new StateDef { ParentIndex = 0xFFFF };
    states[1] = new StateDef { ParentIndex = 0xFFFF };
    
    var transitions = new TransitionDef[1];
    transitions[0] = new TransitionDef 
    { 
        SourceStateIndex = 0, 
        TargetStateIndex = 1,
        EventId = 10 
    };
    
    return new HsmDefinitionBlob(
        header,
        states,
        transitions,
        Array.Empty<RegionDef>(),
        Array.Empty<GlobalTransitionDef>(),
        Array.Empty<ushort>(),
        Array.Empty<ushort>());
}
```

---

## 🧪 Testing Requirements

### Minimum Test Counts

- **CommandBufferIntegrationTests.cs:** 3 tests total (1 existing + 2 new)
- **HotReloadTests.cs:** 5 tests minimum

### Quality Standards

**Tests MUST verify:**
- ✅ Actual behavior, not just "code runs"
- ✅ All enum values of `ReloadResult`
- ✅ Generation counter increments
- ✅ State cleared vs preserved correctly
- ✅ Multiple instances handled correctly

**Tests must NOT:**
- ❌ Just check "object exists"
- ❌ Only verify happy path
- ❌ Skip edge cases (multiple instances, no change, etc.)

---

## 📊 Report Requirements

**You MUST submit a detailed report to `.dev-workstream/reports/BATCH-18-REPORT.md`.**

Use the template at `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`.

**Focus Areas:**

1. **Issues Encountered:**
   - Did the tier-specific casting in `HardReset` cause any problems?
   - Were there any unsafe code lifetime issues with fixed statements?
   - Did the generic constraint `where TInstance : unmanaged` work as expected?

2. **Design Decisions Made:**
   - How did you handle tier detection in `HardReset`?
   - Did you add any helper methods beyond spec?
   - How did you structure the test blobs (hash generation)?

3. **Code Improvements Identified:**
   - Could the tier-specific clearing be unified somehow?
   - Is there a cleaner way to cast between instance types?
   - Any suggestions for the `HotReloadManager` API?

4. **Testing Insights:**
   - What edge cases did you discover during testing?
   - Were there scenarios not covered in instructions?
   - Any flaky test concerns?

---

## 🎯 Success Criteria

This batch is DONE when:

### Functionality
- [ ] BATCH-17 missing tests added (2 tests)
- [ ] `HotReloadManager` class implemented
- [ ] `TryReload` method works for all 4 cases (new, no change, soft, hard)
- [ ] `HardReset` clears state for all 3 tiers (64B, 128B, 256B)
- [ ] Generation counter increments on hard reset

### Tests
- [ ] All 183 existing tests still pass
- [ ] 2 new command buffer tests pass
- [ ] 5 hot reload tests pass
- [ ] **Total: 190 tests passing**

### Report
- [ ] Report submitted to `.dev-workstream/reports/BATCH-18-REPORT.md`
- [ ] All sections completed (issues, decisions, improvements, testing)
- [ ] Test results included (full pass count)

---

## ⚠️ Common Pitfalls to Avoid

### Pitfall 1: Unsafe Pointer Lifetime

```csharp
❌ BAD:
InstanceHeader* header;
fixed (void* ptr = &instance)
{
    header = (InstanceHeader*)ptr; // Pointer escapes fixed block
}
header->Generation++; // CRASH - pointer invalid

✅ GOOD:
fixed (void* ptr = &instance)
{
    InstanceHeader* header = (InstanceHeader*)ptr;
    header->Generation++; // Use within fixed block
}
```

### Pitfall 2: Forgetting to Update Blob Registry

```csharp
❌ BAD:
if (structureChanged)
{
    // Reset instances...
    return ReloadResult.HardReset; // Forgot to update _loadedBlobs!
}

✅ GOOD:
if (structureChanged)
{
    // Reset instances...
    _loadedBlobs[machineId] = newBlob; // Store new blob
    return ReloadResult.HardReset;
}
```

### Pitfall 3: Not Testing All ReloadResult Cases

Make sure you have tests for:
- ✅ NewMachine (first load)
- ✅ NoChange (same hashes)
- ✅ SoftReload (parameter change only)
- ✅ HardReset (structure change)

---

## 📚 Reference Materials

### Design Documents
- **Section 4.1:** `docs/design/HSM-Implementation-Design.md` (lines 1996-2089)
- **Architect Responses:** Q37-Q38 in design talk (hot reload philosophy)
- **Task Definition:** `.dev-workstream/GAP-TASKS.md` - TASK-G03

### Existing Code to Study
- **Instance Structures:** `src/Fhsm.Kernel/Data/HsmInstance*.cs`
- **Instance Manager:** `src/Fhsm.Kernel/HsmInstanceManager.cs` (for initialization patterns)
- **Header Struct:** `src/Fhsm.Kernel/Data/InstanceHeader.cs`

### Related Batches
- **BATCH-17 Review:** `.dev-workstream/reviews/BATCH-17-REVIEW.md` (test gaps)
- **BATCH-04:** Event queue implementation (for tier-specific logic patterns)

---

## 💡 Implementation Tips

### Tip 1: Start with Test Helpers

Create blob generation helpers first - you'll use them in all tests.

### Tip 2: Implement Tier Clearing Carefully

The `ClearInstance*State` methods are repetitive but necessary. Each tier has different capacities:
- Tier 1 (64B): 1 region, 1 timer, 2 history slots
- Tier 2 (128B): 2 regions, 2 timers, 4 history slots
- Tier 3 (256B): 4 regions, 4 timers, 8 history slots

### Tip 3: Test Hard Reset Thoroughly

Hard reset is the most critical - ensure:
- Generation increments
- All arrays cleared (activeLeafIds, timers, history, queue)
- MachineId updated
- Phase reset to Idle

---

**Good luck! This is a substantial batch combining bug fixes with a critical new feature. Take your time, test thoroughly, and document your insights in the report.** 🚀

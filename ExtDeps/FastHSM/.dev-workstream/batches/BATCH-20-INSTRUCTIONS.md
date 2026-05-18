# BATCH-20: All P2 Tasks - Trace Tool, Validation, Fail-Safe, Paged Allocator, Registry (TASK-G08-G12)

**Batch Number:** BATCH-20  
**Tasks:** TASK-G08 (Trace Symbolication), TASK-G09 (Indirect Event Validation), TASK-G10 (Fail-Safe State), TASK-G11 (Paged Allocator), TASK-G12 (Bootstrapper & Registry)  
**Phase:** Tooling & Polish (P2 Complete)  
**Estimated Effort:** 8-10 hours  
**Priority:** P2 (Medium Priority - All Remaining P2 Tasks)  
**Dependencies:** BATCH-19 complete

---

## 📋 Onboarding

**Required Reading:**
1. **Task Definitions:** `.dev-workstream/GAP-TASKS.md` - TASK-G08 through G12
2. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Sections 4.3, 2.7, 3.6, 3.3, 2.3

**Source Code:** `src/Fhsm.Kernel/`, `src/Fhsm.Compiler/`, `tests/Fhsm.Tests/`

**Report:** `.dev-workstream/reports/BATCH-20-REPORT.md`

---

## Context

This batch completes all P2 (Medium Priority) tasks - tooling and polish features.

### TASK-G08: Trace Symbolication Tool
Convert binary trace records to human-readable logs using debug metadata.

### TASK-G09: Indirect Event Validation
Compiler validates events >16B are marked `IsIndirect` (ID-only).

### TASK-G10: Fail-Safe State Transition
Add fail-safe state for RTC infinite loop protection.

### TASK-G11: Command Buffer Paged Allocator
Implement multi-page command buffer system with lane reservations.

### TASK-G12: Bootstrapper & Registry
Implement definition registry with hot reload integration.

---

## ✅ Tasks

### Task 1: Trace Symbolication Tool (TASK-G08)

**File:** `src/Fhsm.Kernel/TraceSymbolicator.cs` (NEW)

**Requirements:**

```csharp
using System;
using System.Text;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Converts binary trace records to human-readable logs.
    /// </summary>
    public class TraceSymbolicator
    {
        private readonly MachineMetadata _metadata;
        
        public TraceSymbolicator(MachineMetadata metadata)
        {
            _metadata = metadata;
        }
        
        /// <summary>
        /// Symbolicate trace records to text.
        /// </summary>
        public string Symbolicate(ReadOnlySpan<TraceRecord> records)
        {
            var sb = new StringBuilder();
            
            foreach (ref readonly var record in records)
            {
                switch (record.OpCode)
                {
                    case TraceOpCode.StateEnter:
                        var stateName = _metadata.GetStateName(record.StateIndex);
                        sb.AppendLine($"[{record.Timestamp}] ENTER: {stateName}");
                        break;
                        
                    case TraceOpCode.StateExit:
                        stateName = _metadata.GetStateName(record.StateIndex);
                        sb.AppendLine($"[{record.Timestamp}] EXIT: {stateName}");
                        break;
                        
                    case TraceOpCode.Transition:
                        var fromState = _metadata.GetStateName(record.StateIndex);
                        var toState = _metadata.GetStateName(record.TargetStateIndex);
                        var eventName = _metadata.GetEventName(record.EventId);
                        sb.AppendLine($"[{record.Timestamp}] TRANSITION: {fromState} -> {toState} [{eventName}]");
                        break;
                        
                    case TraceOpCode.ActionExecuted:
                        var actionName = _metadata.GetActionName(record.ActionId);
                        sb.AppendLine($"[{record.Timestamp}] ACTION: {actionName}");
                        break;
                        
                    case TraceOpCode.GuardEvaluated:
                        var guardName = _metadata.GetActionName(record.GuardId);
                        var result = record.GuardResult != 0 ? "PASS" : "FAIL";
                        sb.AppendLine($"[{record.Timestamp}] GUARD: {guardName} -> {result}");
                        break;
                }
            }
            
            return sb.ToString();
        }
    }
}
```

**Note:** `MachineMetadata` already exists from BATCH-15 (visual demo). Reuse it.

---

### Task 2: Add Trace Symbolication Tests

**File:** `tests/Fhsm.Tests/Tooling/TraceSymbolicationTests.cs` (NEW)

```csharp
[Fact]
public void Symbolicate_StateEnter_ReturnsReadableLog()
{
    var metadata = new MachineMetadata();
    metadata.StateNames[5] = "Combat";
    
    var records = new TraceRecord[1];
    records[0] = new TraceRecord 
    { 
        OpCode = TraceOpCode.StateEnter, 
        StateIndex = 5,
        Timestamp = 1000 
    };
    
    var symbolicator = new TraceSymbolicator(metadata);
    var output = symbolicator.Symbolicate(records);
    
    Assert.Contains("[1000] ENTER: Combat", output);
}

[Fact]
public void Symbolicate_Transition_ShowsSourceTargetEvent()
{
    // Similar pattern for transition records
}

[Fact]
public void Symbolicate_MultipleRecords_ProducesOrderedLog()
{
    // Verify ordering, multiple record types
}
```

---

### Task 3: Indirect Event Validation (TASK-G09)

**File:** `src/Fhsm.Compiler/HsmGraphValidator.cs` (UPDATE)

**Add validation rule:**

```csharp
/// <summary>
/// Validate events >16B are marked IsIndirect.
/// Architect Directive 2.
/// </summary>
private void ValidateIndirectEvents(StateMachineGraph graph)
{
    foreach (var evt in graph.Events)
    {
        if (evt.PayloadSize > 16)
        {
            if (!evt.IsIndirect)
            {
                _errors.Add(new ValidationError
                {
                    Message = $"Event '{evt.Name}' has payload {evt.PayloadSize}B (>16B) but not marked IsIndirect. " +
                              "Large events must be ID-only (use IsIndirect=true).",
                    Severity = ErrorSeverity.Error
                });
            }
        }
        
        // Warn if deferred + indirect (can't defer ID-only events)
        if (evt.IsIndirect && evt.IsDeferred)
        {
            _warnings.Add(new ValidationError
            {
                Message = $"Event '{evt.Name}' is both IsIndirect and IsDeferred. " +
                          "ID-only events cannot be deferred (data not in queue).",
                Severity = ErrorSeverity.Warning
            });
        }
    }
}
```

**Call in Validate:**

```csharp
public static List<ValidationError> Validate(StateMachineGraph graph)
{
    var validator = new HsmGraphValidator();
    
    // ... existing validations ...
    
    validator.ValidateIndirectEvents(graph);
    
    return validator._errors;
}
```

---

### Task 4: Add Indirect Event Tests

**File:** `tests/Fhsm.Tests/Compiler/IndirectEventValidationTests.cs` (NEW)

```csharp
[Fact]
public void Event_Over16B_NotIndirect_Errors()
{
    var builder = new HsmBuilder("Test");
    
    // Assume builder has method to set payload size
    builder.Event("BigEvent", 1, payloadSize: 32, isIndirect: false);
    
    var graph = builder.Build();
    var errors = HsmGraphValidator.Validate(graph);
    
    Assert.Contains(errors, e => e.Message.Contains("BigEvent") && e.Message.Contains("IsIndirect"));
}

[Fact]
public void Event_Over16B_WithIndirect_Passes()
{
    var builder = new HsmBuilder("Test");
    builder.Event("BigEvent", 1, payloadSize: 32, isIndirect: true);
    
    var graph = builder.Build();
    var errors = HsmGraphValidator.Validate(graph);
    
    Assert.DoesNotContain(errors, e => e.Message.Contains("BigEvent"));
}

[Fact]
public void Event_Indirect_And_Deferred_Warns()
{
    var builder = new HsmBuilder("Test");
    builder.Event("ConflictEvent", 1, isIndirect: true, isDeferred: true);
    
    var graph = builder.Build();
    var errors = HsmGraphValidator.Validate(graph);
    
    // Should have warning
    Assert.Contains(errors, e => e.Message.Contains("ConflictEvent") && 
                                 e.Message.Contains("IsIndirect") && 
                                 e.Message.Contains("IsDeferred"));
}
```

**Note:** You may need to extend `HsmBuilder` event API to support `payloadSize`, `isIndirect`, `isDeferred` parameters.

---

### Task 5: Fail-Safe State (TASK-G10)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)

**Add fail-safe logic to RTC loop:**

```csharp
private static void ProcessRTCPhase(...)
{
    const int MaxRTCIterations = 100;
    InstanceHeader* header = (InstanceHeader*)instancePtr;
    ushort* activeLeafIds = GetActiveLeafIds(instancePtr, instanceSize, out int regionCount);
    
    int iteration = 0;
    while (iteration < MaxRTCIterations)
    {
        iteration++;

        TransitionDef? selectedTransition = SelectTransition(...);
        
        if (selectedTransition == null)
        {
            break; // Consumed
        }
        
        ExecuteTransition(...);
        
        break; // One transition per event
    }
    
    // NEW: Check if hit iteration limit (infinite loop)
    if (iteration >= MaxRTCIterations)
    {
        // Fail-safe: Force transition to state 0 (root/initial)
        activeLeafIds[0] = 0;
        header->Phase = InstancePhase.Idle;
        
        // Optional: Set error flag
        header->Flags |= InstanceFlags.Error;
    }
    else
    {
        // Normal path
        header->Phase = InstancePhase.Activity;
    }
}
```

**Add Error flag to InstanceFlags:**

**File:** `src/Fhsm.Kernel/Data/Enums.cs` (UPDATE)

```csharp
[Flags]
public enum InstanceFlags : byte
{
    None = 0,
    Active = 1 << 0,
    DebugTrace = 1 << 1,
    Error = 1 << 2,  // NEW: Fail-safe triggered
}
```

---

### Task 6: Add Fail-Safe Tests

**File:** `tests/Fhsm.Tests/Kernel/FailSafeTests.cs` (NEW)

```csharp
[Fact]
public void RTC_Loop_Hits_Limit_Triggers_FailSafe()
{
    // Setup: Create state machine with circular transitions (infinite loop)
    // A -> B on Event 1
    // B -> A on Event 1 (self-loop via guard always true)
    
    // Expected:
    // - After MaxRTCIterations, fail-safe triggers
    // - ActiveLeafId reset to 0
    // - Error flag set
}

[Fact]
public void Normal_Transition_Does_Not_Trigger_FailSafe()
{
    // Setup: Simple A -> B transition
    // Expected: No error flag, normal completion
}
```

---

### Task 7: Paged Command Buffer Allocator (TASK-G11)

**File:** `src/Fhsm.Kernel/Data/CommandPageAllocator.cs` (NEW)

```csharp
using System;
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Multi-page command buffer allocator.
    /// </summary>
    public unsafe class CommandPageAllocator
    {
        private readonly CommandPage[] _pages;
        private int _currentPage;
        
        public CommandPageAllocator(int pageCount = 4)
        {
            _pages = new CommandPage[pageCount];
            _currentPage = 0;
        }
        
        /// <summary>
        /// Get current command page.
        /// </summary>
        public ref CommandPage GetCurrentPage()
        {
            return ref _pages[_currentPage];
        }
        
        /// <summary>
        /// Allocate next page (if current is full).
        /// </summary>
        public bool TryAllocateNextPage()
        {
            if (_currentPage >= _pages.Length - 1)
                return false; // No more pages
            
            _currentPage++;
            return true;
        }
        
        /// <summary>
        /// Reset allocator for next frame.
        /// </summary>
        public void Reset()
        {
            _currentPage = 0;
            
            // Clear all pages
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new CommandPage();
            }
        }
        
        /// <summary>
        /// Get all used pages.
        /// </summary>
        public Span<CommandPage> GetUsedPages()
        {
            return _pages.AsSpan(0, _currentPage + 1);
        }
    }
}
```

**Note:** This is a simple implementation. Advanced lane reservation is P3.

---

### Task 8: Add Paged Allocator Tests

**File:** `tests/Fhsm.Tests/Kernel/CommandPageAllocatorTests.cs` (NEW)

```csharp
[Fact]
public void GetCurrentPage_ReturnsFirstPage()
{
    var allocator = new CommandPageAllocator(4);
    ref var page = ref allocator.GetCurrentPage();
    
    // Write to page
    page.Data[0] = 0xAA;
    
    // Verify same page on next call
    ref var page2 = ref allocator.GetCurrentPage();
    Assert.Equal(0xAA, page2.Data[0]);
}

[Fact]
public void TryAllocateNextPage_AdvancesToNextPage()
{
    var allocator = new CommandPageAllocator(4);
    
    ref var page1 = ref allocator.GetCurrentPage();
    page1.Data[0] = 0xAA;
    
    bool success = allocator.TryAllocateNextPage();
    Assert.True(success);
    
    ref var page2 = ref allocator.GetCurrentPage();
    page2.Data[0] = 0xBB;
    
    // Verify pages are different
    Assert.Equal(0xBB, page2.Data[0]);
}

[Fact]
public void Reset_ClearsAllPages()
{
    var allocator = new CommandPageAllocator(4);
    
    ref var page = ref allocator.GetCurrentPage();
    page.Data[0] = 0xFF;
    
    allocator.Reset();
    
    ref var resetPage = ref allocator.GetCurrentPage();
    Assert.Equal(0, resetPage.Data[0]);
}

[Fact]
public void GetUsedPages_ReturnsCorrectSpan()
{
    var allocator = new CommandPageAllocator(4);
    
    allocator.GetCurrentPage(); // Page 0
    allocator.TryAllocateNextPage(); // Page 1
    allocator.GetCurrentPage(); // Still page 1
    
    var used = allocator.GetUsedPages();
    Assert.Equal(2, used.Length); // Pages 0 and 1
}
```

---

### Task 9: Definition Registry & Bootstrapper (TASK-G12)

**File:** `src/Fhsm.Kernel/HsmDefinitionRegistry.cs` (NEW)

```csharp
using System;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Registry for HSM definitions with hot reload support.
    /// </summary>
    public class HsmDefinitionRegistry
    {
        private readonly Dictionary<string, HsmDefinitionBlob> _definitions = new();
        private readonly HotReloadManager _hotReload = new();
        
        /// <summary>
        /// Register a definition by name.
        /// </summary>
        public void Register(string name, HsmDefinitionBlob blob)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Name cannot be null or empty", nameof(name));
            
            if (blob == null)
                throw new ArgumentNullException(nameof(blob));
            
            _definitions[name] = blob;
        }
        
        /// <summary>
        /// Get a definition by name.
        /// </summary>
        public HsmDefinitionBlob? Get(string name)
        {
            return _definitions.TryGetValue(name, out var blob) ? blob : null;
        }
        
        /// <summary>
        /// Try hot reload for a definition.
        /// </summary>
        public ReloadResult TryReload<TInstance>(
            string name,
            HsmDefinitionBlob newBlob,
            Span<TInstance> instances)
            where TInstance : unmanaged
        {
            if (!_definitions.TryGetValue(name, out var oldBlob))
            {
                Register(name, newBlob);
                return ReloadResult.NewMachine;
            }
            
            uint machineId = oldBlob.Header.StructureHash;
            var result = _hotReload.TryReload(machineId, newBlob, instances);
            
            if (result == ReloadResult.HardReset || result == ReloadResult.SoftReload)
            {
                _definitions[name] = newBlob;
            }
            
            return result;
        }
        
        /// <summary>
        /// Get all registered definition names.
        /// </summary>
        public IEnumerable<string> GetAllNames()
        {
            return _definitions.Keys;
        }
    }
}
```

---

### Task 10: Add Registry Tests

**File:** `tests/Fhsm.Tests/Kernel/DefinitionRegistryTests.cs` (NEW)

```csharp
[Fact]
public void Register_And_Get_Definition()
{
    var registry = new HsmDefinitionRegistry();
    var blob = CreateDummyBlob();
    
    registry.Register("TestMachine", blob);
    var retrieved = registry.Get("TestMachine");
    
    Assert.NotNull(retrieved);
    Assert.Equal(blob.Header.StructureHash, retrieved.Header.StructureHash);
}

[Fact]
public void Get_NonExistent_ReturnsNull()
{
    var registry = new HsmDefinitionRegistry();
    var retrieved = registry.Get("DoesNotExist");
    
    Assert.Null(retrieved);
}

[Fact]
public void TryReload_NewMachine_Registers()
{
    var registry = new HsmDefinitionRegistry();
    var blob = CreateDummyBlob();
    
    var instances = new HsmInstance64[1];
    var result = registry.TryReload("NewMachine", blob, instances.AsSpan());
    
    Assert.Equal(ReloadResult.NewMachine, result);
    Assert.NotNull(registry.Get("NewMachine"));
}

[Fact]
public void TryReload_StructureChange_TriggersHardReset()
{
    var registry = new HsmDefinitionRegistry();
    var blob1 = CreateDummyBlob(0x1111, 0xAAAA);
    var blob2 = CreateDummyBlob(0x2222, 0xAAAA); // Different structure
    
    registry.Register("Machine", blob1);
    
    var instances = new HsmInstance64[1];
    instances[0].Header.MachineId = 0x1111;
    
    var result = registry.TryReload("Machine", blob2, instances.AsSpan());
    
    Assert.Equal(ReloadResult.HardReset, result);
}

private HsmDefinitionBlob CreateDummyBlob(uint structHash = 0x1111, uint paramHash = 0xAAAA)
{
    var blob = new HsmDefinitionBlob();
    blob.Header.StructureHash = structHash;
    blob.Header.ParameterHash = paramHash;
    return blob;
}
```

---

## 🧪 Testing Requirements

**Minimum Tests:**
- TraceSymbolicationTests: 3 tests
- IndirectEventValidationTests: 3 tests
- FailSafeTests: 2 tests
- CommandPageAllocatorTests: 4 tests
- DefinitionRegistryTests: 4 tests
- **Total: ~16 new tests**

**Quality Standards:**
- Trace symbolication: Verify readable output format
- Indirect validation: Verify all error/warning cases
- Fail-safe: Verify error flag set, state reset
- Allocator: Verify page cycling, reset
- Registry: Verify registration, retrieval, hot reload integration

---

## 🎯 Success Criteria

**Functionality:**
- [ ] Trace symbolication tool working
- [ ] Indirect event validation in compiler
- [ ] Fail-safe state on RTC loop limit
- [ ] Paged command buffer allocator
- [ ] Definition registry with hot reload
- [ ] All 5 P2 tasks complete

**Tests:**
- [ ] All 203 existing tests pass
- [ ] 16 new tests pass
- [ ] **Target: ~219 tests passing**

**Report:**
- [ ] Report submitted with all sections

---

## 📚 Reference Materials

**Task Definitions:** `.dev-workstream/GAP-TASKS.md` (lines 382-561)  
**Design Sections:** 4.3 (Trace), 2.7 (Events), 3.6 (Fail-Safe), 3.3 (Buffers), 2.3 (Registry)

---

## ⚠️ Common Pitfalls

**Pitfall 1: Trace symbolication without metadata**
- Need `MachineMetadata` from BATCH-15
- Verify state/event/action names populated

**Pitfall 2: Infinite loop testing**
- Creating true infinite loop is tricky
- May need to manually construct circular graph

**Pitfall 3: Registry null checks**
- Validate name not null/empty
- Handle missing definitions gracefully

---

Good luck! This batch completes all P2 tasks - tooling and polish. 🚀

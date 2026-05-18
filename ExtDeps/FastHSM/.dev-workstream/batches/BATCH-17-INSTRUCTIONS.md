# BATCH-17: Command Buffer Integration (TASK-G02)

**Assigned:** 2026-01-12  
**Priority:** P0 (Critical)  
**Estimated Effort:** 3-4 hours  
**Prerequisite:** BATCH-16 (Global Transitions) ✅

---

## Objective

Integrate the command buffer system into the kernel's action/guard execution pipeline. This is a **critical architectural change** that enables deterministic replay, deferred side effects, and proper separation of HSM logic from external state mutations.

**Current State:** Actions receive `(void* instance, void* context, ushort eventId)` and can directly mutate external state.

**Target State:** Actions receive `(void* instance, void* context, void* commandsPtr)` and write side effects to a command buffer for later processing.

---

## Context & Design

### Why This Matters

From the design document (Section 3.3):

> **Command Buffer Pattern:** Actions do NOT directly modify external state. Instead, they write "commands" (e.g., `PlaySound`, `SpawnEntity`) to a fixed-size buffer. The caller processes these commands after the HSM update completes.

**Benefits:**
1. **Deterministic Replay:** Commands can be logged and replayed for debugging
2. **Deferred Execution:** Side effects happen after HSM logic completes
3. **Thread Safety:** Commands can be processed on a different thread
4. **Testability:** Actions can be tested by inspecting command buffer contents

### Architect's Decision (Q2)

> **Decision:** Fixed 4KB page size for command buffers. Simple, allocator-friendly, sufficient for most use cases.

### Current Implementation Status

**Already Implemented (BATCH-01):**
- `HsmCommandWriter` (`src/Fhsm.Kernel/Data/HsmCommandWriter.cs`) - ref struct for writing commands
- `CommandPage` (`src/Fhsm.Kernel/Data/CommandPage.cs`) - 4KB fixed-size buffer

**Missing:**
- Integration into kernel execution pipeline
- Updated action/guard signatures
- Source generator changes
- Example updates

---

## Tasks

### Task 1: Update Kernel Core to Create and Pass Command Writer

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs`

**Changes Required:**

1. **Add command buffer parameter to `UpdateBatchCore`:**

```csharp
private static unsafe void UpdateBatchCore(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    void* contextPtr,
    float deltaTime,
    void* commandPagePtr)  // NEW
{
    // ... existing phase state machine ...
}
```

2. **Create `HsmCommandWriter` at the start of each update:**

```csharp
// At the start of UpdateBatchCore:
ref CommandPage commandPage = ref Unsafe.AsRef<CommandPage>(commandPagePtr);
var cmdWriter = new HsmCommandWriter(commandPage, capacity: 4080);
```

3. **Pass command writer to all action invocations:**

Update these methods to accept and forward `ref HsmCommandWriter`:
- `ExecuteEntryActions` (Phase 2)
- `ExecuteTransition` (Phase 3 - transition action)
- `ExecuteActivities` (Phase 4)

**Example for `ExecuteEntryActions`:**

```csharp
private static void ExecuteEntryActions(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    void* contextPtr,
    ushort* path,
    int pathLength,
    ref HsmCommandWriter cmdWriter)  // NEW
{
    for (int i = 0; i < pathLength; i++)
    {
        ushort stateId = path[i];
        ref readonly var state = ref definition.GetState(stateId);
        
        if (state.EntryActionId != 0)
        {
            InvokeAction(definition, state.EntryActionId, instancePtr, contextPtr, ref cmdWriter);
        }
    }
}
```

4. **Update `InvokeAction` signature:**

```csharp
private static void InvokeAction(
    HsmDefinitionBlob definition,
    ushort actionId,
    byte* instancePtr,
    void* contextPtr,
    ref HsmCommandWriter cmdWriter)  // Changed from ushort eventId
{
    var dispatcher = HsmActionDispatcher.GetActionDispatcher(actionId);
    if (dispatcher != null)
    {
        fixed (HsmCommandWriter* cmdPtr = &cmdWriter)
        {
            dispatcher(instancePtr, contextPtr, cmdPtr);
        }
    }
}
```

**Note:** Guards should NOT receive the command writer (they are read-only). Keep `EvaluateGuard` signature unchanged:

```csharp
private static bool EvaluateGuard(
    ushort guardId,
    byte* instancePtr,
    void* contextPtr,
    ushort eventId)  // Guards keep eventId
{
    // ... unchanged ...
}
```

---

### Task 2: Update Public Kernel API

**File:** `src/Fhsm.Kernel/HsmKernel.cs`

**Changes Required:**

1. **Add command buffer parameter to `Update` methods:**

```csharp
public static unsafe void Update<TInstance, TContext>(
    HsmDefinitionBlob definition,
    ref TInstance instance,
    in TContext context,
    float deltaTime,
    ref CommandPage commandPage)  // NEW
    where TInstance : unmanaged
    where TContext : unmanaged
{
    fixed (void* instPtr = &instance)
    fixed (void* ctxPtr = &context)
    fixed (void* cmdPtr = &commandPage)
    {
        HsmKernelCore.UpdateBatchCore(
            definition,
            (byte*)instPtr,
            sizeof(TInstance),
            ctxPtr,
            deltaTime,
            cmdPtr);
    }
}
```

2. **Add overload for backward compatibility (optional):**

If you want to maintain backward compatibility for simple examples that don't need commands:

```csharp
public static unsafe void Update<TInstance, TContext>(
    HsmDefinitionBlob definition,
    ref TInstance instance,
    in TContext context,
    float deltaTime)
    where TInstance : unmanaged
    where TContext : unmanaged
{
    var dummyPage = new CommandPage();
    Update(definition, ref instance, context, deltaTime, ref dummyPage);
}
```

---

### Task 3: Update Source Generator

**File:** `src/Fhsm.SourceGen/HsmActionGenerator.cs`

**Changes Required:**

1. **Update action delegate signature:**

Change from:
```csharp
delegate*<void*, void*, ushort, void>
```

To:
```csharp
delegate*<void*, void*, void*, void>
```

2. **Update generated dispatcher code:**

Change from:
```csharp
[UnmanagedCallersOnly]
public static void MyAction_Wrapper(void* instance, void* context, ushort eventId)
{
    // ... cast and call user method ...
}
```

To:
```csharp
[UnmanagedCallersOnly]
public static void MyAction_Wrapper(void* instance, void* context, void* commandsPtr)
{
    ref HsmCommandWriter writer = ref Unsafe.AsRef<HsmCommandWriter>(commandsPtr);
    // ... cast and call user method with writer ...
}
```

3. **Update user-facing action signature:**

User actions should now be:
```csharp
[HsmAction("MyAction")]
public static void MyAction(ref MyContext context, ref HsmCommandWriter commands)
{
    commands.TryWriteCommand(/* ... */);
}
```

**Note:** Guards remain unchanged (no command writer):
```csharp
[HsmGuard("MyGuard")]
public static bool MyGuard(ref MyContext context, ushort eventId)
{
    return context.SomeCondition;
}
```

---

### Task 4: Update Examples

#### 4.1 Traffic Light Example

**File:** `examples/Fhsm.Examples.Console/TrafficLightExample.cs`

Update all action methods to use the new signature:

**Before:**
```csharp
[HsmAction("OnEnterRed")]
public static void OnEnterRed(ref TrafficLightContext context, ushort eventId)
{
    Console.WriteLine("🔴 RED - Stop!");
}
```

**After:**
```csharp
[HsmAction("OnEnterRed")]
public static void OnEnterRed(ref TrafficLightContext context, ref HsmCommandWriter commands)
{
    Console.WriteLine("🔴 RED - Stop!");
    // For now, direct console writes are acceptable in examples
    // In production, you'd write: commands.TryWriteCommand(new LogCommand("RED - Stop!"));
}
```

Update the main loop to pass a command page:

```csharp
var commandPage = new CommandPage();

while (true)
{
    HsmKernel.Update(blob, ref instance, context, 0.016f, ref commandPage);
    
    // Process commands (if any)
    ProcessCommands(ref commandPage);
    
    Thread.Sleep(16);
}
```

#### 4.2 Visual Demo

**File:** `demos/Fhsm.Demo.Visual/Actions.cs`

Update all action methods:

**Before:**
```csharp
[HsmAction("MoveToTarget")]
public static void MoveToTarget(ref AgentContext context, ushort eventId)
{
    // ... movement logic ...
}
```

**After:**
```csharp
[HsmAction("MoveToTarget")]
public static void MoveToTarget(ref AgentContext context, ref HsmCommandWriter commands)
{
    // ... movement logic ...
    // Could write: commands.TryWriteCommand(new MoveCommand(targetPos));
}
```

**File:** `demos/Fhsm.Demo.Visual/Systems/BehaviorSystem.cs`

Update the kernel update call:

```csharp
public void Update(float deltaTime)
{
    var commandPage = new CommandPage();
    
    foreach (var agent in _agents)
    {
        HsmKernel.Update(
            _definition,
            ref agent.Instance,
            agent.Context,
            deltaTime,
            ref commandPage);
    }
}
```

---

### Task 5: Add Tests

**File:** `tests/Fhsm.Tests/Kernel/CommandBufferIntegrationTests.cs` (NEW)

Create comprehensive tests:

```csharp
using System;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    public unsafe class CommandBufferIntegrationTests
    {
        // Test 1: Action receives command writer and can write to it
        [Fact]
        public void Action_Receives_CommandWriter_And_Can_Write()
        {
            // Setup: Create a simple state machine with one state + entry action
            // The action should write a test command to the buffer
            // Verify: Command buffer contains the written command
        }
        
        // Test 2: Multiple actions write to the same buffer
        [Fact]
        public void Multiple_Actions_Write_To_Same_Buffer()
        {
            // Setup: Transition with exit action + transition action + entry action
            // Each action writes a unique command
            // Verify: All 3 commands are in the buffer in correct order
        }
        
        // Test 3: Command buffer resets between updates
        [Fact]
        public void Command_Buffer_Resets_Between_Updates()
        {
            // Setup: Run update twice with same command page
            // First update writes commands
            // Clear buffer manually
            // Second update writes different commands
            // Verify: Only second update's commands remain
        }
        
        // Test 4: Guard does NOT receive command writer
        [Fact]
        public void Guard_Does_Not_Receive_CommandWriter()
        {
            // This is more of a compile-time check
            // Verify that guards still use old signature (eventId)
            // and can be evaluated without command buffer
        }
    }
}
```

**Implementation Notes:**

For testing, you'll need to:
1. Register test actions that write known commands
2. Use `HsmCommandWriter.TryWriteCommand` with test command structs
3. Read back commands from the `CommandPage` to verify

**Example test command:**

```csharp
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct TestCommand
{
    [FieldOffset(0)] public ushort CommandType;
    [FieldOffset(2)] public ushort Value;
}
```

---

## Design References

**Key Design Document Sections:**
- **Section 3.3:** Command Buffer Pattern
- **Section 2.2:** Action Signature (Table 2.2)
- **Section 4.2:** Deterministic Replay (depends on command buffer)

**Architect's Critical Decisions:**
- **Q2 (Command Buffer Page Size):** Fixed 4KB
- **Q9 (Action Signature):** Void* core + generic wrappers (thin shim pattern)

**Related Data Structures:**
- `HsmCommandWriter` (ref struct) - already implemented
- `CommandPage` (4KB buffer) - already implemented

---

## Acceptance Criteria

### Functional Requirements
- [ ] Actions receive `void* commandsPtr` instead of `ushort eventId`
- [ ] Guards still receive `ushort eventId` (read-only, no command buffer)
- [ ] `HsmKernel.Update` accepts `ref CommandPage` parameter
- [ ] Source generator emits correct new signature
- [ ] Traffic Light example compiles and runs with new signature
- [ ] Visual demo compiles and runs with new signature

### Test Requirements
- [ ] All existing tests still pass
- [ ] New test: Action writes command to buffer
- [ ] New test: Multiple actions write to same buffer
- [ ] New test: Command buffer lifecycle

### Code Quality
- [ ] No breaking changes to guard signatures
- [ ] Backward compatibility overload (optional, but recommended)
- [ ] Clear comments explaining command buffer lifecycle
- [ ] No performance regression (command writer is a ref struct, zero allocation)

---

## Submission Requirements

### Report Structure

Submit your report to `.dev-workstream/reports/BATCH-17-REPORT.md` using the template at `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`.

**Focus Areas for Your Report:**

1. **Challenges Encountered:**
   - Did the source generator changes cause any unexpected issues?
   - Were there any edge cases in the kernel pipeline where command writers needed special handling?
   - Did the ref struct nature of `HsmCommandWriter` cause any lifetime/pinning issues?

2. **Design Decisions Made:**
   - How did you handle the command buffer lifecycle (creation, reset, disposal)?
   - Did you implement backward compatibility overload? Why or why not?
   - How did you structure the test command types?

3. **Code Improvements Identified:**
   - Are there opportunities to simplify the command writer API?
   - Could the kernel pipeline be refactored to reduce command writer passing overhead?
   - Any suggestions for better command buffer debugging/inspection?

4. **Testing Insights:**
   - What edge cases did you discover during testing?
   - Are there additional test scenarios that would improve coverage?
   - Did you find any existing tests that needed updates?

---

## Getting Started

### Step 1: Understand the Command Buffer API

Read the existing implementation:
- `src/Fhsm.Kernel/Data/HsmCommandWriter.cs`
- `src/Fhsm.Kernel/Data/CommandPage.cs`

Key methods:
- `HsmCommandWriter.TryWriteCommand<T>(T command)` - writes a command
- `HsmCommandWriter.Reset()` - clears the buffer
- `CommandPage` - just a 4KB byte array with a header

### Step 2: Start with Kernel Core

Begin with `HsmKernelCore.cs`:
1. Add `commandPagePtr` parameter to `UpdateBatchCore`
2. Create `HsmCommandWriter` at the start
3. Update `InvokeAction` signature
4. Thread command writer through all action call sites

### Step 3: Update Public API

Modify `HsmKernel.cs` to expose the new signature.

### Step 4: Update Source Generator

This is the trickiest part. The generator needs to:
1. Change the function pointer type
2. Update the wrapper signature
3. Pass `ref HsmCommandWriter` to user methods

### Step 5: Fix Examples

Update Traffic Light and Visual Demo to compile with new signatures.

### Step 6: Write Tests

Create comprehensive tests to validate the integration.

---

## Notes

- **Ref Struct Lifetime:** `HsmCommandWriter` is a ref struct. It cannot be stored in fields or captured by lambdas. It must be passed by reference and live only on the stack.
- **Performance:** This change should have **zero allocation overhead**. The command page is allocated once, and the writer is a stack-only ref struct.
- **Backward Compatibility:** Consider adding an overload that creates a dummy command page internally for simple use cases.
- **Guards vs Actions:** Guards remain read-only. They do NOT receive the command writer. Only actions write commands.

---

## Questions?

If you encounter design ambiguities:
1. Check the design document (Section 3.3, Section 2.2)
2. Review the existing `HsmCommandWriter` implementation
3. Document your decision in the report

**Remember:** The goal is to enable deterministic, testable side effects. Actions should write commands, not mutate external state directly.

Good luck! 🚀

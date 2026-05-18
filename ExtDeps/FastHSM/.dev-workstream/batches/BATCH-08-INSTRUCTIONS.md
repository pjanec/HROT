# BATCH-08: Kernel Entry Point & Phase Management

**Batch Number:** BATCH-08  
**Tasks:** TASK-K01 (Kernel Entry Point)  
**Phase:** Phase 3 - Kernel (START)  
**Estimated Effort:** 2-3 days

---

## Context

**Phase 2 (Compiler) complete!** We can now build state machines and emit `HsmDefinitionBlob`.

**Phase 3 (Kernel):** Implement the runtime execution engine that processes instances.

This batch: Core kernel infrastructure with the **Thin Shim Pattern** (Architect Directive 1).

**Related Task:**
- [TASK-K01](../TASK-DEFINITIONS.md#task-k01-kernel-entry-point) - Kernel Entry Point

---

## Architecture: The Thin Shim Pattern

**Problem:** Generic `UpdateBatch<TInstance, TContext>` causes code bloat (JIT compiles for each type pair).

**Solution (Architect Q9):**
```
Generic Wrapper (inlined) → Void* Core (compiled once)
```

**Why:** Non-generic core compiled once. Generic wrapper inlined, zero overhead. I-Cache efficient.

---

## Task 1: Kernel Core (Void* Implementation)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (NEW)

**Non-generic core with void* pointers.**

```csharp
using System;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Non-generic kernel core. Compiled once, no generic expansion.
    /// Uses void* for type erasure.
    /// </summary>
    internal static unsafe class HsmKernelCore
    {
        /// <summary>
        /// Process instances through one tick.
        /// </summary>
        /// <param name="definition">State machine definition</param>
        /// <param name="instancePtr">Pointer to instance array</param>
        /// <param name="instanceCount">Number of instances</param>
        /// <param name="instanceSize">Size of each instance (64/128/256)</param>
        /// <param name="contextPtr">Context pointer (user data)</param>
        /// <param name="deltaTime">Time delta for this tick</param>
        internal static void UpdateBatchCore(
            HsmDefinitionBlob definition,
            void* instancePtr,
            int instanceCount,
            int instanceSize,
            void* contextPtr,
            float deltaTime)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (instancePtr == null) throw new ArgumentNullException(nameof(instancePtr));
            if (instanceCount <= 0) return;
            
            // Process each instance
            for (int i = 0; i < instanceCount; i++)
            {
                byte* instPtr = (byte*)instancePtr + (i * instanceSize);
                InstanceHeader* header = (InstanceHeader*)instPtr;
                
                // Skip instances with invalid phase or wrong definition
                if (!ValidateInstance(header, definition))
                {
                    continue;
                }
                
                // Process based on current phase
                ProcessInstancePhase(
                    definition,
                    instPtr,
                    instanceSize,
                    contextPtr,
                    deltaTime,
                    header);
            }
        }
        
        private static bool ValidateInstance(InstanceHeader* header, HsmDefinitionBlob definition)
        {
            // Check if instance belongs to this definition
            if (header->MachineId != definition.Header.StructureHash)
            {
                return false;
            }
            
            // Check phase is valid
            if (header->Phase < InstancePhase.Idle || header->Phase > InstancePhase.Activity)
            {
                return false;
            }
            
            return true;
        }
        
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
                    // Nothing to do, waiting for external trigger
                    break;
                    
                case InstancePhase.Entry:
                    // Process entry actions (will be implemented in later batch)
                    // For now, advance to RTC
                    header->Phase = InstancePhase.RTC;
                    break;
                    
                case InstancePhase.RTC:
                    // Run-to-completion loop (will be implemented in later batch)
                    // For now, advance to Activity
                    header->Phase = InstancePhase.Activity;
                    break;
                    
                case InstancePhase.Activity:
                    // Execute activities (will be implemented in later batch)
                    // For now, return to Idle
                    header->Phase = InstancePhase.Idle;
                    break;
            }
        }
    }
}
```

---

## Task 2: Public Kernel API (Generic Wrapper)

**File:** `src/Fhsm.Kernel/HsmKernel.cs` (NEW)

**Generic wrapper with aggressive inlining.**

```csharp
using System;
using System.Runtime.CompilerServices;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Public API for HSM kernel execution.
    /// Generic wrapper that inlines to void* core.
    /// </summary>
    public static class HsmKernel
    {
        /// <summary>
        /// Process batch of instances through one tick.
        /// ARCHITECT DIRECTIVE 1: Thin shim pattern with AggressiveInlining.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void UpdateBatch<TInstance, TContext>(
            HsmDefinitionBlob definition,
            Span<TInstance> instances,
            in TContext context,
            float deltaTime)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            if (instances.Length == 0) return;
            
            // Pin and get pointers
            fixed (TInstance* instPtr = instances)
            fixed (TContext* ctxPtr = &context)
            {
                // Call non-generic core
                HsmKernelCore.UpdateBatchCore(
                    definition,
                    instPtr,
                    instances.Length,
                    sizeof(TInstance),
                    ctxPtr,
                    deltaTime);
            }
        }
        
        /// <summary>
        /// Overload for single instance.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Update<TInstance, TContext>(
            HsmDefinitionBlob definition,
            ref TInstance instance,
            in TContext context,
            float deltaTime)
            where TInstance : unmanaged
            where TContext : unmanaged
        {
            fixed (TInstance* instPtr = &instance)
            fixed (TContext* ctxPtr = &context)
            {
                HsmKernelCore.UpdateBatchCore(
                    definition,
                    instPtr,
                    1,
                    sizeof(TInstance),
                    ctxPtr,
                    deltaTime);
            }
        }
        
        /// <summary>
        /// Trigger state machine to start processing from Idle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Trigger<TInstance>(ref TInstance instance)
            where TInstance : unmanaged
        {
            fixed (TInstance* ptr = &instance)
            {
                InstanceHeader* header = (InstanceHeader*)ptr;
                
                // Only trigger if idle
                if (header->Phase == InstancePhase.Idle)
                {
                    header->Phase = InstancePhase.Entry;
                }
            }
        }
    }
}
```

**CRITICAL:** Both methods marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]` per Architect Directive 1.

---

## Task 3: Phase Transition Logic

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Implement proper phase transition rules:

1. **Idle** → (external trigger) → **Entry**
2. **Entry** → (after entry actions) → **RTC**
3. **RTC** → (after transition processing) → **Activity**
4. **Activity** → (after activities) → **Idle**

For now, use placeholder logic (actual implementations in later batches).

---

## Task 4: Tests

**File:** `tests/Fhsm.Tests/Kernel/KernelEntryTests.cs` (NEW)

**Minimum 15 tests:**

### API Tests (5)
1. UpdateBatch processes multiple instances
2. Update processes single instance
3. Trigger sets phase to Entry (if Idle)
4. Trigger ignores non-Idle phases
5. Empty batch handled gracefully

### Phase Management (5)
6. Idle phase stays idle (no external trigger)
7. Entry phase advances to RTC
8. RTC phase advances to Activity
9. Activity phase returns to Idle
10. Invalid phase skipped

### Validation (3)
11. Wrong MachineId skipped
12. Null definition throws
13. Null instance pointer throws

### Generic Wrapper (2)
14. Works with HsmInstance64
15. Works with HsmInstance128, HsmInstance256

---

## Implementation Notes

### Thin Shim Pattern Verification

**How to verify inlining:**

1. **JIT Disassembly (Release mode):**
   ```csharp
   // Should see direct call to UpdateBatchCore with no wrapper overhead
   ```

2. **Code size comparison:**
   - Non-generic core: ~1KB compiled code
   - Each generic wrapper: Minimal (just pointer fixup)

### Pointer Arithmetic

**Instance iteration:**
```csharp
for (int i = 0; i < count; i++)
{
    byte* instPtr = (byte*)basePtr + (i * instanceSize);
    InstanceHeader* header = (InstanceHeader*)instPtr;
    // Process...
}
```

**Size detection:**
```csharp
int tier = instanceSize switch
{
    64 => 1,
    128 => 2,
    256 => 3,
    _ => throw new ArgumentException("Invalid instance size")
};
```

### Phase State Machine

```
     ┌─────────┐
     │  Idle   │ ◄──────────┐
     └────┬────┘            │
          │ Trigger()       │
          ▼                 │
     ┌─────────┐            │
     │  Entry  │            │
     └────┬────┘            │
          │ (entry actions) │
          ▼                 │
     ┌─────────┐            │
     │   RTC   │            │
     └────┬────┘            │
          │ (transitions)   │
          ▼                 │
     ┌─────────┐            │
     │Activity │            │
     └────┬────┘            │
          │ (activities)    │
          └─────────────────┘
```

---

## Success Criteria

- [ ] TASK-K01 completed
- [ ] HsmKernelCore (non-generic) implemented
- [ ] HsmKernel (generic wrapper) with AggressiveInlining
- [ ] Phase transition logic works
- [ ] 15+ tests, all passing
- [ ] Compiles without warnings
- [ ] Report submitted

---

## Reference

- **Task:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) TASK-K01
- **Design:** `docs/design/HSM-Implementation-Design.md` Section 3.1 (Entry Point)
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` Q9 (Thin Shim), Directive 1

**Report to:** `.dev-workstream/reports/BATCH-08-REPORT.md`

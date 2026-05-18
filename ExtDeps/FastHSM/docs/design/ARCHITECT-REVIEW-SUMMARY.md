# Architect Review Summary - FastHSM Implementation

**Review Date:** 2026-01-11  
**Reviewer:** System Architect  
**Status:** ✅ FULLY APPROVED

---

## Executive Summary

The HSM implementation design has been **fully approved** with specific modifications and directives. All 10 open questions have been resolved. No blockers remain for any implementation phase.

**Overall Assessment:** Production-ready, VM-grade design ready for immediate implementation.

---

## Critical Issues Identified & Resolved

### 1. Tier 1 Event Queue Fragmentation
**Problem:** Original recommendation (Option A: separate physical queues for 3 priority classes) is mathematically impossible for Tier 1. A single 24-byte event cannot fit in 32 bytes split 3 ways (32/3 = 10 bytes per queue).

**Resolution:**
- **Tier 1 (64B):** Single shared FIFO queue. Interrupt events can evict oldest Normal events when full.
- **Tier 2/3 (128B/256B):** Hybrid strategy with one reserved slot for Interrupt (24B) + shared ring buffer for Normal/Low.

**Files Updated:**
- `HsmInstance64`, `HsmInstance128`, `HsmInstance256` structs in Implementation Design

---

### 2. History Slot Hot Reload Instability
**Problem:** If history slots are assigned by name or declaration order, adding a new state alphabetically before existing states shifts slot indices, causing hot reload to read wrong/garbage history data.

**Resolution:**
History slots **MUST** be assigned in **StableID sort order** (GUID-based), never by name or declaration order.

**Files Updated:**
- `AssignHistorySlots()` method in Section 2.6 (Flattener)
- Added explicit stability constraint documentation

---

## Implementation Directives (MANDATORY)

### Directive 1: Thin Shim Pattern (Q9)
**Requirement:** Use void* core with generic inlined wrapper to prevent I-cache bloat from template expansion.

**Pattern:**
```csharp
// 1. Non-generic core (compiled once)
private static void UpdateSingleCore(
    HsmDefinitionBlob definition,
    void* instancePtr,
    void* contextPtr, // void*, no generics
    ...)
{
    // Heavy logic here
}

// 2. Generic shim (inlined, zero overhead)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void UpdateBatch<TContext>(...)
    where TContext : unmanaged
{
    fixed (TContext* ctx = &context)
    {
        UpdateSingleCore(definition, inst, ctx, ...);
    }
}
```

**Critical:** The `[MethodImpl(MethodImplOptions.AggressiveInlining)]` attribute is **MANDATORY**.

---

### Directive 2: ID-Only Event Validation
**Requirement:** Compiler must validate that events with payload > 16 bytes are marked `IsIndirect` and warn if they're also deferrable.

**Reasoning:** Large payloads must live in blackboard (persistent), not command buffers (ephemeral). Deferring an ID-only event pointing to ephemeral memory causes dangling references.

**Implementation:** 
```csharp
private void ValidateIndirectEvents(BuilderMachine machine)
{
    foreach (var eventDef in machine.EventDefinitions.Values)
    {
        if (eventDef.PayloadSize > 16)
        {
            if (!eventDef.IsIndirect)
                Error("Must be marked IsIndirect");
            if (eventDef.IsDeferrable)
                Warning("May cause dangling references");
        }
    }
}
```

---

### Directive 3: RNG Access Tracking
**Requirement:** Guards marked `[HsmGuard(UsesRNG=true)]` must have debug-only access count tracking for replay validation.

**Implementation:**
- Debug builds increment `debugAccessCount` on each `HsmRng.NextFloat()` call
- Replay validator compares access counts per frame to detect determinism drift
- Sidecar component stores counts to avoid changing instance memory layout

---

### Directive 4: Stable Slot Sorting
**Requirement:** History slots MUST be assigned based on StableID sort order.

**Implementation:**
```csharp
var historyStates = machine.StatesByName.Values
    .Where(s => s.HasHistory)
    .OrderBy(s => s.StableId) // CRITICAL: Stable sort order
    .ToList();
```

---

## Question Resolutions

| # | Question | Decision | Impact |
|---|----------|----------|--------|
| Q1 | Event Queue Layout | Modified Hybrid (tier-specific) | High - affects all instance structs |
| Q2 | Command Page Size | 4KB Fixed | Low - simple and standard |
| Q3 | History Slots | Compiler Pool + Stable Sort | High - prevents hot reload bugs |
| Q4 | RNG in Guards | Allow with Declaration | Medium - requires debug tracking |
| Q5 | Sync Transitions | Restricted (Reset to Initial) | Low - v1.0 simplicity |
| Q6 | Transition Cost | Structural Only | Low - compiler-provable |
| Q7 | Global Transitions | Separate Table | Low - performance optimization |
| Q8 | Trace Filtering | All Modes | Low - negligible overhead |
| Q9 | Action Signature | Void* Core + Wrappers | High - prevents I-cache bloat |
| Q10 | Local Storage | Scratch Registers | Low - 16B overhead acceptable |

---

## Files Modified

### 1. HSM-Implementation-Design.md
**Changes:**
- Updated event queue layouts for all three tiers (Tier 1 single queue, Tier 2/3 hybrid)
- Added `IsIndirect` flag to `EventFlags` enum
- Added `AssignHistorySlots()` method with stable sorting
- Added `ValidateIndirectEvents()` validation rule
- Updated `HsmKernel` to use thin shim pattern with `AggressiveInlining`
- Updated `HsmRng` with debug access tracking
- Added Section 1.5: Scratch Registers documentation
- Added Section 6: Architect Review & Approved Directives

### 2. HSM-Implementation-Questions.md
**Changes:**
- Marked all 10 questions as APPROVED ✅
- Added architect's rationale for each decision
- Added critical notes for Q1 (tier-specific) and Q3 (stable sorting)
- Updated status to "FULLY APPROVED"
- Added implementation directives summary

### 3. IMPLEMENTATION-PACKAGE-README.md
**Changes:**
- Updated readiness score to 100%
- Marked all phases as ready for immediate start
- Updated "Next Steps" section with green light for implementation
- Added critical additions summary

---

## Implementation Readiness

**Status:** 100% Ready ✅✅✅

**Approved Phases:**
- ✅ Phase 1: Data Layer (use tier-specific queue layouts)
- ✅ Phase 2: Compiler (implement stable sorting + indirect validation)
- ✅ Phase 3: Kernel (use thin shim pattern with AggressiveInlining)
- ✅ Phase 4: Tooling (implement RNG tracking in debug builds)

**Blockers:** NONE

**Critical Path:**
1. Begin Phase 1 immediately with updated struct layouts
2. Implement Directive 1 (thin shim) in Phase 3
3. Implement Directive 2 (indirect validation) in Phase 2
4. Implement Directive 3 (RNG tracking) in Phase 4
5. Implement Directive 4 (stable sorting) in Phase 2

---

## Key Architectural Decisions Confirmed

1. **Tier-Specific Design:** Different tiers have different memory/performance trade-offs
2. **Stability Over Alphabetical Order:** StableID-based sorting prevents hot reload bugs
3. **Performance Over Generics:** Thin shim pattern prevents I-cache bloat
4. **Safety Through Validation:** Compiler catches dangerous patterns (indirect events, slot conflicts)
5. **Debug vs. Release Separation:** RNG tracking only in debug, layout identical

---

## What Changed From Original Design

### Additions
- ✅ Tier-specific event queue strategies
- ✅ StableID-based history slot sorting algorithm
- ✅ Thin shim kernel dispatch pattern
- ✅ ID-only event validation rules
- ✅ Debug RNG access tracking mechanism
- ✅ Scratch register documentation

### No Breaking Changes
All changes are additive or refinements. No fundamental design revisions required.

---

## Next Actions

**For Implementation Team:**
1. Read this summary
2. Review updated `HSM-Implementation-Design.md` (especially Section 6)
3. Note mandatory attributes (`AggressiveInlining`)
4. Begin Phase 1 implementation using tier-specific struct layouts

**For QA:**
1. Plan tests for hot reload scenarios (history slot stability)
2. Plan tests for event queue overflow (priority eviction)
3. Plan replay validation tests (RNG access count comparison)

**For Project Manager:**
1. Remove all question-blocking risks from timeline
2. Green-light all phases for immediate start
3. Schedule mid-Phase-2 checkpoint to verify stable sorting implementation

---

**Document Status:** ✅ Complete  
**Implementation Status:** APPROVED TO PROCEED IMMEDIATELY


# HSM Implementation Questions for Architect

**Version:** 1.1.0  
**Date:** 2026-01-11  
**Status:** ✅ APPROVED - All Questions Resolved

**Review Completed By:** System Architect  
**Review Date:** 2026-01-11

---

## Purpose

This document contains specific implementation questions that arose during the detailed design phase. Each question includes:
- Context explaining why the decision matters
- Impact analysis showing what changes depending on the answer
- Recommendation from the tech lead
- Space for architect's decision

---

## Q1: Event Queue Physical Layout

### Context
The design specifies fixed 24-byte events with three priority classes (Interrupt, Normal, Low). We need to decide the physical memory layout within the instance's event buffer.

### Options

**Option A: Separate Physical Queues**
```
EventBuffer[160 bytes]:
  [0-39]:   Interrupt queue (1-2 events)
  [40-119]: Normal queue (3-4 events)
  [120-159]: Low queue (1-2 events)
```

**Option B: Single Queue with Priority Bits**
```
EventBuffer[160 bytes]:
  Single ring buffer with priority field in each event
  Requires scanning/sorting on dequeue
```

**Option C: Hybrid (Priority Queues + Shared Overflow)**
```
EventBuffer[160 bytes]:
  [0-23]:   Reserved for Interrupt (1 event)
  [24-159]: Shared for Normal/Low (5-6 events)
```

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| CPU Cost | Lowest (fixed indices) | Highest (scan) | Medium |
| Memory Efficiency | Lowest (fragmentation) | Highest | Good |
| Determinism | Trivial | Complex | Simple |
| Implementation Complexity | Simple | Medium | Medium |

### Recommendation
**Option A** for Tier 1/2, **Option C** for Tier 3 (Hero).

**Rationale:** Crowd/Standard AI rarely needs complex priority handling. Heroes benefit from more flexible allocation while keeping interrupt lane reserved.

### Architect Decision
- [ ] Approve Option A (simple, all tiers)
- [x] **APPROVED: Modified Option C (hybrid, tier-specific)** ✅
- [ ] Choose Option B (flexible)
- [x] Custom: **Tier 1 uses single queue (math constraint), Tier 2/3 use hybrid**

**Notes:**
**ARCHITECT RULING:** Option A is mathematically impossible for Tier 1. A single 24B event cannot fit in 32B split 3 ways (10B per queue). 

**Approved Strategy:**
- **Tier 1 (64B):** Single shared FIFO. Interrupt events can overwrite oldest Normal events.
- **Tier 2/3 (128B/256B):** Reserved interrupt slot (24B) + shared ring for Normal/Low.

**Rationale:** Tier-specific approach balances memory constraints with priority safety.


---

## Q2: Command Buffer Page Size Strategy

### Context
The paged command buffer allocator needs to balance memory waste vs allocation overhead. The design suggests 4KB pages.

### Options

**Option A: Fixed 4KB for All Tiers**
```csharp
const int PageSize = 4096;
```

**Option B: Tier-Dependent Sizes**
```csharp
TierSize.Crowd_64B    → 2KB pages
TierSize.Standard_128B → 4KB pages
TierSize.Hero_256B     → 8KB pages
```

**Option C: Adaptive (Runtime Detection)**
```csharp
// Start with 2KB, grow to 8KB if usage pattern justifies
```

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| Memory Waste | Medium | Low | Lowest |
| Allocation Overhead | Low | Variable | Variable |
| Complexity | Lowest | Low | High |
| Predictability | Highest | High | Low |

### Recommendation
**Option A** (fixed 4KB).

**Rationale:** Modern systems have plenty of memory. The complexity of tier-dependent or adaptive sizing outweighs the benefit. Profile first, optimize later if needed.

### Architect Decision
- [x] **APPROVED: Option A (4KB fixed)** ✅
- [ ] Choose Option B (tier-dependent)
- [ ] Choose Option C (adaptive)

**Notes:**
**ARCHITECT RULING:** 4KB is standard, allocator-friendly, and simple. Complexity is the enemy of v1.0. Can optimize later if cache misses prove significant, but start simple.


---

## Q3: History Slot Allocation Strategy

### Context
History states require storing the last active leaf per composite. The design allocates slots at compile time, but the exact strategy affects both RAM usage and authoring constraints.

### Options

**Option A: Global Pool (Compiler Assigns)**
```
Machine has 16 history slots total (in tier budget).
Compiler assigns slot indices to composites.
Conflict detection: ensures no two active composites share a slot.
```

**Option B: Per-Composite Inline**
```
Each composite with history allocates its own slot inline.
No conflict possible, but wastes RAM for inactive composites.
```

**Option C: Lazy Allocation Pool**
```
Slots allocated on first history write (runtime).
Can fail if pool exhausted.
Requires generation tracking.
```

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| RAM Efficiency | High | Low | High |
| Determinism | Perfect | Perfect | Can fail at runtime |
| Validation Complexity | High | None | Medium |
| Authoring Constraints | Medium (must be exclusive) | None | None |

### Recommendation
**Option A** (compiler-assigned global pool).

**Rationale:** Matches the design philosophy (deterministic, validated, fixed-size RAM). The validator can detect conflicts early, and RAM is not wasted on unused slots if the compiler is smart.

### Architect Decision
- [x] **APPROVED: Option A (compiler pool) + STABILITY CONSTRAINT** ✅
- [ ] Choose Option B (per-composite inline)
- [ ] Choose Option C (lazy allocation)

**Notes:**
**ARCHITECT RULING:** Option A is correct, but with a CRITICAL additional constraint:

**STABILITY REQUIREMENT:** History slots MUST be assigned based on **StableID sort order** (GUID-based), NEVER by name or declaration order.

**Hot Reload Risk:** If slots are assigned alphabetically by name, adding a new state "A_New" before existing "B_Old" will shift B_Old's slot index from 2→3. Running instances will read wrong/garbage history data.

**Fix:** Compiler must sort states by `StableId` before assigning slot indices. This maximizes hot-reload compatibility by keeping indices stable even when states are reordered.


---

## Q4: RNG Access in Guards

### Context
Guards evaluate transition preconditions. The question is whether they should have access to the RNG for probabilistic guards (e.g., "AttackChance < 0.5").

### Options

**Option A: Allow RNG in Guards**
```csharp
public static bool AttackChance(
    in Context ctx,
    ref HsmRng rng)  // ← RNG passed
{
    return rng.NextFloat() < 0.5f;
}
```
- Guards consume RNG state
- Replay must capture exact evaluation order
- Powerful but requires discipline

**Option B: Disallow RNG in Guards**
```csharp
public static bool AttackChance(in Context ctx)
{
    return ctx.GetBlackboard().ComputedChance < 0.5f;
}
```
- Guards remain pure
- RNG only in actions/activities
- Forces explicit state management

**Option C: Allow RNG but Require Declaration**
```csharp
[HsmGuard(UsesRNG = true)]
public static bool AttackChance(...)
```
- Validator warns about RNG usage
- Debug builds track RNG access count
- Best of both worlds?

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| Expressiveness | Highest | Lowest | High |
| Replay Safety | Complex | Trivial | Good |
| Determinism | Delicate | Robust | Good (with tracking) |
| Authoring Friction | Low | Medium | Low |

### Recommendation
**Option C** (allow with explicit declaration).

**Rationale:** Real AI often needs probabilistic guards (e.g., "sometimes flee, sometimes stand ground"). Forcing it into actions is awkward. With explicit declaration and debug tracking, we get power without sacrificing debuggability.

### Architect Decision
- [ ] Approve Option A (allow freely)
- [x] **APPROVED: Option C (allow with declaration)** ✅
- [ ] Choose Option B (disallow)

**Notes:**
**ARCHITECT RULING:** Pragmatism wins. Real AI needs probabilistic guards ("sometimes flee, sometimes stand ground").

**Implementation Requirement:** Guards marked `[HsmGuard(UsesRNG=true)]` must have compiler inject hidden `AccessCount` increment in **DEBUG BUILDS ONLY** to maintain replay validation contract.

**Replay Validation:** Debug builds track RNG access counts per frame. Replay validator compares counts to detect determinism drift.


---

## Q5: Synchronized Transitions: Target Flexibility

### Context
Synchronized transitions fire atomically across multiple regions. The question is what targets they can specify.

### Options

**Option A: Restricted Form (Re-enter Initial)**
```json
{
  "type": "synchronized",
  "source": "combat",
  "target": "combat",  // Re-enters the composite
  "trigger": "Staggered",
  "behavior": "reset_to_initial"
}
```
- All regions exit then re-enter their initial states
- Simple exit-set computation
- Covers ~90% of use cases (reset/interrupt patterns)

**Option B: Per-Region Targets**
```json
{
  "type": "synchronized",
  "source": "combat",
  "trigger": "Reload",
  "targets": {
    "MovementRegion": "cover",
    "WeaponRegion": "reloading",
    "PostureRegion": "crouched"
  }
}
```
- Arbitrary target per region
- Complex exit-set (must compute LCA per region)
- Maximum flexibility

**Option C: Hybrid (Allow Both)**
```json
// Simple form:
{ "type": "synchronized", "target": "initial" }

// Complex form:
{ "type": "synchronized", "targets": { ... } }
```

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| Expressiveness | Medium | Highest | High |
| Implementation Complexity | Low | High | Medium |
| Validation Difficulty | Low | High | Medium |
| Common-Case Ergonomics | Good | Verbose | Good |
| Edge-Case Support | Limited | Complete | Good |

### Recommendation
**Option A** for v1.0, plan **Option C** for v1.1+.

**Rationale:** The "reset to initial" pattern covers interrupt/stagger/death scenarios cleanly. Per-region targets are powerful but complex to validate (must ensure no region-crossing conflicts). Ship simple, validate demand, then add.

### Architect Decision
- [x] **APPROVED: Option A (v1.0 simple only)** ✅
- [ ] Choose Option B (full flexibility now)
- [ ] Choose Option C (hybrid now)

**Notes:**
**ARCHITECT RULING:** Start simple. The "reset to initial" pattern covers interrupt/stagger/death scenarios cleanly. 

**Rationale:** Per-region targets are powerful but complex to validate (must ensure no region-crossing conflicts, detect deadlocks/invalid states). This is a graph theory problem. Ship simple (90% case), validate demand, add later if needed.

---

## Q6: Transition Cost Computation (Validation Budget)

### Context
The validator checks "Max Transition Cost" to ensure no single transition blows the microstep budget. The question is what to include in the cost.

### Options

**Option A: Structural Only**
```
Cost = ExitDepth + EntryDepth + 1
Example: Leaf→Root→Leaf = 5 + 5 + 1 = 11 steps
```
- Simple, deterministic
- Ignores user code cost
- Can be validated at compile time

**Option B: Structural + Annotations**
```csharp
[HsmAction(Cost = 10)]  // Designer estimates cost
public static void ExpensiveAction(...) { }

Cost = ExitDepth + EntryDepth + 1 + Sum(AnnotatedCosts)
```
- More accurate
- Requires discipline
- Still compile-time

**Option C: Runtime Profiling**
```
Measure actual execution time in dev builds.
Compiler emits warnings if > threshold.
```
- Most accurate
- Requires tooling
- Can't enforce in strict mode

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| Accuracy | Low (structural only) | Medium | High |
| Compile-Time Enforceable | Yes | Yes | No |
| Authoring Burden | None | Medium | None |
| False Positives | Few | Some | None |
| False Negatives | Possible | Fewer | None |

### Recommendation
**Option A** for v1.0 (structural), add **Option B** (annotations) as optional in v1.1.

**Rationale:** Structural cost guarantees a baseline bound. User code cost is unknowable at compile time (depends on blackboard state, context). Runtime watchdogs handle pathological user code; structural limits prevent topology pathologies.

### Architect Decision
- [x] **APPROVED: Option A (structural only)** ✅
- [ ] Choose Option B (with annotations)
- [ ] Choose Option C (runtime profiling)

**Notes:**
**ARCHITECT RULING:** Anything else is guessing. Structural cost is the only metric the compiler can prove.

**Rationale:** User code cost is unknowable at compile time (depends on blackboard state, context). Runtime watchdogs handle pathological user code; structural limits prevent topology pathologies.

---

## Q7: Global Transition Table Encoding

### Context
Global interrupt transitions (e.g., "Death", "Cutscene") bypass normal hierarchy. The question is how to encode them efficiently in ROM.

### Options

**Option A: Separate Table (in Header)**
```
GlobalTransitionTableOffset → array of TransitionDef
Checked before per-state transitions.
```
- Clean separation
- Fast lookup (small table)
- Clear semantics

**Option B: Flag on Normal Transitions**
```
All transitions in main table.
IsGlobal flag marks them.
Kernel filters during resolution.
```
- No extra table
- Slower (must scan all transitions)
- Mixing concerns

**Option C: Virtual "Root" State with Transitions**
```
Create implicit "SuperRoot" state.
Global transitions are its children.
```
- Elegant (fits existing model)
- Slightly complex (synthesized state)

### Impact Analysis

| Aspect | Option A | Option B | Option C |
|--------|----------|----------|----------|
| ROM Size | +8 bytes header, +N×16 | Same | Same + 32 (SuperRoot) |
| Lookup Speed | O(G) (G=globals) | O(T) (T=all trans) | O(G) |
| Conceptual Clarity | Highest | Lowest | High |
| Implementation Complexity | Low | Low | Medium |

### Recommendation
**Option A** (separate table).

**Rationale:** Global transitions are semantically distinct (always active, highest priority). A small dedicated table (typically 1-5 entries) is clearer and faster than filtering a large main table.

### Architect Decision
- [x] **APPROVED: Option A (separate table)** ✅
- [ ] Choose Option B (flagged in main table)
- [ ] Choose Option C (SuperRoot)

**Notes:**
**ARCHITECT RULING:** Global interrupts (Death, Stun) are checked every tick. An O(G) scan of a tiny separate table (typically 1-5 entries) is much faster than filtering the main transition list.

**Rationale:** Semantic clarity + performance. Global transitions are architecturally distinct (always active, highest priority).

---

## Q8: Debug Trace Filtering Granularity

### Context
Debug tracing can generate large volumes of data (especially guard evaluations). We need to decide what filtering is available.

### Options

**Option A: Global On/Off**
```csharp
TraceBuffer.Enabled = true;  // All or nothing
```

**Option B: Per-Tier Filtering**
```csharp
TraceConfig.TierMask = TraceFlags.Transitions | TraceFlags.StateChanges;
// Can disable TraceFlags.Guards, TraceFlags.Activities
```

**Option C: Per-Entity Filtering**
```csharp
instance.Header.Flags |= InstanceFlags.DebugTrace;  // Per instance
```

**Option D: All of the Above**
```
Global enable/disable
+ Per-tier event-type masks
+ Per-entity flag
= Trace if: Global.Enabled && TierMask.Matches(event) && Instance.HasFlag
```

### Impact Analysis

| Aspect | Option A | Option B | Option C | Option D |
|--------|----------|----------|----------|----------|
| Flexibility | None | Medium | High | Highest |
| Overhead (disabled) | Zero | ~1 bit test | ~1 bit test | ~3 tests |
| Usability | Poor | Good | Good | Excellent |
| Complexity | Lowest | Low | Low | Medium |

### Recommendation
**Option D** (all filtering modes).

**Rationale:** The cost of 2-3 branch checks is negligible (single-digit nanoseconds). The debugging benefit of "trace only entity 42's guards" is enormous. Modern CPUs branch-predict these checks nearly perfectly.

### Architect Decision
- [ ] Approve Option A (global only)
- [ ] Choose Option B (tier filtering)
- [ ] Choose Option C (per-entity)
- [x] **APPROVED: Option D (all modes)** ✅

**Notes:**
**ARCHITECT RULING:** The cost of 2-3 branch checks is negligible (single-digit nanoseconds). The debugging benefit of "trace only entity 42's guards" is enormous. 

**Rationale:** Modern CPUs branch-predict these checks nearly perfectly. Usability wins massively.

---

## Q9: Action/Guard Signature: Blackboard Access

### Context
Actions and guards need access to the blackboard. The question is the signature pattern.

### Options

**Option A: Separate Generic Parameter**
```csharp
delegate void Action<TBlackboard, TContext>(
    ref TBlackboard blackboard,
    ref TContext context,
    ref HsmCommandWriter commands)
    where TBlackboard : unmanaged
    where TContext : unmanaged;
```
- Type-safe
- Generic instantiation per (TBlackboard, TContext) pair
- Potential I-cache bloat

**Option B: Blackboard via Context**
```csharp
interface IHsmContext
{
    ref TBlackboard GetBlackboard<TBlackboard>();
}

delegate void Action<TContext>(
    ref TContext context,
    ref HsmCommandWriter commands)
    where TContext : IHsmContext;
```
- Single generic parameter
- Requires interface (prevents `unmanaged` constraint)
- Runtime cast overhead

**Option C: Void Pointer (Non-Generic Core)**
```csharp
delegate* unmanaged[Cdecl]<void*, void*, void*, void>

// User writes generic wrapper:
public static void Attack_Wrapper(
    void* blackboardPtr,
    void* contextPtr,
    void* commandsPtr)
{
    ref var bb = ref *(Blackboard*)blackboardPtr;
    ref var ctx = ref *(Context*)contextPtr;
    ref var cmds = ref *(HsmCommandWriter*)commandsPtr;
    
    Attack(ref bb, ref ctx, ref cmds);
}
```
- Zero I-cache bloat (single implementation)
- Type-unsafe (requires wrappers)
- Most like the "thin shim" pattern from design

### Recommendation
**Option C** (void* core, generated wrappers).

**Rationale:** This is the pattern described in the design document ("thin shim"). The kernel is non-generic, source generator creates type-safe wrappers. Best performance, worst ergonomics—but the generator hides the ugliness.

### Architect Decision
- [ ] Choose Option A (generic delegates)
- [ ] Choose Option B (context interface)
- [x] **APPROVED: Option C (void* + wrappers)** ✅

**Notes:**
**ARCHITECT RULING:** This is the correct "Thin Shim" pattern from the design document. Best performance (zero I-cache bloat), acceptable ergonomics (generator hides ugliness).

**CRITICAL DIRECTIVE:** Generated wrappers MUST be marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to ensure the JIT eliminates call overhead.

**Pattern:**
```csharp
// Non-generic core (compiled once)
private static void UpdateSingleCore(void* instancePtr, void* contextPtr, ...)

// Generic shim (inlined, zero overhead)  
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void UpdateBatch<TContext>(...)
```

---

## Q10: Blackboard vs Extended State Storage

### Context
The design primarily stores data in the user-defined blackboard. The question is whether to support small per-state "local variables" inside the instance.

### Options

**Option A: Blackboard Only**
```
All data in user blackboard struct.
Instance only has timers, history, queues.
```
- Simple
- Requires designer to pre-allocate fields
- No per-state isolation

**Option B: Fixed Scratch Registers**
```
Instance has 4× int32 LocalRegisters.
States can use them (validator checks overlap).
```
- Zero blackboard cost
- Limited capacity
- Validator complexity

**Option C: Compiler-Allocated Local Blocks**
```
Compiler reserves N bytes per state (if declared).
Validator ensures total ≤ tier budget.
```
- Most flexible
- Complex allocation algorithm
- Hard to explain to users

### Recommendation
**Option B** (scratch registers) for v1.0.

**Rationale:** Simple counters/flags are common (loop counters, retry counts). 4 registers × 4 bytes = 16B overhead is acceptable. If more is needed, use blackboard. Option C is over-engineering for v1.0.

### Architect Decision
- [ ] Choose Option A (blackboard only)
- [x] **APPROVED: Option B (scratch registers)** ✅
- [ ] Choose Option C (compiler-allocated locals)

**Notes:**
**ARCHITECT RULING:** Simple counters/flags are common (loop counters, retry counts). 4 registers × 4 bytes = 16B overhead is acceptable.

**Rationale:** Blackboard is too slow for simple loop counters; compiler-alloc is over-engineering for v1.0. If more is needed, use blackboard.

---

## Summary Table

| Question | Approved Decision | Status | Notes |
|----------|------------------|--------|-------|
| Q1: Event Queue Layout | Modified Option C (tier-specific) | ✅ Approved | Tier 1: single queue; Tier 2/3: hybrid |
| Q2: Command Page Size | Option A (4KB fixed) | ✅ Approved | Simple, standard, optimize later |
| Q3: History Slot Allocation | Option A + Stability Constraint | ✅ Approved | MUST sort by StableID, not name |
| Q4: RNG in Guards | Option C (allow with declaration) | ✅ Approved | Debug tracking for replay validation |
| Q5: Synchronized Transitions | Option A (simple v1.0) | ✅ Approved | Reset to initial only |
| Q6: Transition Cost | Option A (structural) | ✅ Approved | Compiler-provable only |
| Q7: Global Transition Table | Option A (separate table) | ✅ Approved | O(G) faster than filtering |
| Q8: Trace Filtering | Option D (all modes) | ✅ Approved | Negligible overhead, huge value |
| Q9: Action Signature | Option C (void* + wrappers) | ✅ Approved | AggressiveInlining REQUIRED |
| Q10: Local State Storage | Option B (scratch registers) | ✅ Approved | 4 slots, 16B overhead acceptable |

---

## Architect Response Section

**Date:** 2026-01-11  
**Reviewed By:** System Architect

### Overall Assessment
- [ ] Approve all recommendations as-is
- [x] **Approve with modifications (see notes above)**
- [ ] Request design revision

### Critical Issues Identified
1. **Tier 1 Event Queue Fragmentation:** Math makes Option A impossible (24B event won't fit in 32B/3 = 10B). Resolved with tier-specific strategy.
2. **History Slot Hot Reload Stability:** Sorting by name/declaration order causes slot shifts. Resolved with StableID-based sorting.

### Critical Path Items (Originally Blocking)
1. ✅ **RESOLVED:** Q9 (Action signatures) - Use thin shim pattern with AggressiveInlining
2. ✅ **RESOLVED:** Q1 (Event queues) - Tier-specific hybrid strategy
3. ✅ **RESOLVED:** Q3 (History slots) - Compiler pool with stable sorting

### Implementation Directives
1. **Thin Shim Pattern:** Non-generic core with inlined generic wrapper (MANDATORY AggressiveInlining)
2. **ID-Only Validation:** Compiler must check IsIndirect flag for events with payload > 16B
3. **RNG Tracking:** Debug builds inject access count tracking for guards using RNG
4. **Stable Slot Sorting:** History slots MUST be assigned in StableID order, never name order

### Additional Guidance
Implementation design is excellent and production-ready. All questions resolved. No blockers remaining.

**Approved to proceed immediately with all phases.**

---

**Next Steps:**
1. ✅ Architect review complete
2. ✅ Tech lead updated implementation design
3. **→ BEGIN PHASE 1 IMPLEMENTATION**

**Document Status:** ✅ APPROVED - All Questions Resolved

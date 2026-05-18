# FastHSM Implementation - Task Definitions

**Purpose:** Atomic task definitions with unique IDs for tracking and reference.  
**Usage:** Batches reference these task IDs. Tracker marks IDs as done.

---

## Task ID Format

`TASK-<phase><number>` where:
- Phase: D=Data, C=Compiler, K=Kernel, T=Tooling, E=Examples
- Number: Sequential within phase

Example: `TASK-D01`, `TASK-C03`, `TASK-K05`

---

## Architecture Context

**Design Refs:**
- `docs/design/HSM-design-talk.md` - Requirements
- `docs/design/HSM-Implementation-Design.md` - Detailed spec
- `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Critical decisions
- `docs/btree-design-inspiration/` - Design patterns

**Core Principles:**
- Data-Oriented Design (ROM/RAM separation)
- Zero-Allocation Runtime
- Cache-Friendly (64B/128B/256B tiers)
- Deterministic Execution
- ECS-Compatible

---

## Phase D: Data Layer

### TASK-D01: ROM Enumerations
**Status:** ✅ DONE  
**Deliverable:** `Enums.cs` with 5 enumerations  
**Design Ref:** HSM-Implementation-Design.md §1.1

**Scope:**
- StateFlags (16 flags)
- TransitionFlags (16 flags, includes 4-bit priority)
- EventPriority (Interrupt/Normal/Low)
- InstancePhase (Idle/Entry/RTC/Activity)
- InstanceFlags (8 flags)

**Constraints:**
- Exact bit positions per spec
- Priority extraction from TransitionFlags
- Blittable types

---

### TASK-D02: ROM State Definition
**Status:** ✅ DONE  
**Deliverable:** `StateDef.cs` (32 bytes)  
**Design Ref:** HSM-Implementation-Design.md §1.1

**Scope:**
- ParentIndex, FirstChildIndex, NextSiblingIndex
- FirstTransitionIndex, TransitionCount
- Depth, Flags
- Action bindings (Entry/Exit/Activity/Timer)
- HistorySlotIndex

**Constraints:**
- Exactly 32 bytes (cache line optimization)
- LayoutKind.Explicit
- ushort indices (64K limit)

---

### TASK-D03: ROM Transition Definition
**Status:** ✅ DONE  
**Deliverable:** `TransitionDef.cs` (16 bytes)  
**Design Ref:** HSM-Implementation-Design.md §1.1, Arch Q6

**Scope:**
- SourceStateIndex, TargetStateIndex
- EventId, GuardId, ActionId
- Flags (includes priority)
- Cost (structural LCA distance)

**Constraints:**
- Exactly 16 bytes
- Priority in flags (4 bits)
- Cost is structural-only (Architect Q6)

---

### TASK-D04: ROM Region & Global Transition
**Status:** ✅ DONE  
**Deliverable:** `RegionDef.cs` (8B), `GlobalTransitionDef.cs` (16B)  
**Design Ref:** HSM-Implementation-Design.md §1.1, Arch Q7

**Scope:**
- RegionDef: InitialStateIndex, StateCount
- GlobalTransitionDef: Separate table for interrupts (Architect Q7)

**Constraints:**
- Fixed sizes
- Global transitions in separate table

---

### TASK-D05: RAM Instance Header
**Status:** ✅ DONE  
**Deliverable:** `InstanceHeader.cs` (16 bytes)  
**Design Ref:** HSM-Implementation-Design.md §1.3

**Scope:**
- MachineId, Generation, Phase, Flags
- RandomSeed (deterministic RNG)
- QueueHead, ActiveTail, DeferredTail
- CurrentDepth

**Constraints:**
- Exactly 16 bytes
- Common to all tiers

---

### TASK-D06: RAM Instance Tiers
**Status:** ✅ DONE  
**Deliverable:** `HsmInstance64/128/256.cs`  
**Design Ref:** HSM-Implementation-Design.md §1.3, Arch Q1

**Scope:**
- Tier 1 (64B): 2 regions, 2 timers, 4 history, 24B event buffer
- Tier 2 (128B): 4 regions, 4 timers, 8 history, 68B event buffer
- Tier 3 (256B): 8 regions, 8 timers, 16 history, 156B event buffer

**Constraints:**
- **ARCHITECT CRITICAL FIX (Q1):**
  - Tier 1: Single queue, interrupt overwrites oldest normal
  - Tier 2/3: 24B reserved slot + shared ring

---

### TASK-D07: Event Structure
**Status:** ✅ DONE  
**Deliverable:** `HsmEvent.cs` (24 bytes), `EventFlags` enum  
**Design Ref:** HSM-Implementation-Design.md §1.4

**Scope:**
- 8B header (EventId, Priority, Flags, Timestamp)
- 16B payload (inline or ID for indirection)
- EventFlags (IsDeferred, IsIndirect, IsConsumed)

**Constraints:**
- Exactly 24 bytes
- Fixed size (no variable payload)

---

### TASK-D08: Command Buffer
**Status:** ✅ DONE  
**Deliverable:** `CommandPage.cs` (4KB), `HsmCommandWriter.cs`  
**Design Ref:** HSM-Implementation-Design.md §1.4, Arch Q2

**Scope:**
- CommandPage: 4096 bytes (OS page size, Architect Q2)
- HsmCommandWriter: ref struct for zero-alloc writing

**Constraints:**
- Page size 4KB
- Writer is ref struct (stack-only)

---

### TASK-D09: Definition Blob Container
**Status:** ⚠️ NEEDS FIXES  
**Deliverable:** `HsmDefinitionHeader.cs` (32B), `HsmDefinitionBlob.cs`  
**Design Ref:** HSM-Implementation-Design.md §1.2

**Scope:**
- Header: Magic (0x4D534846), StructureHash, ParameterHash, counts
- Blob: sealed class, ReadOnlySpan<T> accessors, dispatch tables

**Constraints:**
- Blob must be sealed
- Arrays private readonly
- Expose ReadOnlySpan<T> only
- Include ActionIds[], GuardIds[]

**Current Issues:**
- Not sealed
- Arrays public
- Missing dispatch tables

---

### TASK-D10: Instance Manager
**Status:** ⚠️ PARTIAL  
**Deliverable:** `HsmInstanceManager.cs`  
**Design Ref:** HSM-Implementation-Design.md §1.3

**Scope:**
- Initialize (zero memory, set defaults)
- Reset (preserve MachineId/Seed, increment Generation)
- SelectTier (heuristics based on complexity)

**Constraints:**
- Tier thresholds: T1(≤8 states), T2(≤32), T3(rest)

---

### TASK-D11: Event Queue Operations
**Status:** ⚠️ PARTIAL  
**Deliverable:** `HsmEventQueue.cs`  
**Design Ref:** HSM-Implementation-Design.md §3.2, Arch Q1

**Scope:**
- TryEnqueue (tier-specific strategies)
- TryDequeue (priority-ordered)
- TryPeek, Clear, GetCount

**Constraints:**
- **MUST implement Architect's tier strategies (Q1)**
- Tier 1: Overwrite logic
- Tier 2/3: Reserved slot + ring

**Current Issues:**
- Ring doesn't enforce priority ordering within ring

---

### TASK-D12: Validation Helpers
**Status:** ⚠️ PARTIAL  
**Deliverable:** `HsmValidator.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.3

**Scope:**
- ValidateDefinition (magic, counts, structure)
- ValidateInstance (phase, IDs, consistency)
- IsValidStateId, IsValidTransitionId

**Constraints:**
- Catch common errors
- No circular parent chains

---

## Phase C: Compiler

### TASK-C01: Graph Node Structures
**Status:** ✅ DONE  
**Deliverable:** `StateNode.cs`, `TransitionNode.cs`, `RegionNode.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.1

**Scope:**
- StateNode: StableId (Guid), hierarchy, actions
- TransitionNode: Source/target, event, guard, action
- RegionNode: Orthogonal region container

**Constraints:**
- StableId for hot reload stability
- Mutable graph nodes

---

### TASK-C02: State Machine Graph Container
**Status:** ✅ DONE  
**Deliverable:** `StateMachineGraph.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.1

**Scope:**
- Root state
- State dictionary
- Event registry
- Function registrations

**Constraints:**
- Implicit __Root creation

---

### TASK-C03: Fluent Builder API
**Status:** ✅ DONE  
**Deliverable:** `HsmBuilder.cs`, `StateBuilder`, `TransitionBuilder`  
**Design Ref:** HSM-Implementation-Design.md §2.1

**Scope:**
- HsmBuilder: Entry point
- StateBuilder: Configure states
- TransitionBuilder: Configure transitions

**Constraints:**
- Fluent chaining
- Error handling (duplicate states, unknown events)

---

### TASK-C04: Graph Normalizer
**Status:** ✅ DONE  
**Deliverable:** `HsmNormalizer.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.2, Arch Q3

**Scope:**
- Assign FlatIndex (BFS)
- Compute Depth
- Resolve Initial States
- **Assign History Slots (CRITICAL: sort by StableId)**
- Compute Transition Ranges

**Constraints:**
- **ARCHITECT CRITICAL (Q3): History slots sorted by StableId (Guid)**
- BFS for cache locality

---

### TASK-C05: Graph Validator
**Status:** ✅ DONE  
**Deliverable:** `HsmGraphValidator.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.3

**Scope:**
- 20+ validation rules
- Structural (orphans, cycles)
- Transitions (valid targets)
- Functions (registered)
- Limits (depth, counts)

**Constraints:**
- Accumulate all errors
- Clear error messages

---

### TASK-C06: Graph Flattener
**Status:** ⚪ TODO  
**Deliverable:** `HsmFlattener.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.4

**Scope:**
- Convert StateNode → StateDef[]
- Convert TransitionNode → TransitionDef[]
- Build dispatch tables (ActionIds[], GuardIds[])
- Compute transition costs (LCA distance)

**Constraints:**
- Preserve FlatIndex order
- Transition cost structural-only (Architect Q6)

---

### TASK-C07: Blob Emitter
**Status:** ⚪ TODO  
**Deliverable:** `HsmEmitter.cs`  
**Design Ref:** HSM-Implementation-Design.md §2.5

**Scope:**
- Create HsmDefinitionBlob from flat arrays
- Compute StructureHash (topology)
- Compute ParameterHash (logic)
- Populate header

**Constraints:**
- StructureHash stable across renames
- ParameterHash changes when logic changes

---

## Phase K: Kernel

### TASK-K01: Kernel Entry Point
**Status:** ⚪ TODO  
**Deliverable:** `HsmKernel.cs`, `HsmKernelCore.cs`  
**Design Ref:** HSM-Implementation-Design.md §3.1, Arch Q9

**Scope:**
- UpdateBatch API (thin shim pattern)
- Void* core (non-generic)
- Generic wrapper (inlined)
- Phase management

**Constraints:**
- **Thin Shim Pattern (Architect Directive 1)**
- AggressiveInlining on wrapper

---

### TASK-K02: Timer Decrement
**Status:** ⚪ TODO  
**Deliverable:** Timer phase logic in kernel  
**Design Ref:** HSM-Implementation-Design.md §3.2

**Scope:**
- Decrement TimerDeadlines[] by deltaTime
- Enqueue timer event when deadline ≤ 0

**Constraints:**
- Phase 1 of tick

---

### TASK-K03: Event Processing
**Status:** ⚪ TODO  
**Deliverable:** Event phase logic in kernel  
**Design Ref:** HSM-Implementation-Design.md §3.2

**Scope:**
- Dequeue highest priority event
- Budget-gated catch-up
- Deferred event handling

**Constraints:**
- Priority ordering (Interrupt > Normal > Low)
- Budget prevents infinite loops

---

### TASK-K04: RTC Loop
**Status:** ⚪ TODO  
**Deliverable:** RTC phase logic in kernel  
**Design Ref:** HSM-Implementation-Design.md §3.3, Arch Q4

**Scope:**
- Bounded iteration
- Transition selection (priority, guards)
- Guard evaluation
- Fail-safe state

**Constraints:**
- Max iterations ~100
- RNG in guards allowed with attribute (Architect Q4)

---

### TASK-K05: LCA Algorithm
**Status:** ⚪ TODO  
**Deliverable:** LCA computation in kernel  
**Design Ref:** HSM-Implementation-Design.md §3.4

**Scope:**
- Find common ancestor
- Compute exit path (bottom-up)
- Compute enter path (top-down)

**Constraints:**
- Efficient algorithm
- Handle root transitions

---

### TASK-K06: Transition Execution
**Status:** ⚪ TODO  
**Deliverable:** Execution logic in kernel  
**Design Ref:** HSM-Implementation-Design.md §3.5, Arch Q3

**Scope:**
- Exit actions (bottom-up)
- Entry actions (top-down)
- History state restore
- Internal transitions

**Constraints:**
- History restore validates with IsAncestor (Architect Q3)
- Internal skips exit/entry

---

### TASK-K07: Activity Execution
**Status:** ⚪ TODO  
**Deliverable:** Activity phase logic in kernel  
**Design Ref:** HSM-Implementation-Design.md §3.5

**Scope:**
- Execute activity actions for active states
- Phase 4 of tick

**Constraints:**
- Only for states with ActivityAction

---

## Phase T: Tooling

### TASK-T01: Hot Reload Manager
**Status:** ⚪ TODO  
**Deliverable:** `HsmHotReloadManager.cs`  
**Design Ref:** HSM-Implementation-Design.md §4.1, Arch Q3, Q8

**Scope:**
- Compare hashes (Structure vs Parameter)
- Hard reset (structure change)
- Soft reload (parameter change)
- Instance migration

**Constraints:**
- Structure change → hard reset
- Parameter change → preserve state

---

### TASK-T02: Debug Trace Buffer
**Status:** ⚪ TODO  
**Deliverable:** `HsmTraceBuffer.cs`  
**Design Ref:** HSM-Implementation-Design.md §4.2, Arch Q8

**Scope:**
- Ring buffer (per-thread)
- Trace records (StateEntry, Transition, etc.)
- Filtering (by entity, event, mode)
- Export (binary format)

**Constraints:**
- Zero-allocation
- Thread-local
- All filtering modes enabled (Architect Q8)

---

## Phase E: Examples & Polish

### TASK-E01: Console Example
**Status:** ⚪ TODO  
**Deliverable:** `Fhsm.Examples.Console` working app  
**Design Ref:** N/A

**Scope:**
- Traffic light or enemy AI example
- Demonstrate all features
- README with explanation

**Constraints:**
- Runnable, clear output

---

### TASK-E02: Documentation
**Status:** ⚪ TODO  
**Deliverable:** API docs, guides  
**Design Ref:** N/A

**Scope:**
- XML doc comments
- Performance guide
- Architecture overview
- Migration guide

**Constraints:**
- No compiler warnings
- Clear for new users

---

## Task Dependencies

```
D01-D04 → D05-D08 → D09-D12
         ↓
C01-C03 → C04-C05 → C06-C07
                   ↓
K01 → K02-K03 → K04 → K05-K06 → K07
                      ↓
T01-T02
↓
E01-E02
```

---

## Quick Reference

**By Status:**
- ✅ DONE: D01-D08, C01-C05
- ⚠️ NEEDS FIXES: D09-D12
- ⚪ TODO: C06-C07, K01-K07, T01-T02, E01-E02

**Critical Architect Decisions:**
- D06: Event queue tier strategies (Q1)
- C04: History slot StableId sorting (Q3)
- K04: RNG in guards (Q4)
- C06: Structural-only cost (Q6)
- C06: Separate global table (Q7)
- T02: All trace modes (Q8)
- K01: Thin shim pattern (Q9)

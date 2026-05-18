# BATCH-02: RAM Instance Structures

**Batch Number:** BATCH-02  
**Phase:** Phase 1 - Data Layer  
**Estimated Effort:** 12-14 hours (1.5 days)  
**Priority:** HIGH (Blocks BATCH-03, BATCH-04)  
**Dependencies:** BATCH-01 (ROM Data Structures)

---

## 📋 Onboarding & Workflow

### Welcome Back!

You're now implementing the **RAM (Runtime) data structures** - the mutable state that runs on each entity. This is more complex than BATCH-01 because you'll implement **three tier variants** with different memory layouts.

**⚠️ CRITICAL:** This batch implements **Architect's Critical Fix #1** - tier-specific event queue strategies. Read the architect review carefully!

### Required Reading (IN ORDER - ~2.5 hours)

1. **Previous Review:** `.dev-workstream/reviews/BATCH-01-REVIEW.md` - Learn what "excellent" looks like (15 min)
2. **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - **CRITICAL FIX #1** about queue layouts (20 min)
3. **Implementation Design:** `docs/design/HSM-Implementation-Design.md` - Section 1.3 (Instance structures) (1.5 hours)
4. **BTree Inspiration:** `docs/btree-design-inspiration/01-Data-Structures.md` - See `BehaviorTreeState` (64B example) (30 min)

**⚠️ MUST READ:** Architect Review Section on "Tier 1 Event Queue Fragmentation Trap" - this is a critical fix you MUST implement correctly.

### Source Code Location

- **Primary Work Area:** `src/Fhsm.Kernel/Data/`
- **Test Project:** `tests/Fhsm.Tests/Data/`
- **Namespace:** `Fhsm.Kernel.Data`

### Report Submission

**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-02-REPORT.md`

**Use this template:**  
`.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-02-QUESTIONS.md`

---

## 🎯 Batch Objectives

This batch implements the **RAM (mutable state)** for HSM instances - the runtime data for each entity running a state machine.

**Why this matters:**
- These structs must be **exactly** the specified sizes (64B, 128B, 256B)
- Must be **cache-friendly** (power-of-2 sizes, aligned)
- Must be **blittable** (no managed references)
- Three tiers support different AI complexity levels

**What you're building:**
- InstanceHeader (16 bytes) - common header for all tiers
- HsmInstance64 (64 bytes) - Tier 1: Crowd AI
- HsmInstance128 (128 bytes) - Tier 2: Standard enemies
- HsmInstance256 (256 bytes) - Tier 3: Hero/Boss AI
- 25+ unit tests validating everything

**Key Challenge:** Tier-specific event queue strategies (Architect's critical fix)

---

## ⚠️ ARCHITECT'S CRITICAL FIX - READ CAREFULLY

From `docs/design/ARCHITECT-REVIEW-SUMMARY.md`:

### The Problem with Original Design
Original design suggested separate physical queues for 3 priority classes. **This is mathematically impossible for Tier 1:**

- Tier 1 has ~32 bytes for event queue
- Single event = 24 bytes
- 32 bytes ÷ 3 queues = 10 bytes per queue
- **10 bytes < 24 bytes = IMPOSSIBLE** ❌

### The Solution (MANDATORY)
**Tier-specific queue strategies:**

**Tier 1 (64B):** 
- Single shared FIFO queue
- Can hold 1 event max
- Priority events **overwrite** oldest Normal event if full

**Tier 2/3 (128B/256B):**
- Hybrid strategy
- Reserved interrupt slot (24B) + shared ring buffer for Normal/Low
- Interrupt events always have guaranteed space

**You MUST implement this correctly or the design is broken.**

---

## ✅ Tasks

### Task 1: Implement InstanceHeader Struct

**File:** `src/Fhsm.Kernel/Data/InstanceHeader.cs` (NEW FILE)

**Description:** Common 16-byte header shared by all tier variants.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Common instance header (16 bytes).
    /// Shared by all tier variants (64B, 128B, 256B).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct InstanceHeader
    {
        // === Identity (8 bytes) ===
        [FieldOffset(0)] public uint MachineId;         // DefinitionBlob structure hash
        [FieldOffset(4)] public uint RandomSeed;        // Deterministic RNG seed

        // === State (4 bytes) ===
        [FieldOffset(8)] public ushort Generation;      // Increments on hard reset
        [FieldOffset(10)] public InstanceFlags Flags;   // Status flags (1 byte)
        [FieldOffset(11)] public InstancePhase Phase;   // Current execution phase (1 byte)

        // === Execution Tracking (4 bytes) ===
        [FieldOffset(12)] public byte MicroStep;        // Current RTC microstep
        [FieldOffset(13)] public byte QueueHead;        // Event queue read cursor
        [FieldOffset(14)] public byte ActiveTail;       // Active queue write cursor
        [FieldOffset(15)] public byte DeferredTail;     // Deferred queue write cursor

        // Total: 16 bytes
    }
}
```

**Critical Rules:**
- **MUST** be exactly 16 bytes
- All cursor values are byte indices (for ring buffer navigation)
- `MachineId` matches `DefinitionBlob.StructureHash` for validation
- Add XML doc comments

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.3.1

**Tests Required:**
- ✅ Size is exactly 16 bytes
- ✅ Field offsets correct (test at least 3-4 fields)
- ✅ Can initialize and set all fields
- ✅ Default values work

---

### Task 2: Implement HsmInstance64 (Tier 1: Crowd)

**File:** `src/Fhsm.Kernel/Data/HsmInstance64.cs` (NEW FILE)

**Description:** Tier 1 instance for crowd/horde AI (exactly 64 bytes).

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Tier 1: Crowd AI (hordes, simple NPCs).
    /// Size: Exactly 64 bytes.
    /// 
    /// ARCHITECT NOTE (CRITICAL): Uses SINGLE SHARED QUEUE due to space constraints.
    /// Priority events overwrite oldest normal events if full.
    /// Math: 32 bytes / 3 queues = 10 bytes each, but 1 event = 24 bytes.
    /// Therefore, separate queues are mathematically impossible for Tier 1.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct HsmInstance64
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (4 bytes) ===
        // Max 2 orthogonal regions supported
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[2];

        // === Timers (8 bytes) ===
        // 2 timer slots × 4 bytes = tick deadlines
        [FieldOffset(24)] public fixed uint TimerDeadlines[2];

        // === History/Scratch (4 bytes) ===
        // 2 slots for history OR scratch registers (dual-purpose)
        // ARCHITECT NOTE: Can be used for simple counters/flags
        [FieldOffset(32)] public fixed ushort HistorySlots[2];

        // === Event Queue (28 bytes) - SINGLE SHARED QUEUE ===
        // ARCHITECT DECISION Q1: Tier 1 special case
        // Can hold 1 full event (24B) with 4B metadata
        // Priority logic: Interrupt events can evict oldest Normal event
        [FieldOffset(36)] public byte EventCount;       // Current count (max 1)
        [FieldOffset(37)] public byte Reserved1;        // Alignment
        [FieldOffset(38)] public ushort Reserved2;      // Future use
        [FieldOffset(40)] public fixed byte EventBuffer[24]; // 1 event (24B)

        // Total: 64 bytes (40 + 24 = 64)
    }
}
```

**Critical Rules:**
- **MUST** be exactly 64 bytes (verify with test!)
- `unsafe` keyword required for fixed buffers
- Event queue is SINGLE SHARED QUEUE (not 3 separate queues)
- Document the architect's reasoning in XML comments
- Max 2 regions, 2 timers, 2 history slots, 1 event capacity

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.3.2 (updated per architect review)

**Tests Required:**
- ✅ Size is exactly 64 bytes (CRITICAL)
- ✅ Field offsets correct
- ✅ Can access header
- ✅ Can access fixed arrays (ActiveLeafIds, TimerDeadlines, etc.)
- ✅ EventBuffer can hold 24 bytes

---

### Task 3: Implement HsmInstance128 (Tier 2: Standard)

**File:** `src/Fhsm.Kernel/Data/HsmInstance128.cs` (NEW FILE)

**Description:** Tier 2 instance for standard enemies/items (exactly 128 bytes).

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Tier 2: Standard enemies, items.
    /// Size: Exactly 128 bytes.
    /// 
    /// ARCHITECT NOTE: Uses HYBRID QUEUE strategy.
    /// One reserved slot for Interrupt events + shared ring for Normal/Low.
    /// [0-23] = Reserved for Interrupt (1 event)
    /// [24-67] = Shared ring for Normal/Low (2 events)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public unsafe struct HsmInstance128
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (8 bytes) ===
        // Max 4 orthogonal regions
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[4];

        // === Timers (16 bytes) ===
        // 4 timer slots
        [FieldOffset(24)] public fixed uint TimerDeadlines[4];

        // === History/Scratch (16 bytes) ===
        // 8 slots (dual-purpose)
        [FieldOffset(40)] public fixed ushort HistorySlots[8];

        // === Event Queue (72 bytes) - HYBRID QUEUE ===
        // ARCHITECT DECISION Q1: Reserved interrupt slot + shared ring
        // Metadata (4 bytes)
        [FieldOffset(56)] public byte InterruptSlotUsed;    // 0 or 1
        [FieldOffset(57)] public byte EventCount;           // Normal/Low count
        [FieldOffset(58)] public ushort Reserved1;          // Alignment
        
        // Queue data (68 bytes = 24B interrupt + 44B shared)
        // Layout: [0-23] Interrupt reserved, [24-67] Shared for Normal/Low (1-2 events)
        [FieldOffset(60)] public fixed byte EventBuffer[68];

        // Total: 128 bytes (60 + 68 = 128)
    }
}
```

**Critical Rules:**
- **MUST** be exactly 128 bytes
- HYBRID queue: interrupt slot + shared ring
- Max 4 regions, 4 timers, 8 history slots
- EventBuffer: first 24 bytes reserved for interrupt, rest shared

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.3.3 (updated per architect review)

**Tests Required:**
- ✅ Size is exactly 128 bytes (CRITICAL)
- ✅ Field offsets correct
- ✅ Can access all fixed arrays
- ✅ EventBuffer is 68 bytes (24 interrupt + 44 shared)

---

### Task 4: Implement HsmInstance256 (Tier 3: Hero)

**File:** `src/Fhsm.Kernel/Data/HsmInstance256.cs` (NEW FILE)

**Description:** Tier 3 instance for player/boss AI (exactly 256 bytes).

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Tier 3: Player characters, bosses.
    /// Size: Exactly 256 bytes.
    /// 
    /// ARCHITECT NOTE: Uses HYBRID QUEUE strategy.
    /// One reserved slot for Interrupt events + shared ring for Normal/Low.
    /// [0-23] = Reserved for Interrupt (1 event)
    /// [24-155] = Shared ring for Normal/Low (5-6 events)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    public unsafe struct HsmInstance256
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (16 bytes) ===
        // Max 8 orthogonal regions
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[8];

        // === Timers (32 bytes) ===
        // 8 timer slots
        [FieldOffset(32)] public fixed uint TimerDeadlines[8];

        // === History/Scratch (32 bytes) ===
        // 16 slots (dual-purpose)
        [FieldOffset(64)] public fixed ushort HistorySlots[16];

        // === Event Queue (160 bytes) - HYBRID QUEUE ===
        // ARCHITECT DECISION Q1: Reserved interrupt slot + shared ring
        // Metadata (4 bytes)
        [FieldOffset(96)] public byte InterruptSlotUsed;    // 0 or 1
        [FieldOffset(97)] public byte EventCount;           // Normal/Low count
        [FieldOffset(98)] public ushort Reserved1;          // Alignment
        
        // Queue data (156 bytes = 24B interrupt + 132B shared)
        // Layout: [0-23] Interrupt reserved, [24-155] Shared for Normal/Low (5-6 events)
        [FieldOffset(100)] public fixed byte EventBuffer[156];

        // Total: 256 bytes (100 + 156 = 256)
    }
}
```

**Critical Rules:**
- **MUST** be exactly 256 bytes
- HYBRID queue like Tier 2 but larger
- Max 8 regions, 8 timers, 16 history slots
- EventBuffer: first 24 bytes reserved, rest shared (5-6 events capacity)

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.3.4 (updated per architect review)

**Tests Required:**
- ✅ Size is exactly 256 bytes (CRITICAL)
- ✅ Field offsets correct
- ✅ Can access all fixed arrays
- ✅ EventBuffer is 156 bytes

---

### Task 5: Implement Comprehensive Unit Tests

**File:** `tests/Fhsm.Tests/Data/InstanceStructuresTests.cs` (NEW FILE)

**Description:** Write thorough unit tests validating all instance structures.

**Requirements:**

**Minimum 25 tests covering:**

1. **Size Tests (CRITICAL - 4 tests):**
   ```csharp
   [Fact]
   public void InstanceHeader_Is_Exactly_16_Bytes()
   {
       Assert.Equal(16, Marshal.SizeOf<InstanceHeader>());
   }
   
   [Fact]
   public void HsmInstance64_Is_Exactly_64_Bytes()
   {
       Assert.Equal(64, unsafe { sizeof(HsmInstance64) });
   }
   
   // Similar for HsmInstance128 (128B), HsmInstance256 (256B)
   ```

2. **Field Offset Tests (8 tests - 2 per struct):**
   ```csharp
   [Fact]
   public void InstanceHeader_MachineId_At_Offset_0()
   {
       unsafe
       {
           var header = new InstanceHeader();
           var basePtr = (byte*)&header;
           var fieldPtr = (byte*)&header.MachineId;
           Assert.Equal(0, fieldPtr - basePtr);
       }
   }
   ```

3. **Initialization Tests (4 tests - 1 per struct):**
   - Can create with default constructor
   - Can set header fields
   - Can access nested header from instance structs

4. **Fixed Array Access Tests (6 tests):**
   ```csharp
   [Fact]
   public void HsmInstance64_Can_Access_ActiveLeafIds()
   {
       unsafe
       {
           var instance = new HsmInstance64();
           instance.ActiveLeafIds[0] = 10;
           instance.ActiveLeafIds[1] = 20;
           Assert.Equal(10, instance.ActiveLeafIds[0]);
           Assert.Equal(20, instance.ActiveLeafIds[1]);
       }
   }
   ```
   Test: ActiveLeafIds, TimerDeadlines, HistorySlots for at least 2 tiers

5. **Event Buffer Tests (3 tests):**
   ```csharp
   [Fact]
   public void HsmInstance64_EventBuffer_Can_Hold_24_Bytes()
   {
       unsafe
       {
           var instance = new HsmInstance64();
           // Write 24 bytes
           for (int i = 0; i < 24; i++)
           {
               instance.EventBuffer[i] = (byte)i;
           }
           // Verify
           for (int i = 0; i < 24; i++)
           {
               Assert.Equal((byte)i, instance.EventBuffer[i]);
           }
       }
   }
   ```
   Test event buffer for all 3 tiers

6. **Capacity Tests (3 tests):**
   - Tier 1: Max 1 event (24 bytes fits in 24-byte buffer)
   - Tier 2: Max ~2 events in shared portion (44 bytes / 24 = 1-2)
   - Tier 3: Max ~5 events in shared portion (132 bytes / 24 = 5-6)

7. **Header Nesting Tests (2 tests):**
   ```csharp
   [Fact]
   public void HsmInstance64_Contains_Valid_Header()
   {
       unsafe
       {
           var instance = new HsmInstance64();
           instance.Header.MachineId = 12345;
           Assert.Equal(12345u, instance.Header.MachineId);
       }
   }
   ```

**Test Quality Standards:**

**❗ REQUIRED TEST QUALITY (Learn from BATCH-01 review):**
- Tests must verify **actual behavior**, not just "it compiles"
- Fixed array tests must read/write values, not just check they exist
- Event buffer tests must verify you can store 24 bytes
- Size tests are NON-NEGOTIABLE (all must pass or batch fails)

**Example of GOOD vs BAD tests:**

❌ **BAD:**
```csharp
[Fact]
public void HsmInstance64_Has_EventBuffer()
{
    var instance = new HsmInstance64();
    // This tests nothing! ❌
}
```

✅ **GOOD:**
```csharp
[Fact]
public void HsmInstance64_EventBuffer_ReadWrite()
{
    unsafe
    {
        var instance = new HsmInstance64();
        instance.EventBuffer[0] = 0xAB;
        instance.EventBuffer[23] = 0xCD;
        Assert.Equal(0xAB, instance.EventBuffer[0]);
        Assert.Equal(0xCD, instance.EventBuffer[23]); // ✅ Tests actual memory works
    }
}
```

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.3 (all subsections)

---

## 🧪 Testing Requirements

### Minimum Test Count
**25-30 unit tests** covering:
- Size validation (4 tests - CRITICAL)
- Field offset validation (8 tests)
- Initialization (4 tests)
- Fixed array access (6 tests)
- Event buffer operations (3 tests)
- Capacity validation (3 tests)
- Header nesting (2 tests)

### Test Execution
- All tests must pass
- No compiler warnings
- Unsafe code must work correctly
- Run tests multiple times to ensure no flakiness
- Include full test output in your report

### Unsafe Code Configuration
Add to `Fhsm.Tests.csproj` if not already present:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

Also add to `Fhsm.Kernel.csproj` for the instance structs:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

---

## 📊 Report Requirements

Your report must include:

1. **Task Completion Summary**
   - Which tasks completed
   - Any deviations from specs (MUST document architect fixes)
   - Files created/modified

2. **Test Results**
   - Full test output (copy from console)
   - Test count and breakdown
   - Any failing tests with explanations

3. **Code Quality Self-Assessment**
   - Did you implement the tier-specific queue strategies correctly?
   - Did you add XML doc comments?
   - Did you verify all struct sizes?
   - Are field offsets correct?

4. **Specific Questions You MUST Answer:**

   **Q1:** Explain the architect's critical fix for Tier 1 event queues. Why is a separate queue per priority class impossible? Show the math.
   
   **Q2:** What is the "hybrid queue strategy" used in Tier 2/3? Why does it work when Tier 1's approach doesn't?
   
   **Q3:** The `HistorySlots` arrays are called "dual-purpose." What are the two purposes? When would you use each?
   
   **Q4:** Why are the instance structs exactly 64B, 128B, and 256B? What's special about these sizes? (Hint: think about cache lines and memory alignment)
   
   **Q5:** Look at `InstanceHeader`. It contains `RandomSeed` for deterministic RNG. Explain how this supports replay/determinism. What would break if we used `System.Random()` instead?
   
   **Q6:** The `EventBuffer` in Tier 2/3 is split: first 24 bytes reserved for interrupt, rest shared. Explain why this design guarantees interrupt events can always be queued (unless the instance is completely full).

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] All 4 struct files created and compile without warnings
- [ ] **InstanceHeader is exactly 16 bytes** (verified by test)
- [ ] **HsmInstance64 is exactly 64 bytes** (verified by test)
- [ ] **HsmInstance128 is exactly 128 bytes** (verified by test)
- [ ] **HsmInstance256 is exactly 256 bytes** (verified by test)
- [ ] 25+ unit tests written and **all passing**
- [ ] Unsafe code configured in both projects
- [ ] XML doc comments on all public types
- [ ] Architect's queue strategy correctly implemented (single for T1, hybrid for T2/3)
- [ ] No compiler warnings
- [ ] Report submitted answering all 6 specific questions

---

## ⚠️ Common Pitfalls to Avoid

### 1. **Forgetting the Architect's Fix**
❌ **Pitfall:** Implementing 3 separate queues for Tier 1 (as originally designed)  
✅ **Solution:** Read architect review! Tier 1 MUST use single shared queue

### 2. **Unsafe Keyword Missing**
❌ **Pitfall:** Struct won't compile because `fixed` buffers require `unsafe`  
✅ **Solution:** Add `unsafe` keyword to struct declaration

### 3. **Wrong EventBuffer Sizes**
❌ **Pitfall:** EventBuffer sizes don't add up to struct total  
✅ **Solution:** 
- Tier 1: 24 bytes (1 event)
- Tier 2: 68 bytes (24 interrupt + 44 shared)
- Tier 3: 156 bytes (24 interrupt + 132 shared)

### 4. **Field Offset Calculation Errors**
❌ **Pitfall:** Fields overlap or have gaps  
✅ **Solution:** Calculate offsets carefully:
```
Header: 0-15 (16 bytes)
ActiveLeafIds: 16-23 (8 bytes for 4 × ushort in Tier 2)
Timers: 24-39 (16 bytes for 4 × uint in Tier 2)
...
```

### 5. **Shallow Tests**
❌ **Pitfall:** Tests that don't verify anything meaningful  
✅ **Solution:** Every test must read/write/verify actual memory

### 6. **Missing sizeof() for Unsafe Structs**
❌ **Pitfall:** Using `Marshal.SizeOf<T>()` for structs with fixed buffers  
✅ **Solution:** Use `sizeof(T)` in unsafe context for structs with `fixed`

### 7. **Not Testing Fixed Array Bounds**
❌ **Pitfall:** Only testing `[0]` index  
✅ **Solution:** Test first AND last valid index:
```csharp
instance.ActiveLeafIds[0] = 1; // First
instance.ActiveLeafIds[1] = 2; // Last (for 2-element array)
```

---

## 📚 Reference Materials

### Design Documents
- **Main Implementation Spec:** `docs/design/HSM-Implementation-Design.md` Section 1.3
- **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` (CRITICAL FIX #1)
- **Previous Batch Review:** `.dev-workstream/reviews/BATCH-01-REVIEW.md` (learn from excellence)

### Inspiration
- **BTree Runtime State:** `docs/btree-design-inspiration/01-Data-Structures.md`
  - Study `BehaviorTreeState` (64 bytes) - similar tier size
  - See how they pack state into cache-friendly sizes

### C# Reference
- [Fixed-Size Buffers](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code#fixed-size-buffers)
- [Unsafe Code](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/unsafe)
- [sizeof Operator](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/sizeof)

### Related Code
- `src/Fhsm.Kernel/Data/Enums.cs` (from BATCH-01) - you'll use `InstanceFlags`, `InstancePhase`
- `src/Fhsm.Kernel/Data/StateDef.cs` (from BATCH-01) - see similar struct layout patterns

---

## 💡 Tips for Success

### Understand the Architect's Fix First
Before you write any code, make sure you understand WHY Tier 1 needs a single queue. Do the math: 32 bytes / 3 = 10.67 bytes per queue, but 1 event = 24 bytes. Impossible!

### Start with InstanceHeader
Get the header working first. All three tier structs embed it at offset 0, so nail this down early.

### Use a Spreadsheet for Offsets
Track your field offsets in a spreadsheet:
```
Field               | Offset | Size | End
--------------------|--------|------|----
Header              | 0      | 16   | 15
ActiveLeafIds[4]    | 16     | 8    | 23
TimerDeadlines[4]   | 24     | 16   | 39
...
```

### Test Size FIRST
Write the size test for a struct BEFORE you implement it. TDD works great here.

### Use Unsafe Liberally in Tests
Don't be afraid of `unsafe` in tests. It's the best way to verify memory layout.

### Test Event Buffer Capacity
Actually calculate how many 24-byte events fit:
- Tier 1: 24 bytes / 24 = 1 event
- Tier 2 shared: 44 bytes / 24 = 1-2 events (with some waste)
- Tier 3 shared: 132 bytes / 24 = 5-6 events

### Read BATCH-01 Review
Your previous batch got an A+. The review explains what made it excellent. Aim for the same quality.

---

## 🚀 Getting Started Checklist

Before you start coding:

- [ ] Read all 4 required documents (2.5 hours)
- [ ] Understand architect's queue fix (CRITICAL)
- [ ] Create spreadsheet for offset tracking
- [ ] Enable unsafe blocks in both projects
- [ ] Review BATCH-01 code for struct patterns

First code you write:

1. [ ] Create `InstanceHeader.cs`
2. [ ] Write `InstanceHeader_Is_Exactly_16_Bytes()` test
3. [ ] Run test → adjust struct until it passes
4. [ ] Create `HsmInstance64.cs` with SINGLE QUEUE (architect fix)
5. [ ] Write `HsmInstance64_Is_Exactly_64_Bytes()` test
6. [ ] Run test → adjust until passes
7. [ ] Repeat for Tier 2 and 3

---

## 📝 Questions?

If anything is unclear:

1. **Check the architect review** - Critical Fix #1 is MANDATORY
2. **Check BATCH-01 review** - See what quality looks like
3. **Check implementation design** - Section 1.3 has all details
4. **Ask in questions file** - `.dev-workstream/questions/BATCH-02-QUESTIONS.md`

**CRITICAL:** Do NOT implement 3 separate queues for Tier 1. This violates the architect's fix. Ask if you're unsure about the queue strategy.

---

**Remember:** This batch implements a critical architectural fix. The tier-specific queue strategies are not optional - they're mandatory. Get the math right, get the layouts right, and write quality tests.

**You've got this! 🚀**

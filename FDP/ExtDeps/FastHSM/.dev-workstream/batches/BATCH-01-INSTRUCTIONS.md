# BATCH-01: ROM Data Structures (Core)

**Batch Number:** BATCH-01  
**Phase:** Phase 1 - Data Layer  
**Estimated Effort:** 10-12 hours (1.5 days)  
**Priority:** HIGH (Foundation - blocks all other batches)  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Welcome to FastHSM!

You're implementing the foundational data structures for a high-performance, AAA-grade Hierarchical State Machine library. This is **the most critical batch** - everything else depends on these structures being exactly correct.

### Required Reading (IN ORDER - ~2-3 hours)

1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches (15 min)
2. **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Critical decisions & changes (15 min)
3. **Implementation Design:** `docs/design/HSM-Implementation-Design.md` - Section 1.1 and 1.2 only (1 hour)
4. **BTree Inspiration:** `docs/btree-design-inspiration/01-Data-Structures.md` - See similar patterns (30 min)

**⚠️ CRITICAL:** Read the Architect Review Summary FIRST - it contains 2 critical fixes and 4 mandatory directives that affect this batch.

### Source Code Location

- **Primary Work Area:** `src/Fhsm.Kernel/Data/`
- **Test Project:** `tests/Fhsm.Tests/Data/`
- **Namespace:** `Fhsm.Kernel.Data`

### Report Submission

**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-01-REPORT.md`

**Use this template:**  
`.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-01-QUESTIONS.md`

---

## 🎯 Batch Objectives

This batch implements the **ROM (Read-Only Memory) data structures** - the immutable "compiled bytecode" that defines state machines.

**Why this matters:**
- These structs must be **exactly** the specified sizes (32B, 16B, 8B)
- They must be **blittable** (no managed references, fixed layout)
- They define the "contract" for the compiler and kernel
- Getting this wrong breaks everything downstream

**What you're building:**
- Core enumerations (flags, priorities, types)
- StateDef: Defines a single state (32 bytes)
- TransitionDef: Defines a transition (16 bytes)
- RegionDef: Defines an orthogonal region (8 bytes)
- GlobalTransitionDef: Defines global interrupts (16 bytes)
- 20+ unit tests validating struct layouts

---

## ✅ Tasks

### Task 1: Create Data Folder Structure

**Files:** NEW FOLDERS

**Description:** Set up the proper folder structure in the Kernel project.

**Requirements:**
```
src/Fhsm.Kernel/
└── Data/
    ├── Enums.cs           (this task)
    ├── StateDef.cs         (this task)
    ├── TransitionDef.cs    (this task)
    ├── RegionDef.cs        (this task)
    └── GlobalTransitionDef.cs (this task)

tests/Fhsm.Tests/
└── Data/
    └── RomStructuresTests.cs (this task)
```

**Action:** Create the folders if they don't exist.

---

### Task 2: Implement Core Enumerations

**File:** `src/Fhsm.Kernel/Data/Enums.cs` (NEW FILE)

**Description:** Define all core enumerations used by ROM structures.

**Requirements:**

```csharp
using System;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// State behavior flags (packed into 16 bits).
    /// </summary>
    [Flags]
    public enum StateFlags : ushort
    {
        None = 0,
        IsComposite = 1 << 0,       // Has child states
        HasHistory = 1 << 1,        // Tracks last active child
        IsDeepHistory = 1 << 2,     // Deep history vs shallow
        HasRegions = 1 << 3,        // Has orthogonal regions
        HasOnEntry = 1 << 4,        // Has entry action
        HasOnExit = 1 << 5,         // Has exit action
        HasOnUpdate = 1 << 6,       // Has update/activity
        IsInitial = 1 << 7,         // Initial state of parent
        IsFinal = 1 << 8,           // Final state (terminates)
        
        // Reserved bits for future use
        Reserved9 = 1 << 9,
        Reserved10 = 1 << 10,
        Reserved11 = 1 << 11,
        Reserved12 = 1 << 12,
        Reserved13 = 1 << 13,
        Reserved14 = 1 << 14,
        Reserved15 = 1 << 15,
    }

    /// <summary>
    /// Transition behavior flags (packed into 16 bits).
    /// Includes priority in high bits (bits 12-15 = 4-bit priority).
    /// </summary>
    [Flags]
    public enum TransitionFlags : ushort
    {
        None = 0,
        
        // Behavior flags (bits 0-11)
        IsExternal = 1 << 0,        // External transition (exit + enter)
        IsInternal = 1 << 1,        // Internal (no exit/entry)
        HasGuard = 1 << 2,          // Has guard condition
        HasEffect = 1 << 3,         // Has effect action
        IsInterrupt = 1 << 4,       // Interrupt-class (high priority)
        IsSynchronized = 1 << 5,    // Part of sync group
        
        // Reserved (bits 6-11)
        Reserved6 = 1 << 6,
        Reserved7 = 1 << 7,
        Reserved8 = 1 << 8,
        Reserved9 = 1 << 9,
        Reserved10 = 1 << 10,
        Reserved11 = 1 << 11,
        
        // Priority (bits 12-15): 0 = lowest, 15 = highest
        Priority_Mask = 0xF000,     // Bits 12-15
    }

    /// <summary>
    /// Event priority classes.
    /// </summary>
    public enum EventPriority : byte
    {
        Low = 0,
        Normal = 1,
        Interrupt = 2,
    }

    /// <summary>
    /// Instance lifecycle phase (for RTC execution tracking).
    /// </summary>
    public enum InstancePhase : byte
    {
        Idle = 0,           // Not executing
        Setup = 1,          // Phase 0: Validation
        Timers = 2,         // Phase 1: Timer processing
        RTC = 3,            // Phase 2: Run-to-completion
        Update = 4,         // Phase 3: Activities
        Complete = 5,       // Tick finished
    }

    /// <summary>
    /// Instance flags (status and error conditions).
    /// </summary>
    [Flags]
    public enum InstanceFlags : byte
    {
        None = 0,
        EventOverflow = 1 << 0,         // Event queue overflow
        CommandOverflow = 1 << 1,       // Command buffer overflow
        CriticalCommandOverflow = 1 << 2, // Critical lane overflow
        BudgetExceeded = 1 << 3,        // Microstep budget exceeded
        Terminated = 1 << 4,            // Reached final state
        Error = 1 << 5,                 // Unrecoverable error
        
        Reserved6 = 1 << 6,
        Reserved7 = 1 << 7,
    }
}
```

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.1

**Tests Required:**
- ✅ Verify StateFlags is 2 bytes (`sizeof(StateFlags) == 2`)
- ✅ Verify TransitionFlags is 2 bytes
- ✅ Verify enum values are correct (spot check a few)
- ✅ Verify priority mask extracts correctly (helper test)

---

### Task 3: Implement StateDef Struct

**File:** `src/Fhsm.Kernel/Data/StateDef.cs` (NEW FILE)

**Description:** Define the 32-byte state definition structure.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// State definition (ROM). Exactly 32 bytes.
    /// Defines the topology and behavior of a single state.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct StateDef
    {
        // === Topology (12 bytes) ===
        [FieldOffset(0)] public ushort ParentIndex;         // Parent state (0xFFFF = root)
        [FieldOffset(2)] public ushort FirstChildIndex;     // First child state
        [FieldOffset(4)] public ushort ChildCount;          // Number of children
        [FieldOffset(6)] public ushort TransitionStartIndex; // First transition index
        [FieldOffset(8)] public ushort TransitionCount;     // Number of transitions
        [FieldOffset(10)] public byte Depth;                // Hierarchy depth (0-16)
        [FieldOffset(11)] public byte RegionCount;          // Orthogonal regions

        // === Actions (6 bytes) ===
        [FieldOffset(12)] public ushort OnEntryActionId;    // Entry action (0 = none)
        [FieldOffset(14)] public ushort OnExitActionId;     // Exit action (0 = none)
        [FieldOffset(16)] public ushort OnUpdateActionId;   // Update/activity (0 = none)

        // === Flags & Metadata (6 bytes) ===
        [FieldOffset(18)] public StateFlags Flags;          // Behavior flags (2 bytes)
        [FieldOffset(20)] public ushort HistorySlotIndex;   // History slot (0xFFFF = none)
        [FieldOffset(22)] public ushort TimerSlotIndex;     // Timer slot (0xFFFF = none)

        // === Regions (4 bytes) ===
        [FieldOffset(24)] public ushort RegionStartIndex;   // First region index
        [FieldOffset(26)] public ushort InitialChildIndex;  // Initial child (0xFFFF = none)

        // === Reserved (4 bytes) ===
        [FieldOffset(28)] public uint Reserved;             // For future use

        // Total: 32 bytes
    }
}
```

**Critical Rules:**
- **MUST** be exactly 32 bytes (verify with test)
- Use `LayoutKind.Explicit` with `Size = 32`
- All indices use `ushort` (max 65535 states/transitions)
- Value `0xFFFF` (65535) means "none" for optional indices
- Add XML doc comments explaining each field

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.2.1

**Tests Required:**
- ✅ Size is exactly 32 bytes
- ✅ Field offsets are correct (spot check 3-4 fields)
- ✅ Can create and initialize struct
- ✅ Default values work correctly

---

### Task 4: Implement TransitionDef Struct

**File:** `src/Fhsm.Kernel/Data/TransitionDef.cs` (NEW FILE)

**Description:** Define the 16-byte transition definition structure.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Transition definition (ROM). Exactly 16 bytes.
    /// Defines a single transition between states.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct TransitionDef
    {
        // === Topology (8 bytes) ===
        [FieldOffset(0)] public ushort SourceStateIndex;    // Source state
        [FieldOffset(2)] public ushort TargetStateIndex;    // Target state
        [FieldOffset(4)] public ushort TriggerEventId;      // Event that triggers (0 = completion)
        [FieldOffset(6)] public ushort SyncGroupId;         // Sync group (0 = none)

        // === Logic (4 bytes) ===
        [FieldOffset(8)] public ushort GuardId;             // Guard condition (0 = none)
        [FieldOffset(10)] public ushort EffectActionId;     // Effect action (0 = none)

        // === Flags & Reserved (4 bytes) ===
        [FieldOffset(12)] public TransitionFlags Flags;     // Behavior + priority (2 bytes)
        [FieldOffset(14)] public ushort Reserved;           // For future use

        // Total: 16 bytes
    }
}
```

**Critical Rules:**
- **MUST** be exactly 16 bytes
- Priority is embedded in `Flags` (bits 12-15)
- Value `0` means "none" for optional IDs
- Add XML doc comments

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.2.2

**Tests Required:**
- ✅ Size is exactly 16 bytes
- ✅ Field offsets are correct
- ✅ Can create and initialize
- ✅ Priority extraction from flags works

---

### Task 5: Implement RegionDef Struct

**File:** `src/Fhsm.Kernel/Data/RegionDef.cs` (NEW FILE)

**Description:** Define the 8-byte orthogonal region definition.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Orthogonal region definition (ROM). Exactly 8 bytes.
    /// Defines a concurrent sub-state-machine within a composite state.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct RegionDef
    {
        // === Topology (4 bytes) ===
        [FieldOffset(0)] public ushort ParentStateIndex;    // Composite state containing this region
        [FieldOffset(2)] public ushort InitialStateIndex;   // Initial state of region

        // === Metadata (4 bytes) ===
        [FieldOffset(4)] public byte Priority;              // Arbitration priority (higher = wins)
        [FieldOffset(5)] public byte Reserved1;             // For alignment
        [FieldOffset(6)] public ushort Reserved2;           // For future use

        // Total: 8 bytes
    }
}
```

**Critical Rules:**
- **MUST** be exactly 8 bytes
- Priority determines arbitration order (Architect Decision Q5)
- Add XML doc comments

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.2.4

**Tests Required:**
- ✅ Size is exactly 8 bytes
- ✅ Field offsets correct
- ✅ Can create and initialize

---

### Task 6: Implement GlobalTransitionDef Struct

**File:** `src/Fhsm.Kernel/Data/GlobalTransitionDef.cs` (NEW FILE)

**Description:** Define the 16-byte global transition (interrupt) structure.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Global transition definition (ROM). Exactly 16 bytes.
    /// Global transitions are checked first every tick (e.g., Death, Stun).
    /// ARCHITECT DECISION Q7: Separate table for O(G) performance.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GlobalTransitionDef
    {
        // === Topology (4 bytes) ===
        [FieldOffset(0)] public ushort TargetStateIndex;    // Target state (global interrupt destination)
        [FieldOffset(2)] public ushort TriggerEventId;      // Event that triggers

        // === Logic (4 bytes) ===
        [FieldOffset(4)] public ushort GuardId;             // Guard condition (0 = none)
        [FieldOffset(6)] public ushort EffectActionId;      // Effect action (0 = none)

        // === Flags & Priority (4 bytes) ===
        [FieldOffset(8)] public TransitionFlags Flags;      // Behavior flags (2 bytes)
        [FieldOffset(10)] public byte Priority;             // Priority (higher checked first)
        [FieldOffset(11)] public byte Reserved1;            // Alignment

        // === Reserved (4 bytes) ===
        [FieldOffset(12)] public uint Reserved2;            // For future use

        // Total: 16 bytes
    }
}
```

**Critical Rules:**
- **MUST** be exactly 16 bytes
- These are checked BEFORE normal transitions (Architect Decision Q7)
- Separate table in blob for O(G) scanning performance
- Add XML doc comments

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.2.3 (implied), Architect Review Q7

**Tests Required:**
- ✅ Size is exactly 16 bytes
- ✅ Field offsets correct
- ✅ Can create and initialize

---

### Task 7: Implement Comprehensive Unit Tests

**File:** `tests/Fhsm.Tests/Data/RomStructuresTests.cs` (NEW FILE)

**Description:** Write thorough unit tests validating all structures.

**Requirements:**

**Minimum 20 tests covering:**

1. **Size Tests (CRITICAL - 5 tests):**
   ```csharp
   [Fact]
   public void StateDef_Is_Exactly_32_Bytes()
   {
       Assert.Equal(32, Marshal.SizeOf<StateDef>());
   }
   
   // Similar for TransitionDef (16), RegionDef (8), GlobalTransitionDef (16)
   // And all 5 enums (check their underlying type sizes)
   ```

2. **Field Offset Tests (8 tests):**
   ```csharp
   [Fact]
   public void StateDef_ParentIndex_Is_At_Offset_0()
   {
       unsafe
       {
           var def = new StateDef();
           var basePtr = (byte*)&def;
           var fieldPtr = (byte*)&def.ParentIndex;
           Assert.Equal(0, fieldPtr - basePtr);
       }
   }
   
   // Test at least 2-3 offsets per struct
   ```

3. **Initialization Tests (4 tests):**
   - Can create each struct with default constructor
   - Can set all fields
   - Default values are correct (0 or 0xFFFF as appropriate)

4. **Flag Manipulation Tests (3 tests):**
   - StateFlags: Can set/check multiple flags
   - TransitionFlags: Priority extraction works correctly
   - InstanceFlags: Can combine flags

5. **Edge Case Tests (2-3 tests):**
   - ushort max value (0xFFFF) represents "none"
   - Depth field works correctly (0-16 range)
   - Priority extraction from TransitionFlags works for all 16 values

**Test Quality Standards:**
- Use xUnit framework
- Tests must be readable (clear Arrange/Act/Assert)
- Test names describe what they validate
- Include comments explaining WHY for non-obvious tests

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.2 (all)

---

## 🧪 Testing Requirements

### Minimum Test Count
**20-25 unit tests** covering:
- Size validation (5 tests - CRITICAL)
- Field offset validation (8 tests)
- Initialization (4 tests)
- Flag manipulation (3 tests)
- Edge cases (2-3 tests)

### Test Quality Expectations

**❗ TEST QUALITY REQUIREMENTS:**

**NOT ACCEPTABLE:**
```csharp
[Fact]
public void StateDef_Exists()
{
    var def = new StateDef();
    Assert.NotNull(def); // ❌ Tests nothing meaningful
}
```

**REQUIRED:**
```csharp
[Fact]
public void StateDef_OnEntryActionId_At_Offset_12()
{
    unsafe
    {
        var def = new StateDef();
        var basePtr = (byte*)&def;
        var fieldPtr = (byte*)&def.OnEntryActionId;
        var offset = (int)(fieldPtr - basePtr);
        Assert.Equal(12, offset); // ✅ Tests actual memory layout
    }
}
```

**Every test must verify actual behavior or layout, not just "it compiles."**

### Test Execution
- All tests must pass
- No compiler warnings
- Run tests multiple times to ensure no flakiness
- Include full test output in your report

---

## 📊 Report Requirements

Your report must include:

1. **Task Completion Summary**
   - Which tasks completed
   - Any deviations from specs (with rationale)
   - Files created/modified

2. **Test Results**
   - Full test output (copy from console)
   - Test count and breakdown
   - Any failing tests with explanations

3. **Code Quality Self-Assessment**
   - Did you add XML doc comments?
   - Did you verify struct sizes?
   - Are field offsets correct?

4. **Specific Questions You MUST Answer:**

   **Q1:** Explain in your own words why StateDef must be exactly 32 bytes. What breaks if it's 33 bytes?
   
   **Q2:** The TransitionFlags enum embeds priority in bits 12-15. Show a code snippet demonstrating how to extract priority from a TransitionFlags value. Test this in a unit test.
   
   **Q3:** Why do we use `LayoutKind.Explicit` instead of `LayoutKind.Sequential`? What's the benefit?
   
   **Q4:** You'll notice we use `ushort` (uint16) for indices instead of `int` (int32). Why? What's the trade-off?
   
   **Q5:** Look at the BTree inspiration document (`docs/btree-design-inspiration/01-Data-Structures.md`). What similarities do you see between `NodeDefinition` and our `StateDef`? What's different?

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] All 5 struct files created and compile without warnings
- [ ] All 1 enum file created
- [ ] **StateDef is exactly 32 bytes** (verified by test)
- [ ] **TransitionDef is exactly 16 bytes** (verified by test)
- [ ] **RegionDef is exactly 8 bytes** (verified by test)
- [ ] **GlobalTransitionDef is exactly 16 bytes** (verified by test)
- [ ] All enums are correct size
- [ ] 20+ unit tests written and **all passing**
- [ ] XML doc comments on all public types
- [ ] No compiler warnings
- [ ] Report submitted answering all 5 specific questions

---

## ⚠️ Common Pitfalls to Avoid

### 1. **Struct Size Mismatch**
❌ **Pitfall:** Struct ends up 33 bytes instead of 32  
✅ **Solution:** Always specify `Size = 32` in `LayoutKind.Explicit`, verify with test

### 2. **Forgetting FieldOffset**
❌ **Pitfall:** Forgetting `[FieldOffset(X)]` on a field  
✅ **Solution:** Every field in `Explicit` layout needs an offset

### 3. **Wrong Field Types**
❌ **Pitfall:** Using `int` instead of `ushort` for indices  
✅ **Solution:** Follow spec exactly - indices are `ushort`, counts are `byte` or `ushort`

### 4. **Padding Issues**
❌ **Pitfall:** Struct is 34 bytes due to unexpected padding  
✅ **Solution:** Use `Pack = 1` if needed, or use Reserved fields to fill gaps

### 5. **Testing "It Compiles"**
❌ **Pitfall:** Writing tests that just check `Assert.NotNull(new StateDef())`  
✅ **Solution:** Test actual behavior - sizes, offsets, field values

### 6. **Missing XML Comments**
❌ **Pitfall:** No documentation on public types  
✅ **Solution:** Every `public struct/enum` needs `/// <summary>` docs

### 7. **Unsafe Code Without Directive**
❌ **Pitfall:** Tests don't compile because `unsafe` not enabled  
✅ **Solution:** Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to test csproj

---

## 📚 Reference Materials

### Design Documents
- **Main Implementation Spec:** `docs/design/HSM-Implementation-Design.md` Section 1.1-1.2
- **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md`
- **Architecture Discussion:** `docs/design/HSM-design-talk.md` (for context)

### Inspiration
- **BTree Data Structures:** `docs/btree-design-inspiration/01-Data-Structures.md`
  - Study the `NodeDefinition` struct (8 bytes)
  - Similar flat-array "bytecode" approach
  - Explicit layout patterns

### C# Reference
- [`StructLayoutAttribute` Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute)
- [`FieldOffsetAttribute` Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.fieldoffsetattribute)
- [`Marshal.SizeOf` Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.sizeof)

### Code Examples in Codebase
None yet - you're creating the foundation!

---

## ⚙️ Project Configuration Notes

### Enable Unsafe Code
You'll need unsafe code for offset tests. Add to `Fhsm.Tests.csproj`:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

### Namespace Convention
All code uses `Fhsm.*` namespace (not `FastHSM.*`):
- `Fhsm.Kernel.Data` - Data structures
- `Fhsm.Tests.Data` - Tests

---

## 💡 Tips for Success

### Start with Enums
Get the enums right first - structs depend on them.

### Verify Sizes IMMEDIATELY
Write the size test first, then implement the struct. This is TDD at its finest.

### Use the Spec as Gospel
The implementation design doc is your bible. If something's unclear, ask - don't guess.

### Test Field Offsets
Offset tests catch subtle bugs that size tests miss (e.g., fields overlapping).

### Read the BTree Inspiration
The `NodeDefinition` struct in the BTree docs uses the same patterns. Study it.

### Think Like the Compiler
You're creating data that a future "compiler" will write and a "kernel" will read. They won't have access to your code - only these bytes.

---

## 🚀 Getting Started Checklist

Before you start coding:

- [ ] Read all 4 required documents (2-3 hours)
- [ ] Understand WHY these structs must be exact sizes
- [ ] Create the folder structure
- [ ] Enable unsafe blocks in test project
- [ ] Set up your IDE for the namespace convention

First code you write:

1. [ ] Create `Enums.cs` with all 5 enums
2. [ ] Write enum size tests
3. [ ] Run tests → pass
4. [ ] Create `StateDef.cs` with struct definition
5. [ ] Write `StateDef_Is_Exactly_32_Bytes()` test
6. [ ] Run test → adjust struct until it passes
7. [ ] Repeat for other structs

---

## 📝 Questions?

If anything is unclear:

1. **Check the docs** - Implementation Design Section 1.1-1.2
2. **Check the architect review** - Decisions Q1-Q10
3. **Ask in questions file** - `.dev-workstream/questions/BATCH-01-QUESTIONS.md`

Do NOT proceed if you don't understand the requirements. Ask first.

---

**Remember:** This is the foundation. Get it right, and everything else will flow smoothly. Get it wrong, and we'll be fixing it for weeks.

**Good luck! 🚀**

# BATCH-03: Event & Command Buffer Structures

**Batch Number:** BATCH-03  
**Phase:** Phase 1 - Data Layer  
**Estimated Effort:** 8-10 hours (1 day)  
**Priority:** HIGH (Blocks BATCH-04)  
**Dependencies:** BATCH-01 (ROM), BATCH-02 (RAM Instances)

---

## ⚠️ CRITICAL: Feedback from BATCH-02

### Issue 1: Mandatory Questions NOT Answered ❌
**BATCH-02 had 6 mandatory questions. You answered ZERO.**

The instructions said "Specific Questions You MUST Answer" - this was not optional.

**For THIS batch:**
- ✅ This batch has **5 mandatory questions**
- ✅ You MUST answer ALL 5 in your report
- ⚠️ **If you skip them again, batch will be REJECTED**

### Issue 2: Report Too Brief for Complexity
BATCH-02 was complex (unsafe code, architect's fix, 3 tiers) but report was minimal.

**For THIS batch:**
- Answer questions thoroughly (not just one sentence)
- Explain any design decisions you made
- Document challenges you encountered

### What You Did Excellently in BATCH-02 ✅
- Code quality was perfect (10/10)
- Architect's critical fix implemented correctly
- Tests verified actual behavior
- **Keep doing this!**

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER - ~1.5 hours)

1. **Previous Review:** `.dev-workstream/reviews/BATCH-02-REVIEW.md` - See the issues (10 min)
2. **Implementation Design:** `docs/design/HSM-Implementation-Design.md` - Section 1.4 (HsmEvent) (1 hour)
3. **BTree Inspiration:** `docs/btree-design-inspiration/01-Data-Structures.md` - See how they handle events (20 min)

### Source Code Location

- **Primary Work Area:** `src/Fhsm.Kernel/Data/`
- **Test Project:** `tests/Fhsm.Tests/Data/`
- **Namespace:** `Fhsm.Kernel.Data`

### Report Submission

**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-03-REPORT.md`

**⚠️ MANDATORY PRE-SUBMISSION CHECKLIST:**
- [ ] All 5 mandatory questions answered thoroughly
- [ ] Each answer is at least 2-3 sentences
- [ ] Test results included
- [ ] Any deviations documented

---

## 🎯 Batch Objectives

Implement the **event and command structures** - the I/O protocol for the HSM kernel.

**What you're building:**
- `HsmEvent` (24 bytes) - Fixed-size event structure with inline payload
- `CommandPage` (4096 bytes) - Paged command buffer
- `HsmCommandWriter` (ref struct) - Zero-allocation command writer
- 20+ unit tests

**Key Challenge:** Events are FIXED 24 bytes - payload must fit or use indirection.

---

## ✅ Tasks

### Task 1: Implement HsmEvent Struct

**File:** `src/Fhsm.Kernel/Data/HsmEvent.cs` (NEW FILE)

**Description:** Fixed 24-byte event structure.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Event structure (exactly 24 bytes).
    /// Events are fixed-size for predictable memory layout and cache efficiency.
    /// Payloads larger than 16 bytes must use indirection (ID-only).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct HsmEvent
    {
        // === Header (8 bytes) ===
        [FieldOffset(0)] public ushort EventId;         // Event type identifier
        [FieldOffset(2)] public EventPriority Priority; // Priority class (1 byte)
        [FieldOffset(3)] public byte Flags;             // Event flags (deferred, etc.)
        [FieldOffset(4)] public uint Timestamp;         // Frame/tick when enqueued

        // === Payload (16 bytes) ===
        [FieldOffset(8)] public unsafe fixed byte Payload[16]; // Inline data or ID

        // Total: 24 bytes (8 + 16)
    }
}
```

**Critical Rules:**
- **MUST** be exactly 24 bytes
- Payload is 16 bytes max
- Larger payloads require ID-only + blackboard lookup
- Add XML doc comments explaining size constraint

**Reference:** `docs/design/HSM-Implementation-Design.md` Section 1.4.1

**Tests Required:**
- ✅ Size is exactly 24 bytes
- ✅ Field offsets correct
- ✅ Can write/read payload bytes
- ✅ Can store different payload types (int, float, struct)

---

### Task 2: Implement Event Flags Enum

**File:** `src/Fhsm.Kernel/Data/Enums.cs` (UPDATE - add to existing)

**Description:** Add event flags to existing enums file.

**Requirements:**

```csharp
/// <summary>
/// Event flags (8 bits).
/// </summary>
[Flags]
public enum EventFlags : byte
{
    None = 0,
    IsDeferred = 1 << 0,        // Event is deferred
    IsIndirect = 1 << 1,        // Payload contains ID, not data
    IsConsumed = 1 << 2,        // Event has been consumed
    Reserved3 = 1 << 3,
    Reserved4 = 1 << 4,
    Reserved5 = 1 << 5,
    Reserved6 = 1 << 6,
    Reserved7 = 1 << 7,
}
```

**Tests Required:**
- ✅ Size is 1 byte
- ✅ Can combine flags
- ✅ IsIndirect flag works correctly

---

### Task 3: Implement CommandPage Struct

**File:** `src/Fhsm.Kernel/Data/CommandPage.cs` (NEW FILE)

**Description:** 4KB page for command buffer allocation.

**Requirements:**

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Command buffer page (4096 bytes).
    /// ARCHITECT DECISION Q2: Fixed 4KB pages for command allocation.
    /// Simple, allocator-friendly, and standard page size.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4096)]
    public unsafe struct CommandPage
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public ushort BytesUsed;       // Current write position
        [FieldOffset(2)] public ushort PageIndex;       // Page number in chain
        [FieldOffset(4)] public uint NextPageOffset;    // Offset to next page (0 = none)
        [FieldOffset(8)] public ulong Reserved;         // Future use

        // === Data (4080 bytes) ===
        [FieldOffset(16)] public fixed byte Data[4080]; // Command data

        // Total: 4096 bytes (16 + 4080)
    }
}
```

**Critical Rules:**
- **MUST** be exactly 4096 bytes (4KB)
- Header tracks usage and chaining
- Data area is 4080 bytes (16 header + 4080 data = 4096)

**Tests Required:**
- ✅ Size is exactly 4096 bytes
- ✅ Data array is 4080 bytes
- ✅ Can write to data
- ✅ Header fields accessible

---

### Task 4: Implement HsmCommandWriter (Ref Struct)

**File:** `src/Fhsm.Kernel/Data/HsmCommandWriter.cs` (NEW FILE)

**Description:** Zero-allocation command writer using ref struct.

**Requirements:**

```csharp
using System;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Zero-allocation command writer (ref struct).
    /// Uses stack-only semantics to emit commands to paged buffers.
    /// CANNOT be stored in fields or returned - stack lifetime only.
    /// </summary>
    public ref struct HsmCommandWriter
    {
        private unsafe CommandPage* _currentPage;
        private int _bytesWritten;
        private readonly int _capacity;

        /// <summary>
        /// Create writer for a command page.
        /// </summary>
        public unsafe HsmCommandWriter(CommandPage* page, int capacity = 4080)
        {
            _currentPage = page;
            _bytesWritten = 0;
            _capacity = capacity;
        }

        /// <summary>
        /// Bytes written to current page.
        /// </summary>
        public int BytesWritten => _bytesWritten;

        /// <summary>
        /// Remaining capacity in current page.
        /// </summary>
        public int RemainingCapacity => _capacity - _bytesWritten;

        /// <summary>
        /// Try to write a command. Returns false if insufficient space.
        /// </summary>
        public unsafe bool TryWriteCommand(ReadOnlySpan<byte> command)
        {
            if (command.Length > RemainingCapacity)
                return false;

            // Write command bytes
            fixed (byte* src = command)
            {
                for (int i = 0; i < command.Length; i++)
                {
                    _currentPage->Data[_bytesWritten + i] = src[i];
                }
            }

            _bytesWritten += command.Length;
            _currentPage->BytesUsed = (ushort)_bytesWritten;
            return true;
        }

        /// <summary>
        /// Reset writer to beginning of page.
        /// </summary>
        public unsafe void Reset()
        {
            _bytesWritten = 0;
            if (_currentPage != null)
                _currentPage->BytesUsed = 0;
        }
    }
}
```

**Critical Rules:**
- MUST be `ref struct` (stack-only)
- Cannot be stored in fields
- Cannot be returned from methods (stack lifetime)
- Uses unsafe pointers internally

**Tests Required:**
- ✅ Can create writer
- ✅ Can write commands
- ✅ BytesWritten increments correctly
- ✅ TryWriteCommand returns false when full
- ✅ Reset works

---

### Task 5: Implement Comprehensive Unit Tests

**File:** `tests/Fhsm.Tests/Data/EventCommandTests.cs` (NEW FILE)

**Description:** Write thorough unit tests validating event and command structures.

**Minimum 20 tests covering:**

1. **HsmEvent Tests (8 tests):**
   - Size is exactly 24 bytes (CRITICAL)
   - Field offsets correct
   - Can write/read payload (all 16 bytes)
   - Can store int in payload
   - Can store float in payload
   - Can store small struct in payload
   - Priority field works
   - Flags can be combined

2. **EventFlags Tests (2 tests):**
   - Size is 1 byte
   - Can combine flags

3. **CommandPage Tests (4 tests):**
   - Size is exactly 4096 bytes
   - Data array is 4080 bytes
   - Can write to data
   - Header fields work

4. **HsmCommandWriter Tests (6 tests):**
   - Can create writer
   - Can write commands
   - BytesWritten tracks correctly
   - RemainingCapacity correct
   - TryWriteCommand fails when full
   - Reset works

**Test Quality Standards:**
- Tests must verify **actual behavior** (not just "it compiles")
- Payload tests must write and read back data
- Command writer tests must verify bytes are written correctly
- Size tests are NON-NEGOTIABLE

**Example of GOOD test:**

```csharp
[Fact]
public void HsmEvent_Can_Store_And_Read_Payload()
{
    unsafe
    {
        var evt = new HsmEvent();
        
        // Write payload
        int testValue = 12345;
        byte* payloadPtr = evt.Payload;
        *(int*)payloadPtr = testValue;
        
        // Read back
        int readValue = *(int*)payloadPtr;
        Assert.Equal(testValue, readValue); // ✅ Tests actual read/write
    }
}
```

---

## 🧪 Testing Requirements

### Minimum Test Count
**20+ unit tests** covering all structures and operations.

### Test Execution
- All tests must pass
- No compiler warnings
- Unsafe code must work correctly
- Include full test output in your report

---

## 📊 Report Requirements

### 1. Task Completion Summary
- Which tasks completed
- Any deviations from specs
- Files created/modified

### 2. Test Results
- Full test output (copy from console)
- Test count and breakdown

### 3. Code Quality Self-Assessment
- Did you add XML doc comments?
- Did you verify struct sizes?
- Did you test ref struct behavior?

### 4. **MANDATORY QUESTIONS - ANSWER ALL 5 THOROUGHLY**

**⚠️ CRITICAL: If you skip these, batch will be REJECTED ⚠️**

**Q1:** Explain why HsmEvent is fixed at 24 bytes. What happens if your event payload needs to be 32 bytes? How would you handle it?

**Q2:** The `HsmCommandWriter` is a `ref struct`. Explain what this means. What can you NOT do with a ref struct that you can do with a normal struct? Why is this restriction useful here?

**Q3:** The CommandPage is 4096 bytes (4KB). Why this specific size? (Hint: think about memory allocators, page sizes, cache)

**Q4:** Look at the EventFlags enum. The `IsIndirect` flag indicates payload contains an ID, not actual data. Explain a scenario where you'd need this flag and how it would work with the blackboard.

**Q5:** The HsmEvent payload is 16 bytes, but the entire struct is 24 bytes. Where do the other 8 bytes go? Show the memory layout.

**Each answer should be 2-3+ sentences minimum. Show your understanding.**

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] All 4 struct/class files created and compile without warnings
- [ ] **HsmEvent is exactly 24 bytes** (verified by test)
- [ ] **CommandPage is exactly 4096 bytes** (verified by test)
- [ ] EventFlags enum added to Enums.cs
- [ ] 20+ unit tests written and **all passing**
- [ ] XML doc comments on all public types
- [ ] **ALL 5 MANDATORY QUESTIONS ANSWERED IN REPORT** ⚠️
- [ ] No compiler warnings
- [ ] Report submitted with complete answers

---

## ⚠️ Common Pitfalls to Avoid

### 1. **Skipping Mandatory Questions Again** ⚠️
❌ **Pitfall:** Not answering the 5 questions (like BATCH-02)  
✅ **Solution:** Answer them FIRST before submitting report

### 2. **Wrong Event Size**
❌ **Pitfall:** Event ends up 23 or 25 bytes  
✅ **Solution:** Use `Size = 24` in LayoutKind.Explicit, verify with test

### 3. **Ref Struct Used Incorrectly**
❌ **Pitfall:** Trying to store HsmCommandWriter in a field  
✅ **Solution:** Only use on stack, in method bodies

### 4. **CommandPage Data Size Wrong**
❌ **Pitfall:** Data array doesn't account for header  
✅ **Solution:** 4096 total = 16 header + 4080 data

### 5. **Shallow Tests**
❌ **Pitfall:** Tests that don't verify actual behavior  
✅ **Solution:** Write/read payload, verify bytes written

---

## 📚 Reference Materials

### Design Documents
- **Implementation Spec:** `docs/design/HSM-Implementation-Design.md` Section 1.4
- **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` (Directive 2: ID-only validation)
- **BATCH-02 Review:** `.dev-workstream/reviews/BATCH-02-REVIEW.md` (Learn from issues)

### C# Reference
- [Ref Structs](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct)
- [Unsafe Code](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code)
- [ReadOnlySpan<T>](https://learn.microsoft.com/en-us/dotnet/api/system.readonlyspan-1)

### Related Code
- `src/Fhsm.Kernel/Data/Enums.cs` (BATCH-01) - Add EventFlags here
- `src/Fhsm.Kernel/Data/HsmInstance*.cs` (BATCH-02) - See event queues that will hold these events

---

## 💡 Tips for Success

### Answer Questions FIRST
Before you submit, write the 5 answers. Make them part of your workflow.

### Understand Ref Struct
`ref struct` is stack-only. You can't:
- Store in fields
- Return from methods
- Use in async methods
- Box it

### Test Payload Operations
Actually write data to the payload and read it back. Test with int, float, small struct.

### Verify Fixed Sizes
24 bytes and 4096 bytes are NON-NEGOTIABLE. Tests must verify.

### Learn from BATCH-02
Your code was perfect. Match that with report quality this time.

---

## 🚀 Pre-Submission Checklist

**Before you submit your report, verify:**

- [ ] ✅ Question 1 answered (2-3+ sentences)
- [ ] ✅ Question 2 answered (2-3+ sentences)
- [ ] ✅ Question 3 answered (2-3+ sentences)
- [ ] ✅ Question 4 answered (2-3+ sentences)
- [ ] ✅ Question 5 answered (2-3+ sentences)
- [ ] ✅ All tests passing
- [ ] ✅ Test output included
- [ ] ✅ Task completion summary written
- [ ] ✅ Any deviations documented

**If ANY checkbox is unchecked, DO NOT SUBMIT YET.**

---

**Remember:** Your code quality in BATCH-02 was perfect (10/10). Now match that with report quality. Answer the questions thoroughly. Show your understanding.

**You've got this! 🚀**

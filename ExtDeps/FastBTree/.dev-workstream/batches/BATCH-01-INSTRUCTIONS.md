# BATCH-01: Foundation - Data Structures & Test Framework

**Project:** FastBTree - High-Performance Behavior Tree Library  
**Batch Number:** 01  
**Phase:** Phase 1 - Core (Week 1)  
**Assigned:** 2026-01-04  
**Estimated Effort:** 3-5 days

---

## 📋 Batch Overview

This is the **foundation batch** for the FastBTree project. You will:

1. Create the core `Fbt.Kernel` library project
2. Implement all fundamental data structures
3. Set up the xUnit test framework
4. Create test fixtures and utilities
5. Achieve 100% test coverage for data structures

**Critical Success Factors:**
- ✅ `BehaviorTreeState` must be **exactly 64 bytes**
- ✅ `NodeDefinition` must be **exactly 8 bytes**
- ✅ All structures must be **blittable** (no managed references)
- ✅ Zero compiler warnings
- ✅ All tests passing

---

## 📚 Required Reading

**BEFORE starting, read these design documents:**

1. **[docs/design/00-Architecture-Overview.md](../../docs/design/00-Architecture-Overview.md)** - Core principles
2. **[docs/design/01-Data-Structures.md](../../docs/design/01-Data-Structures.md)** - Detailed specifications
3. **[docs/design/05-Testing-Strategy.md](../../docs/design/05-Testing-Strategy.md)** - Testing approach

**Quick Reference:**
- [docs/IMPLEMENTATION_CHECKLIST.md](../../docs/IMPLEMENTATION_CHECKLIST.md) - Phase 1, Week 1 section

---

## 🎯 Tasks

### Task 1: Project Setup

**Objective:** Create the solution structure and core library project.

**Acceptance Criteria:**
- [x] Solution file created: `FastBTree.sln`
- [x] Library project created: `src/Fbt.Kernel/Fbt.Kernel.csproj`
  - Target: .NET 8.0
  - Property: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
  - Property: `<Nullable>enable</Nullable>`
- [x] Test project created: `tests/Fbt.Tests/Fbt.Tests.csproj`
  - xUnit framework
  - Reference to `Fbt.Kernel`
- [x] Projects build without errors or warnings

**Directory Structure:**
```
FastBTree/
├── FastBTree.sln
├── src/
│   └── Fbt.Kernel/
│       ├── Fbt.Kernel.csproj
│       └── (source files - you'll create these)
└── tests/
    └── Fbt.Tests/
        ├── Fbt.Tests.csproj
        └── (test files - you'll create these)
```

**Verification:**
```powershell
dotnet build --nologo
# Must output: Build succeeded. 0 Warning(s)
```

---

### Task 2: Core Enumerations

**Objective:** Implement `NodeType` and `NodeStatus` enums.

**File:** `src/Fbt.Kernel/NodeType.cs`

**Specification:** See [01-Data-Structures.md § 2.2](../../docs/design/01-Data-Structures.md#22-nodetype)

**Acceptance Criteria:**
- [x] `NodeType` enum implemented as `byte`
- [x] All node types defined (Root, Selector, Sequence, Parallel, Action, Condition, etc.)
- [x] Grouped logically (composites 0-9, leaves 10-19, decorators 20-29, advanced 30+)
- [x] XML documentation comments on all values

**File:** `src/Fbt.Kernel/NodeStatus.cs`

**Specification:** See [01-Data-Structures.md § 2.1](../../docs/design/01-Data-Structures.md#21-nodestatus)

**Acceptance Criteria:**
- [x] `NodeStatus` enum implemented as `byte`
- [x] Three values: Failure=0, Success=1, Running=2
- [x] XML documentation comments

**Tests Required:**
```csharp
// File: tests/Fbt.Tests/Unit/EnumTests.cs

[Fact]
public void NodeType_ShouldBeByte()
{
    Assert.Equal(sizeof(byte), Unsafe.SizeOf<NodeType>());
}

[Fact]
public void NodeStatus_ShouldBeByte()
{
    Assert.Equal(sizeof(byte), Unsafe.SizeOf<NodeStatus>());
}

[Fact]
public void NodeStatus_FailureIsZero()
{
    Assert.Equal(0, (int)NodeStatus.Failure);
}
```

---

### Task 3: NodeDefinition Structure

**Objective:** Implement the 8-byte node definition structure.

**File:** `src/Fbt.Kernel/NodeDefinition.cs`

**Specification:** See [01-Data-Structures.md § 3.1](../../docs/design/01-Data-Structures.md#31-nodedefinition-structure)

**Acceptance Criteria:**
- [x] Struct size **exactly 8 bytes** (verified via `Unsafe.SizeOf`)
- [x] `[StructLayout(LayoutKind.Sequential, Pack = 1)]` attribute
- [x] Fields: `NodeType Type`, `byte ChildCount`, `ushort SubtreeOffset`, `int PayloadIndex`
- [x] XML documentation comments on all fields
- [x] Public fields (struct is immutable by convention)

**Critical:** Memory layout must match specification:
```
Offset  Size  Field
  0      1    Type
  1      1    ChildCount
  2      2    SubtreeOffset
  4      4    PayloadIndex
Total: 8 bytes
```

**Tests Required:**
```csharp
// File: tests/Fbt.Tests/Unit/DataStructuresTests.cs

[Fact]
public void NodeDefinition_ShouldBe8Bytes()
{
    var size = Unsafe.SizeOf<NodeDefinition>();
    Assert.Equal(8, size);
}

[Fact]
public void NodeDefinition_FieldOffsets_MatchSpecification()
{
    // Verify field offsets using FieldOffset attributes or marshaling
    var def = new NodeDefinition
    {
        Type = NodeType.Action,
        ChildCount = 2,
        SubtreeOffset = 5,
        PayloadIndex = 10
    };
    
    Assert.Equal(NodeType.Action, def.Type);
    Assert.Equal(2, def.ChildCount);
    Assert.Equal(5, def.SubtreeOffset);
    Assert.Equal(10, def.PayloadIndex);
}

[Fact]
public void NodeDefinition_CanBeUsedInArray()
{
    var array = new NodeDefinition[100];
    array[0] = new NodeDefinition { Type = NodeType.Root };
    Assert.Equal(NodeType.Root, array[0].Type);
}
```

---

### Task 4: BehaviorTreeState Structure

**Objective:** Implement the 64-byte runtime state structure.

**File:** `src/Fbt.Kernel/BehaviorTreeState.cs`

**Specification:** See [01-Data-Structures.md § 5.1](../../docs/design/01-Data-Structures.md#51-behaviortreestate-structure)

**Acceptance Criteria:**
- [x] Struct size **exactly 64 bytes** (verified via `Unsafe.SizeOf`)
- [x] `unsafe struct` with `[StructLayout(LayoutKind.Explicit, Size = 64)]`
- [x] All fields correctly offset using `[FieldOffset]`
- [x] `fixed` buffers for: `NodeIndexStack[8]`, `LocalRegisters[4]`, `AsyncHandles[3]`
- [x] Helper properties: `CurrentRunningNode`, `Reset()`, `PushNode()`, `PopNode()`
- [x] XML documentation on all members

**Memory Layout (Critical):**
```
Offset 0-7:   Header (RunningNodeIndex, StackPointer, TreeVersion)
Offset 8-23:  NodeIndexStack[8] (8 × ushort = 16 bytes)
Offset 24-39: LocalRegisters[4] (4 × int = 16 bytes)
Offset 40-63: AsyncHandles[3] (3 × ulong = 24 bytes)
Total: 64 bytes
```

**Tests Required:**
```csharp
// File: tests/Fbt.Tests/Unit/DataStructuresTests.cs

[Fact]
public void BehaviorTreeState_ShouldBe64Bytes()
{
    var size = Unsafe.SizeOf<BehaviorTreeState>();
    Assert.Equal(64, size);
}

[Fact]
public void BehaviorTreeState_Reset_ClearsAllState()
{
    var state = new BehaviorTreeState();
    state.RunningNodeIndex = 5;
    state.TreeVersion = 10;
    unsafe
    {
        state.NodeIndexStack[0] = 3;
        state.LocalRegisters[0] = 42;
        state.AsyncHandles[0] = 999;
    }
    
    state.Reset();
    
    Assert.Equal(0, state.RunningNodeIndex);
    Assert.Equal(11, state.TreeVersion); // Incremented
    unsafe
    {
        Assert.Equal(0, state.NodeIndexStack[0]);
        Assert.Equal(0, state.LocalRegisters[0]);
        Assert.Equal(0ul, state.AsyncHandles[0]);
    }
}

[Fact]
public void BehaviorTreeState_PushPop_ManagesStack()
{
    var state = new BehaviorTreeState();
    
    state.PushNode(5);
    Assert.Equal(1, state.StackPointer);
    Assert.Equal(5, state.CurrentRunningNode);
    
    state.PushNode(10);
    Assert.Equal(2, state.StackPointer);
    Assert.Equal(10, state.CurrentRunningNode);
    
    state.PopNode();
    Assert.Equal(1, state.StackPointer);
    Assert.Equal(5, state.CurrentRunningNode);
    
    state.PopNode();
    Assert.Equal(0, state.StackPointer);
}

[Fact]
public void BehaviorTreeState_StackOverflow_Handled()
{
    var state = new BehaviorTreeState();
    
    // Push 8 times (max depth)
    for (int i = 0; i < 8; i++)
    {
        state.PushNode((ushort)i);
    }
    
    Assert.Equal(7, state.StackPointer); // Should be at max
    
    // Attempt overflow - should not crash, just not push
    state.PushNode(99);
    Assert.Equal(7, state.StackPointer); // Still at max
}
```

---

### Task 5: AsyncToken Structure

**Objective:** Implement async operation token with version validation.

**File:** `src/Fbt.Kernel/AsyncToken.cs`

**Specification:** See [01-Data-Structures.md § 6.1](../../docs/design/01-Data-Structures.md#61-asynctoken-structure)

**Acceptance Criteria:**
- [x] `readonly struct` with `RequestID` (int) and `Version` (uint)
- [x] Constructor: `AsyncToken(int requestId, uint version)`
- [x] Method: `ulong Pack()` - packs into single ulong
- [x] Static method: `AsyncToken Unpack(ulong packed)` - unpacks from ulong
- [x] Method: `bool IsValid(uint currentTreeVersion)` - validates token
- [x] XML documentation

**Packing Format:**
```
Bits 0-31:  RequestID (int)
Bits 32-63: Version (uint)
```

**Tests Required:**
```csharp
// File: tests/Fbt.Tests/Unit/DataStructuresTests.cs

[Fact]
public void AsyncToken_PackUnpack_RoundTrips()
{
    var original = new AsyncToken(12345, 67);
    
    ulong packed = original.Pack();
    var unpacked = AsyncToken.Unpack(packed);
    
    Assert.Equal(original.RequestID, unpacked.RequestID);
    Assert.Equal(original.Version, unpacked.Version);
}

[Fact]
public void AsyncToken_IsValid_CurrentVersion_ReturnsTrue()
{
    var token = new AsyncToken(100, 5);
    Assert.True(token.IsValid(5));
}

[Fact]
public void AsyncToken_IsValid_OldVersion_ReturnsFalse()
{
    var token = new AsyncToken(100, 5);
    Assert.False(token.IsValid(6)); // Version incremented
}

[Fact]
public void AsyncToken_IsValid_ZeroRequest_ReturnsFalse()
{
    var token = new AsyncToken(0, 5);
    Assert.False(token.IsValid(5)); // RequestID=0 is invalid
}

[Fact]
public void AsyncToken_Pack_PreservesAllBits()
{
    var token = new AsyncToken(-1, uint.MaxValue);
    var packed = token.Pack();
    var unpacked = AsyncToken.Unpack(packed);
    
    Assert.Equal(-1, unpacked.RequestID);
    Assert.Equal(uint.MaxValue, unpacked.Version);
}
```

---

### Task 6: BehaviorTreeBlob Class

**Objective:** Implement the tree asset container.

**File:** `src/Fbt.Kernel/BehaviorTreeBlob.cs`

**Specification:** See [01-Data-Structures.md § 4.1](../../docs/design/01-Data-Structures.md#41-behaviortreeblob-class)

**Acceptance Criteria:**
- [x] `[Serializable]` class (not struct)
- [x] Properties: `TreeName`, `Version`, `StructureHash`, `ParamHash`
- [x] Arrays: `NodeDefinition[] Nodes`, `string[] MethodNames`, `float[] FloatParams`, `int[] IntParams`, `string[] SubtreeAssetIds`
- [x] `[NonSerialized]` property: `object CompiledDelegate`
- [x] XML documentation
- [x] Default `Version = 1` in constructor or property initializer

**Tests Required:**
```csharp
// File: tests/Fbt.Tests/Unit/BehaviorTreeBlobTests.cs

[Fact]
public void BehaviorTreeBlob_DefaultVersion_Is1()
{
    var blob = new BehaviorTreeBlob();
    Assert.Equal(1, blob.Version);
}

[Fact]
public void BehaviorTreeBlob_Nodes_CanStoreArray()
{
    var blob = new BehaviorTreeBlob
    {
        Nodes = new[]
        {
            new NodeDefinition { Type = NodeType.Root },
            new NodeDefinition { Type = NodeType.Action }
        }
    };
    
    Assert.Equal(2, blob.Nodes.Length);
    Assert.Equal(NodeType.Root, blob.Nodes[0].Type);
}

[Fact]
public void BehaviorTreeBlob_LookupTables_AreDense()
{
    var blob = new BehaviorTreeBlob
    {
        MethodNames = new[] { "Attack", "Patrol" },
        FloatParams = new[] { 1.0f, 2.0f, 3.0f },
        IntParams = new[] { 10, 20 }
    };
    
    Assert.Equal(2, blob.MethodNames.Length);
    Assert.Equal(3, blob.FloatParams.Length);
    Assert.Equal(2, blob.IntParams.Length);
}
```

---

### Task 7: Test Fixtures Setup

**Objective:** Create test utilities and mock structures for testing.

**Files:**
- `tests/Fbt.Tests/TestFixtures/TestBlackboard.cs`
- `tests/Fbt.Tests/TestFixtures/MockContext.cs` (stub for now)

**TestBlackboard Specification:**
```csharp
namespace Fbt.Tests.TestFixtures
{
    public struct TestBlackboard
    {
        public int Counter;
        public bool Flag;
        public float Timer;
        public bool Priority;
    }
}
```

**MockContext (Minimal Stub):**
```csharp
namespace Fbt.Tests.TestFixtures
{
    // NOTE: Full implementation comes in BATCH-04
    // For now, just a placeholder struct
    public struct MockContext
    {
        public float DeltaTime;
        public int CallCount;
    }
}
```

**Acceptance Criteria:**
- [x] Test fixtures compile
- [x] Organized in `TestFixtures/` directory
- [x] XML documentation

---

## 📊 Deliverables

### Code Files (Must Create)

**Source (src/Fbt.Kernel/):**
- [ ] `NodeType.cs`
- [ ] `NodeStatus.cs`
- [ ] `NodeDefinition.cs`
- [ ] `BehaviorTreeState.cs`
- [ ] `AsyncToken.cs`
- [ ] `BehaviorTreeBlob.cs`

**Tests (tests/Fbt.Tests/):**
- [ ] `Unit/EnumTests.cs`
- [ ] `Unit/DataStructuresTests.cs`
- [ ] `Unit/BehaviorTreeBlobTests.cs`
- [ ] `TestFixtures/TestBlackboard.cs`
- [ ] `TestFixtures/MockContext.cs`

### Test Coverage

**Minimum Required:**
- [x] All enums: 100% coverage
- [x] All structures: 100% coverage (all methods/properties tested)
- [x] Edge cases: Stack overflow, invalid tokens, boundary values

**Total Test Count Expected:** ~20-25 tests

---

## ✅ Definition of Done

**Batch is DONE when:**

1. **Code Quality**
   - [x] All source files created and compile
   - [x] Zero compiler warnings (`dotnet build`)
   - [x] XML documentation on all public APIs
   - [x] Code follows .NET naming conventions

2. **Size Validation**
   - [x] `NodeDefinition` verified as 8 bytes
   - [x] `BehaviorTreeState` verified as 64 bytes
   - [x] Tests explicitly validate sizes

3. **Testing**
   - [x] All tests passing (100% pass rate)
   - [x] Minimum 20 tests written
   - [x] Coverage: 100% on data structures
   - [x] Edge cases tested (overflow, invalid states)

4. **Architecture Compliance**
   - [x] All structures are blittable (no managed refs)
   - [x] `unsafe struct` used correctly for `BehaviorTreeState`
   - [x] Memory layouts match specification

---

## 🚨 Critical Notes

### Size Validation is MANDATORY

**You MUST verify sizes in tests:**
```csharp
Assert.Equal(8, Unsafe.SizeOf<NodeDefinition>());
Assert.Equal(64, Unsafe.SizeOf<BehaviorTreeState>());
```

**If sizes don't match, batch FAILS.**

### No Warnings Allowed

Run this before submitting:
```powershell
dotnet build --nologo | Select-String "warning"
```

Output must be empty. Treat warnings as errors.

### Reference Implementation

The design documents contain pseudocode. **Do not copy-paste blindly!**
- Adapt to C# idioms
- Add proper error handling
- Include XML docs

---

## 📝 Reporting Requirements

When complete, create: `.dev-workstream/reports/BATCH-01-REPORT.md`

**Use template:** `.dev-workstream/templates/BATCH-REPORT-TEMPLATE.md`

**Must include:**
1. Executive summary (status, issues)
2. Task completion table
3. Files changed (list all created files)
4. Test results (pass/fail counts, output)
5. Size validation results (8 bytes, 64 bytes confirmed)
6. Any additional work done
7. Known issues or concerns

---

## ❓ Questions?

If you have questions or blockers:

**For Questions:**
- Create: `.dev-workstream/reports/BATCH-01-QUESTIONS.md`
- Use template: `.dev-workstream/templates/QUESTIONS-TEMPLATE.md`
- I will respond with `BATCH-01-ANSWERS.md`

**For Blockers:**
- Create/Update: `.dev-workstream/reports/BLOCKERS-ACTIVE.md`
- Use template: `.dev-workstream/templates/BLOCKERS-TEMPLATE.md`
- Update immediately when blocked (don't wait for batch end)

---

## 🎯 Success Criteria Summary

**You succeed when:**
- ✅ 6 source files created
- ✅ 5 test files created
- ✅ All tests passing (≥20 tests)
- ✅ Zero warnings
- ✅ Size validation passes (8, 64 bytes)
- ✅ Batch report complete

**Estimated Time:** 3-5 days (including testing and documentation)

---

**Good luck! This is the foundation for everything else. Take your time to get it right. 🚀**

*Batch Issued: 2026-01-04*  
*Development Leader: FastBTree Team Lead*

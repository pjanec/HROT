# BATCH-42 — P5T1 + P5T2 + P5T3: Trace-buffer predicate compiler + end-to-end breakpoints

## Goal
Implement the trace-buffer scan extension to `IPredicateCompiler` and wire it to the existing `DataBreakpointSystem` so that a breakpoint can fire when any record in a `BTreeTraceWorkingMemory1024` or `HsmTraceWorkingMemory1024` buffer matches a given opcode/field combination.

## Reference documents
- DESIGN §6.4 `.dev/breakpoints-1/DESIGN.md`
- TASK-DETAIL P5T1–P5T3 `.dev/breakpoints-1/TASK-DETAIL.md`
- TASK-TRACKER `.dev/breakpoints-1/TASK-TRACKER.md`

---

## Task context (pre-researched — do NOT re-derive)

### Trace buffer memory layout (BOTH BTree and HSM)
Both `BTreeTraceWorkingMemory1024` and `HsmTraceWorkingMemory1024` share the same header layout:
```
offset 0: WritePos     (ushort)
offset 2: RecordCount  (ushort) — saturates at 63
offset 4: LastInstanceId (uint)
offset 8: Buffer       (fixed byte[1016])
```
Each record occupies exactly **16 bytes** (`RecordStride = 16`). The ring buffer holds at most **63 records** (`CapacityRecords = 63`). Records are stored contiguously from `Buffer[0]` up to `RecordCount * 16`.

### BTree record layout (`BTreeTraceRecord`, 16 bytes)
```
offset  0: OpCode     (BTreeTraceOpCode, byte) — namespace Fbt
offset  1: Reserved   (byte)
offset  2: Timestamp  (ushort)
offset  4: InstanceId (uint)
offset  8: NodeIndex  (ushort)  alias: StackDepth for Scope* opcodes
offset 10: Status     (NodeStatus, byte)  — namespace Fbt; alias: Channel (byte) for ChannelMutated
offset 12: ActiveAction / Duration (ushort / float) — opcode-dependent
```

### HSM record layout (`TraceRecord`, 16 bytes)
The unified view of all HSM record types (namespace `Fhsm.Kernel.Data`):
```
offset  0: OpCode          (TraceOpCode, byte)
offset  1: Reserved
offset  2: Timestamp       (ushort)
offset  4: InstanceId      (uint)
offset  8: StateIndex/EventId/ActionId/GuardId/ErrorCode (ushort union)
offset 10: TargetStateIndex / GuardResult (ushort/byte union)
offset 12: TriggerEventId  (ushort) — for Transition records
offset 14: Reserved
```

**For `TraceStateChange` (StateEnter/StateExit):** `StateIndex` at offset 8; offsets 10-15 are zero.  
**For `TraceTransition`:** `FromState` at offset 8, `ToState` at offset 10, `TriggerEventId` at offset 12.

### Write methods available in tests
- **BTree**: call `BTreeTraceWorkingMemory1024.WriteNodeEvaluated(nodeIndex, NodeStatus, tick)`, `WriteScopePopped(stackDepth, tick)`, etc. directly on a `ref` to the component.
- **HSM**: construct `HsmTraceContext` pointing to the component's memory (see pattern in §3 below) and call `ctx.WriteStateChange(instanceId, stateIndex, isEntry)` / `ctx.WriteTransition(instanceId, fromState, toState, triggerEventId)`.

### Namespaces
- `BTreeTraceOpCode`, `NodeStatus` → `Fbt`
- `BTreeTraceWorkingMemory1024`, `BTreeTraceRecord` → `Fdp.Toolkit.Behavior.Diagnostics`
- `HsmTraceWorkingMemory1024` → `Fdp.Toolkit.Behavior.Diagnostics`
- `HsmTraceContext`, `TraceOpCode`, `TraceLevel` → `Fhsm.Kernel.Data`

### `unsafe` blocks
Both `Fdp.Toolkits.csproj` and `Hrot.Diagnostics.Breakpoints.Tests.csproj` have `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. No changes to `.csproj` files are needed.

### ECS storage
Components are stored in native (non-GC) memory. Using `Unsafe.AsPointer(ref comp)` on a component returned by `GetComponentRO<T>` or `GetComponentRW<T>` is safe — no GC pinning required. This matches the production pattern in `HsmTickSystem.cs`.

---

## Changes required

### 1. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`

#### 1a. Add `[JsonDerivedType]` for `TraceBufferScanPredicateDto`

Add one entry to the `[JsonPolymorphic]` attribute block on `SearchPredicateDto`. Insert it as the LAST `[JsonDerivedType]` before the class declaration line `public abstract class SearchPredicateDto { }`:

```csharp
    [JsonDerivedType(typeof(BehaviorParamPredicateDto),      "BehaviorParam")]
    [JsonDerivedType(typeof(TraceBufferScanPredicateDto),    "TraceBufferScan")]  // ADD THIS
    public abstract class SearchPredicateDto { }
```

#### 1b. Add `TraceBufferScanPredicateDto` class

Append the following class at the end of the file (before the final closing brace of the namespace if present, or at the end of the file):

```csharp
    // ──────────────────────────────────────────────────────────────────────────
    // Trace buffer scan
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Predicate that scans a BTree or HSM trace ring buffer for any record that
    /// matches the specified opcode and optional field constraints.
    /// Zero allocation on the hot evaluation path — uses pointer arithmetic over
    /// the component's raw 16-byte-stride buffer.
    /// </summary>
    public sealed class TraceBufferScanPredicateDto : SearchPredicateDto
    {
        /// <summary>
        /// Component type to scan.
        /// Must be <c>BTreeTraceWorkingMemory1024</c> or <c>HsmTraceWorkingMemory1024</c>.
        /// </summary>
        [JsonConverter(typeof(TypeNameJsonConverter))]
        public Type ComponentType { get; set; } = null!;

        /// <summary>
        /// Opcode byte to match (cast from <c>BTreeTraceOpCode</c> or <c>TraceOpCode</c>).
        /// Always checked — there is no "match-any-opcode" mode.
        /// </summary>
        public byte OpCode { get; set; }

        /// <summary>
        /// Value to match at byte offset 8-9 of each record.
        /// BTree: NodeIndex (for NodeEvaluated / Wait*) or StackDepth (for Scope*).
        /// HSM: StateIndex (StateEnter/Exit), EventId (EventHandled), FromState (Transition), etc.
        /// Only checked when <see cref="MatchIndexField"/> is true.
        /// </summary>
        public ushort IndexField { get; set; }

        /// <summary>Whether to check <see cref="IndexField"/>.</summary>
        public bool MatchIndexField { get; set; }

        /// <summary>
        /// Value to match at byte offset 10 of each record (the <em>status/result</em> byte).
        /// BTree: <c>NodeStatus</c> byte for NodeEvaluated.
        /// HSM: <c>GuardResult</c> byte (0=false, 1=true) for GuardEvaluated.
        /// Only checked when <see cref="MatchStatusField"/> is true.
        /// </summary>
        public byte StatusField { get; set; }

        /// <summary>Whether to check <see cref="StatusField"/>.</summary>
        public bool MatchStatusField { get; set; }

        /// <summary>
        /// Value to match at byte offset 12-13 of each record.
        /// HSM: <c>TriggerEventId</c> for Transition records.
        /// BTree: low 16-bits of <c>Duration</c> for Wait* records (rarely useful).
        /// Only checked when <see cref="MatchTriggerEventId"/> is true.
        /// </summary>
        public ushort TriggerEventId { get; set; }

        /// <summary>Whether to check <see cref="TriggerEventId"/>.</summary>
        public bool MatchTriggerEventId { get; set; }
    }
```

---

### 2. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`

#### 2a. Add using directive

Add at the top of the file (with existing `using` statements):

```csharp
using Fdp.Toolkit.Behavior.Diagnostics;
```

#### 2b. Add case in `Compile()` switch

In the `Compile(SearchPredicateDto dto)` method, add a new case **before** the fall-through `StructuralPredicateDto` block:

```csharp
                case TraceBufferScanPredicateDto traceScan:
                    return CompileTraceBufferScan(traceScan);
```

The switch statement should look like:
```csharp
            switch (dto)
            {
                case CompoundPredicateDto compound:
                    return CompileCompound(compound);

                case PropertyMatchDto prop:
                    return CompilePropertyMatch(prop);
                
                case BehaviorParamPredicateDto behaviorParam:
                    return CompileBehaviorParamMatch(behaviorParam);

                case TraceBufferScanPredicateDto traceScan:        // ADD
                    return CompileTraceBufferScan(traceScan);       // ADD

                // Specialized loop predicates: pass-through; handled by the service.
                case StructuralPredicateDto _:
                // ...
```

#### 2c. Add `CompileTraceBufferScan` private method

Add after `CompileBehaviorParamMatch` (before the `BuildBehaviorParamMatcherGenericBrain` helper):

```csharp
        private Func<EntityRepository, Entity, bool> CompileTraceBufferScan(TraceBufferScanPredicateDto scan)
        {
            if (scan.ComponentType == null) return static (_, _) => false;

            var buildMethod = typeof(PredicateCompiler)
                .GetMethod(nameof(BuildTraceBufferScanMatcher), BindingFlags.NonPublic | BindingFlags.Static)!;
            return (Func<EntityRepository, Entity, bool>)buildMethod
                .MakeGenericMethod(scan.ComponentType)
                .Invoke(null, new object[] { scan })!;
        }
```

#### 2d. Add `BuildTraceBufferScanMatcher<T>` static method

Add after `CompileTraceBufferScan` (keep it near the other `Build*Matcher` methods):

```csharp
        private static unsafe Func<EntityRepository, Entity, bool> BuildTraceBufferScanMatcher<T>(
            TraceBufferScanPredicateDto scan)
            where T : unmanaged
        {
            int    typeId       = ComponentTypeRegistry.GetId(typeof(T));
            byte   opCode       = scan.OpCode;
            ushort indexField   = scan.IndexField;
            bool   matchIndex   = scan.MatchIndexField;
            byte   statusField  = scan.StatusField;
            bool   matchStatus  = scan.MatchStatusField;
            ushort triggerEvtId = scan.TriggerEventId;
            bool   matchTrigger = scan.MatchTriggerEventId;

            return (repo, entity) =>
            {
                if (!repo.HasComponentByTypeId(entity, typeId)) return false;

                ref readonly T comp = ref repo.GetComponentRO<T>(entity);
                unsafe
                {
                    // Both BTreeTraceWorkingMemory1024 and HsmTraceWorkingMemory1024 share
                    // identical 8-byte headers:
                    //   offset 0: WritePos     (ushort)
                    //   offset 2: RecordCount  (ushort)
                    //   offset 4: LastInstanceId (uint)
                    //   offset 8: Buffer start (16-byte stride records)
                    byte*  ptr         = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in comp));
                    ushort recordCount = *(ushort*)(ptr + 2);
                    byte*  buf         = ptr + 8;

                    for (int i = 0; i < recordCount; i++)
                    {
                        byte* rec = buf + i * 16;
                        if (rec[0] != opCode)                              continue;
                        if (matchIndex  && *(ushort*)(rec + 8)  != indexField)   continue;
                        if (matchStatus && rec[10]               != statusField)  continue;
                        if (matchTrigger && *(ushort*)(rec + 12) != triggerEvtId) continue;
                        return true;
                    }
                    return false;
                }
            };
        }
```

#### 2e. Update `CollectMandatoryComponents`

In the `CollectMandatoryComponents` method, add a new `else if` branch for `TraceBufferScanPredicateDto` after the existing `BehaviorParamPredicateDto` branch:

```csharp
            else if (dto is TraceBufferScanPredicateDto traceScan)
            {
                if (traceScan.ComponentType != null && !result.Contains(traceScan.ComponentType))
                    result.Add(traceScan.ComponentType);
            }
```

---

### 3. New file: `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TraceBufferScanTests.cs`

Create this file with the following content:

```csharp
using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fbt;
using Fhsm.Kernel.Data;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// =============================================================================
// UBP-P5T1: Compiler extension for trace buffer scans
// =============================================================================

/// <summary>
/// Unit tests for the new <see cref="TraceBufferScanPredicateDto"/> compilation path
/// in <see cref="PredicateCompiler"/>.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class TraceBufferScanCompilerTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var system        = new DataBreakpointSystem(manager);
        return (manager, system, repo);
    }

    /// <summary>
    /// When 3 records are written (two non-matching, one matching NodeEvaluated/NodeIndex=5/Status=Running),
    /// the compiled predicate must return true.
    /// </summary>
    [Fact]
    public void Compile_TraceBufferScan_ReturnsTrueWhenAnyRecordMatches()
    {
        var (_, _, repo) = Setup();
        repo.RegisterComponent<BTreeTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());

        // Write 3 records: only the second matches.
        ref var mem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
        mem.WriteNodeEvaluated(3, NodeStatus.Failure, 1);    // no match — wrong NodeIndex + Status
        mem.WriteNodeEvaluated(5, NodeStatus.Running, 1);    // MATCH
        mem.WriteScopePushed(1, 1);                          // no match — wrong OpCode

        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var dto = new TraceBufferScanPredicateDto
        {
            ComponentType  = typeof(BTreeTraceWorkingMemory1024),
            OpCode         = (byte)BTreeTraceOpCode.NodeEvaluated,
            IndexField     = 5,
            MatchIndexField = true,
            StatusField    = (byte)NodeStatus.Running,
            MatchStatusField = true,
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.True(predicate(repo, entity));
    }

    /// <summary>
    /// When 3 records are written but none match the target OpCode+NodeIndex+Status,
    /// the compiled predicate must return false.
    /// </summary>
    [Fact]
    public void Compile_TraceBufferScan_ReturnsFalseWhenNoRecordMatches()
    {
        var (_, _, repo) = Setup();
        repo.RegisterComponent<BTreeTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());

        ref var mem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
        mem.WriteNodeEvaluated(3, NodeStatus.Failure, 1);    // NodeIndex=3, Status=Failure
        mem.WriteNodeEvaluated(5, NodeStatus.Failure, 1);    // NodeIndex=5, Status=Failure (Status wrong)
        mem.WriteNodeEvaluated(7, NodeStatus.Running, 1);    // NodeIndex=7, Status=Running  (NodeIndex wrong)

        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var dto = new TraceBufferScanPredicateDto
        {
            ComponentType  = typeof(BTreeTraceWorkingMemory1024),
            OpCode         = (byte)BTreeTraceOpCode.NodeEvaluated,
            IndexField     = 5,
            MatchIndexField = true,
            StatusField    = (byte)NodeStatus.Running,
            MatchStatusField = true,
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        Assert.False(predicate(repo, entity));
    }

    /// <summary>
    /// Evaluating the compiled predicate against a full 63-record buffer must not
    /// allocate any GC heap memory (zero bytes per call).
    /// </summary>
    [Fact]
    public void Compile_TraceBufferScan_ZeroAllocations()
    {
        var (_, _, repo) = Setup();
        repo.RegisterComponent<BTreeTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());

        // Fill buffer to capacity with non-matching records (NodeIndex != 99).
        ref var mem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
        for (int i = 0; i < BTreeTraceWorkingMemory1024.CapacityRecords; i++)
            mem.WriteNodeEvaluated(i, NodeStatus.Running, 1);

        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var dto = new TraceBufferScanPredicateDto
        {
            ComponentType   = typeof(BTreeTraceWorkingMemory1024),
            OpCode          = (byte)BTreeTraceOpCode.NodeEvaluated,
            IndexField      = 99,        // no match intentionally
            MatchIndexField = true,
        };
        var predicate = compiler.CompileComponentPredicate(dto);

        // Warmup to JIT-compile the delegate.
        predicate(repo, entity);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        const int Iterations = 10_000;
        for (int i = 0; i < Iterations; i++)
            predicate(repo, entity);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }
}

// =============================================================================
// UBP-P5T2: BTree breakpoints end-to-end
// =============================================================================

/// <summary>
/// Integration tests for BTree trace-buffer breakpoints via <see cref="DataBreakpointSystem"/>.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class BTreeBreakpointTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var system        = new DataBreakpointSystem(manager);
        return (manager, system, repo);
    }

    /// <summary>
    /// A breakpoint scanning for NodeEvaluated/NodeIndex=7/Status=Running must fire
    /// when that record is present in the entity's BTree trace buffer.
    /// </summary>
    [Fact]
    public void BTree_BreakOnActivation_FiresWhenNodeEntersRunning()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<BTreeTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());

        // Write the target record to the trace buffer.
        ref var mem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
        mem.WriteNodeEvaluated(7, NodeStatus.Running, 1);

        bool hitFired = false;
        manager.OnBreakpointHit += (_, _) => hitFired = true;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "BTreeRunning",
            Condition           = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(BTreeTraceWorkingMemory1024),
                OpCode          = (byte)BTreeTraceOpCode.NodeEvaluated,
                IndexField      = 7,
                MatchIndexField = true,
                StatusField     = (byte)NodeStatus.Running,
                MatchStatusField = true,
            }
        });

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
        Assert.True(hitFired);
    }

    /// <summary>
    /// A breakpoint scanning for ScopePopped/StackDepth=2 must fire when that record
    /// is present in the entity's BTree trace buffer.
    /// </summary>
    [Fact]
    public void BTree_BreakOnAbort_FiresOnScopePopped()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<BTreeTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new BTreeTraceWorkingMemory1024());

        // Write a scope-popped record with StackDepth=2.
        // StackDepth aliases NodeIndex at offset 8 in the BTreeTraceRecord layout.
        ref var mem = ref repo.GetComponentRW<BTreeTraceWorkingMemory1024>(entity);
        mem.WriteScopePopped(stackDepth: 2, tick: 1);

        bool hitFired = false;
        manager.OnBreakpointHit += (_, _) => hitFired = true;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "BTreeScopePopped",
            Condition           = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(BTreeTraceWorkingMemory1024),
                OpCode          = (byte)BTreeTraceOpCode.ScopePopped,
                IndexField      = 2,      // StackDepth at offset 8
                MatchIndexField = true,
            }
        });

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
        Assert.True(hitFired);
    }
}

// =============================================================================
// UBP-P5T3: HSM breakpoints end-to-end
// =============================================================================

/// <summary>
/// Integration tests for HSM trace-buffer breakpoints via <see cref="DataBreakpointSystem"/>.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class HsmBreakpointTests
{
    private static (DataBreakpointManager manager, DataBreakpointSystem system, EntityRepository repo) Setup()
    {
        ComponentTypeRegistry.Clear();
        var repo          = new EntityRepository();
        var preTick       = new EntityRepository();
        var tc            = new MockDebugTimeController();
        var provider      = new DebugSnapshotProvider(preTick);
        var compiler      = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var eventCompiler = new EventScannerCompiler(new ComponentEditServiceBuilder().Build());
        var manager       = new DataBreakpointManager(repo, preTick, provider, tc, compiler, eventCompiler);
        var system        = new DataBreakpointSystem(manager);
        return (manager, system, repo);
    }

    /// <summary>
    /// Helper: write a StateEnter or StateExit record to an HsmTraceWorkingMemory1024
    /// component using the production <see cref="HsmTraceContext"/> write path.
    /// Components are stored in native memory so no GC pinning is needed.
    /// </summary>
    private static unsafe void WriteHsmStateChange(
        ref HsmTraceWorkingMemory1024 mem, ushort stateIndex, bool isEntry)
    {
        HsmTraceWorkingMemory1024* ptr = (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref mem);
        var ctx = new HsmTraceContext
        {
            Buffer        = ptr->Buffer,
            WritePos      = &ptr->WritePos,
            RecordCount   = &ptr->RecordCount,
            CapacityBytes = HsmTraceWorkingMemory1024.PayloadBytes,
            MaxRecords    = HsmTraceWorkingMemory1024.CapacityRecords,
            FilterLevel   = TraceLevel.All,
            CurrentTick   = 1,
        };
        ctx.WriteStateChange(instanceId: 0, stateIndex: stateIndex, isEntry: isEntry);
    }

    /// <summary>
    /// Helper: write a Transition record to an HsmTraceWorkingMemory1024 component.
    /// </summary>
    private static unsafe void WriteHsmTransition(
        ref HsmTraceWorkingMemory1024 mem, ushort fromState, ushort toState, ushort triggerEventId)
    {
        HsmTraceWorkingMemory1024* ptr = (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref mem);
        var ctx = new HsmTraceContext
        {
            Buffer        = ptr->Buffer,
            WritePos      = &ptr->WritePos,
            RecordCount   = &ptr->RecordCount,
            CapacityBytes = HsmTraceWorkingMemory1024.PayloadBytes,
            MaxRecords    = HsmTraceWorkingMemory1024.CapacityRecords,
            FilterLevel   = TraceLevel.All,
            CurrentTick   = 1,
        };
        ctx.WriteTransition(instanceId: 0, fromState: fromState, toState: toState, triggerEventId: triggerEventId);
    }

    /// <summary>
    /// A breakpoint scanning for StateEnter/StateIndex=3 must fire when that record
    /// is present in the entity's HSM trace buffer.
    /// </summary>
    [Fact]
    public void HSM_BreakOnEnter_FiresOnStateEnter()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<HsmTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new HsmTraceWorkingMemory1024());

        // Write a StateEnter record for state index 3.
        ref var mem = ref repo.GetComponentRW<HsmTraceWorkingMemory1024>(entity);
        WriteHsmStateChange(ref mem, stateIndex: 3, isEntry: true);

        bool hitFired = false;
        manager.OnBreakpointHit += (_, _) => hitFired = true;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "HsmStateEnter",
            Condition           = new TraceBufferScanPredicateDto
            {
                ComponentType   = typeof(HsmTraceWorkingMemory1024),
                OpCode          = (byte)TraceOpCode.StateEnter,
                IndexField      = 3,     // StateIndex at offset 8
                MatchIndexField = true,
            }
        });

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
        Assert.True(hitFired);
    }

    /// <summary>
    /// A breakpoint scanning for Transition/TriggerEventId=42 must fire when a
    /// Transition record with that event id is present in the HSM trace buffer.
    /// </summary>
    [Fact]
    public void HSM_BreakOnTransition_MatchesTriggerEventId()
    {
        var (manager, system, repo) = Setup();
        repo.RegisterComponent<HsmTraceWorkingMemory1024>();

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new HsmTraceWorkingMemory1024());

        // Write a Transition record with TriggerEventId = 42.
        ref var mem = ref repo.GetComponentRW<HsmTraceWorkingMemory1024>(entity);
        WriteHsmTransition(ref mem, fromState: 1, toState: 5, triggerEventId: 42);

        bool hitFired = false;
        manager.OnBreakpointHit += (_, _) => hitFired = true;

        manager.Add(new Breakpoint
        {
            Id                  = BreakpointId.Invalid,
            Enabled             = true,
            OccurrenceThreshold = 1,
            DisplayName         = "HsmTransitionEv42",
            Condition           = new TraceBufferScanPredicateDto
            {
                ComponentType      = typeof(HsmTraceWorkingMemory1024),
                OpCode             = (byte)TraceOpCode.Transition,
                TriggerEventId     = 42,
                MatchTriggerEventId = true,
            }
        });

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
        Assert.True(hitFired);
    }
}
```

---

## Summary of all changes

| # | File | Action |
|---|------|--------|
| 1 | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` | Add `[JsonDerivedType]` + `TraceBufferScanPredicateDto` class |
| 2 | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs` | Add `using`, new case, `CompileTraceBufferScan`, `BuildTraceBufferScanMatcher<T>`, update `CollectMandatoryComponents` |
| 3 | `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TraceBufferScanTests.cs` | NEW — 7 tests across 3 test classes |

No changes to `.csproj` files. No changes to `DataBreakpointSystem`, `DataBreakpointManager`, or any Hrot production code — the compiler extension slots in automatically via the existing `IPredicateCompiler` dispatch.

---

## Critical notes for implementation

1. **`unsafe` in lambda**: `BuildTraceBufferScanMatcher<T>` must be declared `private static unsafe Func<...>`. The lambda body inside can also use `unsafe` directly since the method is already `unsafe`.

2. **`Unsafe.AsPointer` pattern**: Use `Unsafe.AsPointer(ref Unsafe.AsRef(in comp))` to convert a `ref readonly T` to `byte*`. This is the existing pattern used in `HsmTickSystem.cs` and the behavior-param matchers.

3. **`RecordCount` is at offset 2**: Not offset 4. WritePos=0(2 bytes), RecordCount=2(2 bytes), LastInstanceId=4(4 bytes), Buffer=8(1016 bytes).

4. **Buffer base pointer**: `ptr + 8` — headers are exactly 8 bytes.

5. **No changes to `CompilePropertyMatch` path**: `TraceBufferScanPredicateDto` is handled by its own case before the fall-through block. The `BuildUnmanagedMatcher<T>` path for `PropertyMatchDto` is NOT used for trace buffers (it would fail because `fixed` fields aren't reflected).

6. **`HsmTraceContext.WriteStateChange` filter**: The context's `FilterLevel` must include `TraceLevel.StateChanges` (included in `TraceLevel.All`) for state change records to be written. Set `FilterLevel = TraceLevel.All` in helpers.

7. **`HsmTraceContext.WriteTransition` filter**: Requires `TraceLevel.Transitions` in `FilterLevel`.

8. **`ptr->Buffer` in unsafe context**: For a `HsmTraceWorkingMemory1024*` pointer, `ptr->Buffer` yields `byte*` pointing to the first byte of the fixed buffer — this is valid C# unsafe syntax for `fixed`-array fields accessed through a pointer.

9. **Test collection**: All 3 test classes use `[Collection("ComponentRegistry")]` because they call `ComponentTypeRegistry.Clear()`.

10. **`BTreeTraceWorkingMemory1024.WriteScopePopped(stackDepth, tick)`**: Writes `OpCode = BTreeTraceOpCode.ScopePopped` with `StackDepth` at offset 8. The scanner matches `StackDepth` using `IndexField` (same byte offset 8).

---

## Build & test command
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --filter "TraceBufferScan" -c Debug
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj --filter "TraceBuffer|BTreeBreak|HsmBreak" -c Debug
```
Also run the full Breakpoints test suite to confirm no regressions:
```
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -c Debug
```

---

## Report format
Create `.dev/breakpoints-1/reports/BATCH-42-REPORT.md` with:
- Summary of changes made
- Test run output (pass/fail counts, any errors)
- Any deviations from instructions and reasons

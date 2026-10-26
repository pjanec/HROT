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
        mem.WriteNodeEvaluated(3, NodeStatus.Failure, 1);    // no match -- wrong NodeIndex + Status
        mem.WriteNodeEvaluated(5, NodeStatus.Running, 1);    // MATCH
        mem.WriteScopePushed(1, 1);                          // no match -- wrong OpCode

        var compiler = new PredicateCompiler(new ComponentEditServiceBuilder().Build());
        var dto = new TraceBufferScanPredicateDto
        {
            ComponentType   = typeof(BTreeTraceWorkingMemory1024),
            OpCode          = (byte)BTreeTraceOpCode.NodeEvaluated,
            IndexField      = 5,
            MatchIndexField = true,
            StatusField     = (byte)NodeStatus.Running,
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
            ComponentType   = typeof(BTreeTraceWorkingMemory1024),
            OpCode          = (byte)BTreeTraceOpCode.NodeEvaluated,
            IndexField      = 5,
            MatchIndexField = true,
            StatusField     = (byte)NodeStatus.Running,
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
                ComponentType    = typeof(BTreeTraceWorkingMemory1024),
                OpCode           = (byte)BTreeTraceOpCode.NodeEvaluated,
                IndexField       = 7,
                MatchIndexField  = true,
                StatusField      = (byte)NodeStatus.Running,
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
                ComponentType       = typeof(HsmTraceWorkingMemory1024),
                OpCode              = (byte)TraceOpCode.Transition,
                TriggerEventId      = 42,
                MatchTriggerEventId = true,
            }
        });

        system.Execute(repo, 0f);

        Assert.True(manager.IsPaused);
        Assert.True(hitFired);
    }
}

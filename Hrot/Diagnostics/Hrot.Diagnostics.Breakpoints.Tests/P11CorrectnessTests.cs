using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Debug;
using Hrot.Diagnostics.Breakpoints;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Diagnostics.Breakpoints.Tests;

// ---- Test-only components (file-scoped to avoid ID conflicts) ---------------

[ComponentId(260)]
file struct TestPosition2D
{
    public float X;
    public float Y;
}

// =============================================================================
// Class 1: StageMutationSizeTests (P11T8)
// =============================================================================

/// <summary>
/// Tests that <see cref="DataBreakpointManager.StageMutation"/> stores the CLR managed size
/// (Unsafe.SizeOf&lt;T&gt;()) rather than the interop layout size (Marshal.SizeOf&lt;T&gt;()).
/// </summary>
[Collection("ComponentRegistry")]
public sealed class StageMutationSizeTests
{
    /// <summary>
    /// Verifies that the staged size equals Unsafe.SizeOf&lt;TestHealth&gt;().
    /// </summary>
    [Fact]
    public void StageMutation_SimpleStruct_SizeMatchesUnsafeSizeOf()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        var value  = new TestHealth { Current = 42 };
        manager.StageMutation(entity, typeof(TestHealth), value);

        var mutation = manager.PendingMutationsQueue.Peek();
        int expected = System.Runtime.CompilerServices.Unsafe.SizeOf<TestHealth>();
        Assert.Equal(expected, mutation.SizeBytes);
    }

    /// <summary>
    /// Documents the contract: the stored size must equal Unsafe.SizeOf&lt;T&gt;() (CLR managed /
    /// chunk stride), NOT Marshal.SizeOf&lt;T&gt;() (interop layout -- may differ for fixed-buffer
    /// components).
    /// </summary>
    [Fact]
    public void StageMutation_StagedSize_EqualsManagedSize_NotInteropSize()
    {
        ComponentTypeRegistry.Clear();
        var (manager, liveRepo, _, _) = ManagerFactory.Create();
        liveRepo.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        manager.StageMutation(entity, typeof(TestHealth), new TestHealth { Current = 10 });

        var mutation = manager.PendingMutationsQueue.Peek();
        // The stored size must equal Unsafe.SizeOf<T>() (CLR managed / chunk stride),
        // NOT Marshal.SizeOf<T>() (interop layout -- may differ for fixed-buffer components).
        int clrSize = System.Runtime.CompilerServices.Unsafe.SizeOf<TestHealth>();
        Assert.Equal(clrSize, mutation.SizeBytes);
        // For TestHealth (simple int field), both sizes happen to be equal.
        // For fixed-buffer components like BTreeTraceWorkingMemory1024, they would differ.
    }
}

// =============================================================================
// Class 2: SpatialPositionAccessorTests (P11T10)
// =============================================================================

/// <summary>
/// Tests that the compiled spatial-position accessor built at mount time correctly
/// reads positions from unmanaged position components.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class SpatialPositionAccessorTests
{
    private static DataBreakpointManager CreateManager(EntityRepository liveRepo)
    {
        var preTick  = new EntityRepository();
        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTick);
        return new DataBreakpointManager(liveRepo, preTick, provider, tc);
    }

    /// <summary>
    /// Verifies that the compiled accessor returns the correct position and the spatial
    /// breakpoint fires when the entity is inside the bounds.
    /// </summary>
    [Fact]
    public void SpatialTracker_CompiledAccessor_ReturnsCorrectPosition()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo = new EntityRepository();
        var manager  = CreateManager(liveRepo);

        liveRepo.RegisterComponent<TestPosition2D>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestPosition2D { X = 3.0f, Y = 7.5f });

        manager.AddBreakpoint(new SpatialBoundingPredicateDto
        {
            PositionComponentType = typeof(TestPosition2D),
            PositionXPath         = "X",
            PositionYPath         = "Y",
            TriggerEvent          = BoundaryEvent.EntryOrExit,
            Bounds                = new BoundingBox2D
            {
                Min = new Vector2(0f, 0f),
                Max = new Vector2(10f, 10f)
            }
        });

        // Entity at (3, 7.5) is inside [0-10, 0-10] bounds -- should fire on first evaluation.
        manager.EvaluateStatefulBreakpoints(liveRepo);

        Assert.True(manager.IsPaused, "Entity at (3, 7.5) should be inside [0-10, 0-10] bounds");
    }

    /// <summary>
    /// Verifies that the spatial breakpoint does not fire when the entity is outside the bounds.
    /// </summary>
    [Fact]
    public void SpatialTracker_CompiledAccessor_DoesNotFireOutsideBounds()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo = new EntityRepository();
        var manager  = CreateManager(liveRepo);

        liveRepo.RegisterComponent<TestPosition2D>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestPosition2D { X = 50.0f, Y = 50.0f }); // outside

        manager.AddBreakpoint(new SpatialBoundingPredicateDto
        {
            PositionComponentType = typeof(TestPosition2D),
            PositionXPath         = "X",
            PositionYPath         = "Y",
            TriggerEvent          = BoundaryEvent.EntryOrExit,
            Bounds                = new BoundingBox2D
            {
                Min = new Vector2(0f, 0f),
                Max = new Vector2(10f, 10f)
            }
        });

        manager.EvaluateStatefulBreakpoints(liveRepo);

        Assert.False(manager.IsPaused, "Entity at (50, 50) should be outside [0-10, 0-10] bounds");
    }
}

// =============================================================================
// Class 3: LifecycleNetworkIdTests (P11T13)
// =============================================================================

/// <summary>
/// Tests that MatchesLifecycleCriteria throws NotSupportedException for NetworkId
/// rather than silently returning false.
/// </summary>
[Collection("ComponentRegistry")]
public sealed class LifecycleNetworkIdTests
{
    private static DataBreakpointManager CreateManager(EntityRepository liveRepo)
    {
        var preTick  = new EntityRepository();
        var tc       = new MockDebugTimeController();
        var provider = new DebugSnapshotProvider(preTick);
        return new DataBreakpointManager(liveRepo, preTick, provider, tc);
    }

    /// <summary>
    /// Verifies that evaluating a lifecycle breakpoint with NetworkId identifier throws
    /// NotSupportedException instead of silently skipping the entity.
    /// </summary>
    [Fact]
    public void Lifecycle_NetworkId_NoMapWired_ThrowsNotSupportedException()
    {
        ComponentTypeRegistry.Clear();
        var liveRepo = new EntityRepository();
        var manager  = CreateManager(liveRepo);

        liveRepo.RegisterComponent<TestHealth>();

        var entity = liveRepo.CreateEntity();
        liveRepo.AddComponent(entity, new TestHealth { Current = 10 });

        manager.AddBreakpoint(new LifecyclePredicateDto
        {
            IdentifierType = EntityIdentifierType.NetworkId,
            TargetValue    = "42",
        });

        Assert.Throws<NotSupportedException>(() =>
            manager.EvaluateStatefulBreakpoints(liveRepo));
    }
}

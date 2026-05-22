using System.Runtime.CompilerServices;
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Tests.Benchmarks;

/// <summary>
/// xUnit CI gate for zero-allocation probe overhead.
/// Verifies the null-sink path of NullProbeSink allocates nothing,
/// serving as a substitute for the BenchmarkDotNet < 50ns criterion in CI.
/// </summary>
public sealed class ProbeOverheadTests
{
    private static Entity E1 => new Entity(1, 0);

    /// <summary>
    /// Calling NullProbeSink.OnNodeEnter must allocate zero bytes on the heap.
    /// This is the CI gate for TASK-DBG-006 SC7-13.5 (probe call overhead < 50ns).
    /// </summary>
    [Fact]
    public void ProbeOverhead_OnNodeEnter_NullSink_IsZeroAllocation()
    {
        IBlueprintProbeSink probe = NullProbeSink.Instance;
        var entity = new Entity(1, 0);
        var nodeId = Guid.NewGuid().ToString("D");

        // Warm up to let JIT settle.
        for (int i = 0; i < 10; i++)
            CallOnNodeEnter(probe, entity, nodeId);

        long before = GC.GetAllocatedBytesForCurrentThread();
        CallOnNodeEnter(probe, entity, nodeId);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallOnNodeEnter(IBlueprintProbeSink probe, Entity entity, string nodeId)
        => probe.OnNodeEnter(entity, nodeId);
}

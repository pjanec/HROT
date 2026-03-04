using System.Collections.Generic;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.Map.Common;
using Xunit;

namespace Bagira.Runner.Integration.Tests;

/// <summary>
/// Integration tests that verify the <c>DdsIdAllocator</c> / <c>DdsIdAllocatorServer</c>
/// discovery handshake works correctly when all three subsystems (SimHost, IG, IOS) are
/// started together via <see cref="BagiraRunnerHarness"/>.
///
/// <para>
/// The original bug: <c>DdsIdAllocator</c> sent its first allocation request in the
/// constructor, before DDS pub/sub discovery had completed between the client writer and
/// the server reader.  The write was silently dropped ("write-before-match"), causing the
/// first <c>AllocateId()</c> call to spin for up to three seconds or to exhaust retries.
/// </para>
/// <para>
/// The fix: the client now subscribes to <c>DdsWriter.PublicationMatched</c> and defers
/// the first request until the server's reader is matched.  <c>AllocateId()</c> blocks
/// callers for at most <c>DdsIdAllocator.DiscoveryTimeout</c> (3 s) while waiting for the
/// match, then returns an ID.  <c>SimHostSubsystem</c> also starts the server on its own
/// background thread <em>before</em> creating the client so the DDS match happens as quickly
/// as possible.  The harness no longer creates a second server (which previously caused
/// duplicate ID ranges).
/// </para>
/// </summary>
public class IdAllocatorDiscoveryTests
{
    private static readonly GeoPosition BerlinGeo =
        new GeoPosition { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };

    /// <summary>
    /// Verifies that the very first <c>AllocateId()</c> call succeeds immediately after
    /// <see cref="BagiraRunnerHarness"/> initialises SimHost — even though the DDS
    /// participants may not yet have fully completed discovery at construction time.
    /// </summary>
    [Fact]
    public void FirstIdAllocation_SucceedsRightAfterInitialisation()
    {
        using var harness = new BagiraRunnerHarness();

        // Act — TestHook_SpawnEntity internally calls _idAllocator.AllocateId().
        // Before the fix this would hang / throw when initial DDS match hadn't completed.
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams, BerlinGeo);

        Assert.True(networkId > 0,
            $"Expected a positive network ID on the first allocation; got {networkId}.");
    }

    /// <summary>
    /// Verifies that multiple sequential allocations all return strictly positive,
    /// unique IDs (no duplicates from a rogue second server).
    /// </summary>
    [Fact]
    public void MultipleSequentialAllocations_ReturnUniquePositiveIds()
    {
        using var harness = new BagiraRunnerHarness();

        const int count = 10;
        var seen = new HashSet<long>();

        for (int i = 0; i < count; i++)
        {
            long id = harness.SimHost.TestHook_SpawnEntity(
                TkbEntityTypes.Tank_M1Abrams, BerlinGeo);

            Assert.True(id > 0, $"Allocation {i}: expected positive ID, got {id}.");
            Assert.True(seen.Add(id),
                $"Allocation {i}: duplicate ID {id} — two servers may be allocating from the same range.");
        }
    }

    /// <summary>
    /// Regression test: previously the harness created a <em>second</em>
    /// <c>DdsIdAllocatorServer</c> on a separate DDS participant as a workaround for the
    /// discovery timing issue.  Both servers started their counter at 1, so every request
    /// returned two overlapping responses (IDs 1–100 from each server) leading to duplicate
    /// IDs after the first chunk was consumed.  With the fix, only
    /// <c>SimHostSubsystem</c>'s server runs and IDs are strictly sequential.
    /// </summary>
    [Fact]
    public void AllocatedIds_AreStrictlyIncreasing_WithSingleServer()
    {
        using var harness = new BagiraRunnerHarness();

        const int count = 5;
        long previous = 0;

        for (int i = 0; i < count; i++)
        {
            long id = harness.SimHost.TestHook_SpawnEntity(
                TkbEntityTypes.Tank_M1Abrams, BerlinGeo);

            Assert.True(id > previous,
                $"Allocation {i}: expected ID > {previous} (strictly increasing), got {id}. " +
                "Duplicate or out-of-order IDs indicate multiple servers are running.");

            previous = id;
        }
    }
}

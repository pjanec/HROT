using CarKinem.Trajectory;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// <b><c>B4b</c> — the first host resource taken from a PROVIDER rather than a module's default.</b>
///
/// <para>The trajectory pool is the case where sharing is not an optimisation: the navigation solver
/// writes resolved routes into it <i>by handle</i>, and <c>FormationTargetSystem</c> /
/// <c>CarKinematicsSystem</c> read them back by that handle. Two pools do not leak — they make routes
/// resolve that no vehicle ever follows, with no exception and nothing in a log
/// (<c>CE-180</c>).</para>
/// </summary>
public sealed class TrajectoryPoolProviderTests
{
    /// <summary>
    /// The pack must BORROW the provider's pool. Before <c>B4b</c> it defaulted its own and every
    /// other consumer had to be threaded from it by hand — correct only while one caller remembered.
    /// </summary>
    [Fact]
    public void TheCoreLogicPackBorrowsTheProvidersPoolRatherThanDefaultingItsOwn()
    {
        using var provider = new TrajectoryPoolProvider();
        var pack = new SimHostCoreLogicPack(
            new Fdp.Toolkit.Replication.Services.NetworkEntityMap(),
            default,
            provider.Pool);

        Assert.Same(provider.Pool, pack.TrajectoryPool);

        // Borrowed: disposing the pack must not free the provider's pool, because the navigation
        // module still holds it. A route registered before the pack goes away is still readable.
        pack.TrajectoryPool.RegisterTrajectoryWithKey(
            new[] { new System.Numerics.Vector3(0, 0, 0), new System.Numerics.Vector3(10, 0, 0) }, key: 42);
        pack.Dispose();

        Assert.True(provider.Pool.TryGetTrajectory(42, out _));
    }

    /// <summary>The provider owns the lifetime, and freeing twice must not corrupt the allocator.</summary>
    [Fact]
    public void TheProviderFreesThePoolExactlyOnce()
    {
        var provider = new TrajectoryPoolProvider();
        Assert.NotNull(provider.Pool);

        provider.Dispose();
        provider.Dispose();
    }

    /// <summary>Its identity is the declared resource key, so a capability's Needs can name it.</summary>
    [Fact]
    public void TheProviderIsIdentifiedByTheDeclaredResourceKey()
    {
        using var provider = new TrajectoryPoolProvider();
        Assert.Equal(ResourceKeys.TrajectoryPool, provider.Key);
    }
}

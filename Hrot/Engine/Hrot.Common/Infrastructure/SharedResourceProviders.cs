using CarKinem.Trajectory;
using Fdp.Toolkit.Perception.Modules;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Owns the node's one <see cref="TrajectoryPoolManager"/>.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (<c>B4b</c>).</b> The pool is written by the navigation solver
/// (<c>PathfindingSolverSystem</c> registers resolved routes by handle) and read by the kinematics
/// side (<c>FormationTargetSystem</c>, <c>CarKinematicsSystem</c> look them up by that handle). Two
/// pools therefore do not leak — they make <b>routes resolve that no vehicle follows</b>, silently
/// (<c>CE-180</c>).</para>
///
/// <para><b>What changes by making it a provider.</b> Before this, the pool was <i>defaulted</i>
/// inside <c>GroundKinematicsModule</c> and every consumer had to be threaded from there by hand —
/// which worked only because exactly one production caller remembered to do it. Now the node
/// allocates it once, up front, because some selected capability declared
/// <see cref="ResourceKeys.TrajectoryPool"/>, and every consumer receives the same instance by
/// construction rather than by care.</para>
///
/// <para><b>It disposes.</b> Nothing in production disposed a <c>TrajectoryPoolManager</c> before
/// <c>B3</c> part 3, and even then the owner had no caller. A provider's lifetime is the node's, so
/// this closes that gap for every host that adopts it.</para>
/// </remarks>
public sealed class TrajectoryPoolProvider : INodeResourceProvider
{
    private bool _disposed;

    /// <inheritdoc/>
    public string Key => ResourceKeys.TrajectoryPool;

    /// <summary>The pool. Available before <see cref="Allocate"/> so a host mid-migration can hand it
    /// to a consumer it has not yet expressed as a capability.</summary>
    /// <remarks>
    /// ⚠ That accessor is the migration boundary, not a design feature — the same role
    /// <c>NodeBootPlan.Value{T}</c> plays for a partly-migrated host, and it should shrink as hosts
    /// finish adopting. A fully migrated consumer reads the pool from <see cref="NodeBootValues"/>,
    /// where the read is checked against its declared needs.
    /// </remarks>
    public TrajectoryPoolManager Pool { get; } = new();

    /// <inheritdoc/>
    public void Allocate(HrotNodeContext context, NodeBootValues values) => values.Set(Key, Pool);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Pool.Dispose();
    }
}

/// <summary>
/// Owns the node's one perception <c>SpatialHashGrid</c>, wrapping the provider built in <c>B3</c>
/// part 1 so it can be selected by declared need like every other resource.
/// </summary>
/// <remarks>
/// <see cref="PerceptionGridProvider"/> already allocates and frees correctly; this adds only the
/// <see cref="INodeResourceProvider"/> identity and the publish step, so there is one provider
/// concept rather than two.
/// </remarks>
public sealed class PerceptionGridResourceProvider : INodeResourceProvider
{
    private bool _disposed;

    /// <inheritdoc/>
    public string Key => ResourceKeys.PerceptionGrid;

    /// <summary>The underlying grid owner. See <see cref="TrajectoryPoolProvider.Pool"/> for why a
    /// direct accessor exists during migration.</summary>
    public PerceptionGridProvider Grid { get; } = new();

    /// <inheritdoc/>
    public void Allocate(HrotNodeContext context, NodeBootValues values) => values.Set(Key, Grid);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Grid.Dispose();
    }
}

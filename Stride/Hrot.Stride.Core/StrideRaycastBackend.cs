#nullable enable
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;

namespace Hrot.Stride.Core;

/// <summary>
/// Adapter from <see cref="IRaycastBackend"/> to <see cref="IStrideRaycastService"/>,
/// bridging the <c>Fdp.Toolkits.Physics</c> seam to the Stride/Bullet physics engine
/// (STR-P3-T3).
///
/// <para>
/// <b>Dependency direction invariant.</b>
/// <c>Fdp.Toolkits</c> defines <see cref="IRaycastBackend"/> and knows nothing about
/// <c>Hrot.Stride.Core</c>.  This adapter lives in <c>Hrot.Stride.Core</c>, which
/// references <c>Fdp.Toolkits</c>.  <c>Fdp.Toolkits</c> never references
/// <c>Hrot.Stride.Core</c>.
/// </para>
///
/// <para>
/// <b>How it plugs in.</b>
/// At Stride node startup, after the <c>PhysicsProcessor</c> is running:
/// <code>
/// var raycastService = new StrideRaycastService(simulation);
/// physicsQueryModule.RaycastSolverSystem.RaycastBackend
///     = new StrideRaycastBackend(raycastService);
/// </code>
/// The spatial-hash path in <see cref="Fdp.Toolkit.Physics.Systems.RaycastSolverSystem"/>
/// is then bypassed for all ray requests.
/// </para>
///
/// <para>
/// <b>Blocked-shot semantics.</b>
/// If geometry is hit before the target, <c>T &lt; 1.0</c> and <c>HitEntity</c> may be
/// <see cref="Entity.Null"/> (static wall) or the blocking entity.  The
/// <see cref="Fdp.Toolkit.Physics.Systems.HitResolutionSystem"/> uses <c>T</c> to compute
/// the detonation point as <c>Start + T * (End - Start)</c> — correctly placing the
/// explosion at the wall, not the target.
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> all calls arrive on the single Stride host thread (design §8.3).
/// </para>
/// </summary>
public sealed class StrideRaycastBackend : IRaycastBackend
{
    private readonly IStrideRaycastService _raycast;

    /// <summary>
    /// Fraction of the ray length within which a hit is considered "before" the endpoint.
    /// Hits at or beyond this fraction are treated as misses (the ray reached its target).
    /// Default: 0.999.
    /// </summary>
    public float HitFractionClearThreshold { get; set; } = 0.999f;

    /// <summary>
    /// Creates a <see cref="StrideRaycastBackend"/> using the supplied raycast service.
    /// </summary>
    /// <param name="raycast">
    /// The <see cref="IStrideRaycastService"/> to delegate to.
    /// Use <see cref="FakeStrideRaycastService"/> in tests, the concrete
    /// <c>StrideRaycastService</c> on the GPU node.
    /// </param>
    public StrideRaycastBackend(IStrideRaycastService raycast)
    {
        _raycast = raycast ?? throw new ArgumentNullException(nameof(raycast));
    }

    /// <inheritdoc/>
    public RaycastHit Raycast(
        Vector3 start,
        Vector3 end,
        long    rayId,
        int     layerMask,
        Entity  ignoreEntity,
        Entity  observerEntity,
        Entity  targetEntity)
    {
        // Delegate to the Stride raycast service (converts FDP↔Stride internally).
        var strideHit = _raycast.Raycast(start, end);

        // Build the FDP RaycastHit.
        var hit = new RaycastHit
        {
            RayId        = rayId,
            Observer     = observerEntity,
            Target       = targetEntity,
            IgnoreEntity = ignoreEntity,
            Start        = start,
            End          = end,
        };

        if (!strideHit.HasHit || strideHit.HitFraction >= HitFractionClearThreshold)
        {
            // No hit or hit at/beyond the endpoint — miss.
            hit.HasHit    = 0;
            hit.T         = 1f;
            hit.HitEntity = default;
            return hit;
        }

        hit.HasHit    = 1;
        hit.T         = strideHit.HitFraction;
        hit.HitEntity = strideHit.HitEntity;
        return hit;
    }
}

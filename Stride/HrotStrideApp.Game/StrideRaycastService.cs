#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Hrot.Stride.Core;
using Stride.Physics;
using SMath = Stride.Core.Mathematics;

namespace HrotStrideApp;

/// <summary>
/// Concrete implementation of <see cref="IStrideRaycastService"/> backed by Stride's
/// <c>Simulation.Raycast</c> (Bullet physics).
///
/// <para>
/// <b>GPU-deferred (STR-D11).</b>
/// <c>Stride.Physics.Simulation</c> and all its raycast methods are owned by
/// <c>PhysicsProcessor</c> (internals to <c>Stride.Physics</c>). A live
/// <c>Simulation</c> instance is only available once <c>Game.Run()</c> has started and the
/// Stride scene is loaded with a running <c>PhysicsProcessor</c>.  Therefore this class
/// lives in <c>HrotStrideApp.Game</c> (not in <c>Hrot.Stride.Core</c>) and is injected
/// at runtime via the <see cref="IStrideRaycastService"/> seam.  Headless tests use
/// <see cref="Hrot.Stride.Core.FakeStrideRaycastService"/> instead.
/// </para>
///
/// <para>
/// <b>Coordinate conversion.</b>
/// All FDP inputs are converted to Stride space via <c>FdpStrideTransform.ToStridePosition</c>
/// before the raycast, and all Stride outputs (point, normal) are converted back via
/// <c>FdpStrideTransform.ToFdpPosition</c> (hit point) and
/// <c>FdpStrideTransform.ToFdpVelocity</c> (hit normal — direction swizzle, not position).
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> must be called on the single Stride host thread
/// (design §8.3). All callers that reach this service come through the FDP kernel tick,
/// which runs on the host thread.
/// </para>
/// </summary>
public sealed class StrideRaycastService : IStrideRaycastService
{
    private readonly Simulation _simulation;

    /// <summary>
    /// Creates a <see cref="StrideRaycastService"/> wrapping an active <paramref name="simulation"/>.
    /// </summary>
    /// <param name="simulation">
    /// The running Bullet <c>Simulation</c> obtained from a live <c>PhysicsProcessor</c>
    /// (e.g. via <c>ScriptComponent.GetSimulation()</c> or
    /// <c>Entity.GetSimulation()</c> inside a Stride script / game system).
    /// </param>
    /// <exception cref="ArgumentNullException">When <paramref name="simulation"/> is null.</exception>
    public StrideRaycastService(Simulation simulation)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    }

    /// <inheritdoc/>
    public StrideRaycastHit Raycast(
        Vector3 fromFdp,
        Vector3 toFdp,
        int     collisionGroups = -1,
        int     collisionFilter = -1)
    {
        SMath.Vector3 from = FdpStrideTransform.ToStridePosition(fromFdp);
        SMath.Vector3 to   = FdpStrideTransform.ToStridePosition(toFdp);

        HitResult hit;

        // Use the filter overload when caller specifies non-default masks.
        if (collisionGroups == -1 && collisionFilter == -1)
        {
            hit = _simulation.Raycast(from, to);
        }
        else
        {
            var groups = (CollisionFilterGroups)collisionGroups;
            var filter = (CollisionFilterGroupFlags)collisionFilter;
            hit = _simulation.Raycast(from, to, groups, filter);
        }

        return ToFdpHit(hit);
    }

    /// <inheritdoc/>
    public void RaycastPenetrating(
        Vector3                      fromFdp,
        Vector3                      toFdp,
        IList<StrideRaycastHit>      hits,
        int                          collisionGroups = -1,
        int                          collisionFilter = -1)
    {
        if (hits == null) throw new ArgumentNullException(nameof(hits));

        SMath.Vector3 from = FdpStrideTransform.ToStridePosition(fromFdp);
        SMath.Vector3 to   = FdpStrideTransform.ToStridePosition(toFdp);

        var rawHits = new List<HitResult>();

        if (collisionGroups == -1 && collisionFilter == -1)
        {
            _simulation.RaycastPenetrating(from, to, rawHits);
        }
        else
        {
            var groups = (CollisionFilterGroups)collisionGroups;
            var filter = (CollisionFilterGroupFlags)collisionFilter;
            _simulation.RaycastPenetrating(from, to, rawHits, groups, filter);
        }

        // Convert each Stride hit to FDP space and append.
        foreach (var raw in rawHits)
            hits.Add(ToFdpHit(raw));
    }

    // ── Conversion helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a Stride <see cref="HitResult"/> to an <see cref="StrideRaycastHit"/>
    /// in FDP world space.
    ///
    /// <para>
    /// <b>Normal swizzle:</b> the normal is a direction vector; it uses
    /// <c>FdpStrideTransform.ToFdpVelocity</c> (direction / velocity swizzle), NOT
    /// <c>FdpStrideTransform.ToFdpPosition</c> (position swizzle).  Both apply the same
    /// axis mapping <c>(stride.X, stride.Z, stride.Y)</c> for pure-direction vectors,
    /// but using the explicit velocity path makes the intent clear and is robust against
    /// any future translation offset being added to the position path.
    /// </para>
    /// </summary>
    private static StrideRaycastHit ToFdpHit(in HitResult hit)
    {
        if (!hit.Succeeded)
            return StrideRaycastHit.Miss;

        // Position swizzle for the hit point.
        Vector3 pointFdp  = FdpStrideTransform.ToFdpPosition(hit.Point);

        // Direction (velocity) swizzle for the surface normal — NOT the position swizzle.
        Vector3 normalFdp = FdpStrideTransform.ToFdpVelocity(hit.Normal);

        // The Stride collider may carry an FDP entity handle in its user-object slot.
        // PhysicsBodyLifecycleSystem tags the Stride entity name with the FDP entity
        // Index packed as a decimal string.  Static scene geometry has no such tag
        // and resolves to Entity.Null.
        Entity hitEntity = Entity.Null;
        if (hit.Collider?.Entity?.Name is string name &&
            int.TryParse(name, out int idx))
        {
            // Reconstruct entity with index only (generation 1 = first allocation).
            // The generation field is best-effort here; callers must verify liveness
            // via EntityRepository.IsAlive before using the handle.
            hitEntity = new Entity(idx, 1);
        }

        return new StrideRaycastHit(
            hasHit:      true,
            pointFdp:    pointFdp,
            normalFdp:   normalFdp,
            hitFraction: hit.HitFraction,
            hitEntity:   hitEntity);
    }
}

#nullable enable
using System.Numerics;
using Fdp.Core;

namespace Hrot.Stride.Core;

/// <summary>
/// Seam interface for Stride/Bullet raycast queries — all I/O in FDP world-space
/// (right-handed, X=East, Y=North, Z=Up).
///
/// <para>
/// <b>Design rationale (STR-D11 pattern).</b>
/// <c>Stride.Physics.Simulation</c> and its <c>Raycast</c> methods are owned by
/// <c>PhysicsProcessor</c> (internals to <c>Stride.Physics</c>) and cannot be
/// instantiated headlessly without a running <c>Stride.Scene</c> + <c>Game</c>.
/// Therefore the concrete <see cref="StrideRaycastService"/> implementation is
/// GPU-deferred: it lives in <c>HrotStrideApp.Game</c> and is injected at
/// runtime when a live <c>PhysicsProcessor</c> is available.
/// </para>
///
/// <para>
/// Tests exercise the coordinate-conversion and mask-plumbing logic via
/// <see cref="FakeStrideRaycastService"/> (or any scriptable fake that implements
/// this interface), without needing a live <c>Simulation</c>.
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> All calls occur on the single host thread
/// (design §8.3). No cross-thread access of the underlying <c>Simulation</c>.
/// </para>
/// </summary>
public interface IStrideRaycastService
{
    // ── Point raycast ──────────────────────────────────────────────────────────

    /// <summary>
    /// Performs a single-hit raycast from <paramref name="fromFdp"/> to <paramref name="toFdp"/>
    /// in FDP world space, filtered by <paramref name="collisionGroups"/> and
    /// <paramref name="collisionFilter"/>.
    /// </summary>
    /// <param name="fromFdp">Ray start in FDP world space.</param>
    /// <param name="toFdp">Ray end in FDP world space.</param>
    /// <param name="collisionGroups">Collision groups bitmask for the ray source.</param>
    /// <param name="collisionFilter">Collision group bitmask for the ray target filter.</param>
    /// <returns>
    /// A <see cref="StrideRaycastHit"/> describing the closest hit in FDP space,
    /// or <see cref="StrideRaycastHit.Miss"/> if nothing was hit.
    /// </returns>
    StrideRaycastHit Raycast(
        Vector3 fromFdp,
        Vector3 toFdp,
        int     collisionGroups = -1,
        int     collisionFilter = -1);

    // ── Penetrating (all-hits) raycast ─────────────────────────────────────────

    /// <summary>
    /// Performs an all-hits (penetrating) raycast and returns every hit in order
    /// of ascending distance.
    /// <para>
    /// Backed by <c>Simulation.RaycastPenetrating</c> on the concrete implementation.
    /// GPU-deferred (STR-D11).
    /// </para>
    /// </summary>
    /// <param name="fromFdp">Ray start in FDP world space.</param>
    /// <param name="toFdp">Ray end in FDP world space.</param>
    /// <param name="hits">Output list populated with each hit in FDP space, nearest first.</param>
    /// <param name="collisionGroups">Collision groups bitmask.</param>
    /// <param name="collisionFilter">Collision filter bitmask.</param>
    void RaycastPenetrating(
        Vector3                      fromFdp,
        Vector3                      toFdp,
        System.Collections.Generic.IList<StrideRaycastHit> hits,
        int                          collisionGroups = -1,
        int                          collisionFilter = -1);
}

// ── Hit result ────────────────────────────────────────────────────────────────

/// <summary>
/// Result of a single Stride raycast query, expressed in FDP world-space
/// (right-handed, X=East, Y=North, Z=Up).
///
/// <para>
/// <b>Normal swizzle note.</b>
/// Hit normals are direction vectors, not position vectors.  They are converted
/// using the same axis swizzle as velocity / direction vectors
/// (<c>FdpStrideTransform.ToFdpVelocity</c>), NOT the position swizzle.
/// Both use the mapping <c>(stride.X, stride.Z, stride.Y)</c> but for normals we
/// explicitly call the velocity/direction path to make the intent clear at the
/// call site and to prevent any future divergence if the position swizzle ever
/// gains a translation component.
/// </para>
///
/// <para>
/// Backed by Stride's <c>HitResult</c> on the concrete implementation.
/// </para>
/// </summary>
public readonly struct StrideRaycastHit
{
    /// <summary><see langword="true"/> when the ray intersected a collider.</summary>
    public readonly bool HasHit;

    /// <summary>
    /// Hit point in FDP world space.  Undefined when <see cref="HasHit"/> is <see langword="false"/>.
    /// </summary>
    public readonly Vector3 PointFdp;

    /// <summary>
    /// Surface normal at the hit point in FDP world space (unit vector).
    /// Converted via <see cref="FdpStrideTransform.ToFdpVelocity"/> (direction swizzle),
    /// NOT <see cref="FdpStrideTransform.ToFdpPosition"/> (position swizzle).
    /// Undefined when <see cref="HasHit"/> is <see langword="false"/>.
    /// </summary>
    public readonly Vector3 NormalFdp;

    /// <summary>
    /// Fraction ∈ [0, 1] along the ray segment at which the hit occurred
    /// (<c>t=0</c> = <c>from</c>, <c>t=1</c> = <c>to</c>).
    /// Undefined when <see cref="HasHit"/> is <see langword="false"/>.
    /// </summary>
    public readonly float HitFraction;

    /// <summary>
    /// The FDP entity that was hit, resolved from the Stride collider's user data.
    /// May be <see cref="Entity.Null"/> when the collider is static scene geometry
    /// (walls, terrain) with no associated FDP entity.
    /// Undefined when <see cref="HasHit"/> is <see langword="false"/>.
    /// </summary>
    public readonly Entity HitEntity;

    /// <summary>Constructs a hit result.</summary>
    public StrideRaycastHit(
        bool    hasHit,
        Vector3 pointFdp,
        Vector3 normalFdp,
        float   hitFraction,
        Entity  hitEntity)
    {
        HasHit      = hasHit;
        PointFdp    = pointFdp;
        NormalFdp   = normalFdp;
        HitFraction = hitFraction;
        HitEntity   = hitEntity;
    }

    /// <summary>A sentinel miss result (no hit).</summary>
    public static readonly StrideRaycastHit Miss = new StrideRaycastHit(
        hasHit:      false,
        pointFdp:    Vector3.Zero,
        normalFdp:   Vector3.Zero,
        hitFraction: 1f,
        hitEntity:   default);
}

#nullable enable
using System;
using System.Numerics;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.Stride.Core;

/// <summary>
/// Stride-backed implementation of <see cref="ILosService"/> using
/// <see cref="IStrideRaycastService"/> for real 3D occlusion queries.
///
/// <para>
/// <b>Drop-in replacement.</b>
/// Satisfies <see cref="ILosService"/> exactly, replacing the flat spatial-hash
/// approximation (<see cref="BlockedLosService"/> stub or the inline 2-D
/// segment-circle sweep in <c>LosRequestBatchingSystem</c>) with a real 3-D raycast
/// against Stride/Bullet scene geometry (walls, terrain, obstacles).
/// </para>
///
/// <para>
/// <b>3-D endpoints (CE-210 (a)).</b> <see cref="ILosService.HasLineOfSight"/> takes engine-space points with the
/// entity's eye/aim heights already applied by the caller (EQS: <c>CheapLineOfSightTest</c>, <c>ThreatExposureTest</c>).
/// ⛔ SUPERSEDED: a 2-D <c>HasCheapLineOfSight</c> that lifted both ends to one service-wide <c>EyeHeightMetres</c> —
/// stance, vehicles and terrain height were unrepresentable.
/// </para>
///
/// <para>
/// <b>Semantics (matching <see cref="ILosService"/> contract).</b>
/// <list type="bullet">
///   <item>Returns <see langword="true"/>  if the ray is clear (no hit before the target) — LOS visible.</item>
///   <item>Returns <see langword="false"/> if the ray is blocked (hit before the target) — occlusion / cover valid.</item>
/// </list>
/// A miss on the raycast (nothing between observer and target) → true (clear).
/// A hit before the target → false (blocked).
/// A hit at or beyond the target distance → true (clear — the only thing hit is the target itself).
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> all calls occur on the single host thread (design §8.3).
/// </para>
/// </summary>
public sealed class StrideRaycastLosService : ILosService
{
    private readonly IStrideRaycastService _raycast;

    /// <summary>
    /// Fraction of the ray length within which a hit is considered "before" the target.
    /// Set slightly below 1 to avoid false occlusion when the target's own collider
    /// is hit at the very end of the ray.
    /// Default: 0.99 (1 cm tolerance for a 1 m ray; proportional).
    /// </summary>
    public float HitFractionClearThreshold { get; set; } = 0.99f;

    /// <summary>
    /// Creates a <see cref="StrideRaycastLosService"/> using the supplied raycast backend.
    /// </summary>
    /// <param name="raycast">
    /// The <see cref="IStrideRaycastService"/> providing real 3-D raycasts.
    /// In headless tests, pass a <see cref="FakeStrideRaycastService"/>.
    /// On the Stride node, pass the concrete <c>StrideRaycastService</c>.
    /// </param>
    public StrideRaycastLosService(IStrideRaycastService raycast)
    {
        _raycast = raycast ?? throw new ArgumentNullException(nameof(raycast));
    }

    // ── ILosService ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>⭐ CE-210 (a): the 3-D method IS the interface method now; the caller applies the entity's eye/aim heights.
    /// ⛔ SUPERSEDED: <c>HasCheapLineOfSight(Vector2, Vector2)</c>, which lifted both ends to a fixed eye height.</remarks>
    public bool HasLineOfSight(Vector3 eye, Vector3 aim) => HasLineOfSight3D(eye, aim);

    // ── 3-D entry point ────────────────────────────────────────────────────────

    /// <summary>
    /// 3-D line-of-sight check in FDP world space.
    ///
    /// <para>
    /// Returns <see langword="true"/> (clear) when no geometry blocks the segment from
    /// <paramref name="observerFdp"/> to <paramref name="targetFdp"/>.
    /// Returns <see langword="false"/> (blocked) when something hits before the target.
    /// </para>
    ///
    /// <para>
    /// A hit at fraction ≥ <see cref="HitFractionClearThreshold"/> is treated as
    /// hitting the target itself (or a surface right at the end of the ray) and is
    /// considered clear, to avoid false occlusion from the target's own collider.
    /// </para>
    /// </summary>
    /// <param name="observerFdp">Observer position in FDP world space.</param>
    /// <param name="targetFdp">Target position in FDP world space.</param>
    /// <returns>
    /// <see langword="true"/> = clear LOS (visible);
    /// <see langword="false"/> = blocked (occluded).
    /// </returns>
    public bool HasLineOfSight3D(Vector3 observerFdp, Vector3 targetFdp)
    {
        var hit = _raycast.Raycast(observerFdp, targetFdp);

        if (!hit.HasHit)
            return true;  // Nothing between observer and target — clear.

        // A hit at or beyond HitFractionClearThreshold means it hit the target itself
        // (or the very far end of the ray) — treat as clear.
        return hit.HitFraction >= HitFractionClearThreshold;
    }
}

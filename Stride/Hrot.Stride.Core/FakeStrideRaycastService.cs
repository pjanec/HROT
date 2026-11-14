#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hrot.Stride.Core;

/// <summary>
/// Scriptable test-double for <see cref="IStrideRaycastService"/>.
///
/// <para>
/// Used in headless tests to exercise coordinate-conversion and mask-plumbing
/// logic without a live <c>Stride.Physics.Simulation</c> (which is GPU-deferred,
/// see <c>StrideRaycastService</c> STR-D11 pattern).
/// </para>
///
/// <para>
/// The default behaviour is a miss on every cast.  To simulate a blocking hit, set
/// <see cref="NextHit"/> before calling <see cref="Raycast"/>.  The service records the
/// last call's arguments in <see cref="LastFrom"/>, <see cref="LastTo"/>,
/// <see cref="LastCollisionGroups"/>, and <see cref="LastCollisionFilter"/> so tests can
/// assert that the correct values were passed in.
/// </para>
/// </summary>
public sealed class FakeStrideRaycastService : IStrideRaycastService
{
    // ── Captured call arguments ────────────────────────────────────────────────

    /// <summary>The <c>fromFdp</c> argument of the most recent <see cref="Raycast"/> call.</summary>
    public Vector3 LastFrom { get; private set; }

    /// <summary>The <c>toFdp</c> argument of the most recent <see cref="Raycast"/> call.</summary>
    public Vector3 LastTo { get; private set; }

    /// <summary>The <c>collisionGroups</c> argument of the most recent <see cref="Raycast"/> call.</summary>
    public int LastCollisionGroups { get; private set; }

    /// <summary>The <c>collisionFilter</c> argument of the most recent <see cref="Raycast"/> call.</summary>
    public int LastCollisionFilter { get; private set; }

    /// <summary>Total number of <see cref="Raycast"/> calls made since construction.</summary>
    public int CallCount { get; private set; }

    // ── Scripted result ────────────────────────────────────────────────────────

    /// <summary>
    /// The hit that <see cref="Raycast"/> returns on the next call.
    /// Defaults to <see cref="StrideRaycastHit.Miss"/>.
    /// After each call the value is NOT reset — set it before each call if needed.
    /// </summary>
    public StrideRaycastHit NextHit { get; set; } = StrideRaycastHit.Miss;

    /// <summary>
    /// Optional list of hits returned by <see cref="RaycastPenetrating"/> on the next call.
    /// Each entry is appended to the output list.
    /// Defaults to empty (no penetrating hits).
    /// </summary>
    public List<StrideRaycastHit> NextPenetratingHits { get; } = new List<StrideRaycastHit>();

    // ── IStrideRaycastService implementation ──────────────────────────────────

    /// <inheritdoc/>
    public StrideRaycastHit Raycast(
        Vector3 fromFdp,
        Vector3 toFdp,
        int     collisionGroups = -1,
        int     collisionFilter = -1)
    {
        LastFrom           = fromFdp;
        LastTo             = toFdp;
        LastCollisionGroups = collisionGroups;
        LastCollisionFilter = collisionFilter;
        CallCount++;
        return NextHit;
    }

    /// <inheritdoc/>
    public void RaycastPenetrating(
        Vector3                 fromFdp,
        Vector3                 toFdp,
        IList<StrideRaycastHit> hits,
        int                     collisionGroups = -1,
        int                     collisionFilter = -1)
    {
        if (hits == null) throw new ArgumentNullException(nameof(hits));

        LastFrom            = fromFdp;
        LastTo              = toFdp;
        LastCollisionGroups = collisionGroups;
        LastCollisionFilter = collisionFilter;
        CallCount++;

        foreach (var h in NextPenetratingHits)
            hits.Add(h);
    }
}

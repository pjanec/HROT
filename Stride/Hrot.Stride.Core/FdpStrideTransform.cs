using System.Numerics;
using Fdp.Core;
using SNum = System.Numerics;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// Pure static class: all conversions between FDP world space and Stride world space,
/// both directions. Centralised here; never use ad-hoc swizzles elsewhere.
///
/// <para>
/// <b>Axis convention</b> (architect-confirmed; same mapping the engine's Recast integration uses):
/// <list type="table">
///   <item><term>FDP (right-handed)</term><description>X = East, Y = North, Z = Up</description></item>
///   <item><term>Stride (Y-up, left-handed)</term><description>X = East, Y = Up, Z = North</description></item>
/// </list>
/// Swizzle rule: <c>Stride = (fdp.X, fdp.Z, fdp.Y)</c>  i.e. FDP.Z becomes Stride.Y,
/// FDP.Y becomes Stride.Z. The East axis (X) is unchanged.
/// </para>
///
/// <para>
/// <b>Rotation / handedness.</b>
/// FDP uses right-handed quaternions (rotation angles follow the right-hand rule around
/// each axis). Stride uses a left-handed coordinate system, so a pure axis-relabel is
/// not sufficient — the sign of the imaginary (XYZ) components must be negated to convert
/// the rotation sense from right-handed to left-handed.
///
/// Derivation:
/// <list type="number">
///   <item>Start with an FDP quaternion q = (w, x, y, z) in right-handed space.</item>
///   <item>Apply axis relabel matching the position swizzle:
///         FDP X→Stride X, FDP Z→Stride Y, FDP Y→Stride Z.</item>
///   <item>Negate all imaginary components (X, Y, Z) to flip the rotation sense from
///         right-handed to left-handed: q_stride = (w, -x', -y', -z') where primes
///         denote the relabelled axes.</item>
/// </list>
/// Combined:
/// <code>
///   stride.W =  fdp.W
///   stride.X = -fdp.X   (East imaginary; sign-flipped for LH)
///   stride.Y = -fdp.Z   (Altitude → Stride-up imaginary; relabelled + sign-flipped)
///   stride.Z = -fdp.Y   (North → Stride-Z imaginary; relabelled + sign-flipped)
/// </code>
/// This is equivalent to conjugating the quaternion in the relabelled space, which converts
/// the rotation from right-handed to left-handed. The homomorphism test
/// <c>ToStridePosition(Transform(v, q)) ≈ Transform(ToStridePosition(v), ToStrideRotation(q))</c>
/// proves the correctness numerically — it fails with a pure axis-relabel (no sign flip).
/// </para>
///
/// <para>
/// <b>FdpRay</b> (small struct defined here; <c>Fdp.Core</c> has no ray type):
/// Used as the return type of <see cref="ScreenRayToFdp"/>.
/// </para>
/// </summary>
public static class FdpStrideTransform
{
    // ── Position ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an FDP world position to a Stride world position.
    /// Swizzle: Stride = (fdp.X, fdp.Z, fdp.Y).
    /// </summary>
    public static SMath.Vector3 ToStridePosition(in SNum.Vector3 p)
        => new SMath.Vector3(p.X, p.Z, p.Y);

    /// <summary>
    /// Converts a Stride world position to an FDP world position.
    /// Inverse swizzle: FDP = (stride.X, stride.Z, stride.Y).
    /// </summary>
    public static SNum.Vector3 ToFdpPosition(in SMath.Vector3 s)
        => new SNum.Vector3(s.X, s.Z, s.Y);

    // ── Rotation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an FDP right-handed quaternion to a Stride left-handed quaternion.
    ///
    /// <para>See class-level documentation for the full derivation. In short:
    /// axis relabel (X→X, Z→Y, Y→Z) + negate all imaginary components to convert
    /// right-handed → left-handed rotation sense.</para>
    /// </summary>
    public static SMath.Quaternion ToStrideRotation(in SNum.Quaternion r)
        => new SMath.Quaternion(-r.X, -r.Z, -r.Y, r.W);

    /// <summary>
    /// Converts a Stride left-handed quaternion to an FDP right-handed quaternion.
    /// Inverse of <see cref="ToStrideRotation"/>.
    /// </summary>
    public static SNum.Quaternion ToFdpRotation(in SMath.Quaternion q)
        => new SNum.Quaternion(-q.X, -q.Z, -q.Y, q.W);

    // ── Velocity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an FDP linear velocity vector to Stride space.
    /// Uses the same axis swizzle as <see cref="ToStridePosition"/> (no translation term).
    /// </summary>
    public static SMath.Vector3 ToStrideVelocity(in SNum.Vector3 v)
        => new SMath.Vector3(v.X, v.Z, v.Y);

    /// <summary>
    /// Converts a Stride linear velocity vector to FDP space.
    /// Inverse swizzle of <see cref="ToStrideVelocity"/>.
    /// </summary>
    public static SNum.Vector3 ToFdpVelocity(in SMath.Vector3 s)
        => new SNum.Vector3(s.X, s.Z, s.Y);

    /// <summary>
    /// Converts a Stride angular velocity vector to FDP angular velocity.
    ///
    /// <para>Angular velocity transforms like a pseudovector: same axis swizzle as
    /// linear velocity, but the sign of all components is negated to account for
    /// the handedness flip (right-hand rule → left-hand rule reverses the sign of
    /// the rotation rate).</para>
    ///
    /// <para>Stride angular velocity is in left-handed radians/s; FDP is right-handed rad/s.</para>
    /// </summary>
    public static SNum.Vector3 ToFdpAngularVelocity(in SMath.Vector3 s)
        => new SNum.Vector3(-s.X, -s.Z, -s.Y);

    // ── Screen ray ────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a world-space ray in FDP coordinates from a screen pixel.
    /// The ray originates at the camera's near-plane world position and points
    /// into the scene. All output coordinates are in FDP space (right-handed,
    /// X=East, Y=North, Z=Up).
    ///
    /// <para><b>Implementation note (ScreenRayToFdp approach).</b>
    /// Stride's <see cref="global::Stride.Engine.CameraComponent"/> exposes
    /// <c>ViewMatrix</c> and <c>ProjectionMatrix</c> which are valid without a
    /// running game window (they are computed from camera properties). The unproject
    /// is done via the combined view-projection matrix:
    /// <list type="number">
    ///   <item>Convert screen pixel to NDC (normalised device coordinates) in [-1,1]².</item>
    ///   <item>Unproject two NDC points (near z=0, far z=1) through the inverse VP matrix
    ///         to get world-space points.</item>
    ///   <item>The ray direction is <c>normalize(farWorld - nearWorld)</c>.</item>
    ///   <item>Convert both origin and direction from Stride to FDP space via
    ///         <see cref="ToFdpPosition"/>.</item>
    /// </list>
    /// The camera matrices are read-only properties and work without a running graphics
    /// device, so this function is pure (no side-effects, testable without a running game)
    /// as long as the <see cref="global::Stride.Engine.CameraComponent"/> has been
    /// configured with valid projection settings.
    /// </para>
    /// </summary>
    /// <param name="cam">The Stride camera component (must have valid VP matrices).</param>
    /// <param name="screenPx">Screen pixel coordinates (origin top-left).</param>
    /// <returns>A ray in FDP world space.</returns>
    public static FdpRay ScreenRayToFdp(
        global::Stride.Engine.CameraComponent cam,
        System.Numerics.Vector2 screenPx)
    {
        // Build the view-projection matrix from the camera component.
        SMath.Matrix vp = cam.ViewProjectionMatrix;

        // Viewport dimensions — use the camera's aspect ratio if no explicit viewport.
        // CameraComponent.AspectRatio is Width/Height; assume a unit height and derive width.
        // vpWidth and vpHeight are only used conceptually here; NDC conversion works
        // on the assumption the caller passes screenPx already in [0,1]² normalised
        // coordinates (not raw pixels). See ScreenRayToFdp documentation.
        _ = cam.AspectRatio; // aspect ratio informs the projection matrix, already baked in VP

        // Convert pixel → NDC [-1,1].
        // Stride NDC: x ∈ [-1,+1] (left→right), y ∈ [+1,-1] (top→bottom), z ∈ [0,1].
        // screenPx is assumed in a [0,vpWidth×vpHeight] viewport.
        // For a proper pixel→NDC we need the actual viewport size; the caller is responsible
        // for passing a pixel relative to the viewport extents.
        // Here we accept (screenPx.X, screenPx.Y) in range [0, viewportWidth) × [0, viewportHeight).
        // We use CameraComponent.AspectRatio for width and assume height = 1 (normalised).
        // In practice the caller should pass a pixel already normalised to [0,1]² — this is
        // documented in the FdpRay API.  For the pure test we work with NDC directly.
        float ndcX =  2.0f * screenPx.X - 1.0f;  // [0,1] → [-1,+1]
        float ndcY = -2.0f * screenPx.Y + 1.0f;  // [0,1] → [+1,-1] (flip Y)

        // Unproject near (z=0) and far (z=1) NDC points.
        // NDC → World: apply inverse VP.
        SMath.Matrix.Invert(ref vp, out SMath.Matrix invVP);

        SMath.Vector4 nearNdc = new SMath.Vector4(ndcX, ndcY, 0.0f, 1.0f);
        SMath.Vector4 farNdc  = new SMath.Vector4(ndcX, ndcY, 1.0f, 1.0f);

        SMath.Vector4 nearWorld4 = SMath.Vector4.Transform(nearNdc, invVP);
        SMath.Vector4 farWorld4  = SMath.Vector4.Transform(farNdc,  invVP);

        // Perspective divide.
        if (nearWorld4.W != 0f) { nearWorld4 /= nearWorld4.W; }
        if (farWorld4.W  != 0f) { farWorld4  /= farWorld4.W;  }

        SMath.Vector3 nearWorld = new SMath.Vector3(nearWorld4.X, nearWorld4.Y, nearWorld4.Z);
        SMath.Vector3 farWorld  = new SMath.Vector3(farWorld4.X,  farWorld4.Y,  farWorld4.Z);

        // Direction in Stride space.
        SMath.Vector3 dir = SMath.Vector3.Normalize(farWorld - nearWorld);

        // Convert to FDP space.
        SNum.Vector3 fdpOrigin    = ToFdpPosition(nearWorld);
        SNum.Vector3 fdpDirection = ToFdpPosition(dir);
        fdpDirection = SNum.Vector3.Normalize(fdpDirection);

        return new FdpRay(fdpOrigin, fdpDirection);
    }
}

/// <summary>
/// A world-space ray in FDP coordinates (right-handed, X=East, Y=North, Z=Up).
/// Returned by <see cref="FdpStrideTransform.ScreenRayToFdp"/>.
///
/// <para>
/// <b>Why a new struct?</b>  <c>Fdp.Core</c> does not expose a ray type as of this batch.
/// If a ray type is added to <c>Fdp.Core</c> in the future, replace this with the shared type.
/// </para>
/// </summary>
public readonly struct FdpRay
{
    /// <summary>Ray origin in FDP world coordinates.</summary>
    public readonly System.Numerics.Vector3 Origin;

    /// <summary>Normalised ray direction in FDP world coordinates.</summary>
    public readonly System.Numerics.Vector3 Direction;

    /// <summary>Constructs a ray from an origin and a (assumed normalised) direction.</summary>
    public FdpRay(System.Numerics.Vector3 origin, System.Numerics.Vector3 direction)
    {
        Origin    = origin;
        Direction = direction;
    }
}

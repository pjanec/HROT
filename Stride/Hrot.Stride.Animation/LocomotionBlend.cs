using System;

namespace Hrot.Stride.Animation;

/// <summary>
/// Which two locomotion clips are being cross-blended this frame, and at what
/// factor. This is the engine-agnostic description of an idle/walk/run blend
/// tree leaf — it carries no Stride types and is fully unit-testable without a
/// <c>GraphicsDevice</c> (DD-1 §15, STR-P4-T1 testable seam).
/// </summary>
public enum LocomotionClip : byte
{
    /// <summary>The standing/idle pose clip (<c>Animations/Idle</c>).</summary>
    Idle = 0,

    /// <summary>The walk-cycle clip (<c>Animations/Walk</c>).</summary>
    Walk = 1,

    /// <summary>The run-cycle clip (<c>Animations/Run</c>).</summary>
    Run = 2,
}

/// <summary>
/// Result of mapping a locomotion speed (m/s) onto the idle/walk/run blend
/// space. Mirrors the template <c>AnimationController.UpdateWalking</c> two-clip
/// lerp, but expressed in absolute speed (the backend receives physics-sourced
/// velocity, not a normalized 0..1 input), and exposes the discrete idle/walk/run
/// weights so tests can assert exact values per speed threshold.
/// </summary>
public readonly struct LocomotionBlendWeights : IEquatable<LocomotionBlendWeights>
{
    /// <summary>Weight (0..1) on the Idle clip.</summary>
    public float Idle { get; }

    /// <summary>Weight (0..1) on the Walk clip.</summary>
    public float Walk { get; }

    /// <summary>Weight (0..1) on the Run clip.</summary>
    public float Run { get; }

    /// <summary>Lower clip of the active two-clip blend (the one weighted by <c>1 - Factor</c>).</summary>
    public LocomotionClip LowerClip { get; }

    /// <summary>Upper clip of the active two-clip blend (the one weighted by <c>Factor</c>).</summary>
    public LocomotionClip UpperClip { get; }

    /// <summary>
    /// Blend factor (0..1) used by the Stride blend tree:
    /// <c>NewBlend(Blend, Factor)</c> mixes <see cref="LowerClip"/> (at 1-Factor)
    /// toward <see cref="UpperClip"/> (at Factor).
    /// </summary>
    public float Factor { get; }

    internal LocomotionBlendWeights(
        float idle, float walk, float run,
        LocomotionClip lower, LocomotionClip upper, float factor)
    {
        Idle = idle;
        Walk = walk;
        Run = run;
        LowerClip = lower;
        UpperClip = upper;
        Factor = factor;
    }

    /// <inheritdoc/>
    public bool Equals(LocomotionBlendWeights other)
        => Idle == other.Idle && Walk == other.Walk && Run == other.Run
           && LowerClip == other.LowerClip && UpperClip == other.UpperClip
           && Factor == other.Factor;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LocomotionBlendWeights w && Equals(w);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Idle, Walk, Run, LowerClip, UpperClip, Factor);
}

/// <summary>
/// Pure, deterministic mapping from locomotion speed (m/s) to idle/walk/run blend
/// weights. This is the <b>testable half</b> of <see cref="StrideAnimationBackend"/>'s
/// locomotion blending (STR-P4-T1): it has no Stride dependency, so the speed→weight
/// thresholds can be asserted headlessly. The <see cref="PerEntityBlendTreeBuilder"/>
/// consumes the result to drive the GPU-bound Stride <c>AnimationComponent</c> blend tree.
/// </summary>
/// <remarks>
/// <para>Thresholds (m/s), modeled on the template <c>AnimationController</c> two-clip
/// lerp but in absolute speed:</para>
/// <list type="bullet">
///   <item><description><c>speed &lt;= IdleSpeed</c> (0.1): pure Idle.</description></item>
///   <item><description><c>IdleSpeed &lt; speed &lt; WalkSpeed</c> (1.5): Idle→Walk blend.</description></item>
///   <item><description><c>speed == WalkSpeed</c>: pure Walk.</description></item>
///   <item><description><c>WalkSpeed &lt; speed &lt; RunSpeed</c> (4.0): Walk→Run blend.</description></item>
///   <item><description><c>speed &gt;= RunSpeed</c>: pure Run.</description></item>
/// </list>
/// <para>The Idle→Walk leg applies a <c>sqrt</c> skew (as the template does) because a
/// linear idle↔walk blend reads as foot-sliding; the skew biases toward Walk.</para>
/// </remarks>
public static class LocomotionBlend
{
    /// <summary>At or below this speed (m/s) the character is fully idle.</summary>
    public const float IdleSpeed = 0.1f;

    /// <summary>The speed (m/s) at which the walk cycle plays at full weight.</summary>
    public const float WalkSpeed = 1.5f;

    /// <summary>At or above this speed (m/s) the run cycle plays at full weight.</summary>
    public const float RunSpeed = 4.0f;

    /// <summary>
    /// Compute the idle/walk/run blend for a planar locomotion speed.
    /// </summary>
    /// <param name="speed">Horizontal speed magnitude in m/s (non-negative; negatives are clamped to 0).</param>
    public static LocomotionBlendWeights FromSpeed(float speed)
    {
        if (float.IsNaN(speed) || speed <= IdleSpeed)
            return new LocomotionBlendWeights(1f, 0f, 0f, LocomotionClip.Idle, LocomotionClip.Walk, 0f);

        if (speed < WalkSpeed)
        {
            // Idle → Walk leg. sqrt-skew toward Walk (template UpdateWalking).
            float t = (speed - IdleSpeed) / (WalkSpeed - IdleSpeed);
            float factor = MathF.Sqrt(Math.Clamp(t, 0f, 1f));
            return new LocomotionBlendWeights(
                idle: 1f - factor, walk: factor, run: 0f,
                lower: LocomotionClip.Idle, upper: LocomotionClip.Walk, factor: factor);
        }

        if (speed >= RunSpeed)
            return new LocomotionBlendWeights(0f, 0f, 1f, LocomotionClip.Walk, LocomotionClip.Run, 1f);

        // Walk → Run leg (linear).
        float u = (speed - WalkSpeed) / (RunSpeed - WalkSpeed);
        u = Math.Clamp(u, 0f, 1f);
        return new LocomotionBlendWeights(
            idle: 0f, walk: 1f - u, run: u,
            lower: LocomotionClip.Walk, upper: LocomotionClip.Run, factor: u);
    }

    /// <summary>
    /// Convenience overload computing speed from a planar velocity (X, Z components).
    /// </summary>
    public static LocomotionBlendWeights FromVelocity(float velX, float velZ)
        => FromSpeed(MathF.Sqrt(velX * velX + velZ * velZ));
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using SMath = Stride.Core.Mathematics;
using NLog;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

// ── Stance enum ───────────────────────────────────────────────────────────────

/// <summary>
/// Character stance for speed-multiplier scaling in <see cref="BulletCharacterMotor"/>.
///
/// <para>
/// Mirrors <c>Hrot.MuscleCharacter.Animation.StanceId</c> by value (Standing=0, Crouched=1,
/// Prone=2) so the byte value from <c>StanceStatus.CurrentStance</c> can be cast directly.
/// Defined locally in <c>Hrot.Stride.Core</c> to avoid a circular project reference into
/// <c>Hrot.MuscleCharacter.Animation</c> — that assembly is animation-specific and would
/// drag in the full animation pipeline as a dependency of the physics motor.
/// </para>
///
/// <para>
/// <b>Stance source (design §6.2 [VERIFY] result):</b>
/// <c>StanceStatus.CurrentStance</c> (type <c>Hrot.MuscleCharacter.Animation.StanceId</c>,
/// component ID 223) is the live stance on crowd/humanoid entities.  The caller (bootstrap /
/// <c>BulletCharacterMotor</c> driver) reads it from the ECS and casts the byte value to this
/// enum before passing it to the motor. A direct assembly reference is avoided by design.
/// </para>
/// </summary>
public enum CharacterStance : byte
{
    /// <summary>Standing upright (default). Speed multiplier = 1.0.</summary>
    Standing = 0,

    /// <summary>Crouched / half-height. Speed multiplier &lt; 1.0 (configurable).</summary>
    Crouched = 1,

    /// <summary>Prone / fully horizontal. Speed multiplier &lt; 1.0 (configurable).</summary>
    Prone = 2,
}

// ── BulletCharacterMotor ──────────────────────────────────────────────────────

/// <summary>
/// Pre-physics motor for humanoid / <c>CrowdAgent</c> entities (STR-P1-T3, design §6.2).
///
/// <para>
/// Each frame, for every entity that has a <see cref="PhysicsBodyReference"/> and a
/// <see cref="CrowdMotorIntent"/>, this motor:
/// <list type="number">
///   <item>Reads <see cref="CrowdMotorIntent.Velocity"/> (FDP space, X=East, Y=North, Z=Up).</item>
///   <item>Applies the entity's current <see cref="CharacterStance"/> speed multiplier
///         (Standing=1.0, Crouched/Prone configurable).</item>
///   <item>Converts the scaled velocity to Stride space via
///         <c>FdpStrideTransform.ToStrideVelocity</c>.</item>
///   <item>Calls <see cref="IPhysicsBodyService.SetCharacterVelocity"/> with the Stride-space velocity.</item>
///   <item>If <see cref="CrowdMotorIntent.Jump"/> is <see langword="true"/> AND
///         <see cref="IPhysicsBodyService.IsGrounded"/> returns <see langword="true"/>,
///         calls <see cref="IPhysicsBodyService.Jump"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Stance source:</b> the motor takes a <c>Func&lt;Entity, CharacterStance&gt;</c> resolver
/// injected at construction time (default: always <see cref="CharacterStance.Standing"/>).
/// The real driver (bootstrap / Stride script) reads <c>StanceStatus.CurrentStance</c> from
/// the ECS and returns it cast to <see cref="CharacterStance"/>. Tests pass a scriptable stub.
/// This design keeps the motor decoupled from the animation assembly.
/// </para>
///
/// <para>
/// <b>Phase:</b> <see cref="SystemPhase.Simulation"/> — runs before the physics step (§6.2).
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class BulletCharacterMotor
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Throttle: emit PostCollision velocity log once every N frames per entity.</summary>
    private const int PostCollisionLogIntervalFrames = 120;
    private readonly Dictionary<ulong, int> _postCollisionLogCounter = new();

    private readonly IPhysicsBodyService _bodyService;
    private readonly PhysicsBodyLifecycleSystem _lifecycle;
    private readonly Func<Entity, CharacterStance> _stanceResolver;

    /// <summary>
    /// Speed multiplier for <see cref="CharacterStance.Standing"/> (≡ 1.0, kept for symmetry).
    /// </summary>
    public float StandingMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Speed multiplier for <see cref="CharacterStance.Crouched"/>.
    /// Default 0.5 — entity moves at half speed when crouching.
    /// </summary>
    public float CrouchedMultiplier { get; set; } = 0.5f;

    /// <summary>
    /// Speed multiplier for <see cref="CharacterStance.Prone"/>.
    /// Default 0.25 — entity moves at quarter speed when prone.
    /// </summary>
    public float ProneMultiplier { get; set; } = 0.25f;

    /// <summary>Min horizontal FDP speed (m/s) before the character turns to face travel.
    /// Below this it keeps its current facing (so a stopped mannequin doesn't snap to a default).</summary>
    public float FacingMinSpeed { get; set; } = 0.10f;

    /// <summary>Model-forward yaw correction (degrees), added to the travel heading. The mannequin
    /// model's local forward is not FDP +East at yaw 0; this aligns it. Matches the known mannequin
    /// correction (StrideVisualFactory.MannequinYawCorrectionDeg = −90). Tune if facing is off.</summary>
    public float FacingYawOffsetDeg { get; set; } = -90f;

    /// <summary>Per-frame slerp factor toward the target facing (0..1). ~0.20 gives a smooth turn.</summary>
    public float FacingTurnLerp { get; set; } = 0.20f;

    /// <summary>
    /// Constructs the motor.
    /// </summary>
    /// <param name="bodyService">
    /// Physics service — routes character drive calls to the concrete
    /// <c>BulletPhysicsBodyService</c> (or a recording fake in tests).
    /// </param>
    /// <param name="lifecycle">
    /// Lifecycle system — provides the <see cref="PhysicsBodyLifecycleSystem.Bodies"/>
    /// dictionary that maps FDP entities to their <see cref="PhysicsBodyReference"/>.
    /// </param>
    /// <param name="stanceResolver">
    /// Optional function that returns the current <see cref="CharacterStance"/> for an entity.
    /// When <see langword="null"/>, all entities are treated as <see cref="CharacterStance.Standing"/>.
    /// </param>
    public BulletCharacterMotor(
        IPhysicsBodyService        bodyService,
        PhysicsBodyLifecycleSystem lifecycle,
        Func<Entity, CharacterStance>? stanceResolver = null)
    {
        _bodyService    = bodyService ?? throw new ArgumentNullException(nameof(bodyService));
        _lifecycle      = lifecycle   ?? throw new ArgumentNullException(nameof(lifecycle));
        _stanceResolver = stanceResolver ?? (_ => CharacterStance.Standing);
    }

    /// <summary>
    /// Executes the motor: translates <see cref="CrowdMotorIntent"/> → Bullet character drive
    /// for every entity that has both a body reference and an intent component.
    /// </summary>
    /// <param name="simRunning">
    /// When <see langword="false"/> (paused/edit mode), commands zero character velocity to each
    /// body and skips the normal drive path — keeping the character frozen.
    /// Defaults to <see langword="true"/> so existing callers compile unchanged.
    /// </param>
    public void Execute(ISimulationView view, float deltaTime, bool simRunning = true)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(BulletCharacterMotor)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

        if (!repo.IsComponentTypeRegistered<CrowdMotorIntent>())
            return;

        var query = repo.Query()
            .With<CrowdMotorIntent>()
            .WithOwned<SimTransform>()
            .Build();

        foreach (var entity in query)
        {
            // Only drive entities that have a Bullet body.
            if (!_lifecycle.Bodies.TryGetValue(entity, out var bodyRef))
                continue;

            // BATCH-S2-L: paused (edit mode) — freeze the character, don't advance it.
            if (!simRunning)
            {
                _bodyService.SetCharacterVelocity(bodyRef.BodyHandle, SMath.Vector3.Zero);
                continue;
            }

            var intent = repo.GetComponent<CrowdMotorIntent>(entity);

            // ── Stance speed multiplier ───────────────────────────────────────
            var stance = _stanceResolver(entity);
            float multiplier = stance switch
            {
                CharacterStance.Standing => StandingMultiplier,
                CharacterStance.Crouched => CrouchedMultiplier,
                CharacterStance.Prone    => ProneMultiplier,
                _                        => StandingMultiplier,
            };

            // Scale the FDP-space velocity by the stance multiplier.
            Vector3 scaledFdpVelocity = intent.Velocity * multiplier;

            // Convert FDP (X=East, Y=North, Z=Up) → Stride (X=East, Y=Up, Z=North).
            SMath.Vector3 strideVelocity = FdpStrideTransform.ToStrideVelocity(scaledFdpVelocity);

            // ── Drive the character body ──────────────────────────────────────
            _bodyService.SetCharacterVelocity(bodyRef.BodyHandle, strideVelocity);

            // ── Face the direction of travel (BATCH-S2-Y) ─────────────────────────────
            // Owned mannequin visual rotation = the body entity transform; the kinematic controller
            // never turns on its own. Turn it to face horizontal velocity, smoothed, when moving.
            // FDP horizontal plane is X=East, Y=North (Z=up). Heading yaw is about FDP up (Z).
            float hx = scaledFdpVelocity.X, hy = scaledFdpVelocity.Y;
            float horizSpeed = MathF.Sqrt(hx * hx + hy * hy);
            if (horizSpeed >= FacingMinSpeed)
            {
                float headingRad = MathF.Atan2(hy, hx);                  // FDP yaw about Z (up)
                float yawRad     = headingRad + FacingYawOffsetDeg * (MathF.PI / 180f);
                var fdpFacing    = System.Numerics.Quaternion.CreateFromAxisAngle(
                                       new Vector3(0f, 0f, 1f), yawRad); // FDP Z = up
                var targetStride = FdpStrideTransform.ToStrideRotation(fdpFacing);

                // Slerp from the current body orientation for a smooth turn.
                var curStride  = _bodyService.GetBodyState(bodyRef.BodyHandle).Rotation;
                var nextStride = SMath.Quaternion.Slerp(curStride, targetStride, FacingTurnLerp);
                _bodyService.SetCharacterFacing(bodyRef.BodyHandle, nextStride);
            }

            // ── Write post-collision velocity channel (velocity invariant, §6.1) ──
            // BulletReverseSyncSystem reads PostCollisionLinearVelocityFdp for kinematic
            // bodies (CharacterComponent is internally kinematic — GetBodyState returns
            // zero velocity for it). Without this write SimVelocity stays zero, the
            // locomotion blend sees idle speed, and no walk animation plays.
            // Mirror: KinematicVehicleMotor does the same after MoveKinematic.
            bodyRef.PostCollisionLinearVelocityFdp  = scaledFdpVelocity;
            bodyRef.PostCollisionAngularVelocityFdp = Vector3.Zero;

            // ── Throttled diagnostic: confirm PostCollision channel written ────
            // Helps confirm the channel is being set so BulletReverseSyncSystem can
            // read a nonzero value for the kinematic (CharacterComponent) body.
            // Emitted at Debug level every PostCollisionLogIntervalFrames frames.
            if (!_postCollisionLogCounter.TryGetValue(entity.PackedValue, out int logCount))
                logCount = 0;
            logCount++;
            if (logCount >= PostCollisionLogIntervalFrames)
            {
                logCount = 0;
                Log.Debug(
                    "[BulletCharacterMotor] entity #{0} PostCollisionLinearVelocityFdp written: " +
                    "({1:F2},{2:F2},{3:F2}) stance={4} mult={5:F2}",
                    entity.Index,
                    scaledFdpVelocity.X, scaledFdpVelocity.Y, scaledFdpVelocity.Z,
                    stance, multiplier);
            }
            _postCollisionLogCounter[entity.PackedValue] = logCount;

            // ── Grounded-gated jump ───────────────────────────────────────────
            if (intent.Jump && _bodyService.IsGrounded(bodyRef.BodyHandle))
                _bodyService.Jump(bodyRef.BodyHandle);
        }
    }

    /// <summary>
    /// Returns the configured speed multiplier for the given stance.
    /// Exposed for tests that assert multiplier application.
    /// </summary>
    public float GetMultiplier(CharacterStance stance) => stance switch
    {
        CharacterStance.Standing => StandingMultiplier,
        CharacterStance.Crouched => CrouchedMultiplier,
        CharacterStance.Prone    => ProneMultiplier,
        _                        => StandingMultiplier,
    };
}

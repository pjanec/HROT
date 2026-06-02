#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using NLog;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// Post-physics system that writes the Bullet-resolved pose and velocity back into
/// <see cref="SimTransform"/> / <see cref="SimVelocity"/> for locally-owned entities
/// (STR-P1-T5, design §6.1, §7, §9).
///
/// <para>
/// Runs once per frame <b>after</b> the <c>PhysicsProcessor</c> has stepped the simulation.
/// Must be wrapped in a <see cref="Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup"/>
/// so the reverse-sync can be severed during replay (design §9 / STR-D5).
/// With the group disabled, this system does not execute — historical
/// <see cref="SimTransform"/> values restored by <c>PlaybackTickSystem</c> are preserved.
/// </para>
///
/// <para>
/// <b>Authority filtering:</b> only <c>.WithOwned&lt;SimTransform&gt;()</c> entities are
/// processed. Non-owned entities (ghosts, replay playback) are skipped — their
/// <see cref="SimTransform"/> is driven by <c>DeadReckoningSyncSystem</c> /
/// <c>PlaybackTickSystem</c>.
/// </para>
///
/// <para>
/// <b>Velocity invariant (design §6.1):</b>
/// <list type="bullet">
///   <item><b>Dynamic bodies</b> (<see cref="BodyState.IsKinematic"/> = false):
///     velocity is read from <see cref="IPhysicsBodyService.GetBodyState"/>.
///     A collision-arrested dynamic body reports zero velocity directly from the
///     solver, so <see cref="SimVelocity"/> is written as exactly zero — satisfying
///     the invariant without any extra zeroing in this system.</item>
///   <item><b>Dynamic VEHICLE bodies</b> (<see cref="CollisionShapeKind.OrientedBox"/>,
///     <see cref="BodyState.IsKinematic"/> = false — BATCH-17 dynamic-body migration):
///     The vehicle is a DYNAMIC <c>RigidbodyComponent</c> driven via
///     <see cref="IPhysicsBodyService.SetLinearVelocityXZ"/> /
///     <see cref="IPhysicsBodyService.SetYawRate"/> each frame.  Bullet's solver reports
///     the actual post-contact velocity (zero when wall-arrested) directly in
///     <see cref="BodyState.LinearVelocity"/> / <see cref="BodyState.AngularVelocity"/>.
///     This system reads those fields via the dynamic branch (same as any dynamic body) —
///     satisfying the velocity invariant without any extra zeroing step.</item>
///   <item><b>Kinematic CHARACTER bodies</b> (<see cref="CollisionShapeKind.Capsule"/>):
///     Bullet's character controller does not expose a post-collision velocity.
///     The motor (STR-P1-T3) commands a velocity but the character may be fully blocked
///     by a wall — so the commanded value overestimates actual motion when blocked.
///     Instead, this system computes the ACTUAL velocity from the frame-to-frame FDP
///     position delta: <c>linearFdp = (currentFdpPos − prevFdpPos) / deltaTime</c>.
///     This yields ~commanded speed while walking freely and ~zero when the character
///     is blocked by a wall, satisfying the invariant without any extra zeroing step.
///     Previous positions are stored per-entity in <see cref="_prevFdpPositions"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Phase:</b> <see cref="SystemPhase.PostSimulation"/> — runs after Bullet has stepped.
/// Registered inside a <see cref="Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup"/>
/// by <c>EditorStrideSubsystem</c> / <c>StrideMuscleNodeBootstrapper</c>.
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class BulletReverseSyncSystem : IEcsModuleSystem
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Throttle: emit velocity-channel log once every N frames per entity.</summary>
    private const int VelocityLogIntervalFrames = 120;
    private readonly Dictionary<ulong, int> _velocityLogCounter = new();

    /// <summary>
    /// Previous-frame FDP position for each capsule (character) entity.
    /// Used to compute actual measured velocity = (currentPos − prevPos) / dt.
    /// Keyed by <c>entity.PackedValue</c>.
    /// Entries are seeded on the first frame the entity is seen (no velocity spike on spawn).
    /// </summary>
    private readonly Dictionary<ulong, Vector3> _prevFdpPositions = new();

    /// <summary>
    /// EMA-smoothed FDP linear velocity per capsule entity.
    /// Smoothing prevents the walk/idle blend from toggling when the raw measured
    /// velocity momentarily dips near the threshold due to per-frame jitter.
    /// Keyed by <c>entity.PackedValue</c>.
    /// </summary>
    private readonly Dictionary<ulong, Vector3> _smoothedFdpVelocity = new();

    /// <summary>
    /// EMA smoothing factor for capsule (character) velocity.
    /// <c>vSmooth = lerp(vPrevSmooth, vMeasured, EmaAlpha)</c>.
    /// 0.25 gives a ~4-frame settling time — fast enough to fall to ~0 within a
    /// fraction of a second when the character stops at a wall, but stable enough
    /// to prevent single-frame dips from toggling the idle/walk blend threshold.
    /// </summary>
    private const float EmaAlpha = 0.25f;

    private readonly IPhysicsBodyService       _bodyService;
    private readonly PhysicsBodyLifecycleSystem _lifecycle;

    /// <summary>
    /// Constructs the reverse-sync system.
    /// </summary>
    /// <param name="bodyService">
    /// Physics body service — provides <see cref="IPhysicsBodyService.GetBodyState"/>
    /// for reading the Bullet-resolved pose + velocity.
    /// Pass a scriptable fake in tests (headless — no Bullet runtime required).
    /// </param>
    /// <param name="lifecycle">
    /// The lifecycle system whose <see cref="PhysicsBodyLifecycleSystem.Bodies"/>
    /// dictionary maps FDP entities to their <see cref="PhysicsBodyReference"/>.
    /// </param>
    public BulletReverseSyncSystem(
        IPhysicsBodyService        bodyService,
        PhysicsBodyLifecycleSystem lifecycle)
    {
        _bodyService = bodyService ?? throw new ArgumentNullException(nameof(bodyService));
        _lifecycle   = lifecycle   ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// <summary>
    /// Executes the reverse-sync: reads Bullet body state → writes
    /// <see cref="SimTransform"/> and <see cref="SimVelocity"/> for all owned entities.
    /// </summary>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(BulletReverseSyncSystem)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

        // Query only locally-owned entities (authority bit set).
        var ownedQuery = repo.Query()
            .With<SimTransform>()
            .WithOwned<SimTransform>()
            .Build();

        foreach (var entity in ownedQuery)
        {
            // Only sync entities that have an active Bullet body.
            if (!_lifecycle.Bodies.TryGetValue(entity, out var bodyRef))
                continue;

            // ── Read resolved pose + velocity from Bullet ─────────────────────
            var state = _bodyService.GetBodyState(bodyRef.BodyHandle);

            // ── Write SimTransform (pose) ─────────────────────────────────────
            // Convert Stride world-space pose to FDP world-space.
            var newTransform = new SimTransform
            {
                Position = FdpStrideTransform.ToFdpPosition(state.Position),
                Rotation = FdpStrideTransform.ToFdpRotation(state.Rotation),
            };
            repo.SetComponent(entity, newTransform);

            // ── Current FDP position (just written to SimTransform above) ────
            var currentFdpPos = newTransform.Position;

            // ── Write SimVelocity (velocity invariant) ────────────────────────
            Vector3 linearFdp;
            Vector3 angularFdp;

            if (!state.IsKinematic)
            {
                // Dynamic body: solver-computed velocity (zero on collision arrest).
                linearFdp  = FdpStrideTransform.ToFdpVelocity(state.LinearVelocity);
                angularFdp = FdpStrideTransform.ToFdpAngularVelocity(state.AngularVelocity);
            }
            else if (bodyRef.ShapeKind == CollisionShapeKind.Capsule)
            {
                // CHARACTER (capsule) body: derive actual velocity from frame-to-frame
                // pose delta, then apply a light EMA to prevent jitter-driven blend toggling.
                //
                // Raw measured velocity = (currentPos − prevPos) / dt.
                // This is ~commanded speed while moving freely and ~zero when blocked at a
                // wall, satisfying the velocity invariant without any extra zeroing step.
                //
                // EMA smoothing (vSmooth = lerp(vPrevSmooth, vMeasured, EmaAlpha)):
                // The raw per-frame delta is jittery because Bullet's character controller
                // integrates discretely.  When the raw speed momentarily dips near the
                // idle/walk blend threshold the blend toggles and the clip visually resets
                // (stutter).  The EMA (α = EmaAlpha ≈ 0.25) keeps the blend stable during
                // steady walking yet converges to ~0 within ~4–8 frames when the character
                // stops at a wall — fast enough to not lag the stop-at-wall behaviour.
                //
                // NOTE: the motor's PostCollisionLinearVelocityFdp write remains in
                // BulletCharacterMotor (harmless — it is no longer read here for
                // capsules; KinematicVehicleMotor's capsule-skip guard also prevents
                // the vehicle motor from clobbering it).
                //
                // First frame (no previous position recorded): seed prevPos = currentPos
                // and report zero velocity (avoids a spurious spike on spawn).
                ulong key = entity.PackedValue;
                if (!_prevFdpPositions.TryGetValue(key, out var prevFdpPos))
                {
                    // Seed — no velocity on the first frame this entity is seen.
                    _prevFdpPositions[key]       = currentFdpPos;
                    _smoothedFdpVelocity[key]    = Vector3.Zero;
                    linearFdp  = Vector3.Zero;
                }
                else if (deltaTime > 0f)
                {
                    // Raw measured velocity from pose delta.
                    var rawVelocity = (currentFdpPos - prevFdpPos) / deltaTime;
                    _prevFdpPositions[key] = currentFdpPos;

                    // EMA smoothing: vSmooth = lerp(vPrevSmooth, vRaw, EmaAlpha).
                    if (!_smoothedFdpVelocity.TryGetValue(key, out var prevSmooth))
                        prevSmooth = Vector3.Zero;
                    var smoothed = Vector3.Lerp(prevSmooth, rawVelocity, EmaAlpha);
                    _smoothedFdpVelocity[key] = smoothed;

                    linearFdp = smoothed;
                }
                else
                {
                    // deltaTime == 0 (paused / first tick guard): don't divide by zero.
                    // Decay the smooth towards zero so it doesn't get stuck.
                    if (!_smoothedFdpVelocity.TryGetValue(key, out var prevSmooth))
                        prevSmooth = Vector3.Zero;
                    var smoothed = Vector3.Lerp(prevSmooth, Vector3.Zero, EmaAlpha);
                    _smoothedFdpVelocity[key] = smoothed;
                    linearFdp = smoothed;
                    // Don't update prevPos so next frame still has a valid prior sample.
                }
                angularFdp = Vector3.Zero; // characters don't yaw via angular velocity
            }
            else
            {
                // Kinematic body that is NOT a capsule character (unusual fallback path).
                // In the current design, vehicles are DYNAMIC (IsKinematic=false, handled
                // by the first branch above).  This branch is retained for any future
                // kinematic non-capsule body type; it reads the motor's post-collision
                // channel (already in FDP space — no conversion needed).
                linearFdp  = bodyRef.PostCollisionLinearVelocityFdp;
                angularFdp = bodyRef.PostCollisionAngularVelocityFdp;
            }

            repo.SetComponent(entity, new SimVelocity
            {
                Linear  = linearFdp,
                Angular = angularFdp,
            });

            // ── Throttled diagnostic: velocity source + SimVelocity written ──
            if (!_velocityLogCounter.TryGetValue(entity.PackedValue, out int logCount))
                logCount = 0;
            logCount++;
            if (logCount >= VelocityLogIntervalFrames)
            {
                logCount = 0;
                string source = !state.IsKinematic ? "Dynamic(solver)"
                    : bodyRef.ShapeKind == CollisionShapeKind.Capsule ? "MeasuredDelta"
                    : "PostCollision(kinematic-fallback)";
                Log.Debug(
                    "[BulletReverseSyncSystem] entity #{0} IsKinematic={1} source={2} " +
                    "SimVelocity written=({3:F2},{4:F2},{5:F2})",
                    entity.Index,
                    state.IsKinematic,
                    source,
                    linearFdp.X, linearFdp.Y, linearFdp.Z);
            }
            _velocityLogCounter[entity.PackedValue] = logCount;
        }
    }
}

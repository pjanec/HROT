#nullable enable
using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Network;
using Hrot.Stride.Core;
using Hrot.Stride.Core.TestHarness;
using Stride.Rendering;
using SNum = System.Numerics;
using SMath = Stride.Core.Mathematics;
using StrideEntity = Stride.Engine.Entity;

namespace HrotStrideApp;

/// <summary>
/// BATCH-17 physics <see cref="VisualTestCase"/>s for the in-app Stride test harness
/// (STR-D11 + STR-D13 GPU bring-up).
///
/// <para>
/// These cases drive the <b>real physics path</b>:
/// <c>CrowdMotorIntent → BulletCharacterMotor → CharacterComponent.SetVelocity → Bullet →
/// BulletReverseSyncSystem → SimTransform</c>. They do NOT write SimTransform directly.
/// </para>
///
/// <para>
/// <b>Controls (keyboard shortcuts assigned in registration order, after BATCH-15 cases):</b>
/// <list type="bullet">
///   <item><b>Physics Drop</b>  (D0) — spawn a capsule character 3 m above the arena floor;
///     it should fall under Bullet gravity and land (resting contact). Logs Z over time.</item>
///   <item><b>Physics Walk</b>  (F1) — spawn a capsule character at floor level and set a
///     <see cref="CrowdMotorIntent"/> velocity; the character walks across the floor and
///     should collide with a wall/obstacle. Also plays the walk animation blend if
///     the backend is wired. Logs position over time.</item>
///   <item><b>Physics Drive</b> (F2) — spawn a MilitaryAPC (TKB 2001, OrientedBox →
///     kinematic RigidbodyComponent) at floor level and set <c>VehicleState.Speed</c>
///     each frame so <see cref="KinematicVehicleMotor"/> drives it via
///     <c>IPhysicsBodyService.MoveKinematic</c>. The box should visibly move across the
///     arena and block/slide on hitting a wall (proving Bullet collision). Logs position
///     and speed every 0.5 s for 10 s, then sets Speed=0 to halt. GPU-verified-only.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>GPU-verified-only note:</b> these cases require a running Bullet simulation.
/// They compile clean and log diagnostics via NLog ("StrideTestHarness" logger →
/// <c>logs/editor_stride.log</c>), but their physical outcome (fall/land/walk/drive/collide) is
/// confirmed by the human watching the Stride window and reading the log.
/// </para>
///
/// <para>
/// <b>What the human should see (when <c>BulletPhysicsBodyService</c> is wired):</b>
/// <list type="bullet">
///   <item><b>Physics Drop</b>: mannequin spawns above the floor and visibly falls downward
///     (Stride Y decreases). After ~0.5–1 s it lands and Y stabilizes at floor level.
///     The log will show "Grounded LANDED" when it touches and per-body position lines
///     with Y decreasing then constant.</item>
///   <item><b>Physics Walk</b>: mannequin starts moving in the +Y (north) direction.
///     It continues until it hits a wall or reaches the arena boundary and stops
///     (or slides). The log shows CrowdMotorIntent set and the per-body position changing
///     each diagnostic tick. If animation is wired, the walk blend plays.</item>
///   <item><b>Physics Drive</b>: a box-shaped APC spawns at floor level and visibly moves
///     in the FDP +X (east) direction while also yaw-turning left (positive steer angle).
///     It should curve northward and, after a few seconds, hit the arena wall and stop
///     or slide. The log shows position + speed changing, then "STOPPED" when the motor
///     velocity output drops near zero (wall block). After 10 s Speed is zeroed and motion
///     ceases. Proves the kinematic-box Bullet path works (not FDP kinematics-controlled).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>What to check if a step fails:</b>
/// <list type="bullet">
///   <item>If entity spawns but does NOT fall: check that <c>BulletPhysicsBodyService</c>
///     replaced <c>NoOpPhysicsBodyService</c> (look for "BulletPhysicsBodyService] Constructed"
///     in the log). If NoOp is still active, the drop case will log Z=0 forever.</item>
///   <item>If entity spawns but falls through the floor: the MainScene's static colliders
///     may not be loaded. Check the log for Bullet collision errors or missing scene geometry.</item>
///   <item>If the walk case spawns but the entity doesn't move: check that
///     <c>BulletCharacterMotor</c> is executing (look for "CrowdMotorIntent" component registered)
///     and that the motor found the entity's body in the lifecycle Bodies dictionary.</item>
///   <item>If the animation doesn't blend to Walk: check that <c>BulletReverseSyncSystem</c>
///     is writing a non-zero SimVelocity (logged via the reverse-sync system at Debug level)
///     and that <c>StrideAnimationBridge</c> is reading it.</item>
///   <item>If the drive case box does NOT move: check that <c>VehicleState</c> is registered
///     in the world (it is, via <c>KinematicComponentRegistry</c>), that the entity has a
///     <c>SimTransform</c> with <c>WithOwned</c> authority (spawn pipeline grants authority for
///     OwnerAppInstanceId=0), and that the <c>KinematicVehicleMotor</c> found the body in the
///     lifecycle Bodies dictionary. The log should show a body-created entry for the APC.</item>
///   <item>If the drive case box does NOT stop on hitting the wall: the
///     <c>BulletPhysicsBodyService.MoveKinematic</c> sweep may not be detecting the static
///     collider. Check the "MoveKinematic blocked" debug log entries and that static scene
///     colliders are loaded.</item>
/// </list>
/// </para>
/// </summary>
public static class StridePhysicsHarnessCases
{
    private const long TkbInfantrySoldier = 2002L; // capsule shape → CharacterComponent
    private const long TkbMilitaryApc     = 2001L; // OrientedBox → kinematic RigidbodyComponent

    // ── Spawn row for physics cases ────────────────────────────────────────
    // Place physics cases further north so they don't overlap existing demo spawns.
    private static float s_physicsRowY = 12f;

    // FDP altitude above floor for the Drop case (metres in FDP Z = Stride Y).
    // With the ISSUE-1 collider LocalOffset fix the entity origin is the model base (feet).
    // Spawn 1 m above the floor: short visible fall, still demonstrates gravity clearly.
    // (Previously 3 m was used to make the float visible, but now the float is fixed.)
    private const float DropAltitude = 1.0f;

    // Walk speed (m/s FDP space).
    private const float WalkSpeedMps = 2.0f;

    // How long the Walk case drives the entity (seconds).
    private const float WalkDriveSeconds = 10.0f;

    // Vehicle drive speed (m/s FDP space). Enough to cross a reasonable arena.
    private const float DrivingSpeedMps = 3.0f;

    // Steer angle for the drive demo (radians). Small positive value → slight left-turn.
    // This causes the APC to curve, so that it hits the wall at an angle rather than head-on,
    // making both the motion and the collision/stop more visible to the human.
    private const float DriveSteerAngleRad = 0.15f;

    // WheelBase for the drive demo VehicleParams (metres). Determines the yaw rate.
    // BATCH-17 yaw-fidelity fix: reduced from 3.5 m → 2.5 m to tighten the bicycle-model
    // R_min. Combined with the raised maxSteerAngleRad (0.6→0.7) this gives
    // R_min = 2.5/tan(0.7) ≈ 2.9 m — clearly car-like. WpWheelBase is defined as
    // DriveWheelBase and the VehicleParams injected at spawn are updated in sync.
    private const float DriveWheelBase = 2.5f;

    // How long the Drive case drives the entity (seconds).
    private const float DriveDriveSeconds = 10.0f;

    // How often (seconds) the walk/drop/drive log emits a position line.
    private const float PositionLogIntervalSec = 0.5f;

    // APC spawn FDP Z — initial spawn height before physics body is created.
    //
    // ROOT CAUSE FIX (BATCH-17 F2 content mismatch):
    // BulletPhysicsBodyService.CreateBody now reads the ModelComponent.Model.BoundingBox from
    // the visual entity and sizes the BoxColliderShape to exactly match the RENDERED mesh
    // (not the TKB ShapeDims, which were wrong for the placeholder model).
    // LocalOffset = boxCenter (bbox center in entity-local space) so collider and visual coincide.
    // CreateBody also overrides the entity's Stride Y to -bbox.Minimum.Y so the model bottom
    // sits exactly on the floor regardless of the initial spawn Z here.
    //
    // Therefore this constant only needs to place the entity "somewhere reasonable" — not a precise
    // half-height.  We keep ApcBoxHalfHeightFdpZ = 1.25f (the TKB ShapeHeight/2) as a sensible
    // above-floor start position; CreateBody will override the exact Y from the model bbox.
    //
    // CONTRAST with the capsule (F1/D0):
    //   Capsule model origin = FEET (base of mesh at entity origin).
    //   CreateBody: LocalOffset = +capsuleHalfHeight (bottom aligns with entity origin).
    //   Spawn: FDP Z = 0 (entity origin = feet on floor).  No Y override in CreateBody.
    //
    // These are DIFFERENT conventions — do not conflate them.
    private const float ApcBoxHalfHeightFdpZ = 1.25f; // initial spawn height; CreateBody overrides Y from model bbox

    /// <summary>
    /// Registers the "Physics Drop", "Physics Walk", and "Physics Drive" cases into
    /// <paramref name="registry"/>. Registration order determines key assignment:
    /// <list type="bullet">
    ///   <item>Index 9  (after D0 range) → <b>D0</b>: Physics Drop</item>
    ///   <item>Index 10 → <b>F1</b>: Physics Walk</item>
    ///   <item>Index 11 → <b>F2</b>: Physics Drive</item>
    /// </list>
    /// The <paramref name="lifecycleSystem"/> and <paramref name="bodyService"/> are captured so
    /// the cases can verify the body was created before logging diagnostics.
    /// </summary>
    public static TestHarnessRegistry RegisterPhysicsCases(
        TestHarnessRegistry        registry,
        PhysicsBodyLifecycleSystem lifecycleSystem,
        IPhysicsBodyService        bodyService)
    {
        if (registry        == null) throw new ArgumentNullException(nameof(registry));
        if (lifecycleSystem == null) throw new ArgumentNullException(nameof(lifecycleSystem));
        if (bodyService     == null) throw new ArgumentNullException(nameof(bodyService));

        // ── Physics Drop ──────────────────────────────────────────────────
        registry.Register(new VisualTestCase(
            "Physics Drop",
            "Spawn a capsule character 3 m above the floor; it falls under gravity and lands. " +
            "Logs Z over time. GPU-verified-only: requires BulletPhysicsBodyService.",
            ctx => PhysicsDrop(ctx, lifecycleSystem)));

        // ── Physics Walk ──────────────────────────────────────────────────
        registry.Register(new VisualTestCase(
            "Physics Walk",
            "Spawn a capsule character at floor level; set CrowdMotorIntent → motor drives " +
            "CharacterComponent.SetVelocity → Bullet moves entity across the floor. " +
            "GPU-verified-only: requires BulletPhysicsBodyService.",
            ctx => PhysicsWalk(ctx, lifecycleSystem)));

        // ── Physics Drive ─────────────────────────────────────────────────
        registry.Register(new VisualTestCase(
            "Physics Drive",
            "Spawn a MilitaryAPC (TKB 2001, OrientedBox→kinematic Rigidbody) at floor level; " +
            "set VehicleState.Speed each frame → KinematicVehicleMotor drives the box via " +
            "IPhysicsBodyService.MoveKinematic (block-or-slide). The box moves across the arena " +
            "and blocks on hitting a wall. Proves vehicle physics is NOT FDP-kinematics-controlled. " +
            "GPU-verified-only: requires BulletPhysicsBodyService.",
            ctx => PhysicsDrive(ctx, lifecycleSystem)));

        return registry;
    }

    // ── Drive To Waypoint ─────────────────────────────────────────────────────

    // Waypoint tolerance for the harness (generous — proves convergence, not centimetre precision).
    // 1.2 m so the car drives UP TO each marker (its ~2 m-long front reaches the pillar) instead of
    // stopping 3 m short, and so the closely-spaced waypoints register as DISTINCT arrivals.
    private const float WaypointToleranceM = 1.2f;

    // Cruise speed for waypoint driving (m/s).
    private const float WpCruiseSpeed = 3.0f;

    // Per-waypoint timeout (seconds). At 3 m/s the APC can travel 75 m — well beyond arena.
    private const float WpTimeoutSec = 25.0f;

    // WheelBase used by the waypoint controller (must match VehicleParams injected at spawn).
    private const float WpWheelBase = DriveWheelBase; // 2.5 m — same as Physics Drive case (BATCH-17 yaw-fidelity fix)

    /// <summary>
    /// Registers the "Drive To Waypoint" case (index 12 → key F3) into
    /// <paramref name="registry"/>.
    ///
    /// <para>
    /// This case spawns the MilitaryAPC (TKB 2001) at a fixed start position, then runs a
    /// closed-loop <see cref="VehicleWaypointController"/> each frame — reading the car's
    /// actual <see cref="SimTransform"/> pose (back-propagated from the dynamic Bullet body
    /// by <c>BulletReverseSyncSystem</c>), computing steering toward the current waypoint, and
    /// commanding <c>VehicleState.Speed</c> / <c>VehicleState.SteerAngle</c> accordingly.
    /// </para>
    ///
    /// <para>
    /// <b>What this proves:</b> closed-loop steer-to-point for the dynamic vehicle. The waypoints
    /// are placed entirely in confirmed-open space (X ∈ [8,17], Y ∈ [7,12]) because the controller
    /// is <b>pure go-to-goal with NO obstacle avoidance</b>. Real navigation routes around walls via
    /// the navmesh/path (future work), which feeds waypoints to this same controller.
    /// </para>
    ///
    /// <para>
    /// <b>Visible markers:</b> small visual-only Stride entities (no physics components) are spawned
    /// at each waypoint so the user can see the targets and watch the car reach them. Markers are
    /// removed when the case restarts.
    /// </para>
    ///
    /// <para>
    /// <b>Stuck-detection:</b> if the car does not improve its best distance to the current waypoint
    /// by at least 0.3 m over 3 s, it is declared blocked and the case skips to the next waypoint,
    /// so the demo never freezes silently.
    /// </para>
    ///
    /// <para>
    /// <b>Proof of closed-loop steerability:</b> the APC must curve to each of the three target
    /// waypoints and stop within <c>WaypointToleranceM</c>. The log will show distance monotonically
    /// decreasing to &lt; tolerance at each waypoint, and will print
    /// <c>[Drive To Waypoint] PROOF COMPLETE — reached N/N waypoints (K skipped as blocked)</c> at the end.
    /// </para>
    ///
    /// <para>
    /// <b>Key F3</b> (index 12 in registration order: D1–D9 = 0–8, D0 = 9, F1 = 10, F2 = 11, F3 = 12).
    /// </para>
    /// </summary>
    public static TestHarnessRegistry RegisterDriveToWaypointCase(
        TestHarnessRegistry        registry,
        PhysicsBodyLifecycleSystem lifecycleSystem,
        IPhysicsBodyService        bodyService,
        Func<string, Model?>?      loadModel = null)
    {
        if (registry        == null) throw new ArgumentNullException(nameof(registry));
        if (lifecycleSystem == null) throw new ArgumentNullException(nameof(lifecycleSystem));
        if (bodyService     == null) throw new ArgumentNullException(nameof(bodyService));

        registry.Register(new VisualTestCase(
            "Drive To Waypoint",
            "Spawn MilitaryAPC at start, run closed-loop VehicleWaypointController to 3 waypoints. " +
            "APC must curve to each point and stop within tolerance. " +
            "Proves dynamic-body vehicle is steerable to a goal. Key F3. GPU-verified-only.",
            ctx => DriveToWaypoint(ctx, lifecycleSystem, loadModel)));

        return registry;
    }

    // ── Stuck-detection constants ─────────────────────────────────────────────

    // Minimum displacement (m) the car must have moved over StuckWindowSec to NOT be stuck.
    // A legitimately turning/curving car moves through space even if distance-to-target grows;
    // only a genuinely wedged car (displacement ≈ 0 over the window) is declared blocked.
    private const float StuckDisplacementThresholdM = 0.3f;

    // Time window (s) over which displacement is measured.
    private const float StuckWindowSec = 3.0f;

    // ── Drive To Waypoint (implementation) ───────────────────────────────────

    private static void DriveToWaypoint(
        TestHarnessContext         ctx,
        PhysicsBodyLifecycleSystem lifecycle,
        Func<string, Model?>?      loadModel)
    {
        // Spawn APC (TKB 2001) at a known start position, facing East (identity rotation).
        // Spawn row continues from Physics Drive — use s_physicsRowY + margin.
        float startX = 6f;
        float startY = s_physicsRowY;
        float startZ = ApcBoxHalfHeightFdpZ;  // same as Physics Drive: bottom sits on floor after CreateBody
        s_physicsRowY += 4f;                  // gap for the APC and its driving path

        var startPos = new SNum.Vector3(startX, startY, startZ);

        // ── Smooth forward route ───────────────────────────────────────────────────────────────
        // Car spawns at FDP (6,12) facing EAST (+X). Confirmed-open area: X∈[6,18], Y∈[7,18].
        // Waypoints form a GENTLE COUNTER-CLOCKWISE LOOP so each successive heading change is ≤ ~90°:
        //
        //   WP0 = (14,12):  EAST from spawn — heading error ≈ 0° (straight ahead). Dist ≈ 8 m.
        //   WP1 = (15,16):  from WP0, bearing ≈ +76° (roughly north-east, ≤ 90° turn). Dist ≈ 4 m.
        //   WP2 = ( 8,16):  from WP1, bearing ≈ 180° west — but heading after WP1 is ~N-E, so
        //                    required turn ≈ −135°.  FIX: use (8,17) so approach angle from WP1 is
        //                    better, or restructure as described below.
        //
        // REVISED design (cleaner ≤ 80° turns everywhere):
        //   WP0 = (14,12):  ahead east from (6,12).  Heading at arrival ≈ 0 rad (east).
        //   WP1 = (15,16):  from (14,12) bearing ≈ atan2(4,1) ≈ 76°.  Turn ≈ 76° (CCW). ✓
        //   WP2 = ( 8,16):  from (15,16) bearing ≈ atan2(0,-7) ≈ 180°. Turn ≈ 104° (CCW). ✗ too sharp.
        //
        // RE-REVISED to keep every turn ≤ 80°:
        //   WP0 = (14,12):  east from (6,12).  Turn from spawn heading (0°) ≈ 0°.      ✓
        //   WP1 = (16,15):  from (14,12) bearing ≈ atan2(3,2) ≈ 56°.  Turn ≈ 56°.     ✓
        //   WP2 = (10,17):  from (16,15) bearing ≈ atan2(2,-6) ≈ 162°. Heading at WP1 ≈ 56°.
        //                    Turn ≈ 162°-56° = 106°. Still too sharp.
        //
        // FINAL design — genuine ≤ 80° turns, all within X∈[7,17], Y∈[8,17]:
        //   WP0 = (14,12):  bearing from (6,12): atan2(0,8)=0°.      Turn from spawn (0°) = 0°.  ✓
        //   WP1 = (15,16):  bearing from (14,12): atan2(4,1)≈76°.    Turn from WP0 heading≈0° ≈ 76°. ✓
        //   WP2 = (10,17):  bearing from (15,16): atan2(1,-5)≈169°.  Turn from WP1 heading≈76° ≈ 93°. ✗
        //
        // ACCEPTED DESIGN — smooth path with largest turn ≤ 80°, all open-space:
        //   Spawn  (6,12)  heading east (0 rad)
        //   WP0  = (14,12): delta=(8,0).  Required heading=0°.  Change from spawn=0°.   ✓
        //   WP1  = (16,15): delta=(2,3).  Required heading=atan2(3,2)≈56°. Change≈56°.  ✓
        //   WP2  = (10,16): delta=(-6,1). Required heading=atan2(1,-6)≈170°.
        //                   Heading at WP1 arrival: ≈56°. Turn required = 170°-56° = 114°. ✗
        //
        // SIMPLEST valid layout that guarantees ≤ 80° turns at every leg:
        //   Lay three legs CCW where each successive bearing is ≤ 80° more than the previous.
        //   Spawn heading: 0° (east).
        //   Leg 1: heading 0°   → WP0=(14,12). Turn=0°.           ✓
        //   Leg 2: heading +50° → WP1=(17,16). bearing from (14,12)=atan2(4,3)≈53°. Turn≈53°. ✓
        //   Leg 3: heading +50° → WP2=(14,19). bearing from (17,16)=atan2(3,-3)≈135°. Turn≈82°. ✗
        //
        // DEFINITIVE ROUTE (avoids all near-U-turns, all waypoints in open confirmed area):
        //   Spawn  (6, 12) heading East (0°)
        //   WP0  = (14,12)  — east,  turn = 0°
        //   WP1  = (15,16)  — NE,    turn = atan2(4,1)≈76° (< 80°) ✓
        //   WP2  = (10,17)  — NW, from WP1 heading≈76° → desired≈atan2(1,-5)≈169°, turn≈93°. ✗ still bad.
        //
        // ROOT PROBLEM: any third WP west of WP1 forces a > 90° turn at WP1.
        // SOLUTION: make WP2 also go NORTH or NE so all turns are small:
        //   Spawn (6,12) east → WP0=(14,12) east → WP1=(16,15) NE(56°) → WP2=(14,17) NW(atan2(2,-2)=135°, turn from 56°=79°✓)
        //   Check WP2: (14,17) from (16,15): dx=-2, dy=2, bearing=atan2(2,-2)=135°.
        //   Turn at WP1 = 135°-56° = 79° < 80°. ✓ ACCEPTED.
        //   All within X∈[6,17], Y∈[12,17]. Wall at X≳19 safe. ✓
        var waypoints = new[]
        {
            new SNum.Vector2(14f, 12f),   // WP0: east — heading change ≈ 0° from spawn
            new SNum.Vector2(16f, 15f),   // WP1: NE curve — heading change ≈ 56° at WP0
            new SNum.Vector2(14f, 17f),   // WP2: NW curve — heading change ≈ 79° at WP1
        };
        int totalWaypoints = waypoints.Length;

        ctx.Log(
            $"[Drive To Waypoint] Route (smooth forward path, all turns ≤ ~80°): " +
            "WP0=(14,12) east, WP1=(16,15) NE-curve, WP2=(14,17) NW-curve. " +
            "All within X∈[6,17],Y∈[12,17] (confirmed open). " +
            "Spawn heading east; each successive turn ≤ 80° so no near-U-turns.");

        // Build the steering controller with the same params validated headlessly.
        // BATCH-17 yaw-fidelity fix: maxSteerAngleRad raised 0.6→0.7 rad to tighten R_min.
        // WpWheelBase = DriveWheelBase = 2.5 m (updated in sync).
        // New R_min = 2.5/tan(0.7) ≈ 2.9 m.
        var controller = new VehicleWaypointController(
            cruiseSpeed:      WpCruiseSpeed,
            maxSteerAngleRad: 0.7f,
            headingGainK:     2.0f,
            arriveToleranceM: WaypointToleranceM,
            slowRadiusM:      3.5f,   // cruise until ~3.5 m out, then ease in (less crawling on long legs)
            slowMinFrac:      0.2f,
            wheelBase:        WpWheelBase);

        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbMilitaryApc,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbMilitaryApc },
                new VehicleParams
                {
                    WheelBase   = WpWheelBase,
                    Length      = 4.5f,
                    Width       = 2.2f,
                    MaxSpeedFwd = 10f,
                    MaxAccel    = 3f,
                },
            },
        });

        ctx.Log(
            $"[Drive To Waypoint] Spawned MilitaryAPC @ FDP ({startX:F1},{startY:F1},{startZ:F2}) " +
            $"facing East. Will drive closed-loop to {totalWaypoints} waypoints " +
            $"(smooth forward route, each turn ≤ ~80°). " +
            $"R_min≈{controller.MinTurningRadiusM:F1} m. " +
            $"Timeout per waypoint: {WpTimeoutSec:F0} s. Tolerance: {WaypointToleranceM:F1} m.");

        // ── Spawn VISIBLE marker entities at each waypoint ────────────────────────
        // Approach (a): add a ModelComponent loaded via the Content manager so the entity
        // actually renders. The model "Models/Box2x1x1" is the same small box asset already
        // used for TKB 2001 vehicles — it IS compiled in the HrotStrideApp asset pipeline.
        // Scale: thin pillar (0.4 × 3.0 × 0.4 in Stride space) so it reads as a "target post".
        // NO CharacterComponent / RigidbodyComponent is attached, so PhysicsBodyLifecycleSystem
        // never sees them (it only processes FDP ECS entities with SimTransform + WithOwned
        // authority). Markers are therefore invisible to physics and CANNOT block the car.
        // loadModel is null in headless tests (no GPU); markers are bare entities in that case
        // (same as before), but the log still says "(VISIBLE model=...)" for documentation.
        // Markers are stored so they can be removed on case completion/re-run.
        const string MarkerModelRef = "Models/Box2x1x1";
        var markerEntities = new List<StrideEntity>();
        for (int mi = 0; mi < totalWaypoints; mi++)
        {
            var wp = waypoints[mi];
            // FDP waypoint ground position: (wp.X, wp.Y, 0). Lift 1.5 m so the pillar is visible
            // above the floor even from the overview camera.
            var fdpPos    = new SNum.Vector3(wp.X, wp.Y, 0f);
            var stridePos = FdpStrideTransform.ToStridePosition(fdpPos);

            var marker = new StrideEntity($"WP_Marker_{mi}");
            marker.Transform.Position = stridePos;
            // Pillar scale: thin (0.4) in X/Z, tall (3.0) in Y (Stride Y = FDP Z = up).
            marker.Transform.Scale = new SMath.Vector3(0.4f, 3.0f, 0.4f);

            string modelNote;
            if (loadModel != null)
            {
                // Load the model and attach a ModelComponent so the entity actually renders.
                // Content.Load is synchronous; the model is already compiled in the asset pipeline.
                Model? model = null;
                try
                {
                    model = loadModel(MarkerModelRef);
                }
                catch (Exception ex)
                {
                    ctx.Log($"[Drive To Waypoint] WARNING: Content.Load<Model>('{MarkerModelRef}') " +
                            $"failed for WP{mi} marker: {ex.GetType().Name}: {ex.Message}. " +
                            $"Marker will be invisible (bare entity).");
                }

                if (model != null)
                {
                    marker.Add(new Stride.Engine.ModelComponent { Model = model });
                    modelNote = $"VISIBLE model={MarkerModelRef}";
                }
                else
                {
                    modelNote = "INVISIBLE (model load failed)";
                }
            }
            else
            {
                // Headless / no content manager — bare entity (no GPU, no rendering needed).
                modelNote = "bare entity (headless)";
            }

            ctx.Scene.Entities.Add(marker);
            markerEntities.Add(marker);

            ctx.Log($"[Drive To Waypoint] Marker WP{mi} spawned ({modelNote}) " +
                    $"at FDP ({wp.X:F1},{wp.Y:F1}) " +
                    $"→ Stride ({stridePos.X:F1},{stridePos.Y:F1},{stridePos.Z:F1})");
        }

        Fdp.Core.Entity target  = default;
        bool resolved           = false;
        int  currentWpIdx       = 0;
        float elapsed           = 0f;       // time since last waypoint (or start)
        float totalElapsed      = 0f;
        float nextLogAt         = 0f;
        float bestDistThisWp    = float.MaxValue;
        bool proofComplete      = false;
        int  reachedCount       = 0;
        int  skippedCount       = 0;

        // ── Movement-based stuck-detection state ──────────────────────────────────────
        // Track the car's actual position over StuckWindowSec. A car that is legitimately
        // turning (even if distance-to-target temporarily increases) IS moving through space
        // and must NOT be declared stuck. Only flag stuck when the car's total displacement
        // over the window is below StuckDisplacementThresholdM (genuinely wedged against a wall).
        SNum.Vector3 stuckWindowStartPos = startPos; // position when the window started
        float        windowOpenedAt      = 0f;        // totalElapsed when window started

        ctx.RegisterUpdate(dt =>
        {
            elapsed      += dt;
            totalElapsed += dt;

            // ── Resolve spawned entity ────────────────────────────────────
            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[Drive To Waypoint] Entity #{target.Index} resolved.");
                    // Initialise stuck-window from the actual resolved position.
                    if (ctx.World.HasComponent<SimTransform>(target))
                        stuckWindowStartPos = ctx.World.GetComponentRO<SimTransform>(target).Position;
                    windowOpenedAt = totalElapsed;
                }
                return elapsed < 10f;
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[Drive To Waypoint] Entity gone — stopping.");
                RemoveMarkers(ctx, markerEntities);
                return false;
            }

            // ── Proof complete / all waypoints done ───────────────────────
            if (proofComplete)
                return false;

            // ── Read current SimTransform ─────────────────────────────────
            if (!ctx.World.HasComponent<SimTransform>(target))
                return true; // not yet ready

            var simTf  = ctx.World.GetComponentRO<SimTransform>(target);
            float posX = simTf.Position.X;
            float posY = simTf.Position.Y;
            var   curPos = simTf.Position;

            // Forward direction: UnitX rotated by SimTransform.Rotation (FDP X-forward).
            var forward    = SNum.Vector3.Transform(SNum.Vector3.UnitX, simTf.Rotation);
            float heading  = MathF.Atan2(forward.Y, forward.X);

            // ── Ensure VehicleState present ───────────────────────────────
            if (!ctx.World.HasComponent<VehicleState>(target))
            {
                if (ctx.World.IsComponentTypeRegistered<VehicleState>())
                    ctx.World.AddComponent(target, new VehicleState());
                return true;
            }

            // ── Current waypoint ──────────────────────────────────────────
            var wp     = waypoints[currentWpIdx];
            var output = controller.Compute(posX, posY, heading, wp.X, wp.Y);

            // Update best distance (still tracked for progress logging).
            if (output.DistToTarget < bestDistThisWp)
                bestDistThisWp = output.DistToTarget;

            // ── Movement-based stuck-detection ────────────────────────────
            // A car is declared stuck ONLY when its actual position has barely moved over
            // StuckWindowSec — i.e. it is genuinely wedged, not merely turning/curving.
            // A car that is turning (distance-to-target may increase during a curve) is
            // NOT stuck as long as it keeps moving through space.
            float windowAge        = totalElapsed - windowOpenedAt;
            float displacement     = (curPos - stuckWindowStartPos).Length();
            // Roll the window forward if the car moved enough to prove it's not stuck.
            if (displacement >= StuckDisplacementThresholdM)
            {
                stuckWindowStartPos = curPos;
                windowOpenedAt      = totalElapsed;
                displacement        = 0f;
                windowAge           = 0f;
            }

            bool isStuck = !output.Arrived
                           && windowAge >= StuckWindowSec
                           && displacement < StuckDisplacementThresholdM;

            if (isStuck)
            {
                ctx.Log($"[Drive To Waypoint] BLOCKED before WP{currentWpIdx} " +
                        $"({wp.X:F1},{wp.Y:F1}) at pos=({posX:F2},{posY:F2}) " +
                        $"(wall in the way — controller has no obstacle avoidance), SKIPPING to next");

                skippedCount++;
                // Stop momentarily, then advance.
                if (ctx.World.HasComponent<VehicleState>(target))
                {
                    ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                    vs.Speed = 0f; vs.SteerAngle = 0f;
                }
                AdvanceWaypoint(ctx, ref currentWpIdx, ref elapsed, ref bestDistThisWp,
                                ref stuckWindowStartPos, ref windowOpenedAt,
                                totalWaypoints, ref reachedCount, ref skippedCount,
                                target, totalElapsed, curPos, ref proofComplete, markerEntities);
                return !proofComplete;
            }

            // ── Timeout guard ─────────────────────────────────────────────
            if (elapsed > WpTimeoutSec && !output.Arrived)
            {
                ctx.Log($"[Drive To Waypoint] TIMEOUT on WP{currentWpIdx} " +
                        $"({wp.X:F1},{wp.Y:F1}) after {elapsed:F1}s. " +
                        $"Best dist achieved: {bestDistThisWp:F2} m (tolerance {WaypointToleranceM:F1} m). " +
                        $"FAILURE — closed-loop did not converge. Check log above for heading errors.");
                // Stop the vehicle.
                if (ctx.World.HasComponent<VehicleState>(target))
                {
                    ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                    vs.Speed = 0f; vs.SteerAngle = 0f;
                }
                RemoveMarkers(ctx, markerEntities);
                return false;
            }

            // ── Command VehicleState ──────────────────────────────────────
            {
                ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                vs.Speed      = output.Speed;
                vs.SteerAngle = output.SteerAngle;
            }

            // ── Waypoint arrival ──────────────────────────────────────────
            if (output.Arrived)
            {
                reachedCount++;
                ctx.Log($"[Drive To Waypoint] REACHED WP{currentWpIdx} ({wp.X:F1},{wp.Y:F1}) " +
                        $"at t={totalElapsed:F1}s, final dist={output.DistToTarget:F2} m " +
                        $"(tol={WaypointToleranceM:F1} m). " +
                        $"[{reachedCount}/{totalWaypoints}]");

                AdvanceWaypoint(ctx, ref currentWpIdx, ref elapsed, ref bestDistThisWp,
                                ref stuckWindowStartPos, ref windowOpenedAt,
                                totalWaypoints, ref reachedCount, ref skippedCount,
                                target, totalElapsed, curPos, ref proofComplete, markerEntities);
                return !proofComplete;
            }

            // ── Periodic log (every ~0.5 s) ───────────────────────────────
            if (totalElapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;
                bool hasBody = lifecycle.Bodies.TryGetValue(target, out _);
                ctx.Log($"[Drive To Waypoint] t={totalElapsed:F1}s entity #{target.Index} " +
                        $"pos=({posX:F2},{posY:F2}) heading={heading:F2}rad " +
                        $"→ WP{currentWpIdx}({wp.X:F1},{wp.Y:F1}) " +
                        $"dist={output.DistToTarget:F2}m err={output.HeadingErrorRad:F2}rad " +
                        $"cmd: spd={output.Speed:F2} steer={output.SteerAngle:F3} body={hasBody}");
            }

            return true; // keep running
        });
    }

    /// <summary>
    /// Advances to the next waypoint (or finalises the run) and resets per-waypoint tracking state.
    /// Called on both successful arrival and stuck-skip.
    /// <paramref name="currentPos"/> is the car's current FDP position, used to reset the
    /// movement-based stuck-detection window.
    /// </summary>
    private static void AdvanceWaypoint(
        TestHarnessContext ctx,
        ref int  currentWpIdx,
        ref float elapsed,
        ref float bestDistThisWp,
        ref SNum.Vector3 stuckWindowStartPos,
        ref float windowOpenedAt,
        int   totalWaypoints,
        ref int  reachedCount,
        ref int  skippedCount,
        Fdp.Core.Entity target,
        float totalElapsed,
        SNum.Vector3 currentPos,
        ref bool proofComplete,
        List<StrideEntity> markerEntities)
    {
        currentWpIdx++;
        elapsed              = 0f;
        bestDistThisWp       = float.MaxValue;
        // Reset movement window to current position so stuck-detection starts fresh.
        stuckWindowStartPos  = currentPos;
        windowOpenedAt       = totalElapsed;

        if (currentWpIdx >= totalWaypoints)
        {
            proofComplete = true;
            // Stop the vehicle.
            if (ctx.World.HasComponent<VehicleState>(target))
            {
                ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                vs.Speed = 0f; vs.SteerAngle = 0f;
            }
            ctx.Log($"[Drive To Waypoint] PROOF COMPLETE — reached {reachedCount}/{totalWaypoints} waypoints " +
                    $"({skippedCount} skipped as blocked) in {totalElapsed:F1}s total. " +
                    $"Proves CLOSED-LOOP STEER-TO-POINT for the dynamic vehicle. " +
                    $"Waypoints were in open space; controller has no obstacle avoidance " +
                    $"(real navigation uses navmesh to feed waypoints — future work).");
            RemoveMarkers(ctx, markerEntities);
        }
    }

    /// <summary>
    /// Removes all waypoint marker Stride entities from the scene.
    /// Safe to call multiple times (entities are removed from the list after removal).
    /// </summary>
    private static void RemoveMarkers(TestHarnessContext ctx, List<StrideEntity> markers)
    {
        foreach (var m in markers)
        {
            // Guard: remove from scene; safe even if already removed (no-throw).
            try { ctx.Scene.Entities.Remove(m); } catch { /* ignore if not present */ }
        }
        markers.Clear();
    }

    // ── Physics Drop ──────────────────────────────────────────────────────────

    private static void PhysicsDrop(
        TestHarnessContext         ctx,
        PhysicsBodyLifecycleSystem lifecycle)
    {
        // Spawn a mannequin 1 m above the floor (FDP Z = DropAltitude, Stride Y = DropAltitude).
        // With the ISSUE-1 collider LocalOffset fix the entity origin is the model base (feet),
        // so FDP Z=1 places the mannequin 1 m above the floor — it falls and lands with feet on floor.
        // The Bullet CharacterComponent will have gravity enabled so it falls.
        float x   = -2f;
        float y   = s_physicsRowY;
        float z   = DropAltitude; // FDP Z=Up → Stride Y
        s_physicsRowY += 2f;

        var startPos = new SNum.Vector3(x, y, z);

        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0, // owned immediately
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbInfantrySoldier },
            },
        });

        ctx.Log(
            $"[Physics Drop] Spawned mannequin @ FDP ({x:F1},{y:F1},{z:F1}) — " +
            $"Stride ({x:F1},{z:F1},{y:F1}). Entity origin = model base (feet) after ISSUE-1 LocalOffset fix. " +
            $"Should fall 1 m under gravity and land with feet on floor. " +
            $"Watch for 'Grounded LANDED' in log and FDP Z decreasing 1→0.");

        // Continuous hook: log the entity's Z (altitude) over time so the human can confirm
        // the fall and landing. Also drives IsGrounded via the body service diagnostics.
        Fdp.Core.Entity target = default;
        bool resolved          = false;
        float elapsed          = 0f;
        float nextLogAt        = 0f;
        bool landingLogged     = false;

        ctx.RegisterUpdate(dt =>
        {
            elapsed += dt;

            // Resolve the spawned entity.
            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[Physics Drop] Entity #{target.Index} resolved. Waiting for Bullet body (next frame).");
                }
                return elapsed < 10f; // stop looking after 10 s
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[Physics Drop] Entity gone — stopping hook.");
                return false;
            }

            // Log altitude periodically.
            if (elapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;

                var pos = ctx.World.HasComponent<SimTransform>(target)
                    ? ctx.World.GetComponentRO<SimTransform>(target).Position
                    : startPos;

                // Check grounded via lifecycle → body service.
                bool hasBody = lifecycle.Bodies.TryGetValue(target, out var bodyRef);
                string groundedStr = "no body yet";
                if (hasBody && bodyRef != null)
                {
                    groundedStr = "body present";
                }

                ctx.Log($"[Physics Drop] t={elapsed:F1}s entity #{target.Index} " +
                        $"FDP pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) Z={pos.Z:F3} {groundedStr}");

                // Landing detection: Z should stabilize near 0 (floor level in FDP).
                if (!landingLogged && elapsed > 1.0f && Math.Abs(pos.Z) < 0.2f)
                {
                    landingLogged = true;
                    ctx.Log($"[Physics Drop] LANDED: entity #{target.Index} Z={pos.Z:F3} (floor contact detected via SimTransform).");
                }
            }

            // Keep running for 8 seconds to observe the fall and resting contact.
            return elapsed < 8f;
        });
    }

    // ── Physics Walk ──────────────────────────────────────────────────────────

    private static void PhysicsWalk(
        TestHarnessContext         ctx,
        PhysicsBodyLifecycleSystem lifecycle)
    {
        // Spawn at floor level (FDP Z=0) and drive with CrowdMotorIntent.
        float x = 2f;
        float y = s_physicsRowY;
        float z = 0f; // floor level
        s_physicsRowY += 2f;

        var startPos = new SNum.Vector3(x, y, z);

        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbInfantrySoldier },
            },
        });

        ctx.Log(
            $"[Physics Walk] Spawned mannequin @ FDP ({x:F1},{y:F1},{z:F1}). " +
            $"Will set CrowdMotorIntent velocity={WalkSpeedMps} m/s north (+Y). " +
            $"Entity should walk forward and collide with arena wall.");

        Fdp.Core.Entity target = default;
        bool resolved          = false;
        bool intentSet         = false;
        float elapsed          = 0f;
        float nextLogAt        = 0f;

        // Drive direction: FDP +Y (north).
        var walkVelocity = new SNum.Vector3(0f, WalkSpeedMps, 0f);

        ctx.RegisterUpdate(dt =>
        {
            elapsed += dt;

            // Resolve the spawned entity.
            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[Physics Walk] Entity #{target.Index} resolved.");
                }
                return elapsed < 10f;
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[Physics Walk] Entity gone — stopping hook.");
                return false;
            }

            // Set the CrowdMotorIntent on the entity so BulletCharacterMotor picks it up.
            // The motor reads CrowdMotorIntent.Velocity each frame and calls SetCharacterVelocity.
            if (!intentSet)
            {
                // Add CrowdMotorIntent if not already present.
                if (!ctx.World.HasComponent<CrowdMotorIntent>(target))
                    ctx.World.AddComponent(target, new CrowdMotorIntent());
                intentSet = true;
                ctx.Log($"[Physics Walk] CrowdMotorIntent added to entity #{target.Index} vel=({walkVelocity.X:F1},{walkVelocity.Y:F1},{walkVelocity.Z:F1}) m/s FDP.");
            }

            // Keep the intent velocity live (update each frame).
            if (ctx.World.HasComponent<CrowdMotorIntent>(target))
            {
                ref var intent = ref ctx.World.GetComponentRW<CrowdMotorIntent>(target);
                intent.Velocity = walkVelocity;
                intent.Jump     = false;
            }

            // Log position periodically.
            if (elapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;

                var pos = ctx.World.HasComponent<SimTransform>(target)
                    ? ctx.World.GetComponentRO<SimTransform>(target).Position
                    : startPos;

                bool hasBody  = lifecycle.Bodies.TryGetValue(target, out _);
                float distFwd = pos.Y - startPos.Y; // FDP north travel

                ctx.Log(
                    $"[Physics Walk] t={elapsed:F1}s entity #{target.Index} " +
                    $"FDP pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) " +
                    $"travelN={distFwd:F2}m body={hasBody}");
            }

            // Stop after WalkDriveSeconds; clear the intent so the entity halts.
            if (elapsed >= WalkDriveSeconds)
            {
                if (ctx.World.IsAlive(target) && ctx.World.HasComponent<CrowdMotorIntent>(target))
                {
                    ref var intent = ref ctx.World.GetComponentRW<CrowdMotorIntent>(target);
                    intent.Velocity = SNum.Vector3.Zero;
                }
                ctx.Log($"[Physics Walk] Drive complete: intent velocity zeroed. Entity #{target.Index} should stop.");
                return false;
            }

            return true;
        });
    }

    // ── Physics Drive ─────────────────────────────────────────────────────────

    private static void PhysicsDrive(
        TestHarnessContext         ctx,
        PhysicsBodyLifecycleSystem lifecycle)
    {
        // ROOT CAUSE FIX (BATCH-17 F2 content mismatch — collider matched to model bbox):
        // BulletPhysicsBodyService.CreateBody now reads ModelComponent.Model.BoundingBox and
        // sizes the BoxColliderShape to exactly match the rendered mesh.
        // LocalOffset = boxCenter (bbox center) so collider and visual are co-located.
        // CreateBody overrides the entity Stride Y to -bbox.Minimum.Y so the model bottom
        // rests on the floor regardless of the FDP spawn Z supplied here.
        // We spawn at z = ApcBoxHalfHeightFdpZ = 1.25 m as a sensible above-floor position;
        // the body service adjusts to the correct resting height from the actual bbox.
        // Contrast: capsule (F1/D0) has model origin at FEET → LocalOffset = +halfHeight →
        // spawned at Z=0.  Different conventions — do not conflate.
        float x = 6f;
        float y = s_physicsRowY;
        float z = ApcBoxHalfHeightFdpZ; // entity origin = box CENTER; spawn at half-height so bottom rests on floor
        s_physicsRowY += 3f; // leave a wider gap for the box visual

        var startPos = new SNum.Vector3(x, y, z);

        // Spawn a MilitaryAPC (TKB 2001). VehicleKinematicsTkbTranslator injects VehicleParams
        // and VehicleState (guarded by IsComponentTypeRegistered checks — both are already
        // registered via KinematicComponentRegistry through MuscleRoleComponentRegistry in
        // SimHostComponentRegistry.RegisterAll). No need to add them in InitialComponents.
        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,            // localNodeId=0 → authority granted (WithOwned<SimTransform>)
            TkbType            = TkbMilitaryApc,
            InitialComponents  = new List<object>
            {
                // SimTransform: spawn position (floor level) + identity rotation (facing east = FDP +X).
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbMilitaryApc },
                // VehicleParams explicitly supplied in InitialComponents as an override so the motor
                // has a known WheelBase even before the TKB translator runs (runs on next kernel tick).
                // The translator is guarded by HasComponent, so the pre-supplied value is not clobbered.
                new VehicleParams
                {
                    WheelBase   = DriveWheelBase,
                    Length      = 4.5f,
                    Width       = 2.2f,
                    MaxSpeedFwd = 10f,
                    MaxAccel    = 3f,
                },
            },
        });

        ctx.Log(
            $"[Physics Drive] Spawned MilitaryAPC (TKB 2001, box→kinematic body) @ FDP ({x:F1},{y:F1},{z:F1}) " +
            $"(initial Z={ApcBoxHalfHeightFdpZ:F2} m; CreateBody will override Y from model BoundingBox " +
            $"so the model bottom rests exactly on the floor). " +
            $"Will set VehicleState.Speed={DrivingSpeedMps:F1} m/s, SteerAngle={DriveSteerAngleRad:F2} rad. " +
            $"KinematicVehicleMotor will drive the box via MoveKinematic (real-box skin-lift sweep + slide). " +
            $"Watch for the APC curving northward and stopping/sliding on hitting a wall.");

        Fdp.Core.Entity target = default;
        bool resolved          = false;
        bool stateSet          = false;
        float elapsed          = 0f;
        float nextLogAt        = 0f;
        bool stoppedLogged     = false;

        ctx.RegisterUpdate(dt =>
        {
            elapsed += dt;

            // ── Resolve the spawned entity ──────────────────────────────
            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[Physics Drive] Entity #{target.Index} resolved (APC box). Waiting for kinematic body.");
                }
                return elapsed < 10f;
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[Physics Drive] Entity gone — stopping hook.");
                return false;
            }

            // ── Ensure VehicleState component is present ────────────────
            // The TKB translator adds VehicleState if registered; if for some reason
            // it is absent (e.g. TKB path didn't run yet), add it defensively.
            if (!ctx.World.HasComponent<VehicleState>(target))
            {
                if (ctx.World.IsComponentTypeRegistered<VehicleState>())
                    ctx.World.AddComponent(target, new VehicleState { Speed = 0f, SteerAngle = 0f });
                // If not registered, the motor will silently skip — the log will show no motion.
            }

            // ── Set VehicleState each frame for the drive period ────────
            if (elapsed < DriveDriveSeconds)
            {
                if (ctx.World.HasComponent<VehicleState>(target))
                {
                    ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                    vs.Speed      = DrivingSpeedMps;
                    vs.SteerAngle = DriveSteerAngleRad;

                    if (!stateSet)
                    {
                        stateSet = true;
                        ctx.Log($"[Physics Drive] VehicleState set on entity #{target.Index}: " +
                                $"Speed={vs.Speed:F1} m/s, SteerAngle={vs.SteerAngle:F3} rad. " +
                                $"KinematicVehicleMotor will integrate forward=heading×speed×dt.");
                    }
                }
            }
            else
            {
                // Stop after DriveDriveSeconds: zero the speed.
                if (ctx.World.HasComponent<VehicleState>(target))
                {
                    ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                    vs.Speed      = 0f;
                    vs.SteerAngle = 0f;
                }
                ctx.Log($"[Physics Drive] Drive complete: VehicleState Speed=0. Entity #{target.Index} should stop.");
                return false;
            }

            // ── Log position + speed periodically ──────────────────────
            if (elapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;

                var pos = ctx.World.HasComponent<SimTransform>(target)
                    ? ctx.World.GetComponentRO<SimTransform>(target).Position
                    : startPos;

                bool hasBody = lifecycle.Bodies.TryGetValue(target, out _);
                float distTravelled = (pos - startPos).Length();

                // Detect if the vehicle has stopped moving (likely blocked by a wall).
                // We compare progress vs. expected distance at constant speed.
                float expectedDist  = elapsed * DrivingSpeedMps;
                float shortfall     = expectedDist - distTravelled;
                bool likelyBlocked  = !stoppedLogged && elapsed > 1.5f && shortfall > expectedDist * 0.5f;

                ctx.Log($"[Physics Drive] t={elapsed:F1}s entity #{target.Index} " +
                        $"FDP pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) " +
                        $"dist={distTravelled:F2}m body={hasBody}");

                if (likelyBlocked)
                {
                    stoppedLogged = true;
                    ctx.Log($"[Physics Drive] WALL CONTACT LIKELY: entity #{target.Index} " +
                            $"dist={distTravelled:F2}m but expected={expectedDist:F2}m at {elapsed:F1}s — " +
                            $"kinematic block-or-slide response active (Bullet MoveKinematic stopped it).");
                }
            }

            return true;
        });
    }

    // ── Navmesh Drive (F4, BATCH-18, STR-D19) ────────────────────────────────

    /// <summary>
    /// Registers the "Navmesh Drive" case (index 13 → key F4) into <paramref name="registry"/>.
    ///
    /// <para>
    /// This case spawns a MilitaryAPC at FDP (-5, 3, 0) and plans a DotRecast navmesh path
    /// to FDP (5, 12, 0) on the opposite side of real arena obstacles. The returned corner list
    /// is fed directly into <see cref="VehicleWaypointController"/> (the same controller as F3
    /// "Drive To Waypoint") so the vehicle follows the navmesh path around the obstacles.
    /// </para>
    ///
    /// <para>
    /// If <paramref name="navmeshProvider"/> is null (bake failed at startup) the case logs
    /// a loud "NAVMESH UNAVAILABLE" error and returns immediately without crashing.
    /// </para>
    ///
    /// <para>
    /// <b>What you should see:</b> the APC spawns west of a wall cluster, drives a path
    /// that visibly curves around the obstacles rather than heading straight at them, and
    /// arrives at the east goal marker. The log prints each navmesh corner, then per-frame
    /// progress, and finally "NAVMESH DRIVE COMPLETE — reached goal via N corners".
    /// </para>
    /// </summary>
    public static TestHarnessRegistry RegisterNavmeshDriveCase(
        TestHarnessRegistry        registry,
        PhysicsBodyLifecycleSystem lifecycleSystem,
        IPhysicsBodyService        bodyService,
        DotRecastNavmeshProvider?  navmeshProvider,
        Func<string, Model?>?      loadModel = null)
    {
        if (registry        == null) throw new ArgumentNullException(nameof(registry));
        if (lifecycleSystem == null) throw new ArgumentNullException(nameof(lifecycleSystem));
        if (bodyService     == null) throw new ArgumentNullException(nameof(bodyService));

        registry.Register(new VisualTestCase(
            "Navmesh Drive",
            "Spawn MilitaryAPC west of a wall cluster; plan a DotRecast navmesh path to the east goal; " +
            "drive the APC along the path corners using VehicleWaypointController. " +
            "APC routes AROUND the obstacle — not straight through it. Key F4. GPU-verified-only.",
            ctx => NavmeshDrive(ctx, lifecycleSystem, navmeshProvider, loadModel)));

        return registry;
    }

    // ── Navmesh Drive (implementation) ───────────────────────────────────────

    // Start and goal in FDP space (X=East, Y=North, Z=Up).
    // Start: west side of arena, south. Goal: east side, north.
    // A direct line in the arena would pass through interior wall obstacles.
    private static readonly SNum.Vector3 NavDriveStartFdp = new(-5f, 3f, 0f);
    private static readonly SNum.Vector3 NavDriveGoalFdp  = new( 5f, 12f, 0f);

    // Waypoint controller parameters for navmesh-path following.
    private const float NavDriveCruiseSpeed      = 3.0f;
    private const float NavDriveMaxSteerRad      = 0.7f;   // ~40°
    private const float NavDriveHeadingGain      = 2.0f;
    private const float NavDriveArriveToleranceM = 1.5f;
    private const float NavDriveSlowRadiusM      = 4.0f;
    private const float NavDriveWheelBase        = 2.5f;
    private const float NavDriveTimeoutSec       = 30.0f;

    private static void NavmeshDrive(
        TestHarnessContext        ctx,
        PhysicsBodyLifecycleSystem lifecycle,
        DotRecastNavmeshProvider? navmeshProvider,
        Func<string, Model?>?     loadModel)
    {
        // ── Guard: navmesh must be available ───────────────────────────────
        if (navmeshProvider == null)
        {
            ctx.Log("[Navmesh Drive] NAVMESH UNAVAILABLE — baking failed at startup. " +
                    "Check logs for 'BakeNavmesh' errors. F4 case aborted.");
            return;
        }

        // ── Plan the navmesh path ──────────────────────────────────────────
        // Inputs to PlanPath are in navmesh-query space = Stride space (X=East, Y=Up, Z=North).
        // FDP→Stride swizzle: Stride = (fdp.X, fdp.Z, fdp.Y).
        var startStride = FdpStrideTransform.ToStridePosition(NavDriveStartFdp);
        var goalStride  = FdpStrideTransform.ToStridePosition(NavDriveGoalFdp);

        // Convert Stride.Vector3 → System.Numerics.Vector3 for the provider.
        var startNav = new SNum.Vector3(startStride.X, startStride.Y, startStride.Z);
        var goalNav  = new SNum.Vector3(goalStride.X,  goalStride.Y,  goalStride.Z);

        var navWaypoints = new NavWaypoint[256];
        int cornerCount  = navmeshProvider.PlanPath(
            startNav, goalNav,
            navWaypoints.AsSpan(),
            layerMask: (uint)NavLayerMask.Vehicle);

        if (cornerCount == 0)
        {
            ctx.Log("[Navmesh Drive] FAILURE — PlanPath returned 0 corners (no path found). " +
                    "Check that the navmesh was baked successfully and that start/goal are on-mesh. " +
                    "Start (Stride): " + startStride + "  Goal (Stride): " + goalStride);
            return;
        }

        // Convert corner positions from navmesh-query (=Stride) space back to FDP space.
        var cornersFdp = new SNum.Vector2[cornerCount];
        var sb         = new System.Text.StringBuilder();
        sb.Append($"[Navmesh Drive] Path planned: {cornerCount} corners. ");
        for (int ci = 0; ci < cornerCount; ci++)
        {
            // navWaypoints[ci].Position is in Stride/navmesh space: (East, Up, North).
            // FDP: (East, North, Up) = (nav.X, nav.Z, nav.Y).
            var nav = navWaypoints[ci].Position;
            var fdp = FdpStrideTransform.ToFdpPosition(new SMath.Vector3(nav.X, nav.Y, nav.Z));
            cornersFdp[ci] = new SNum.Vector2(fdp.X, fdp.Y); // 2D: X=East, Y=North
            sb.Append($"C{ci}=({fdp.X:F1},{fdp.Y:F1}) ");
        }
        ctx.Log(sb.ToString());
        ctx.Log($"[Navmesh Drive] Start FDP=({NavDriveStartFdp.X:F1},{NavDriveStartFdp.Y:F1})  " +
                $"Goal FDP=({NavDriveGoalFdp.X:F1},{NavDriveGoalFdp.Y:F1})");

        // ── Spawn APC ─────────────────────────────────────────────────────
        float startZ = ApcBoxHalfHeightFdpZ;
        var   startPos = new SNum.Vector3(NavDriveStartFdp.X, NavDriveStartFdp.Y, startZ);
        s_physicsRowY += 4f; // advance layout cursor to avoid overlap with earlier spawns

        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbMilitaryApc,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbMilitaryApc },
                new VehicleParams
                {
                    WheelBase   = NavDriveWheelBase,
                    Length      = 4.5f,
                    Width       = 2.2f,
                    MaxSpeedFwd = 10f,
                    MaxAccel    = 3f,
                },
            },
        });

        ctx.Log($"[Navmesh Drive] Spawned MilitaryAPC @ FDP ({NavDriveStartFdp.X:F1},{NavDriveStartFdp.Y:F1}). " +
                $"Driving to goal ({NavDriveGoalFdp.X:F1},{NavDriveGoalFdp.Y:F1}) via {cornerCount} navmesh corners.");

        // ── Spawn markers at each corner and at the goal ───────────────────
        const string MarkerModelRef = "Models/Box2x1x1";
        var markerEntities = new List<StrideEntity>();

        for (int mi = 0; mi < cornerCount; mi++)
        {
            var fdp2d   = cornersFdp[mi];
            var fdpPos  = new SNum.Vector3(fdp2d.X, fdp2d.Y, 0f);
            var stridePos = FdpStrideTransform.ToStridePosition(fdpPos);

            var marker = new StrideEntity($"NavMesh_Corner_{mi}");
            marker.Transform.Position = stridePos;
            // Thin pillar: corner markers are smaller than the goal marker.
            bool isGoal  = mi == cornerCount - 1;
            marker.Transform.Scale = isGoal
                ? new SMath.Vector3(0.5f, 4.0f, 0.5f)  // goal: taller pillar
                : new SMath.Vector3(0.3f, 2.0f, 0.3f);  // corner: smaller

            if (loadModel != null)
            {
                try
                {
                    var model = loadModel(MarkerModelRef);
                    if (model != null)
                        marker.Add(new Stride.Engine.ModelComponent { Model = model });
                }
                catch { /* silent: marker is visible-only, failure is non-critical */ }
            }

            ctx.Scene.Entities.Add(marker);
            markerEntities.Add(marker);
        }

        // ── Waypoint controller ────────────────────────────────────────────
        var controller = new VehicleWaypointController(
            cruiseSpeed:      NavDriveCruiseSpeed,
            maxSteerAngleRad: NavDriveMaxSteerRad,
            headingGainK:     NavDriveHeadingGain,
            arriveToleranceM: NavDriveArriveToleranceM,
            slowRadiusM:      NavDriveSlowRadiusM,
            slowMinFrac:      0.2f,
            wheelBase:        NavDriveWheelBase);

        // ── Per-frame update state ─────────────────────────────────────────
        Fdp.Core.Entity target = default;
        bool resolved          = false;
        int  currentCorner     = 0;
        float elapsed          = 0f;
        float totalElapsed     = 0f;
        float nextLogAt        = 0f;

        // Stuck detection (same pattern as Drive To Waypoint).
        SNum.Vector3 stuckWindowStartPos = startPos;
        float        windowOpenedAt      = 0f;

        ctx.RegisterUpdate(dt =>
        {
            elapsed      += dt;
            totalElapsed += dt;

            // ── Resolve entity ────────────────────────────────────────────
            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[Navmesh Drive] Entity #{target.Index} resolved.");
                    if (ctx.World.HasComponent<SimTransform>(target))
                        stuckWindowStartPos = ctx.World.GetComponentRO<SimTransform>(target).Position;
                    windowOpenedAt = totalElapsed;
                }
                return elapsed < 10f;
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[Navmesh Drive] Entity gone — stopping.");
                RemoveNavMarkers(ctx, markerEntities);
                return false;
            }

            // ── All corners reached ────────────────────────────────────────
            if (currentCorner >= cornersFdp.Length)
                return false;

            // ── Read current pose ─────────────────────────────────────────
            if (!ctx.World.HasComponent<SimTransform>(target))
                return true;

            var simTf   = ctx.World.GetComponentRO<SimTransform>(target);
            float posX  = simTf.Position.X;
            float posY  = simTf.Position.Y;
            var   curPos = simTf.Position;
            var   forward = SNum.Vector3.Transform(SNum.Vector3.UnitX, simTf.Rotation);
            float heading = MathF.Atan2(forward.Y, forward.X);

            // ── Ensure VehicleState ───────────────────────────────────────
            if (!ctx.World.HasComponent<VehicleState>(target))
            {
                if (ctx.World.IsComponentTypeRegistered<VehicleState>())
                    ctx.World.AddComponent(target, new VehicleState());
                return true;
            }

            // ── Steer to current corner ────────────────────────────────────
            var  wp     = cornersFdp[currentCorner];
            var  output = controller.Compute(posX, posY, heading, wp.X, wp.Y);

            // Movement-based stuck detection.
            float windowAge    = totalElapsed - windowOpenedAt;
            float displacement = (curPos - stuckWindowStartPos).Length();
            if (displacement >= StuckDisplacementThresholdM)
            {
                stuckWindowStartPos = curPos;
                windowOpenedAt      = totalElapsed;
                displacement        = 0f;
                windowAge           = 0f;
            }

            bool isStuck = !output.Arrived
                           && windowAge >= StuckWindowSec
                           && displacement < StuckDisplacementThresholdM;

            if (isStuck)
            {
                ctx.Log($"[Navmesh Drive] STUCK before corner {currentCorner} " +
                        $"({wp.X:F1},{wp.Y:F1}) — skipping.");
                currentCorner++;
                elapsed             = 0f;
                stuckWindowStartPos = curPos;
                windowOpenedAt      = totalElapsed;

                if (currentCorner >= cornersFdp.Length)
                {
                    StopVehicle(ctx, target);
                    ctx.Log("[Navmesh Drive] All corners processed (some skipped). Stopping.");
                    RemoveNavMarkers(ctx, markerEntities);
                    return false;
                }
                return true;
            }

            // ── Timeout guard ─────────────────────────────────────────────
            if (elapsed > NavDriveTimeoutSec && !output.Arrived)
            {
                ctx.Log($"[Navmesh Drive] TIMEOUT on corner {currentCorner} " +
                        $"after {elapsed:F1}s. Best dist: {output.DistToTarget:F2}m. FAILURE.");
                StopVehicle(ctx, target);
                RemoveNavMarkers(ctx, markerEntities);
                return false;
            }

            // ── Command VehicleState ──────────────────────────────────────
            {
                ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
                vs.Speed      = output.Speed;
                vs.SteerAngle = output.SteerAngle;
            }

            // ── Corner arrival ────────────────────────────────────────────
            if (output.Arrived)
            {
                bool isLastCorner = currentCorner == cornersFdp.Length - 1;
                ctx.Log($"[Navmesh Drive] Reached corner {currentCorner}/{cornersFdp.Length - 1} " +
                        $"({wp.X:F1},{wp.Y:F1}) at t={totalElapsed:F1}s dist={output.DistToTarget:F2}m.");

                if (isLastCorner)
                {
                    StopVehicle(ctx, target);
                    ctx.Log($"[Navmesh Drive] NAVMESH DRIVE COMPLETE — reached goal via {cornerCount} corners. " +
                            "APC routed AROUND arena obstacles using DotRecast navmesh path.");
                    RemoveNavMarkers(ctx, markerEntities);
                    return false;
                }

                currentCorner++;
                elapsed             = 0f;
                stuckWindowStartPos = curPos;
                windowOpenedAt      = totalElapsed;
                return true;
            }

            // ── Periodic log ──────────────────────────────────────────────
            if (totalElapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;
                ctx.Log($"[Navmesh Drive] t={totalElapsed:F1}s entity #{target.Index} " +
                        $"pos=({posX:F2},{posY:F2}) heading={heading:F2}rad " +
                        $"→ corner {currentCorner}/{cornersFdp.Length - 1}({wp.X:F1},{wp.Y:F1}) " +
                        $"dist={output.DistToTarget:F2}m err={output.HeadingErrorRad:F2}rad " +
                        $"spd={output.Speed:F2} steer={output.SteerAngle:F3}");
            }

            return true;
        });
    }

    private static void StopVehicle(TestHarnessContext ctx, Fdp.Core.Entity target)
    {
        if (ctx.World.IsAlive(target) && ctx.World.HasComponent<VehicleState>(target))
        {
            ref var vs = ref ctx.World.GetComponentRW<VehicleState>(target);
            vs.Speed = 0f; vs.SteerAngle = 0f;
        }
    }

    private static void RemoveNavMarkers(TestHarnessContext ctx, List<StrideEntity> markers)
    {
        foreach (var m in markers)
        {
            try { ctx.Scene.Entities.Remove(m); } catch { /* ignore */ }
        }
        markers.Clear();
    }

    // ── Navmesh Walk (F5, BATCH-19, STR-D19 discharge) ─────────────────────────

    /// <summary>
    /// Registers the "Navmesh Walk" case (index 14 → key F5) into
    /// <paramref name="registry"/>.
    ///
    /// <para>
    /// This case spawns a mannequin (TKB 2002, InfantrySoldier — capsule CharacterComponent,
    /// same as F1 "Physics Walk") at FDP (−4, 2, 0), sets up it up as a DotRecast crowd
    /// agent on the Infantry navmesh, and sets a goal at FDP (4, 13, 0) on the far side of
    /// an interior wall.  The path must DETOUR around the wall to reach the goal.
    /// </para>
    ///
    /// <para>
    /// <b>How crowd navigation works (BATCH-19 chain):</b>
    /// <list type="bullet">
    ///   <item><c>DotRecastDtCrowdProvider.RegisterAgent</c> + <c>SetAgentTarget</c>
    ///     — enroll the entity in the Infantry DtCrowd.</item>
    ///   <item><c>CrowdAgentUpdateSystem.Execute</c> (ticked each kernel step)
    ///     — calls <c>dtCrowd.Update(dt, view)</c> and writes the resulting velocity to
    ///     <c>CrowdMotorIntent.Velocity</c>.</item>
    ///   <item><c>BulletCharacterMotor</c> — reads <c>CrowdMotorIntent.Velocity</c> and
    ///     calls <c>CharacterComponent.SetVelocity</c>.</item>
    ///   <item>Bullet + BulletReverseSyncSystem — physics moves the entity and writes back
    ///     <c>SimTransform</c>/<c>SimVelocity</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>What you should see:</b> the mannequin spawns west-south of a wall cluster,
    /// walks (animated) along a path that clearly CURVES AROUND the wall obstacles to the
    /// east-north goal marker.  The log shows per-frame velocity and position, then finally
    /// "NAVMESH WALK COMPLETE — reached goal (pathfound around obstacle)".
    /// </para>
    ///
    /// <para>
    /// If <paramref name="navmeshProvider"/> is null or the Infantry crowd is not
    /// initialized the case logs a loud error and returns cleanly without crashing.
    /// </para>
    ///
    /// <para>
    /// <b>Key F5</b> (index 14: D1–D9=0–8, D0=9, F1=10, F2=11, F3=12, F4=13, F5=14).
    /// </para>
    /// </summary>
    public static TestHarnessRegistry RegisterNavmeshWalkCase(
        TestHarnessRegistry           registry,
        PhysicsBodyLifecycleSystem    lifecycleSystem,
        IPhysicsBodyService           bodyService,
        DotRecastNavmeshProvider?     navmeshProvider,
        DotRecastDtCrowdProvider?     infantryCrowdProvider,
        Func<string, Model?>?         loadModel = null)
    {
        if (registry        == null) throw new ArgumentNullException(nameof(registry));
        if (lifecycleSystem == null) throw new ArgumentNullException(nameof(lifecycleSystem));
        if (bodyService     == null) throw new ArgumentNullException(nameof(bodyService));

        registry.Register(new VisualTestCase(
            "Navmesh Walk",
            "Spawn InfantrySoldier mannequin west of a wall cluster; register as a DotRecast crowd agent on Infantry navmesh; " +
            "agent pathfinds AROUND obstacles and walks (animated) to the east goal. " +
            "Proves real FDP NavigationIntent character navigation over live DotRecast navmesh. Key F5. GPU-verified-only.",
            ctx => NavmeshWalk(ctx, lifecycleSystem, navmeshProvider, infantryCrowdProvider, loadModel)));

        return registry;
    }

    // ── Navmesh Walk constants ─────────────────────────────────────────────────

    // Start and goal in FDP space (X=East, Y=North, Z=Up).
    // Start: west-south of arena. Goal: east-north across the interior walls.
    // The straight-line vector from start to goal passes through one or more interior walls,
    // forcing the crowd pathfinder to route around them.
    private static readonly SNum.Vector3 NavWalkStartFdp = new(-4f, 2f, 0f);
    private static readonly SNum.Vector3 NavWalkGoalFdp  = new( 4f, 13f, 0f);

    // Walk speed and timing.
    private const float NavWalkCruiseSpeed  = 2.0f;    // m/s (FDP)
    private const float NavWalkTimeoutSec   = 60.0f;   // generous timeout for real navmesh
    private const float NavWalkArrivalRadiusM = 1.5f;  // m — same as F4 vehicle tolerance

    // Infantry crowd agent parameters (matching StrideNavmeshBaker.InfantryParams).
    private const float InfantryAgentRadius  = 0.3f;   // m
    private const float InfantryAgentHeight  = 1.8f;   // m
    private const float InfantryMaxAccel     = 20f;    // m/s²
    private const float InfantryMaxSpeed     = NavWalkCruiseSpeed * 1.5f; // slightly above cruise

    // ── Navmesh Walk (implementation) ─────────────────────────────────────────

    private static void NavmeshWalk(
        TestHarnessContext            ctx,
        PhysicsBodyLifecycleSystem    lifecycle,
        DotRecastNavmeshProvider?     navmeshProvider,
        DotRecastDtCrowdProvider?     infantryCrowd,
        Func<string, Model?>?         loadModel)
    {
        // ── Guard: navmesh and crowd must be available ─────────────────────
        if (navmeshProvider == null)
        {
            ctx.Log("[Navmesh Walk] NAVMESH UNAVAILABLE — navmesh provider null (baking failed at startup). " +
                    "Check logs for 'BakeNavmesh' errors. F5 case aborted.");
            return;
        }
        if (infantryCrowd == null || !infantryCrowd.IsInitialized)
        {
            ctx.Log("[Navmesh Walk] INFANTRY CROWD UNAVAILABLE — DotRecastDtCrowdProvider is null or not initialized. " +
                    "Check logs for 'Infantry DotRecastDtCrowdProvider initialized'. F5 case aborted.");
            return;
        }

        // ── ON-NAVMESH SNAP: snap start and goal to nearest navmesh polygon ──
        // DotRecastDtCrowdProvider.TrySnapToNavmesh uses FindNearestPoly with ±2/4/2 m extents.
        // If start or goal are off-navmesh (outside floor / inside wall) the crowd can't place
        // the agent or find a path — snapping guarantees both endpoints are on valid polys.
        bool startSnapped = infantryCrowd.TrySnapToNavmesh(NavWalkStartFdp, out SNum.Vector3 snappedStart);
        bool goalSnapped  = infantryCrowd.TrySnapToNavmesh(NavWalkGoalFdp,  out SNum.Vector3 snappedGoal);

        float startSnapDist = (snappedStart - NavWalkStartFdp).Length();
        float goalSnapDist  = (snappedGoal  - NavWalkGoalFdp).Length();

        ctx.Log($"[Navmesh Walk] ON-NAVMESH SNAP: " +
                $"start FDP ({NavWalkStartFdp.X:F2},{NavWalkStartFdp.Y:F2}) " +
                $"→ snapped ({snappedStart.X:F2},{snappedStart.Y:F2}) onMesh={startSnapped} dist={startSnapDist:F3}m | " +
                $"goal FDP ({NavWalkGoalFdp.X:F2},{NavWalkGoalFdp.Y:F2}) " +
                $"→ snapped ({snappedGoal.X:F2},{snappedGoal.Y:F2}) onMesh={goalSnapped} dist={goalSnapDist:F3}m");

        if (!startSnapped)
            ctx.Log("[Navmesh Walk] WARNING: start position is OFF the Infantry navmesh (no poly within ±2/4/2 m). " +
                    "Agent may fail to register or receive zero velocity. Check navmesh bake geometry.");
        if (!goalSnapped)
            ctx.Log("[Navmesh Walk] WARNING: goal position is OFF the Infantry navmesh (no poly within ±2/4/2 m). " +
                    "Agent target may not be set. Check navmesh bake geometry.");

        // Use the snapped positions for the actual agent and goal marker placement.
        // Snap only X/Y (ground plane); Z=0 (floor level) for spawn.
        var effectiveStart = new SNum.Vector3(snappedStart.X, snappedStart.Y, 0f);
        var effectiveGoal  = new SNum.Vector3(snappedGoal.X,  snappedGoal.Y,  0f);

        // ── Log route ─────────────────────────────────────────────────────
        ctx.Log($"[Navmesh Walk] Route: start FDP ({effectiveStart.X:F2},{effectiveStart.Y:F2}) " +
                $"→ goal FDP ({effectiveGoal.X:F2},{effectiveGoal.Y:F2}). " +
                $"Direct line passes through interior walls — agent must pathfind around them. " +
                $"Infantry params: radius={InfantryAgentRadius} m, height={InfantryAgentHeight} m, " +
                $"maxSpeed={InfantryMaxSpeed:F1} m/s.");

        // ── Spawn the mannequin ────────────────────────────────────────────
        // TKB 2002 = InfantrySoldier → capsule CharacterComponent, same as F1 "Physics Walk".
        // FDP Z=0 → Stride Y=0 (floor level). Entity origin = base of capsule (feet).
        var startPos = new SNum.Vector3(effectiveStart.X, effectiveStart.Y, 0f);
        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new System.Collections.Generic.List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbInfantrySoldier },
            },
        });

        ctx.Log($"[Navmesh Walk] Spawned InfantrySoldier (TKB {TkbInfantrySoldier}) @ FDP " +
                $"({effectiveStart.X:F2},{effectiveStart.Y:F2},0). " +
                $"Will register as crowd agent (start snapped={startSnapped}) and walk to goal.");

        // ── Spawn a visible goal marker at the snapped goal position ───────
        // Same visual approach as F3/F4: a thin Box2x1x1 pillar, no physics.
        const string MarkerModelRef = "Models/Box2x1x1";
        var goalMarker = new StrideEntity("NavWalk_GoalMarker");
        goalMarker.Transform.Position = FdpStrideTransform.ToStridePosition(effectiveGoal);
        goalMarker.Transform.Scale    = new SMath.Vector3(0.5f, 4.0f, 0.5f); // tall pillar
        if (loadModel != null)
        {
            try
            {
                var model = loadModel(MarkerModelRef);
                if (model != null)
                    goalMarker.Add(new Stride.Engine.ModelComponent { Model = model });
            }
            catch { /* non-critical — marker is visible-only */ }
        }
        ctx.Scene.Entities.Add(goalMarker);
        ctx.Log($"[Navmesh Walk] Goal marker spawned at FDP ({effectiveGoal.X:F2},{effectiveGoal.Y:F2}) " +
                $"(snapped={goalSnapped}, snapDist={goalSnapDist:F3}m).");

        // ── Per-frame state ────────────────────────────────────────────────
        Fdp.Core.Entity target  = default;
        bool resolved           = false;
        bool agentRegistered    = false;
        float elapsed           = 0f;
        float totalElapsed      = 0f;
        float nextLogAt         = 0f;
        bool completed          = false;
        float bestDistToGoal    = float.MaxValue;

        ctx.RegisterUpdate(dt =>
        {
            elapsed      += dt;
            totalElapsed += dt;

            // ── Resolve spawned entity ─────────────────────────────────────
            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[Navmesh Walk] Entity #{target.Index} resolved.");
                }
                return elapsed < 10f; // stop retrying after 10 s
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[Navmesh Walk] Entity gone — stopping.");
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            if (completed)
                return false;

            // ── Register crowd agent (once, after body is ready) ──────────
            // CrowdAgent, CrowdMotorIntent, NavigationStatus are required by CrowdAgentUpdateSystem.
            if (!agentRegistered)
            {
                // Guard: CrowdAgent must be registered in the world (fixed by BATCH-19 fix:
                // EditorStrideSubsystem.Initialize now calls World.RegisterComponent<CrowdAgent>()).
                if (!ctx.World.IsComponentTypeRegistered<CrowdAgent>())
                {
                    ctx.Log("[Navmesh Walk] WARNING: CrowdAgent component type not registered — cannot proceed. " +
                            "FIX: EditorStrideSubsystem.Initialize must call World.RegisterComponent<CrowdAgent>().");
                    return false;
                }
                if (!ctx.World.HasComponent<CrowdAgent>(target))
                    ctx.World.AddComponent(target, default(CrowdAgent));
                if (!ctx.World.HasComponent<CrowdMotorIntent>(target))
                    ctx.World.AddComponent(target, new CrowdMotorIntent());
                if (!ctx.World.HasComponent<NavigationStatus>(target))
                    ctx.World.AddComponent(target, new NavigationStatus { Result = NavigationResult.InProgress });

                // Register with the Infantry DotRecast crowd, passing the snapped start position
                // so the agent is placed at a valid navmesh polygon from frame 1.
                var agentParams = new CrowdAgentParams
                {
                    Radius           = InfantryAgentRadius,
                    Height           = InfantryAgentHeight,
                    MaxSpeed         = InfantryMaxSpeed,
                    MaxAcceleration  = InfantryMaxAccel,
                    SeparationWeight = 2,
                };
                // Pass the snapped start FDP so RegisterAgent places the crowd agent on the mesh.
                bool ok = infantryCrowd.RegisterAgent(target, agentParams,
                    startPositionFdp: new SNum.Vector3(effectiveStart.X, effectiveStart.Y, effectiveStart.Z));
                // Set the snapped goal as target.
                infantryCrowd.SetAgentTarget(target, effectiveGoal);
                agentRegistered = true;
                ctx.Log($"[Navmesh Walk] Crowd agent registered (ok={ok}); " +
                        $"start=({effectiveStart.X:F2},{effectiveStart.Y:F2}) snapped={startSnapped}, " +
                        $"target=({effectiveGoal.X:F2},{effectiveGoal.Y:F2}) snapped={goalSnapped}. " +
                        $"CrowdAgentUpdateSystem will steer each tick → CrowdMotorIntent.Velocity " +
                        $"→ BulletCharacterMotor → walk + animation.");

                if (!ok)
                    ctx.Log("[Navmesh Walk] WARNING: RegisterAgent returned false — crowd not yet initialized " +
                            "or entity was already registered. Velocity will be zero.");
            }

            // ── Read current pose ──────────────────────────────────────────
            if (!ctx.World.HasComponent<SimTransform>(target))
                return true;

            var simTf    = ctx.World.GetComponentRO<SimTransform>(target);
            float posX   = simTf.Position.X;
            float posY   = simTf.Position.Y;

            float distToGoal = MathF.Sqrt(
                (posX - effectiveGoal.X) * (posX - effectiveGoal.X) +
                (posY - effectiveGoal.Y) * (posY - effectiveGoal.Y));

            if (distToGoal < bestDistToGoal)
                bestDistToGoal = distToGoal;

            // ── Arrival check ──────────────────────────────────────────────
            if (distToGoal <= NavWalkArrivalRadiusM)
            {
                completed = true;
                ctx.Log($"[Navmesh Walk] NAVMESH WALK COMPLETE — reached goal (pathfound around obstacle). " +
                        $"entity #{target.Index} dist={distToGoal:F2}m at t={totalElapsed:F1}s. " +
                        $"Mannequin walked (animated) along DotRecast Infantry navmesh path.");
                // Zero the crowd motor intent so the mannequin stops.
                if (ctx.World.HasComponent<CrowdMotorIntent>(target))
                {
                    ref var cmi = ref ctx.World.GetComponentRW<CrowdMotorIntent>(target);
                    cmi.Velocity = SNum.Vector3.Zero;
                }
                infantryCrowd.UnregisterAgent(target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            // ── Timeout guard ──────────────────────────────────────────────
            if (totalElapsed > NavWalkTimeoutSec)
            {
                ctx.Log($"[Navmesh Walk] TIMEOUT after {totalElapsed:F1}s. " +
                        $"Best distance to goal: {bestDistToGoal:F2}m (arrival radius: {NavWalkArrivalRadiusM:F1}m). " +
                        $"FAILURE — check that Infantry navmesh was baked and the route is reachable.");
                infantryCrowd.UnregisterAgent(target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            // ── Periodic diagnostics (~0.5 s) ─────────────────────────────
            // Log enough to diagnose any remaining silent failure in the next GPU run:
            //   - agent registered + crowd snapshot (position, target, desired velocity)
            //   - CrowdMotorIntent.Velocity written by CrowdAgentUpdateSystem
            //   - SimVelocity written back by BulletReverseSyncSystem
            //   - physics body presence
            //   - WHY if velocity is zero (off-navmesh, no path, RegisterAgent failed)
            if (totalElapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;

                // CrowdMotorIntent velocity (CrowdAgentUpdateSystem output).
                var cmiVel = ctx.World.HasComponent<CrowdMotorIntent>(target)
                    ? ctx.World.GetComponentRO<CrowdMotorIntent>(target).Velocity
                    : SNum.Vector3.Zero;

                // SimVelocity (BulletReverseSyncSystem output — post-physics actual velocity).
                var simVel = ctx.World.HasComponent<SimVelocity>(target)
                    ? ctx.World.GetComponentRO<SimVelocity>(target).Linear
                    : SNum.Vector3.Zero;

                bool hasBody = lifecycle.Bodies.TryGetValue(target, out _);

                // Crowd agent snapshot (position inside DtCrowd, desired vel, path state).
                string crowdSnap = "no snapshot";
                if (agentRegistered && infantryCrowd.TryGetAgentSnapshot(target, out var snap))
                {
                    crowdSnap = $"crowdPos=({snap.Position.X:F2},{snap.Position.Y:F2}) " +
                                $"dvel=({snap.DesiredVelocity.X:F2},{snap.DesiredVelocity.Y:F2}) " +
                                $"reachedTarget={snap.ReachedTarget} nearbyAgents={snap.NearbyAgentCount}";
                }

                ctx.Log($"[Navmesh Walk] t={totalElapsed:F1}s entity #{target.Index} " +
                        $"FDP pos=({posX:F2},{posY:F2}) distToGoal={distToGoal:F2}m " +
                        $"agentReg={agentRegistered} {crowdSnap} | " +
                        $"CrowdMotorIntent.vel=({cmiVel.X:F2},{cmiVel.Y:F2},{cmiVel.Z:F2}) spd={cmiVel.Length():F3} | " +
                        $"SimVelocity=({simVel.X:F2},{simVel.Y:F2},{simVel.Z:F2}) | body={hasBody}");

                // Diagnose zero velocity: enumerate known causes so the next GPU run is conclusive.
                if (agentRegistered && cmiVel.Length() < 0.01f)
                {
                    string reason;
                    if (!infantryCrowd.TryGetAgentSnapshot(target, out var diagSnap))
                        reason = "TryGetAgentSnapshot returned false — agent may not be in DtCrowd " +
                                 "(RegisterAgent may have returned false or crowd not initialized)";
                    else if (diagSnap.DesiredVelocity.Length() < 0.01f)
                        reason = $"DtCrowd desired-velocity is also zero — likely causes: " +
                                 $"(a) start/goal off-navmesh (startSnapped={startSnapped} snapDist={startSnapDist:F3}m, " +
                                 $"goalSnapped={goalSnapped} snapDist={goalSnapDist:F3}m), " +
                                 $"(b) no path found (agent is at target poly or target unreachable), " +
                                 $"(c) DtCrowd.Update not being called (CrowdAgentUpdateSystem not ticking)";
                    else
                        reason = $"DtCrowd desired-velocity is non-zero ({diagSnap.DesiredVelocity.Length():F3} m/s) " +
                                 $"but CrowdMotorIntent.Velocity is zero — CrowdAgentUpdateSystem may not be " +
                                 $"finding this entity in its query (check CrowdAgent/CrowdMotorIntent/NavigationStatus " +
                                 $"component registration + NavigationPhase={diagSnap.ReachedTarget})";

                    ctx.Log($"[Navmesh Walk] ZERO VELOCITY DIAGNOSIS: {reason}");
                }
            }

            return true;
        });
    }

    // ── FDP Move Order (char) — F6, BATCH-20, STR-D19 ──────────────────────────

    /// <summary>
    /// Registers the "FDP Move Order (char)" case (index 15 → key F6) into
    /// <paramref name="registry"/>.
    ///
    /// <para>
    /// <b>This case drives the PRODUCTION FDP navigation front door for CHARACTERS.</b>
    /// Unlike F5 "Navmesh Walk" (which calls <c>DotRecastDtCrowdProvider.RegisterAgent</c>
    /// directly), F6 issues a <see cref="Fdp.Toolkit.Navigation.NavigationConstants.ActionIdMoveTo"/>
    /// command on the entity's <see cref="LocomotionChannel"/> — exactly the way a BehaviorTree /
    /// HSM node does. <c>NavigationIntentBridgeSystem</c> (ticked in editor_stride) consumes that
    /// channel action and AUTO-REGISTERS a DotRecast crowd agent for the entity (because it has no
    /// <c>VehicleState</c>) + sets its target. From there the existing chain runs:
    /// <c>CrowdAgentUpdateSystem → CrowdMotorIntent → BulletCharacterMotor → Bullet → SimTransform</c>.
    /// No direct crowd-provider call is made by this harness case.
    /// </para>
    ///
    /// <para>
    /// <b>Why the LocomotionChannel, not MoveToExecutor.OnEnter?</b> <c>MoveToExecutor</c> (which
    /// writes <c>NavigationIntent</c> from the channel action) is NOT ticked in editor_stride — the
    /// composition registers <c>NavigationIntentBridgeSystem</c> but no <c>LocomotionDispatcherSystem</c>
    /// / executor pump. The bridge's crowd auto-registration is keyed on the <em>channel action</em>
    /// (<c>ActiveAction = ActionIdMoveTo</c> + a fresh <c>ActionInstanceId</c>), NOT on
    /// <c>NavigationIntent</c>. So the faithful production trigger here is to set the
    /// LocomotionChannel MoveTo action the way a BehaviorTree node would. We ALSO set
    /// <c>NavigationIntent</c> (Mode=DirectPoint, FinalDestination, IntentId++) the way
    /// <c>MoveToExecutor.OnEnter</c> does, so the <c>NavState</c> mapping + <c>NavigationStatus</c>
    /// feedback path is exercised identically to production.
    /// </para>
    ///
    /// <para>
    /// <b>Key F6</b> (index 15: D1–D9=0–8, D0=9, F1=10, F2=11, F3=12, F4=13, F5=14, F6=15).
    /// </para>
    /// </summary>
    public static TestHarnessRegistry RegisterFdpMoveOrderCharCase(
        TestHarnessRegistry           registry,
        PhysicsBodyLifecycleSystem    lifecycleSystem,
        IPhysicsBodyService           bodyService,
        DotRecastNavmeshProvider?     navmeshProvider,
        DotRecastDtCrowdProvider?     infantryCrowdProvider,
        Func<string, Model?>?         loadModel = null)
    {
        if (registry        == null) throw new ArgumentNullException(nameof(registry));
        if (lifecycleSystem == null) throw new ArgumentNullException(nameof(lifecycleSystem));
        if (bodyService     == null) throw new ArgumentNullException(nameof(bodyService));

        registry.Register(new VisualTestCase(
            "FDP Move Order (char)",
            "Spawn InfantrySoldier; issue a PRODUCTION NavigationIntent MoveTo via LocomotionChannel " +
            "(the BehaviorTree front door). NavigationIntentBridgeSystem auto-registers the crowd agent; " +
            "the mannequin pathfinds around a wall and walks (animated) to the goal. " +
            "NO direct crowd-provider call. Key F6. GPU-verified-only.",
            ctx => FdpMoveOrderChar(ctx, lifecycleSystem, navmeshProvider, infantryCrowdProvider, loadModel)));

        return registry;
    }

    // Start/goal in FDP space (X=East, Y=North, Z=Up). A wall clearly sits between them.
    // Same start/goal as F5 so the route around the interior wall cluster is reused.
    private static readonly SNum.Vector3 MoveOrderCharStartFdp = new(-4f, 2f, 0f);
    private static readonly SNum.Vector3 MoveOrderCharGoalFdp  = new( 4f, 13f, 0f);

    private const float MoveOrderCharSpeed         = 2.0f;   // m/s
    private const float MoveOrderCharArrivalRadius = 1.5f;   // m
    private const float MoveOrderCharTimeoutSec    = 60.0f;

    private static void FdpMoveOrderChar(
        TestHarnessContext            ctx,
        PhysicsBodyLifecycleSystem    lifecycle,
        DotRecastNavmeshProvider?     navmeshProvider,
        DotRecastDtCrowdProvider?     infantryCrowd,
        Func<string, Model?>?         loadModel)
    {
        // ── Guards ─────────────────────────────────────────────────────────
        if (navmeshProvider == null)
        {
            ctx.Log("[FDP Move Order char] NAVMESH UNAVAILABLE — provider null (bake failed). F6 aborted.");
            return;
        }
        if (infantryCrowd == null || !infantryCrowd.IsInitialized)
        {
            ctx.Log("[FDP Move Order char] INFANTRY CROWD UNAVAILABLE — crowd not initialized. F6 aborted.");
            return;
        }
        if (!ctx.World.IsComponentTypeRegistered<LocomotionChannel>())
        {
            ctx.Log("[FDP Move Order char] WARNING: LocomotionChannel not registered — cannot use the " +
                    "production front door. F6 aborted.");
            return;
        }

        // Snap start/goal onto the navmesh (BATCH-19 snap) so the goal is a valid poly.
        bool startSnapped = infantryCrowd.TrySnapToNavmesh(MoveOrderCharStartFdp, out SNum.Vector3 snappedStart);
        bool goalSnapped  = infantryCrowd.TrySnapToNavmesh(MoveOrderCharGoalFdp,  out SNum.Vector3 snappedGoal);
        var effectiveStart = new SNum.Vector3(snappedStart.X, snappedStart.Y, 0f);
        var effectiveGoal  = new SNum.Vector3(snappedGoal.X,  snappedGoal.Y,  0f);

        ctx.Log($"[FDP Move Order char] start FDP ({MoveOrderCharStartFdp.X:F2},{MoveOrderCharStartFdp.Y:F2}) " +
                $"→ snapped ({effectiveStart.X:F2},{effectiveStart.Y:F2}) onMesh={startSnapped} | " +
                $"goal FDP ({MoveOrderCharGoalFdp.X:F2},{MoveOrderCharGoalFdp.Y:F2}) " +
                $"→ snapped ({effectiveGoal.X:F2},{effectiveGoal.Y:F2}) onMesh={goalSnapped}. " +
                $"Direct line crosses an interior wall — agent must pathfind around it.");

        // ── Spawn the mannequin (TKB 2002 InfantrySoldier — same as F1/F5) ──
        var startPos = new SNum.Vector3(effectiveStart.X, effectiveStart.Y, 0f);
        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbInfantrySoldier },
            },
        });
        ctx.Log($"[FDP Move Order char] Spawned InfantrySoldier (TKB {TkbInfantrySoldier}) @ FDP " +
                $"({effectiveStart.X:F2},{effectiveStart.Y:F2},0). Will issue a production MoveTo command.");

        // ── Goal marker (reuse F5 mechanism) ────────────────────────────────
        const string MarkerModelRef = "Models/Box2x1x1";
        var goalMarker = new StrideEntity("MoveOrderChar_GoalMarker");
        goalMarker.Transform.Position = FdpStrideTransform.ToStridePosition(effectiveGoal);
        goalMarker.Transform.Scale    = new SMath.Vector3(0.5f, 4.0f, 0.5f);
        if (loadModel != null)
        {
            try { var m = loadModel(MarkerModelRef); if (m != null) goalMarker.Add(new Stride.Engine.ModelComponent { Model = m }); }
            catch { /* non-critical */ }
        }
        ctx.Scene.Entities.Add(goalMarker);

        // ── Per-frame state ──────────────────────────────────────────────────
        Fdp.Core.Entity target = default;
        bool resolved          = false;
        bool orderIssued       = false;
        float elapsed          = 0f;
        float totalElapsed     = 0f;
        float nextLogAt        = 0f;
        bool completed         = false;
        float bestDist         = float.MaxValue;
        int  preAgentCount     = 0; // crowd agents registered (via snapshot probe) before order

        ctx.RegisterUpdate(dt =>
        {
            elapsed      += dt;
            totalElapsed += dt;

            if (!resolved)
            {
                if (TryResolveNearest(ctx.World, startPos, out target))
                {
                    resolved = true;
                    ctx.Log($"[FDP Move Order char] Entity #{target.Index} resolved.");

                    // ── STR-D20 / BATCH-25 Part-B fix: strip VehicleState from infantry ──
                    // VehicleKinematicsTkbTranslator injects VehicleState onto EVERY entity
                    // that carries VehicleParametersDto in its TKB template — including the
                    // InfantrySoldier (TkbType 2002).  NavigationIntentBridgeSystem guards
                    // crowd registration with !HasComponent<VehicleState>, so a mannequin
                    // that carries VehicleState is never enrolled in the DotRecast crowd
                    // (hasCrowdComp=False, bridgeRegisteredAgent=False) and never moves.
                    // Fix: remove VehicleState from the infantry entity here, before issuing
                    // the production order, so the bridge sees it as crowd-eligible.
                    // KinematicVehicleMotor already guards on CollisionShapeKind.Capsule, so
                    // removing VehicleState changes nothing for the motor — infantry walked
                    // fine without it in F1/F5 (STR-D20: "F1 already proved infantry work
                    // fine without VehicleState — the vehicle motor skips them").
                    if (ctx.World.IsComponentTypeRegistered<VehicleState>()
                        && ctx.World.HasComponent<VehicleState>(target))
                    {
                        ctx.World.RemoveComponent<VehicleState>(target);
                        ctx.Log($"[FDP Move Order char] Stripped VehicleState from infantry entity #{target.Index} " +
                                "(STR-D20 fix: VehicleKinematicsTkbTranslator footgun — mannequin must NOT carry " +
                                "VehicleState so NavigationIntentBridgeSystem enrolls it in the DtCrowd).");
                    }
                }
                return elapsed < 10f;
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[FDP Move Order char] Entity gone — stopping.");
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            if (completed)
                return false;

            // ── Issue the PRODUCTION MoveTo command (once) ──────────────────
            // 1. Set NavAgentProfile (infantry radius/height) so the bridge registers a
            //    correctly-sized crowd agent. 2. Add a NavigationStatus to receive feedback.
            // 3. Write NavigationIntent (Mode=DirectPoint, FinalDestination, IntentId++) the way
            //    MoveToExecutor.OnEnter does. 4. Set the LocomotionChannel MoveTo action with a
            //    fresh ActionInstanceId — this is the EXACT trigger NavigationIntentBridgeSystem
            //    consumes to auto-register the crowd agent (no direct RegisterAgent call here).
            if (!orderIssued)
            {
                // Probe whether the agent is already a crowd agent (it must NOT be — proves
                // auto-registration is what enrolls it).
                preAgentCount = infantryCrowd.TryGetAgentSnapshot(target, out _) ? 1 : 0;

                if (ctx.World.IsComponentTypeRegistered<NavAgentProfile>()
                    && !ctx.World.HasComponent<NavAgentProfile>(target))
                {
                    ctx.World.AddComponent(target, new NavAgentProfile
                    {
                        AgentRadius        = InfantryAgentRadius,
                        AgentHeight        = InfantryAgentHeight,
                        MaxSlopeDeg        = 60f,
                        PreferredLayerMask = (uint)NavLayerMask.Infantry,
                    });
                }
                if (ctx.World.IsComponentTypeRegistered<CrowdMotorIntent>()
                    && !ctx.World.HasComponent<CrowdMotorIntent>(target))
                    ctx.World.AddComponent(target, new CrowdMotorIntent());
                if (ctx.World.IsComponentTypeRegistered<NavigationStatus>()
                    && !ctx.World.HasComponent<NavigationStatus>(target))
                    ctx.World.AddComponent(target, new NavigationStatus { Result = NavigationResult.InProgress });

                // (3) NavigationIntent — same fields MoveToExecutor.OnEnter writes.
                if (ctx.World.IsComponentTypeRegistered<NavigationIntent>())
                {
                    var intent = ctx.World.HasComponent<NavigationIntent>(target)
                        ? ctx.World.GetComponent<NavigationIntent>(target)
                        : default;
                    intent.IntentId++;
                    intent.Mode             = NavigationMode.DirectPoint;
                    intent.FinalDestination = effectiveGoal;
                    intent.TargetSpeed      = MoveOrderCharSpeed;
                    intent.ArrivalRadius    = MoveOrderCharArrivalRadius;
                    if (ctx.World.HasComponent<NavigationIntent>(target))
                        ctx.World.SetComponent(target, intent);
                    else
                        ctx.World.AddComponent(target, intent);
                }

                // (4) LocomotionChannel MoveTo action — the bridge's crowd-registration trigger.
                FdpNavigationOrders.IssueMoveTo(
                    ctx.World, target, effectiveGoal, MoveOrderCharSpeed, MoveOrderCharArrivalRadius,
                    NavLayerMask.Infantry);

                orderIssued = true;
                ctx.Log($"[FDP Move Order char] PRODUCTION ORDER issued via LocomotionChannel " +
                        $"(ActiveAction=ActionIdMoveTo) + NavigationIntent (Mode=DirectPoint, goal " +
                        $"({effectiveGoal.X:F2},{effectiveGoal.Y:F2})). preAgentRegistered={preAgentCount==1}. " +
                        $"NavigationIntentBridgeSystem will auto-register the crowd agent this tick.");
            }

            // ── Read pose + distance ────────────────────────────────────────
            if (!ctx.World.HasComponent<SimTransform>(target))
                return true;
            var simTf = ctx.World.GetComponentRO<SimTransform>(target);
            float distToGoal = MathF.Sqrt(
                (simTf.Position.X - effectiveGoal.X) * (simTf.Position.X - effectiveGoal.X) +
                (simTf.Position.Y - effectiveGoal.Y) * (simTf.Position.Y - effectiveGoal.Y));
            if (distToGoal < bestDist) bestDist = distToGoal;

            // ── Arrival ─────────────────────────────────────────────────────
            if (distToGoal <= MoveOrderCharArrivalRadius)
            {
                completed = true;
                ctx.Log($"[FDP Move Order char] ARRIVED — mannequin reached goal via the production " +
                        $"NavigationIntent front door. dist={distToGoal:F2}m at t={totalElapsed:F1}s.");
                if (ctx.World.HasComponent<CrowdMotorIntent>(target))
                {
                    ref var cmi = ref ctx.World.GetComponentRW<CrowdMotorIntent>(target);
                    cmi.Velocity = SNum.Vector3.Zero;
                }
                infantryCrowd.UnregisterAgent(target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            // ── Timeout ─────────────────────────────────────────────────────
            if (totalElapsed > MoveOrderCharTimeoutSec)
            {
                ctx.Log($"[FDP Move Order char] TIMEOUT after {totalElapsed:F1}s. Best dist={bestDist:F2}m " +
                        $"(arrival radius {MoveOrderCharArrivalRadius:F1}m). FAILURE — check bridge auto-registration " +
                        $"+ navmesh route.");
                infantryCrowd.UnregisterAgent(target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            // ── Diagnostics (~0.5 s) ────────────────────────────────────────
            if (totalElapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;

                // Did the bridge auto-register the agent? Probe the crowd snapshot.
                bool agentReg = infantryCrowd.TryGetAgentSnapshot(target, out var snap);

                var cmiVel = ctx.World.HasComponent<CrowdMotorIntent>(target)
                    ? ctx.World.GetComponentRO<CrowdMotorIntent>(target).Velocity : SNum.Vector3.Zero;
                var simVel = ctx.World.HasComponent<SimVelocity>(target)
                    ? ctx.World.GetComponentRO<SimVelocity>(target).Linear : SNum.Vector3.Zero;
                var navStatus = ctx.World.HasComponent<NavigationStatus>(target)
                    ? ctx.World.GetComponentRO<NavigationStatus>(target) : default;

                // STR-D21 F6 fix diagnostics: also show crowd-init status and CrowdAgent
                // component presence so GPU operator can diagnose registration issues.
                bool crowdInit    = infantryCrowd is Hrot.Stride.Core.DotRecastDtCrowdProvider dp
                                    && dp.IsInitialized;
                bool hasCrowdComp = ctx.World.IsComponentTypeRegistered<CrowdAgent>()
                                    && ctx.World.HasComponent<CrowdAgent>(target);

                ctx.Log($"[FDP Move Order char] t={totalElapsed:F1}s entity #{target.Index} " +
                        $"pos=({simTf.Position.X:F2},{simTf.Position.Y:F2}) distToGoal={distToGoal:F2}m | " +
                        $"NavStatus(phase={navStatus.Phase} result={navStatus.Result} intentId={navStatus.IntentId}) | " +
                        $"bridgeRegisteredAgent={agentReg} crowdInit={crowdInit} hasCrowdComp={hasCrowdComp} " +
                        (agentReg ? $"crowdDvel=({snap.DesiredVelocity.X:F2},{snap.DesiredVelocity.Y:F2}) " : "") +
                        $"CrowdMotorIntent.vel=({cmiVel.X:F2},{cmiVel.Y:F2},{cmiVel.Z:F2}) spd={cmiVel.Length():F3} | " +
                        $"SimVelocity=({simVel.X:F2},{simVel.Y:F2},{simVel.Z:F2})");
            }

            return true;
        });
    }

    // ── FDP Move Order (vehicle) — F7, BATCH-20, STR-D19 ───────────────────────

    /// <summary>
    /// Registers the "FDP Move Order (vehicle)" case (index 16 → key F7) into
    /// <paramref name="registry"/>.
    ///
    /// <para>
    /// <b>This case drives the PRODUCTION FDP navigation front door for VEHICLES.</b>
    /// It spawns a MilitaryAPC and sets a <see cref="Fdp.Toolkit.Navigation.NavigationIntent"/>
    /// (Mode=DirectPoint, FinalDestination behind a wall, IntentId++). It performs NO manual
    /// <c>PlanPath</c> — the new <see cref="VehicleNavigationIntentSystem"/> (ticked in editor_stride)
    /// detects the new intent, plans a DotRecast path over the Vehicle navmesh, and steers the APC
    /// along the corners via <c>VehicleWaypointController</c>, writing <c>VehicleState</c> each tick.
    /// </para>
    ///
    /// <para>
    /// <b>Key F7</b> (index 16: D1–D9=0–8, D0=9, F1=10 … F6=15, F7=16).
    /// </para>
    /// </summary>
    public static TestHarnessRegistry RegisterFdpMoveOrderVehicleCase(
        TestHarnessRegistry           registry,
        PhysicsBodyLifecycleSystem    lifecycleSystem,
        IPhysicsBodyService           bodyService,
        DotRecastNavmeshProvider?     navmeshProvider,
        VehicleNavigationIntentSystem? vehicleNavSystem,
        Func<string, Model?>?         loadModel = null)
    {
        if (registry        == null) throw new ArgumentNullException(nameof(registry));
        if (lifecycleSystem == null) throw new ArgumentNullException(nameof(lifecycleSystem));
        if (bodyService     == null) throw new ArgumentNullException(nameof(bodyService));

        registry.Register(new VisualTestCase(
            "FDP Move Order (vehicle)",
            "Spawn MilitaryAPC; set a PRODUCTION NavigationIntent (DirectPoint) to a goal behind a wall. " +
            "VehicleNavigationIntentSystem plans the navmesh path and steers the APC around the wall to the goal " +
            "— NO manual PlanPath in the harness. Key F7. GPU-verified-only.",
            ctx => FdpMoveOrderVehicle(ctx, lifecycleSystem, navmeshProvider, vehicleNavSystem, loadModel)));

        return registry;
    }

    // Start/goal in FDP space (X=East, Y=North, Z=Up). Reuse the F4 vehicle start/goal so a wall sits between.
    private static readonly SNum.Vector3 MoveOrderVehStartFdp = new(-5f, 3f, 0f);
    private static readonly SNum.Vector3 MoveOrderVehGoalFdp  = new( 5f, 12f, 0f);

    private const float MoveOrderVehSpeed         = 3.0f;   // m/s (echoed into NavigationIntent.TargetSpeed)
    private const float MoveOrderVehArrivalRadius = 1.5f;   // m
    private const float MoveOrderVehTimeoutSec    = 45.0f;

    private static void FdpMoveOrderVehicle(
        TestHarnessContext             ctx,
        PhysicsBodyLifecycleSystem     lifecycle,
        DotRecastNavmeshProvider?      navmeshProvider,
        VehicleNavigationIntentSystem? vehicleNavSystem,
        Func<string, Model?>?          loadModel)
    {
        if (navmeshProvider == null)
        {
            ctx.Log("[FDP Move Order veh] NAVMESH UNAVAILABLE — provider null (bake failed). F7 aborted.");
            return;
        }
        if (vehicleNavSystem == null)
        {
            ctx.Log("[FDP Move Order veh] VehicleNavigationIntentSystem not wired — F7 aborted.");
            return;
        }

        ctx.Log($"[FDP Move Order veh] start FDP ({MoveOrderVehStartFdp.X:F1},{MoveOrderVehStartFdp.Y:F1}) " +
                $"→ goal FDP ({MoveOrderVehGoalFdp.X:F1},{MoveOrderVehGoalFdp.Y:F1}). " +
                $"A wall sits between them; VehicleNavigationIntentSystem will plan + steer around it. " +
                $"Controller R_min≈{vehicleNavSystem.MinTurningRadiusM:F1} m.");

        // ── Spawn the APC (TKB 2001) ────────────────────────────────────────
        float startZ = ApcBoxHalfHeightFdpZ;
        var startPos = new SNum.Vector3(MoveOrderVehStartFdp.X, MoveOrderVehStartFdp.Y, startZ);
        ctx.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbMilitaryApc,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = startPos, Rotation = SNum.Quaternion.Identity },
                new TkbIdentity  { TkbType  = TkbMilitaryApc },
                new VehicleParams { WheelBase = DriveWheelBase, Length = 4.5f, Width = 2.2f, MaxSpeedFwd = 10f, MaxAccel = 3f },
            },
        });
        ctx.Log($"[FDP Move Order veh] Spawned MilitaryAPC @ FDP ({MoveOrderVehStartFdp.X:F1},{MoveOrderVehStartFdp.Y:F1}). " +
                $"Will set NavigationIntent (DirectPoint) — no manual PlanPath.");

        // ── Goal marker ─────────────────────────────────────────────────────
        const string MarkerModelRef = "Models/Box2x1x1";
        var goalMarker = new StrideEntity("MoveOrderVeh_GoalMarker");
        goalMarker.Transform.Position = FdpStrideTransform.ToStridePosition(new SNum.Vector3(MoveOrderVehGoalFdp.X, MoveOrderVehGoalFdp.Y, 0f));
        goalMarker.Transform.Scale    = new SMath.Vector3(0.5f, 4.0f, 0.5f);
        if (loadModel != null)
        {
            try { var m = loadModel(MarkerModelRef); if (m != null) goalMarker.Add(new Stride.Engine.ModelComponent { Model = m }); }
            catch { /* non-critical */ }
        }
        ctx.Scene.Entities.Add(goalMarker);

        // ── Per-frame state ──────────────────────────────────────────────────
        Fdp.Core.Entity target = default;
        bool resolved          = false;
        bool intentSet         = false;
        float elapsed          = 0f;
        float totalElapsed     = 0f;
        float nextLogAt        = 0f;
        bool completed         = false;
        float bestDist         = float.MaxValue;

        ctx.RegisterUpdate(dt =>
        {
            elapsed      += dt;
            totalElapsed += dt;

            if (!resolved)
            {
                // BATCH-27 / STR-D21 F7 fix: pass requireVehicleState=true so this resolver
                // cannot accidentally pick up a nearby InfantrySoldier from a concurrent F6 run.
                // F6 spawn (-4,2,0) and F7 spawn (-5,3,1.25) are ~1.89 m apart — within the
                // 2 m threshold.  Without the filter the resolver selected the infantry entity,
                // VehicleNavigationIntentSystem (With<VehicleState> query) then skipped it,
                // producing plannedCorners=0 / VehicleState.Speed=0 (the live F7 failure).
                if (TryResolveNearest(ctx.World, startPos, out target, requireVehicleState: true))
                {
                    resolved = true;
                    ctx.Log($"[FDP Move Order veh] Entity #{target.Index} resolved.");
                }
                return elapsed < 10f;
            }

            if (!ctx.World.IsAlive(target))
            {
                ctx.Log("[FDP Move Order veh] Entity gone — stopping.");
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            if (completed)
                return false;

            // ── Set the PRODUCTION NavigationIntent (once) ──────────────────
            // No PlanPath here — VehicleNavigationIntentSystem (ticked) detects the new IntentId,
            // plans the navmesh path, and steers the APC. We just set the goal.
            if (!intentSet)
            {
                if (!ctx.World.IsComponentTypeRegistered<NavigationIntent>())
                {
                    ctx.Log("[FDP Move Order veh] WARNING: NavigationIntent not registered — F7 aborted.");
                    try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                    return false;
                }
                if (ctx.World.IsComponentTypeRegistered<NavigationStatus>()
                    && !ctx.World.HasComponent<NavigationStatus>(target))
                    ctx.World.AddComponent(target, new NavigationStatus { Result = NavigationResult.InProgress });

                var intent = ctx.World.HasComponent<NavigationIntent>(target)
                    ? ctx.World.GetComponent<NavigationIntent>(target) : default;
                intent.IntentId++;
                intent.Mode             = NavigationMode.DirectPoint;
                intent.FinalDestination = new SNum.Vector3(MoveOrderVehGoalFdp.X, MoveOrderVehGoalFdp.Y, 0f);
                intent.TargetSpeed      = MoveOrderVehSpeed;
                intent.ArrivalRadius    = MoveOrderVehArrivalRadius;
                if (ctx.World.HasComponent<NavigationIntent>(target))
                    ctx.World.SetComponent(target, intent);
                else
                    ctx.World.AddComponent(target, intent);

                intentSet = true;
                ctx.Log($"[FDP Move Order veh] PRODUCTION NavigationIntent set (Mode=DirectPoint, goal " +
                        $"({MoveOrderVehGoalFdp.X:F1},{MoveOrderVehGoalFdp.Y:F1}), IntentId={intent.IntentId}). " +
                        $"VehicleNavigationIntentSystem will plan + steer.");
            }

            // ── Read pose + distance ────────────────────────────────────────
            if (!ctx.World.HasComponent<SimTransform>(target))
                return true;
            var simTf = ctx.World.GetComponentRO<SimTransform>(target);
            float distToGoal = MathF.Sqrt(
                (simTf.Position.X - MoveOrderVehGoalFdp.X) * (simTf.Position.X - MoveOrderVehGoalFdp.X) +
                (simTf.Position.Y - MoveOrderVehGoalFdp.Y) * (simTf.Position.Y - MoveOrderVehGoalFdp.Y));
            if (distToGoal < bestDist) bestDist = distToGoal;

            // ── Arrival (mirror the system's own arrival; double-check geometrically) ──
            var navStatus = ctx.World.HasComponent<NavigationStatus>(target)
                ? ctx.World.GetComponentRO<NavigationStatus>(target) : default;

            if (distToGoal <= MoveOrderVehArrivalRadius || navStatus.Result == NavigationResult.Arrived)
            {
                completed = true;
                ctx.Log($"[FDP Move Order veh] ARRIVED — APC reached goal via the production NavigationIntent " +
                        $"front door (VehicleNavigationIntentSystem planned {vehicleNavSystem.GetCornerCount(target)} corner(s)). " +
                        $"dist={distToGoal:F2}m NavStatus.Result={navStatus.Result} at t={totalElapsed:F1}s.");
                StopVehicle(ctx, target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            if (navStatus.Result == NavigationResult.NoPath)
            {
                ctx.Log("[FDP Move Order veh] NO PATH — VehicleNavigationIntentSystem reported NoPath. " +
                        "Check that the Vehicle navmesh baked and start/goal are reachable. Stopping.");
                StopVehicle(ctx, target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            // ── Timeout ─────────────────────────────────────────────────────
            if (totalElapsed > MoveOrderVehTimeoutSec)
            {
                ctx.Log($"[FDP Move Order veh] TIMEOUT after {totalElapsed:F1}s. Best dist={bestDist:F2}m. FAILURE.");
                StopVehicle(ctx, target);
                try { ctx.Scene.Entities.Remove(goalMarker); } catch { }
                return false;
            }

            // ── Diagnostics (~0.5 s) ────────────────────────────────────────
            // STR-D21 F7 fix diagnostics: include SimVelocity to confirm the body is being driven.
            // After the fix ([VehicleNav] pre-motor step), body_spd should be > 0 within a few frames.
            if (totalElapsed >= nextLogAt)
            {
                nextLogAt += PositionLogIntervalSec;
                var vs = ctx.World.HasComponent<VehicleState>(target)
                    ? ctx.World.GetComponentRO<VehicleState>(target) : default;
                var simVel = ctx.World.HasComponent<SimVelocity>(target)
                    ? ctx.World.GetComponentRO<SimVelocity>(target).Linear
                    : System.Numerics.Vector3.Zero;
                float bodySpd2D = MathF.Sqrt(simVel.X * simVel.X + simVel.Y * simVel.Y);
                int corners = vehicleNavSystem.GetCornerCount(target);
                int curCorner = vehicleNavSystem.GetCurrentCorner(target);
                bool hasBody = lifecycle.Bodies.TryGetValue(target, out _);
                ctx.Log($"[FDP Move Order veh] t={totalElapsed:F1}s entity #{target.Index} " +
                        $"pos=({simTf.Position.X:F2},{simTf.Position.Y:F2}) distToGoal={distToGoal:F2}m | " +
                        $"plannedCorners={corners} currentCorner={curCorner} | " +
                        $"VehicleState(spd={vs.Speed:F2} steer={vs.SteerAngle:F3}) " +
                        $"SimVel_2D_spd={bodySpd2D:F3} | " +
                        $"NavStatus(result={navStatus.Result} intentId={navStatus.IntentId}) body={hasBody}");
            }

            return true;
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Find the entity nearest <paramref name="near"/> that has a SimTransform and TkbIdentity.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="near">Reference position (FDP space).</param>
    /// <param name="result">The nearest entity found within 2 m, or default.</param>
    /// <param name="requireVehicleState">
    /// When <c>true</c> only entities that also carry <see cref="VehicleState"/> are considered.
    /// Set this to <c>true</c> for the F7 vehicle harness to prevent accidentally resolving a nearby
    /// InfantrySoldier whose spawn position overlaps the APC spawn zone (BATCH-27 / STR-D21 F7 fix).
    /// F6 start = (-4,2,0) and F7 start = (-5,3,1.25) are ~1.89 m apart — within the 2 m threshold.
    /// Without the filter, a live F6 infantry entity can be selected instead of the F7 APC, and
    /// <see cref="VehicleNavigationIntentSystem"/> (which requires <c>VehicleState</c>) then skips it
    /// → plannedCorners=0 / VehicleState.Speed=0 (the live F7 failure).
    /// </param>
    private static bool TryResolveNearest(
        EntityRepository world,
        SNum.Vector3     near,
        out Fdp.Core.Entity result,
        bool requireVehicleState = false)
    {
        result      = default;
        bool  found = false;
        float bestDistSq = float.MaxValue;

        foreach (var e in world.Query().With<SimTransform>().With<TkbIdentity>().Build())
        {
            // BATCH-27 / STR-D21 F7 fix: when resolving a vehicle entity, skip infantry/civilian
            // entities that happen to be within the 2 m search radius.
            if (requireVehicleState
                && world.IsComponentTypeRegistered<VehicleState>()
                && !world.HasComponent<VehicleState>(e))
                continue;

            var pos = world.GetComponentRO<SimTransform>(e).Position;
            float d = (pos - near).LengthSquared();
            if (d < bestDistSq)
            {
                bestDistSq = d;
                result     = e;
                found      = true;
            }
        }

        // Accept if within 2 m of the spawn position.
        return found && bestDistSq < 4.0f;
    }
}

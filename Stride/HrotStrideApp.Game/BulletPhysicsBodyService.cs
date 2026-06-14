#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using NLog;
using Stride.Engine;
using Stride.Physics;
using Stride.Rendering;
using SMath = Stride.Core.Mathematics;

namespace HrotStrideApp;

/// <summary>
/// Concrete Bullet physics body service — the real GPU-path implementation of
/// <see cref="IPhysicsBodyService"/> (STR-D11, BATCH-17).
///
/// <para>
/// <b>Requires a running Stride <c>Simulation</c>.</b>
/// Constructed in <see cref="StrideHrotGame.BootEditorSubsystem"/> after
/// <c>BeginRun</c> — at the point where <c>SceneSystem.SceneInstance.RootScene</c> is valid
/// and Stride's <c>PhysicsProcessor</c> has been initialised by the engine's system pipeline.
/// </para>
///
/// <para>
/// <b>STR-D13 visual-entity unification.</b>
/// The physics component is added to the <b>same Stride entity as the
/// <c>ModelComponent</c></b> — the one created by <see cref="StrideVisualFactory"/> and
/// recorded in <see cref="StrideVisualReference.VisualHandle"/>.
/// This is looked up via the <see cref="StrideVisualBindingSystem.Visuals"/> dictionary
/// that is passed at construction time. Bullet moving the body therefore moves the visible
/// model at zero additional cost (design §6.2 "option B").
/// </para>
///
/// <para>
/// <b>Shape mapping (per <see cref="IPhysicsBodyService"/> XML docs + design §6.1/§6.2):</b>
/// <list type="bullet">
///   <item><see cref="CollisionShapeKind.Capsule"/> →
///     <c>CharacterComponent</c> with a <c>CapsuleColliderShape</c> (radius/height from
///     <see cref="ShapeDims"/>); gravity enabled so it falls and rests on the static arena
///     floor (the MainScene's 144 authoritative static colliders, §12).</item>
///   <item><see cref="CollisionShapeKind.OrientedBox"/> →
///     kinematic <c>RigidbodyComponent</c> (<c>IsKinematic = true</c>) with a
///     <c>BoxColliderShape</c> from <see cref="ShapeDims"/>. The
///     <see cref="KinematicVehicleMotor"/> owns collision response for this shape.</item>
/// </list>
/// Other shape kinds receive a best-effort fallback (sphere/box) and a Warn log.
/// </para>
///
/// <para>
/// <b>How to obtain the Simulation [VERIFY result]:</b>
/// <c>scene.GetProcessor&lt;PhysicsProcessor&gt;()</c> on the root scene returns the
/// <c>PhysicsProcessor</c>; from there <c>physicsProcessor.Simulation</c> gives the live
/// <c>Stride.Physics.Simulation</c>. Adding a physics component (
/// <c>CharacterComponent</c> / <c>RigidbodyComponent</c>) to a scene entity automatically
/// registers it with the <c>PhysicsProcessor</c> (the processor tracks all physics
/// components via Stride's <c>EntityProcessor&lt;T&gt;</c> base class). Removing the
/// component unregisters it. We do NOT call any internal Add/Remove methods directly.
/// </para>
///
/// <para>
/// <b>MoveKinematic approach + limitations:</b>
/// Bullet kinematic bodies are moved by setting <c>PhysicsComponent.Entity.Transform.Position</c>
/// and calling <c>Simulation.SimulationProfiler</c>. However, Stride does not expose a
/// first-class "swept move" API on kinematic bodies. Our implementation performs:
/// 1. A Bullet <c>Simulation.ShapeSweepPenetrationDepth</c>-style sweep via
///    <c>Simulation.LinearSweepPenetrationDepth</c> (if available) to detect contacts.
/// 2. If the sweep is blocked (penetration &gt; 0), we clamp the move to the safe distance
///    (slide along the contact normal, or fully block). This is a reasonable first-cut
///    block-or-slide. Limitation: does not produce smooth slides along curved surfaces;
///    complex multi-face contacts may block instead of sliding. The
///    <see cref="KinematicVehicleMotor"/> handles the post-collision velocity from the
///    returned <see cref="KinematicMoveResult.ActualDelta"/>.
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> all calls happen on the single Stride host thread (§8.3).
/// </para>
/// </summary>
public sealed class BulletPhysicsBodyService : IPhysicsBodyService, IBodyRepositionService
{
    // ── NLog ─────────────────────────────────────────────────────────────────────
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // ── Throttle constants ────────────────────────────────────────────────────
    /// <summary>Throttle interval (in frames) for the per-entity position log.</summary>
    private const int PositionLogInterval = 120; // ~2 s at 60 fps

    // ── Physics processor + simulation ────────────────────────────────────────
    private readonly Simulation _simulation;

    // ── Visual binding ────────────────────────────────────────────────────────
    // Used to look up the entity's existing Stride visual entity (STR-D13).
    private readonly IReadOnlyDictionary<Fdp.Core.Entity, StrideVisualReference> _visuals;

    // ── Body tracking ─────────────────────────────────────────────────────────
    // Maps the opaque handle returned by CreateBody to the concrete body state.
    private readonly Dictionary<object, BodyEntry> _bodies = new();
    private int _handleCounter;

    // Per-entity diagnostics state.
    private readonly Dictionary<object, DiagState> _diagState = new();

    // ── Types ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deferred configuration for a dynamic <see cref="RigidbodyComponent"/>.
    /// Stored at <see cref="BodyEntry"/> creation time and applied once the native Bullet
    /// body is confirmed in the simulation (i.e. <c>Simulation != null</c>).
    /// </summary>
    private readonly struct DynamicConfig
    {
        public SMath.Vector3 AngularFactor   { get; }
        public SMath.Vector3 LinearFactor    { get; }
        public bool          CanSleep        { get; }
        public float         LinearDamping   { get; }
        /// <summary>
        /// Angular damping applied to the Bullet body.
        /// Set to 0 so the imposed angular velocity (yaw rate) is not bled off by Bullet's
        /// internal angular-damping integration between velocity-command frames (BATCH-17
        /// yaw-fidelity fix). [VERIFY] RigidbodyComponent.AngularDamping confirmed present
        /// in Stride.Physics.dll 4.2.1.2487 via reflection.
        /// </summary>
        public float         AngularDamping  { get; }
        public float         Friction        { get; }

        public DynamicConfig(
            SMath.Vector3 angularFactor,
            SMath.Vector3 linearFactor,
            bool          canSleep,
            float         linearDamping,
            float         angularDamping,
            float         friction)
        {
            AngularFactor  = angularFactor;
            LinearFactor   = linearFactor;
            CanSleep       = canSleep;
            LinearDamping  = linearDamping;
            AngularDamping = angularDamping;
            Friction       = friction;
        }
    }

    /// <summary>Internal record of a live physics body.</summary>
    private sealed class BodyEntry
    {
        /// <summary>The Stride visual entity that owns the physics component (STR-D13).</summary>
        public global::Stride.Engine.Entity StrideEntity { get; }

        /// <summary>
        /// The physics component attached to the visual entity.
        /// Either a <see cref="CharacterComponent"/> or a <see cref="RigidbodyComponent"/>.
        /// </summary>
        public PhysicsComponent PhysicsComponent { get; }

        /// <summary>Whether this body is a kinematic character/vehicle (true) or a dynamic body (false).</summary>
        public bool IsKinematic { get; }

        /// <summary>Shape kind stored for diagnostics.</summary>
        public CollisionShapeKind ShapeKind { get; }

        /// <summary>
        /// Stride-space half-extents of the box (for <see cref="CollisionShapeKind.OrientedBox"/>).
        /// Used by <see cref="BulletPhysicsBodyService.MoveKinematic"/> to compute the safe stop distance
        /// (box FACE at wall, not center).  Zero for non-box shapes.
        /// </summary>
        public SMath.Vector3 BoxHalfExtentsStride { get; }

        /// <summary>
        /// Pending runtime-physics configuration for dynamic <see cref="RigidbodyComponent"/> bodies.
        /// Non-null only when the body was created as DYNAMIC and the config has not yet been applied.
        /// Applied lazily the first time the body is confirmed in the simulation
        /// (<c>rb.Simulation != null</c>) by <see cref="BulletPhysicsBodyService.ApplyDynamicConfigIfReady"/>.
        /// </summary>
        public DynamicConfig? PendingDynamicConfig { get; set; }

        /// <summary>
        /// True when at least one velocity/config call has thrown <see cref="InvalidOperationException"/>
        /// indicating the native Bullet body is registered in the simulation (<c>Simulation != null</c>)
        /// but the underlying <c>btRigidBody</c> has not yet been fully initialised by Bullet's
        /// <c>stepSimulation</c>.  While this flag is set all motor calls skip cheaply (no throw,
        /// no per-frame log).  Cleared by <see cref="BulletPhysicsBodyService.ApplyDynamicConfigIfReady"/>
        /// on the first frame the deferred config is successfully applied (proving the native body is ready).
        /// Re-armed automatically if the component is replaced.
        /// </summary>
        public bool NativeBodyNotReady { get; set; }

        /// <summary>
        /// True once the single Warn log "body never became physics-ready" has been emitted for
        /// this body, so we do not spam the log every frame.  Reset together with
        /// <see cref="NativeBodyNotReady"/> so a re-armed body logs again if it fails again.
        /// </summary>
        public bool NativeNotReadyWarnLogged { get; set; }

        // ── Task 1: initial-pose slam (BATCH-S2-G) ────────────────────────────
        /// <summary>
        /// Stride-space position captured at CreateBody time (after the final
        /// <c>Transform.Position</c> assignment, including the restingY override for OrientedBox).
        /// Used by <see cref="BulletPhysicsBodyService.ApplyDynamicConfigIfReady"/> to slam
        /// the native body to its intended spawn position on the first ready frame
        /// (belt-and-suspenders guard for hosted-mode timing).
        /// </summary>
        public SMath.Vector3 InitialStridePos { get; set; }

        /// <summary>
        /// Stride-space rotation captured at CreateBody time.
        /// Paired with <see cref="InitialStridePos"/> for the first-ready slam.
        /// </summary>
        public SMath.Quaternion InitialStrideRot { get; set; }

        /// <summary>
        /// False until the first-ready slam has been applied (exactly once per body).
        /// Prevents repeated re-slamming after the body starts moving.
        /// </summary>
        public bool InitialPoseApplied { get; set; }

        /// <summary>
        /// The FDP-space position the reverse-sync last wrote into SimTransform for this body
        /// (BATCH-S2-K). This is the baseline SyncBodyToExternalPose compares the incoming
        /// SimTransform against — divergence means an EXTERNAL writer (operator drag) changed
        /// SimTransform, not the muscle's own physics motion.
        /// </summary>
        public System.Numerics.Vector3 LastReverseSyncedFdpPos { get; set; }

        /// <summary>False until RecordReverseSyncedPose has run at least once for this body.</summary>
        public bool HasReverseSyncBaseline { get; set; }

        public BodyEntry(
            global::Stride.Engine.Entity strideEntity,
            PhysicsComponent physicsComponent,
            bool isKinematic,
            CollisionShapeKind shapeKind,
            SMath.Vector3 boxHalfExtentsStride = default,
            DynamicConfig? pendingDynamicConfig = null)
        {
            StrideEntity          = strideEntity;
            PhysicsComponent      = physicsComponent;
            IsKinematic           = isKinematic;
            ShapeKind             = shapeKind;
            BoxHalfExtentsStride  = boxHalfExtentsStride;
            PendingDynamicConfig  = pendingDynamicConfig;
        }
    }

    /// <summary>Per-body diagnostic state for grounded-transition tracking + position throttle.</summary>
    private sealed class DiagState
    {
        public bool LastGrounded        { get; set; }
        public bool GroundedInitialised { get; set; }
        public int  FrameCounter        { get; set; }
        public int  EarlyPosCount       { get; set; }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs the service bound to a running Stride physics simulation and the live visual set.
    /// </summary>
    /// <param name="simulation">
    /// The running <c>Stride.Physics.Simulation</c>.
    /// Obtain via <c>scene.GetProcessor&lt;PhysicsProcessor&gt;().Simulation</c>
    /// after <c>BeginRun</c>.
    /// </param>
    /// <param name="visuals">
    /// The live visual binding dictionary from <see cref="StrideVisualBindingSystem.Visuals"/>.
    /// Used by <see cref="CreateBody"/> to look up the entity's existing Stride visual entity
    /// (STR-D13): the physics component is attached to that entity so Bullet motion moves
    /// the visible model.
    /// </param>
    public BulletPhysicsBodyService(
        Simulation simulation,
        IReadOnlyDictionary<Fdp.Core.Entity, StrideVisualReference> visuals)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _visuals    = visuals    ?? throw new ArgumentNullException(nameof(visuals));

        Log.Info("[BulletPhysicsBodyService] Constructed. Simulation={0}, FixedTimeStep={1:F4}s",
            simulation.GetType().Name,
            simulation.FixedTimeStep);
    }

    // ── IPhysicsBodyService: body lifecycle ───────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// STR-D13: the physics component is added to the entity's existing Stride <b>visual</b>
    /// entity (looked up from <c>visuals[entity]</c>), not to a new physics-only entity.
    /// If the visual entity has not yet been created (race with <c>StrideVisualBindingSystem</c>),
    /// the body creation is rejected with a Warn log — <see cref="PhysicsBodyLifecycleSystem"/>
    /// will retry the next frame.
    /// </remarks>
    public object CreateBody(
        Fdp.Core.Entity    entity,
        CollisionShapeKind shapeKind,
        ShapeDims          dims,
        in SimTransform    initialPose)
    {
        // ── STR-D13: look up the visual entity ─────────────────────────────
        if (!_visuals.TryGetValue(entity, out var visualRef))
        {
            Log.Warn("[BulletPhysicsBodyService] CreateBody: no visual entity for FDP entity #{0} yet — skip (will retry next frame).",
                entity.Index);
            // Return a sentinel handle so the lifecycle system records a body reference;
            // but use a distinct sentinel so we never try to cast it.
            // Actually, the lifecycle system only calls CreateBody and stores the handle —
            // since no real Bullet object exists, returning a sentinel here would cause the
            // lifecycle to think a body exists when it doesn't. Better to return null and
            // let the lifecycle skip (by convention: null handle = no body).
            // However the interface returns object (non-nullable). Use a special sentinel class.
            return new SkippedBodyHandle();
        }

        if (visualRef.VisualHandle is not global::Stride.Engine.Entity strideEntity)
        {
            Log.Warn("[BulletPhysicsBodyService] CreateBody: visual handle for entity #{0} is not a Stride Entity ({1}) — skip.",
                entity.Index, visualRef.VisualHandle?.GetType().Name ?? "null");
            return new SkippedBodyHandle();
        }

        // ── Initial Stride-space position ──────────────────────────────────
        var initialPos = FdpStrideTransform.ToStridePosition(initialPose.Position);
        var initialRot = FdpStrideTransform.ToStrideRotation(initialPose.Rotation);
        Log.Info("[DIAG-POS] CreateBody entity=#{0} shape={1} FDP=({2:F3},{3:F3},{4:F3}) StrideInit=({5:F3},{6:F3},{7:F3})",
            entity.Index, shapeKind,
            initialPose.Position.X, initialPose.Position.Y, initialPose.Position.Z,
            initialPos.X, initialPos.Y, initialPos.Z);

        // We set the entity transform to the initial pose so the physics component
        // is placed correctly when registered.
        strideEntity.Transform.Position = initialPos;
        strideEntity.Transform.Rotation = initialRot;

        // ── Create + attach the physics component ──────────────────────────
        PhysicsComponent physComp;
        bool isKinematic;
        // Stride-space box half-extents (set for OrientedBox, zero otherwise).
        // Used by MoveKinematic to compute face-stop safe distance.
        SMath.Vector3 boxHalfExtentsStride = SMath.Vector3.Zero;
        // Deferred runtime config for dynamic rigidbodies (applied once Simulation != null).
        DynamicConfig? pendingDynamicConfig = null;

        switch (shapeKind)
        {
            case CollisionShapeKind.Capsule:
            {
                // CharacterComponent: capsule shape, gravity enabled (falls + rests on floor).
                // CapsuleColliderShape(radius, height, ShapeOrientation.UpY).
                // Stride CapsuleColliderShape: the "height" param is the shaft length
                // (cylindrical part); total height = height + 2*radius.
                // We map ShapeDims.Height as the total capsule height, so shaft = Height - 2*Radius.
                float radius = Math.Max(dims.Radius, 0.1f);
                float totalHeight = Math.Max(dims.Height, radius * 2f + 0.01f);
                float shaftHeight = totalHeight - 2f * radius;
                shaftHeight = Math.Max(shaftHeight, 0.01f);

                // CapsuleColliderShape(bool is2D, float radius, float length, ShapeOrientation upAxis)
                // "length" is the shaft (cylindrical) part; total height = length + 2*radius.
                var capsuleShape = new CapsuleColliderShape(
                    is2D:   false,
                    radius: radius,
                    length: shaftHeight,        // [VERIFY] 3rd param is "length" (shaft), not "height"
                    ShapeOrientation.UpY);

                // ── ISSUE-1 FIX: collider vertical offset so entity-origin = model base ──
                // Bullet places the capsule CENTER at the entity's Transform.Position.
                // The rendered model's origin is at its base (feet). Without an offset the
                // capsule center sits at foot-level, making the model appear ~halfHeight m in
                // the air when the capsule rests on the floor.
                // Shifting the collider up by halfHeight = radius + shaftHeight/2 aligns the
                // bottom of the capsule with the entity origin → feet land on the floor.
                float capsuleHalfHeight = radius + shaftHeight / 2f;
                capsuleShape.LocalOffset = new SMath.Vector3(0f, capsuleHalfHeight, 0f);
                capsuleShape.UpdateLocalTransformations();

                var character = new CharacterComponent
                {
                    ColliderShape = capsuleShape,
                    // JumpSpeed: sensible default (m/s). CharacterComponent uses Bullet's built-in gravity.
                    JumpSpeed     = 5f,
                    // MaxSlope: allow walking up to 45° (CharacterComponent.MaxSlope is AngleSingle).
                    MaxSlope      = new SMath.AngleSingle((float)(Math.PI / 4.0), SMath.AngleType.Radian),
                    StepHeight    = 0.35f,
                };

                // BATCH-S2-G Task 1: commit the entity world matrix before Add so
                // Stride's PhysicsProcessor creates the native body at the intended position.
                strideEntity.Transform.UpdateWorldMatrix();
                strideEntity.Add(character);
                physComp    = character;
                isKinematic = true; // CharacterComponent is internally kinematic

                Log.Info(
                    "[BulletPhysicsBodyService] CreateBody: entity #{0} → CharacterComponent " +
                    "(capsule r={1:F3} shaft={2:F3} halfH={3:F3}) LocalOffset.Y={3:F3} attached to visual '{4}' @ Stride {5}.",
                    entity.Index, radius, shaftHeight, capsuleHalfHeight,
                    strideEntity.Name, initialPos);
                break;
            }

            case CollisionShapeKind.OrientedBox:
            {
                // ── DYNAMIC RIGIDBODY (BATCH-17 dynamic-body migration) ───────────────
                // Vehicle (F2 APC) is now a DYNAMIC RigidbodyComponent driven via
                // SetLinearVelocityXZ / SetYawRate each frame.  Bullet's contact solver
                // handles all wall/floor collisions — the body cannot pass through walls
                // and rests on the floor under gravity without any manual sweep logic.
                //
                // ── COLLIDER SIZE: derived from model BoundingBox ─────────────────────
                // The collider is derived from the visual model's ACTUAL bounding box so
                // collider and visible mesh are exactly the same size (KEEP from prior impl).
                //
                // ── MODEL-ORIGIN CONVENTION (box) ─────────────────────────────────────
                // BOX: model origin can be ANYWHERE inside the mesh (depends on the 3D artist).
                //   → LocalOffset = boxCenter (center of the bounding box in entity-local space)
                //     so the collider CENTER coincides with the visual center regardless of
                //     where the model origin is placed.
                //   → The entity is placed at Stride Y = -bbox.Minimum.Y so the visual
                //     bottom (entity.Y + Minimum.Y) = 0 (floor).  Equivalently the collider
                //     bottom = entity.Y + boxCenter.Y − halfY = 0.
                //
                // FALLBACK (headless / no GPU model loaded): use TKB ShapeDims as before.

                BoxParams? bboxParams = null;
                var modelComp = strideEntity.Get<ModelComponent>();
                if (modelComp?.Model?.BoundingBox is SMath.BoundingBox mbb)
                {
                    bboxParams = ComputeBoxParamsFromBoundingBox(mbb);
                    if (bboxParams == null)
                    {
                        Log.Warn(
                            "[BulletPhysicsBodyService] CreateBody: entity #{0} model BoundingBox " +
                            "is degenerate (min={1} max={2}) — falling back to ShapeDims.",
                            entity.Index, mbb.Minimum, mbb.Maximum);
                    }
                }
                else
                {
                    Log.Warn(
                        "[BulletPhysicsBodyService] CreateBody: entity #{0} has no ModelComponent " +
                        "or model not loaded — falling back to ShapeDims (headless / GPU not ready).",
                        entity.Index);
                }

                SMath.Vector3 boxLocalOffset;
                float         useHalfX, useHalfY, useHalfZ;

                if (bboxParams.HasValue)
                {
                    // Model-derived box: collider exactly overlaps the rendered mesh.
                    var p = bboxParams.Value;
                    useHalfX     = p.HalfExtents.X;
                    useHalfY     = p.HalfExtents.Y;
                    useHalfZ     = p.HalfExtents.Z;
                    boxLocalOffset = p.BoxCenter;

                    // Place the entity at the resting Stride Y so the visual bottom is at Y=0.
                    // The dynamic body will settle under gravity — this is the correct resting height.
                    strideEntity.Transform.Position = new SMath.Vector3(
                        strideEntity.Transform.Position.X,
                        p.RestingStrideY,
                        strideEntity.Transform.Position.Z);

                    Log.Info(
                        "[BulletPhysicsBodyService] CreateBody: entity #{0} → DYNAMIC RigidbodyComponent " +
                        "(box {1:F3}×{2:F3}×{3:F3} from MODEL bbox min={4} max={5}) " +
                        "LocalOffset={6} restingY={7:F3} (bbox-derived, bottom on floor) " +
                        "attached to visual '{8}'.",
                        entity.Index,
                        useHalfX * 2f, useHalfY * 2f, useHalfZ * 2f,
                        modelComp!.Model!.BoundingBox.Minimum,
                        modelComp.Model.BoundingBox.Maximum,
                        boxLocalOffset,
                        p.RestingStrideY,
                        strideEntity.Name);
                }
                else
                {
                    // ShapeDims fallback (headless tests / model not yet loaded).
                    // FDP→Stride swizzle: Stride.X=FDP.X, Stride.Y=FDP.Z, Stride.Z=FDP.Y.
                    useHalfX     = Math.Max(dims.HalfX, 0.05f);
                    useHalfY     = Math.Max(dims.HalfZ, 0.05f); // FDP Z=Up → Stride Y
                    useHalfZ     = Math.Max(dims.HalfY, 0.05f); // FDP Y=North → Stride Z
                    boxLocalOffset = SMath.Vector3.Zero; // ShapeDims assumes center-origin

                    Log.Info(
                        "[BulletPhysicsBodyService] CreateBody: entity #{0} → DYNAMIC RigidbodyComponent " +
                        "(box {1:F3}×{2:F3}×{3:F3} from ShapeDims FALLBACK) LocalOffset=Zero " +
                        "attached to visual '{4}' @ Stride {5}.",
                        entity.Index, useHalfX * 2f, useHalfY * 2f, useHalfZ * 2f,
                        strideEntity.Name, strideEntity.Transform.Position);
                }

                var boxShape = new BoxColliderShape(
                    is2D: false,
                    size: new SMath.Vector3(useHalfX * 2f, useHalfY * 2f, useHalfZ * 2f));

                boxShape.LocalOffset = boxLocalOffset;
                boxShape.UpdateLocalTransformations();

                // DYNAMIC body — Bullet solver handles resting on floor and wall collisions.
                // IsKinematic = false: solver-controlled.
                // Mass = 1f: gives inertia so the body responds correctly to velocity commands.
                // Gravity: enabled by default (keeps the body on the floor).
                //
                // IMPORTANT: runtime-physics properties (AngularFactor, LinearFactor, CanSleep,
                // LinearDamping, Friction) are NOT set here in the initializer.  Those properties
                // reach into the native Bullet body, which does not exist until the entity is added
                // to the scene AND Stride's PhysicsProcessor has processed it on the next step.
                // Setting them before Add() → PhysicsProcessor run throws:
                //   "Attempted to call a Physics function that is available only when the Entity
                //    has been already added to the Scene."
                // They are stored in PendingDynamicConfig and applied lazily (first frame the
                // body reports Simulation != null) by ApplyDynamicConfigIfReady.
                // ColliderShape, IsKinematic, Mass ARE safe in the initializer (consumed at
                // native-body creation time by the PhysicsProcessor).
                var rigidbody = new RigidbodyComponent
                {
                    ColliderShape = boxShape,
                    IsKinematic   = false,
                    Mass          = 1f,
                };

                // BATCH-S2-G Task 1: commit the entity world matrix before Add so
                // Stride's PhysicsProcessor creates the native btRigidBody at the intended
                // world position (not at the stale origin world matrix).
                strideEntity.Transform.UpdateWorldMatrix();
                strideEntity.Add(rigidbody);
                physComp    = rigidbody;
                isKinematic = false;  // DYNAMIC — BodyEntry.IsKinematic = false
                // Store model-derived (or ShapeDims-fallback) half-extents (for diagnostics).
                boxHalfExtentsStride = new SMath.Vector3(useHalfX, useHalfY, useHalfZ);
                // Schedule deferred runtime-physics config (applied once Simulation != null).
                // BATCH-17 yaw-fidelity fix: near-zero friction + zero angular damping so the
                // floor contact patch cannot fight the imposed yaw rate.  The vehicle box resting
                // on the floor creates a large contact area; at friction=0.1 the friction torque
                // was wide enough to widen the effective turn radius vs the commanded bicycle model.
                // Reducing friction to ~0.02 eliminates the yaw-opposing torque; setting
                // angularDamping=0 ensures Bullet's own damping integration does not bleed off the
                // commanded angular velocity between frames.  Wall collision is non-penetration
                // (friction-independent), and velocity is re-commanded every frame, so the car
                // still stops at walls and we do not drift on straight runs.
                pendingDynamicConfig = new DynamicConfig(
                    angularFactor  : new SMath.Vector3(0f, 1f, 0f), // yaw only — upright lock
                    linearFactor   : new SMath.Vector3(1f, 1f, 1f), // full XYZ translation
                    canSleep       : false,                          // always respond to velocity
                    linearDamping  : 0.05f,                         // minimal drag (re-commanded each frame)
                    angularDamping : 0f,                            // 0 → commanded yaw not bled off
                    friction       : 0.02f);                        // near-zero → floor can't resist yaw
                break;
            }

            case CollisionShapeKind.Sphere:
            {
                float radius = Math.Max(dims.Radius, 0.1f);
                var sphereShape = new SphereColliderShape(is2D: false, radiusParam: radius);
                var rigidbody = new RigidbodyComponent
                {
                    ColliderShape = sphereShape,
                    IsKinematic   = false,
                    Mass          = 1f,
                };
                // BATCH-S2-G Task 1: commit world matrix before Add.
                strideEntity.Transform.UpdateWorldMatrix();
                strideEntity.Add(rigidbody);
                physComp    = rigidbody;
                isKinematic = false;

                Log.Warn(
                    "[BulletPhysicsBodyService] CreateBody: entity #{0} → Sphere fallback (r={1:F3}) on '{2}'.",
                    entity.Index, radius, strideEntity.Name);
                break;
            }

            default:
            {
                // Fallback: a small box.
                var boxShape = new BoxColliderShape(is2D: false, size: new SMath.Vector3(0.5f, 0.5f, 0.5f));
                var rigidbody = new RigidbodyComponent
                {
                    ColliderShape = boxShape,
                    IsKinematic   = false,
                    Mass          = 1f,
                };
                // BATCH-S2-G Task 1: commit world matrix before Add.
                strideEntity.Transform.UpdateWorldMatrix();
                strideEntity.Add(rigidbody);
                physComp    = rigidbody;
                isKinematic = false;

                Log.Warn(
                    "[BulletPhysicsBodyService] CreateBody: entity #{0} has unsupported shape '{1}' — box fallback on '{2}'.",
                    entity.Index, shapeKind, strideEntity.Name);
                break;
            }
        }

        // ── Register the body entry ────────────────────────────────────────
        var handle = $"BulletBody_{++_handleCounter}_{shapeKind}";
        var entry  = new BodyEntry(strideEntity, physComp, isKinematic, shapeKind,
                                   boxHalfExtentsStride, pendingDynamicConfig);

        // BATCH-S2-G Task 1: capture the initial Stride pose (after all restingY adjustments)
        // for the belt-and-suspenders first-ready slam in ApplyDynamicConfigIfReady.
        entry.InitialStridePos = strideEntity.Transform.Position;
        entry.InitialStrideRot = strideEntity.Transform.Rotation;
        entry.InitialPoseApplied = false;

        _bodies[handle]   = entry;
        _diagState[handle] = new DiagState();

        return handle;
    }

    /// <inheritdoc/>
    public void RemoveBody(object bodyHandle)
    {
        if (bodyHandle is SkippedBodyHandle)
            return; // Sentinel for entities whose visual wasn't ready — nothing to remove.

        if (!_bodies.TryGetValue(bodyHandle, out var entry))
        {
            Log.Warn("[BulletPhysicsBodyService] RemoveBody: unknown handle '{0}' — ignored.", bodyHandle);
            return;
        }

        // Remove the physics component from the visual entity.
        // Stride's EntityProcessor detects the removal and unregisters it from Bullet.
        try
        {
            entry.StrideEntity.Remove(entry.PhysicsComponent);
            // Dispose the collider shape to release unmanaged Bullet memory.
            entry.PhysicsComponent.ColliderShape?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "[BulletPhysicsBodyService] RemoveBody: exception removing component from '{0}'.",
                entry.StrideEntity.Name);
        }

        _bodies.Remove(bodyHandle);
        _diagState.Remove(bodyHandle);

        Log.Info("[BulletPhysicsBodyService] RemoveBody: entity '{0}' ({1}) removed from simulation.",
            entry.StrideEntity.Name, entry.ShapeKind);
    }

    // ── IPhysicsBodyService: character motor ──────────────────────────────────

    /// <inheritdoc/>
    public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity)
    {
        if (bodyHandle is SkippedBodyHandle) return;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
        if (entry.PhysicsComponent is CharacterComponent character)
        {
            character.SetVelocity(velocity);
        }
    }

    /// <inheritdoc/>
    public void Jump(object bodyHandle)
    {
        if (bodyHandle is SkippedBodyHandle) return;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
        if (entry.PhysicsComponent is CharacterComponent character)
        {
            character.Jump();
            Log.Debug("[BulletPhysicsBodyService] Jump: '{0}'.", entry.StrideEntity.Name);
        }
    }

    /// <inheritdoc/>
    public bool IsGrounded(object bodyHandle)
    {
        if (bodyHandle is SkippedBodyHandle) return false;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return false;
        if (entry.PhysicsComponent is CharacterComponent character)
        {
            bool grounded = character.IsGrounded;

            // ── Grounded-transition diagnostic ────────────────────────────
            if (_diagState.TryGetValue(bodyHandle, out var diag))
            {
                if (!diag.GroundedInitialised)
                {
                    diag.GroundedInitialised = true;
                    diag.LastGrounded = grounded;
                    Log.Info("[BulletPhysicsBodyService] Grounded initial state: '{0}' grounded={1}.",
                        entry.StrideEntity.Name, grounded);
                }
                else if (grounded != diag.LastGrounded)
                {
                    diag.LastGrounded = grounded;
                    if (grounded)
                        Log.Info("[BulletPhysicsBodyService] Grounded LANDED: '{0}' touched floor.",
                            entry.StrideEntity.Name);
                    else
                        Log.Info("[BulletPhysicsBodyService] Grounded AIRBORNE: '{0}' left floor.",
                            entry.StrideEntity.Name);
                }
            }

            return grounded;
        }
        return false;
    }

    // ── IPhysicsBodyService: dynamic vehicle motor ────────────────────────────

    /// <summary>
    /// Applies the deferred runtime-physics configuration to a dynamic <see cref="RigidbodyComponent"/>
    /// once the native Bullet body is confirmed in the simulation.
    ///
    /// <para>
    /// The native Bullet body is created by Stride's <c>PhysicsProcessor</c> on the first
    /// simulation step AFTER the component is added to the scene entity.  Properties such as
    /// <c>AngularFactor</c>, <c>LinearFactor</c>, <c>CanSleep</c>, <c>LinearDamping</c>, and
    /// <c>Friction</c> call into the native body and throw
    /// <c>"…Physics function that is available only when the Entity has been added to the Scene"</c>
    /// if called before the native body exists.
    /// </para>
    ///
    /// <para>
    /// Readiness check: <c>rb.Simulation != null</c>.
    /// <c>PhysicsComponent.Simulation</c> is set by the <c>PhysicsProcessor</c> when it processes
    /// the entity — this happens on the next step after <c>strideEntity.Add(component)</c>.
    /// Until then <c>Simulation</c> is <c>null</c> and the call is skipped (will be retried next frame).
    /// </para>
    ///
    /// <para>
    /// Config is applied exactly once (idempotent via the <see cref="BodyEntry.PendingDynamicConfig"/>
    /// nullable flag).  On success, <see cref="BodyEntry.NativeBodyNotReady"/> is cleared so
    /// motor calls resume. On failure (native body not yet stepped by Bullet despite
    /// <c>Simulation != null</c>), <see cref="BodyEntry.NativeBodyNotReady"/> is set so the
    /// motor skips cheaply — zero throws, zero per-frame logs.
    /// </para>
    /// </summary>
    private void ApplyDynamicConfigIfReady(BodyEntry entry)
    {
        if (entry.PendingDynamicConfig is not { } cfg) return; // nothing pending
        if (entry.PhysicsComponent is not RigidbodyComponent rb)    return; // not a rigidbody
        if (rb.Simulation == null)                                   return; // not yet in simulation

        try
        {
            // AngularFactor = (0,1,0): yaw only — upright lock (no tip/roll).
            rb.AngularFactor  = cfg.AngularFactor;
            // LinearFactor = (1,1,1): full XYZ translation (default, but explicit).
            rb.LinearFactor   = cfg.LinearFactor;
            // CanSleep = false: always respond to velocity commands.
            rb.CanSleep       = cfg.CanSleep;
            // LinearDamping: slight drag for stability.
            rb.LinearDamping  = cfg.LinearDamping;
            // AngularDamping = 0: do NOT bleed off the commanded yaw rate between frames.
            // Bullet's default angular damping (non-zero) would reduce the angular velocity
            // by the factor (1 - dt * angularDamping) each step, widening the effective turn
            // radius even after the floor-friction fix. Setting to 0 ensures the imposed
            // yaw rate survives intact until the next velocity-command frame.
            // [VERIFY] RigidbodyComponent.AngularDamping confirmed in Stride.Physics.dll 4.2.1.2487.
            rb.AngularDamping = cfg.AngularDamping;
            // Friction: near-zero so the floor doesn't resist the commanded yaw (BATCH-17
            // yaw-fidelity fix — large bottom-face contact patch was creating a friction
            // torque opposing the imposed yaw rate, widening the effective turn radius).
            rb.Friction       = cfg.Friction;

            // Mark config as applied — set PendingDynamicConfig to null.
            entry.PendingDynamicConfig = null;

            // Native body is now confirmed ready: clear the not-ready guard so motor calls resume.
            entry.NativeBodyNotReady       = false;
            entry.NativeNotReadyWarnLogged = false;

            // BATCH-S2-G Task 1 — belt-and-suspenders initial-pose slam.
            // On the FIRST frame the native body is confirmed physics-ready, slam its position
            // to the stored spawn pose and zero all velocity.  This guards against hosted-mode
            // timing where the native btRigidBody was silently created at origin despite the
            // UpdateWorldMatrix() call in CreateBody (the body was already partially settled).
            if (!entry.InitialPoseApplied)
            {
                entry.StrideEntity.Transform.Position = entry.InitialStridePos;
                entry.StrideEntity.Transform.Rotation = entry.InitialStrideRot;
                entry.StrideEntity.Transform.UpdateWorldMatrix();
                // Push the entity transform INTO the native btRigidBody. For a dynamic body,
                // setting Transform alone does not move it — physics drives the transform, not
                // vice-versa. Stride API: "Forces an update from the TransformComponent to the
                // Collider.PhysicsWorldTransform. Useful to manually force movements."
                rb.UpdatePhysicsTransformation(true);
                rb.LinearVelocity  = SMath.Vector3.Zero;
                rb.AngularVelocity = SMath.Vector3.Zero;
                entry.InitialPoseApplied = true;
                Log.Info("[BulletPhysicsBodyService] InitialPose slammed: '{0}' -> ({1:F2},{2:F2},{3:F2})",
                    entry.StrideEntity.Name,
                    entry.InitialStridePos.X, entry.InitialStridePos.Y, entry.InitialStridePos.Z);
            }

            Log.Info(
                "[BulletPhysicsBodyService] ApplyDynamicConfig: '{0}' — " +
                "AngularFactor={1} LinearFactor={2} CanSleep={3} LinearDamping={4:F3} " +
                "AngularDamping={5:F3} Friction={6:F3}.",
                entry.StrideEntity.Name,
                cfg.AngularFactor, cfg.LinearFactor, cfg.CanSleep,
                cfg.LinearDamping, cfg.AngularDamping, cfg.Friction);
        }
        catch (Exception)
        {
            // Native body not yet stepped by Bullet despite Simulation != null.
            // This happens in hosted mode when the component was added to the scene entity
            // in the same bracket-pre step as the motor (base.Update / Simulate hasn't run yet).
            // Mark the body as not-ready so all motor calls skip cheaply this frame.
            // The body will be retried next frame (ApplyDynamicConfigIfReady is called again
            // from SetLinearVelocityXZ/SetYawRate once Simulation != null).
            entry.NativeBodyNotReady = true;
            // Warn is emitted by the caller (SetLinearVelocityXZ) on first detection.
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the current <c>RigidbodyComponent.LinearVelocity.Y</c> (vertical, set by
    /// gravity/solver) and sets <c>LinearVelocity = (strideVel.X, currentY, strideVel.Z)</c>.
    /// Activates the body so Bullet does not defer the command to the next simulation step.
    ///
    /// <para>
    /// <b>Readiness guard:</b> if the body is not yet in the simulation
    /// (<c>rb.Simulation == null</c>), this method is a no-op for this frame.
    /// The deferred runtime config (AngularFactor/CanSleep/etc.) is applied once on the
    /// first ready call via <see cref="ApplyDynamicConfigIfReady"/>.
    /// </para>
    /// </remarks>
    public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel)
    {
        if (bodyHandle is SkippedBodyHandle) return;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
        if (entry.PhysicsComponent is RigidbodyComponent rb && !rb.IsKinematic)
        {
            // Readiness guard 1: Simulation not yet assigned by PhysicsProcessor.
            if (rb.Simulation == null) return;

            // Readiness guard 2: native Bullet body not yet stepped (NativeBodyNotReady set by
            // ApplyDynamicConfigIfReady or a prior SetLinearVelocityXZ throw).
            // Skip cheaply — zero exception, zero per-frame log.
            if (entry.NativeBodyNotReady)
            {
                // Emit a single Warn per body so the problem is visible without log spam.
                if (!entry.NativeNotReadyWarnLogged)
                {
                    entry.NativeNotReadyWarnLogged = true;
                    Log.Warn(
                        "[BulletPhysicsBodyService] '{0}': native Bullet body not yet physics-ready " +
                        "(Simulation != null but btRigidBody not yet stepped). " +
                        "Velocity commands will be skipped until the body becomes ready. " +
                        "Vehicle will not move until then.",
                        entry.StrideEntity.Name);
                }
                // Retry: if deferred config is still pending, try to apply it — success clears the flag.
                // If config is already applied (PendingDynamicConfig == null), probe LinearVelocity
                // directly; a successful read means the body is now ready.
                if (entry.PendingDynamicConfig != null)
                {
                    ApplyDynamicConfigIfReady(entry);
                }
                else
                {
                    // Config already applied — probe readiness with a LinearVelocity read.
                    try
                    {
                        _ = rb.LinearVelocity; // throws if not ready
                        // Succeeded — body is now ready; clear the not-ready flag.
                        entry.NativeBodyNotReady       = false;
                        entry.NativeNotReadyWarnLogged = false;
                        Log.Info("[BulletPhysicsBodyService] '{0}': native body became physics-ready (recovered).",
                            entry.StrideEntity.Name);
                    }
                    catch (Exception)
                    {
                        // Still not ready — skip and retry next frame.
                        return;
                    }
                }
                // If the flag is still set (config retry failed), skip this frame.
                if (entry.NativeBodyNotReady) return;
            }

            // Apply deferred config (AngularFactor/CanSleep/etc.) on the first ready frame.
            // ApplyDynamicConfigIfReady sets NativeBodyNotReady on failure, clears it on success.
            ApplyDynamicConfigIfReady(entry);

            // If config application just failed (body became not-ready), skip this frame.
            if (entry.NativeBodyNotReady) return;

            try
            {
                // Preserve Y so gravity keeps the body grounded.
                float currentY = rb.LinearVelocity.Y;
                rb.LinearVelocity = new SMath.Vector3(strideLinearVel.X, currentY, strideLinearVel.Z);
                // Activate so the solver sees the updated velocity immediately.
                // Bullet activates a dynamic body automatically when its velocity is set.
            }
            catch (Exception ex)
            {
                // Unexpected throw after config succeeded — mark not-ready and log once.
                entry.NativeBodyNotReady       = true;
                entry.NativeNotReadyWarnLogged = false; // re-arm warn so it fires on next frame
                Log.Warn(
                    "[BulletPhysicsBodyService] SetLinearVelocityXZ: '{0}' threw {1} after config; " +
                    "marking not-ready. Vehicle will not move until body recovers.",
                    entry.StrideEntity.Name, ex.GetType().Name);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Sets <c>RigidbodyComponent.AngularVelocity = (0, strideYawRateRadPerSec, 0)</c>.
    /// Activates the body so Bullet does not defer the command.
    ///
    /// <para>
    /// <b>Readiness guard:</b> if the body is not yet in the simulation
    /// (<c>rb.Simulation == null</c>), this method is a no-op for this frame.
    /// The deferred runtime config (AngularFactor/CanSleep/etc.) is applied once on the
    /// first ready call via <see cref="ApplyDynamicConfigIfReady"/>.
    /// </para>
    /// </remarks>
    public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec)
    {
        if (bodyHandle is SkippedBodyHandle) return;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
        if (entry.PhysicsComponent is RigidbodyComponent rb && !rb.IsKinematic)
        {
            // Readiness guard 1: Simulation not yet assigned by PhysicsProcessor.
            if (rb.Simulation == null) return;

            // Readiness guard 2: native body not yet stepped — skip cheaply.
            // (NativeBodyNotReady is managed by SetLinearVelocityXZ which runs first in the motor;
            // SetYawRate simply checks the same flag and returns without logging again.)
            if (entry.NativeBodyNotReady) return;

            // Apply deferred config on first ready frame (idempotent).
            if (entry.PendingDynamicConfig != null)
                ApplyDynamicConfigIfReady(entry);
            if (entry.NativeBodyNotReady) return; // config just failed

            try
            {
                rb.AngularVelocity = new SMath.Vector3(0f, strideYawRateRadPerSec, 0f);
            }
            catch (Exception ex)
            {
                // Unexpected throw — mark not-ready (warn already logged by SetLinearVelocityXZ
                // which ran earlier this frame; don't double-log).
                entry.NativeBodyNotReady = true;
                Log.Debug(
                    "[BulletPhysicsBodyService] SetYawRate: '{0}' threw {1}; body re-marked not-ready.",
                    entry.StrideEntity.Name, ex.GetType().Name);
            }
        }
    }

    // ── IPhysicsBodyService: reverse-sync ─────────────────────────────────────

    /// <inheritdoc/>
    public BodyState GetBodyState(object bodyHandle)
    {
        if (bodyHandle is SkippedBodyHandle)
        {
            return new BodyState(
                SMath.Vector3.Zero, SMath.Quaternion.Identity,
                SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: true);
        }

        if (!_bodies.TryGetValue(bodyHandle, out var entry))
        {
            return new BodyState(
                SMath.Vector3.Zero, SMath.Quaternion.Identity,
                SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: false);
        }

        // Read the Bullet-resolved pose from the entity's world transform.
        // After PhysicsProcessor steps Bullet, the entity's Transform is updated
        // with the physics-resolved position/rotation.
        var pos = entry.StrideEntity.Transform.Position;
        var rot = entry.StrideEntity.Transform.Rotation;

        // ── Per-entity position diagnostic (throttled) ─────────────────────
        if (_diagState.TryGetValue(bodyHandle, out var diag))
        {
            if (diag.EarlyPosCount < 5)
            {
                diag.EarlyPosCount++;
                Log.Info("[DIAG-POS] GetBodyState '{0}' earlyFrame={1} StridePos=({2:F3},{3:F3},{4:F3}) shape={5} kinematic={6}",
                    entry.StrideEntity.Name, diag.EarlyPosCount, pos.X, pos.Y, pos.Z, entry.ShapeKind, entry.IsKinematic);
            }
            diag.FrameCounter++;
            if (diag.FrameCounter >= PositionLogInterval)
            {
                diag.FrameCounter = 0;
                Log.Debug(
                    "[BulletPhysicsBodyService] BodyState: '{0}' Stride pos=({1:F3},{2:F3},{3:F3}) shape={4}.",
                    entry.StrideEntity.Name, pos.X, pos.Y, pos.Z, entry.ShapeKind);
            }
        }

        // Read velocities from the physics component.
        SMath.Vector3 linearVel  = SMath.Vector3.Zero;
        SMath.Vector3 angularVel = SMath.Vector3.Zero;

        if (entry.PhysicsComponent is RigidbodyComponent rb && !rb.IsKinematic)
        {
            // Guard: if the native body is not yet stepped by Bullet, LinearVelocity /
            // AngularVelocity throw InvalidOperationException. Return zero until the body
            // becomes ready (NativeBodyNotReady is cleared by ApplyDynamicConfigIfReady on
            // the first successful config application).
            if (!entry.NativeBodyNotReady)
            {
                linearVel  = rb.LinearVelocity;
                angularVel = rb.AngularVelocity;
            }
        }
        // CharacterComponents and kinematic rigidbodies: velocity returned as zero;
        // the motor's PostCollisionLinearVelocityFdp channel is used by the reverse-sync.

        return new BodyState(pos, rot, linearVel, angularVel, IsKinematic: entry.IsKinematic);
    }

    // ── BATCH-S2-K: record muscle-authored baseline ───────────────────────────

    /// <inheritdoc cref="IBodyRepositionService.RecordReverseSyncedPose"/>
    public void RecordReverseSyncedPose(object bodyHandle, in SimTransform simTf)
    {
        if (bodyHandle is SkippedBodyHandle) return;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;
        entry.LastReverseSyncedFdpPos = simTf.Position;
        entry.HasReverseSyncBaseline  = true;
    }

    // ── BATCH-S2-G Task 2: external-reposition detection + teleport ───────────

    /// <summary>
    /// Detects when an entity's <see cref="SimTransform"/> was changed externally
    /// (operator drag) and teleports the dynamic Bullet body to match.
    ///
    /// <para>
    /// <b>Baseline:</b> the body's current Stride position (read from
    /// <c>StrideEntity.Transform.Position</c>) is compared against the Stride-space
    /// projection of <paramref name="simTf"/>.  Because <see cref="BulletReverseSyncSystem"/>
    /// writes <c>SimTransform = body position</c> each post-physics frame for owned entities,
    /// <c>SimTransform</c> and the body position stay in sync during normal physics motion.
    /// A divergence larger than <see cref="RepositionEpsilonM"/> therefore indicates that
    /// <c>SimTransform</c> was written from OUTSIDE the muscle (e.g. an editor drag) → teleport.
    /// </para>
    ///
    /// <para>
    /// Only called for dynamic (non-kinematic) bodies; kinematic bodies are moved by
    /// their motor on every frame anyway.  Only called AFTER <see cref="InitialPoseApplied"/>
    /// is true to avoid fighting the first-ready slam.
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Opaque handle returned by <see cref="CreateBody"/>.</param>
    /// <param name="simTf">Current <see cref="SimTransform"/> of the entity from the ECS.</param>
    public void SyncBodyToExternalPose(object bodyHandle, in SimTransform simTf)
    {
        if (bodyHandle is SkippedBodyHandle) return;
        if (!_bodies.TryGetValue(bodyHandle, out var entry)) return;

        // Convert the FDP SimTransform position to Stride space.
        var targetStridePos = FdpStrideTransform.ToStridePosition(simTf.Position);
        var targetStrideRot = FdpStrideTransform.ToStrideRotation(simTf.Rotation);

        // BATCH-S2-K: detect an EXTERNAL write by comparing the incoming SimTransform against the
        // muscle's own last reverse-synced pose (NOT the live body pose). The live body leads
        // SimTransform by one physics step (reverse-sync runs pre-step), so comparing against the
        // live body produced a false divergence every frame and froze the vehicle.
        //
        // No baseline yet (reverse-sync hasn't run for this body): the initial-pose slam owns
        // placement — skip external-reposition until we have a baseline.
        if (!entry.HasReverseSyncBaseline) return;

        // Horizontal (FDP X,Y) divergence between the externally-visible SimTransform and what the
        // muscle last authored. <= epsilon => muscle-authored motion (or no change) => skip.
        float dXf = simTf.Position.X - entry.LastReverseSyncedFdpPos.X;
        float dYf = simTf.Position.Y - entry.LastReverseSyncedFdpPos.Y;
        float distSqFdpXY = dXf * dXf + dYf * dYf;
        if (distSqFdpXY <= RepositionEpsilonM * RepositionEpsilonM) return; // not externally moved

        // External reposition detected. (Below: keep the existing teleport — read the live body pos
        // for Y-preservation and teleport in Stride XZ.)
        var currentBodyPos = entry.StrideEntity.Transform.Position;

        // Teleport (XZ from request, Y kept).
        var newPos = new SMath.Vector3(targetStridePos.X, currentBodyPos.Y, targetStridePos.Z);

        if (entry.PhysicsComponent is CharacterComponent ch)
        {
            // ── CharacterComponent (capsule / mannequin) path ─────────────────
            // CharacterComponent bodies are kinematic (CharacterController), so
            // we cannot use UpdatePhysicsTransformation or zero LinearVelocity.
            // Instead: set the entity transform and call CharacterComponent.Teleport
            // (Stride 4.2.1.2487 API — takes a world Vector3 position).
            // No InitialPoseApplied gate here — capsule bodies never go through
            // ApplyDynamicConfigIfReady, so that flag is always false for them.
            // Readiness guard: only teleport if the component is in the simulation.
            if (ch.Simulation == null) return;

            entry.StrideEntity.Transform.Position = newPos;
            entry.StrideEntity.Transform.Rotation = targetStrideRot;
            entry.StrideEntity.Transform.UpdateWorldMatrix();
            try
            {
                ch.Teleport(newPos);
            }
            catch (Exception ex)
            {
                // CharacterController not yet fully initialised — safe to skip; position set.
                Log.Debug("[BulletPhysicsBodyService] SyncBodyToExternalPose(character): Teleport failed for '{0}' ({1}); entity transform set.",
                    entry.StrideEntity.Name, ex.GetType().Name);
            }

            Log.Info("[BulletPhysicsBodyService] ExternalReposition(character) '{0}': distXZ={1:F3} → Stride ({2:F2},{3:F2},{4:F2}).",
                entry.StrideEntity.Name, MathF.Sqrt(distSqFdpXY),
                newPos.X, newPos.Y, newPos.Z);
        }
        else if (entry.PhysicsComponent is RigidbodyComponent rb && !rb.IsKinematic)
        {
            // ── Dynamic RigidbodyComponent (vehicle) path ─────────────────────
            // Don't act until the initial-pose slam has happened (body must be physics-ready).
            if (!entry.InitialPoseApplied) return;
            if (rb.Simulation == null) return;
            if (entry.NativeBodyNotReady) return;

            entry.StrideEntity.Transform.Position = newPos;
            entry.StrideEntity.Transform.Rotation = targetStrideRot;
            entry.StrideEntity.Transform.UpdateWorldMatrix();
            try
            {
                // Push the entity transform INTO the native btRigidBody (dynamic bodies are
                // physics-driven; setting Transform alone does not move them). Stride API:
                // "Forces an update from the TransformComponent to the Collider.PhysicsWorldTransform."
                rb.UpdatePhysicsTransformation(true);
                rb.LinearVelocity  = SMath.Vector3.Zero;
                rb.AngularVelocity = SMath.Vector3.Zero;
            }
            catch (Exception ex)
            {
                // Body not yet ready for velocity calls — safe to ignore; position is set.
                Log.Debug("[BulletPhysicsBodyService] SyncBodyToExternalPose(vehicle): velocity zero/teleport failed for '{0}' ({1}); position set.",
                    entry.StrideEntity.Name, ex.GetType().Name);
            }

            Log.Info("[BulletPhysicsBodyService] ExternalReposition(vehicle) '{0}': distXZ={1:F3} → Stride ({2:F2},{3:F2},{4:F2}), zeroed velocity.",
                entry.StrideEntity.Name, MathF.Sqrt(distSqFdpXY),
                newPos.X, newPos.Y, newPos.Z);
        }
        // Other kinds (kinematic RigidbodyComponent, unknown) are skipped — they are moved
        // by their own motor each frame and do not need operator-drag teleport.
    }

    /// <summary>
    /// Threshold (metres) for <see cref="SyncBodyToExternalPose"/>: a divergence of more
    /// than this between the mapped <c>SimTransform</c> and the body's Stride position is
    /// treated as an external reposition (operator drag).  Set to 0.01 m — far larger
    /// than per-frame Bullet jitter but smaller than any intentional reposition.
    /// </summary>
    public const float RepositionEpsilonM = 0.01f;

    // ── IPhysicsBodyService: kinematic vehicle motor ──────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// <b>MoveKinematic implementation approach (real box, small floor-skin lift):</b>
    /// Stride 4.2.1.2487 does not expose a direct swept-move API for kinematic bodies.
    /// Our approach:
    /// <list type="number">
    ///   <item>Compute the target position = current position + desiredDelta.</item>
    ///   <item>Sweep the REAL box collider shape from/to the entity's actual transform,
    ///     applying only a tiny <c>SweepFloorSkinM = 0.05 m</c> Y-lift so the box's
    ///     resting bottom (coplanar with the floor at Y=0) does not register a spurious
    ///     floor contact due to floating-point coplanarity.  This is NOT a half-height
    ///     compensation — the box is properly resting ON the floor (entity spawned at
    ///     Y=halfY; visual and collider are aligned).</item>
    ///   <item>On wall hit: clamp the move so the box FACE stops at the wall surface
    ///     (subtract the horizontal half-extent projected onto moveDir and add a skin
    ///     margin).  The contact Y is irrelevant for a horizontal face-stop.</item>
    ///   <item>On no hit: apply the full desiredDelta (smooth drive across floor).</item>
    ///   <item>Apply the full rotation delta (rotation is not swept).</item>
    /// </list>
    /// WHY the small Y-lift is correct:
    /// The vehicle's box bottom is at Y = entityY − halfY.  With the REAL resting spawn
    /// (entity at Y = halfY) the box bottom sits at Y=0, coplanar with the static floor.
    /// Bullet's <c>ShapeSweep</c> may return the floor as the closest hit due to
    /// floating-point near-zero overlap.  A <c>SweepFloorSkinM = 0.05 m</c> lift moves
    /// the swept shape's bottom to Y=0.05 m — just above the floor plane — so only true
    /// wall contacts are returned.
    /// <para><b>Limitation:</b> block-only response (no slide-along-normal).</para>
    /// </remarks>
    public KinematicMoveResult MoveKinematic(
        object           bodyHandle,
        SMath.Vector3    desiredDelta,
        SMath.Quaternion desiredRotDelta)
    {
        if (bodyHandle is SkippedBodyHandle)
            return new KinematicMoveResult(SMath.Vector3.Zero, SMath.Quaternion.Identity);

        if (!_bodies.TryGetValue(bodyHandle, out var entry))
            return new KinematicMoveResult(SMath.Vector3.Zero, SMath.Quaternion.Identity);

        var currentPos = entry.StrideEntity.Transform.Position;
        var currentRot = entry.StrideEntity.Transform.Rotation;
        var targetPos  = currentPos + desiredDelta;

        // ── Swept-move collision check ─────────────────────────────────────
        //
        // ROOT CAUSE FIX (F2 floor-burial + floor-graze):
        // The box model's origin is at its CENTER (empirically confirmed).  The entity is
        // spawned at FDP Z = halfZ (Stride Y = halfY) so the box bottom rests at Y=0.
        // The collider has LocalOffset=Zero so collider and visual are co-located.
        //
        // We sweep the REAL box shape at the REAL entity position, with only a tiny
        // SweepFloorSkinM Y-lift so the resting bottom (coplanar with the floor at Y=0)
        // does not register a spurious floor contact due to floating-point coplanarity.
        // This epsilon lift is NOT a half-height compensation — the box is properly ON
        // the floor (not buried). Only genuine wall contacts block translation.

        // SweepFloorSkinM: small Y-lift (metres) applied to the real box's from/to Y.
        // Raises the swept bottom from Y=0 to Y=SweepFloorSkinM, preventing coplanar
        // floor contacts without introducing any structural bias.
        const float SweepFloorSkinM = 0.05f;

        // SkinM: clearance from the wall contact surface (prevents box face penetration).
        const float SkinM = 0.05f;

        SMath.Vector3 actualDelta = desiredDelta;
        bool          blocked     = false;

        // Use the REAL vehicle box shape (not a purpose-built substitute).
        // BoxHalfExtentsStride stores (halfX, halfY, halfZ) in Stride space.
        var realHalf = entry.BoxHalfExtentsStride;

        // Allocate the real-shape sweep box (same dims as the collision body).
        // Disposed immediately after use to release unmanaged Bullet memory.
        BoxColliderShape? sweepShape = null;
        try
        {
            sweepShape = new BoxColliderShape(
                is2D: false,
                size: new SMath.Vector3(realHalf.X * 2f, realHalf.Y * 2f, realHalf.Z * 2f));

            // Sweep from/to: track the full desired move in X and Z.
            // Apply SweepFloorSkinM Y-lift so the swept bottom clears Y=0 (floor plane).
            // The entity's resting Y is preserved — only the sweep probes slightly above.
            var fromMatrix = SMath.Matrix.RotationQuaternion(currentRot);
            fromMatrix.TranslationVector = new SMath.Vector3(
                currentPos.X,
                currentPos.Y + SweepFloorSkinM,
                currentPos.Z);

            var toMatrix = SMath.Matrix.RotationQuaternion(currentRot);
            toMatrix.TranslationVector = new SMath.Vector3(
                targetPos.X,
                targetPos.Y + SweepFloorSkinM,
                targetPos.Z);

            // Simulation.ShapeSweep returns a HitResult (Stride 4.2.1.2487 verified API).
            // HitResult.Succeeded = true when a contact was found.
            // HitResult.Point = world-space contact point on the obstacle surface.
            // HitResult.Normal = contact normal pointing away from the obstacle.
            //
            // Because the sweep is lifted SweepFloorSkinM above the real resting position,
            // the floor at Y=0 is no longer contacted.  Every hit is a genuine wall.
            var hitResult = _simulation.ShapeSweep(
                sweepShape, fromMatrix, toMatrix,
                CollisionFilterGroups.DefaultFilter,
                CollisionFilterGroupFlags.DefaultFilter,
                hitTriggers: false);

            if (hitResult.Succeeded)
            {
                // Wall hit: block-and-SLIDE response.
                //
                // FACE-STOP: the body CENTER must stop one horizontal half-extent
                // (projected onto moveDir) back from the contact surface so the box FACE
                // (not center) is flush with the wall.  SkinM adds a small gap.
                //
                // SLIDE: after the face-stop clamps the forward component, the tangential
                // component (desiredDelta projected onto the wall plane) is retained so a
                // vehicle steering into a wall scrapes along it rather than freezing.
                // The tangential component is already inside the safe distance from the
                // contact point (the slide is along the wall surface, not into it).
                float desiredLen = desiredDelta.Length();
                if (desiredLen > 1e-6f)
                {
                    // Direction of the desired move.
                    SMath.Vector3 moveDir = desiredDelta / desiredLen;

                    // Contact normal (pointing away from the obstacle, horizontal component only;
                    // the contact Y from the lifted probe is irrelevant for horizontal driving).
                    var wallNormal = new SMath.Vector3(hitResult.Normal.X, 0f, hitResult.Normal.Z);
                    float wallNormalLen = wallNormal.Length();
                    bool  hasHorizontalNormal = wallNormalLen > 1e-6f;

                    // Distance from current entity center to the wall contact surface,
                    // measured along moveDir.  Use currentPos (not the lifted probe position)
                    // so the distance reflects the actual body trajectory.
                    // The contact Y from the lifted probe is offset by SweepFloorSkinM;
                    // for a horizontal sweep moveDir.Y ≈ 0 so the Y component cancels in Dot.
                    SMath.Vector3 toContact = hitResult.Point - currentPos;
                    float distToContact = SMath.Vector3.Dot(toContact, moveDir);

                    // Horizontal half-extent projected onto moveDir (support function of AABB).
                    // Use the REAL model-derived vehicle footprint half-extents.
                    float halfExtentAlongMove =
                        Math.Abs(moveDir.X) * realHalf.X +
                        Math.Abs(moveDir.Y) * realHalf.Y +
                        Math.Abs(moveDir.Z) * realHalf.Z;

                    // Safe advance along moveDir (face-stop).
                    float safeDist = Math.Max(0f, distToContact - halfExtentAlongMove - SkinM);
                    safeDist = Math.Min(safeDist, desiredLen);

                    var safeForward = moveDir * safeDist;

                    // Slide: tangential component of desiredDelta along the wall plane.
                    // tangential = desiredDelta - (desiredDelta · wallNormal̂) * wallNormal̂
                    // This is zero when moveDir is perpendicular to the wall (head-on); nonzero
                    // when approaching at an angle (gives smooth scrape along the wall).
                    SMath.Vector3 slideComponent = SMath.Vector3.Zero;
                    if (hasHorizontalNormal && safeDist < 1e-6f)
                    {
                        // Only apply slide when the forward advance is near-zero (fully blocked).
                        // If safeDist > 0 the vehicle hasn't reached the wall yet and the full
                        // forward component is already included in safeForward.
                        var nHat = wallNormal / wallNormalLen;
                        float normalProj = SMath.Vector3.Dot(desiredDelta, nHat);
                        slideComponent = desiredDelta - nHat * normalProj;
                        // Keep the Y component zero (no vertical slides).
                        slideComponent.Y = 0f;
                    }

                    actualDelta = safeForward + slideComponent;
                }
                else
                {
                    actualDelta = SMath.Vector3.Zero;
                }

                blocked = true;

                Log.Debug(
                    "[BulletPhysicsBodyService] MoveKinematic: '{0}' wall hit (real-box skin-lift sweep) " +
                    "point=({1:F3},{2:F3},{3:F3}) normal=({4:F3},{5:F3},{6:F3}) safeDelta.len={7:F4}.",
                    entry.StrideEntity.Name,
                    hitResult.Point.X, hitResult.Point.Y, hitResult.Point.Z,
                    hitResult.Normal.X, hitResult.Normal.Y, hitResult.Normal.Z,
                    actualDelta.Length());
            }
        }
        catch (Exception ex)
        {
            // Sweep not available in this build or component not yet registered.
            // Fall through to direct move.
            Log.Debug("[BulletPhysicsBodyService] MoveKinematic: sweep failed for '{0}' ({1}); direct move.",
                entry.StrideEntity.Name, ex.GetType().Name);
            actualDelta = desiredDelta;
            blocked     = false;
        }
        finally
        {
            // Dispose the temporary sweep shape to release unmanaged Bullet memory.
            sweepShape?.Dispose();
        }

        // ── Apply the (clamped) position and rotation ──────────────────────
        if (!blocked)
        {
            entry.StrideEntity.Transform.Position = targetPos;
        }
        else
        {
            // Move to the contact boundary (zero if contactPoint == currentPos).
            var sqLen = actualDelta.LengthSquared();
            if (sqLen < 1e-10f)
                actualDelta = SMath.Vector3.Zero;
            else
                entry.StrideEntity.Transform.Position = currentPos + actualDelta;
        }

        // Always apply the rotation delta (not swept).
        SMath.Quaternion.Multiply(ref currentRot, ref desiredRotDelta, out var newRot);
        entry.StrideEntity.Transform.Rotation = newRot;

        return new KinematicMoveResult(actualDelta, desiredRotDelta);
    }

    // ── Bounding-box helper (pure / testable) ─────────────────────────────────

    /// <summary>
    /// Result of <see cref="ComputeBoxParamsFromBoundingBox"/>.
    /// All values are in Stride/local space.
    /// </summary>
    public readonly struct BoxParams
    {
        /// <summary>Half-extents of the box in Stride local space (X=East, Y=Up, Z=North).</summary>
        public SMath.Vector3 HalfExtents { get; }

        /// <summary>
        /// Center of the bounding box in entity-local space.
        /// Used as <c>BoxColliderShape.LocalOffset</c> so the physics body is co-located with the
        /// visual model regardless of where the model origin is within the mesh.
        /// </summary>
        public SMath.Vector3 BoxCenter { get; }

        /// <summary>
        /// Stride Y that the entity must be placed at so the model's bottom face rests exactly on
        /// the floor (Y=0).  Equals <c>-BoundingBox.Minimum.Y</c>.
        ///
        /// <para>
        /// <b>Derivation:</b> entity origin in Stride Y = <c>restingY</c>.
        /// Visual bottom = entity.Y + Minimum.Y = <c>restingY + Minimum.Y = 0</c>.
        /// Physics center = entity.Y + LocalOffset.Y = <c>restingY + BoxCenter.Y</c>.
        /// Physics bottom = physics center − HalfExtents.Y = <c>restingY + BoxCenter.Y − HalfY = 0</c>
        /// (verified: <c>BoxCenter.Y = (Min.Y + Max.Y)/2</c>, <c>HalfY = (Max.Y − Min.Y)/2</c>,
        /// so <c>restingY + (Min.Y+Max.Y)/2 − (Max.Y−Min.Y)/2 = restingY + Min.Y = 0</c>).
        /// </para>
        /// </summary>
        public float RestingStrideY { get; }

        public BoxParams(SMath.Vector3 halfExtents, SMath.Vector3 boxCenter, float restingStrideY)
        {
            HalfExtents   = halfExtents;
            BoxCenter     = boxCenter;
            RestingStrideY = restingStrideY;
        }
    }

    /// <summary>
    /// Computes <see cref="BoxParams"/> from a model bounding box.
    ///
    /// <para>
    /// Returns <c>null</c> when the bounding box is degenerate (zero or NaN extents on any axis),
    /// in which case the caller should fall back to <see cref="ShapeDims"/>.
    /// </para>
    /// </summary>
    /// <param name="bbox">The <c>Stride.Core.Mathematics.BoundingBox</c> in model/local space.</param>
    /// <param name="minClamp">Minimum half-extent on any axis (prevents a zero-size Bullet shape).</param>
    public static BoxParams? ComputeBoxParamsFromBoundingBox(
        SMath.BoundingBox bbox,
        float minClamp = 0.05f)
    {
        var size = bbox.Maximum - bbox.Minimum;

        // Degenerate check: any NaN or non-positive extent.
        if (float.IsNaN(size.X) || float.IsNaN(size.Y) || float.IsNaN(size.Z) ||
            size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
            return null;

        var halfExtents = new SMath.Vector3(
            Math.Max(size.X * 0.5f, minClamp),
            Math.Max(size.Y * 0.5f, minClamp),
            Math.Max(size.Z * 0.5f, minClamp));

        // Center of the bbox in entity-local space.
        // This becomes the LocalOffset of the BoxColliderShape so the collider
        // exactly overlaps the rendered model regardless of where the model origin is.
        var boxCenter = (bbox.Maximum + bbox.Minimum) * 0.5f;

        // Resting Stride Y so the visual bottom (entity.Y + Minimum.Y) is at Y=0.
        float restingStrideY = -bbox.Minimum.Y;

        return new BoxParams(halfExtents, boxCenter, restingStrideY);
    }

    // ── Sentinel ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sentinel handle returned when visual entity was not yet available at CreateBody time.
    /// RemoveBody and all query methods handle this gracefully (no-op / default-return).
    /// </summary>
    private sealed class SkippedBodyHandle
    {
        public override string ToString() => "SkippedBodyHandle";
    }
}

/// <summary>
/// Deferred wrapper for <see cref="BulletPhysicsBodyService"/> that resolves the
/// visual binding dictionary lazily on the first <see cref="IPhysicsBodyService.CreateBody"/> call.
///
/// <para>
/// Solves the chicken-and-egg problem in <c>StrideHrotGame.BootEditorSubsystem</c>:
/// <see cref="BulletPhysicsBodyService"/> needs <see cref="StrideVisualBindingSystem.Visuals"/>,
/// but that dictionary is only created inside <c>EditorStrideSubsystem.Initialize()</c>.
/// By deferring the lookup until the first body creation — which happens during the first
/// <c>Tick()</c> call after <c>Initialize()</c> — the circular dependency is broken.
/// </para>
///
/// <para>
/// The <c>visualsProvider</c> delegate is called once on the first
/// <see cref="IPhysicsBodyService.CreateBody"/> and the resulting service is cached;
/// all subsequent calls use the cached instance.
/// </para>
/// </summary>
public sealed class BulletPhysicsBodyServiceDeferred : IPhysicsBodyService, IBodyRepositionService
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    private readonly Stride.Physics.Simulation _simulation;
    private readonly Func<System.Collections.Generic.IReadOnlyDictionary<Fdp.Core.Entity, StrideVisualReference>> _visualsProvider;

    private BulletPhysicsBodyService? _inner;

    /// <summary>
    /// Constructs the deferred wrapper.
    /// </summary>
    /// <param name="simulation">The running Bullet simulation (non-null, valid in BeginRun).</param>
    /// <param name="visualsProvider">
    /// Delegate that returns the live <see cref="StrideVisualBindingSystem.Visuals"/> dictionary.
    /// Invoked once on the first <see cref="IPhysicsBodyService.CreateBody"/> call and cached.
    /// Must not be null; may return an empty dictionary if called before any visuals exist.
    /// </param>
    public BulletPhysicsBodyServiceDeferred(
        Stride.Physics.Simulation simulation,
        Func<System.Collections.Generic.IReadOnlyDictionary<Fdp.Core.Entity, StrideVisualReference>> visualsProvider)
    {
        _simulation      = simulation      ?? throw new ArgumentNullException(nameof(simulation));
        _visualsProvider = visualsProvider ?? throw new ArgumentNullException(nameof(visualsProvider));
    }

    private BulletPhysicsBodyService Inner
    {
        get
        {
            if (_inner == null)
            {
                var visuals = _visualsProvider();
                _inner = new BulletPhysicsBodyService(_simulation, visuals);
                Log.Info("[BulletPhysicsBodyServiceDeferred] Inner BulletPhysicsBodyService resolved with {0} visual(s).",
                    visuals.Count);
            }
            return _inner;
        }
    }

    /// <inheritdoc/>
    public object CreateBody(Fdp.Core.Entity entity, CollisionShapeKind shapeKind, ShapeDims dims, in SimTransform initialPose)
        => Inner.CreateBody(entity, shapeKind, dims, in initialPose);

    /// <inheritdoc/>
    public void RemoveBody(object bodyHandle)
        => Inner.RemoveBody(bodyHandle);

    /// <inheritdoc/>
    public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity)
        => Inner.SetCharacterVelocity(bodyHandle, velocity);

    /// <inheritdoc/>
    public void Jump(object bodyHandle)
        => Inner.Jump(bodyHandle);

    /// <inheritdoc/>
    public bool IsGrounded(object bodyHandle)
        => Inner.IsGrounded(bodyHandle);

    /// <inheritdoc/>
    /// <remarks>
    /// Forwards external-reposition requests to the inner service ONLY if it already exists
    /// (a body has been created). Never force-creates the inner service here: if no body
    /// exists there is nothing to reposition, so a no-op is correct. Without this forwarding
    /// the live hosted path (which uses this deferred wrapper) would silently drop all
    /// reposition requests, since PhysicsBodyLifecycleSystem downcasts its IPhysicsBodyService
    /// to IBodyRepositionService.
    /// </remarks>
    public void SyncBodyToExternalPose(object bodyHandle, in SimTransform simTf)
    {
        if (_inner != null)
            _inner.SyncBodyToExternalPose(bodyHandle, in simTf);
    }

    /// <inheritdoc/>
    public void RecordReverseSyncedPose(object bodyHandle, in SimTransform simTf)
    {
        if (_inner != null)
            _inner.RecordReverseSyncedPose(bodyHandle, in simTf);
    }

    /// <inheritdoc/>
    public BodyState GetBodyState(object bodyHandle)
        => Inner.GetBodyState(bodyHandle);

    /// <inheritdoc/>
    public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel)
        => Inner.SetLinearVelocityXZ(bodyHandle, strideLinearVel);

    /// <inheritdoc/>
    public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec)
        => Inner.SetYawRate(bodyHandle, strideYawRateRadPerSec);

    /// <inheritdoc/>
    public KinematicMoveResult MoveKinematic(object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
        => Inner.MoveKinematic(bodyHandle, desiredDelta, desiredRotDelta);
}

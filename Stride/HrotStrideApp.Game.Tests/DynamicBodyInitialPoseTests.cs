#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using Xunit;
using SMath = Stride.Core.Mathematics;

namespace HrotStrideApp.Tests;

/// <summary>
/// CPU-only tests for BATCH-S2-G / BATCH-S2-K: dynamic Bullet body must honor initial position
/// and external reposition (operator drag → SimTransform changed).
///
/// <para>
/// BATCH-S2-K changes the divergence DETECTION baseline from "live body pose" to "last
/// reverse-synced pose" so that normal physics motion (live body leads SimTransform by one
/// step) is NOT mistaken for an external drag.  Tests 2 and 3 are updated to the new contract:
/// <list type="bullet">
///   <item>A baseline must be established via <c>RecordReverseSyncedPose</c> before
///     <c>SyncBodyToExternalPose</c> can fire.</item>
///   <item>SimTransform == baseline → NO teleport (muscle-authored motion).</item>
///   <item>SimTransform differs from baseline by &gt; epsilon → teleport (external drag).</item>
/// </list>
/// </para>
///
/// <para>
/// All tests use a <see cref="RecordingFakePhysicsBodyService"/> (headless — no GPU).
/// </para>
/// </summary>
public sealed class DynamicBodyInitialPoseTests : IDisposable
{
    // ── Recording fake: IPhysicsBodyService ──────────────────────────────────

    /// <summary>
    /// Recording fake that captures <c>CreateBody</c> calls with the exact
    /// <c>initialPose</c> argument, and records <c>SyncBodyToExternalPose</c>
    /// calls for the reposition detection assertions.
    ///
    /// BATCH-S2-K: baseline is the last FDP position recorded via
    /// <c>RecordReverseSyncedPose</c>, NOT the live body Stride position.
    /// No baseline → SyncBodyToExternalPose is always a no-op (matching the
    /// <c>HasReverseSyncBaseline</c> guard in the real service).
    /// </summary>
    private sealed class RecordingFakePhysicsBodyService : IPhysicsBodyService, IBodyRepositionService
    {
        public record CreateCall(
            Entity Entity,
            CollisionShapeKind ShapeKind,
            ShapeDims Dims,
            SimTransform InitialPose,
            object Handle);

        public record RepositionCall(
            object Handle,
            SimTransform SimTf);

        public List<CreateCall>     Creates      { get; } = new();
        public List<RepositionCall> Repositions  { get; } = new();

        /// <summary>
        /// Whether a reverse-sync baseline has been established for the last created body.
        /// Mirrors <c>BodyEntry.HasReverseSyncBaseline</c> (BATCH-S2-K).
        /// </summary>
        public bool HasReverseSyncBaseline { get; private set; }

        /// <summary>
        /// The last FDP position recorded by <c>RecordReverseSyncedPose</c>.
        /// This is the baseline that <c>SyncBodyToExternalPose</c> compares against.
        /// </summary>
        public Vector3 LastReverseSyncedFdpPos { get; private set; }

        private int _counter;

        public object CreateBody(
            Entity entity, CollisionShapeKind shapeKind, ShapeDims dims, in SimTransform initialPose)
        {
            var handle = $"FakeBody_{++_counter}";
            Creates.Add(new CreateCall(entity, shapeKind, dims, initialPose, handle));
            // Reset baseline on new body — matches real service semantics.
            HasReverseSyncBaseline = false;
            LastReverseSyncedFdpPos = Vector3.Zero;
            return handle;
        }

        public void RemoveBody(object bodyHandle) { }
        public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;
        public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel) { }
        public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { }
        public KinematicMoveResult MoveKinematic(object bodyHandle, SMath.Vector3 desiredDelta, SMath.Quaternion desiredRotDelta)
            => new(desiredDelta, desiredRotDelta);
        public BodyState GetBodyState(object bodyHandle)
            => new(SMath.Vector3.Zero, SMath.Quaternion.Identity,
                   SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: false);

        // IBodyRepositionService — BATCH-S2-K baseline-driven contract.

        /// <summary>
        /// Records the muscle-authored FDP pose as the reposition baseline.
        /// Mirrors <c>BulletPhysicsBodyService.RecordReverseSyncedPose</c>.
        /// </summary>
        public void RecordReverseSyncedPose(object bodyHandle, in SimTransform simTf)
        {
            LastReverseSyncedFdpPos = simTf.Position;
            HasReverseSyncBaseline  = true;
        }

        /// <summary>
        /// Detects an external write by comparing <paramref name="simTf"/> against the
        /// last reverse-synced baseline (BATCH-S2-K contract).
        /// No baseline → always skip (matching the <c>HasReverseSyncBaseline</c> guard).
        /// </summary>
        public void SyncBodyToExternalPose(object bodyHandle, in SimTransform simTf)
        {
            // BATCH-S2-K: no baseline yet → skip (initial-pose slam owns placement).
            if (!HasReverseSyncBaseline) return;

            // Horizontal FDP (X,Y) divergence vs baseline.
            float dXf = simTf.Position.X - LastReverseSyncedFdpPos.X;
            float dYf = simTf.Position.Y - LastReverseSyncedFdpPos.Y;
            float distSqFdpXY = dXf * dXf + dYf * dYf;

            if (distSqFdpXY > RepositionEpsilonSq)
            {
                // External write detected — record the reposition and update the baseline
                // so subsequent frames don't false-fire.
                Repositions.Add(new RepositionCall(bodyHandle, simTf));
                LastReverseSyncedFdpPos = simTf.Position;
            }
            // Within epsilon → muscle-authored motion → no record.
        }

        // Mirror the real service's epsilon.
        private const float RepositionEpsilonM  = 0.01f;
        private static readonly float RepositionEpsilonSq = RepositionEpsilonM * RepositionEpsilonM;
    }

    // ── Null visual factory ──────────────────────────────────────────────────

    private sealed class NullVisualFactory : IStrideVisualFactory
    {
        private int _counter;
        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t)
            => $"NullModel_{++_counter}";
        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t)
            => $"NullProc_{++_counter}";
        public void UpdatePose(object h, in SimTransform t) { }
        public void Destroy(object h) { }
    }

    // ── Test infrastructure ──────────────────────────────────────────────────

    private readonly EntityRepository                _world;
    private readonly RecordingFakePhysicsBodyService  _fakeService;
    private readonly StrideVisualBindingSystem         _visualSystem;
    private readonly PhysicsBodyLifecycleSystem        _sut;
    private readonly TkbDatabase                      _tkbDb;

    private const long BoxTkbType = 502L;

    public DynamicBodyInitialPoseTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();

        _tkbDb = BuildTkbDb();
        _fakeService  = new RecordingFakePhysicsBodyService();
        _visualSystem = new StrideVisualBindingSystem(new NullVisualFactory(), _tkbDb);
        _sut          = new PhysicsBodyLifecycleSystem(_fakeService, _visualSystem);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db = new TkbDatabase();
        var boxDef = new StrideRenderModelDefDto
        {
            ShapeKind = CollisionShapeKind.OrientedBox,
            BoxHalfX  = 1.0f,
            BoxHalfY  = 0.5f,
            BoxHalfZ  = 2.0f,
        };
        var boxTemplate = new TkbTemplate("BoxUnit", BoxTkbType);
        boxTemplate.AddDescriptor(boxDef);
        db.Register(boxTemplate);
        return db;
    }

    private Entity SpawnOwned(long tkbType, Vector3 pos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = tkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos });
        _world.SetAuthority<SimTransform>(entity, true);
        return entity;
    }

    private void SyncVisuals() => _visualSystem.Sync(_world);
    private void RunSystem() => _sut.Execute(_world, 1f / 60f);

    // ── Test 1: Initial pose honored (Task 1) ────────────────────────────────

    /// <summary>
    /// <b>Task 1 — Initial pose propagation:</b>
    /// When an owned entity with a far-from-origin SimTransform (FDP 668, 0, 427) is processed
    /// by the lifecycle system, <c>CreateBody</c> must receive the correct non-origin initial
    /// pose — specifically, the <c>initialPose.Position</c> must equal the entity's SimTransform.
    ///
    /// <para>
    /// Also verifies that <c>FdpStrideTransform.ToStridePosition</c> maps the FDP position
    /// to the expected Stride position: (668, 427, 0) → Stride (668, 427, 0)
    /// [FDP X=East → Stride X; FDP Z=Up → Stride Y; FDP Y=North → Stride Z].
    /// </para>
    ///
    /// <para>
    /// What this proves about the GPU path: the same <c>initialPose</c> is passed to
    /// <c>BulletPhysicsBodyService.CreateBody</c>, which then sets
    /// <c>strideEntity.Transform.Position</c> from it and captures it in
    /// <c>BodyEntry.InitialStridePos</c> for the first-ready slam.
    /// </para>
    /// </summary>
    [Fact]
    public void CreateBody_ReceivesNonOriginInitialPose_MatchingSimTransform()
    {
        // FDP position far from origin: the hill-attack tank scenario (668, 0, 427).
        var fdpPos = new Vector3(668f, 0f, 427f);
        var entity = SpawnOwned(BoxTkbType, fdpPos);
        SyncVisuals();
        RunSystem();

        // Body must have been created.
        Assert.Single(_fakeService.Creates);
        var call = _fakeService.Creates[0];

        // The exact FDP position must reach CreateBody unchanged.
        Assert.Equal(fdpPos.X, call.InitialPose.Position.X, precision: 3);
        Assert.Equal(fdpPos.Y, call.InitialPose.Position.Y, precision: 3);
        Assert.Equal(fdpPos.Z, call.InitialPose.Position.Z, precision: 3);

        // Cross-check: the Stride projection of this pose is non-origin.
        var stridePos = FdpStrideTransform.ToStridePosition(call.InitialPose.Position);
        Assert.NotEqual(0f, stridePos.X); // should be 668 (East)
        Assert.NotEqual(0f, stridePos.Y); // should be 427 (Stride Y = FDP Z = Up)
        Assert.Equal(668f, stridePos.X, precision: 3);
        Assert.Equal(427f, stridePos.Y, precision: 3); // FDP Z=Up → Stride Y
        Assert.Equal(0f,   stridePos.Z, precision: 3); // FDP Y=North → Stride Z
    }

    // ── Test 2: Reposition teleports the body (Task 2) ───────────────────────

    /// <summary>
    /// <b>Task 2 — External reposition detected and teleported (BATCH-S2-K baseline contract):</b>
    /// After a body is created and a reverse-sync baseline is established via
    /// <c>RecordReverseSyncedPose</c>, a subsequent <c>SyncBodyToExternalPose</c> call with a
    /// SimTransform EQUAL to the baseline must NOT teleport. A call with a SimTransform that
    /// differs by more than epsilon (simulating an operator drag) MUST teleport exactly once.
    ///
    /// <para>
    /// Sequence:
    /// <list type="number">
    ///   <item>Create body at FDP (100, 0, 50).</item>
    ///   <item>Establish baseline: call <c>RecordReverseSyncedPose</c> with the same pose.</item>
    ///   <item>Run a frame — SyncBodyToExternalPose sees SimTransform == baseline → no teleport.</item>
    ///   <item>Externally change SimTransform to FDP (200, 0, 100).</item>
    ///   <item>Run a frame — divergence > epsilon → exactly one reposition recorded.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Fact]
    public void ExternalReposition_DetectedAndTeleported_ToNewSimTransform()
    {
        var initialFdpPos = new Vector3(100f, 0f, 50f);
        var entity = SpawnOwned(BoxTkbType, initialFdpPos);
        SyncVisuals();

        // Frame 1: body created; no baseline yet → SyncBodyToExternalPose is a no-op.
        RunSystem();
        Assert.Single(_fakeService.Creates);

        // Establish the reverse-sync baseline (as BulletReverseSyncSystem would do
        // after writing SimTransform from the physics body state).
        var handle = _fakeService.Creates[0].Handle;
        var baselineTf = new SimTransform { Position = initialFdpPos };
        _fakeService.RecordReverseSyncedPose(handle, in baselineTf);

        // SimTransform still equals the baseline. Run a frame — must NOT teleport.
        RunSystem();
        Assert.Empty(_fakeService.Repositions);

        // External reposition: operator drags the entity to FDP (200, 0, 100).
        var repositionedFdpPos = new Vector3(200f, 0f, 100f);
        ref var simTf = ref _world.GetComponentRW<SimTransform>(entity);
        simTf.Position = repositionedFdpPos;

        // Frame 3: lifecycle system detects the divergence and calls SyncBodyToExternalPose.
        RunSystem();

        Assert.Single(_fakeService.Repositions);
        var repoCall = _fakeService.Repositions[0];
        Assert.Equal(repositionedFdpPos.X, repoCall.SimTf.Position.X, precision: 3);
        Assert.Equal(repositionedFdpPos.Y, repoCall.SimTf.Position.Y, precision: 3);
        Assert.Equal(repositionedFdpPos.Z, repoCall.SimTf.Position.Z, precision: 3);

        // The Stride-space target must be the correctly swizzled repositioned pose.
        var expectedStrideTarget = FdpStrideTransform.ToStridePosition(repositionedFdpPos);
        Assert.Equal(expectedStrideTarget.X, FdpStrideTransform.ToStridePosition(repoCall.SimTf.Position).X, precision: 3);
        Assert.Equal(expectedStrideTarget.Y, FdpStrideTransform.ToStridePosition(repoCall.SimTf.Position).Y, precision: 3);
        Assert.Equal(expectedStrideTarget.Z, FdpStrideTransform.ToStridePosition(repoCall.SimTf.Position).Z, precision: 3);
    }

    // ── Test 3: No false reposition on normal physics motion (Task 2 invariant) ─

    /// <summary>
    /// <b>Task 3 — No false reposition on normal physics motion (BATCH-S2-K baseline contract):</b>
    /// When the reverse-sync keeps recording the body's current pose as the baseline each frame
    /// (simulating BulletReverseSyncSystem writing SimTransform = body pose, then calling
    /// <c>RecordReverseSyncedPose</c>), and SimTransform equals the baseline, the reposition
    /// path must NOT fire across multiple frames.
    ///
    /// <para>
    /// Simulates 10 physics frames where the body "moves" (SimTransform.Position advances each
    /// frame) and the reverse-sync baseline is updated to match each frame's SimTransform,
    /// and asserts that zero <c>SyncBodyToExternalPose</c> reposition calls are recorded.
    /// This directly replicates the BATCH-S2-K fix: the baseline tracks the muscle's output,
    /// so SimTransform == baseline → no divergence → no teleport.
    /// </para>
    /// </summary>
    [Fact]
    public void NormalPhysicsMotion_DoesNotFireReposition()
    {
        var initialFdpPos = new Vector3(50f, 30f, 10f);
        var entity = SpawnOwned(BoxTkbType, initialFdpPos);
        SyncVisuals();

        // Frame 1: body created.
        RunSystem();
        Assert.Single(_fakeService.Creates);
        Assert.Empty(_fakeService.Repositions);

        var handle = _fakeService.Creates[0].Handle;

        // Simulate 10 physics frames of normal motion:
        // Each frame the body moves a small step and the reverse-sync writes SimTransform
        // = new body pose, then records it as the new baseline via RecordReverseSyncedPose.
        // SimTransform == baseline every frame → no divergence → no teleport.
        var currentFdpPos = initialFdpPos;
        for (int i = 1; i <= 10; i++)
        {
            // Body advances by a small FDP step (well above the 0.01 m epsilon per frame).
            currentFdpPos = new Vector3(currentFdpPos.X + 0.5f, currentFdpPos.Y, currentFdpPos.Z);

            // Reverse-sync: write SimTransform = new body pose.
            ref var simTf = ref _world.GetComponentRW<SimTransform>(entity);
            simTf.Position = currentFdpPos;

            // Reverse-sync records the new pose as the baseline (BATCH-S2-K).
            var newTf = new SimTransform { Position = currentFdpPos };
            _fakeService.RecordReverseSyncedPose(handle, in newTf);

            // Lifecycle system sees SimTransform == baseline → must not teleport.
            RunSystem();
        }

        // No reposition should have fired — SimTransform was always equal to the baseline.
        Assert.Empty(_fakeService.Repositions);
    }
}

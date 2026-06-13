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
/// CPU-only tests for BATCH-S2-G: dynamic Bullet body must honor initial position and
/// external reposition (operator drag → SimTransform changed).
///
/// <para>
/// All three tests use a <see cref="RecordingFakePhysicsBodyService"/> (headless — no GPU)
/// that records <c>CreateBody</c> calls including the <c>initialPose</c> argument, and a
/// <see cref="RecordingBodyRepositionService"/> that records <c>SyncBodyToExternalPose</c>
/// calls to let us assert teleport targets and no-teleport scenarios.
/// </para>
///
/// <para>
/// GPU-path assertions (actual native btRigidBody position after UpdateWorldMatrix) are
/// not possible in CPU-only tests.  We assert the CPU-observable invariants:
/// <list type="bullet">
///   <item><b>Task 1</b>: <c>CreateBody</c> receives the non-origin initial pose; the
///     <c>InitialStridePos</c> captured in the body entry matches the spawn SimTransform
///     converted to Stride space.</item>
///   <item><b>Task 2</b>: <c>SyncBodyToExternalPose</c> fires with the repositioned
///     SimTransform as target when the position diverges beyond epsilon; does NOT fire when
///     the position is within epsilon of the baseline.</item>
///   <item><b>Task 3</b>: no false fire on normal physics motion (SimTransform stays within
///     epsilon of the body's current Stride position).</item>
/// </list>
/// </para>
/// </summary>
public sealed class DynamicBodyInitialPoseTests : IDisposable
{
    // ── Recording fake: IPhysicsBodyService ──────────────────────────────────

    /// <summary>
    /// Recording fake that captures <c>CreateBody</c> calls with the exact
    /// <c>initialPose</c> argument, and records <c>SyncBodyToExternalPose</c>
    /// calls for the reposition detection assertions.
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

        public List<CreateCall>    Creates      { get; } = new();
        public List<RepositionCall> Repositions { get; } = new();

        /// <summary>
        /// Current simulated body position (Stride space) — updated by SyncBodyToExternalPose
        /// to model the reverse-sync: after a reposition the body is now at the new pose,
        /// so subsequent frames won't false-fire.
        /// Set by tests to simulate what the body's entity transform is at.
        /// </summary>
        public SMath.Vector3 SimulatedBodyStridePos { get; set; } = SMath.Vector3.Zero;

        private int _counter;

        public object CreateBody(
            Entity entity, CollisionShapeKind shapeKind, ShapeDims dims, in SimTransform initialPose)
        {
            var handle = $"FakeBody_{++_counter}";
            Creates.Add(new CreateCall(entity, shapeKind, dims, initialPose, handle));
            // Seed the simulated body position at the Stride-space projection of initialPose.
            SimulatedBodyStridePos = FdpStrideTransform.ToStridePosition(initialPose.Position);
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
            => new(SimulatedBodyStridePos, SMath.Quaternion.Identity,
                   SMath.Vector3.Zero, SMath.Vector3.Zero, IsKinematic: false);

        // IBodyRepositionService
        public void SyncBodyToExternalPose(object bodyHandle, in SimTransform simTf)
        {
            // Compute target Stride position from SimTransform.
            var targetStridePos = FdpStrideTransform.ToStridePosition(simTf.Position);
            float distSq = SMath.Vector3.DistanceSquared(targetStridePos, SimulatedBodyStridePos);

            if (distSq > RepositionEpsilonSq)
            {
                // Record the reposition call.
                Repositions.Add(new RepositionCall(bodyHandle, simTf));
                // Update simulated body position so subsequent frames don't false-fire.
                SimulatedBodyStridePos = targetStridePos;
            }
            // Within epsilon → normal physics motion → no record.
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
    /// <b>Task 2 — External reposition detected and teleported:</b>
    /// After a body is created and the simulated reverse-sync keeps SimTransform in sync
    /// with the body position (no reposition expected), an external write to SimTransform
    /// (operator drag to a far position) must trigger exactly one <c>SyncBodyToExternalPose</c>
    /// call, with the repositioned pose as target.
    ///
    /// <para>
    /// Sequence:
    /// <list type="number">
    ///   <item>Create body at FDP (100, 0, 50).</item>
    ///   <item>Run one frame — body created, no reposition (SimTransform == body pos).</item>
    ///   <item>Externally change SimTransform.Position to FDP (200, 0, 100) (far repositioned).</item>
    ///   <item>Run one frame — reposition must be detected; exactly one <c>Repositions</c> entry
    ///     with target = FDP (200, 0, 100).</item>
    /// </list>
    /// </para>
    /// </summary>
    [Fact]
    public void ExternalReposition_DetectedAndTeleported_ToNewSimTransform()
    {
        var initialFdpPos = new Vector3(100f, 0f, 50f);
        var entity = SpawnOwned(BoxTkbType, initialFdpPos);
        SyncVisuals();

        // Frame 1: body created; SimTransform == body pos (no reposition expected).
        RunSystem();
        Assert.Single(_fakeService.Creates);

        // At this point SimulatedBodyStridePos is seeded from CreateBody's initialPose.
        // Normal physics motion: the body position and SimTransform are in sync.
        // Run another frame to confirm no false reposition fires.
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
    /// <b>Task 3 — No false reposition on normal physics motion:</b>
    /// When <c>SimTransform</c> stays within epsilon of the body's Stride position
    /// (simulating the reverse-sync writing SimTransform = body pos each frame),
    /// the reposition path must NOT fire across multiple frames.
    ///
    /// <para>
    /// Simulates 10 physics frames where the body "moves" (SimulatedBodyStridePos and
    /// SimTransform.Position advance by small amounts well within the epsilon threshold),
    /// and asserts that zero <c>SyncBodyToExternalPose</c> reposition calls are recorded.
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

        // Simulate 10 physics frames of normal motion:
        // Each frame the body moves a tiny step (well within 0.01 m epsilon) and the
        // reverse-sync updates SimTransform.Position to exactly match the body.
        // This replicates BulletReverseSyncSystem writing SimTransform = body pos.
        for (int i = 1; i <= 10; i++)
        {
            // Body advances by 0.001 m per frame (normal physics step, ~0.01 m/s at 60fps).
            var deltaStride = new SMath.Vector3(0.001f * i, 0f, 0f);
            _fakeService.SimulatedBodyStridePos += deltaStride;

            // Reverse-sync: write SimTransform = body Stride pos converted to FDP.
            // FdpStrideTransform.ToFdpPosition: Stride (x, y, z) → FDP (x, z, y).
            var newFdpPos = FdpStrideTransform.ToFdpPosition(_fakeService.SimulatedBodyStridePos);
            ref var simTf = ref _world.GetComponentRW<SimTransform>(entity);
            simTf.Position = newFdpPos;

            RunSystem();
        }

        // No reposition should have fired — SimTransform was always in sync with body pos.
        Assert.Empty(_fakeService.Repositions);
    }
}

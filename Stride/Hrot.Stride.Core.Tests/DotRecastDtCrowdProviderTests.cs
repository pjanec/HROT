#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Headless tests for <see cref="DotRecastDtCrowdProvider"/> (STR-P2-T3).
///
/// <para>
/// All tests use a real <c>DtCrowd</c> instance over a baked synthetic navmesh —
/// no stubs for the crowd logic itself.
/// </para>
///
/// <para>
/// Coordinate convention throughout: FDP world space — X=East, Y=North, Z=Up.
/// Internally the provider swizzles to crowd space (X=East, Y=altitude, Z=North).
/// </para>
///
/// <para>
/// Scenarios:
/// <list type="bullet">
///   <item>T3-SC1: <see cref="RegisterAgent"/> returns false for a duplicate entity.</item>
///   <item>T3-SC2: <see cref="UnregisterAgent"/> is safe when called for an absent entity.</item>
///   <item>T3-SC3: <see cref="GetAgentVelocity"/> returns a steering velocity pointing
///               toward the target after <see cref="Update"/> steps.</item>
///   <item>T3-SC4: Steering velocity magnitude is ≤ MaxSpeed.</item>
///   <item>T3-SC5: <see cref="TryGetAgentSnapshot"/> reflects position, velocity, target,
///               and desired velocity; returns false for an unregistered entity.</item>
///   <item>T3-SC6: Contract parity with <see cref="FakeDtCrowdProvider"/> on key behaviours.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DotRecastDtCrowdProviderTests : IDisposable
{
    // ── Constants ────────────────────────────────────────────────────────────

    private const float MaxSpeed = 5f;

    // ── Shared navmesh fixture ───────────────────────────────────────────────
    //
    // Flat ground quad: X ∈ [-20,20], Y ∈ [-20,20] (FDP East/North),
    // baked as a crowd-space flat quad (X=East, Y=0=altitude, Z=North).
    // The crowd can steer across the full ±20 m range.

    private static readonly DtNavMesh SharedNavMesh = BakeFlatGround(-20f, 20f, -20f, 20f);

    // ── Per-test state ────────────────────────────────────────────────────────

    private readonly EntityRepository          _world;
    private readonly DotRecastDtCrowdProvider  _sut;

    public DotRecastDtCrowdProviderTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _sut = new DotRecastDtCrowdProvider(SharedNavMesh);
    }

    public void Dispose() => _world.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Default params: 0.3 m radius, 1.8 m height, MaxSpeed 5 m/s, MaxAcceleration 20 m/s².
    /// </summary>
    private static CrowdAgentParams DefaultParams() => new CrowdAgentParams
    {
        Radius          = 0.3f,
        Height          = 1.8f,
        MaxSpeed        = MaxSpeed,
        MaxAcceleration = 20f,
        SeparationWeight = 2,
    };

    /// <summary>
    /// Creates an entity with a <see cref="SimTransform"/> at FDP position <paramref name="fdpPos"/>.
    /// </summary>
    private Entity CreateEntityAt(Vector3 fdpPos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new SimTransform { Position = fdpPos });
        return entity;
    }

    /// <summary>Minimal fake simulation view backed by the shared EntityRepository.</summary>
    private ISimulationView View => _world;

    /// <summary>
    /// Steps the crowd <paramref name="steps"/> times with dt=0.1 s/step.
    /// </summary>
    private void StepCrowd(int steps = 1, float dt = 0.1f)
    {
        for (int i = 0; i < steps; i++)
            _sut.Update(dt, View);
    }

    // ── T3-SC1: duplicate registration returns false ──────────────────────────

    /// <summary>
    /// <see cref="DotRecastDtCrowdProvider.RegisterAgent"/> returns false when the
    /// same entity is registered twice.
    /// </summary>
    [Fact]
    public void RegisterAgent_DuplicateEntity_ReturnsFalse()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        bool first  = _sut.RegisterAgent(entity, DefaultParams());
        bool second = _sut.RegisterAgent(entity, DefaultParams());

        Assert.True(first,   "First registration must return true.");
        Assert.False(second, "Second registration for the same entity must return false.");
    }

    // ── T3-SC2: unregister absent entity is safe ──────────────────────────────

    /// <summary>
    /// <see cref="DotRecastDtCrowdProvider.UnregisterAgent"/> does not throw when the
    /// entity was never registered.
    /// </summary>
    [Fact]
    public void UnregisterAgent_NeverRegistered_DoesNotThrow()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        // Should not throw:
        _sut.UnregisterAgent(entity);
    }

    /// <summary>
    /// Registering an agent, unregistering it, then registering again succeeds.
    /// </summary>
    [Fact]
    public void RegisterAfterUnregister_Succeeds()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        _sut.RegisterAgent(entity, DefaultParams());
        _sut.UnregisterAgent(entity);
        bool reregistered = _sut.RegisterAgent(entity, DefaultParams());
        Assert.True(reregistered, "Re-registration after unregister must return true.");
    }

    // ── T3-SC3: steering velocity points toward target ────────────────────────

    /// <summary>
    /// After Update steps, <see cref="DotRecastDtCrowdProvider.GetAgentVelocity"/>
    /// returns a non-zero velocity whose direction has a positive component toward the target.
    ///
    /// <para>
    /// Setup: agent at FDP position (0,0,0) with target at (10,0,0) (due East).
    /// After several Update ticks the velocity X component must be &gt; 0.
    /// </para>
    /// </summary>
    [Fact]
    public void GetAgentVelocity_AfterUpdate_PointsTowardTarget()
    {
        // FDP: agent at (0,0,0), target at (10,0,0) — due East (positive X).
        var entity = CreateEntityAt(new Vector3(0f, 0f, 0f));
        _sut.RegisterAgent(entity, DefaultParams());
        _sut.SetAgentTarget(entity, new Vector3(10f, 0f, 0f));

        // Update several steps (DtCrowd needs a few ticks to compute steering).
        StepCrowd(steps: 10, dt: 0.1f);

        var velocity = _sut.GetAgentVelocity(entity);

        // Direction: velocity must point toward the target (positive East / +X in FDP).
        Assert.True(velocity.X > 0f,
            $"Velocity must have a positive East (X) component toward target; got {velocity}");
    }

    /// <summary>
    /// Same as above but target is due North (positive Y in FDP space).
    /// After Update steps, the Y component must be positive.
    /// </summary>
    [Fact]
    public void GetAgentVelocity_AfterUpdate_PointsNorth_WhenTargetIsNorth()
    {
        // FDP: agent at (0,0,0), target at (0,10,0) — due North (positive Y in FDP).
        var entity = CreateEntityAt(new Vector3(0f, 0f, 0f));
        _sut.RegisterAgent(entity, DefaultParams());
        _sut.SetAgentTarget(entity, new Vector3(0f, 10f, 0f));

        StepCrowd(steps: 10, dt: 0.1f);

        var velocity = _sut.GetAgentVelocity(entity);

        Assert.True(velocity.Y > 0f,
            $"Velocity must have a positive North (Y) component toward target; got {velocity}");
    }

    // ── T3-SC4: velocity magnitude ≤ MaxSpeed ────────────────────────────────

    /// <summary>
    /// The magnitude of the steering velocity returned by
    /// <see cref="DotRecastDtCrowdProvider.GetAgentVelocity"/> must not exceed
    /// the agent's <see cref="CrowdAgentParams.MaxSpeed"/> (5 m/s in this test).
    /// </summary>
    [Fact]
    public void GetAgentVelocity_MagnitudeAtMostMaxSpeed()
    {
        var entity = CreateEntityAt(new Vector3(0f, 0f, 0f));
        _sut.RegisterAgent(entity, DefaultParams());
        _sut.SetAgentTarget(entity, new Vector3(15f, 0f, 0f));

        // Run many steps to let the crowd reach steady state.
        StepCrowd(steps: 20, dt: 0.1f);

        var velocity = _sut.GetAgentVelocity(entity);
        float magnitude = velocity.Length();

        Assert.True(magnitude <= MaxSpeed + 0.01f,  // tiny tolerance for floating-point
            $"Velocity magnitude {magnitude:F3} must be ≤ MaxSpeed ({MaxSpeed}).");
    }

    // ── T3-SC5: TryGetAgentSnapshot ──────────────────────────────────────────

    /// <summary>
    /// <see cref="DotRecastDtCrowdProvider.TryGetAgentSnapshot"/> returns false for
    /// an entity that is not registered.
    /// </summary>
    [Fact]
    public void TryGetAgentSnapshot_UnregisteredEntity_ReturnsFalse()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        bool found = _sut.TryGetAgentSnapshot(entity, out _);
        Assert.False(found);
    }

    /// <summary>
    /// After registering, setting a target and stepping the crowd,
    /// <see cref="DotRecastDtCrowdProvider.TryGetAgentSnapshot"/> returns true with
    /// a non-zero desired velocity.
    /// </summary>
    [Fact]
    public void TryGetAgentSnapshot_RegisteredWithTarget_ReturnsValidState()
    {
        var entity = CreateEntityAt(new Vector3(0f, 0f, 0f));
        _sut.RegisterAgent(entity, DefaultParams());
        _sut.SetAgentTarget(entity, new Vector3(10f, 0f, 0f));

        StepCrowd(steps: 10, dt: 0.1f);

        bool found = _sut.TryGetAgentSnapshot(entity, out var snap);

        Assert.True(found, "TryGetAgentSnapshot must return true for a registered entity.");
        // Desired velocity should point toward the target.
        Assert.True(snap.DesiredVelocity.X > 0f || snap.Velocity.X > 0f,
            $"Snapshot must reflect a non-zero velocity toward the target; " +
            $"dvel={snap.DesiredVelocity}, vel={snap.Velocity}");
        // Target in snapshot must match what was set (FDP space).
        Assert.Equal(10f, snap.Target.X, precision: 3);
        Assert.Equal(0f,  snap.Target.Y, precision: 3);
    }

    // ── T3-SC6: contract parity with FakeDtCrowdProvider ─────────────────────

    /// <summary>
    /// Both providers return false for a duplicate <see cref="RegisterAgent"/> call.
    /// </summary>
    [Fact]
    public void ContractParity_RegisterDuplicate_BothReturnFalse()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        var fake = new FakeDtCrowdProvider();

        bool realFirst   = _sut.RegisterAgent(entity, DefaultParams());
        bool fakeFirst   = fake.RegisterAgent(entity, DefaultParams());

        bool realSecond  = _sut.RegisterAgent(entity, DefaultParams());
        bool fakeSecond  = fake.RegisterAgent(entity, DefaultParams());

        Assert.True(realFirst  == fakeFirst,  "First registration return value must match fake.");
        Assert.True(realSecond == fakeSecond, "Duplicate registration return value must match fake.");
    }

    /// <summary>
    /// Both providers return <see cref="Vector3.Zero"/> for an unregistered entity.
    /// </summary>
    [Fact]
    public void ContractParity_GetVelocityForUnregistered_BothReturnZero()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        var fake = new FakeDtCrowdProvider();

        var realVel = _sut.GetAgentVelocity(entity);
        var fakeVel = fake.GetAgentVelocity(entity);

        Assert.Equal(Vector3.Zero, realVel);
        Assert.Equal(Vector3.Zero, fakeVel);
    }

    /// <summary>
    /// Both providers return false from <see cref="TryGetAgentSnapshot"/> for an
    /// unregistered entity.
    /// </summary>
    [Fact]
    public void ContractParity_TryGetSnapshotForUnregistered_BothReturnFalse()
    {
        var entity = CreateEntityAt(Vector3.Zero);
        var fake = new FakeDtCrowdProvider();

        bool realFound = _sut.TryGetAgentSnapshot(entity, out _);
        bool fakeFound = fake.TryGetAgentSnapshot(entity, out _);

        Assert.False(realFound);
        Assert.False(fakeFound);
    }

    // ── Navmesh baker helper ──────────────────────────────────────────────────

    /// <summary>
    /// Bakes a flat ground quad in crowd space (X=East, Y=0=altitude, Z=North)
    /// covering [xMin..xMax] in X and [zMin..zMax] in Z.
    ///
    /// FDP coordinates (X=East, Y=North, Z=Up) are swizzled before baking:
    /// the FDP range [xMin..xMax] × [yMin..yMax] at Z=0 maps to crowd space
    /// [xMin..xMax] × [yMin..yMax] at Y=0, which is a valid flat navmesh surface.
    ///
    /// Note: in the test helpers below, the flat ground is authored directly in
    /// crowd space (X=East, Y=0=altitude, Z=North) — no swizzle needed because
    /// the synthetic geometry is purpose-built for the navmesh.
    /// </summary>
    private static DtNavMesh BakeFlatGround(
        float xMin, float xMax, float zMin, float zMax)
    {
        // Crowd space: X=East, Y=altitude(0=ground), Z=North.
        // CCW winding when viewed from above (+Y) = upward normal = walkable.
        float[] verts =
        {
            xMin, 0f, zMin,  // 0
            xMax, 0f, zMin,  // 1
            xMax, 0f, zMax,  // 2
            xMin, 0f, zMax,  // 3
        };
        int[] indices =
        {
            0, 2, 1,  // CCW triangle 1 (upward normal)
            0, 3, 2,  // CCW triangle 2
        };

        var baker    = new StrideNavmeshBaker();
        var meshes   = baker.Bake(verts, indices, NavLayerMask.Infantry);

        Assert.True(meshes.ContainsKey(NavLayerMask.Infantry),
            "Infantry navmesh must bake from the flat ground quad.");

        return meshes[NavLayerMask.Infantry];
    }
}

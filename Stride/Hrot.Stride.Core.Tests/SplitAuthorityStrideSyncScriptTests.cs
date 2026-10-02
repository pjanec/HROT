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

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="SplitAuthorityStrideSyncScript"/> (STR-P1-T6).
///
/// <para>
/// Scenarios:
/// <list type="bullet">
///   <item>T6-SC1: Owned entity is NOT forward-synced (Pass B skips it).</item>
///   <item>T6-SC2: Non-owned entity's visual pose follows <see cref="SimTransform"/> via
///     <see cref="FdpStrideTransform"/>.</item>
///   <item>T6-SC3: Pass A reconciliation creates/tears down visuals on appear/disappear.</item>
///   <item>T6-SC4: Owned entity manual <see cref="SimTransform"/> change does not propagate
///     through Pass B.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SplitAuthorityStrideSyncScriptTests : IDisposable
{
    // ── Recording fake factory ─────────────────────────────────────────────────

    private sealed class RecordingFakeFactory : IStrideVisualFactory
    {
        public record UpdatePoseCall(object Handle, SimTransform Pose);
        public record DestroyCall(object Handle);

        public List<UpdatePoseCall>  UpdateCalls  { get; } = new();
        public List<DestroyCall>     DestroyCalls { get; } = new();

        private int _counter;

        public object CreateModelVisual(string m, string s, float sc, Vector3 o, in SimTransform t)
            => $"Model_{++_counter}";

        public object CreateProceduralVisual(CollisionShapeKind k, ShapeDims d, float sc, Vector3 o, in SimTransform t)
            => $"Proc_{++_counter}";

        public void UpdatePose(object visualHandle, in SimTransform pose)
            => UpdateCalls.Add(new UpdatePoseCall(visualHandle, pose));

        public void Destroy(object visualHandle)
            => DestroyCalls.Add(new DestroyCall(visualHandle));
    }

    // ── Test infrastructure ────────────────────────────────────────────────────

    private const long CapsuleTkbType = 801L;

    private readonly EntityRepository             _world;
    private readonly RecordingFakeFactory         _fakeFactory;
    private readonly StrideVisualBindingSystem     _visualSystem;
    private readonly SplitAuthorityStrideSyncScript _sut;

    public SplitAuthorityStrideSyncScriptTests()
    {
        _world = new EntityRepository();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<SimVelocity>();
        _world.RegisterComponent<TkbIdentity>();

        var tkbDb = BuildTkbDb();
        _fakeFactory  = new RecordingFakeFactory();
        _visualSystem = new StrideVisualBindingSystem(_fakeFactory, tkbDb);
        _sut          = new SplitAuthorityStrideSyncScript(_visualSystem, _fakeFactory);
    }

    public void Dispose() => _world.Dispose();

    private static TkbDatabase BuildTkbDb()
    {
        var db   = new TkbDatabase();
        var def  = new StrideRenderModelDefDto
        {
            ShapeKind   = CollisionShapeKind.Capsule,
            ShapeRadius = 0.3f,
            ShapeHeight = 1.8f,
        };
        var tmpl = new TkbTemplate("TestUnit", CapsuleTkbType);
        tmpl.AddDescriptor(def);
        db.Register(tmpl);
        return db;
    }

    private Entity SpawnOwned(Vector3 pos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = CapsuleTkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new SimVelocity());
        _world.SetAuthority<SimTransform>(entity, true);
        return entity;
    }

    private Entity SpawnNonOwned(Vector3 pos)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TkbIdentity { TkbType = CapsuleTkbType });
        _world.AddComponent(entity, new SimTransform { Position = pos, Rotation = Quaternion.Identity });
        _world.AddComponent(entity, new SimVelocity());
        // DO NOT set authority — entity is non-owned (ghost / replay)
        return entity;
    }

    private void Run() => _sut.Sync(_world);

    // ── T6-SC1: owned entity is NOT forward-synced by Pass B ──────────────────

    /// <summary>
    /// An owned entity (with local <see cref="SimTransform"/> authority) must NOT
    /// have its visual pose updated by Pass B of the split-authority sync.
    ///
    /// After Pass A creates the visual, the test changes the entity's
    /// <see cref="SimTransform"/> directly, runs the sync, and asserts that
    /// <c>UpdatePose</c> was NOT called for the owned entity's handle.
    /// The owned entity's Stride body is physics-driven — a forward-sync would
    /// fight Bullet and cause jitter.
    /// </summary>
    [Fact]
    public void OwnedEntity_PassB_DoesNotCallUpdatePose()
    {
        var entity = SpawnOwned(new Vector3(1f, 2f, 3f));

        // Pass A: create the visual for the owned entity.
        Run();
        _fakeFactory.UpdateCalls.Clear();

        // Mutate the SimTransform — Pass B must NOT propagate this to the visual.
        _world.SetComponent(entity, new SimTransform
        {
            Position = new Vector3(99f, 99f, 99f),
            Rotation = Quaternion.Identity,
        });

        Run();

        // Owned entity: UpdatePose must NOT have been called.
        Assert.Empty(_fakeFactory.UpdateCalls);
    }

    // ── T6-SC2: non-owned entity's visual pose follows SimTransform ───────────

    /// <summary>
    /// A non-owned entity (ghost / replay) must have its visual pose updated
    /// by Pass B from <see cref="SimTransform"/> using the FDP coordinate convention.
    ///
    /// The factory's <c>UpdatePose</c> must be called with the exact
    /// <see cref="SimTransform"/> from the ECS (the factory then applies
    /// <see cref="FdpStrideTransform"/> when writing to the Stride entity).
    /// </summary>
    [Fact]
    public void NonOwnedEntity_PassB_CallsUpdatePoseWithCorrectSimTransform()
    {
        var pos = new Vector3(10f, 20f, 30f);
        var entity = SpawnNonOwned(pos);

        // Pass A: create the visual.
        Run();
        _fakeFactory.UpdateCalls.Clear();

        // Update the SimTransform to a known value.
        var newTf = new SimTransform
        {
            Position = new Vector3(5f, 6f, 7f),
            Rotation = Quaternion.Identity,
        };
        _world.SetComponent(entity, newTf);

        Run();

        // Pass B must have called UpdatePose for the non-owned entity.
        Assert.Single(_fakeFactory.UpdateCalls);
        var call = _fakeFactory.UpdateCalls[0];

        // The SimTransform passed to UpdatePose must match the entity's current transform.
        Assert.Equal(5f, call.Pose.Position.X, precision: 5);
        Assert.Equal(6f, call.Pose.Position.Y, precision: 5);
        Assert.Equal(7f, call.Pose.Position.Z, precision: 5);
    }

    /// <summary>
    /// Verifies that <c>UpdatePose</c> is called on the CORRECT visual handle
    /// (the one created for this specific non-owned entity by Pass A).
    /// </summary>
    [Fact]
    public void NonOwnedEntity_PassB_UpdatePoseCalledOnCorrectHandle()
    {
        var entity = SpawnNonOwned(Vector3.Zero);

        // First Sync: Pass A creates the visual — record which handle was created.
        Run();

        // The StrideVisualBindingSystem stores visuals in Visuals dict.
        Assert.True(_visualSystem.Visuals.ContainsKey(entity),
            "Pass A must have created a visual for the non-owned entity.");
        var expectedHandle = _visualSystem.Visuals[entity].VisualHandle;

        _fakeFactory.UpdateCalls.Clear();

        // Second Sync: Pass B should call UpdatePose with the correct handle.
        Run();

        Assert.Single(_fakeFactory.UpdateCalls);
        Assert.Equal(expectedHandle, _fakeFactory.UpdateCalls[0].Handle);
    }

    // ── T6-SC3: appear/disappear reconciliation (Pass A) ──────────────────────

    /// <summary>
    /// When an entity with <see cref="TkbIdentity"/> + <see cref="SimTransform"/>
    /// appears (first Sync call), Pass A must create a Stride visual for it.
    /// </summary>
    [Fact]
    public void PassA_EntityAppear_CreatesVisual()
    {
        var entity = SpawnOwned(Vector3.Zero);

        // Before sync: no visual.
        Assert.Empty(_visualSystem.Visuals);

        Run();

        // After sync: visual created.
        Assert.True(_visualSystem.Visuals.ContainsKey(entity),
            "Pass A must create a visual when an entity appears.");
    }

    /// <summary>
    /// When an entity dies (is destroyed from the world), Pass A must tear down
    /// its Stride visual on the next Sync call.
    /// </summary>
    [Fact]
    public void PassA_EntityDisappear_TeardownVisual()
    {
        var entity = SpawnOwned(Vector3.Zero);

        // First Sync: visual created.
        Run();
        Assert.True(_visualSystem.Visuals.ContainsKey(entity));

        // Destroy the entity.
        _world.DestroyEntity(entity);

        // Second Sync: visual must be torn down.
        Run();

        Assert.False(_visualSystem.Visuals.ContainsKey(entity),
            "Pass A must destroy the visual when the entity is removed.");
        Assert.Single(_fakeFactory.DestroyCalls);
    }

    // ── T6-SC4: owned entity's manual SimTransform change does not propagate ──

    /// <summary>
    /// An owned entity's <see cref="SimTransform"/> change must NOT be propagated
    /// by Pass B even across multiple frames. This is the critical guarantee:
    /// the Stride body is physics-driven, not FDP-state-driven.
    /// </summary>
    [Fact]
    public void OwnedEntity_MultipleFrames_SimTransformChangesNeverForwardSynced()
    {
        var entity = SpawnOwned(Vector3.Zero);

        // Run 5 frames, each time mutating the SimTransform.
        for (int i = 0; i < 5; i++)
        {
            _world.SetComponent(entity, new SimTransform
            {
                Position = new Vector3(i * 10f, 0f, 0f),
                Rotation = Quaternion.Identity,
            });
            _fakeFactory.UpdateCalls.Clear();
            Run();

            // Must never call UpdatePose for the owned entity.
            Assert.Empty(_fakeFactory.UpdateCalls);
        }
    }

    // ── T6-SC5: mix of owned + non-owned ─────────────────────────────────────

    /// <summary>
    /// When both owned and non-owned entities are present, only the non-owned entity
    /// gets <c>UpdatePose</c> called. The owned entity is never forward-synced.
    /// </summary>
    [Fact]
    public void MixedEntities_OnlyNonOwnedForwardSynced()
    {
        var ownedEntity    = SpawnOwned(new Vector3(1f, 0f, 0f));
        var nonOwnedEntity = SpawnNonOwned(new Vector3(2f, 0f, 0f));

        // First Sync: Pass A creates visuals for both.
        Run();
        _fakeFactory.UpdateCalls.Clear();

        // Mutate both.
        _world.SetComponent(ownedEntity,    new SimTransform { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity });
        _world.SetComponent(nonOwnedEntity, new SimTransform { Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity });

        Run();

        // Exactly one UpdatePose call — for the non-owned entity only.
        Assert.Single(_fakeFactory.UpdateCalls);

        // Verify it is for the non-owned entity's handle.
        var nonOwnedHandle = _visualSystem.Visuals[nonOwnedEntity].VisualHandle;
        Assert.Equal(nonOwnedHandle, _fakeFactory.UpdateCalls[0].Handle);

        // Verify the pose matches the non-owned entity's SimTransform.
        Assert.Equal(20f, _fakeFactory.UpdateCalls[0].Pose.Position.X, precision: 5);
    }
}

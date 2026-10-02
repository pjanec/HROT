#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.Stride.Animation;
using Hrot.Stride.Core;
using Xunit;
using Entity = Fdp.Core.Entity;

namespace HrotStrideApp.Tests;

/// <summary>
/// BATCH-16 Fix A: tests for the live-animation glue <i>wiring decision</i>
/// (<see cref="MannequinAnimationBinder"/>). The GPU-bound clip-load + builder-attach step is
/// behind <see cref="IMannequinBlendTreeInstaller"/>, so here we drive the binder with a fake
/// installer and assert the decision logic: a mannequin that has both a visual and a backend
/// handle gets a builder installed exactly once; when its visual disappears (death) the builder is
/// released. This is the connection that was missing on the real GPU run (D5–D7 moved but did not
/// animate). The actual skeletal playback is GPU-verified by the human.
/// </summary>
public sealed class MannequinAnimationBinderTests : IDisposable
{
    private const long TkbInfantrySoldier = 2002L;

    /// <summary>Records install/release calls; returns a unique token per install.</summary>
    private sealed class FakeInstaller : IMannequinBlendTreeInstaller
    {
        public int InstallCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public readonly List<AnimationBackendHandle> Installed = new();
        public readonly HashSet<object> Live = new();
        public bool ReturnNull; // simulate "visual has no AnimationComponent"

        public object? Install(AnimationBackendHandle handle, object visualHandle, StrideAnimationBackend backend)
        {
            if (ReturnNull) return null;
            InstallCount++;
            Installed.Add(handle);
            var token = new object();
            Live.Add(token);
            return token;
        }

        public void Release(object token)
        {
            ReleaseCount++;
            Live.Remove(token);
        }
    }

    /// <summary>Records created visuals; returns the FDP-entity-agnostic handle object.</summary>
    private sealed class RecordingFakeFactory : IStrideVisualFactory
    {
        private int _counter;
        public object CreateModelVisual(string modelRef, string skeletonRef, float scale, Vector3 offsetFdp, in SimTransform initialPose)
            => $"M_{++_counter}";
        public object CreateProceduralVisual(CollisionShapeKind kind, ShapeDims dims, float scale, Vector3 offsetFdp, in SimTransform initialPose)
            => $"P_{++_counter}";
        public void UpdatePose(object handle, in SimTransform pose) { }
        public void Destroy(object handle) { }
    }

    private readonly List<EditorStrideSubsystem> _suts = new();
    public void Dispose() { foreach (var s in _suts) s.Dispose(); }

    private (EditorStrideSubsystem sut, FakeInstaller installer, MannequinAnimationBinder binder) CreateSut(bool returnNull = false)
    {
        var sut = new EditorStrideSubsystem();
        sut.Initialize(new RecordingFakeFactory());
        _suts.Add(sut);

        var installer = new FakeInstaller { ReturnNull = returnNull };
        var binder = new MannequinAnimationBinder(
            sut.AnimationBackend, sut.AnimationBridge, sut.VisualBindingSystem!, installer);
        return (sut, installer, binder);
    }

    private static void SpawnMannequin(EditorStrideSubsystem sut)
    {
        sut.ScenarioSource.Enqueue(new Hrot.Core.Network.EntityCreationRequest
        {
            RequestId = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType = TkbInfantrySoldier,
            InitialComponents = new List<object>
            {
                new SimTransform { Position = new Vector3(0, 5, 0), Rotation = Quaternion.Identity },
                new SimVelocity(),
                new TkbIdentity { TkbType = TkbInfantrySoldier },
            },
        });
    }

    private static Entity FindMannequin(EntityRepository world)
    {
        foreach (var e in world.Query().With<SimTransform>().With<TkbIdentity>().Build())
            if (world.GetComponentRO<TkbIdentity>(e).TkbType == TkbInfantrySoldier)
                return e;
        return default;
    }

    // ── Wiring decision ──────────────────────────────────────────────────────

    [Fact]
    public void Reconcile_NoMannequins_InstallsNothing()
    {
        var (sut, installer, binder) = CreateSut();
        binder.Reconcile();
        Assert.Equal(0, installer.InstallCount);
        Assert.Equal(0, binder.BoundCount);
    }

    [Fact]
    public void Reconcile_RegisteredMannequinWithVisual_InstallsBuilderExactlyOnce()
    {
        var (sut, installer, binder) = CreateSut();
        SpawnMannequin(sut);

        // Tick the subsystem so the spawn materializes, the bridge registers the mannequin with
        // the backend, and the visual factory creates its visual.
        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);

        var mannequin = FindMannequin(sut.World);
        Assert.NotEqual(default, mannequin);
        Assert.True(sut.AnimationBridge.TryGetHandle(mannequin, out var handle));
        Assert.True(sut.VisualBindingSystem!.Visuals.ContainsKey(mannequin));

        // First reconcile installs the builder.
        binder.Reconcile();
        Assert.Equal(1, installer.InstallCount);
        Assert.Equal(1, binder.BoundCount);
        Assert.Contains(handle, installer.Installed);

        // Idempotent: subsequent reconciles do NOT re-install (no duplicate builders/evaluators).
        binder.Reconcile();
        binder.Reconcile();
        Assert.Equal(1, installer.InstallCount);
        Assert.Equal(1, binder.BoundCount);
    }

    [Fact]
    public void Reconcile_MannequinDies_ReleasesBuilder()
    {
        var (sut, installer, binder) = CreateSut();
        SpawnMannequin(sut);
        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);

        binder.Reconcile();
        Assert.Equal(1, binder.BoundCount);

        var mannequin = FindMannequin(sut.World);
        sut.World.DestroyEntity(mannequin);
        // Tick so the visual binding tears down the visual and the bridge unregisters the handle.
        for (int i = 0; i < 3; i++) sut.Tick(1f / 60f);

        binder.Reconcile();
        Assert.Equal(1, installer.ReleaseCount);
        Assert.Equal(0, binder.BoundCount);
        Assert.Empty(installer.Live); // no leaked builder tokens.
    }

    [Fact]
    public void Reconcile_VisualWithoutAnimationComponent_DoesNotBind()
    {
        // Installer returns null (visual has no AnimationComponent) → binder must not track it,
        // and must keep retrying without leaking.
        var (sut, installer, binder) = CreateSut(returnNull: true);
        SpawnMannequin(sut);
        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);

        binder.Reconcile();
        Assert.Equal(0, binder.BoundCount);
        Assert.Equal(0, installer.ReleaseCount);
    }

    [Fact]
    public void ReleaseAll_ReleasesEveryBoundBuilder()
    {
        var (sut, installer, binder) = CreateSut();
        SpawnMannequin(sut);
        SpawnMannequin(sut);
        for (int i = 0; i < 8; i++) sut.Tick(1f / 60f);

        binder.Reconcile();
        Assert.True(binder.BoundCount >= 1);
        int bound = binder.BoundCount;

        binder.ReleaseAll();
        Assert.Equal(bound, installer.ReleaseCount);
        Assert.Equal(0, binder.BoundCount);
        Assert.Empty(installer.Live);
    }

    [Fact]
    public void Subsystem_WithInstaller_CreatesBinder_AndAnimatesThroughTick()
    {
        // End-to-end: the subsystem itself creates the binder when both a visual factory AND an
        // installer are supplied, and drives it from Tick() so a spawned mannequin gets bound.
        var sut = new EditorStrideSubsystem();
        var installer = new FakeInstaller();
        sut.Initialize(new RecordingFakeFactory(), installer);
        _suts.Add(sut);

        Assert.NotNull(sut.AnimationBinder);

        SpawnMannequin(sut);
        for (int i = 0; i < 8; i++) sut.Tick(1f / 60f);

        Assert.True(installer.InstallCount >= 1, "the subsystem's Tick must reconcile + bind the mannequin.");
        Assert.True(sut.AnimationBinder!.BoundCount >= 1);
    }

    [Fact]
    public void Subsystem_WithoutInstaller_DoesNotCreateBinder()
    {
        var sut = new EditorStrideSubsystem();
        sut.Initialize(new RecordingFakeFactory()); // no installer
        _suts.Add(sut);
        Assert.Null(sut.AnimationBinder);
    }

    // ── ISSUE-2 FIX: Blender-not-ready retry ────────────────────────────────

    /// <summary>
    /// ISSUE-2 fix: when the installer returns null on first call (simulating AnimationComponent
    /// .Blender not yet initialised by Stride's AnimationProcessor), the binder must:
    ///   1. NOT add the entity to _bound (BoundCount stays 0).
    ///   2. Retry on the NEXT Reconcile() call (when installer succeeds) and bind correctly.
    /// This mirrors the real behaviour: on spawn-frame the Blender is null → skip; on
    /// frame 2+ the Blender is initialised → bind immediately.
    /// </summary>
    [Fact]
    public void Reconcile_InstallerReturnsNullOnFirstCall_RetrySucceedsOnSecondCall()
    {
        var (sut, installer, binder) = CreateSut();
        SpawnMannequin(sut);
        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);

        // Ensure the entity has a visual + backend handle (precondition for bind attempt).
        var mannequin = FindMannequin(sut.World);
        Assert.NotEqual(default, mannequin);
        Assert.True(sut.AnimationBridge.TryGetHandle(mannequin, out _));
        Assert.True(sut.VisualBindingSystem!.Visuals.ContainsKey(mannequin));

        // First Reconcile: installer returns null (Blender not ready yet).
        installer.ReturnNull = true;
        binder.Reconcile();
        Assert.Equal(0, binder.BoundCount); // not bound yet

        // Second Reconcile: installer returns a real token (Blender now ready).
        installer.ReturnNull = false;
        binder.Reconcile();
        Assert.Equal(1, installer.InstallCount); // Install was called on second reconcile
        Assert.Equal(1, binder.BoundCount);      // entity is now bound
    }

    /// <summary>
    /// ISSUE-2 fix: verifies that MULTIPLE retries (installer returns null many times) eventually
    /// bind the mannequin on the first Reconcile where the installer returns non-null.
    /// This proves the retry path is unlimited: the binder keeps trying every frame until the
    /// Blender is ready, no matter how many frames that takes.
    /// </summary>
    [Fact]
    public void Reconcile_ManyNullRetries_BindsOnFirstSuccess()
    {
        var (sut, installer, binder) = CreateSut();
        SpawnMannequin(sut);
        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);

        // Ensure preconditions.
        Assert.True(sut.AnimationBridge.TryGetHandle(FindMannequin(sut.World), out _));

        installer.ReturnNull = true;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            binder.Reconcile();
            Assert.Equal(0, binder.BoundCount);   // still not bound
            Assert.Equal(0, installer.InstallCount); // installer returned null every time
        }

        // Now Blender is "ready": installer returns a token.
        installer.ReturnNull = false;
        binder.Reconcile();
        Assert.Equal(1, installer.InstallCount); // bound on first non-null
        Assert.Equal(1, binder.BoundCount);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Animation;
using Hrot.Stride.Core;
using Hrot.Stride.Core.TestHarness;
using Stride.Engine;
using Xunit;
using Entity = Fdp.Core.Entity;

namespace HrotStrideApp.Tests;

/// <summary>
/// End-to-end headless tests for the BATCH-14 animation wiring (STR-P4-T3/T4): the
/// <see cref="StrideAnimationBackend"/> + <see cref="StrideAnimationBridge"/> wired into a real
/// <see cref="EditorStrideSubsystem"/>, and the Walk / Run / Jump harness cases driven against
/// it with a recording fake visual factory. These prove the <i>logic</i> end-to-end — spawn a
/// mannequin, the subsystem's per-frame <see cref="EditorStrideSubsystem.Tick"/> runs the
/// bridge which registers it with the backend and drives <c>UpdateLocomotionInputs</c> from the
/// harness-driven <see cref="SimVelocity"/>, and a Jump traversal starts the montage. The
/// actual skeletal playback (GPU) is human-verified.
/// </summary>
public sealed class AnimationHarnessIntegrationTests : IDisposable
{
    private const long TkbInfantrySoldier = 2002L;

    private sealed class RecordingFakeFactory : IStrideVisualFactory
    {
        private int _counter;
        public int CreateCount { get; private set; }
        public object CreateModelVisual(string modelRef, string skeletonRef, float scale, Vector3 offsetFdp, in SimTransform initialPose)
        { CreateCount++; return $"M_{++_counter}"; }
        public object CreateProceduralVisual(CollisionShapeKind kind, ShapeDims dims, float scale, Vector3 offsetFdp, in SimTransform initialPose)
        { CreateCount++; return $"P_{++_counter}"; }
        public void UpdatePose(object handle, in SimTransform pose) { }
        public void Destroy(object handle) { }
    }

    private readonly List<EditorStrideSubsystem> _suts = new();
    public void Dispose() { foreach (var s in _suts) s.Dispose(); }

    private EditorStrideSubsystem CreateSut()
    {
        var sut = new EditorStrideSubsystem();
        sut.Initialize(new RecordingFakeFactory());
        _suts.Add(sut);
        return sut;
    }

    private static TestHarnessContext CreateContext(EditorStrideSubsystem sut, List<string> log)
        => new TestHarnessContext(
            sut.World, sut.ScenarioSource, sut.VisualBindingSystem,
            new Scene(), cameraEntity: null, log: log.Add);

    private static TestHarnessRegistry BuildRegistry(EditorStrideSubsystem sut)
    {
        var reg = new TestHarnessRegistry();
        StrideTestHarnessCases.RegisterInitialCases(reg);
        StrideAnimationHarnessCases.RegisterAnimationCases(reg, sut.AnimationBackend, sut.AnimationBridge);
        return reg;
    }

    private static int IndexOf(TestHarnessRegistry reg, string label)
    {
        for (int i = 0; i < reg.Count; i++)
            if (reg.Cases[i].Label == label) return i;
        throw new InvalidOperationException($"case '{label}' not registered");
    }

    private static Entity FindMannequin(EntityRepository world)
    {
        foreach (var e in world.Query().With<SimTransform>().With<TkbIdentity>().Build())
            if (world.GetComponentRO<TkbIdentity>(e).TkbType == TkbInfantrySoldier)
                return e;
        return default;
    }

    // ── Subsystem wiring ─────────────────────────────────────────────────

    [Fact]
    public void Subsystem_Initialize_WiresAnimationBackendAndBridge()
    {
        var sut = CreateSut();
        Assert.NotNull(sut.AnimationBackend);
        Assert.NotNull(sut.AnimationBridge);
        Assert.Equal(0, sut.AnimationBridge.RegisteredCount);
    }

    [Fact]
    public void Subsystem_Tick_RegistersSpawnedMannequin_WithBackend()
    {
        var sut = CreateSut();

        sut.ScenarioSource.Enqueue(new Hrot.Core.Network.EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = new Vector3(0, 5, 0), Rotation = Quaternion.Identity },
                new SimVelocity(),
                new TkbIdentity { TkbType = TkbInfantrySoldier },
            },
        });

        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);

        // The mannequin is registered with the backend via the bridge.
        Assert.True(sut.AnimationBridge.RegisteredCount >= 1);
        Assert.True(sut.AnimationBackend.SnapshotMetrics().ActiveEntityCount >= 1);

        var mannequin = FindMannequin(sut.World);
        Assert.NotEqual(default, mannequin);
        Assert.True(sut.AnimationBridge.TryGetHandle(mannequin, out _));
    }

    [Fact]
    public void Subsystem_Tick_UnregistersMannequin_OnDeath()
    {
        var sut = CreateSut();
        sut.ScenarioSource.Enqueue(new Hrot.Core.Network.EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = new Vector3(0, 5, 0), Rotation = Quaternion.Identity },
                new SimVelocity(),
                new TkbIdentity { TkbType = TkbInfantrySoldier },
            },
        });
        for (int i = 0; i < 6; i++) sut.Tick(1f / 60f);
        Assert.True(sut.AnimationBridge.RegisteredCount >= 1);

        var mannequin = FindMannequin(sut.World);
        sut.World.DestroyEntity(mannequin);
        for (int i = 0; i < 2; i++) sut.Tick(1f / 60f);

        Assert.Equal(0, sut.AnimationBridge.RegisteredCount);
        Assert.Equal(0, sut.AnimationBackend.SnapshotMetrics().ActiveEntityCount);
    }

    // ── Harness Walk / Run cases (drive SimVelocity through the bridge) ───

    [Fact]
    public void WalkMannequin_Case_DrivesWalkBlend_ThroughBridge()
    {
        var sut = CreateSut();
        var reg = BuildRegistry(sut);
        var log = new List<string>();
        var ctx = CreateContext(sut, log);

        // Register Walk case is present (the visible deliverable).
        Assert.Contains(reg.Cases, c => c.Label == "Walk Mannequin");

        reg.Trigger(IndexOf(reg, "Walk Mannequin"), ctx);

        // Pump: subsystem tick drains the spawn, registers, and the harness hook (pumped here)
        // sets SimVelocity to walk speed; the bridge feeds it into UpdateLocomotionInputs.
        Entity mannequin = default;
        for (int i = 0; i < 30; i++)
        {
            ctx.PumpUpdates(1f / 60f);
            sut.Tick(1f / 60f);
            if (mannequin == default) mannequin = FindMannequin(sut.World);
        }

        Assert.NotEqual(default, mannequin);
        Assert.True(sut.AnimationBridge.TryGetHandle(mannequin, out var handle));

        var loco = sut.AnimationBackend.QueryLocomotion(handle);
        Assert.True(loco.Walk > loco.Idle, $"Walk ({loco.Walk}) should exceed Idle ({loco.Idle})");
        Assert.True(loco.Walk > loco.Run, $"Walk ({loco.Walk}) should exceed Run ({loco.Run})");
    }

    [Fact]
    public void RunMannequin_Case_DrivesRunBlend_ThroughBridge()
    {
        var sut = CreateSut();
        var reg = BuildRegistry(sut);
        var log = new List<string>();
        var ctx = CreateContext(sut, log);

        Assert.Contains(reg.Cases, c => c.Label == "Run Mannequin");

        reg.Trigger(IndexOf(reg, "Run Mannequin"), ctx);

        Entity mannequin = default;
        for (int i = 0; i < 30; i++)
        {
            ctx.PumpUpdates(1f / 60f);
            sut.Tick(1f / 60f);
            if (mannequin == default) mannequin = FindMannequin(sut.World);
        }

        Assert.NotEqual(default, mannequin);
        Assert.True(sut.AnimationBridge.TryGetHandle(mannequin, out var handle));

        var loco = sut.AnimationBackend.QueryLocomotion(handle);
        Assert.True(loco.Run > loco.Walk, $"Run ({loco.Run}) should exceed Walk ({loco.Walk})");
        Assert.True(loco.Run > loco.Idle, $"Run ({loco.Run}) should exceed Idle ({loco.Idle})");
    }

    // ── Harness Trigger Jump case ─────────────────────────────────────────

    [Fact]
    public void TriggerJump_Case_StartsMontageOnSlot()
    {
        var sut = CreateSut();
        var reg = BuildRegistry(sut);
        var log = new List<string>();
        var ctx = CreateContext(sut, log);

        Assert.Contains(reg.Cases, c => c.Label == "Trigger Jump");

        // Spawn a mannequin first (via Walk case) and let the bridge register it.
        reg.Trigger(IndexOf(reg, "Walk Mannequin"), ctx);
        Entity mannequin = default;
        for (int i = 0; i < 10; i++)
        {
            ctx.PumpUpdates(1f / 60f);
            sut.Tick(1f / 60f);
            if (mannequin == default) mannequin = FindMannequin(sut.World);
        }
        Assert.NotEqual(default, mannequin);
        Assert.True(sut.AnimationBridge.TryGetHandle(mannequin, out var handle));

        // Fire the jump.
        reg.Trigger(IndexOf(reg, "Trigger Jump"), ctx);

        // The jump montage is now playing on the slot.
        Assert.True(sut.AnimationBackend.IsAnySlotActive(handle), "a jump montage slot must be active");
        Assert.Equal(1, sut.AnimationBridge.ActiveJumpCount);
        Assert.Contains(log, l => l.Contains("Trigger Jump"));
    }

    [Fact]
    public void AnimationCases_AreRegistered_InExpectedOrder()
    {
        var sut = CreateSut();
        var reg = BuildRegistry(sut);

        var labels = reg.Cases.Select(c => c.Label).ToList();
        // The four BATCH-12 cases come first, then the three BATCH-14 animation cases.
        Assert.Equal(new[] { "Walk Mannequin", "Run Mannequin", "Trigger Jump" },
            labels.Skip(labels.Count - 3).ToArray());
    }
}

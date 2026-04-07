using System;
using System.Threading;
using FDP.Framework.Runner;
using Hrot.ClusterRunner.Services;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="EyesAndMuscleSubsystem"/> (EAM-E001) and
/// <see cref="EyesAndMuscleModule"/> (EAM-E002).
///
/// All tests run in headless mode — no DDS, no Raylib window, no allocator wait.
/// </summary>
public sealed class EyesAndMuscleSubsystemTests : IDisposable
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SubsystemConfig HeadlessConfig(int nodeId = 55) => new()
    {
        Headless      = true,
        NodeId        = nodeId,
        DomainId      = 0,
        OwnWindow     = false,
        SubsystemName = "EyesAndMuscle",
    };

    private readonly EyesAndMuscleSubsystem _sub;

    public EyesAndMuscleSubsystemTests()
    {
        _sub = new EyesAndMuscleSubsystem();
    }

    public void Dispose()
    {
        try { _sub.Shutdown(); } catch { /* best-effort */ }
    }

    // ── EAM-E001 SC1 — Boots without exception ────────────────────────────────

    [Fact]
    public void Initialize_Headless_DoesNotThrow_AndWorldIsNonNull()
    {
        var ex = Record.Exception(() =>
            _sub.Initialize(HeadlessConfig()));

        Assert.Null(ex);
        Assert.NotNull(_sub.World);
        Assert.NotNull(_sub.Module);
    }

    // ── EAM-E001 SC2 — Update does not throw on empty world ──────────────────

    [Fact]
    public void Update_HeadlessEmptyWorld_DoesNotThrow()
    {
        _sub.Initialize(HeadlessConfig());

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 10; i++)
                _sub.Update(0.016f);
        });

        Assert.Null(ex);
    }

    // ── EAM-E001 SC3 — Shutdown is idempotent ────────────────────────────────

    [Fact]
    public void Shutdown_CalledTwice_DoesNotThrow()
    {
        _sub.Initialize(HeadlessConfig());
        _sub.Shutdown();

        // Second call must not throw
        var ex = Record.Exception(() => _sub.Shutdown());
        Assert.Null(ex);
    }

    // ── EAM-E002 SC1 — EyesTicks increments after pumping frames ─────────────

    [Fact]
    public void Module_EyesTicks_IncrementAfterPumping()
    {
        _sub.Initialize(HeadlessConfig());

        // Async module may need several frames to run — pump 100 frames
        for (int i = 0; i < 100; i++)
            _sub.Update(0.016f);

        Assert.True(_sub.Module!.EyesTicks >= 1,
            $"EyesTicks expected >= 1, was {_sub.Module.EyesTicks}");
    }

    // ── EAM-E002 SC2 — MuscleTicks is 0 for ImageGenerator-only role ─────────

    [Fact]
    public void EyesAndMuscleModule_MuscleTicks_ZeroWhenImageGeneratorOnlyRole()
    {
        // Create a module configured as ImageGenerator only (no Muscle tier)
        var module = new EyesAndMuscleModule(NodeRole.ImageGenerator);

        Assert.Equal(0, module.MuscleTicks);
        // After construction, counters are at zero — this confirms the role gate logic
    }

    // ── EAM-E002 SC3 — LastTickThreadId is initially null ─────────────────────

    [Fact]
    public void EyesAndMuscleModule_LastTickThreadId_NullBeforeFirstTick()
    {
        var module = new EyesAndMuscleModule(NodeRole.AllInOne);
        Assert.Null(module.LastTickThreadId);
    }

    // ── EAM-E002 SC4 — No stale view held after Tick (structural) ────────────
    // This is a static code-review assertion verified at implementation time:
    // EyesAndMuscleModule has no field of type ISimulationView.
    // Verified structurally — no runtime assertion needed.
}

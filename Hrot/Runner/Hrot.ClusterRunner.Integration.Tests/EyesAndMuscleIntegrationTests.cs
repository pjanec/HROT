using System;
using System.Threading;
using Fdp.Engine.Runner;
using Hrot.ClusterRunner.Services;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="EyesAndMuscleSubsystem"/> (EAM-E003).
///
/// <para>Uses a simple inline harness with headless mode — no DDS, no Raylib window,
/// no OrchestratorSubsystem, no allocator-routing wait. Each test operates on the
/// same <see cref="EyesAndMuscleSubsystem"/> instance initialized in the constructor.</para>
/// </summary>
public sealed class EyesAndMuscleIntegrationTests : IDisposable
{
    private readonly EyesAndMuscleSubsystem _sub;

    public EyesAndMuscleIntegrationTests()
    {
        _sub = new EyesAndMuscleSubsystem();
        _sub.Initialize(new SubsystemConfig
        {
            Headless      = true,
            NodeId        = 55,
            DomainId      = 0,
            OwnWindow     = false,
            SubsystemName = "EyesAndMuscle",
        });
    }

    public void Dispose() => _sub.Shutdown();

    // ── Helper ────────────────────────────────────────────────────────────────

    private void PumpFrames(int n)
    {
        for (int i = 0; i < n; i++)
            _sub.Update(0.016f);
    }

    // ── Test 1 — Subsystem boots and runs ─────────────────────────────────────

    /// <summary>
    /// EAM-E003 Test 1: Subsystem initialises in headless mode, pumps 50 frames
    /// without exception, and the ECS world is non-null with zero entities.
    /// </summary>
    [Fact]
    public void Subsystem_BootsAndRuns_WithoutException()
    {
        // arrange: subsystem already initialized in constructor
        // act
        PumpFrames(50);

        // assert
        Assert.NotNull(_sub.World);
        Assert.Equal(0, _sub.World!.EntityCount);  // no entities spawned
    }

    // ── Test 2 — EyesTicks and MuscleTicks increment ──────────────────────────

    /// <summary>
    /// EAM-E003 Test 2: Both <see cref="EyesAndMuscleModule.EyesTicks"/> and
    /// <see cref="EyesAndMuscleModule.MuscleTicks"/> increment after pumping enough
    /// frames for the async background module to run at least once.
    /// </summary>
    [Fact]
    public void Module_EyesAndMuscleTicks_IncrementAfterPumping()
    {
        PumpFrames(60);  // enough frames for async SoD module to run

        Assert.True(_sub.Module!.EyesTicks > 0,
            $"EyesTicks expected > 0, was {_sub.Module.EyesTicks}");
        Assert.True(_sub.Module!.MuscleTicks > 0,
            $"MuscleTicks expected > 0, was {_sub.Module.MuscleTicks}");
    }

    // ── Test 3 — Async execution (Tick runs on non-main thread) ───────────────

    /// <summary>
    /// EAM-E003 Test 3: Because <see cref="EyesAndMuscleModule"/> uses
    /// <see cref="Fdp.ModuleHost.Abstractions.ExecutionPolicy.SlowBackground(int)"/>, its
    /// <c>Tick</c> runs on a background thread. This test asserts that
    /// <see cref="EyesAndMuscleModule.LastTickThreadId"/> differs from the main thread's ID.
    /// </summary>
    [Fact]
    public void Module_Tick_RunsOnNonMainThread()
    {
        int mainThreadId = Thread.CurrentThread.ManagedThreadId;

        // Pump until the async module has run (up to 200 frames)
        int pumped = 0;
        while (_sub.Module!.LastTickThreadId == null && pumped < 200)
        {
            _sub.Update(0.016f);
            pumped++;
        }

        Assert.NotNull(_sub.Module.LastTickThreadId);
        Assert.NotEqual(mainThreadId, _sub.Module.LastTickThreadId.Value);
    }
}

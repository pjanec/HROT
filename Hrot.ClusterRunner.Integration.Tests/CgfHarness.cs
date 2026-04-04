using System;
using System.Threading;
using FDP.Framework.Runner;
using Hrot.ClusterRunner.Services;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Domain-isolated test harness wrapping <see cref="CgfSubsystem"/> for integration tests.
///
/// <para>Provides two construction modes:</para>
/// <list type="bullet">
///   <item>Auto-increment: uses same counter base as <see cref="HrotRunnerHarness"/> to avoid
///     domain conflicts (starting at 200 for CGF-only tests).</item>
///   <item>Shared-domain: <c>CgfHarness(int domainId)</c> — used with
///     <c>HrotRunnerHarness(RunMode, int domainId)</c> in IT-4 tests.</item>
/// </list>
/// </summary>
public sealed class CgfHarness : IDisposable
{
    private const int CgfDomainIdBase  = 200;
    private const int WarmupFrames     = 20;
    private const int PumpSleepMs      =  5;
    private const int PostWarmupSettle = 200;

    private static int _domainCounter = CgfDomainIdBase - 1;

    public int          DomainId { get; }
    public CgfSubsystem CgfSvc   { get; }

    // ── Auto-increment constructor ────────────────────────────────────────────

    /// <summary>
    /// Creates a new harness assigned a unique domain ID from the internal counter.
    /// Two independently created instances always get different IDs.
    /// </summary>
    public CgfHarness()
        : this(Interlocked.Increment(ref _domainCounter))
    {
    }

    // ── Shared-domain constructor ─────────────────────────────────────────────

    /// <summary>
    /// Creates a harness using the specified domain ID (shared with another harness).
    /// Used in <c>DistributedBrainMuscleIntegrationTests</c> (IT-4) to pair with
    /// a <see cref="HrotRunnerHarness"/> on the same loopback domain.
    /// </summary>
    public CgfHarness(int domainId)
    {
        DomainId = domainId;
        CgfSvc   = new CgfSubsystem();
        CgfSvc.Initialize(new SubsystemConfig
        {
            DomainId = domainId,
            Headless = true,
        });

        Warmup();
    }

    // ── Pump API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances <paramref name="frames"/> simulation frames (5 ms sleep between each).
    /// </summary>
    public void PumpFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            CgfSvc.Update(PumpSleepMs / 1000f);
            Thread.Sleep(PumpSleepMs);
        }
    }

    /// <summary>
    /// Pumps frames until <paramref name="condition"/> returns <c>true</c>
    /// or <paramref name="timeoutMs"/> milliseconds have elapsed.
    /// Returns <c>true</c> if the condition was met before timeout.
    /// </summary>
    public bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        if (condition()) return true;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            CgfSvc.Update(PumpSleepMs / 1000f);
            Thread.Sleep(PumpSleepMs);
            if (condition()) return true;
        }

        return false;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        CgfSvc.Shutdown();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Warmup()
    {
        for (int i = 0; i < WarmupFrames; i++)
        {
            CgfSvc.Update(PumpSleepMs / 1000f);
            Thread.Sleep(PumpSleepMs);
        }
        Thread.Sleep(PostWarmupSettle);
    }
}

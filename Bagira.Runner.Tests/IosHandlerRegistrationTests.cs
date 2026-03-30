using Bagira.Runner.Services;
using FDP.Toolkit.Orchestration.Handlers;

namespace Bagira.Runner.Tests;

/// <summary>
/// Verifies that <see cref="IosSubsystem"/> registers the required DSM handlers
/// after initialization (CGF1-S0104 / BATCH-23 Part A.3).
///
/// Uses <see cref="IosSubsystem.TestHook_DrillSlave"/> for assertions.
/// IOS is a listener node — it registers ReferenceReplayLoadHandler and
/// ReferenceLiveLoadHandler as thin stubs; it does NOT register brain-level
/// persistence handlers (no SerializeLocal / checkpoint).
/// </summary>
public sealed class IosHandlerRegistrationTests
{
    private static SubsystemConfig HeadlessConfig() => new()
    {
        DomainId      = 0,
        Headless      = true,
        OwnWindow     = false,
        SubsystemName = "IOS",
    };

    // ── A.3: IOS listener stubs (BATCH-23) ────────────────────────────────────

    /// <summary>IOS must register a ReferenceReplayLoadHandler as a listener stub.</summary>
    [Fact]
    public void AfterInit_RegistersReferenceReplayLoadHandler()
    {
        var subsystem = new IosSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_DrillSlave;
        Assert.NotNull(slave);
        Assert.True(
            slave!.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
            "IOS must register ReferenceReplayLoadHandler so replay fan-outs do not stall cluster.");

        subsystem.Shutdown();
    }

    /// <summary>IOS must register a ReferenceLiveLoadHandler as a listener stub.</summary>
    [Fact]
    public void AfterInit_RegistersReferenceLiveLoadHandler()
    {
        var subsystem = new IosSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_DrillSlave;
        Assert.NotNull(slave);
        Assert.True(
            slave!.IsHandlerRegistered<ReferenceLiveLoadHandler>(),
            "IOS must register ReferenceLiveLoadHandler so live fan-outs are acknowledged.");

        subsystem.Shutdown();
    }

    /// <summary>IOS must register a ReferenceDryRunHandler.</summary>
    [Fact]
    public void AfterInit_RegistersReferenceDryRunHandler()
    {
        var subsystem = new IosSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_DrillSlave;
        Assert.NotNull(slave);
        Assert.True(
            slave!.IsHandlerRegistered<ReferenceDryRunHandler>(),
            "IOS must register ReferenceDryRunHandler.");

        subsystem.Shutdown();
    }
}

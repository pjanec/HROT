using Hrot.ClusterRunner.Services;
using FDP.Toolkit.Orchestration.Handlers;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Verifies that <see cref="ExConSubsystem"/> registers the required Cluster state handlers
/// after initialization (CGF1-S0104 / BATCH-23 Part A.3).
///
/// Uses <see cref="ExConSubsystem.TestHook_ClusterSlave"/> for assertions.
/// ExCon is a listener node — it registers ReferenceReplayLoadHandler and
/// ReferenceLiveLoadHandler as thin stubs; it does NOT register brain-level
/// persistence handlers (no SerializeLocal / checkpoint).
/// </summary>
public sealed class ExConHandlerRegistrationTests
{
    private static SubsystemConfig HeadlessConfig() => new()
    {
        DomainId      = 0,
        Headless      = true,
        OwnWindow     = false,
        SubsystemName = "ExCon",
    };

    // ── A.3: ExCon listener stubs (BATCH-23) ────────────────────────────────────

    /// <summary>ExCon must register a ReferenceReplayLoadHandler as a listener stub.</summary>
    [Fact]
    public void AfterInit_RegistersReferenceReplayLoadHandler()
    {
        var subsystem = new ExConSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_ClusterSlave;
        Assert.NotNull(slave);
        Assert.True(
            slave!.IsHandlerRegistered<ReferenceReplayLoadHandler>(),
            "ExCon must register ReferenceReplayLoadHandler so replay fan-outs do not stall cluster.");

        subsystem.Shutdown();
    }

    /// <summary>ExCon must register a ReferenceLiveLoadHandler as a listener stub.</summary>
    [Fact]
    public void AfterInit_RegistersReferenceLiveLoadHandler()
    {
        var subsystem = new ExConSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_ClusterSlave;
        Assert.NotNull(slave);
        Assert.True(
            slave!.IsHandlerRegistered<ReferenceLiveLoadHandler>(),
            "ExCon must register ReferenceLiveLoadHandler so live fan-outs are acknowledged.");

        subsystem.Shutdown();
    }

    /// <summary>ExCon must register a ReferencePreviewHandler.</summary>
    [Fact]
    public void AfterInit_RegistersReferencePreviewHandler()
    {
        var subsystem = new ExConSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_ClusterSlave;
        Assert.NotNull(slave);
        Assert.True(
            slave!.IsHandlerRegistered<ReferencePreviewHandler>(),
            "ExCon must register ReferencePreviewHandler.");

        subsystem.Shutdown();
    }
}

using Hrot.ExCon;
using Fdp.Toolkit.Orchestration.Handlers;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Verifies that <see cref="ExConSubsystem"/> registers the required Cluster state handlers
/// after initialization (CGF1-S0104 / BATCH-23 Part A.3).
///
/// Uses <see cref="ExConSubsystem.TestHook_ClusterSlave"/> for assertions.
/// ⭐⭐ C10 (Q91 §4-L L5, R-4011) — ExCon holds NO record/replay controller and registers NO live/replay load handler: it
/// has no world, so ClusterSlave's no-handler path answers those rounds (Success, not participating) and nothing stalls.
/// ⛔ Was: two thin stub handlers fed by a listen-only controller, whose only job was that same ACK.
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

    /// <summary>
    /// ⭐⭐ C10 — no handler on ExCon claims a live or replay load round, so ClusterSlave answers it itself (Success, not
    /// participating). ⛔ FLIPPED: was "ExCon must register ReferenceReplayLoadHandler / ReferenceLiveLoadHandler".
    /// </summary>
    [Theory]
    [InlineData(Fdp.Toolkit.Orchestration.NodeOpType.PrepareReplay)]
    [InlineData(Fdp.Toolkit.Orchestration.NodeOpType.FinalizeReplay)]
    [InlineData(Fdp.Toolkit.Orchestration.NodeOpType.PrepareLive)]
    [InlineData(Fdp.Toolkit.Orchestration.NodeOpType.FinalizeLive)]
    public void AfterInit_NoHandlerClaimsALiveOrReplayLoadRound(Fdp.Toolkit.Orchestration.NodeOpType op)
    {
        var subsystem = new ExConSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var slave = subsystem.TestHook_ClusterSlave;
        Assert.NotNull(slave);
        Assert.False(slave!.IsHandlerRegistered<ReferenceReplayLoadHandler>());
        Assert.False(slave.IsHandlerRegistered<ReferenceLiveLoadHandler>());
        Assert.DoesNotContain(slave.RegisteredHandlers, h => h.CanHandle(op));

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

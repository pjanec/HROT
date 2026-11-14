using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Scenarios;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Scenarios;

/// <summary>
/// ⭐ <c>CE-1025</c> (R-1031) — what HTTP names as the run a load stopped (<c>stoppedRun</c>). ⭐⭐ <c>CE-1438</c> (R-3066 audit #7): the definition is the run choice's
/// (<c>RunModeRunGuard.Of</c>, off the facade's <c>TransportMode</c>); this guard only applies it to a cluster state. The interactive <i>Stop &amp; Load</i> warning moved into
/// <c>RunModeChoice</c> (its rails: <c>Hrot.Presentation.Tests</c> → <c>TheOneTimeFacadeTests.CE1438_*</c>).
/// </summary>
public sealed class ScenarioLoadGuardTests
{
    /// <summary>🔴 Red-proof: <c>RunModeRunGuard.Of</c> answering for <c>Review</c> / <c>Replay</c>, or ignoring <c>inDryRun</c> ⇒ red.</summary>
    [Fact]
    public void WhatCountsAsRunning()
    {
        Assert.Null(ScenarioLoadGuard.RunningRun(false, ClusterState.Idle));
        Assert.Null(ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingEdit));
        Assert.Null(ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingReview));   // a DAR / an AAR are not a run to stop here (CE-1025: unchanged)
        Assert.Null(ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingReplay));
        Assert.Equal("a dry run", ScenarioLoadGuard.RunningRun(true, ClusterState.OperatingEdit));   // a paused dry run too
        Assert.Equal("a dry run", ScenarioLoadGuard.RunningRun(true, ClusterState.OperatingLive));    // a live run can read as "in preview" (CE-4054)
        Assert.Equal("a live run", ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingLive));
        Assert.Equal("a dry run", ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingPreview));
    }
}

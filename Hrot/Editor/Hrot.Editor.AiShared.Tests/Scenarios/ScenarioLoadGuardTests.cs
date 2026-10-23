using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Scenarios;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Scenarios;

/// <summary>
/// ⭐ <c>CE-1025</c> (R-1031) — an interactive load over a running live run or dry run warns first, and only then; Cancel loads nothing.
/// 📄 docs/DESIGN_Cluster_Load_Phase.md §9.1.
/// </summary>
public sealed class ScenarioLoadGuardTests
{
    [Fact]
    public void WhatCountsAsRunning()
    {
        Assert.Null(ScenarioLoadGuard.RunningRun(false, ClusterState.Idle));
        Assert.Null(ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingEdit));
        Assert.Equal("a dry run", ScenarioLoadGuard.RunningRun(true, ClusterState.OperatingEdit));   // a paused dry run too
        Assert.Equal("a dry run", ScenarioLoadGuard.RunningRun(true, ClusterState.OperatingLive));    // the editor's dry run plays inside a live-loaded cluster
        Assert.Equal("a live run", ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingLive));
        Assert.Equal("a dry run", ScenarioLoadGuard.RunningRun(false, ClusterState.OperatingPreview));
        Assert.DoesNotContain("restore point", ScenarioLoadGuard.Prompt("a live run", "b"));        // only a dry run has one to forget
        Assert.Contains("restore point", ScenarioLoadGuard.Prompt("a dry run", "b"));
    }

    /// <summary>🔴 Red-proof: load at once in <c>ScenarioMenuCommands.Guarded</c> (skip the prompt) ⇒ red.</summary>
    [Fact]
    public void ARunningRun_AsksFirst_CancelLoadsNothing_AndNoRunLoadsAtOnce()
    {
        var loaded = new List<string>();
        System.Action? pending = null;
        string? message = null;

        ScenarioMenuCommands.Guarded("b", loaded.Add, () => "a dry run", (title, msg, label, run) => { message = msg; pending = run; });
        Assert.Empty(loaded);                                                  // asked, not loaded
        Assert.Contains("a dry run", message);
        Assert.Contains("forgets its restore point", message);
        pending!();                                                            // Stop & Load
        Assert.Equal(new[] { "b" }, loaded);

        loaded.Clear();
        ScenarioMenuCommands.Guarded("c", loaded.Add, () => "a live run", (_, _, _, _) => { });   // Cancel: the continuation never runs
        Assert.Empty(loaded);

        ScenarioMenuCommands.Guarded("d", loaded.Add, () => null, (_, _, _, _) => Assert.Fail("no prompt when nothing runs"));
        Assert.Equal(new[] { "d" }, loaded);
    }
}

using System;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Xunit;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐ R-3055 (the R-3049 rule, for a replay) — a replay that names no exercise gets one from the MASTER, so the same request means the
/// same thing on every host: the live run in progress, else the newest finished one. 📌 Measured before (run C): the asker picked from
/// its own cached inventory — the request worked on the editor and was refused on CGF.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterReplayExerciseTests
{
    private static ClusterConfiguration Config() => new()
    {
        Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
    };

    private static void Set(ClusterMaster m, string field, object value)
        => typeof(ClusterMaster).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(m, value);

    [Fact]
    public void FromALiveRun_TheReplayIsOfThatRun_ElseTheNewestFinishedOne()
    {
        using var master = new ClusterMaster(new FdpEventBus(), Config());
        var running = Guid.NewGuid();
        var newest  = Guid.NewGuid();
        master.NewestExercise = () => newest;

        Set(master, "_currentDsmState", ClusterState.OperatingLive);
        Set(master, "_currentExerciseId", running);
        Assert.Equal(running, master.ResolveReplayExercise());   // the planner ends this run on the way into its replay

        Set(master, "_currentDsmState", ClusterState.Idle);
        Assert.Equal(newest, master.ResolveReplayExercise());

        master.NewestExercise = null;
        Assert.Equal(Guid.Empty, master.ResolveReplayExercise()); // ⇒ the request is rejected: nothing has run
    }
}

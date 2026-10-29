using System;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using ClusterState    = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NodeOpType      = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <c>CE-3345</c> part A (R-3073, user <c>2026-10-29</c>) — <b>the CLUSTER ERROR STATE</b>: a mandatory node name or role is lost while a scenario is loaded or running.
/// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.5.
/// <para>🔒 User: <i>"Putting the cluster into error state seems sufficient to me (master broadcasts error state of the cluster with the reason, pauses clock, nodes show some red error
/// indicator in status bar), until the master clears the error state the node operations are disallowed similarly to when node is not ready to play"</i>.</para>
/// <para>⭐ What it is, in terms of what already exists (no second mechanism): the broadcast state is <c>Degraded</c> with an <c>ErrorReason</c> beside CE-3344's <c>WaitingFor</c>; the
/// clock is paused through <c>PublishClusterPause</c> (the ONE pause); the live recording is closed through the ONE finalise (<c>NodeOpType.FinalizeLive</c>, what the unload sends); the
/// bootstrap latch stays CLOSED (so every request but the ways back to Idle is refused exactly like "not ready"); and <c>_currentDsmState</c> is NOT touched, so the planner plans
/// Close (a transition to Idle) from the state the world is really in.</para>
/// <para>⭐ Sticky: a returning node changes <c>WaitingFor</c> only (the latch does not re-release while in error). The ONLY exit is the cluster reaching Idle
/// (<see cref="ClearClusterError"/>); the latch then reports "waiting for" as usual until the node is back.</para>
/// </summary>
public sealed partial class ClusterMaster
{
    private string? _errorReason;

    /// <summary>⭐ CE-3345 — why the cluster is in its ERROR state (<i>"Brain (CGF, node 3) lost at sim 00:12:31"</i>); empty when it is not. Broadcast with every cluster-state publication.</summary>
    public string ErrorReason => _errorReason ?? string.Empty;

    /// <summary>⭐ CE-3345 — a mandatory node / role was lost with a world loaded; cleared only by the cluster reaching Idle.</summary>
    public bool IsInError => _errorReason != null;

    /// <summary>
    /// ⭐ CE-3345 — the cluster's simulation clock in seconds, for the error text (<i>"… lost at sim 00:12:31"</i>). Set by the host that owns the time controller
    /// (<c>OrchestratorCore</c>, like <see cref="WallTicks"/>); <see langword="null"/> ⇒ the text omits the time.
    /// </summary>
    public Func<double>? SimSeconds { get; set; }

    /// <summary>⭐ CE-3345 — is there a world to lose? Anything but Idle (loading, editing, previewing, live, a replay or a review): the cluster has loaded something.</summary>
    private bool HasWorldLoaded => _currentDsmState != ClusterState.Idle;

    /// <summary>
    /// ⭐ CE-3345 — the cluster enters its error state (see the type remarks). Called by <see cref="EjectNode"/> after the lost node left the roster, with the latch still
    /// open ⇒ closed here, BEFORE the broadcast, so the state carries what is now missing as well.
    /// </summary>
    private void EnterClusterError(int nodeId, string subsystemName, bool lostMandatoryName)
    {
        // a second loss while already in error: the first reason stands (it is the one that broke the run); the text of what is missing moves with WaitingFor
        if (_errorReason != null)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3345: node {0} ({1}) lost while the cluster is already in error — the first reason stands: {2}.",
                nodeId, subsystemName, _errorReason);
            _bootstrapLatch = false;
            PublishClusterState(ClusterState.Degraded);
            return;
        }

        string who = lostMandatoryName ? $"{subsystemName} lost (node {nodeId})" : $"{string.Join(", ", MissingMandatoryRoles())} lost (node {nodeId}, {subsystemName})";
        string at = SimSeconds?.Invoke() is { } t && t >= 0
            ? $" at sim {(int)TimeSpan.FromSeconds(t).TotalHours:00}:{TimeSpan.FromSeconds(t):mm\\:ss}"
            : string.Empty;
        _errorReason = who + at;
        _bootstrapLatch = false;   // ⭐ refused like "not ready" (the latch is the ONE gate); it does not re-release while in error (CheckBootstrapLatch)

        // ⭐ the ONE pause (Pause op, Stop, a restore): nothing runs under a broken cluster; the lost node is already off the step roster
        PublishClusterPause();

        // ⭐ the live recording is closed and KEPT up to the failure — the ONE finalise (what the unload sends). The ledger lists the run when the broadcast below leaves OperatingLive.
        //   ⛔ Only a LIVE run records; a review's recording is suspended and finalised by its own unload when the user closes.
        if (_currentDsmState is ClusterState.OperatingLive or ClusterState.LoadingLive && _roster.ActiveNodes.Count > 0)
            FanOutNodeOp(NodeOpType.FinalizeLive, Guid.NewGuid(), null, new System.Collections.Generic.List<int>(_roster.ActiveNodes.Keys));

        // ⭐ NO abort, NO Idle to the survivors: the world stays on every node for inspection. _currentDsmState is untouched so Close plans from where the world is.
        PublishClusterState(ClusterState.Degraded);
        FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3345: the cluster is in ERROR — {0}. Clock paused, world kept ({1}); Close (a transition to Idle) is the way out.", _errorReason, _currentDsmState);
    }

    /// <summary>⭐ CE-3345 — the cluster reached Idle: the error is cleared (the latch then reports <c>WaitingFor</c> as CE-3344 built it). Returns true when there was one.</summary>
    private bool ClearClusterError()
    {
        if (_errorReason == null) return false;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3345: the cluster reached Idle — the error ({0}) is cleared.", _errorReason);
        _errorReason = null;
        return true;
    }

    /// <summary>⭐ CE-3345 — a way back to Idle: the one request a cluster in error still takes (the rule: a way out is never gated).</summary>
    private static bool IsLeaveToIdle(TransitionStateIntent intent) => (FdpClusterState)(int)intent.TargetState == FdpClusterState.Idle;

    /// <summary>
    /// ⭐ CE-3345 — does the master refuse this transition outright? Not ready (the latch closed): everything but a scenario LOAD, which WAITS for the mandatory set (CE-4087). ⭐ In ERROR: everything but the way back to
    /// Idle — a load is REFUSED there (it must not wait on a cluster whose world is broken: the user closes first), the one exit is Close.
    /// </summary>
    private bool LatchRefuses(TransitionStateIntent intent)
        => IsInError ? !IsLeaveToIdle(intent) : !_bootstrapLatch && !IsScenarioLoad(intent);
}

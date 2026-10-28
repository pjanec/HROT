using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using NodeRole     = Fdp.Core.NodeRole;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <c>CE-4087</c> — <b>the bootstrap latch, extended to ROLES, and a load that WAITS on it.</b>
/// <para>The latch (<c>_bootstrapLatch</c>, CGF-1 design §3.5) already says "the cluster is not ready": until every mandatory subsystem
/// NAME is up in <c>Idle</c> it rejects requests. This file adds the other half of the mandatory set — ROLES
/// (<see cref="ClusterConfiguration.MandatoryRoles"/>, default <c>Brain</c> + <c>MuscleGround</c>) — and changes what a SCENARIO LOAD does
/// against an unready cluster: instead of the bare <c>Rejected</c> it WAITS (up to <see cref="ClusterConfiguration.LoadWaitSeconds"/>),
/// showing <i>"waiting for: MuscleGround (SimHost)"</i>, starts the moment the mandatory set is present, and otherwise is REFUSED with
/// that reason. ⛔ One concept, one implementation: the names are checked by the latch's own helper
/// (<see cref="MissingMandatoryNames"/>, also what <c>CheckBootstrapLatch</c> uses); this file only adds the roles beside it and the wait.</para>
/// <para>🔒 User, <c>2026-10-28</c>: <i>"scenario should not be able to load at all without muscle role node loaded, as it can not be
/// edited nor started."</i></para>
/// <para>⭐ Scope: a request that names a scenario and targets <c>LoadingLive</c> / <c>LoadingEdit</c> / <c>OperatingLive</c> /
/// <c>OperatingEdit</c>. ⛔ Every other request keeps its behaviour: with the name latch closed it is rejected as before; a missing ROLE
/// never rejects a time command, a replay or a storage op (an orchestrator with no nodes still pauses its clock).</para>
/// <para>⚠ Deliberately NOT built: the latch's <i>re-engage on ejection → Degraded → Idle</i> (<c>EjectNode</c>) for a lost ROLE. It is right
/// for a configured subsystem name; as a default for roles a 5 s CGF blip would tear down every running scenario, and C7 catches a
/// rejoining node up in place (Q91 §4-M).</para>
/// <para>📄 <c>docs/designs/cgf-1/CGF-1-DESIGN.md</c> §3.5, <c>docs/DESIGN_Cluster_Load_Phase.md</c> §9.2,
/// <c>docs/blueprints/Architect_Question_91_Player_Nodes.md</c> §4-A′, <c>docs/DESIGN_Node_Roles_And_Policies.md</c> §3.4.</para>
/// </summary>
public sealed partial class ClusterMaster
{
    /// <summary>
    /// ⭐⭐ The roles THIS master's own host plays in-process — the editor composes all four simulation roles inside the process that
    /// holds the master (<c>EditorCapabilities.DefaultRole</c>, <c>R-4005</c>). They count toward the mandatory set at once, without
    /// waiting for the host's own heartbeat and advert to loop back through the bus. ⛔ A host that plays roles and does not pass
    /// them would make the editor refuse its own loads (the silent-default rule: the composing host MUST set it).
    /// </summary>
    public NodeRole LocalRoles { get; set; } = NodeRole.None;

    private sealed record HeldLoad(TransitionStateIntent Intent, double SinceSeconds, string Waiting);

    /// <summary>⭐ AT MOST ONE waiting load (like L8's one parked transition): a newer request replaces it.</summary>
    private HeldLoad? _heldLoad;

    /// <summary>
    /// The text the operator and HTTP read about the mandatory set: <i>"waiting for: MuscleGround (SimHost)"</i> while a load waits, the
    /// refusal reason after one was refused, <see langword="null"/> before any. Cleared by the next request.
    /// </summary>
    public string? LoadNotice { get; private set; }

    /// <summary>The request id of the load waiting on the mandatory set, or <see langword="null"/> (test/diagnostic seam).</summary>
    public Guid? HeldLoadRequestId => _heldLoad?.Intent.TransactionId;

    /// <summary>The typical host a role is found on — operator-facing text only. ⚠ Application knowledge (R-4000), never behaviour.</summary>
    private static string TypicalHostOf(NodeRole role) => role switch
    {
        NodeRole.Brain            => "CGF",
        NodeRole.MuscleGround     => "SimHost",
        NodeRole.Perception       => "SimHost",
        NodeRole.NavigationSolver => "SimHost",
        NodeRole.Map2D            => "IG",
        _                         => "?",
    };

    /// <summary>
    /// The mandatory subsystem NAMES with no roster entry in <c>Idle</c> yet — ⭐ the ONE implementation of the latch's name test:
    /// <c>CheckBootstrapLatch</c> clears the latch when this is empty, the load wait reads it for the "waiting for" text.
    /// </summary>
    private List<string> MissingMandatoryNames()
    {
        var missing = new List<string>();
        foreach (var name in _config.Mandatory)
        {
            bool found = false;
            foreach (var kv in _roster.ActiveNodes)
            {
                if (string.Equals(kv.Value.SubsystemName, name, StringComparison.OrdinalIgnoreCase)
                    && kv.Value.LocalClusterState == ClusterState.Idle)
                {
                    found = true;
                    break;
                }
            }
            if (!found) missing.Add(name);
        }
        return missing;
    }

    /// <summary>
    /// Does a node holding <paramref name="role"/> exist? ⭐ A node counts only AFTER its capability advert landed (the mask reads
    /// <c>None</c> until then, <c>IngestHeartbeats</c> / R-4005) — and the master's own host counts through <see cref="LocalRoles"/>.
    /// </summary>
    private bool RolePresent(NodeRole role)
        => (LocalRoles & role) != 0
           || _roster.NodesWithRole(role).Any(_nodeCapabilities.ContainsKey);

    /// <summary>The mandatory ROLES with no node yet, as <i>"Brain (CGF)"</i> entries.</summary>
    private List<string> MissingMandatoryRoles()
    {
        var missing = new List<string>();
        var required = _config.MandatoryRoles;
        if (required == NodeRole.None) return missing;
        foreach (var role in new[] { NodeRole.Brain, NodeRole.MuscleGround, NodeRole.Perception, NodeRole.NavigationSolver, NodeRole.Map2D })
            if ((required & role) != 0 && !RolePresent(role))
                missing.Add($"{role} ({TypicalHostOf(role)})");
        return missing;
    }

    /// <summary>
    /// What a load is still waiting for, as <i>"SimHost, Brain (CGF)"</i>; <see langword="null"/> when the mandatory set is complete.
    /// ⭐ The names count while the latch is closed (the latch is their authority once open), the roles always.
    /// </summary>
    private string? WaitingFor()
    {
        var parts = new List<string>();
        if (!_bootstrapLatch) parts.AddRange(MissingMandatoryNames());
        parts.AddRange(MissingMandatoryRoles());
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// Is this request a scenario LOAD the wait applies to? ⭐ It names a scenario AND targets a loading / operating live-or-edit state.
    /// ⛔ A replay, a review, a branch and a dry run name no scenario and are none of this file's business.
    /// </summary>
    private static bool IsScenarioLoad(TransitionStateIntent intent)
        => !string.IsNullOrEmpty(intent.ScenarioId)
           && (FdpClusterState)(int)intent.TargetState is FdpClusterState.LoadingLive or FdpClusterState.LoadingEdit
                                                       or FdpClusterState.OperatingLive or FdpClusterState.OperatingEdit;

    /// <summary>
    /// The wait. Returns <see langword="true"/> when the load must NOT start now (it is held); <see langword="false"/> lets
    /// <see cref="ProcessTransitionStateIntent"/> go on. Called first, before any state is touched.
    /// </summary>
    private bool HoldLoadUntilMandatory(TransitionStateIntent intent)
    {
        if (!IsScenarioLoad(intent)) return false;

        var waitingFor = WaitingFor();
        if (waitingFor == null)
        {
            LoadNotice = null;
            return false;
        }

        var waiting = $"waiting for: {waitingFor}";
        _heldLoad = new HeldLoad(intent, UtcNowSeconds(), waiting);
        LoadNotice = waiting;
        FdpLog<ClusterMaster>.Warn(
            "[Orchestrator] CE-4087: load {0} of '{1}' is {2} — it starts when the mandatory set is present, else it is refused after {3:F0}s.",
            intent.TransactionId, intent.ScenarioId ?? "", waiting, _config.LoadWaitSeconds);
        PublishOpStatus(intent.TransactionId, OrchestrationStatusCode.Pending, waiting);
        return true;
    }

    /// <summary>
    /// Every tick: start the held load once the mandatory set is present, REFUSE it after the wait. ⭐ Runs after the latch is re-checked
    /// and before new intents are admitted (like the parked transition), so a node whose advert landed this frame starts the load this frame.
    /// </summary>
    private void ProcessHeldLoad()
    {
        if (_heldLoad is not { } held) return;

        var waitingFor = WaitingFor();
        if (waitingFor == null)
        {
            _heldLoad = null;
            LoadNotice = null;
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] CE-4087: the mandatory set for load {0} of '{1}' is present ({2:F1}s) — it starts.",
                held.Intent.TransactionId, held.Intent.ScenarioId ?? "", UtcNowSeconds() - held.SinceSeconds);
            ProcessTransitionStateIntent(held.Intent);
            return;
        }

        var waiting = $"waiting for: {waitingFor}";
        if (waiting != held.Waiting)   // something arrived, something else is still missing: say what is left
        {
            _heldLoad = held with { Waiting = waiting };
            LoadNotice = waiting;
            PublishOpStatus(held.Intent.TransactionId, OrchestrationStatusCode.Pending, waiting);
        }

        double waited = UtcNowSeconds() - held.SinceSeconds;
        if (waited < _config.LoadWaitSeconds) return;

        _heldLoad = null;
        var reason = $"load of '{held.Intent.ScenarioId}' refused after {waited:F0}s — {waiting}";
        LoadNotice = reason;
        FdpLog<ClusterMaster>.Error("[Orchestrator] CE-4087: {0}", reason);
        PublishOpStatus(held.Intent.TransactionId, OrchestrationStatusCode.Failure, reason);
    }

    /// <summary>
    /// A request other than the held load arrived: the waiting load is dropped — a newer request replaces it, exactly as
    /// <c>ScenarioLoadSequence.Request</c> does on the caller's side. ⭐ Its asker is told (<c>Cancelled</c>).
    /// </summary>
    private void SupersedeHeldLoad(Guid byRequestId)
    {
        if (_heldLoad is null) { LoadNotice = null; return; }   // a REFUSAL is news only until the next request of any kind
        var held = _heldLoad;
        if (held.Intent.TransactionId == byRequestId) return;
        _heldLoad = null;
        LoadNotice = null;
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] CE-4087: the load {0} that waited for the mandatory set is dropped — request {1} replaces it.",
            held.Intent.TransactionId, byRequestId);
        PublishOpStatus(held.Intent.TransactionId, OrchestrationStatusCode.Cancelled);
    }

    /// <summary>The operator's cancel of the waiting load (<c>R-4004</c>). Returns true when <paramref name="targetId"/> was it.</summary>
    private bool TryCancelHeldLoad(Guid targetId)
    {
        if (_heldLoad is not { } held || held.Intent.TransactionId != targetId) return false;
        _heldLoad = null;
        LoadNotice = null;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-4087: the load {0} that waited for the mandatory set was CANCELLED.", targetId);
        PublishOpStatus(targetId, OrchestrationStatusCode.Cancelled);
        return true;
    }
}

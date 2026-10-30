using Fdp.Toolkit.Orchestration;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ <c>CE-1437</c> (AQ108 §2.10) — the kind of host that builds a run choice. The subsystems that do each name themselves once;
/// the gate policy is looked up from <see cref="HostGatePolicies"/>, never written as a literal at the use site. ⭐ CE-1443: the armed-default table that lived beside it is GONE (R-1043 — Load always loads into the cluster's armed mode; only the master's bootstrap differs).
/// </summary>
public enum RunHostKind
{
    /// <summary>The scenario editor.</summary>
    Editor,
    /// <summary>The CGF (scenario / operator) node.</summary>
    Cgf,
    /// <summary>The SimHost node.</summary>
    SimHost,
    /// <summary>⭐ <c>CE-1439</c> — the Orchestrator (the cluster master's own window): an OPERATOR host with no world of its own.</summary>
    Orchestrator,
    /// <summary>⭐ <c>CE-3345</c> — the IG (image generator): a PLAYER host, not an operator — see <see cref="HostGatePolicies"/>.</summary>
    Ig,
    /// <summary>Any other host (the all-in-one's other subsystems, the server, a test host).</summary>
    Other,
}

/// <summary>
/// ⭐⭐⭐ <c>CE-3345</c> (R-3072, <c>AQ110</c> D) — <b>the ONE gate-policy table</b>, the pattern of the (withdrawn) per-host default table (R-3068): what each kind of host waits on before its controls open.
/// <see cref="HostGatePolicy.Player"/> for the IG (its own <c>PlayReady</c> alone — what the rest of the cluster loads is irrelevant to it); <see cref="HostGatePolicy.Operator"/> for every other kind (Editor, CGF, Orchestrator,
/// SimHost, any other: its own load AND the cluster's must be done). ⛔ Never written as a literal at a host: <c>IProvidesRunControls.GatePolicy</c> reads this table from the host's declared <see cref="RunHostKind"/>,
/// and <c>ClusterView.GateOpen(policy)</c> combines the facts — so no host re-derives it and no two hosts can disagree. ⭐ A new situation is a new policy value here, never a new message.
/// </summary>
public static class HostGatePolicies
{
    /// <summary>The gate policy of a host of this kind: <see cref="RunHostKind.Ig"/> ⇒ <see cref="HostGatePolicy.Player"/>, every other kind ⇒ <see cref="HostGatePolicy.Operator"/>.</summary>
    public static HostGatePolicy For(RunHostKind hostKind) => hostKind == RunHostKind.Ig ? HostGatePolicy.Player : HostGatePolicy.Operator;
}

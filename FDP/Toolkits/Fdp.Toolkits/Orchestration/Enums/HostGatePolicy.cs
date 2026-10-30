namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> part B (R-3072, <c>AQ110</c> D) — <b>who waits on what</b>: the per-host-kind gate policy, a bootstrap parameter of the shared code (the <c>RunArmedDefaults</c> / R-3068 pattern —
    /// the table is <c>HostGatePolicies.For(RunHostKind)</c>, ⛔ never re-derived per host). <c>ClusterView.GateOpen(policy)</c> combines the facts the view already exposes.
    /// </summary>
    public enum HostGatePolicy
    {
        /// <summary>An operator host (Editor, CGF, Orchestrator): open when the cluster can be operated, this node is not loading, and the cluster is not loading — or has finished (<c>ClusterPlayReady</c>).</summary>
        Operator = 0,
        /// <summary>A player host (IG): open on its OWN <c>PlayReady</c> alone (plus the cluster being operable, minus the cluster-play-ready clause) — what the rest of the cluster is loading is irrelevant to it.</summary>
        Player = 1,
    }
}

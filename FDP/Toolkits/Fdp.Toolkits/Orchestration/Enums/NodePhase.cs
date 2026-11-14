namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> part B (R-3072, <c>AQ110</c> A) — <b>what a node reports about ITSELF on its heartbeat</b>: a fact about the node only, no rule in it. The master combines the
    /// participants' phases into <c>ClusterView.ClusterPlayReady</c>; a host's <see cref="HostGatePolicy"/> decides who waits on what — a new situation is a new policy value, never a new message.
    /// </summary>
    /// <remarks>⛔ <b>PERMANENT WIRE VALUES (R-42)</b> — travels as an <c>int</c> on <c>NodeHeartbeat.Phase</c>. ⭐ <see cref="Ready"/> is <b>0 on purpose</b>: a sender that never sets the field (an older peer)
    /// reads as the node simply being ready, which is what its heartbeat meant before.</remarks>
    public enum NodePhase : int
    {
        /// <summary>Idle (or any rest state), initialisation done, nothing being loaded. Also what an unset wire field means.</summary>
        Ready = 0,
        /// <summary>Not ready yet: the capability advert (the explicit ready report) has not gone out — the host's init is not done (<c>ClusterSlave.HoldReady</c>) or the first <c>Tick</c> has not run.</summary>
        Booting = 1,
        /// <summary>A scenario load's prepare is in flight or held on this node (knowledge base · terrain · scenario …); <c>Step</c> names the step.</summary>
        Loading = 2,
        /// <summary>A late joiner's catch-up load (the universal parts only) is in flight on this node.</summary>
        CatchingUp = 3,
        /// <summary>The node committed an <c>Operating*</c> state: its world is loaded and it plays.</summary>
        PlayReady = 4,
        /// <summary>The node's last prepare failed (or was cancelled by the user); <c>Reason</c> says why. Cleared by the next transaction.</summary>
        Failed = 5,
    }
}

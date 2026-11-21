namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3346</c> (R-3072, <c>AQ110</c> E) — <b>a node joining while a scenario load is PARKED</b>: a deployment choice, a bootstrap parameter of the master
    /// (<c>ClusterConfiguration.AdmissionPolicy</c>, code-only). A node joining a cluster at rest in <c>Idle</c> is admitted at once under every value.
    /// <para>⛔ <c>RefuseUntilLoaded</c> (AQ110 E's third value) is NOT built — only when a deployment needs it.</para>
    /// </summary>
    public enum AdmissionPolicy
    {
        /// <summary>Today's behaviour: a node joining while a load is parked (waiting for its files — targets not chosen yet) is admitted into that load.</summary>
        JoinTheLoadWhenParked = 0,
        /// <summary>A node joining while a load is parked is NOT admitted into it: it waits in the roster, not in sync, and is caught up (C7) once the cluster operates.</summary>
        Lobby = 1,
    }
}

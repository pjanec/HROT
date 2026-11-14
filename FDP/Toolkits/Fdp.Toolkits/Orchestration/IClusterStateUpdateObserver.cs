namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> PW (§12c W2) — optional extension for an <see cref="IClusterStateHandler"/> that waits for the MASTER's commit
    /// of a round it took part in: <c>ClusterSlave</c> calls <see cref="OnClusterStateUpdate"/> for every cluster-state broadcast it
    /// receives (the master broadcasts one when a round completes on every node — a replay seek, a checkpoint restore). ⭐ Same shape as
    /// <see cref="IIdleResetHandler"/>. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12a.
    /// </summary>
    public interface IClusterStateUpdateObserver : IClusterStateHandler
    {
        /// <summary>Called on the frame thread, in the frame the broadcast arrived. <paramref name="masterGone"/> — the session ended.</summary>
        void OnClusterStateUpdate(ClusterState state, bool masterGone);
    }
}

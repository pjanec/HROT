namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐ <c>CE-3329</c> P3 (F20) — the ONE predicate for "the cluster shows a recording": an ordinary replay or a review of the live
    /// run. The behaviour both share (the replay's gates, the sim axis, world-changing commands refused) asks this; a place where the
    /// two differ (a review's only exit is back to live) names the state explicitly.
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §11b F20, §8 row 41.
    /// </summary>
    public static class ClusterStates
    {
        public static bool IsReplaying(ClusterState s) => s is ClusterState.OperatingReplay or ClusterState.OperatingReview;

        public static bool IsReview(ClusterState s) => s is ClusterState.LoadingReview or ClusterState.OperatingReview or ClusterState.UnloadingReview;
    }
}

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-4000</c> F2 / <c>R-4019</c> — optional extension for an <see cref="IClusterStateHandler"/> that owns the node's
    /// world: <c>ClusterSlave</c> calls <see cref="OnEnteredIdle"/> when the node ENTERS <c>Idle</c> — by a committed transition
    /// or by dropping to <c>Idle</c> on its own (a new cluster session, a gone master). Not called while the node stays in
    /// <c>Idle</c>. ⭐ Same shape as <see cref="ITickableClusterStateHandler"/>.
    /// </summary>
    public interface IIdleResetHandler : IClusterStateHandler
    {
        /// <summary>Called on the frame thread, in the frame the node entered <c>Idle</c>.</summary>
        void OnEnteredIdle();
    }
}

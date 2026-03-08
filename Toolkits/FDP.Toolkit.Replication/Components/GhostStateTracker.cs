using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    /// <summary>
    /// Tracks the birth frame of a ghost entity, enabling the
    /// <see cref="FDP.Toolkit.Replication.Systems.GhostPromotionSystem"/> to honour
    /// soft-timeout requirements and the
    /// <see cref="FDP.Toolkit.Replication.Systems.GhostTimeoutSystem"/> to destroy
    /// ghosts that never receive their <c>EntityMaster</c> packet.
    ///
    /// Attached by <see cref="FDP.Toolkit.Replication.Systems.GhostCreationSystem"/>
    /// at ghost creation time and removed by <c>GhostPromotionSystem</c> upon successful
    /// promotion to <c>EntityLifecycle.Constructing</c>.
    /// </summary>
    [ComponentId(GlobalComponentIds.GhostStateTracker)]
    public struct GhostStateTracker
    {
        /// <summary>
        /// Simulation frame at which the ghost entity was first created.
        /// Used by <c>GhostPromotionSystem</c> to evaluate <c>SoftTimeoutFrames</c>
        /// and by <c>GhostTimeoutSystem</c> to destroy stale ghosts.
        /// </summary>
        public uint FirstSeenFrame;
    }
}

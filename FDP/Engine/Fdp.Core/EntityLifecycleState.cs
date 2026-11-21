namespace Fdp.Core
{
    /// <summary>
    /// Represents the lifecycle state of an entity.
    /// Distributed lifecycle management ensures entities are fully initialized before becoming Active.
    /// </summary>
    public enum EntityLifecycle : byte
    {
        /// <summary>
        /// Entity is allocated but not yet fully initialized/acknowledged by all modules.
        /// </summary>
        Constructing = 0,
        
        /// <summary>
        /// Entity is fully initialized and active in the simulation.
        /// </summary>
        Active = 1,
        
        /// <summary>
        /// <summary>
        /// Entity is scheduled for destruction and is being cleaned up by modules.
        /// </summary>
        TearDown = 2,
        
        /// <summary>
        /// Entity created from network state, awaiting EntityMaster
        /// </summary>
        Ghost = 4,
        
        /// <summary>
        /// ⭐ CE-4007 / CE-4000 residual — special value for queries: every state BUT <see cref="TearDown"/>. What an egress publishes from
        /// (a constructing entity must announce itself; a dying one must never be written again after its delete went out — measured live,
        /// docs/DESIGN_Cluster_Load_Phase.md §8.3).
        /// </summary>
        AllButTearDown = 254,

        /// <summary>
        /// Special value for queries to include all states.
        /// </summary>
        All = 255
    }
}

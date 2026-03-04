using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Utilities
{
    /// <summary>
    /// Static utility that encapsulates dirty-tracking and throttling logic for
    /// descriptor publication. Replaces the defunct <c>SmartEgressSystem</c>
    /// demand-driven checks and is called directly from egress translators.
    /// </summary>
    public static class SmartEgressUtil
    {
        // 10 seconds refresh interval at 60 Hz to ensure eventual consistency
        // for unreliable (UDP) packets dropped in transit.
        private const uint REFRESH_INTERVAL = 600;

        /// <summary>
        /// Returns <c>true</c> if the descriptor should be published this tick.
        /// </summary>
        /// <param name="view">Current simulation view.</param>
        /// <param name="entity">Entity being evaluated.</param>
        /// <param name="descriptorOrdinal">The ordinal key of the descriptor to check.</param>
        /// <param name="isUnreliable">
        ///   <c>true</c> for best-effort / UDP topics (enables heartbeat refresh);
        ///   <c>false</c> for reliable topics (publish only on dirty).
        /// </param>
        public static bool ShouldPublish(
            ISimulationView view,
            Entity entity,
            long descriptorOrdinal,
            bool isUnreliable = false)
        {
            // If the entity has no publication-state component we cannot track it.
            // Default to safe behaviour: publish, so no data is silently dropped.
            if (!view.HasManagedComponent<EgressPublicationState>(entity))
            {
                return true;
            }

            var state = view.GetManagedComponentRO<EgressPublicationState>(entity);
            uint currentTick = view.Tick;

            // 1. Explicit dirty check — set by mutation-observer or business logic.
            if (state.DirtyDescriptors.Contains(descriptorOrdinal))
            {
                return true;
            }

            // 2. Reliable descriptors: publish exactly once on first encounter,
            //    then only when explicitly dirtied.
            //    IMPORTANT: EgressPublicationState may have been created by a
            //    different translator (e.g. GeoSpatial) for a different ordinal.
            //    We must check LastPublishedTickMap per-ordinal, not just whether
            //    the component exists, to avoid silently skipping first-publish.
            if (!isUnreliable)
            {
                return !state.LastPublishedTickMap.ContainsKey(descriptorOrdinal);
            }

            // 3. Unreliable heartbeat — resend on a salted rolling window to
            //    recover from packet loss without all entities firing at once.
            if (state.LastPublishedTickMap.TryGetValue(descriptorOrdinal, out uint lastTick))
            {
                uint salt = (uint)(entity.Index % REFRESH_INTERVAL);
                uint tickPhase = (currentTick + salt) % REFRESH_INTERVAL;
                return tickPhase == 0;
            }

            // Never published before — publish now.
            return true;
        }

        /// <summary>
        /// Records that a descriptor was successfully published.
        /// Clears the dirty flag and updates the last-published tick map.
        /// </summary>
        public static void MarkPublished(
            ISimulationView view,
            Entity entity,
            long descriptorOrdinal)
        {
            if (view is not EntityRepository repo)
                return; // Need write access via concrete repository.

            EgressPublicationState state;
            if (repo.HasManagedComponent<EgressPublicationState>(entity))
            {
                // For class components, RO still returns the mutable reference.
                state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
            }
            else
            {
                // Lazily register the managed component table if this is the first use.
                repo.RegisterManagedComponent<EgressPublicationState>();
                state = new EgressPublicationState();
                repo.SetManagedComponent(entity, state);
            }

            state.DirtyDescriptors.Remove(descriptorOrdinal);
            state.LastPublishedTickMap[descriptorOrdinal] = repo.GlobalVersion;
        }

        /// <summary>
        /// Explicitly marks a descriptor as dirty to force publication on the next tick.
        /// Call this whenever the underlying component data changes.
        /// </summary>
        public static void MarkDirty(EntityRepository repo, Entity entity, long descriptorOrdinal)
        {
            EgressPublicationState state;
            bool hadState = repo.HasManagedComponent<EgressPublicationState>(entity);
            if (hadState)
            {
                // For class components, RO still returns the mutable reference.
                state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
            }
            else
            {
                // Lazily register the managed component table if this is the first use.
                repo.RegisterManagedComponent<EgressPublicationState>();
                state = new EgressPublicationState();
                repo.SetManagedComponent(entity, state);
            }

            state.DirtyDescriptors.Add(descriptorOrdinal);
        }
    }
}

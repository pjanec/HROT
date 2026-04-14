using Fdp.Kernel;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network.Messages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Runtime.ConstrainedExecution;
using System.Runtime.Intrinsics.X86;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Fdp.Toolkit.Replication.Utilities
{
    /// <summary>
    /// Static utility that encapsulates dirty-tracking and throttling logic for
    /// descriptor publication.
    /// </summary>
    /// <remarks
    /// ### The Advantages of `SmartEgressUtil` (What it's good for)
    /// 
    /// `SmartEgressUtil` was designed to handle **Discrete, Reliable, Complex Data** (like `EntityMission`, `EntityInfo`, and `EntityMaster`). 
    /// 
    /// 1. **Guaranteed "First Publish":** It tracks whether a descriptor has *ever* been sent for a newly spawned entity (`!state.LastPublishedTickMap.ContainsKey`). If you remove this, you will have to manually write boilerplate in every single translator to ensure new entities get their baseline data broadcasted to the network when they spawn.
    /// 2. **O(1) Dirty Flagging for Complex Data:** Imagine trying to write state-comparison logic for `EntityMission`. You would have to deep-compare lists of tasks, string parameters, and active phases every single frame just to see if it changed. That is horribly slow. Instead, the system that changes the mission simply calls `SmartEgressUtil.MarkDirty()`, and the egress system instantly knows to send it.
    /// 3. **Decoupling:** It decouples the business logic (which modifies data) from the networking layer (which sends data). 
    /// 
    /// ### What you will lose if you stop using it completely
    /// If you delete `SmartEgressUtil`, you will break your reliable replication pipeline.
    /// *   Your `EntityMaster`, `EntityInfo`, and `EntityMission` egress translators will have no idea when they are supposed to send data. 
    /// *   You will have to write expensive, custom "Did this change?" comparison code for every single component type in your game.
    /// *   You will lose the standard "Heartbeat" logic that ensures UDP drops are eventually corrected.
    /// 
    /// ### Why `SmartEgressUtil` is bad for `GeoSpatial`
    /// While `SmartEgressUtil` is fantastic for low-frequency events, it is **terrible for high-frequency physics**.
    /// 
    /// Look at how `EgressPublicationState` is defined:
    /// ```csharp
    /// [DataPolicy(DataPolicy.Transient)]
    /// [ComponentId(GlobalComponentIds.EgressPublicationState)]
    /// public class EgressPublicationState
    /// {
    ///     public Dictionary<long, uint> LastPublishedTickMap { get; } = new();
    ///     public HashSet<long> DirtyDescriptors { get; } = new();
    /// }
    /// ```
    /// This is a **managed class** (`class`, not `struct`). To use it, the ECS must follow a pointer to the heap. Furthermore, checking if it should publish requires hashing a key and doing lookups in a `Dictionary` and a `HashSet`. 
    /// 
    /// If you have 10,000 cars moving at 60 FPS, calling `ShouldPublish` means doing 600,000 dictionary lookups per second, per descriptor, generating massive overhead and cache misses.
    /// 
    /// ### The Recommended Architecture: A Split Strategy
    /// 
    /// To get the best of both worlds, you should use a **two-tiered egress strategy**:
    /// 
    /// #### 1. For Low-Frequency / Reliable Data (Keep using SmartEgressUtil)
    /// For `EntityMaster`, `EntityInfo`, `WeaponState`, and `EntityMission`, use `SmartEgressUtil` exactly as it was originally written (before your patch).
    /// *   **How it works:** When a player changes a tank's mission, the UI/Logic system calls `SmartEgressUtil.MarkDirty()`. The egress translator sees the flag, sends the packet, and clears the flag. It costs virtually zero CPU time when things aren't changing.
    /// 
    /// #### 2. For High-Frequency / Unreliable Data (Use State Comparison)
    /// For `GeoSpatial` and `GeoSpatialDR`, **do not use `SmartEgressUtil`**. 
    ///     *   **How it works:** Use purely unmanaged ECS math. Compare the live `SimTransform` against a shadow component (`NetworkTransform`). 
    /// *   **Why?** Because comparing two `Vector3` structs that sit sequentially in memory takes less than a nanosecond and doesn't touch the heap, HashSets, or Dictionaries.
    /// </remarks>

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

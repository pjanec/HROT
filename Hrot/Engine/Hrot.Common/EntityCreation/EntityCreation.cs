#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Hrot.Common.Systems;
using Hrot.Core.Network;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// What <see cref="EntityCreationPack.Build"/> produced. ⛔ Nothing here is scheduled — the host
    /// registers the <b>four</b> systems with its own kernel and then calls
    /// <see cref="Unserviceable"/> so an omission is loud instead of silent.
    /// <para>⚠ <b>FOUR since <c>P2</c> (<c>2026-09-11</c>)</b> — <see cref="PromotionSystem"/> joined when
    /// ghost promotion's registrar moved out of <c>NedReplicationModule</c>.</para>
    /// </summary>
    public sealed class EntityCreation
    {
        internal EntityCreation(
            IReadOnlyList<ITkbEntityTranslator> translators,
            EntityLifecycleModule elm,
            ScenarioEntityCreationRequestSource localRequests,
            CreateEntityRequestSystem requestSystem,
            EntityRequestFinalizationSystem finalizationSystem,
            NetworkSpawningSystem spawnSystem,
            Fdp.Toolkit.Replication.Systems.GhostPromotionSystem promotionSystem)
        {
            Translators        = translators;
            Elm                = elm;
            LocalRequests      = localRequests;
            RequestSystem      = requestSystem;
            FinalizationSystem = finalizationSystem;
            SpawnSystem        = spawnSystem;
            PromotionSystem    = promotionSystem;
        }

        /// <summary>
        /// ⭐ The ONE list instance for this node. It has already been handed to
        /// <see cref="Elm"/> and <see cref="SpawnSystem"/>. ⚠ Pass <b>this same instance</b> to anything
        /// else that projects TKB descriptors — notably the replication module's ghost promotion —
        /// rather than calling <c>TkbTranslatorSet.Base()</c> again. 📄 <c>tkb-1/DESIGN.md</c> §6.3.
        /// </summary>
        public IReadOnlyList<ITkbEntityTranslator> Translators { get; }

        /// <summary>The lifecycle module, with <see cref="Translators"/> already set on it.</summary>
        public EntityLifecycleModule Elm { get; }

        /// <summary>
        /// ⭐⭐ <b>The local, in-process request queue — this is how a node creates an entity it OWNS.</b>
        /// Enqueue an <c>EntityCreationRequest</c> with <c>OwnerAppInstanceId = NodeId</c> and
        /// <see cref="RequestSystem"/> drains it on the next tick; no DDS round trip.
        ///
        /// <para>⚠ Thread-safe: <c>Enqueue</c> may be called from an orchestration thread, and draining
        /// happens on the ECS tick.</para>
        ///
        /// <para>⚠ The typed authoring affordances that wrap this (<c>CreateLocallyOwned</c> /
        /// <c>RequestFromDefaultProcessor</c>, <c>DESIGN</c> §3.4) land with <c>Q65-A′</c> + <c>CE-143</c>,
        /// because they need a <c>ReliableInitType</c> the request does not carry yet.</para>
        /// </summary>
        public ScenarioEntityCreationRequestSource LocalRequests { get; }

        /// <summary>Turns requests into orders. Schedule this.</summary>
        public CreateEntityRequestSystem RequestSystem { get; }

        /// <summary>Dispatches phase-2 ACKs once the lifecycle confirms. Schedule this.</summary>
        public EntityRequestFinalizationSystem FinalizationSystem { get; }

        /// <summary>Turns orders into live entities. Schedule this.</summary>
        public NetworkSpawningSystem SpawnSystem { get; }

        /// <summary>
        /// ⭐⭐⭐ <b><c>P2</c> — applies the TKB template to an arrived ghost and advances its lifecycle.
        /// Schedule this.</b> 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.7, §6 step <c>0a</c>.
        ///
        /// <para>⭐ Its registrar moved here from <c>NedReplicationModule.RegisterSystems</c> — one network
        /// implementation — because promotion consumes the TKB and the translator list, neither of which is
        /// networked. ⭐ Ghost CREATION stays on <c>IReplicationModule</c>, where it belongs.</para>
        ///
        /// <para>⭐⭐ <b>Ordering needs nothing from the host:</b> the system carries
        /// <c>[UpdateAfter(typeof(GhostCreationSystem))]</c>, so *creation precedes promotion* is true by
        /// construction rather than by which registrar the host wired first. And <c>[SingleInstance]</c>
        /// makes a double registration throw at <c>BeginRun()</c> instead of promoting twice a frame.</para>
        /// </summary>
        public Fdp.Toolkit.Replication.Systems.GhostPromotionSystem PromotionSystem { get; }

        /// <summary>
        /// ⭐⭐ <b>The <c>S2b</c> diagnostic habit: report what the pack built and the host did NOT
        /// schedule.</b> 📌 Every one of the five entity-creation defects that produced this design was a
        /// SILENT omission — this is the mechanism that makes the next one loud.
        ///
        /// <para>Pass whatever the host actually registered. Returns an empty string when nothing is
        /// missing, otherwise a human-readable list naming each unscheduled piece.</para>
        /// </summary>
        /// <example>
        /// <code>
        /// var scheduled = new object[] { creation.RequestSystem, creation.SpawnSystem };
        /// var missing = creation.Unserviceable(scheduled);
        /// if (missing.Length > 0) FdpLog&lt;MyHost&gt;.Warn(missing);
        /// </code>
        /// </example>
        public string Unserviceable(IEnumerable<object> scheduled)
        {
            var seen = new HashSet<object>(scheduled ?? Enumerable.Empty<object>(),
                                           ReferenceEqualityComparer.Instance);

            var missing = new List<string>(4);
            // ⭐⭐ P2 — the FOURTH row, and it is the one this mechanism was most needed for: the other
            //   three were new capabilities a host had never had, while promotion is a capability every
            //   host ALREADY HAD from its replication module. ⇒ a host that adopts the pack and forgets
            //   to schedule this would SILENTLY LOSE ghost promotion — the exact regression shape the
            //   design warns about ("a host that has not adopted the pack would LOSE ghost promotion the
            //   moment the NED module stops registering it").
            if (!seen.Contains(PromotionSystem))
                missing.Add($"{nameof(PromotionSystem)} (GhostPromotionSystem) — arrived ghosts will " +
                            "never get their TKB projection and will stay in EntityLifecycle.Ghost " +
                            "forever. ⚠ This USED to be registered by NedReplicationModule; if this host " +
                            "relied on that, scheduling it here is not optional");
            if (!seen.Contains(RequestSystem))
                missing.Add($"{nameof(RequestSystem)} (CreateEntityRequestSystem) — this node cannot " +
                            "process entity-creation requests, including ones it targets at itself");
            if (!seen.Contains(SpawnSystem))
                missing.Add($"{nameof(SpawnSystem)} (NetworkSpawningSystem) — orders will never become entities");
            if (!seen.Contains(FinalizationSystem))
                missing.Add($"{nameof(FinalizationSystem)} — phase-2 ACKs will never be dispatched, so a " +
                            "requester waits forever");

            if (missing.Count == 0) return string.Empty;

            return "EntityCreationPack built pieces this host did not schedule: " +
                   string.Join(" · ", missing);
        }
    }
}

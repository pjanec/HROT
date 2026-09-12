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
            Fdp.Toolkit.Replication.Systems.GhostPromotionSystem promotionSystem,
            int nodeId)
        {
            Translators        = translators;
            Elm                = elm;
            LocalRequests      = localRequests;
            RequestSystem      = requestSystem;
            FinalizationSystem = finalizationSystem;
            SpawnSystem        = spawnSystem;
            PromotionSystem    = promotionSystem;
            NodeId             = nodeId;
        }

        /// <summary>
        /// ⭐⭐ <b>This node's app-instance id — the value an author passes as <c>owner</c> to say
        /// <i>"I own this one."</i></b>
        ///
        /// <para>📄 <c>docs/DESIGN_Entity_Authoring_Surface.md</c> §4. ⚠ <b>It was NOT here before that
        /// design was built</b> — §4 asserted <i>"<c>creation.NodeId</c> is already on it"</i> and §6's
        /// class diagram drew it as an existing member; both were wrong, and the design carries the
        /// correction. The value was already a composition input (<c>EntityCreationContext.NodeId</c>),
        /// handed to the request system and the spawn system; it simply was never surfaced, so an author
        /// wanting <c>owner: myNodeId</c> had to reach back to the host for a number the pack held.</para>
        /// </summary>
        public int NodeId { get; }

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
        /// <para>⭐⭐⭐ <b>AUTHORS SHOULD NOT TOUCH THIS — call <see cref="RequestEntityCreation"/>.</b>
        /// ⚠ An earlier version of this remark promised two affordances, <c>CreateLocallyOwned</c> and
        /// <c>RequestFromDefaultProcessor</c> (<c>DESIGN_Entity_Creation_Unification.md</c> §3.4),
        /// blocked on <c>CE-143</c>. ⛔ <b>That two-method shape is SUPERSEDED</b> — the routing input is
        /// ONE field, so two verbs named the ARGUMENT rather than the behaviour, and neither could
        /// express <i>"owned by a third node"</i>. 📄 <c>DESIGN_Entity_Authoring_Surface.md</c> §4b.
        /// ⭐ <c>CE-143</c> is resolved (<c>EntityCreationRequest.InitType</c> exists), and the one
        /// method below shipped in its place. This property stays public because a TRANSLATOR (§2)
        /// legitimately enqueues a fully-determined request.</para>
        /// </summary>
        public ScenarioEntityCreationRequestSource LocalRequests { get; }

        /// <summary>
        /// ⭐⭐⭐ <b>THE AUTHORING AFFORDANCE — ask for an entity to exist.</b> Returns the request id,
        /// which is what a <c>CreateUpdateDeleteEntityAck</c> is keyed by.
        ///
        /// <para>📄 <c>docs/DESIGN_Entity_Authoring_Surface.md</c> §4 (the API), §4b (why ONE method and
        /// not two), §4c (the sequence this sets in motion, including the double ACK).</para>
        ///
        /// <para>⭐⭐ <b>WHO MUST USE IT — the AUTHOR/TRANSLATOR rule</b> (§2, and it is the load-bearing
        /// rule of that design):
        /// <list type="bullet">
        ///   <item>⭐⭐⭐ <b>AUTHOR</b> — a NEW INTENT originates here: a human gesture, an AI decision, a
        ///     tool. Nothing outside has decided the fields yet. <b>It MUST call this method.</b></item>
        ///   <item>⛔ <b>TRANSLATOR</b> — an EXISTING external representation is being mapped in: a
        ///     scenario file, a DDS sample, another node's message. The representation already fixed the
        ///     owner, the components and the id. <b>It constructs <c>EntityCreationRequest</c> directly
        ///     onto <see cref="LocalRequests"/>, and that is CORRECT — not a loophole.</b> Forcing it
        ///     through here would add a defaulting layer over fields that are already determined, and it
        ///     needs <c>PreAllocatedNetworkId</c> / <c>ChildComponentOverrides</c>, which this affordance
        ///     deliberately excludes (§3). The two production translators are
        ///     <c>StagingEntityExtractor</c> (scenario load) and <c>NedCgfEntityLifecycleAdapters</c>
        ///     (wire ingress).</item>
        /// </list>
        /// ⚠ <b>Stated as the weaker guarantee it is:</b> since <paramref name="owner"/> accepts any node
        /// id, a translator now *could* call this. The exemption is by DEFINITION above, not by the type
        /// system (§7 R1).</para>
        ///
        /// <para>⛔⛔ <b>SCOPE — NETWORKED entities only</b> (§7b). The trigger is <i>"does this entity
        /// need a network identity?"</i>, ⛔ NOT <i>"am I creating an entity?"</i>. Local probe entities
        /// such as the AI's EQS sensor children are created straight through <c>ctx.World</c> and must
        /// NOT come through here — it would give them a network id and a lifecycle handshake they have
        /// no use for.</para>
        /// </summary>
        /// <param name="tkbType">The TKB entity type code. ⚠ An unknown type is rejected with an
        /// <c>UnknownDescriptorType</c> ack and there is no phase-2 ack.</param>
        /// <param name="transform">Where it starts. ⭐ Folded into the request's component list — the
        /// request system pulls the <see cref="Fdp.Core.SimTransform"/> back out of it
        /// (<c>CreateEntityRequestSystem</c>) — so there is no transform field on the DTO and no second
        /// way to say it.</param>
        /// <param name="initialComponents">Ordinary ECS components to apply at spawn. ⭐ Geometry needs
        /// NOTHING special: <c>EditablePolyline</c> / <c>RoutePlan</c> ride here exactly like
        /// <c>SimTransform</c> does, so the shared base never learns what a polyline is.</param>
        /// <param name="owner">
        /// ⭐⭐⭐ <b>WHO RUNS GENESIS AND OWNS THE RESULT.</b> The sole routing input
        /// (<see cref="EntityCreationRouting.IsHandledLocally"/>), and it has THREE legal values, which
        /// is why this is a parameter and not two verbs:
        /// <see cref="EntityCreationRouting.DefaultEntityCreationRequestProcessor"/> (the default — the
        /// broadcast arbiter owns it), <see cref="NodeId"/> (<i>"mine"</i>), or any other node's id.
        /// ⭐ Whichever it is, the call is identical: the request is enqueued locally, and the pack's
        /// forwarder sends it on if it is addressed elsewhere.
        /// </param>
        /// <param name="initType">⭐ Whether the creator WAITS for peers before the entity goes
        /// <c>Active</c> — a SEPARATE axis from <paramref name="owner"/> (<c>Q65</c> §5.5): owning
        /// something does not mean nobody needs to ack it. ⭐ Pass <c>None</c> for a scratch drawing
        /// nobody should have to ack.</param>
        /// <param name="initialAttributesJson">Operator-supplied property overrides, compiled on top of
        /// the template. ⭐ An AUTHORING input — the human typed it.</param>
        /// <param name="isTransient">⭐ Marks a THROWAWAY that must never reach a saved scenario
        /// (<c>R-140</c>). A third independent axis: a temporary entity may still want peers to ack it.
        /// ⚠ The receiver stamps <c>ScenarioIgnoreTag</c> locally at spawn, so nothing depends on the
        /// author still being alive.</param>
        /// <param name="disType">
        /// ⭐ An explicit OVERRIDE of the TKB template's DIS entity type. <b>Leaving it <c>0</c> is correct
        /// and safe</b>: <c>NetworkSpawningSystem</c> stamps
        /// <c>cmd.DisType != 0 ? cmd.DisType : template.DisType.Value</c>, so the template's value is used
        /// unless an author deliberately supplies a different one (a variant of one template).
        ///
        /// <para>⚠ An earlier version of this remark claimed the value is copied VERBATIM and that leaving
        /// it <c>0</c> leaves the entity with no DIS type. ⛔ That was measured one hop too early —
        /// <c>CreateEntityRequestSystem</c> does copy it verbatim, but the SPAWN system it feeds falls back
        /// to the template, and the only other consumer
        /// (<c>IOwnershipDistributionStrategy.GetInitialGrants</c>) never reads the parameter in its one
        /// production implementation. 📄 <c>docs/DESIGN_Entity_Authoring_Surface.md</c> §3.</para>
        /// </param>
        /// <param name="requestId">Supply one to correlate the two-phase ACK yourself; omit it and one
        /// is minted. ⭐ Either way the value actually used is RETURNED.</param>
        public Guid RequestEntityCreation(
            long                   tkbType,
            Fdp.Core.SimTransform? transform             = null,
            IReadOnlyList<object>? initialComponents     = null,
            int                    owner                 = EntityCreationRouting.DefaultEntityCreationRequestProcessor,
            Fdp.Toolkit.Replication.ReliableInitType initType
                                                         = Fdp.Toolkit.Replication.ReliableInitType.AllPeers,
            string?                initialAttributesJson = null,
            bool                   isTransient           = false,
            ulong                  disType               = 0,
            Guid                   requestId             = default)
        {
            // ⭐ Mint only when the caller did not name its own request. An author that must be told the
            //   outcome supplies one; one that does not care ignores the return value.
            if (requestId == Guid.Empty) requestId = Guid.NewGuid();

            // ⭐⭐ The transform rides in the component list. ⛔ Deliberately NOT a DTO field: the request
            //   system already separates SimTransform/SimVelocity back out of InitialComponents, so a
            //   second channel for the same fact is the duplicate-mechanism trap.
            List<object>? components = null;
            if (initialComponents is { Count: > 0 }) components = new List<object>(initialComponents);
            if (transform.HasValue) (components ??= new List<object>(1)).Add(transform.Value);

            LocalRequests.Enqueue(new Hrot.Core.Network.EntityCreationRequest
            {
                RequestId             = requestId,
                OwnerAppInstanceId    = owner,
                TkbType               = tkbType,
                DisType               = disType,
                InitialComponents     = components,
                InitialAttributesJson = initialAttributesJson,
                InitType              = initType,
                IsTransient           = isTransient,
                // ⛔ PreAllocatedNetworkId and ChildComponentOverrides are NOT exposed: one producer
                //   each, and that producer is the scenario extractor — a TRANSLATOR (§3).
            });

            return requestId;
        }

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

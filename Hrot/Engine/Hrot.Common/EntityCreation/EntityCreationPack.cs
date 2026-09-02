#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Hrot.Core.Tkb;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// ⭐⭐⭐ Everything a node needs to CREATE entities, constructed once, as a unit.
    ///
    /// <para>🔒 <b>User ruling, <c>2026-08-30</c>:</b> <i>"Basic concepts like entity creation should be
    /// shared across subsystems, not relying on that all subsystem do it right and the same way."</i>
    /// 📐 That reliance failed <b>five times in one week</b>, always identically: an optional constructor
    /// argument, a host that held the value and did not pass it, and no error.</para>
    ///
    /// <para>🔒 <b>User ruling, <c>2026-08-31</c> — the governing one:</b> <i>"the shared code for entity
    /// creation support should not restrict any ECS enabled node from creating own networked entities …
    /// no exceptions, not removing capabilities by design, and only concrete authoring code picks the way
    /// it needs."</i> ⇒ ⛔⛔ <b><see cref="Build"/> has NO flag, and no <c>NodeRole</c>, that omits the
    /// request system or the spawn system.</b> A node that never creates entities locally simply never
    /// enqueues a self-targeted request; the capability stays present and costs an idle system.</para>
    ///
    /// <para>⭐⭐ <b>Pack constructs, host schedules.</b> <see cref="EntityCreationContext"/> carries no
    /// <c>ModuleHostKernel</c> — the same structural enforcement <c>MapInteractionContext</c> uses
    /// (<c>UXI-23 S2b</c>), which is what stopped hosts half-composing that pack.</para>
    ///
    /// <para>⚠ <b>What this pack does NOT own</b> — measured, and deliberate:
    /// <list type="bullet">
    ///   <item><b>Ghost lifecycle.</b> <c>NedReplicationModule</c> already registers
    ///     <c>GhostCreationSystem</c> for all roles and <c>GhostPromotionSystem</c> behind a
    ///     <c>NodeRole</c> gate. Registering them here would create a SECOND registrar — the
    ///     duplicate-implementation trap. Widening that gate is <c>Q65-B</c>, in that module.</item>
    ///   <item><b>The TKB catalogue.</b> <see cref="EntityCreationContext.TkbDb"/> is required input,
    ///     never built here — that is what keeps the four <c>HrotEnvironment.CreateTkb()</c> sites from
    ///     diverging.</item>
    ///   <item><b>Component registration.</b> That stays the host's <c>*ComponentRegistry</c>, which is
    ///     the narrowing lever (<c>tkb-1/DESIGN.md</c> §6.5b gate ②).</item>
    /// </list></para>
    ///
    /// <para>⚠ <b>The two AUTHORING AFFORDANCES are not here yet.</b> <c>DESIGN</c> §3.4 specifies
    /// <c>RequestFromDefaultProcessor</c> and <c>CreateLocallyOwned</c>, and they need an explicit
    /// <c>ReliableInitType</c> — which <c>EntityCreationRequest</c> does not carry yet
    /// (<c>CE-143</c>: <c>CreateEntityRequestSystem</c> hardcodes <c>AllPeers</c> at <c>:302</c> and
    /// <c>:397</c>). ⛔ Adding them now would ship a signature that changes immediately. ⇒ ⭐ they land
    /// with <c>Q65-A′</c> + <c>CE-143</c>; this slice is the CONSTRUCTION half only, and it is a pure
    /// composition change.</para>
    ///
    /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3, §3.4, UML §4 ·
    /// <c>docs/blueprints/Architect_Question_65_Entity_Genesis_Uniformity.md</c> §0, §4.</para>
    /// </summary>
    public static class EntityCreationPack
    {
        /// <summary>
        /// Builds the node's entity-creation assembly. ⛔ Does not schedule anything — the caller
        /// registers <see cref="EntityCreation.RequestSystem"/>, <see cref="EntityCreation.SpawnSystem"/>
        /// and <see cref="EntityCreation.FinalizationSystem"/> with its own kernel, then calls
        /// <see cref="EntityCreation.Unserviceable"/> to make any omission loud.
        /// </summary>
        public static EntityCreation Build(EntityCreationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            ctx.Validate();

            // ⭐ ONE list instance for the whole node — tkb-1/DESIGN.md §6.3: "identical for all three
            //   systems within the same node". Handing the SAME instance to the ELM and the spawn system
            //   is what makes that true BY CONSTRUCTION rather than by convention.
            // ⛔ Both forms are ADD-ONLY: there is no way to pass a narrower list. Per-component
            //   narrowing is gate ② (IsComponentTypeRegistered), never the list.
            // ⭐⭐ CE-146 — TranslatorPlacements is the ORDER-SENSITIVE form, for the one host that has a
            //   positional contract (the Stride editor's InfantryVehicleStateStrip, which must follow
            //   VehicleKinematicsTkbTranslator). ⛔ Setting both is a composition mistake, not a merge:
            //   two ways to say one thing is the duplicate-mechanism trap, so it throws.
            bool hasExtras     = ctx.ExtraTranslators     is { Count: > 0 };
            bool hasPlacements = ctx.TranslatorPlacements is { Count: > 0 };
            if (hasExtras && hasPlacements)
            {
                throw new ArgumentException(
                    "EntityCreationContext: set ExtraTranslators OR TranslatorPlacements, not both. " +
                    "ExtraTranslators is the append-only shorthand; express every addition as a " +
                    "TranslatorPlacement when any one of them is order-sensitive.");
            }

            IReadOnlyList<ITkbEntityTranslator> translators =
                hasPlacements ? TkbTranslatorSet.BaseWith(ctx.TranslatorPlacements!.ToArray())
              : hasExtras     ? TkbTranslatorSet.BasePlus(ctx.ExtraTranslators!.ToArray())
                              : TkbTranslatorSet.Base();

            ctx.Elm.SetTranslators(translators);   // ⚠ must precede the kernel's Initialize

            // ⭐⭐ The LOCAL request source. This is what makes path 2 (a node creating an entity it
            //   OWNS) possible without a DDS round trip: an authoring site enqueues a request targeted
            //   at this node, and the request system below drains it on the next tick.
            //   📌 CGF already composed exactly this shape; the pack generalises it.
            var localRequests = new ScenarioEntityCreationRequestSource();

            // ⭐⭐⭐ D1 — when the host can reach the cluster, the LOCAL source is WRAPPED so a request
            //   addressed to another node is sent there instead of being silently dropped by the
            //   Level-1 guard. 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4b
            //
            //   ⛔⛔ The wrap goes INSIDE the composite, around the LOCAL source only. Wrapping the
            //   composite would put the forwarder on the merged stream, where it cannot distinguish a
            //   locally-authored request from one that ARRIVED from a peer — and it would re-forward
            //   the latter, bouncing it between nodes forever. Wrapping here makes a wire-originated
            //   request structurally unreachable to the forwarder.
            //
            //   ⚠ A null egress is a LEGITIMATE optional, not the silent-default defect: it states
            //   "this host does not forward", which is true of every host that materialises entities
            //   itself. ⭐ The rail EntityCreationPack_WiresTheForwarder_WhenAnEgressIsSupplied is the
            //   control that a host which HAS an egress actually gets one.
            IEntityCreationRequestSource localTier = ctx.RequestEgress == null
                ? localRequests
                : new ForwardingEntityCreationRequestSource(
                    localRequests, ctx.RequestEgress, ctx.NodeId, ctx.IsBroadcastArbiter);

            var sources = new List<IEntityCreationRequestSource> { localTier };
            if (ctx.NetworkRequestSource != null) sources.Add(ctx.NetworkRequestSource);
            var requestSource = new CompositeEntityCreationRequestSource(sources);

            IEntityAckSink ackSink = ctx.AckSink ?? new NullEntityAckSink();

            var finalization = new EntityRequestFinalizationSystem(ackSink, ctx.EntityMap);

            // ⭐⭐⭐ isDefaultProcessor is a BROADCAST TIEBREAKER, not an authority gate — a request
            //   targeted at THIS node is processed regardless of it (CreateEntityRequestSystem:151-156,
            //   and the comment above that guard says so). ⇒ this bool is the ONLY value that differs
            //   between nodes, and exactly one node in the cluster sets it true, for Owner == 0
            //   broadcasts from non-ECS clients like ExCon.
            var requestSystem = new CreateEntityRequestSystem(
                requestSource:         requestSource,
                ackSink:               ackSink,
                tkbDb:                 ctx.TkbDb,
                idAllocator:           ctx.IdAllocator,
                localNodeId:           ctx.NodeId,
                jsonAttributeCompiler: ctx.JsonAttributeCompiler,
                finalizationSystem:    finalization,
                isDefaultProcessor:    ctx.IsBroadcastArbiter,
                ownershipStrategy:     ctx.OwnershipStrategy);

            var spawnSystem = new NetworkSpawningSystem(
                ctx.TkbDb,
                ctx.Elm,
                ctx.EntityMap,
                ctx.IdAllocator,
                ctx.NodeId,
                // ⭐⭐⭐ CE-147 step 4 — no onEntitySpawned. It was the pack's LAST per-host hole:
                //   an OPTIONAL Action that exactly ONE production host ever passed, carrying AX-011's
                //   egress-shadow attach. That attach now lives in GeoSpatialEgressTranslator, where it is
                //   true for every owning host by construction, so the parameter has nothing left to carry.
                //   ⛔ Do not reintroduce it to move an invariant into a composition root — that is the
                //   SILENT-DEFAULT shape: an optional dependency one caller happens to pass and the next
                //   host forgets. 📄 docs/DESIGN_Cgf_AxisB_Rotation_Slice.md §13.7.
                translators: translators);

            return new EntityCreation(
                translators, ctx.Elm, localRequests, requestSystem, finalization, spawnSystem);
        }
    }
}

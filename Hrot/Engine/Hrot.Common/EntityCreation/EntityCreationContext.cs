#nullable enable
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Abstractions;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Hrot.Core.Network;
using Hrot.Core.Tkb;

namespace Hrot.Common.EntityCreation
{
    /// <summary>
    /// Inputs to <see cref="EntityCreationPack.Build"/>.
    ///
    /// <para>⛔⛔ <b>There is deliberately NO <c>ModuleHostKernel</c> here.</b> The pack constructs; the
    /// host schedules. That is structural, not a convention — it is the <c>MapInteractionContext</c>
    /// precedent (<c>UXI-23 S2b</c>), and it is why a host cannot half-compose this.</para>
    ///
    /// <para>⛔⛔ <b>And there is deliberately NO flag that omits a system.</b> 🔒 User ruling
    /// <c>2026-08-31</c>: the shared code must not <i>"restrict any ECS enabled node from creating own
    /// networked entities … not removing capabilities by design"</i>. ⇒ every node that builds this pack
    /// gets the full request + spawn tier; <see cref="IsBroadcastArbiter"/> is the only value that
    /// differs.</para>
    /// </summary>
    public sealed class EntityCreationContext
    {
        // ── Required ────────────────────────────────────────────────────────────────────────────

        /// <summary>The node's ECS world.</summary>
        public required EntityRepository World { get; init; }

        /// <summary>The node's network-id ↔ entity map, shared with replication.</summary>
        public required NetworkEntityMap EntityMap { get; init; }

        /// <summary>
        /// The TKB catalogue. ⭐ <b>Required input, never built here</b> — one catalogue per process is
        /// what keeps the four <c>HrotEnvironment.CreateTkb()</c> sites from diverging.
        /// </summary>
        public required ITkbDatabase TkbDb { get; init; }

        /// <summary>Network id allocator — <c>DdsIdAllocator</c> when networked, sequential offline.</summary>
        public required INetworkIdAllocator IdAllocator { get; init; }

        /// <summary>
        /// The node's lifecycle module. ⚠ <b>Passed in, not created</b> — hosts obtain it from
        /// <c>HrotNodeContext.BaseModules[0]</c>, and the pack must wire the SAME instance the kernel
        /// already knows about.
        /// </summary>
        public required EntityLifecycleModule Elm { get; init; }

        /// <summary>This node's id, used as <c>OwnerNodeId</c> and for the self-targeting guard.</summary>
        public required int NodeId { get; init; }

        // ── The one value that differs between nodes ───────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>The ONLY per-node difference.</b> When <c>true</c> this node intercepts broadcast
        /// requests where <c>Owner == 0</c> — those from non-ECS clients like ExCon. ⛔ <b>Exactly one
        /// node in a cluster may set it</b>, or two nodes allocate ids for one request.
        ///
        /// <para>⚠ It is NOT an authority gate: a request targeted at a node is processed by that node
        /// regardless of this flag. 📄 <c>Q65</c> §1.</para>
        /// </summary>
        public bool IsBroadcastArbiter { get; init; }

        // ── Optional ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The DDS ingress source, when this node is networked. ⭐ The pack always composes its own
        /// in-memory source and merges this one behind <c>CompositeEntityCreationRequestSource</c>, so an
        /// offline node still has a working local path.
        /// </summary>
        public IEntityCreationRequestSource? NetworkRequestSource { get; init; }

        /// <summary>ACK sink; <c>NullEntityAckSink</c> is substituted when absent (offline / headless).</summary>
        public IEntityAckSink? AckSink { get; init; }

        /// <summary>
        /// ⭐ <b>ADD-ONLY.</b> Translators that cannot live in <c>Hrot.Core</c> for reference-graph
        /// reasons — e.g. <c>AiDiagnosticsTkbTranslator</c> (<c>Hrot.SimHost</c>). ⛔ There is no way to
        /// pass a NARROWER list than <c>TkbTranslatorSet.Base()</c>; per-component narrowing is gate ②.
        /// </summary>
        public IReadOnlyList<ITkbEntityTranslator>? ExtraTranslators { get; init; }

        /// <summary>
        /// ⭐⭐ The ORDER-SENSITIVE form of <see cref="ExtraTranslators"/> — each addition says whether it
        /// appends or must immediately follow a named translator type.
        ///
        /// <para>📌 <c>CE-146</c>. The Stride editor has the one such contract in the tree:
        /// <c>InfantryVehicleStateStripTkbTranslator</c> must run immediately after
        /// <c>VehicleKinematicsTkbTranslator</c>. <c>BasePlus</c> APPENDS, so <see cref="ExtraTranslators"/>
        /// alone cannot express it — 🔒 <c>R-137</c>: unification must not quietly drop a capability, it
        /// gets put back as configuration.</para>
        ///
        /// <para>⛔ Set EITHER this or <see cref="ExtraTranslators"/>, never both — two ways to say one
        /// thing is the duplicate-mechanism trap, so <see cref="EntityCreationPack.Build"/> throws.
        /// ⭐ <see cref="ExtraTranslators"/> stays the simple form and is exactly this list with every
        /// placement appending.</para>
        /// </summary>
        public IReadOnlyList<TranslatorPlacement>? TranslatorPlacements { get; init; }

        /// <summary>Optional JSON attribute-override compiler for <c>InitialAttributesJson</c>.</summary>
        public JsonAttributeCompiler? JsonAttributeCompiler { get; init; }

        /// <summary>
        /// Optional ownership-distribution POLICY, e.g. <c>BrainMuscleOwnershipStrategy</c>. ⚠ Only
        /// consulted when <see cref="IsBroadcastArbiter"/> is true — a non-arbiter creator keeps every
        /// component, which is exactly right for a node creating something it owns outright.
        /// </summary>
        public IOwnershipDistributionStrategy? OwnershipStrategy { get; init; }

        
        /// <summary>Throws when a required input is missing, naming the field.</summary>
        internal void Validate()
        {
            if (World       == null) throw new ArgumentException("EntityCreationContext.World is required.");
            if (EntityMap   == null) throw new ArgumentException("EntityCreationContext.EntityMap is required.");
            if (TkbDb       == null) throw new ArgumentException("EntityCreationContext.TkbDb is required.");
            if (IdAllocator == null) throw new ArgumentException("EntityCreationContext.IdAllocator is required.");
            if (Elm         == null) throw new ArgumentException("EntityCreationContext.Elm is required.");
        }
    }
}

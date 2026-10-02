using System;

namespace Hrot.Core.Network;

/// <summary>
/// ⭐⭐ <b>Sends an entity-creation request to the node that should service it.</b>
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b (<c>D1</c>).</para>
///
/// <para>📐 <b>Measured <c>2026-09-02</c>: no such seam existed.</b> Two ad-hoc writers of the DDS
/// <c>CreateEntityRequest</c> did the job for their own callers —
/// <c>NedExConEgressWriters.WriteCreateEntity</c> (the operator console, always untargeted) and
/// <c>SpawnEntityCommandEgressTranslator</c> (IG, and at the wrong LEVEL — it reads the local
/// <c>SpawnEntityCommand</c> ORDER rather than the request). ⇒ this is the general form.</para>
///
/// <para>⛔ <b>Deliberately protocol-neutral</b>, like every other seam in this file: the NED and BDC
/// stacks implement it, and an offline host simply has none.</para>
/// </summary>
public interface IEntityCreationRequestEgress
{
    /// <summary>Transmits <paramref name="request"/> to the cluster for the owner to service.</summary>
    void Send(EntityCreationRequest request);
}

/// <summary>
/// ⭐⭐⭐ <b><c>D1</c> — the forwarder. Decorates the node's LOCAL request source: a request this node
/// should service is passed through; one addressed elsewhere is sent over the wire.</b>
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b. 🔒 The user's ruling that produced
/// it: <i>"Can't the request contain the desired creator/owner of the entity which will solve this?"</i>
/// — it can, and the reason it did not work was that the forwarding listened one level too low.</para>
///
/// <para>🔴 <b>The defect this replaces.</b> <c>SpawnEntityCommandEgressTranslator</c> subscribes to
/// <c>SpawnEntityCommand</c> — the node-local ORDER the request system publishes once it has already
/// decided to act. On a host that also materialises entities, both consumers read the same non-draining
/// bus, so one gesture produced two entities. And in the other direction a request addressed to a remote
/// owner published no order at all, so nothing was ever forwarded. ⇒ <b>neither owner value worked</b>,
/// which is why the fix is a level change and not a filter.</para>
///
/// <para>⭐⭐ <b>Why it wraps the LOCAL source and not the merged stream.</b>
/// <see cref="CompositeEntityCreationRequestSource"/> merges the local queue with the network source,
/// and by design <c>CreateEntityRequestSystem</c> cannot tell them apart. ⛔ A forwarder placed on the
/// merged stream would re-forward a request that ARRIVED from a peer and is not addressed here — an
/// unbounded bounce between nodes. ⇒ wrapping the local source makes a wire-originated request
/// <b>structurally unreachable</b> to this class. ⭐ No origin flag, and the mistake cannot be made.</para>
///
/// <para>⭐ <b>Composition:</b> <c>new CompositeEntityCreationRequestSource(new[] {
/// new ForwardingEntityCreationRequestSource(localSource, egress, nodeId, isDefaultProcessor),
/// networkSource })</c> — the wrap goes INSIDE the composite, never around it.</para>
/// </summary>
public sealed class ForwardingEntityCreationRequestSource : IEntityCreationRequestSource
{
    private readonly IEntityCreationRequestSource  _local;
    private readonly IEntityCreationRequestEgress  _egress;
    private readonly int                           _localNodeId;
    private readonly bool                          _isDefaultProcessor;

    /// <summary>Number of requests forwarded to the wire. Diagnostics and rails only.</summary>
    public int ForwardedCount { get; private set; }

    /// <param name="local">
    /// ⛔ The node's LOCAL request source ONLY — typically the <c>ScenarioEntityCreationRequestSource</c>
    /// that authoring code enqueues into. Passing a composite that includes the network source
    /// re-introduces the bounce this class exists to prevent.
    /// </param>
    /// <param name="egress">Where a request for another node is sent.</param>
    /// <param name="localNodeId">This node's id.</param>
    /// <param name="isDefaultProcessor">
    /// Whether this node intercepts untargeted requests. ⭐ A node that is NOT the default processor
    /// forwards untargeted requests instead of dropping them — which is a capability GAIN: today such a
    /// request is silently discarded by the Level-1 guard and nothing is created anywhere.
    /// </param>
    public ForwardingEntityCreationRequestSource(
        IEntityCreationRequestSource local,
        IEntityCreationRequestEgress egress,
        int                          localNodeId,
        bool                         isDefaultProcessor = false)
    {
        _local              = local  ?? throw new ArgumentNullException(nameof(local));
        _egress             = egress ?? throw new ArgumentNullException(nameof(egress));
        _localNodeId        = localNodeId;
        _isDefaultProcessor = isDefaultProcessor;
    }

    /// <inheritdoc/>
    public void ProcessRequests(Action<EntityCreationRequest> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        _local.ProcessRequests(request =>
        {
            // ⭐ ONE routing rule, shared with CreateEntityRequestSystem's guard (EntityCreationRouting).
            //   ⛔ Never re-implement the test here: two copies that disagree service a request twice, or
            //   never. That is the whole reason the predicate was extracted.
            if (EntityCreationRouting.IsHandledLocally(request, _localNodeId, _isDefaultProcessor))
            {
                handler(request);
                return;
            }

            _egress.Send(request);
            ForwardedCount++;
        });
    }
}

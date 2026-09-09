using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Abstractions;

namespace Hrot.Core.Network;

/// <summary>
/// Protocol-specific entity lifecycle adapters for a CGF (Brain) node.
/// Returned by <see cref="INetworkFactory.CreateCgfEntityLifecycleAdapters"/>.
/// Null returned by factories whose protocol does not support CGF entity creation.
/// </summary>
public interface ICgfEntityLifecycleAdapters
{
    /// <summary>Source of incoming entity-creation requests from the network.</summary>
    IEntityCreationRequestSource RequestSource { get; }

    /// <summary>Source of incoming entity-deletion requests from the network.</summary>
    IEntityDeletionRequestSource DeleteSource { get; }

    /// <summary>Sink for entity lifecycle ACK messages sent back to requesters.</summary>
    IEntityAckSink AckSink { get; }

    /// <summary>
    /// ⭐⭐ Sends a creation request OUTWARD, to the node that should service it — the mirror of
    /// <see cref="RequestSource"/>. Consumed by <c>ForwardingEntityCreationRequestSource</c> via
    /// <c>EntityCreationContext.RequestEgress</c> (D1).
    ///
    /// <para>⚠ <b>Null on a stack that cannot forward</b>, in which case the pack composes no forwarder
    /// and every locally-enqueued request is serviced locally — today's behaviour for every host that
    /// has not adopted forwarding.</para>
    /// </summary>
    IEntityCreationRequestEgress? RequestEgress { get; }

    /// <summary>
    /// Optional strategy that distributes initial descriptor ownership across Muscle nodes.
    /// When null the Brain node retains full ownership of all descriptors.
    /// </summary>
    IOwnershipDistributionStrategy? OwnershipStrategy { get; }

    /// <summary>
    /// Optional compiler that applies InitialAttributesJson overrides during entity creation.
    /// When null, InitialAttributesJson is ignored.
    /// </summary>
    JsonAttributeCompiler? JsonCompiler { get; }

    /// <summary>
    /// Poll the underlying network transport once per update frame.
    /// Typically reads node heartbeats and refreshes the cluster state cache used
    /// by <see cref="OwnershipStrategy"/>.
    /// </summary>
    void PollNetwork();
}

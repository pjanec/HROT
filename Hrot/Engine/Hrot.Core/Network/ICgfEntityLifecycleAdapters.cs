using FDP.Toolkit.Replication.Patching;
using Fdp.ModuleHost.Network.Interfaces;

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

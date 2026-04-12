using System;

namespace Hrot.Core.Network;

/// <summary>
/// Neutral status codes for entity lifecycle ACKs.
/// Integer values are intentionally identical to <c>NedStatusCode</c> so that
/// the NED adapter can cast directly without a lookup table.
/// </summary>
public enum EntityOperationStatus : int
{
    /// <summary>Operation completed successfully.</summary>
    Success = 0,
    /// <summary>Request accepted; final ACK will follow after ECS confirms.</summary>
    InProgress = 1,
    /// <summary>Requested entity type not found in TKB.</summary>
    UnknownDescriptorType = 2,
    /// <summary>Entity ID not found in the network map.</summary>
    EntityNotFound = 3,
}

/// <summary>
/// Pre-parsed entity-creation request received from the network.
/// Simple primitive fields only -- no ECS components, no descriptor unions.
/// </summary>
public sealed class EntityCreationRequest
{
    /// <summary>Unique request identifier used for two-phase ACK tracking.</summary>
    public Guid RequestId { get; init; }

    /// <summary>AppInstanceId of the requesting node (0 = broadcast).</summary>
    public int OwnerAppInstanceId { get; init; }

    /// <summary>TKB entity type code extracted from the EntityMaster descriptor.</summary>
    public long TkbType { get; init; }

    /// <summary>Packed DIS entity type discriminator.</summary>
    public ulong DisType { get; init; }

    /// <summary>
    /// JSON attribute overrides forwarded verbatim from the wire message.
    /// Processed by <c>JsonAttributeCompiler</c> inside <c>CreateEntityRequestSystem</c>.
    /// </summary>
    public string? InitialAttributesJson { get; init; }
}

/// <summary>
/// Pre-parsed entity-deletion request received from the network.
/// </summary>
public sealed class EntityDeletionRequest
{
    /// <summary>Unique request identifier.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Network entity ID to delete.</summary>
    public long EntityId { get; init; }
}

/// <summary>
/// Source of incoming entity-creation requests.
/// Implemented by NED/BDC adapters; tested via stubs.
/// </summary>
public interface IEntityCreationRequestSource
{
    /// <summary>
    /// Drains all pending requests and invokes <paramref name="handler"/> for each.
    /// Callback-based to avoid per-frame List allocations.
    /// </summary>
    void ProcessRequests(Action<EntityCreationRequest> handler);
}

/// <summary>
/// Source of incoming entity-deletion requests.
/// </summary>
public interface IEntityDeletionRequestSource
{
    /// <summary>Drains all pending requests and invokes <paramref name="handler"/> for each.</summary>
    void ProcessRequests(Action<EntityDeletionRequest> handler);
}

/// <summary>
/// Sink for entity lifecycle ACK messages (creation and deletion).
/// Single neutral method covers both create and delete ACKs.
/// </summary>
public interface IEntityAckSink
{
    /// <summary>Publishes a lifecycle ACK back to the original requester.</summary>
    void WriteAck(Guid requestId, long entityId, EntityOperationStatus status);
}

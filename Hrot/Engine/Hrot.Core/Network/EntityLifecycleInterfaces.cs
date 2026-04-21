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

    /// <summary>
    /// Optional pre-converted ECS component instances extracted from the wire
    /// <c>InitialDescriptors</c> by the protocol adapter layer.  These are applied
    /// alongside <see cref="InitialAttributesJson"/> when the entity is spawned.
    /// Examples: <c>EditablePolyline</c> from <c>dtMapVisualOverlay</c>,
    /// <c>RoutePlan</c> from <c>dtMapRoute</c>.
    /// </summary>
    public List<object>? InitialComponents { get; init; }

    /// <summary>
    /// When non-zero, <see cref="Hrot.CGF.Systems.CreateEntityRequestSystem"/> uses
    /// this value directly as the entity's network ID and skips
    /// <c>INetworkIdAllocator.AllocateId()</c>.  Set by
    /// <c>StagingEntityExtractor</c> during scenario load.
    /// </summary>
    public long PreAllocatedNetworkId { get; init; } = 0;

    /// <summary>
    /// Optional per-child component overrides keyed by
    /// <see cref="Fdp.Interfaces.ChildBlueprintDefinition.InstanceId"/>.
    /// Each entry supplies a pre-allocated network ID for the child and a list
    /// of additional ECS components to merge into its initial component set.
    /// When <c>null</c>, all children use the normal <c>AllocateId()</c> path.
    /// </summary>
    public IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>?
        ChildComponentOverrides { get; init; } = null;
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

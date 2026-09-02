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
    /// When non-zero, <c>Hrot.Common.Systems.CreateEntityRequestSystem</c> uses
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

    /// <summary>
    /// ⭐⭐ <c>CE-143</c> — <b>whether the creator WAITS for peers to ACK before the entity goes
    /// <c>Active</c>.</b>
    ///
    /// <para>📌 <c>CreateEntityRequestSystem</c> hardcoded <c>ReliableInitType.AllPeers</c> at BOTH its
    /// publish sites, and this request carried no way to say otherwise. ⇒ an IG tactical drawing created
    /// through a self-targeted request was held in <c>Constructing</c> until every expected peer
    /// returned a <c>ConstructionAck</c> — pointless latency for a single-owner presentation entity,
    /// and a stall if a peer is absent or slow.</para>
    ///
    /// <para>⛔ <b>This is a SEPARATE axis from <see cref="OwnerAppInstanceId"/>, and they must not be
    /// conflated</b> (<c>Architect_Question_65</c> §5.5): the owner decides <i>who runs genesis</i>;
    /// this decides <i>whether the creator waits</i>. *"I own this"* does not imply *"nobody needs to
    /// ACK it"* — a node can locally own something genuinely simulated.</para>
    ///
    /// <para>⭐ <b>Defaults to <see cref="ReliableInitType.AllPeers"/>, which is exactly what the two
    /// hardcoded sites did</b>, so every existing caller behaves identically — acceptance ⑥'s
    /// byte-identical default holds.</para>
    /// </summary>
    public Fdp.Toolkit.Replication.ReliableInitType InitType { get; init; }
        = Fdp.Toolkit.Replication.ReliableInitType.AllPeers;

    /// <summary>
    /// ⭐⭐⭐ <b><c>D2</c> — whether this entity is a THROWAWAY that must never reach a saved scenario.</b>
    ///
    /// <para>🔒 <b>The ruling (user, <c>2026-09-02</c>, <c>R-140</c>):</b> a passive node such as an IG
    /// creates only temporary entities — <i>"if IG crashes, its entities are gone, but no one cares, they
    /// were temporary anyway"</i>. 📄 <c>docs/DESIGN_Node_Roles_And_Policies.md</c> §5, §7.3.</para>
    ///
    /// <para>📐 <b>Why the flag is needed even though IG never saves.</b> IG genuinely does not answer the
    /// cluster-wide save — it registers no <c>SerializeLocal</c> handler. ⛔ But an entity it creates
    /// <b>replicates into every peer's world</b>, including the nodes that DO save, and
    /// <c>ScenarioSerializer.CollectSaveableEntities</c> writes every live entity except those bearing
    /// <see cref="Fdp.Toolkit.Scenario.ScenarioIgnoreTag"/>. ⇒ without this, an operator's sketch is
    /// saved by CGF — indistinguishable in the file from a real unit, since ownership is stripped at save
    /// time.</para>
    ///
    /// <para>⭐⭐ <b>The mechanism already existed and had ZERO production writers.</b>
    /// <c>ScenarioIgnoreTag</c> is a per-<i>entity</i> filter (unlike <c>DataPolicy.NoSave</c>, which is
    /// per component <i>type</i>) and is itself <c>NoSave</c>, so it never round-trips. This flag is only
    /// the CARRIER that lets it be stamped; no save-side machinery changes.</para>
    ///
    /// <para>⛔ <b>Why the REQUEST carries it rather than the receiver deriving it from the owner's
    /// role:</b> once the originating node disconnects, a receiver can no longer resolve that role and the
    /// sketch would <b>silently become permanent</b>. The author knows; the roster may not. Every receiver
    /// therefore derives the tag locally at spawn, so nothing depends on the tag replicating or on the
    /// author still being alive.</para>
    ///
    /// <para>⭐ <b>Defaults to <c>false</c></b> — every existing caller, and every scenario load, behaves
    /// identically. This is a SEPARATE axis from both <see cref="OwnerAppInstanceId"/> and
    /// <see cref="InitType"/>: owning something does not make it temporary, and a temporary entity may
    /// still legitimately want peers to ACK it.</para>
    /// </summary>
    public bool IsTransient { get; init; }
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

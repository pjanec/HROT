using System;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Modules.Geographic;
using Hrot.Core.Network;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Common;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.CGF;

/// <summary>
/// ⭐⭐ <b>Bit assignments for the wire <c>CreateEntityRequest.Flags</c> field.</b>
///
/// <para>📐 <b>Measured <c>2026-09-02</c>: the field was written as literal <c>0</c> by both existing
/// producers and read by nobody</b> — an unused <c>Int64</c> already on the contract. It is the natural
/// carrier for per-request booleans that must survive forwarding.</para>
/// </summary>
public static class EntityCreationRequestFlags
{
    /// <summary>No flags.</summary>
    public const long None = 0L;

    /// <summary>
    /// ⭐⭐⭐ <b>The entity is TRANSIENT and must never be persisted to a scenario</b>
    /// (<c>D2</c>, <c>R-140</c>: an IG's sketches are disposable).
    ///
    /// <para>🔴 <b>Why this had to exist.</b> <c>D2</c> built the flag on the LOCAL path only —
    /// <c>EntityCreationRequest.IsTransient</c> reaches <c>NetworkSpawningSystem</c>, which stamps
    /// <c>ScenarioIgnoreTag</c>. ⛔ But a request addressed to another node is FORWARDED, and the wire
    /// had no carrier: the owning node would have materialised the sketch as an ordinary, saveable
    /// entity. ⇒ without this bit, <c>D2</c>'s guarantee stops at the node boundary, which is precisely
    /// the case host (f) introduces — IG addressing a persisting node.</para>
    /// </summary>
    public const long Transient = 1L << 0;

    /// <summary>
    /// Decodes the transient claim from a wire sample's flags. ⭐ Both ends go through this, so the
    /// encode/decode pair can be railed rather than asserted twice in prose.
    /// </summary>
    public static bool IsTransient(long flags) => (flags & Transient) != 0;

    /// <summary>Encodes a request's transient claim into the wire flags word.</summary>
    public static long Encode(bool isTransient) => isTransient ? Transient : None;
}

/// <summary>
/// ⭐⭐⭐ <b>NED implementation of <see cref="IEntityCreationRequestEgress"/> — sends an entity-creation
/// INTENT to the node that should service it.</b>
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b (<c>D1</c>), host (f).</para>
///
/// <para>⭐ <b>What it replaces.</b> <c>SpawnEntityCommandEgressTranslator</c> put the same DDS sample on
/// the wire, but subscribed to the node-local <c>SpawnEntityCommand</c> ORDER — one level too low, which
/// is what produced the double-spawn hazard and left a remotely-addressed request unforwarded. This class
/// is driven by <see cref="ForwardingEntityCreationRequestSource"/> instead, so it is reached only for a
/// request this node should NOT service. ⛔ The descriptor construction is unchanged: it delegates to
/// <see cref="CreateEntityRequestDescriptorBuilder"/>, which the retired translator also used
/// (<c>R-137</c> — the capability moves, it is not lost).</para>
/// </summary>
public sealed class NedEntityCreationRequestEgress : IEntityCreationRequestEgress, IDisposable
{
    private readonly IDdsWriter<CreateEntityRequest> _writer;
    private readonly IGeographicTransform? _geoTransform;
    private readonly long _localNodeId;

    /// <summary>Number of requests written to the wire. Diagnostics and rails only.</summary>
    public long SentSampleCount { get; private set; }

    /// <summary>Production constructor: creates a live DDS writer.</summary>
    public NedEntityCreationRequestEgress(
        DdsParticipant participant,
        IGeographicTransform? geoTransform,
        long localNodeId = 0)
        : this(new DdsWriterAdapter<CreateEntityRequest>(participant, "CreateEntityRequest"),
               geoTransform, localNodeId)
    {
    }

    /// <summary>Testable constructor: accepts an injected writer stub.</summary>
    public NedEntityCreationRequestEgress(
        IDdsWriter<CreateEntityRequest> writer,
        IGeographicTransform? geoTransform,
        long localNodeId = 0)
    {
        _writer       = writer ?? throw new ArgumentNullException(nameof(writer));
        _geoTransform = geoTransform;
        _localNodeId  = localNodeId;
    }

    /// <inheritdoc/>
    public void Send(EntityCreationRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var sample = CreateEntityRequestDescriptorBuilder.Build(
            requestId:             request.RequestId,
            tkbType:               request.TkbType,
            initialAttributesJson: request.InitialAttributesJson,
            anchor:                CreateEntityRequestDescriptorBuilder.ResolveAnchor(
                                       explicitTransform: null, request.InitialComponents),
            initialComponents:     request.InitialComponents,
            geoTransform:          _geoTransform);

        // ⭐ The address travels: the builder is owner-agnostic, so the routing fields are applied here.
        sample.Owner = new NodeId { AppDomainId = 0, AppInstanceId = request.OwnerAppInstanceId };
        sample.Flags = EntityCreationRequestFlags.Encode(request.IsTransient);

        _writer.Write(sample);
        SentSampleCount++;

        FdpLog<NedEntityCreationRequestEgress>.Debug(
            "[Node-{0}] forwarded EntityCreationRequest req={1} owner={2} transient={3}",
            _localNodeId, sample.RequestId, request.OwnerAppInstanceId, request.IsTransient);
    }

    public void Dispose()
    {
        if (_writer is IDisposable d) d.Dispose();
    }
}

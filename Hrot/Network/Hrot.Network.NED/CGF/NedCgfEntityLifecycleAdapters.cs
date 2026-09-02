using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Modules.Geographic;
using Hrot.Core.Network;
using Hrot.Map.Common.Replication.Utils;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.CGF;

/// <summary>
/// DDS-backed source of <c>CreateEntityRequest</c> messages.
/// Converts NED wire messages to the neutral <see cref="EntityCreationRequest"/> DTO.
///
/// Design: TkbType and DisType are extracted from the EntityMaster descriptor.
/// Non-master descriptors (dtWorldPos, dtEntityInfo, dtMapVisualOverlay, dtMapRoute, etc.)
/// are converted to ECS components via <see cref="DescriptorMapper.MapToComponents"/> and
/// placed in <see cref="EntityCreationRequest.InitialComponents"/>.
/// InitialAttributesJson is passed through unchanged and applied on top as an override.
/// </summary>
public sealed class NedEntityCreationRequestSource : IEntityCreationRequestSource
{
    private readonly DdsReader<CreateEntityRequest> _reader;
    private readonly IGeographicTransform?          _geoTransform;

    public NedEntityCreationRequestSource(DdsParticipant participant, IGeographicTransform? geoTransform = null)
    {
        _reader       = new DdsReader<CreateEntityRequest>(participant);
        _geoTransform = geoTransform;
    }

    public void ProcessRequests(Action<EntityCreationRequest> handler)
    {
        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var msg = sample.Data;

            // Extract TkbType and DisType from EntityMaster descriptor only.
            long  tkbType = 0;
            ulong disType = 0;

            if (msg.InitialDescriptors != null)
            {
                foreach (var desc in msg.InitialDescriptors)
                {
                    if (desc._d == EDescriptorType.dtEntityMaster)
                    {
                        tkbType = desc.EntityMaster.TkbType;
                        var d = desc.EntityMaster.DisType;
                        disType = ((ulong)d.Kind        << 56)
                                | ((ulong)d.Domain      << 48)
                                | ((ulong)d.Country     << 32)
                                | ((ulong)d.Category    << 24)
                                | ((ulong)d.Subcategory << 16)
                                | ((ulong)d.Specific    <<  8)
                                |  (ulong)d.Extra;
                        break;
                    }
                }
            }

            // Convert non-master descriptors (position, entity info, overlay, route, etc.)
            // into ECS component instances so downstream systems don't need NED knowledge.
            List<object>? initialComponents = null;
            if (msg.InitialDescriptors != null && msg.InitialDescriptors.Count > 0)
            {
                var mapped = DescriptorMapper.MapToComponents(msg.InitialDescriptors, _geoTransform);
                if (mapped.Count > 0)
                    initialComponents = mapped;
            }

            handler(new EntityCreationRequest
            {
                RequestId             = msg.RequestId,
                OwnerAppInstanceId    = msg.Owner.AppInstanceId,
                TkbType               = tkbType,
                DisType               = disType,
                InitialAttributesJson = msg.InitialAttributesJson,
                InitialComponents     = initialComponents,
                // D2 across the node boundary: a forwarded sketch must stay unsaveable on the node that
                // materialises it. See EntityCreationRequestFlags.Transient for why the wire needed a
                // carrier at all -- IsTransient previously reached only the LOCAL spawn path.
                IsTransient           = EntityCreationRequestFlags.IsTransient(msg.Flags),
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS-backed source of <c>DeleteEntityRequest</c> messages.
/// </summary>
public sealed class NedEntityDeletionRequestSource : IEntityDeletionRequestSource
{
    private readonly DdsReader<DeleteEntityRequest> _reader;

    public NedEntityDeletionRequestSource(DdsParticipant participant)
        => _reader = new DdsReader<DeleteEntityRequest>(participant);

    public void ProcessRequests(Action<EntityDeletionRequest> handler)
    {
        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            handler(new EntityDeletionRequest
            {
                RequestId = sample.Data.RequestId,
                EntityId  = sample.Data.EntityId,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// DDS-backed ACK sink for entity lifecycle operations.
/// Writes <c>CreateUpdateDeleteEntityAck</c> for both creation and deletion ACKs.
/// </summary>
public sealed class NedEntityAckSink : IEntityAckSink
{
    private readonly DdsWriter<CreateUpdateDeleteEntityAck> _writer;

    public NedEntityAckSink(DdsParticipant participant)
        => _writer = new DdsWriter<CreateUpdateDeleteEntityAck>(participant);

    public void WriteAck(Guid requestId, long entityId, EntityOperationStatus status)
        => _writer.Write(new CreateUpdateDeleteEntityAck
        {
            RequestId  = requestId,
            EntityId   = (int)entityId,
            StatusCode = (int)status,
        });

    public void Dispose() => _writer.Dispose();
}

using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
using Fdp.Toolkit.DER;
using Hrot.Core.Network;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.ExCon;

// ── Event queue bridging handlers ─────────────────────────────────────────────

/// <summary>
/// Reads NED <see cref="MapClickEvent"/> DDS samples and enqueues neutral
/// <see cref="MapClickEventDto"/> values via a callback.
/// </summary>
public sealed class NedMapClickIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MapClickEvent>   _reader;
    private readonly Action<MapClickEventDto>   _enqueue;
    private readonly int                        _maxSamples;
    private readonly long                       _localNodeId;

    public NedMapClickIngressHandler(
        DdsParticipant            participant,
        Action<MapClickEventDto>  enqueue,
        int                       maxSamples  = 10,
        long                      localNodeId = 0)
    {
        _reader      = new DdsReader<MapClickEvent>(participant, "MapClickEvent");
        _enqueue     = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        _maxSamples  = Math.Max(1, maxSamples);
        _localNodeId = localNodeId;
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d = sample.Data;
            FdpLog<NedMapClickIngressHandler>.Debug(
                "[Node-{0}] MapClickEvent ingress ctx={1}", _localNodeId, d.InteractionContextId);

            var hits = new List<int>(d.HitStack?.Count ?? 0);
            if (d.HitStack != null)
                foreach (var h in d.HitStack) hits.Add(h.EntityId);

            _enqueue(new MapClickEventDto
            {
                InteractionContextId = d.InteractionContextId,
                Latitude             = d.Position.Latitude,
                Longitude            = d.Position.Longitude,
                Altitude             = d.Position.Altitude,
                HitEntityIds         = hits,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Reads NED <see cref="SelectionChangedEvent"/> DDS samples and enqueues neutral
/// <see cref="SelectionChangedEventDto"/> values via a callback.
/// </summary>
public sealed class NedSelectionChangedIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<SelectionChangedEvent>       _reader;
    private readonly Action<SelectionChangedEventDto>       _enqueue;
    private readonly int                                    _maxSamples;

    public NedSelectionChangedIngressHandler(
        DdsParticipant                  participant,
        Action<SelectionChangedEventDto> enqueue,
        int                             maxSamples = 10)
    {
        _reader     = new DdsReader<SelectionChangedEvent>(participant, "SelectionChangedEvent");
        _enqueue    = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d = sample.Data;
            var ids = new List<int>(d.SelectedEntityIds?.Count ?? 0);
            if (d.SelectedEntityIds != null)
                foreach (var id in d.SelectedEntityIds) ids.Add(id);

            _enqueue(new SelectionChangedEventDto
            {
                MapId             = d.MapId,
                SelectedEntityIds = ids,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Reads NED <see cref="CreateUpdateDeleteEntityAck"/> DDS samples and enqueues neutral
/// <see cref="EntityLifecycleAckDto"/> values via a callback.
/// </summary>
public sealed class NedEntityLifecycleAckIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<CreateUpdateDeleteEntityAck> _reader;
    private readonly Action<EntityLifecycleAckDto>          _enqueue;
    private readonly int                                    _maxSamples;
    private readonly long                                   _localNodeId;

    public NedEntityLifecycleAckIngressHandler(
        DdsParticipant               participant,
        Action<EntityLifecycleAckDto> enqueue,
        int                          maxSamples  = 10,
        long                         localNodeId = 0)
    {
        _reader      = new DdsReader<CreateUpdateDeleteEntityAck>(participant, "CreateUpdateDeleteEntityAck");
        _enqueue     = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        _maxSamples  = Math.Max(1, maxSamples);
        _localNodeId = localNodeId;
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d = sample.Data;
            FdpLog<NedEntityLifecycleAckIngressHandler>.Debug(
                "[Node-{0}] CreateUpdateDeleteEntityAck ingress req={1} entityId={2} status={3}",
                _localNodeId, d.RequestId, d.EntityId, d.StatusCode);

            _enqueue(new EntityLifecycleAckDto
            {
                RequestId  = d.RequestId,
                EntityId   = d.EntityId,
                StatusCode = d.StatusCode,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Reads NED <see cref="MapCommandAck"/> DDS samples and enqueues neutral
/// <see cref="MapCommandAckDto"/> values via a callback.
/// </summary>
public sealed class NedMapCommandAckIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MapCommandAck>    _reader;
    private readonly Action<MapCommandAckDto>    _enqueue;
    private readonly int                         _maxSamples;
    private readonly long                        _localNodeId;

    public NedMapCommandAckIngressHandler(
        DdsParticipant            participant,
        Action<MapCommandAckDto>  enqueue,
        int                       maxSamples  = 10,
        long                      localNodeId = 0)
    {
        _reader      = new DdsReader<MapCommandAck>(participant, "MapCommandAck");
        _enqueue     = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        _maxSamples  = Math.Max(1, maxSamples);
        _localNodeId = localNodeId;
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d = sample.Data;
            FdpLog<NedMapCommandAckIngressHandler>.Debug(
                "[Node-{0}] MapCommandAck ingress req={1} status={2}",
                _localNodeId, d.RequestId, d.StatusCode);

            _enqueue(new MapCommandAckDto
            {
                RequestId  = d.RequestId,
                StatusCode = (int)d.StatusCode,
                DataJson   = d.DataJson,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Reads NED <see cref="ContextActionInvoked"/> DDS samples and forwards neutral
/// <see cref="ContextActionInvokedDto"/> values via a callback.
/// </summary>
public sealed class NedContextActionInvokedIngressHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<ContextActionInvoked>        _reader;
    private readonly Action<ContextActionInvokedDto>        _enqueue;
    private readonly int                                    _maxSamples;

    public NedContextActionInvokedIngressHandler(
        DdsParticipant                   participant,
        Action<ContextActionInvokedDto>  enqueue,
        int                              maxSamples = 10)
    {
        _reader     = new DdsReader<ContextActionInvoked>(participant, "ContextActionInvoked");
        _enqueue    = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d = sample.Data;
            _enqueue(new ContextActionInvokedDto
            {
                MapId    = d.MapId,
                ActionId = d.ActionId,
                EntityId = d.ContextEntityId,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

// ── DER neutral descriptor bridging handlers ──────────────────────────────────

/// <summary>
/// Reads NED <see cref="EntityInfo"/> DDS samples and stores both the NED descriptor
/// and a neutral <see cref="EntityInfoDescriptor"/> on the DER entity.
/// ExCon uses <see cref="EntityInfoDescriptor"/> to avoid a direct NED dependency.
/// </summary>
public sealed class NedEntityInfoBridgingHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<EntityInfo> _reader;
    private readonly IDerRepo              _repo;
    private readonly int                   _maxSamples;

    public NedEntityInfoBridgingHandler(
        DdsParticipant participant,
        IDerRepo       repo,
        int            maxSamples = 10)
    {
        _reader     = new DdsReader<EntityInfo>(participant, "EntityInfo");
        _repo       = repo ?? throw new ArgumentNullException(nameof(repo));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d      = sample.Data;
            var entity = _repo.GetEntity(d.EntityId);
            if (entity == null) continue;

            entity.SetDescriptor(d);
            entity.SetDescriptor(new EntityInfoDescriptor
            {
                EntityId    = d.EntityId,
                Name        = d.Name ?? string.Empty,
                Affiliation = d.ForceIdentifier.ToString(),
                CommanderId = d.CommanderId,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Reads NED <see cref="EntityMission"/> DDS samples and stores both the NED descriptor
/// and a neutral <see cref="EntityMissionDescriptor"/> on the DER entity.
/// </summary>
public sealed class NedEntityMissionBridgingHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<EntityMission> _reader;
    private readonly IDerRepo                 _repo;
    private readonly int                      _maxSamples;

    public NedEntityMissionBridgingHandler(
        DdsParticipant participant,
        IDerRepo       repo,
        int            maxSamples = 10)
    {
        _reader     = new DdsReader<EntityMission>(participant, "EntityMission");
        _repo       = repo ?? throw new ArgumentNullException(nameof(repo));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d      = sample.Data;
            var entity = _repo.GetEntity((int)d.EntityId);
            if (entity == null) continue;

            entity.SetDescriptor(d);

            // Derive optimistic-lock version from the DescriptorOptimisticLock if present.
            long version = entity.HasDescriptor<DescriptorOptimisticLock>()
                ? entity.GetDescriptor<DescriptorOptimisticLock>().CurrentVersion
                : 0;

            entity.SetDescriptor(new EntityMissionDescriptor
            {
                EntityId = (int)d.EntityId,
                Plan     = NedMissionHelper.ToNeutral(d.Plan),
                Version  = version,
            });
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// Reads NED <see cref="MapVisualOverlay"/> DDS samples and stores both the NED descriptor
/// and a neutral <see cref="MapOverlayDescriptor"/> on the DER entity.
/// </summary>
public sealed class NedMapOverlayBridgingHandler : IIngressHandler, IDisposable
{
    private readonly DdsReader<MapVisualOverlay> _reader;
    private readonly IDerRepo                    _repo;
    private readonly int                         _maxSamples;

    public NedMapOverlayBridgingHandler(
        DdsParticipant participant,
        IDerRepo       repo,
        int            maxSamples = 10)
    {
        _reader     = new DdsReader<MapVisualOverlay>(participant, "MapVisualOverlay");
        _repo       = repo ?? throw new ArgumentNullException(nameof(repo));
        _maxSamples = Math.Max(1, maxSamples);
    }

    public void Poll()
    {
        using var loan = _reader.Take(_maxSamples);
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            var d      = sample.Data;
            var entity = _repo.GetEntity(d.EntityId);
            if (entity == null) continue;

            entity.SetDescriptor(d);
            entity.SetDescriptor(new MapOverlayDescriptor { EntityId = d.EntityId, IsEditable = d.IsEditable });
        }
    }

    public void Dispose() => _reader.Dispose();
}
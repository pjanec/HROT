using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.Map.Common.Components;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress;

/// <summary>
/// Ingress translator for the <c>MapRoute</c> DDS topic.
///
/// <para>
/// Receives <see cref="MapRoute"/> DDS samples and writes the decoded waypoints
/// into the target entity's <see cref="RoutePlan"/> managed component. Geodetic
/// coordinates in the DDS payload are converted to local Cartesian world-space
/// via <see cref="IGeographicTransform.ToCartesian"/>.
/// </para>
/// <para>
/// If the target entity is not yet registered in <see cref="NetworkEntityMap"/>
/// (e.g. the <c>EntityMaster</c> sample has not been processed yet), the sample
/// is placed in a retry queue and re-processed on the next
/// <see cref="PollIngress"/> call.
/// </para>
/// </summary>
public class MapRouteIngressTranslator : IDescriptorTranslator
{
    private const string DdsTopicName = "MapRoute";
    private const long OrdinalValue = (long)EDescriptorType.dtMapRoute;

    private readonly DdsReader<MapRoute>? _reader;
    private readonly NetworkEntityMap _entityMap;
    private readonly IGeographicTransform _geoTransform;

    /// <summary>
    /// Samples deferred because the entity was not yet registered in
    /// <see cref="NetworkEntityMap"/> when the sample arrived.
    /// Key: DDS EntityId (network ID); Value: the pending sample.
    /// </summary>
    private readonly Dictionary<long, MapRoute> _pendingRoutes = new();

    /// <summary>
    /// Network IDs registered since the last <see cref="PollIngress"/> call.
    /// Populated by the <see cref="NetworkEntityMap.EntityRegistered"/> callback so
    /// that the retry loop only inspects newly-arrived entities rather than scanning
    /// the entire <see cref="_pendingRoutes"/> dictionary on every tick.
    /// </summary>
    private readonly HashSet<long> _recentlyRegistered = new();

    public string TopicName => DdsTopicName;
    public long DescriptorOrdinal => OrdinalValue;

    public MapRouteIngressTranslator(
        DdsParticipant? participant,
        NetworkEntityMap entityMap,
        IGeographicTransform geoTransform)
    {
        _reader       = participant is not null ? new DdsReader<MapRoute>(participant) : null;
        _entityMap    = entityMap    ?? throw new ArgumentNullException(nameof(entityMap));
        _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));

        // Subscribe to registration events so the retry loop only runs when relevant
        // entities become available, rather than scanning all pending IDs every tick.
        _entityMap.EntityRegistered += OnEntityRegistered;
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
    {
        // Retry deferred samples — only for net IDs registered since the last poll.
        if (_recentlyRegistered.Count > 0 && _pendingRoutes.Count > 0)
        {
            foreach (var netId in _recentlyRegistered)
            {
                if (_pendingRoutes.TryGetValue(netId, out var sample)
                 && _entityMap.TryGetEntity(netId, out var deferredEntity))
                {
                    ApplyRouteToEntity(deferredEntity, in sample, cmd, view);
                    _pendingRoutes.Remove(netId);
                }
            }
        }
        _recentlyRegistered.Clear();

        if (_reader is null) return;

        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            if (sample.Info.InstanceState != DdsInstanceState.Alive) continue;
            var sampleData = sample.Data;
            ProcessSample(in sampleData, cmd, view);
        }
    }

    public void ScanAndPublish(ISimulationView view) { }

    public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
    {
        if (data is not MapRoute mapRoute) return;

        RoutePlan routePlan;
        if (repo.HasManagedComponent<RoutePlan>(entity))
            routePlan = ((ISimulationView)repo).GetManagedComponentRO<RoutePlan>(entity);
        else
        {
            routePlan = new RoutePlan();
            repo.SetManagedComponent(entity, routePlan);
        }

        routePlan.IsLoop = mapRoute.IsLoop;
        ApplyWaypoints(routePlan, mapRoute);
    }

    public void Dispose(long networkEntityId)
    {
        _pendingRoutes.Remove(networkEntityId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    internal void ProcessSample(in MapRoute data, IEntityCommandBuffer cmd, ISimulationView view)
    {
        long netId = data.EntityId;
        if (!_entityMap.TryGetEntity(netId, out var entity))
        {
            // Entity not yet spawned — defer until it is registered.
            _pendingRoutes[netId] = data;
            FdpLog<MapRouteIngressTranslator>.Debug(
                "[ROUTE-INGRESS] Deferred MapRoute for unknown NetID={0}", netId);
            return;
        }

        ApplyRouteToEntity(entity, in data, cmd, view);
    }

    private void ApplyRouteToEntity(
        Entity entity, in MapRoute data, IEntityCommandBuffer cmd, ISimulationView view)
    {
        RoutePlan routePlan;
        if (view.HasManagedComponent<RoutePlan>(entity))
        {
            routePlan = view.GetManagedComponentRO<RoutePlan>(entity);
        }
        else
        {
            routePlan = new RoutePlan();
            cmd.SetManagedComponent(entity, routePlan);
        }

        routePlan.IsLoop = data.IsLoop;
        ApplyWaypoints(routePlan, data);
    }

    private void ApplyWaypoints(RoutePlan routePlan, MapRoute data)
    {
        routePlan.Mutate(wps =>
        {
            wps.Clear();
            if (data.Points != null)
            {
                foreach (var wp in data.Points)
                {
                    var cartesian = _geoTransform.ToCartesian(
                        wp.Position.Latitude,
                        wp.Position.Longitude,
                        wp.Position.Altitude);

                    wps.Add(new RouteWaypoint
                    {
                        Position      = cartesian,
                        TargetSpeed   = (float)wp.SpeedMetersPerSec,
                        ExtensionJson = string.IsNullOrEmpty(wp.ExtensionJson) ? null : wp.ExtensionJson,
                    });
                }
            }
        });
    }

    /// <summary>
    /// Called by <see cref="NetworkEntityMap.EntityRegistered"/> immediately after a new
    /// entity is registered. Records the net ID so that <see cref="PollIngress"/> can
    /// check only the newly-arrived entries rather than scanning all pending routes.
    /// </summary>
    private void OnEntityRegistered(long netId, Entity _)
    {
        if (_pendingRoutes.ContainsKey(netId))
            _recentlyRegistered.Add(netId);
    }
}

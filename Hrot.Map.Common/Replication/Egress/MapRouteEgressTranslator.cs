using System;
using System.Collections.Generic;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress;

/// <summary>
/// Egress translator that publishes <see cref="MapRoute"/> DDS samples from
/// the <see cref="RoutePlan"/> managed component.
///
/// <para>
/// Runs as part of the CycloneNetworkModule egress pass. On each tick it
/// queries all route entities and skips any whose <see cref="RoutePlan.Version"/>
/// has not changed since the last publish, ensuring minimal DDS traffic.
/// </para>
/// </summary>
public class MapRouteEgressTranslator : IDescriptorTranslator
{
    private const string DdsTopicName = "MapRoute";
    private const long OrdinalValue = (long)EDescriptorType.dtMapRoute;

    private readonly IDdsWriter<MapRoute> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly IGeographicTransform _geoTransform;

    /// <summary>
    /// Tracks the last-published <see cref="RoutePlan.Version"/> per entity so
    /// we can skip entities whose route has not changed since the previous tick.
    /// </summary>
    private readonly Dictionary<Entity, int> _publishedVersions = new();

    public string TopicName => DdsTopicName;
    public long DescriptorOrdinal => OrdinalValue;

    // Targets: RoutePlan (168 = HrotComponentIds.RoutePlan)
    private static readonly IReadOnlyList<int> _targetIds = new[] { 168 };
    public IReadOnlyList<int> TargetComponentIds => _targetIds;

    /// <summary>Production constructor: creates a live DDS writer.</summary>
    public MapRouteEgressTranslator(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        IGeographicTransform geoTransform)
        : this(new DdsWriterAdapter<MapRoute>(participant, DdsTopicName), entityMap, geoTransform)
    {
    }

    /// <summary>Testable constructor: accepts an injected writer stub.</summary>
    internal MapRouteEgressTranslator(
        IDdsWriter<MapRoute> writer,
        NetworkEntityMap entityMap,
        IGeographicTransform geoTransform)
    {
        _writer       = writer       ?? throw new ArgumentNullException(nameof(writer));
        _entityMap    = entityMap    ?? throw new ArgumentNullException(nameof(entityMap));
        _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<NetworkIdentity>()
            .With<SimTransform>()
            .WithManaged<RoutePlan>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity, DescriptorOrdinal))
                continue;

            var routePlan = view.GetManagedComponentRO<RoutePlan>(entity);

            // Skip if this version was already published.
            if (_publishedVersions.TryGetValue(entity, out int lastVersion) &&
                lastVersion == routePlan.Version)
                continue;

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            var waypoints = new List<Waypoint>(routePlan.Waypoints.Count);
            for (int i = 0; i < routePlan.Waypoints.Count; i++)
            {
                var wp = routePlan.Waypoints[i];
                var (lat, lon, alt) = _geoTransform.ToGeodetic(wp.Position);
                waypoints.Add(new Waypoint
                {
                    Position          = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt },
                    Name              = string.Empty,
                    SpeedMetersPerSec = wp.TargetSpeed,
                    ExtensionJson     = wp.ExtensionJson ?? string.Empty,
                });
            }

            _writer.Write(new MapRoute
            {
                EntityId      = (int)netId.Value,
                Points        = waypoints,
                IsLoop        = routePlan.IsLoop,
                ExtensionJson = string.Empty,
            });

            _publishedVersions[entity] = routePlan.Version;

            FdpLog<MapRouteEgressTranslator>.Debug(
                "[TRACE] Egress: MapRoute NetID={0} waypoints={1} version={2}",
                netId.Value, routePlan.Waypoints.Count, routePlan.Version);
        }
    }

    public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

    public void Dispose(long networkEntityId)
    {
        if (_entityMap.TryGetEntity(networkEntityId, out var entity))
            _publishedVersions.Remove(entity);

        _writer.DisposeInstance(new MapRoute { EntityId = (int)networkEntityId });
    }
}

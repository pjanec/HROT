using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.IG.Components;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using ModuleHost.Core.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// Egress translator that converts <see cref="SpawnEntityCommand"/> events consumed from
    /// <see cref="FdpEventBus"/> into <see cref="CreateEntityRequest"/> DDS samples.
    ///
    /// <para>
    /// All commands follow the standard path: command fields (including
    /// <see cref="SpawnEntityCommand.InitialComponents"/>) are serialised into a new
    /// <see cref="CreateEntityRequest"/> containing <c>dtEntityMaster</c>,
    /// <c>dtWorldPos</c>, and — when geometry components are present —
    /// <c>dtMapVisualOverlay</c> or <c>dtMapRoute</c> descriptors.
    /// </para>
    /// </summary>
    public class SpawnEntityCommandEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "CreateEntityRequest";

        // Synthetic ordinal — this translator is event-driven (PollIngress only); ScanAndPublish is empty.
        private const long OrdinalValue = -1001L;

        private readonly IDdsWriter<CreateEntityRequest> _writer;
        private readonly FdpEventBus _eventBus;
        private readonly IGeographicTransform? _geoTransform;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        /// <summary>Production constructor: creates a live DDS writer.</summary>
        public SpawnEntityCommandEgressTranslator(
            DdsParticipant participant,
            FdpEventBus eventBus,
            IGeographicTransform? geoTransform)
            : this(new DdsWriterAdapter<CreateEntityRequest>(participant, DdsTopicName), eventBus, geoTransform)
        {
        }

        /// <summary>Testable constructor: accepts an injected writer stub.</summary>
        internal SpawnEntityCommandEgressTranslator(
            IDdsWriter<CreateEntityRequest> writer,
            FdpEventBus eventBus,
            IGeographicTransform? geoTransform)
        {
            _writer       = writer    ?? throw new ArgumentNullException(nameof(writer));
            _eventBus     = eventBus  ?? throw new ArgumentNullException(nameof(eventBus));
            _geoTransform = geoTransform;
        }

        /// <summary>
        /// Consumes pending <see cref="SpawnEntityCommand"/> events from the event bus and
        /// writes each as a <see cref="CreateEntityRequest"/> to DDS.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            foreach (var spawnCmd in _eventBus.ConsumeManaged<SpawnEntityCommand>())
            {
                var request = BuildCreateEntityRequest(spawnCmd);
                _writer.Write(request);
                FdpLog<SpawnEntityCommandEgressTranslator>.Debug(
                    "[Egress] SpawnCmd → CreateEntityRequest req={0} tkbType={1}",
                    request.RequestId, spawnCmd.TkbType);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }

        // ── Private helpers ───────────────────────────────────────────────────

        private CreateEntityRequest BuildCreateEntityRequest(SpawnEntityCommand cmd)
        {
            double lat, lon, alt;

            if (_geoTransform != null && cmd.InitialTransform.HasValue)
            {
                (lat, lon, alt) = _geoTransform.ToGeodetic(cmd.InitialTransform.Value.Position);
            }
            else if (cmd.InitialTransform.HasValue)
            {
                // Offline / test mode: treat canvas XY as lat/lon directly.
                var pos = cmd.InitialTransform.Value.Position;
                lat = pos.Y;
                lon = pos.X;
                alt = 0.0;
            }
            else
            {
                lat = lon = alt = 0.0;
            }

            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d           = EDescriptorType.dtEntityMaster,
                    EntityMaster = new EntityMaster { TkbType = cmd.TkbType },
                },
                new EntityDescriptorUnion
                {
                    _d       = EDescriptorType.dtWorldPos,
                    WorldPos = new WorldPos
                    {
                        Pos = new GeoPoint
                        {
                            Latitude  = lat,
                            Longitude = lon,
                            Altitude  = alt,
                        },
                    },
                },
            };

            // Extract geometry descriptors from InitialComponents.
            if (cmd.InitialComponents != null)
            {
                EditablePolyline? polyline = null;
                MapOverlayStyle?  style    = null;
                RoutePlan?        route    = null;

                foreach (var component in cmd.InitialComponents)
                {
                    if      (component is EditablePolyline ep) polyline = ep;
                    else if (component is MapOverlayStyle  ms) style    = ms;
                    else if (component is RoutePlan        rp) route    = rp;
                }

                if (polyline != null)
                    descriptors.Add(BuildOverlayDescriptor(polyline, style, cmd.InitialTransform?.Position));

                if (route != null)
                    descriptors.Add(BuildRouteDescriptor(route));
            }

            return new CreateEntityRequest
            {
                RequestId             = cmd.RequestId == Guid.Empty ? Guid.NewGuid() : cmd.RequestId,
                Owner                 = default,
                Flags                 = 0,
                InitialAttributesJson = cmd.InitialAttributesJson,
                InitialDescriptors    = descriptors,
            };
        }

        /// <summary>
        /// Builds a <c>dtMapVisualOverlay</c> descriptor from an <see cref="EditablePolyline"/>
        /// and optional <see cref="MapOverlayStyle"/>. Entity-relative Cartesian XY points are
        /// converted to relative geodetic offsets from the entity anchor.
        /// </summary>
        private EntityDescriptorUnion BuildOverlayDescriptor(
            EditablePolyline polyline, MapOverlayStyle? style, Vector3? anchor)
        {
            var geoPoints = new List<GeoPoint>(polyline.Points?.Count ?? 0);

            if (polyline.Points != null)
            {
                foreach (var relPt in polyline.Points)
                {
                    GeoPoint deltaGeo;
                    if (_geoTransform != null && anchor.HasValue)
                    {
                        // Convert entity-relative Cartesian XY to relative geodetic offset.
                        var absCart   = new Vector3(anchor.Value.X + relPt.X, anchor.Value.Y + relPt.Y, anchor.Value.Z);
                        var (absLat, absLon, absAlt) = _geoTransform.ToGeodetic(absCart);
                        var (refLat, refLon, refAlt) = _geoTransform.ToGeodetic(anchor.Value);
                        deltaGeo = new GeoPoint
                        {
                            Latitude  = absLat - refLat,
                            Longitude = absLon - refLon,
                            Altitude  = absAlt - refAlt,
                        };
                    }
                    else
                    {
                        // No geo-transform or no anchor: treat XY as lat/lon offsets directly.
                        deltaGeo = new GeoPoint { Latitude = relPt.Y, Longitude = relPt.X, Altitude = 0.0 };
                    }
                    geoPoints.Add(deltaGeo);
                }
            }

            string styleJson = style.HasValue ? style.Value.ToJson() : string.Empty;

            return new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtMapVisualOverlay,
                MapVisualOverlay = new MapVisualOverlay
                {
                    PersistenceMode   = PersistenceMode.MODE_PERSISTENT,
                    Points            = geoPoints,
                    IsEditable        = true,
                    IsClickable       = true,
                    StyleOverrideJson = styleJson,
                },
            };
        }

        /// <summary>
        /// Builds a <c>dtMapRoute</c> descriptor from a <see cref="RoutePlan"/>.
        /// Cartesian waypoint positions are converted to geodetic coordinates.
        /// </summary>
        private EntityDescriptorUnion BuildRouteDescriptor(RoutePlan route)
        {
            var waypoints = new List<Waypoint>(route.Waypoints.Count);

            foreach (var wp in route.Waypoints)
            {
                GeoPoint pos;
                if (_geoTransform != null)
                {
                    var (wLat, wLon, wAlt) = _geoTransform.ToGeodetic(wp.Position);
                    pos = new GeoPoint { Latitude = wLat, Longitude = wLon, Altitude = wAlt };
                }
                else
                {
                    // No geo-transform: treat Cartesian XYZ as lon/lat/alt.
                    pos = new GeoPoint { Latitude = wp.Position.Y, Longitude = wp.Position.X, Altitude = wp.Position.Z };
                }
                waypoints.Add(new Waypoint { Position = pos, SpeedMetersPerSec = wp.TargetSpeed });
            }

            return new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtMapRoute,
                MapRoute = new MapRoute
                {
                    Points = waypoints,
                    IsLoop = route.IsLoop,
                },
            };
        }
    }
}

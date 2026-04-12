using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Fdp.Interfaces;
using FDP.Toolkit.NetworkSpawning.Systems;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Modules
{
    // ─── Module ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hosts a <see cref="FDP.Toolkit.NetworkSpawning.Systems.NetworkSpawningSystem"/>.
    /// Exposes optional egress/ingress translators for DDS topic publication and ingestion.
    /// Entity lifecycle (create/delete request handling) is a brain (CGF) responsibility
    /// and must NOT be wired here.
    /// </summary>
    public class SimHostModule : IEcsModule
    {
        public string         Name   => "SimHost";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkSpawningSystem             _spawnSystem;
        private readonly GeoSpatialEgressTranslator?       _geoEgressTranslator;
        private readonly MapVisualOverlayEgressTranslator? _mapOverlayEgressTranslator;
        private readonly MapRouteEgressTranslator?         _mapRouteEgressTranslator;
        private readonly EntityMissionIngressTranslator?   _missionIngressTranslator;
        private readonly EntityMissionEgressTranslator?    _missionEgressTranslator;

        public SimHostModule(
            NetworkSpawningSystem             spawnSystem,
            GeoSpatialEgressTranslator?       geoEgressTranslator        = null,
            MapVisualOverlayEgressTranslator? mapOverlayEgressTranslator = null,
            MapRouteEgressTranslator?         mapRouteEgressTranslator   = null,
            EntityMissionIngressTranslator?   missionIngressTranslator   = null,
            EntityMissionEgressTranslator?    missionEgressTranslator    = null)
        {
            _spawnSystem                = spawnSystem;
            _geoEgressTranslator        = geoEgressTranslator;
            _mapOverlayEgressTranslator = mapOverlayEgressTranslator;
            _mapRouteEgressTranslator   = mapRouteEgressTranslator;
            _missionIngressTranslator   = missionIngressTranslator;
            _missionEgressTranslator    = missionEgressTranslator;
        }

        /// <summary>
        /// Gets the GeoSpatial egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public GeoSpatialEgressTranslator? GeoEgressTranslator => _geoEgressTranslator;

        /// <summary>
        /// Gets the MapVisualOverlay egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public MapVisualOverlayEgressTranslator? MapOverlayEgressTranslator => _mapOverlayEgressTranslator;

        /// <summary>
        /// Gets the MapRoute egress translator for registration with the network module.
        /// Returns null if no geographic transform was provided.
        /// </summary>
        public MapRouteEgressTranslator? MapRouteEgressTranslator => _mapRouteEgressTranslator;

        /// <summary>
        /// Gets the EntityMission ingress translator (DDS → ECS).
        /// Null when not provided at construction.
        /// </summary>
        public EntityMissionIngressTranslator? MissionIngressTranslator => _missionIngressTranslator;

        /// <summary>
        /// Gets the EntityMission egress translator (ECS → DDS).
        /// Null when not provided at construction.
        /// </summary>
        public EntityMissionEgressTranslator? MissionEgressTranslator => _missionEgressTranslator;

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(_spawnSystem);
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
